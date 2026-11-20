using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Kütüphane açılışı ("kitabın içinden geç"): karanlıkta deri ciltli kapalı bir kitap belirir, kapağı perspektifle
    /// açılır, birkaç sayfa hızla çevrilir; sağ sayfaya el yazısıyla "vulu Launcher" yazılır (yazıyı SplashView'daki
    /// ScriptTitle çizer, yeri <see cref="PageRect"/>). Açılış anında kamera yazının içine dalar (sahne büyür, kenarlardan
    /// hız çizgileri akar), ekran kâğıtla dolar ve kâğıdın ortası açılarak arkadaki launcher görünür (<see cref="KutRush"/>).
    /// </summary>
    internal sealed class KutBook : Control
    {
        // Zaman çizelgesi (ms)
        public const double FadeIn = 350, OpenAt = 420, OpenMs = 620, FlipAt = 980, FlipStep = 120, Flips = 3, FlipMs = 300;
        /// <summary>Yazının başladığı an (sayfalar çevrildikten sonra).</summary>
        public const double WriteAt = FlipAt + (Flips - 1) * FlipStep + FlipMs - 60;

        private double _t;
        private double _cx, _cy, _w, _h;

        /// <summary>Sağ sayfanın yazı alanı (kontrolün koordinatlarında).</summary>
        public Rect PageRect { get; private set; }

        private static readonly IBrush Leather = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#5A2A1C"), 0), new GradientStop(Color.Parse("#46201A"), 0.5), new GradientStop(Color.Parse("#2E1510"), 1) },
        };
        private static readonly IBrush Paper = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#F1E6CF"), 0), new GradientStop(Color.Parse("#E8DABE"), 1) },
        };
        private static readonly IBrush Endpaper = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#6E3B26"), 0), new GradientStop(Color.Parse("#4E2A1C"), 1) },
        };
        private static readonly IBrush Gold = new SolidColorBrush(Color.Parse("#D2A24C"));
        private static readonly IPen GoldPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0xD2, 0xA2, 0x4C)), 1.6);
        private static readonly IBrush TextLine = new SolidColorBrush(Color.FromArgb(46, 0x4A, 0x32, 0x1E));
        private static readonly IBrush Ornament = new SolidColorBrush(Color.FromArgb(150, 0x6B, 0x4A, 0x2E));
        private static readonly IBrush PageEdge = new SolidColorBrush(Color.Parse("#D9C9A8"));
        private static readonly IBrush PageLine = new SolidColorBrush(Color.FromArgb(70, 0x8A, 0x70, 0x50));
        private static readonly IBrush Gutter = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(120, 60, 40, 20), 0), new GradientStop(Color.FromArgb(0, 60, 40, 20), 1) },
        };
        private static readonly IBrush Lamp = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.42, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.42, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(52, 255, 196, 120), 0), new GradientStop(Color.FromArgb(0, 255, 196, 120), 1) },
        };
        private static readonly IBrush TableShadow = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.FromArgb(150, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0, 0, 0, 0), 1) },
        };

        private FormattedText? _emblem;

        public void Update(double t)
        {
            _t = t;
            var size = Bounds.Size;
            _h = Math.Round(Math.Min(440, size.Height * 0.56));
            _w = Math.Round(_h * 0.72);
            _cx = size.Width / 2;
            _cy = size.Height * 0.47;
            double open = CoverProgress(t);
            double spine = _cx - _w / 2 * (1 - Ease(Math.Min(1, open * 1.15)));
            PageRect = new Rect(spine + _w * 0.08, _cy - _h * 0.24, _w * 0.84, _h * 0.36);
            InvalidateVisual();
        }

        private static double Ease(double x) => 1 - Math.Pow(1 - Math.Clamp(x, 0, 1), 3);
        private static double InOut(double x) { x = Math.Clamp(x, 0, 1); return x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2; }
        private static double CoverProgress(double t) => InOut((t - OpenAt) / OpenMs);

        public override void Render(DrawingContext ctx)
        {
            var size = Bounds.Size;
            if (size.Width < 10 || _h < 40) return;   // boyut ilk Update'te hesaplanır
            ctx.FillRectangle(Lamp, new Rect(size));

            double appear = Ease(_t / FadeIn);
            double open = CoverProgress(_t);
            double spine = _cx - _w / 2 * (1 - Ease(Math.Min(1, open * 1.15)));
            double top = _cy - _h / 2 + 14 * (1 - appear), bottom = top + _h;

            using (ctx.PushOpacity(appear))
            {
                // Masadaki gölge
                ctx.DrawEllipse(TableShadow, null, new Point(spine + _w * (open > 0.5 ? 0 : 0.5), bottom + 10), _w * (0.75 + 0.5 * open), 26);

                // Sağdaki sayfa bloğu (kalınlık: alt ve sağ kenarda sayfa çizgileri)
                var block = new Rect(spine, top, _w, _h);
                ctx.FillRectangle(PageEdge, new Rect(block.X + 3, block.Y + 4, block.Width + 3, block.Height + 3));
                for (int i = 0; i < 5; i++) ctx.FillRectangle(PageLine, new Rect(block.Right + 1, block.Y + 6 + i, 1, block.Height - 4));
                ctx.FillRectangle(Paper, block);
                ctx.FillRectangle(Gutter, new Rect(block.X, block.Y, 26, block.Height));
                // sağ sayfa: yazının altında küçük süs (yazı bitince belirir)
                double orn = Ease((_t - WriteAt - 700) / 500);
                if (orn > 0)
                    using (ctx.PushOpacity(orn))
                    {
                        double oy = PageRect.Bottom + _h * 0.04, ox = PageRect.Center.X;
                        ctx.FillRectangle(Ornament, new Rect(ox - 46 * orn, oy, 92 * orn, 1.2));
                        ctx.DrawEllipse(Ornament, null, new Point(ox, oy + 0.6), 3, 3);
                    }

                // Sol sayfa (kapak açıldıkça ortaya çıkan iç kapak ve sayfalar)
                if (open > 0.5)
                {
                    var left = new Rect(spine - _w, top, _w, _h);
                    ctx.FillRectangle(PageEdge, new Rect(left.X - 6, left.Y + 4, left.Width + 3, left.Height + 3));
                    ctx.FillRectangle(Paper, left);
                    // soluk metin satırları ve sayfa numarası (gerçek bir kitap sayfası gibi)
                    var rng = new Random(3);
                    double lx = left.X + _w * 0.13, lw = _w * 0.74, ly = left.Y + _h * 0.12, lh = _h * 0.034;
                    for (int i = 0; i < 22; i++)
                    {
                        double y = ly + i * lh;
                        if (y > left.Bottom - _h * 0.12) break;
                        bool paragraphEnd = rng.NextDouble() < 0.16;
                        double w = paragraphEnd ? lw * (0.3 + rng.NextDouble() * 0.4) : lw * (0.94 + rng.NextDouble() * 0.06);
                        double x = lx + (i == 0 || rng.NextDouble() < 0.0 ? 0 : 0);
                        ctx.FillRectangle(TextLine, new Rect(x, y, w, Math.Max(1.6, lh * 0.32)));
                        if (paragraphEnd) i++;
                    }
                    ctx.FillRectangle(TextLine, new Rect(left.X + _w / 2 - 6, left.Bottom - _h * 0.06, 12, 2));
                    using (ctx.PushTransform(Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(spine * 2, 0)))
                        ctx.FillRectangle(Gutter, new Rect(spine, top, 26, _h));
                }

                // Hızla çevrilen sayfalar
                for (int i = 0; i < Flips; i++)
                {
                    double p = InOut((_t - (FlipAt + i * FlipStep)) / FlipMs);
                    if (p <= 0 || p >= 1) continue;
                    DrawLeaf(ctx, spine, top, p * Math.PI, isCover: false);
                }

                // Kapak: sırtın çevresinde döner (0 kapalı, π tam açık)
                if (open < 1) DrawLeaf(ctx, spine, top, open * Math.PI, isCover: true);
                else
                {
                    // tam açık kapak solda: iç kapak kâğıdının arkasında deri kenar
                    ctx.FillRectangle(Leather, new Rect(spine - _w - 8, top - 6, 8, _h + 12));
                }
            }
        }

        /// <summary>Sırtta menteşeli bir yaprak (kapak ya da sayfa); açıya göre perspektifle daralır, serbest kenarı yakına gelir.</summary>
        private void DrawLeaf(DrawingContext ctx, double spine, double top, double theta, bool isCover)
        {
            double c = Math.Cos(theta), s = Math.Sin(theta);
            double reach = (isCover ? _w + 8 : _w) * c;          // serbest kenarın yatay konumu (sırta göre)
            double lift = s * _h * 0.07;                           // yakına gelen kenar büyür
            double t0 = top - (isCover ? 6 : 0), b0 = top + _h + (isCover ? 6 : 0);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(spine, t0), true);
                g.LineTo(new Point(spine + reach, t0 - lift));
                g.LineTo(new Point(spine + reach, b0 + lift));
                g.LineTo(new Point(spine, b0));
                g.EndFigure(true);
            }
            bool front = c > 0;
            IBrush fill = isCover ? (front ? Leather : Endpaper) : Paper;
            ctx.DrawGeometry(fill, null, geo);
            // dönerken kararma (ışıktan uzaklaşır)
            byte dark = (byte)(150 * Math.Abs(s) * (front ? 0.8 : 0.5));
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(dark, 0, 0, 0)), null, geo);
            if (isCover && front && c > 0.25)
            {
                // altın çerçeve ve ortada "vulu" (perspektifle sıkışır)
                using (ctx.PushTransform(Matrix.CreateScale(c, 1) * Matrix.CreateTranslation(spine, 0)))
                {
                    var frame = new Rect(22, t0 + 22, (_w + 8) - 44, (b0 - t0) - 44);
                    ctx.DrawRectangle(null, GoldPen, frame, 4, 4);
                    ctx.DrawRectangle(null, GoldPen, frame.Deflate(7), 2, 2);
                    _emblem ??= new FormattedText("vulu", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("avares://vulu Launcher/Assets/Fonts#Great Vibes")), _h * 0.2, Gold);
                    ctx.DrawText(_emblem, new Point((_w + 8 - _emblem.Width) / 2, t0 + (b0 - t0) * 0.38 - _emblem.Height / 2));
                    ctx.FillRectangle(Gold, new Rect((_w + 8) / 2 - 30, t0 + (b0 - t0) * 0.56, 60, 1.4));
                }
                // sırt bandı
                ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), new Rect(spine, t0, 14 * c, b0 - t0));
            }
            else if (!isCover)
            {
                // sayfanın alt köşesine doğru hafif gölge çizgisi
                ctx.FillRectangle(PageLine, new Rect(Math.Min(spine, spine + reach), b0 + lift - 1, Math.Abs(reach), 1));
            }
        }
    }

    /// <summary>
    /// Kitabın içine dalış: odaktan dışarı akan hız çizgileri, ekranı dolduran kâğıt ve ortası açılarak arkadaki
    /// launcher'ı gösteren delik. <see cref="Update"/>: açılışın başından beri geçen süre ve dalış noktası.
    /// </summary>
    internal sealed class KutRush : Control
    {
        public const double ZoomMs = 620, PaperAt = 380, PaperFull = 560, HoleMs = 420, Total = PaperFull + HoleMs;
        private double _open = -1;
        private Point _focus;
        private readonly (double Angle, double Speed, double Offset, double Width)[] _lines;

        public KutRush()
        {
            IsHitTestVisible = false;
            var rng = new Random(5);
            _lines = Enumerable.Range(0, 90).Select(_ => (rng.NextDouble() * Math.PI * 2, 0.6 + rng.NextDouble() * 0.8, rng.NextDouble(), 0.8 + rng.NextDouble() * 1.8)).ToArray();
        }

        public void Update(double open, Point focus)
        {
            _open = open;
            _focus = focus;
            InvalidateVisual();
        }

        private static readonly Color PaperColor = Color.Parse("#EFE3CA");

        public override void Render(DrawingContext ctx)
        {
            if (_open < 0) return;
            var size = Bounds.Size;
            double reach = Math.Sqrt(size.Width * size.Width + size.Height * size.Height);

            // Hız çizgileri: odaktan dışarı, dalış hızlandıkça uzar ve yoğunlaşır
            double z = Math.Clamp(_open / ZoomMs, 0, 1);
            double fadeLines = 1 - Math.Clamp((_open - PaperAt) / (PaperFull - PaperAt), 0, 1);
            if (z > 0.05 && fadeLines > 0)
                foreach (var (a, speed, off, width) in _lines)
                {
                    double head = ((off + _open / 1000 * speed * 2.4) % 1) * reach;
                    double len = (40 + 260 * z * z) * speed;
                    double r0 = Math.Max(20, head - len);
                    var p0 = new Point(_focus.X + Math.Cos(a) * r0, _focus.Y + Math.Sin(a) * r0);
                    var p1 = new Point(_focus.X + Math.Cos(a) * head, _focus.Y + Math.Sin(a) * head);
                    byte alpha = (byte)(255 * Math.Min(1, z * 1.4) * 0.55 * fadeLines);
                    ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0xF3, 0xE6, 0xCC)), width), p0, p1);
                }

            // Kâğıt ekranı doldurur, sonra ortası açılır (delik hızla büyür, kenarı yumuşak)
            double fill = Math.Clamp((_open - PaperAt) / (PaperFull - PaperAt), 0, 1);
            if (fill <= 0) return;
            double hole = Math.Clamp((_open - PaperFull) / HoleMs, 0, 1);
            hole = hole * hole * (3 - 2 * hole);
            if (hole <= 0)
            {
                ctx.FillRectangle(new SolidColorBrush(PaperColor, fill * fill), new Rect(size));
                return;
            }
            // Delik: kenarı hafif pürüzlü (yırtılan kâğıt), hızla ekranın köşelerini aşar
            double r = hole * reach * 0.62;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.SetFillRule(FillRule.EvenOdd);
                g.BeginFigure(new Point(-10, -10), true);
                g.LineTo(new Point(size.Width + 10, -10));
                g.LineTo(new Point(size.Width + 10, size.Height + 10));
                g.LineTo(new Point(-10, size.Height + 10));
                g.EndFigure(true);
                const int n = 96;
                for (int i = 0; i <= n; i++)
                {
                    double a2 = i * Math.PI * 2 / n;
                    double rr = r * (1 + 0.035 * Math.Sin(a2 * 7 + 1.3) + 0.025 * Math.Sin(a2 * 17 + 0.4) + 0.015 * Math.Sin(a2 * 31));
                    var pt = new Point(_focus.X + Math.Cos(a2) * rr, _focus.Y + Math.Sin(a2) * rr);
                    if (i == 0) g.BeginFigure(pt, true); else g.LineTo(pt);
                }
                g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(PaperColor), new Pen(new SolidColorBrush(Color.Parse("#FBF5E8")), 2.5), geo);
        }
    }
}
