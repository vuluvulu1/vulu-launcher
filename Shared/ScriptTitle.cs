using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace VuluShared
{
    /// <summary>
    /// El yazısı "vulu Launcher" başlığı (Great Vibes harfleri yol olarak). Her harf önce ince bakır kalemle çizilir,
    /// sonra altın-bakırla dolar. Launcher'ın açılışında ve kurulum programında kullanılır.
    /// </summary>
    public sealed partial class ScriptTitle : Decorator
    {
        private const double ViewWidth = 4401, ViewHeight = 1182;
        /// <summary>Tüm harflerin çizilip dolması (ms).</summary>
        public const double Duration = 11 * 70 + 300 + 400;

        private static readonly Easing PenEase = new SplineEasing(0.45, 0, 0.35, 1);
        private static readonly Easing InkEase = new CubicEaseOut();

        private readonly List<(Path Path, double Length, Brush Fill, Brush Stroke)> _letters = new();
        private readonly Stopwatch _clock = new();
        private double _delay, _last = -1, _thickness;
        private bool _playing;

        public ScriptTitle()
        {
            var gold = new GradientStops
            {
                new GradientStop(Color.Parse("#8A5A2B"), 0),
                new GradientStop(Color.Parse("#E3B37A"), 0.3),
                new GradientStop(Color.Parse("#F3D6A4"), 0.5),
                new GradientStop(Color.Parse("#C27E3E"), 0.7),
                new GradientStop(Color.Parse("#7A4D24"), 1),
            };
            var canvas = new Canvas { Width = ViewWidth, Height = ViewHeight };
            foreach (string data in Letters)
            {
                var fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(ViewWidth, ViewHeight, RelativeUnit.Absolute),
                    GradientStops = gold,
                    Opacity = 0,
                };
                var stroke = new SolidColorBrush(Color.Parse("#E3B37A"));
                var path = new Path
                {
                    Data = Geometry.Parse(data),
                    Fill = fill,
                    Stroke = stroke,
                    StrokeLineCap = PenLineCap.Round,
                    StrokeJoin = PenLineJoin.Round,
                };
                // viewBox -3 -831: yollar tuvale kaydırılır
                Canvas.SetLeft(path, 3);
                Canvas.SetTop(path, 831);
                canvas.Children.Add(path);
                _letters.Add((path, PathLength(data), fill, stroke));
            }
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = canvas };
            IsHitTestVisible = false;
            SizeChanged += (_, _) => { if (_last >= 0) Render(_last); };
            Render(0);
        }

        /// <param name="ms">Çizimin başından beri geçen süre; harf i 70·i ms'de çizilmeye, 300 ms sonra dolmaya başlar.</param>
        public void Render(double ms)
        {
            _last = ms;
            // Kalem ekranda ~1,1 piksel kalınlığında kalır
            double thickness = Bounds.Width > 0 ? 1.1 * ViewWidth / Bounds.Width : 9.7;
            bool resized = Math.Abs(thickness - _thickness) > 0.01;
            _thickness = thickness;
            for (int i = 0; i < _letters.Count; i++)
            {
                var (path, length, fill, stroke) = _letters[i];
                // Kesik çizgi birimleri kalınlığın katlarıdır; çizgi harfin boyu kadar uzun tutulur
                double dash = length / thickness + 1;
                if (resized)
                {
                    path.StrokeThickness = thickness;
                    path.StrokeDashArray = new AvaloniaList<double> { dash, dash };
                }
                path.StrokeDashOffset = dash * (1 - PenEase.Ease(Progress(ms, i * 70, 500)));
                double ink = InkEase.Ease(Progress(ms, 300 + i * 70, 400));
                fill.Opacity = ink;
                stroke.Opacity = 1 - 0.7 * ink;
            }
        }

        private List<SolidColorBrush>? _ember;
        private static readonly (double At, Color Color)[] EmberStops =
        {
            (0, Color.Parse("#FFFFFF")), (0.18, Color.Parse("#FFF0C4")), (0.5, Color.Parse("#FFBE5E")), (1, Color.Parse("#F2A33A")),
        };

        /// <summary>
        /// Meşale açılışı: kalem yok; harfler sırayla akkor beyazdan ambere tutuşur (web: m-catch, harf başına 85 ms arayla 1,1 sn).
        /// </summary>
        /// <param name="ms">Tutuşmanın başından beri geçen süre.</param>
        public void RenderEmber(double ms)
        {
            if (_ember == null)
            {
                _ember = new();
                foreach (var (path, _, _, _) in _letters)
                {
                    var brush = new SolidColorBrush(Colors.White) { Opacity = 0 };
                    path.Fill = brush;
                    path.Stroke = null;
                    _ember.Add(brush);
                }
            }
            for (int i = 0; i < _ember.Count; i++)
            {
                double p = InkEase.Ease(Progress(ms, i * 85, 1100));
                _ember[i].Opacity = Math.Min(1, p / 0.18);
                int k = 1;
                while (k < EmberStops.Length - 1 && p > EmberStops[k].At) k++;
                var (a, ca) = EmberStops[k - 1];
                var (b, cb) = EmberStops[k];
                double f = b > a ? Math.Clamp((p - a) / (b - a), 0, 1) : 1;
                _ember[i].Color = Color.FromRgb((byte)(ca.R + (cb.R - ca.R) * f), (byte)(ca.G + (cb.G - ca.G) * f), (byte)(ca.B + (cb.B - ca.B) * f));
            }
        }

        /// <summary>Kendi başına oynar (kurulum programı gibi dışarıdan zamanlanmayan yerler için).</summary>
        public void Play(double delayMs = 0)
        {
            _delay = delayMs;
            _clock.Restart();
            Render(-delayMs);
            if (_playing) return;
            _playing = true;
            Frame(TimeSpan.Zero);
        }

        private void Frame(TimeSpan _)
        {
            double ms = _clock.Elapsed.TotalMilliseconds - _delay;
            Render(ms);
            if (ms > Duration || TopLevel.GetTopLevel(this) is not { } top) { _playing = false; return; }
            top.RequestAnimationFrame(Frame);
        }

        private static double Progress(double t, double start, double duration) => Math.Clamp((t - start) / duration, 0, 1);

        /// <summary>Yolun yaklaşık uzunluğu (M, L, H, V, Q ve Z komutları); kalem çizgisinin boyu için.</summary>
        internal static double PathLength(string data)
        {
            var tokens = new List<string>();
            int k = 0;
            while (k < data.Length)
            {
                char c = data[k];
                if (char.IsLetter(c)) { tokens.Add(c.ToString()); k++; }
                else if (c == '-' || c == '.' || char.IsDigit(c))
                {
                    int start = k++;
                    bool dot = c == '.';
                    while (k < data.Length && (char.IsDigit(data[k]) || (data[k] == '.' && !dot)))
                    {
                        if (data[k] == '.') dot = true;
                        k++;
                    }
                    tokens.Add(data[start..k]);
                }
                else k++;
            }

            double length = 0, x = 0, y = 0, sx = 0, sy = 0;
            char command = 'M';
            int n = 0;
            double Next() => double.Parse(tokens[n++], CultureInfo.InvariantCulture);
            while (n < tokens.Count)
            {
                if (char.IsLetter(tokens[n][0])) command = tokens[n++][0];
                switch (command)
                {
                    case 'M': x = sx = Next(); y = sy = Next(); command = 'L'; break;
                    case 'L': { double nx = Next(), ny = Next(); length += Math.Sqrt((nx - x) * (nx - x) + (ny - y) * (ny - y)); x = nx; y = ny; break; }
                    case 'H': { double nx = Next(); length += Math.Abs(nx - x); x = nx; break; }
                    case 'V': { double ny = Next(); length += Math.Abs(ny - y); y = ny; break; }
                    case 'Q':
                    {
                        double cx = Next(), cy = Next(), ex = Next(), ey = Next(), px = x, py = y;
                        for (int s = 1; s <= 8; s++)
                        {
                            double u = s / 8.0, a = (1 - u) * (1 - u), b = 2 * (1 - u) * u, d = u * u;
                            double qx = a * x + b * cx + d * ex, qy = a * y + b * cy + d * ey;
                            length += Math.Sqrt((qx - px) * (qx - px) + (qy - py) * (qy - py));
                            px = qx; py = qy;
                        }
                        x = ex; y = ey;
                        break;
                    }
                    case 'Z': length += Math.Sqrt((sx - x) * (sx - x) + (sy - y) * (sy - y)); x = sx; y = sy; command = 'L'; break;
                    default: n++; break;   // bilinmeyen komut: atlanır
                }
            }
            return length;
        }
    }
}
