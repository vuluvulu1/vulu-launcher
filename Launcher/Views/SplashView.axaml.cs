using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;
using VuluLauncher.Native;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Açılış sahnesi (web arayüzündeki perde açılışının native hali). Zamanlama (~2,4 sn): spot yanar → harfler
    /// sırayla çizilip dolar → ayraç ve karşılama belirir. Açılırken spot söner, perdeler iki yana çekilirken toplanır,
    /// saçak kalkar. Kareler pencerenin çizim döngüsüne bağlıdır (RequestAnimationFrame).
    /// </summary>
    internal sealed partial class SplashView : UserControl
    {
        private const double StrokeWidth = 9.7;   // 500 piksellik yazıda ~1,1 piksel
        private static readonly Easing PenEase = new SplineEasing(0.45, 0, 0.35, 1);
        private static readonly Easing CurtainEase = new SplineEasing(0.65, 0, 0.3, 1);
        private static readonly Easing ValanceEase = new SplineEasing(0.65, 0, 0.35, 1);
        private static readonly Easing OutEase = new CubicEaseOut();

        private readonly List<(Path Path, double Dash, Brush Fill)> _letters = new();
        private readonly Stopwatch _clock = new();
        private readonly Stopwatch _openClock = new();
        private SplashViewModel? _vm;
        private TaskCompletionSource? _opened;
        private bool _running;

        public SplashView()
        {
            InitializeComponent();
            BuildLetters();
            Stage.SizeChanged += (_, e) => Layout(e.NewSize);
            DataContextChanged += (_, _) =>
            {
                if (_vm != null) { _vm.Played -= Play; _vm.Opener = null; }
                _vm = DataContext as SplashViewModel;
                if (_vm == null) return;
                _vm.Played += Play;
                _vm.Opener = OpenAsync;
            };
        }

        /// <summary>Harfler web arayüzündeki yazının yollarıdır; her birinin kalem çizgisi kendi uzunluğuna göre ayarlanır.</summary>
        private void BuildLetters()
        {
            var gold = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(4401, 1182, RelativeUnit.Absolute),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#8A5A2B"), 0),
                    new GradientStop(Color.Parse("#E3B37A"), 0.3),
                    new GradientStop(Color.Parse("#F3D6A4"), 0.5),
                    new GradientStop(Color.Parse("#C27E3E"), 0.7),
                    new GradientStop(Color.Parse("#7A4D24"), 1),
                },
            };
            foreach (string data in SplashTitle.Letters)
            {
                double dash = PathLength(data) / StrokeWidth + 1;
                var fill = new LinearGradientBrush { StartPoint = gold.StartPoint, EndPoint = gold.EndPoint, GradientStops = gold.GradientStops, Opacity = 0 };
                var path = new Path
                {
                    Data = Geometry.Parse(data),
                    Fill = fill,
                    Stroke = new SolidColorBrush(Color.Parse("#E3B37A")),
                    StrokeThickness = StrokeWidth,
                    StrokeLineCap = PenLineCap.Round,
                    StrokeJoin = PenLineJoin.Round,
                    StrokeDashArray = new AvaloniaList<double> { dash, dash },
                    StrokeDashOffset = dash,
                };
                // viewBox -3 -831: yollar tuvale kaydırılır
                Canvas.SetLeft(path, 3);
                Canvas.SetTop(path, 831);
                Letters.Children.Add(path);
                _letters.Add((path, dash, fill));
            }
        }

        private void Layout(Size size)
        {
            double half = Math.Ceiling(size.Width * 0.505);
            CurtainLeft.Width = CurtainRight.Width = half;
            // Kadife kıvrımları: 80 piksellik tekrar eden gölge-ışık şeridi
            FoldsLeft.Background = FoldsRight.Background = new LinearGradientBrush
            {
                SpreadMethod = GradientSpreadMethod.Repeat,
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(80, 0, RelativeUnit.Absolute),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(140, 0, 0, 0), 0),
                    new GradientStop(Color.FromArgb(51, 0, 0, 0), 22 / 80.0),
                    new GradientStop(Color.FromArgb(13, 255, 220, 180), 40 / 80.0),
                    new GradientStop(Color.FromArgb(46, 0, 0, 0), 58 / 80.0),
                    new GradientStop(Color.FromArgb(140, 0, 0, 0), 1),
                },
            };
            // Spot: yazının üstüne düşen ışık havuzu ve çevresindeki karanlık
            Pool.Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.47, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.47, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.26, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.30, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(33, 255, 224, 178), 0),
                    new GradientStop(Color.FromArgb(13, 255, 224, 178), 0.45),
                    new GradientStop(Color.FromArgb(0, 255, 224, 178), 0.75),
                },
            };
            Dark.Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.47, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.47, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.30, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.36, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.35),
                    new GradientStop(Color.FromArgb(240, 0, 0, 0), 1),
                },
            };
            // Saçak: 92 piksellik dalgalar; her dalganın altı yarım elips oyulur
            const double height = 70, tile = 92, rx = 46, ry = 18;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                double width = Math.Ceiling(size.Width / tile) * tile;
                g.BeginFigure(new Point(0, 0), true);
                g.LineTo(new Point(width, 0));
                g.LineTo(new Point(width, height));
                for (double x = width; x > 0; x -= tile)
                    g.ArcTo(new Point(x - tile, height), new Size(rx, ry), 0, false, SweepDirection.CounterClockwise);
                g.EndFigure(true);
            }
            Valance.Data = geometry;
        }

        /// <summary>Sahneyi kapalı perdeyle baştan oynatır.</summary>
        private void Play()
        {
            _opened?.TrySetResult();
            _opened = null;
            _openClock.Reset();
            _clock.Restart();
            Render(0, -1);
            Start();
        }

        private Task OpenAsync()
        {
            _opened?.TrySetResult();
            _opened = new TaskCompletionSource();
            _openClock.Restart();
            Start();
            return _opened.Task;
        }

        private void Start()
        {
            if (_running) return;
            _running = true;
            Frame(TimeSpan.Zero);
        }

        private void Frame(TimeSpan _)
        {
            var top = TopLevel.GetTopLevel(this);
            double open = _openClock.IsRunning ? _openClock.Elapsed.TotalMilliseconds : -1;
            Render(_clock.Elapsed.TotalMilliseconds, open);
            bool openDone = open >= 1100;
            if (openDone) _opened?.TrySetResult();
            // Perde açıldıysa ya da yazı bitip açılış bekleniyorsa kare istemeye gerek yok
            bool idle = open < 0 && _clock.ElapsedMilliseconds > 2300;
            if (openDone || idle || top == null || _vm is not { IsVisible: true })
            {
                _running = false;
                if (top == null || _vm is not { IsVisible: true }) _opened?.TrySetResult();
                return;
            }
            top.RequestAnimationFrame(Frame);
        }

        private static double Progress(double t, double start, double duration) => Math.Clamp((t - start) / duration, 0, 1);

        /// <param name="t">Sahnenin başından beri geçen süre (ms).</param>
        /// <param name="open">Perdeler açılmaya başladıysa o andan beri geçen süre, yoksa -1.</param>
        private void Render(double t, double open)
        {
            Blackout.Opacity = 1 - OutEase.Ease(Progress(t, 100, 550));
            for (int i = 0; i < _letters.Count; i++)
            {
                var (path, dash, fill) = _letters[i];
                path.StrokeDashOffset = dash * (1 - PenEase.Ease(Progress(t, 450 + i * 70, 500)));
                double ink = OutEase.Ease(Progress(t, 750 + i * 70, 400));
                fill.Opacity = ink;
                ((Brush)path.Stroke!).Opacity = 1 - 0.7 * ink;
            }
            Rise(Ornament, Progress(t, 1600, 450));
            Rise(GreetingText, Progress(t, 1750, 450));

            double width = Stage.Bounds.Width;
            double curtain = open < 0 ? 0 : CurtainEase.Ease(Progress(open, 0, 1100));
            Slide(CurtainLeft, -1, curtain, width);
            Slide(CurtainRight, 1, curtain, width);
            double valance = open < 0 ? 0 : ValanceEase.Ease(Progress(open, 350, 600));
            Valance.RenderTransform = new TranslateTransform(0, -70 * valance);
            double fade = open < 0 ? 0 : Progress(open, 0, 350);
            Title.Opacity = 1 - fade;
            Title.RenderTransform = new ScaleTransform(1 - 0.03 * fade, 1 - 0.03 * fade);
            Vignette.Opacity = 1 - (open < 0 ? 0 : Progress(open, 0, 500));
        }

        private static void Rise(Control control, double p)
        {
            control.Opacity = OutEase.Ease(p);
            control.RenderTransform = new TranslateTransform(0, 5 * (1 - OutEase.Ease(p)));
        }

        /// <summary>Perde kendi genişliği kadar dışarı kayarken dış kenarına doğru %85'e toplanır.</summary>
        private static void Slide(Border curtain, int direction, double p, double width)
        {
            double w = curtain.Bounds.Width > 0 ? curtain.Bounds.Width : width / 2;
            curtain.RenderTransformOrigin = new RelativePoint(direction < 0 ? 0 : 1, 0.5, RelativeUnit.Relative);
            curtain.RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(1 - 0.15 * p, 1), new TranslateTransform(direction * w * p, 0) },
            };
        }

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
                    while (k < data.Length && (char.IsDigit(data[k]) || (data[k] == '.' && !data[start..k].Contains('.')))) k++;
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
