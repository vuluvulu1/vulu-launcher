using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Minecraft karakterinin modeli: kutular ve yÃ¼zleri. Her yÃ¼z skinin bir dikdÃ¶rtgenidir; ekrana dik (ortografik)
    /// izdÃ¼ÅŸÃ¼mde dikdÃ¶rtgen bir paralelkenara dÃ¶nÃ¼ÅŸÃ¼r, bu da tek bir afin dÃ¶nÃ¼ÅŸÃ¼mdÃ¼r. BÃ¶ylece 3B gÃ¶rÃ¼nÃ¼m WebGL ya da
    /// GPU kabuÄŸu olmadan, sÄ±radan 2B Ã§izimle yapÄ±lÄ±r: her yÃ¼z kendi dÃ¶nÃ¼ÅŸÃ¼mÃ¼yle skinden kesilip Ã§izilir, uzaktakiler Ã¶nce.
    /// </summary>
    internal static class SkinModel
    {
        internal readonly record struct V3(double X, double Y, double Z)
        {
            public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static V3 operator *(V3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
        }

        /// <summary>Bir yÃ¼z: dokudaki dikdÃ¶rtgen (u,v,w,h), 3B'de sol Ã¼st, saÄŸ Ã¼st, sol alt kÃ¶ÅŸe ve dÄ±ÅŸa bakan yÃ¶n.</summary>
        internal readonly record struct Face(int Texture, Rect Uv, V3 TopLeft, V3 TopRight, V3 BottomLeft, V3 Normal, int Part);

        /// <summary>Bir uzvun dÃ¶nme noktasÄ± ve Ã¶ne/arkaya aÃ§Ä±sÄ± (kollar ve bacaklar sallanÄ±r, pelerin hafif aÃ§Ä±lÄ±r).</summary>
        internal readonly record struct Pivot(V3 At, double Pitch);

        public const int SkinTexture = 0, CapeTexture = 1;
        public const int Head = 0, Body = 1, RightArm = 2, LeftArm = 3, RightLeg = 4, LeftLeg = 5, Cape = 6;

        /// <summary>
        /// Kutunun altÄ± yÃ¼zÃ¼. Kutu [x0,x1]Ã—[y0,y1]Ã—[z0,z1]; karakterin Ã¶nÃ¼ +z, saÄŸÄ± âˆ’x. (u,v): dokudaki kutu aÃ§Ä±lÄ±mÄ±nÄ±n
        /// sol Ã¼stÃ¼; w,h,d geniÅŸlik, yÃ¼kseklik, derinlik (doku pikseli). inflate: ikinci katman iÃ§in bÃ¼yÃ¼tme.
        /// </summary>
        public static IEnumerable<Face> Box(int texture, int part, double u, double v, double w, double h, double d,
            double x0, double y0, double z0, double inflate = 0, bool capeLayout = false)
        {
            double x1 = x0 + w + inflate, y1 = y0 + h + inflate, z1 = z0 + d + inflate;
            x0 -= inflate; y0 -= inflate; z0 -= inflate;
            Rect top = new(u + d, v, w, d), bottom = new(u + d + w, v, w, d);
            Rect right = new(u, v + d, d, h), front = new(u + d, v + d, w, h), left = new(u + d + w, v + d, d, h), back = new(u + d * 2 + w, v + d, w, h);
            if (capeLayout) (front, back) = (back, front);   // pelerinin dÄ±ÅŸ yÃ¼zÃ¼ (1,1) karakterin arkasÄ±na bakar
            yield return new(texture, front, new(x0, y1, z1), new(x1, y1, z1), new(x0, y0, z1), new(0, 0, 1), part);
            yield return new(texture, back, new(x1, y1, z0), new(x0, y1, z0), new(x1, y0, z0), new(0, 0, -1), part);
            yield return new(texture, right, new(x0, y1, z0), new(x0, y1, z1), new(x0, y0, z0), new(-1, 0, 0), part);
            yield return new(texture, left, new(x1, y1, z1), new(x1, y1, z0), new(x1, y0, z1), new(1, 0, 0), part);
            yield return new(texture, top, new(x0, y1, z0), new(x1, y1, z0), new(x0, y1, z1), new(0, 1, 0), part);
            yield return new(texture, bottom, new(x0, y0, z1), new(x1, y0, z1), new(x0, y0, z0), new(0, -1, 0), part);
        }

        /// <summary>BÃ¼tÃ¼n model: ayaklar y=0'da, baÅŸ y=32'ye kadar. legacy: 64Ã—32 eski skin (sol uzuvlar saÄŸÄ±n aynasÄ±).</summary>
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
            // Ä°kinci katman (ÅŸapka, ceket, kollar, paÃ§alar); eski skinlerde yalnÄ±zca ÅŸapka
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

        /// <summary>UzuvlarÄ±n dÃ¶nme noktalarÄ± (omuz, kalÃ§a, pelerinin Ã¼st kenarÄ±).</summary>
        public static Pivot[] Pivots(double swing, double capeLift) =>
        [
            new(new(0, 24, 0), 0), new(new(0, 24, 0), 0),
            new(new(-5, 22, 0), swing), new(new(5, 22, 0), -swing),
            new(new(-2, 12, 0), -swing), new(new(2, 12, 0), swing),
            new(new(0, 24, -2.2), capeLift),
        ];

        /// <summary>x ekseni Ã§evresinde dÃ¶ndÃ¼rme (Ã¶ne/arkaya).</summary>
        public static V3 Pitch(V3 p, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return new(p.X, p.Y * c - p.Z * s, p.Y * s + p.Z * c);
        }

        /// <summary>GÃ¶rÃ¼ÅŸ dÃ¶nÃ¼ÅŸÃ¼mÃ¼: Ã¶nce y Ã§evresinde (karakter dÃ¶ner), sonra x Ã§evresinde (yukarÄ±dan bakÄ±ÅŸ).</summary>
        public static V3 View(V3 p, double yaw, double pitch)
        {
            double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
            var r = new V3(p.X * cy + p.Z * sy, p.Y, -p.X * sy + p.Z * cy);
            return Pitch(r, pitch);
        }
    }

    /// <summary>
    /// Profildeki dÃ¶nen 3B karakter. KendiliÄŸinden yavaÅŸÃ§a dÃ¶ner, fareyle sÃ¼rÃ¼klenince elle Ã§evrilir. Pelerin varsa
    /// sÄ±rtÄ±nda gÃ¶rÃ¼nÃ¼r. GÃ¶rÃ¼nmÃ¼yorken (sekme kapalÄ±, pencere gizli) hiÃ§ Ã§izim yapmaz.
    /// </summary>
    internal sealed class SkinViewer3D : Control
    {
        public static readonly StyledProperty<byte[]?> SkinProperty = AvaloniaProperty.Register<SkinViewer3D, byte[]?>(nameof(Skin));
        public static readonly StyledProperty<byte[]?> CapeProperty = AvaloniaProperty.Register<SkinViewer3D, byte[]?>(nameof(Cape));
        public static readonly StyledProperty<bool> SlimProperty = AvaloniaProperty.Register<SkinViewer3D, bool>(nameof(Slim));
        public static readonly StyledProperty<bool> AnimateProperty = AvaloniaProperty.Register<SkinViewer3D, bool>(nameof(Animate), true);

        /// <summary>Skin PNG baytlarÄ± (64Ã—64 ya da 64Ã—32). BoÅŸsa varsayÄ±lan karakter Ã§izilir.</summary>
        public byte[]? Skin { get => GetValue(SkinProperty); set => SetValue(SkinProperty, value); }
        /// <summary>Pelerin PNG baytlarÄ± (64Ã—32); boÅŸsa pelerinsiz.</summary>
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
        private double _yaw = -0.5, _time;
        private bool _dragging, _attached;
        private Point _dragFrom;
        private DateTime _lastDrag = DateTime.MinValue, _lastFrame = DateTime.UtcNow;

        public SkinViewer3D()
        {
            Cursor = new Cursor(StandardCursorType.SizeWestEast);
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
            if (_skin != DefaultSkin.Value) _skin?.Dispose();   // varsayÄ±lan karakter paylaÅŸÄ±lÄ±r, atÄ±lmaz
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

        /// <summary>Skin yoksa: dÃ¼z renkli basit bir karakter (Steve'in renklerine yakÄ±n), yÃ¼klÃ¼ bir dosya gerektirmez.</summary>
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
                Fill(0, 0, 32, 16, 0xFFB4846C);   // baÅŸ (ten)
                Fill(8, 0, 16, 8, 0xFF2A1D10);    // saÃ§
                Fill(0, 8, 32, 2, 0xFF2A1D10);
                Fill(9, 12, 2, 1, 0xFFFFFFFF); Fill(13, 12, 2, 1, 0xFFFFFFFF);   // gÃ¶zler
                Fill(10, 12, 1, 1, 0xFF4A3A8F); Fill(13, 12, 1, 1, 0xFF4A3A8F);
                Fill(16, 16, 24, 16, 0xFF2FA3A8); // gÃ¶vde (tiÅŸÃ¶rt)
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
                // SÃ¼rÃ¼klemeden 2 sn sonra kendiliÄŸinden dÃ¶nmeye devam eder
                if (!_dragging && (now - _lastDrag).TotalSeconds > 2) _yaw += dt * 0.55;
                InvalidateVisual();
                RequestFrame();
            });
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            _dragging = true;
            _dragFrom = e.GetPosition(this);
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_dragging) return;
            var p = e.GetPosition(this);
            _yaw += (p.X - _dragFrom.X) * 0.015;
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
            // Model 32 birim yÃ¼ksek, ~16 birim geniÅŸ; pelerin ve dÃ¶nÃ¼ÅŸ iÃ§in pay bÄ±rakÄ±lÄ±r
            double scale = Math.Min(size.Height / 38, size.Width / 20);
            if (scale <= 0) return;
            double cx = size.Width / 2, cy = size.Height / 2 + 16 * scale;   // y=16 (gÃ¶vde ortasÄ±) ekranÄ±n ortasÄ±nda
            const double viewPitch = -0.22;   // hafif yukarÄ±dan
            double swing = Animate ? Math.Sin(_time * 2.2) * 0.18 : 0;
            double capeLift = 0.12 + (Animate ? Math.Sin(_time * 1.6) * 0.04 : 0);
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
                if (SkinModel.View(n, _yaw, viewPitch).Z <= 0.001) continue;   // arkasÄ± dÃ¶nÃ¼k yÃ¼z
                var tl = Project(f.TopLeft, f.Part);
                var tr = Project(f.TopRight, f.Part);
                var bl = Project(f.BottomLeft, f.Part);
                var centre = f.TopRight + (f.BottomLeft - f.TopRight) * 0.5;
                if (pivot.Pitch != 0) centre = SkinModel.Pitch(centre - pivot.At, pivot.Pitch) + pivot.At;
                double depth = SkinModel.View(centre - new SkinModel.V3(0, 16, 0), _yaw, viewPitch).Z;
                visible.Add((depth, f, tl, tr, bl));
            }
            visible.Sort((a, b) => a.Depth.CompareTo(b.Depth));

            using var _ = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None, EdgeMode = EdgeMode.Aliased });
            foreach (var (_, f, tl, tr, bl) in visible)
            {
                var image = f.Texture == SkinModel.CapeTexture ? _cape : _skin;
                if (image == null) continue;
                double w = f.Uv.Width, h = f.Uv.Height;
                var m = new Matrix((tr.X - tl.X) / w, (tr.Y - tl.Y) / w, (bl.X - tl.X) / h, (bl.Y - tl.Y) / h, tl.X, tl.Y);
                using (context.PushTransform(m))
                {
                    // YÃ¼zler arasÄ±nda saÃ§ teli kadar boÅŸluk kalmasÄ±n diye hedef Ã§ok az bÃ¼yÃ¼tÃ¼lÃ¼r
                    const double e = 0.04;
                    context.DrawImage(image, f.Uv, new Rect(-e, -e, w + e * 2, h + e * 2));
                }
            }
        }
    }
}
