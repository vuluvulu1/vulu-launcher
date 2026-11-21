using Avalonia;
using Avalonia.Media;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Kurulan paketin kapağındaki ilerleme (telefonlardaki uygulama indirme simgesi gibi): ince bir halka ve içinde
    /// dolan bir dilim. İlerleme bilinmiyorsa ("Bekliyor", "Hazırlanıyor") halkada dönen kısa bir yay olur.
    /// Gösterilen değer hedefe yumuşakça yetişir, sıçramaz.
    /// </summary>
    internal sealed class InstallRing : Animated
    {
        public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<InstallRing, double>(nameof(Value));
        public static readonly StyledProperty<bool> IsIndeterminateProperty = AvaloniaProperty.Register<InstallRing, bool>(nameof(IsIndeterminate), true);

        /// <summary>0..1</summary>
        public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public bool IsIndeterminate { get => GetValue(IsIndeterminateProperty); set => SetValue(IsIndeterminateProperty, value); }

        private double _shown;
        private long _last;

        public InstallRing() { Width = 56; Height = 56; }

        public override void Render(DrawingContext context)
        {
            double size = Math.Min(Bounds.Width, Bounds.Height);
            if (size <= 0) return;
            long ms = Clock.ElapsedMilliseconds;
            double dt = Math.Clamp((ms - _last) / 1000.0, 0, 0.1);
            _last = ms;

            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
            double r = size / 2 - 2;
            var white = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), new Pen(white, 2.4), center, r, r);

            if (IsIndeterminate)
            {
                double start = ms / 1000.0 * 300;
                context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 2.4, lineCap: PenLineCap.Round),
                    Arc(center, r - 5, start, 70));
                _shown = 0;
                return;
            }

            // Hedefe yaklaşır: büyük atlayışlarda hızlı, küçüklerde yumuşak
            double target = Math.Clamp(Value, 0, 1);
            _shown += (target - _shown) * Math.Min(1, dt * 6);
            if (Math.Abs(target - _shown) < 0.002) _shown = target;
            if (_shown <= 0.002) return;
            double pie = r - 4.5;
            if (_shown >= 0.999) { context.DrawEllipse(white, null, center, pie, pie); return; }
            context.DrawGeometry(white, null, Pie(center, pie, _shown * 360));
        }

        private static (double X, double Y) At(Point c, double r, double deg)
        {
            double a = (deg - 90) * Math.PI / 180;
            return (c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        }

        private static StreamGeometry Arc(Point c, double r, double start, double sweep)
        {
            var g = new StreamGeometry();
            using var ctx = g.Open();
            var (x0, y0) = At(c, r, start);
            var (x1, y1) = At(c, r, start + sweep);
            ctx.BeginFigure(new Point(x0, y0), false);
            ctx.ArcTo(new Point(x1, y1), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise);
            ctx.EndFigure(false);
            return g;
        }

        private static StreamGeometry Pie(Point c, double r, double sweep)
        {
            var g = new StreamGeometry();
            using var ctx = g.Open();
            var (x0, y0) = At(c, r, 0);
            var (x1, y1) = At(c, r, sweep);
            ctx.BeginFigure(c, true);
            ctx.LineTo(new Point(x0, y0));
            ctx.ArcTo(new Point(x1, y1), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise);
            ctx.EndFigure(true);
            return g;
        }
    }
}
