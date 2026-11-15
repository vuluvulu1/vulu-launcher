using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace VuluLauncher.Views
{
    /// <summary>Haritadaki oyuncu işareti (kendin ya da bir arkadaşın).</summary>
    internal sealed record MapMarker(string Name, double X, double Z, bool You);

    /// <summary>
    /// vulu-resmi'nin üstten haritası: 512 px karolar (bir bölge = 512×512 blok); uzaklaştıkça daha kaba karolar
    /// (2, 4, 8 bölge bir karoda) kullanılır. Sürükleyerek kaydırılır, tekerlekle imlecin olduğu yere yakınlaşır.
    /// Karolar istendikçe yüklenir ve bellekte tutulur.
    /// </summary>
    internal sealed class WorldMapControl : Control
    {
        public static readonly StyledProperty<IReadOnlyList<MapMarker>?> MarkersProperty =
            AvaloniaProperty.Register<WorldMapControl, IReadOnlyList<MapMarker>?>(nameof(Markers));
        public static readonly StyledProperty<IBrush?> BackgroundProperty =
            AvaloniaProperty.Register<WorldMapControl, IBrush?>(nameof(Background));
        public static readonly StyledProperty<IBrush?> AccentProperty =
            AvaloniaProperty.Register<WorldMapControl, IBrush?>(nameof(Accent));

        public IReadOnlyList<MapMarker>? Markers { get => GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
        public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
        /// <summary>Kendi işaretinin rengi (temanın vurgu rengi).</summary>
        public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

        /// <summary>Karo yükleyici: (seviye, x, z) → resim (yoksa null). Görünüm modeli verir.</summary>
        public Func<int, int, int, Task<Bitmap?>>? TileSource { get; set; }

        /// <summary>İmlecin altındaki blok koordinatı değişti ("x, z").</summary>
        public event Action<string>? HoverChanged;

        static WorldMapControl()
        {
            AffectsRender<WorldMapControl>(MarkersProperty, BackgroundProperty, AccentProperty);
            ClipToBoundsProperty.OverrideDefaultValue<WorldMapControl>(true);
        }

        private readonly Dictionary<(int, int, int), Bitmap?> _tiles = new();
        private readonly HashSet<(int, int, int)> _loading = new();
        private double _cx, _cz;        // ekranın ortasındaki blok
        private double _scale = 0.5;    // piksel / blok
        private bool _dragging;
        private Point _last;

        public WorldMapControl()
        {
            Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        /// <summary>Önbelleği atar (karolar sunucuda yenilendi).</summary>
        public void Refresh()
        {
            foreach (var b in _tiles.Values) b?.Dispose();
            _tiles.Clear();
            InvalidateVisual();
        }

        public void CenterOn(double x, double z, double? scale = null)
        {
            _cx = x; _cz = z;
            if (scale is { } s) _scale = Math.Clamp(s, 1.0 / 16, 8);
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            _dragging = true;
            _last = e.GetPosition(this);
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var p = e.GetPosition(this);
            if (_dragging)
            {
                _cx -= (p.X - _last.X) / _scale;
                _cz -= (p.Y - _last.Y) / _scale;
                _last = p;
                InvalidateVisual();
            }
            var (bx, bz) = ToWorld(p);
            HoverChanged?.Invoke($"x {Math.Floor(bx):0}, z {Math.Floor(bz):0}");
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _dragging = false;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            var p = e.GetPosition(this);
            var (bx, bz) = ToWorld(p);
            double next = Math.Clamp(_scale * Math.Pow(1.25, e.Delta.Y), 1.0 / 16, 8);
            // İmlecin altındaki blok yerinde kalsın
            _cx = bx - (p.X - Bounds.Width / 2) / next;
            _cz = bz - (p.Y - Bounds.Height / 2) / next;
            _scale = next;
            InvalidateVisual();
            e.Handled = true;
        }

        private (double X, double Z) ToWorld(Point p) => (_cx + (p.X - Bounds.Width / 2) / _scale, _cz + (p.Y - Bounds.Height / 2) / _scale);
        private Point ToScreen(double x, double z) => new(Bounds.Width / 2 + (x - _cx) * _scale, Bounds.Height / 2 + (z - _cz) * _scale);

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            if (Background != null) context.FillRectangle(Background, bounds);

            // Uzaklaştıkça kaba karolar: 1 piksel 2^L bloktan azını gösteriyorsa L. seviye yeter
            int level = Math.Clamp((int)Math.Floor(Math.Log2(1 / _scale)), 0, 3);
            double span = 512.0 * (1 << level);   // karonun kapladığı blok
            var (x0, z0) = ToWorld(new Point(0, 0));
            var (x1, z1) = ToWorld(new Point(Bounds.Width, Bounds.Height));
            int tx0 = (int)Math.Floor(x0 / span), tx1 = (int)Math.Floor(x1 / span);
            int tz0 = (int)Math.Floor(z0 / span), tz1 = (int)Math.Floor(z1 / span);
            if ((tx1 - tx0 + 1) * (tz1 - tz0 + 1) > 400) return;

            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = _scale >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.MediumQuality }))
            {
                for (int tz = tz0; tz <= tz1; tz++)
                    for (int tx = tx0; tx <= tx1; tx++)
                    {
                        var bitmap = Tile(level, tx, tz);
                        var tl = ToScreen(tx * span, tz * span);
                        var rect = new Rect(tl.X, tl.Y, span * _scale + 0.5, span * _scale + 0.5);
                        if (bitmap != null) context.DrawImage(bitmap, new Rect(bitmap.Size), rect);
                    }
            }

            // Bellek: bir karo ~1 MB; çok birikince görünmeyenler atılır
            if (_tiles.Count > 120)
                foreach (var k in _tiles.Keys.Where(k => k.Item1 != level || k.Item2 < tx0 || k.Item2 > tx1 || k.Item3 < tz0 || k.Item3 > tz1).ToList())
                {
                    _tiles[k]?.Dispose();
                    _tiles.Remove(k);
                }

            // Oyuncular: nokta + ad
            var accent = Accent ?? Brushes.Orange;
            var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
            foreach (var m in Markers ?? [])
            {
                var p = ToScreen(m.X, m.Z);
                if (!bounds.Inflate(40).Contains(p)) continue;
                IBrush fill = m.You ? accent : Brushes.White;
                context.DrawEllipse(fill, new Pen(Brushes.Black, 2), p, 6, 6);
                var text = new FormattedText(m.Name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12, Brushes.White);
                var box = new Rect(p.X + 10, p.Y - text.Height / 2 - 2, text.Width + 10, text.Height + 4);
                context.FillRectangle(new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)), box, 3);
                context.DrawText(text, new Point(box.X + 5, box.Y + 2));
            }
        }

        /// <summary>Karo hazırsa döner; değilse yüklemeyi başlatır ve (varsa) bir üst seviyenin karosunu gösterir.</summary>
        private Bitmap? Tile(int level, int x, int z)
        {
            var key = (level, x, z);
            if (_tiles.TryGetValue(key, out var bitmap)) return bitmap;
            if (TileSource != null && _loading.Add(key))
            {
                _ = Load();
                async Task Load()
                {
                    Bitmap? b = null;
                    try { b = await TileSource(level, x, z); } catch { }
                    _tiles[key] = b;
                    _loading.Remove(key);
                    InvalidateVisual();
                }
            }
            return null;
        }
    }
}
