using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Minecraft karakterinin modeli: kutular ve yüzleri. Her yüz skinin bir dikdörtgenidir; ekrana dik (ortografik)
    /// izdüşümde dikdörtgen bir paralelkenara dönüşür, bu da tek bir afin dönüşümdür. Böylece 3B görünüm WebGL ya da
    /// GPU kabuğu olmadan, sıradan 2B çizimle yapılır: her yüz kendi dönüşümüyle skinden kesilip çizilir, uzaktakiler önce.
    /// </summary>
    internal static class SkinModel
    {
        internal readonly record struct V3(double X, double Y, double Z)
        {
            public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static V3 operator *(V3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
        }

        /// <summary>Bir yüz: dokudaki dikdörtgen (u,v,w,h), 3B'de sol üst, sağ üst, sol alt köşe ve dışa bakan yön.</summary>
        internal readonly record struct Face(int Texture, Rect Uv, V3 TopLeft, V3 TopRight, V3 BottomLeft, V3 Normal, int Part);

        /// <summary>Bir uzvun dönme noktası ve öne/arkaya açısı (kollar ve bacaklar sallanır, pelerin hafif açılır).</summary>
        internal readonly record struct Pivot(V3 At, double Pitch);

        public const int SkinTexture = 0, CapeTexture = 1;
        public const int Head = 0, Body = 1, RightArm = 2, LeftArm = 3, RightLeg = 4, LeftLeg = 5, Cape = 6;

        /// <summary>
        /// Kutunun altı yüzü. Kutu [x0,x1]×[y0,y1]×[z0,z1]; karakterin önü +z, sağı −x. (u,v): dokudaki kutu açılımının
        /// sol üstü; w,h,d genişlik, yükseklik, derinlik (doku pikseli). inflate: ikinci katman için büyütme.
        /// </summary>
        public static IEnumerable<Face> Box(int texture, int part, double u, double v, double w, double h, double d,
            double x0, double y0, double z0, double inflate = 0, bool capeLayout = false)
        {
            double x1 = x0 + w + inflate, y1 = y0 + h + inflate, z1 = z0 + d + inflate;
            x0 -= inflate; y0 -= inflate; z0 -= inflate;
            Rect top = new(u + d, v, w, d), bottom = new(u + d + w, v, w, d);
            Rect right = new(u, v + d, d, h), front = new(u + d, v + d, w, h), left = new(u + d + w, v + d, d, h), back = new(u + d * 2 + w, v + d, w, h);
            if (capeLayout) (front, back) = (back, front);   // pelerinin dış yüzü (1,1) karakterin arkasına bakar
            yield return new(texture, front, new(x0, y1, z1), new(x1, y1, z1), new(x0, y0, z1), new(0, 0, 1), part);
            yield return new(texture, back, new(x1, y1, z0), new(x0, y1, z0), new(x1, y0, z0), new(0, 0, -1), part);
            yield return new(texture, right, new(x0, y1, z0), new(x0, y1, z1), new(x0, y0, z0), new(-1, 0, 0), part);
            yield return new(texture, left, new(x1, y1, z1), new(x1, y1, z0), new(x1, y0, z1), new(1, 0, 0), part);
            yield return new(texture, top, new(x0, y1, z0), new(x1, y1, z0), new(x0, y1, z1), new(0, 1, 0), part);
            yield return new(texture, bottom, new(x0, y0, z1), new(x1, y0, z1), new(x0, y0, z0), new(0, -1, 0), part);
        }

        /// <summary>Bütün model: ayaklar y=0'da, baş y=32'ye kadar. legacy: 64×32 eski skin (sol uzuvlar sağın aynası).</summary>
        public static List<Face> Build(bool slim, bool legacy, bool cape)
        {
            double arm = slim ? 3 : 4;
            var faces = new List<Face>();
            void Add(IEnumerable<Face> f) => faces.AddRange(f);
            // Temel katman
            Add(Box(SkinTexture, Head, 0, 0, 8, 8, 8, -4, 24, -4));
            Add(Box(SkinTexture, Body, 16, 16, 8, 12, 4, -4, 12, -2));
            Add(Box(SkinTexture, RightArm, 40, 16, arm, 12, 4, -4 - arm, 12, -2));
            Add(Box(SkinTexture, LeftArm, legacy ? 40 : 32, legacy ? 16 : 48, arm, 12, 4, 4, 12, -2));
            Add(Box(SkinTexture, RightLeg, 0, 16, 4, 12, 4, -4, 0, -2));
            Add(Box(SkinTexture, LeftLeg, legacy ? 0 : 16, legacy ? 16 : 48, 4, 12, 4, 0, 0, -2));
            // İkinci katman (şapka, ceket, kollar, paçalar); eski skinlerde yalnızca şapka
            Add(Box(SkinTexture, Head, 32, 0, 8, 8, 8, -4, 24, -4, 0.5));
            if (!legacy)
            {
                Add(Box(SkinTexture, Body, 16, 32, 8, 12, 4, -4, 12, -2, 0.25));
                Add(Box(SkinTexture, RightArm, 40, 32, arm, 12, 4, -4 - arm, 12, -2, 0.25));
                Add(Box(SkinTexture, LeftArm, 48, 48, arm, 12, 4, 4, 12, -2, 0.25));
                Add(Box(SkinTexture, RightLeg, 0, 32, 4, 12, 4, -4, 0, -2, 0.25));
                Add(Box(SkinTexture, LeftLeg, 0, 48, 4, 12, 4, 0, 0, -2, 0.25));
            }
            if (cape) Add(Box(CapeTexture, Cape, 0, 0, 10, 16, 1, -5, 8, -3.2, 0, capeLayout: true));
            return faces;
        }

        /// <summary>Uzuvların dönme noktaları (omuz, kalça, pelerinin üst kenarı).</summary>
        public static Pivot[] Pivots(double swing, double capeLift) =>
        [
            new(new(0, 24, 0), 0), new(new(0, 24, 0), 0),
            new(new(-5, 22, 0), swing), new(new(5, 22, 0), -swing),
            new(new(-2, 12, 0), -swing), new(new(2, 12, 0), swing),
            new(new(0, 24, -2.2), capeLift),
        ];

        /// <summary>x ekseni çevresinde döndürme (öne/arkaya).</summary>
        public static V3 Pitch(V3 p, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return new(p.X, p.Y * c - p.Z * s, p.Y * s + p.Z * c);
        }

        /// <summary>Görüş dönüşümü: önce y çevresinde (karakter döner), sonra x çevresinde (yukarıdan bakış).</summary>
        public static V3 View(V3 p, double yaw, double pitch)
        {
            double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
            var r = new V3(p.X * cy + p.Z * sy, p.Y, -p.X * sy + p.Z * cy);
            return Pitch(r, pitch);
        }
    }

    /// <summary>
    /// Profildeki dönen 3B karakter. Kendiliğinden yavaşça döner, fareyle sürüklenince elle çevrilir. Pelerin varsa
    /// sırtında görünür. Görünmüyorken (sekme kapalı, pencere gizli) hiç çizim yapmaz.
    /// </summary>
    internal sealed class SkinViewer3D : Control
    {
        public static readonly StyledProperty<byte[]?> SkinProperty = AvaloniaProperty.Register<SkinViewer3D, byte[]?>(nameof(Skin));
        public static readonly StyledProperty<byte[]?> CapeProperty = AvaloniaProperty.Register<SkinViewer3D, byte[]?>(nameof(Cape));
        public static readonly StyledProperty<bool> SlimProperty = AvaloniaProperty.Register<SkinViewer3D, bool>(nameof(Slim));
        public static readonly StyledProperty<bool> AnimateProperty = AvaloniaProperty.Register<SkinViewer3D, bool>(nameof(Animate), true);

        /// <summary>Skin PNG baytları (64×64 ya da 64×32). Boşsa varsayılan karakter çizilir.</summary>
        public byte[]? Skin { get => GetValue(SkinProperty); set => SetValue(SkinProperty, value); }
        /// <summary>Pelerin PNG baytları (64×32); boşsa pelerinsiz.</summary>
        public byte[]? Cape { get => GetValue(CapeProperty); set => SetValue(CapeProperty, value); }
        public bool Slim { get => GetValue(SlimProperty); set => SetValue(SlimProperty, value); }
        public bool Animate { get => GetValue(AnimateProperty); set => SetValue(AnimateProperty, value); }

        static SkinViewer3D()
        {
            AffectsRender<SkinViewer3D>(SkinProperty, CapeProperty, SlimProperty);
            ClipToBoundsProperty.OverrideDefaultValue<SkinViewer3D>(true);
            FocusableProperty.OverrideDefaultValue<SkinViewer3D>(false);
        }

        private Bitmap? _skin, _cape;
        private List<SkinModel.Face> _faces = new();
        private double _yaw = -0.5, _pitch = DefaultPitch, _time;
        private const double DefaultPitch = -0.22;   // hafif yukarıdan
        private bool _dragging, _attached;
        private Point _dragFrom;
        private DateTime _lastDrag = DateTime.MinValue, _lastFrame = DateTime.UtcNow;

        public SkinViewer3D()
        {
            Cursor = new Cursor(StandardCursorType.SizeAll);
            Rebuild();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == SkinProperty || change.Property == CapeProperty || change.Property == SlimProperty) Rebuild();
            if (change.Property == AnimateProperty && Animate) RequestFrame();
        }

        private void Rebuild()
        {
            if (_skin != DefaultSkin.Value) _skin?.Dispose();   // varsayılan karakter paylaşılır, atılmaz
            _cape?.Dispose();
            _skin = Decode(Skin) ?? DefaultSkin.Value;
            _cape = Decode(Cape);
            bool legacy = _skin != null && _skin.PixelSize.Height == 32;
            _faces = SkinModel.Build(Slim, legacy, _cape != null);
            InvalidateVisual();
        }

        private static Bitmap? Decode(byte[]? png)
        {
            if (png == null || png.Length == 0) return null;
            try { using var s = new MemoryStream(png); return new Bitmap(s); } catch { return null; }
        }

        /// <summary>Skin yoksa: düz renkli basit bir karakter (Steve'in renklerine yakın), yüklü bir dosya gerektirmez.</summary>
        private static readonly Lazy<Bitmap?> DefaultSkin = new(() =>
        {
            try
            {
                using var bmp = new SkiaSharp.SKBitmap(64, 64);
                using var canvas = new SkiaSharp.SKCanvas(bmp);
                canvas.Clear(SkiaSharp.SKColors.Transparent);
                void Fill(int x, int y, int w, int h, uint argb)
                {
                    using var paint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(argb), IsAntialias = false };
                    canvas.DrawRect(x, y, w, h, paint);
                }
                Fill(0, 0, 32, 16, 0xFFB4846C);   // baş (ten)
                Fill(8, 0, 16, 8, 0xFF2A1D10);    // saç
                Fill(0, 8, 32, 2, 0xFF2A1D10);
                Fill(9, 12, 2, 1, 0xFFFFFFFF); Fill(13, 12, 2, 1, 0xFFFFFFFF);   // gözler
                Fill(10, 12, 1, 1, 0xFF4A3A8F); Fill(13, 12, 1, 1, 0xFF4A3A8F);
                Fill(16, 16, 24, 16, 0xFF2FA3A8); // gövde (tişört)
                Fill(40, 16, 16, 16, 0xFFB4846C); // kollar
                Fill(40, 16, 16, 8, 0xFF2FA3A8);
                Fill(0, 16, 16, 16, 0xFF3B3A8F);  // bacaklar
                Fill(32, 48, 16, 16, 0xFFB4846C); Fill(32, 48, 16, 8, 0xFF2FA3A8);
                Fill(16, 48, 16, 16, 0xFF3B3A8F);
                using var image = SkiaSharp.SKImage.FromBitmap(bmp);
                using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                return new Bitmap(data.AsStream());
            }
            catch { return null; }
        });

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            _lastFrame = DateTime.UtcNow;
            RequestFrame();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _attached = false;
            base.OnDetachedFromVisualTree(e);
        }

        private bool _frameRequested;
        private void RequestFrame()
        {
            if (_frameRequested || !_attached || !Animate) return;
            if (TopLevel.GetTopLevel(this) is not { } top) return;
            _frameRequested = true;
            top.RequestAnimationFrame(_ =>
            {
                _frameRequested = false;
                if (!_attached || !Animate) return;
                if (!IsEffectivelyVisible) { DispatcherTimer.RunOnce(RequestFrame, TimeSpan.FromMilliseconds(500)); return; }
                var now = DateTime.UtcNow;
                double dt = Math.Min(0.1, (now - _lastFrame).TotalSeconds);
                _lastFrame = now;
                _time += dt;
                // Sürüklemeden 2 sn sonra kendiliğinden dönmeye devam eder
                if (!_dragging && (now - _lastDrag).TotalSeconds > 2) _yaw += dt * 0.55;
                InvalidateVisual();
                RequestFrame();
            });
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            // Çift tık: dik duruşa döner
            if (e.ClickCount == 2) { _pitch = DefaultPitch; InvalidateVisual(); }
            _dragging = true;
            _dragFrom = e.GetPosition(this);
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_dragging) return;
            var p = e.GetPosition(this);
            // Yatay sürükleme y ekseninde, dikey sürükleme x ekseninde çevirir; ikisi de sınırsız (360°)
            _yaw += (p.X - _dragFrom.X) * 0.015;
            _pitch -= (p.Y - _dragFrom.Y) * 0.015;
            _dragFrom = p;
            _lastDrag = DateTime.UtcNow;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _dragging = false;
            _lastDrag = DateTime.UtcNow;
            e.Pointer.Capture(null);
        }

        public override void Render(DrawingContext context)
        {
            if (_skin == null) return;
            var size = Bounds.Size;
            // Model 32 birim yüksek, ~16 birim geniş; pelerin ve dönüş için pay bırakılır
            double scale = Math.Min(size.Height / 38, size.Width / 20);
            if (scale <= 0) return;
            double cx = size.Width / 2, cy = size.Height / 2 + 16 * scale;   // y=16 (gövde ortası) ekranın ortasında
            double viewPitch = _pitch;
            double swing = Animate ? Math.Sin(_time * 2.2) * 0.14 : 0;
            double capeLift = 0.2 + (Animate ? Math.Sin(_time * 1.6) * 0.04 : 0);
            var pivots = SkinModel.Pivots(swing, capeLift);

            Point Project(SkinModel.V3 p, int part)
            {
                var pivot = pivots[part];
                if (pivot.Pitch != 0) p = SkinModel.Pitch(p - pivot.At, pivot.Pitch) + pivot.At;
                var v = SkinModel.View(p - new SkinModel.V3(0, 16, 0), _yaw, viewPitch);
                return new Point(cx + v.X * scale, cy - (v.Y + 16) * scale);
            }

            var visible = new List<(double Depth, SkinModel.Face Face, Point Tl, Point Tr, Point Bl)>(_faces.Count);
            foreach (var f in _faces)
            {
                var pivot = pivots[f.Part];
                var n = pivot.Pitch != 0 ? SkinModel.Pitch(f.Normal, pivot.Pitch) : f.Normal;
                if (SkinModel.View(n, _yaw, viewPitch).Z <= 0.001) continue;   // arkası dönük yüz
                var tl = Project(f.TopLeft, f.Part);
                var tr = Project(f.TopRight, f.Part);
                var bl = Project(f.BottomLeft, f.Part);
                var centre = f.TopRight + (f.BottomLeft - f.TopRight) * 0.5;
                if (pivot.Pitch != 0) centre = SkinModel.Pitch(centre - pivot.At, pivot.Pitch) + pivot.At;
                double depth = SkinModel.View(centre - new SkinModel.V3(0, 16, 0), _yaw, viewPitch).Z;
                visible.Add((depth, f, tl, tr, bl));
            }
            // Pelerin uzun ve ince bir kutu: yüz merkezine göre sıralanınca bacakların arasına yanlış düşer. Arkadan
            // bakılıyorsa pelerin her şeyin üstüne (en son), önden bakılıyorsa her şeyin altına (en önce) çizilir.
            var capeBack = SkinModel.Pitch(new SkinModel.V3(0, 0, -1), pivots[SkinModel.Cape].Pitch);
            double fromBehind = SkinModel.View(capeBack, _yaw, viewPitch).Z;
            double Order((double Depth, SkinModel.Face Face, Point, Point, Point) f) =>
                f.Face.Part != SkinModel.Cape || Math.Abs(fromBehind) < 0.3 ? f.Depth : fromBehind > 0 ? 1e6 + f.Depth : -1e6 + f.Depth;
            visible.Sort((a, b) => Order(a).CompareTo(Order(b)));

            using var _ = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None, EdgeMode = EdgeMode.Aliased });
            foreach (var (_, f, tl, tr, bl) in visible)
            {
                var image = f.Texture == SkinModel.CapeTexture ? _cape : _skin;
                if (image == null) continue;
                double w = f.Uv.Width, h = f.Uv.Height;
                var m = new Matrix((tr.X - tl.X) / w, (tr.Y - tl.Y) / w, (bl.X - tl.X) / h, (bl.Y - tl.Y) / h, tl.X, tl.Y);
                using (context.PushTransform(m))
                {
                    // Yüzler arasında saç teli kadar boşluk kalmasın diye hedef çok az büyütülür
                    const double e = 0.04;
                    // HD doku (64×32'nin katı): kesilecek bölge dokunun gerçek boyutuna göre büyütülür
                    double k = image.PixelSize.Width / 64.0;
                    var src = k == 1 ? f.Uv : new Rect(f.Uv.X * k, f.Uv.Y * k, f.Uv.Width * k, f.Uv.Height * k);
                    context.DrawImage(image, src, new Rect(-e, -e, w + e * 2, h + e * 2));
                }
            }
        }
    }
}
