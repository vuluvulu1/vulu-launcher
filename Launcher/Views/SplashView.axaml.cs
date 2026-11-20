using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Meşale açılışı (web: css/mesale.css ve js/mesale.js). Zamanlama (~2,4 sn): kor ışığı yanar, harfler sırayla
    /// akkor beyazdan ambere tutuşurken alev yazı boyunca kıvılcım saçar, karşılama belirir; ışık titremeye devam eder.
    /// Açılırken karanlık yazının ortasından dışa doğru 1,5 sn'de yanar, cephe boyunca kıvılcımlar uçar.
    /// Kareler pencerenin çizim döngüsüne bağlıdır (RequestAnimationFrame).
    /// </summary>
    internal sealed partial class SplashView : UserControl
    {
        private const double BurnMs = 1500;
        private static readonly Easing OutEase = new CubicEaseOut();
        // Kor ışığının titremesi (web: m-flicker, 1,7 sn)
        private static readonly (double At, double Value)[] Flicker = { (0, 1), (.22, .78), (.41, .95), (.63, .7), (.82, .92), (1, 1) };

        private readonly Stopwatch _clock = new();
        private readonly Stopwatch _openClock = new();
        private SplashViewModel? _vm;
        private TaskCompletionSource? _opened;
        private bool _running;
        private double _last;
        /// <summary>Bu oynatma Ufkun öbür ucu açılışı mı (tema açılışta bir kez okunur).</summary>
        private bool _ufuk;
        /// <summary>Bu oynatma Grafit açılışı mı.</summary>
        private bool _grafit;
        /// <summary>Grafit açılışının kapanışı (ms): kartlar sönünce altta gerçek kartlar kalır.</summary>
        private const double GrafitOpenMs = 650;
        /// <summary>Bu oynatma Kütüphane açılışı mı.</summary>
        private bool _kut;

        public SplashView()
        {
            InitializeComponent();
            // Sahne başlık çubuğunu da kaplar; pencere yine de buradan sürüklenebilir
            Stage.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(this) is Window window) window.BeginMoveDrag(e);
            };
            DataContextChanged += (_, _) =>
            {
                if (_vm != null) { _vm.Played -= Play; _vm.Opener = null; }
                _vm = DataContext as SplashViewModel;
                if (_vm == null) return;
                _vm.Played += Play;
                _vm.Opener = OpenAsync;
            };
        }

        /// <summary>Sahneyi karanlıktan baştan oynatır.</summary>
        private void Play()
        {
            _opened?.TrySetResult();
            _opened = null;
            _openClock.Reset();
            Sparks.Clear();
            // Sahnesi olan temalarda (Ufkun öbür ucu, Derin Karanlık) sahneli açılış; yazının rengi temaya göre
            _ufuk = Themes.HasScene;
            if (_ufuk) StyleSceneTitle();
            UfukStage.IsVisible = _ufuk;
            _grafit = Themes.IsGrafit;
            GrafitStage.IsVisible = _grafit;
            GrafitStage.Opacity = 1;
            _kut = Themes.IsKutuphane;
            KutStage.IsVisible = _kut;
            KutStage.Opacity = 1;
            _kutBook = KutSplashChoice() != "signature";
            KutBookStage.IsVisible = _kutBook;
            KutBookStage.Opacity = KutBg.Opacity = 1;
            KutBookStage.RenderTransform = null;
            KutSignatureBox.IsVisible = KutGlow.IsVisible = !_kutBook;
            KutTitle.VerticalAlignment = _kutBook ? Avalonia.Layout.VerticalAlignment.Bottom : Avalonia.Layout.VerticalAlignment.Center;
            KutTitle.Margin = _kutBook ? new Thickness(0, 0, 0, 56) : default;
            KutRush.Update(-1, default);
            UfukStage.Opacity = 1;
            if (_ufuk)
            {
                if (_vm is { Holding: true }) UfukScene.HoldSplash();
                else UfukScene.StartSplash();
            }
            Sheet.IsVisible = Glow.IsVisible = Title.IsVisible = Sparks.IsVisible = !_ufuk && !_grafit && !_kut;
            _clock.Restart();
            _last = 0;
            Render(0, -1, 0);
            Start();
        }

        private Task OpenAsync()
        {
            _opened?.TrySetResult();
            _opened = new TaskCompletionSource();
            _openClock.Restart();
            if (_ufuk) UfukScene.OpenSplash();
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
            if (_vm is { Holding: true }) _clock.Restart();   // perde arkasında ana ekran kuruluyor: ilk karede bekle
            double now = _clock.Elapsed.TotalMilliseconds;
            double dt = Math.Min(50, now - _last);
            _last = now;
            double open = _openClock.IsRunning ? _openClock.Elapsed.TotalMilliseconds : -1;
            Render(now, open, dt);
            bool openDone = open >= (_kut && _kutBook ? KutRush.Total : _grafit || _kut ? GrafitOpenMs : BurnMs);
            if (openDone || top == null || _vm is not { IsVisible: true })
            {
                _running = false;
                _opened?.TrySetResult();
                return;
            }
            top.RequestAnimationFrame(Frame);
        }

        private static double Progress(double t, double start, double duration) => Math.Clamp((t - start) / duration, 0, 1);

        /// <summary>Cephenin yarıçapı (pencere yüksekliği cinsinden): yavaş tutuşur, hızlanarak yayılır.</summary>
        private double BurnFront(double progress)
        {
            double w = Math.Max(1, Stage.Bounds.Width), h = Math.Max(1, Stage.Bounds.Height);
            double end = Math.Sqrt(Math.Pow(w / h * .42, 2) + .85 * .85) + .4;
            return -.22 + Math.Pow(progress, 1.7) * (end + .22);
        }

        /// <param name="t">Sahnenin başından beri geçen süre (ms).</param>
        /// <param name="open">Açılış başladıysa o andan beri geçen süre, yoksa -1.</param>
        /// <param name="dt">Önceki kareden beri geçen süre (ms).</param>
        private void Render(double t, double open, double dt)
        {
            if (_ufuk) { RenderUfuk(t, open, dt); return; }
            if (_grafit) { RenderGrafit(t, open); return; }
            if (_kut) { RenderKutuphane(t, open); return; }
            // Yazının yeri (sahneye göre)
            var size = Letters.Bounds.Size;
            var center = Letters.TranslatePoint(new Point(size.Width / 2, size.Height / 2), Stage) ?? new Point(Stage.Bounds.Width / 2, Stage.Bounds.Height / 2);
            var topLeft = Letters.TranslatePoint(new Point(0, 0), Stage) ?? center;
            double h = Math.Max(1, Stage.Bounds.Height);

            Letters.RenderEmber(t - 300);
            Rise(GreetingText, Progress(t, 1650, 450));

            // Kor ışığı: 0,3 sn'de yanmaya başlar, 1,9 sn'den sonra titrer; açılınca söner
            if (open >= 0) Glow.Opacity = 0;
            else if (t < 1900) Glow.Opacity = OutEase.Ease(Progress(t, 300, 1600));
            else
            {
                double f = ((t - 1900) % 1700) / 1700;
                int k = 1;
                while (k < Flicker.Length - 1 && f > Flicker[k].At) k++;
                var (a, va) = Flicker[k - 1];
                var (b, vb) = Flicker[k];
                Glow.Opacity = va + (vb - va) * ((f - a) / (b - a));
            }

            double progress = open < 0 ? 0 : Math.Min(1, open / BurnMs);
            double front = BurnFront(progress);
            Sheet.Word = center;
            Sheet.Front = open < 0 ? null : front;
            Sheet.Time = t / 1000;
            Sheet.InvalidateVisual();

            // Yazı, yanma altından çıkana kadar durur (web: .open .stage-title, 0,25 sn gecikmeli)
            double fade = open < 0 ? 0 : Progress(open, 250, 350);
            Title.Opacity = 1 - fade;
            double scale = 1 - 0.03 * (open < 0 ? 0 : Progress(open, 250, 500));
            Title.RenderTransform = new ScaleTransform(scale, scale);

            // Kıvılcımlar: önce alev yazı boyunca yürür, sonra arada bir tek tük; açılınca cephe boyunca
            double age = t / 1000;
            if (open < 0 && age > .25 && age < 1.5)
            {
                double x = topLeft.X + size.Width * Math.Min(1, (age - .25) / 1.05);
                for (int i = 0; i < 2; i++) Sparks.Add(x, center.Y + (Sparks.Next() - .5) * size.Height * .6, .1, .16);
            }
            else if (open < 0 && Sparks.Next() < .12) Sparks.Add(topLeft.X + Sparks.Next() * size.Width, center.Y, .06, .07);
            if (open >= 0 && progress < .9 && front > 0)
            {
                for (int i = 0; i < 7; i++)
                {
                    double angle = Sparks.Next() * Math.PI * 2;
                    Sparks.Add(center.X + Math.Cos(angle) * front / .55 * h, center.Y + Math.Sin(angle) * front / 1.25 * h, .22, .3);
                }
            }
            Sparks.Step(dt);
        }

        /// <summary>
        /// Ufkun öbür ucu: 0,9 sn'de karanlıktan açılır, güneş 1,6 sn'de doğar, yazılar sırayla belirir. Açılırken ızgara
        /// hızlanır, sahne beyaza doğru parlar ve söner; arkadaki launcher görünür.
        /// </summary>
        private void RenderUfuk(double t, double open, double dt)
        {
            // Sahnenin kendisi (zaman, doğuş, parlama, saydamlaşma) çizim iş parçacığında hesaplanır
            double p = open < 0 ? 0 : Math.Min(1, open / BurnMs);
            Rise(UfukTop, Progress(t, 500, 500));
            double word = OutEase.Ease(Progress(t, 650, 700));
            UfukWord.Opacity = word;
            double s = 1.12 - 0.12 * word;
            UfukWord.RenderTransform = new ScaleTransform(s, s);
            Rise(UfukGreeting, Progress(t, 1650, 450));

            // Açılış: yazı öne doğru büyüyüp söner, sahne parlamanın ardından saydamlaşır
            double grow = 1 + 0.35 * OutEase.Ease(p);
            UfukTitle.RenderTransform = new ScaleTransform(grow, grow);
            UfukTitle.Opacity = 1 - Progress(open < 0 ? 0 : open, 150, 500);
        }

        /// <summary>
        /// Grafit: kartlar 0,15 sn arayla, 0,45 sn'de aşağıdan yerine oturur; ardından simge ve yazı, sonra karşılama.
        /// Açılırken yazı ve kartlar 0,65 sn'de söner (zemin en son), altta gerçek arayüz aynı yerde durur.
        /// </summary>
        private void RenderGrafit(double t, double open)
        {
            Settle(GrafitLeft, Progress(t, 150, 450));
            Settle(GrafitCenter, Progress(t, 300, 450));
            Settle(GrafitRight, Progress(t, 450, 450));
            GrafitMark.Opacity = OutEase.Ease(Progress(t, 850, 400));
            double mark = 0.8 + 0.2 * OutEase.Ease(Progress(t, 850, 400));
            GrafitMark.RenderTransform = new ScaleTransform(mark, mark);
            Rise(GrafitWord, Progress(t, 950, 450));
            Rise(GrafitGreeting, Progress(t, 1650, 450));
            if (open < 0) { GrafitStage.Opacity = 1; GrafitTitle.Opacity = 1; return; }
            GrafitTitle.Opacity = 1 - OutEase.Ease(Progress(open, 0, 300));
            GrafitStage.Opacity = 1 - OutEase.Ease(Progress(open, 150, GrafitOpenMs - 150));
        }

        /// <summary>
        /// Kütüphane: Meşale'deki el yazısı başlık, ama tutuşmaz: harfler sırayla ince kalemle yazılıp mürekkeple dolar,
        /// altına sağdan sola bir kuyruk çekilir, arkada sıcak bir ışık belirir; sonra karşılama. Açılırken söner.
        /// </summary>
        private void RenderKutuphane(double t, double open)
        {
            if (_kutBook) { RenderKutBook(t, open); return; }
            const double writeAt = 320, tailAt = writeAt + VuluShared.ScriptTitle.Duration - 250, tailMs = 520;
            KutLetters.Render(t - writeAt);
            KutGlow.Opacity = OutEase.Ease(Progress(t, writeAt, 1400));
            if (_tailLength == 0) _tailLength = TailLength();
            double dash = _tailLength / KutTail.StrokeThickness + 1;
            if (KutTail.StrokeDashArray is not { Count: 2 }) KutTail.StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { dash, dash };
            KutTail.StrokeDashOffset = dash * (1 - TailEase.Ease(Progress(t, tailAt, tailMs)));
            Rise(KutGreeting, Progress(t, tailAt + 250, 500));
            if (open < 0) { KutStage.Opacity = 1; KutTitle.Opacity = 1; return; }
            KutTitle.Opacity = 1 - OutEase.Ease(Progress(open, 0, 300));
            KutStage.Opacity = 1 - OutEase.Ease(Progress(open, 150, GrafitOpenMs - 150));
        }

        private bool _kutBook, _kutInk;

        /// <summary>Kütüphane açılışı seçimi: ayar; geliştirmede VULU_KUT_SPLASH=book|signature ile denenebilir.</summary>
        private static string KutSplashChoice()
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("VULU_KUT_SPLASH") is { Length: > 0 } env) return env;
#endif
            return AppSettings.Current.KutuphaneSplash;
        }

        /// <summary>
        /// Kitap açılışı: kitap açılır, sayfalar çevrilir, sağ sayfaya el yazısıyla "vulu Launcher" (kahverengi mürekkep)
        /// yazılır. Açılırken kamera yazının içine dalar (sahne üstel büyür), hız çizgileri ve kâğıt (KutRush) gelir;
        /// kâğıt ekranı doldurunca kitap gizlenir ve kâğıdın ortası açılarak launcher görünür.
        /// </summary>
        private void RenderKutBook(double t, double open)
        {
            if (!_kutInk)
            {
                _kutInk = true;
                KutPageLetters.SetInk(Color.Parse("#6B4A2E"), Color.Parse("#4A2E18"), Color.Parse("#2E1A0C"), Color.Parse("#5A3A20"));
            }
            KutBook.Update(t);
            var page = KutBook.PageRect;
            double letterH = page.Width * 1182 / 4401;
            KutPageLetters.Width = page.Width;
            KutPageLetters.Margin = new Thickness(page.X, page.Y + (page.Height - letterH) / 2, 0, 0);
            const double speed = 1.5;
            KutPageLetters.Render((t - KutBook.WriteAt) * speed);
            Rise(KutGreeting, Progress(t, KutBook.WriteAt + VuluShared.ScriptTitle.Duration / speed - 100, 500));
            var focus = new Point(page.X + page.Width * 0.5, page.Y + page.Height / 2);
            if (open < 0)
            {
                KutBookStage.RenderTransform = null;
                KutStage.Opacity = KutTitle.Opacity = KutBookStage.Opacity = KutBg.Opacity = 1;
                KutRush.Update(-1, focus);
                return;
            }
            double z = Math.Clamp(open / KutRush.ZoomMs, 0, 1);
            double scale = Math.Exp(Math.Log(30) * z * z * z);
            KutBookStage.RenderTransform = new MatrixTransform(Matrix.CreateTranslation(-focus.X, -focus.Y) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(focus.X, focus.Y));
            KutTitle.Opacity = 1 - OutEase.Ease(Progress(open, 0, 220));
            KutRush.Update(open, focus);
            bool paperFull = open >= KutRush.PaperFull;
            KutBookStage.Opacity = KutBg.Opacity = paperFull ? 0 : 1;
            KutStage.Opacity = 1;
        }

        private static readonly Avalonia.Animation.Easings.Easing TailEase = new Avalonia.Animation.Easings.SplineEasing(0.3, 0, 0.2, 1);
        private double _tailLength;

        /// <summary>Kuyruk çizgisinin yaklaşık boyu (kesik çizgiyle çizilerek açılması için).</summary>
        private double TailLength()
        {
            // Data'daki iki kübik eğri örneklenir
            (double X, double Y)[][] curves =
            {
                new[] { (548.0, 112.0), (590.0, 150.0), (330.0, 126.0), (170.0, 140.0) },
                new[] { (170.0, 140.0), (110.0, 145.0), (60.0, 150.0), (30.0, 146.0) },
            };
            double len = 0;
            foreach (var c in curves)
            {
                (double X, double Y) prev = c[0];
                for (int i = 1; i <= 60; i++)
                {
                    double u = i / 60.0, v = 1 - u;
                    double x = v * v * v * c[0].X + 3 * v * v * u * c[1].X + 3 * v * u * u * c[2].X + u * u * u * c[3].X;
                    double y = v * v * v * c[0].Y + 3 * v * v * u * c[1].Y + 3 * v * u * u * c[2].Y + u * u * u * c[3].Y;
                    len += Math.Sqrt((x - prev.X) * (x - prev.X) + (y - prev.Y) * (y - prev.Y));
                    prev = (x, y);
                }
            }
            return len;
        }

        private static void Settle(Control control, double p)
        {
            double e = OutEase.Ease(p);
            control.Opacity = e;
            control.RenderTransform = new TranslateTransform(0, 18 * (1 - e));
        }

        private IBrush? _ufukWordBrush;
        private Avalonia.Media.IEffect? _ufukWordEffect;
        private string? _ufukTopText;
        private IBrush? _ufukTopBrush;

        /// <summary>Açılış yazısı: Ufuk'ta krom (axaml'daki), Derin Karanlık'ta beyazdan camgöbeğine biyolüminesans.</summary>
        private void StyleSceneTitle()
        {
            _ufukWordBrush ??= UfukWord.Foreground;
            _ufukWordEffect ??= UfukWord.Effect;
            _ufukTopText ??= UfukTop.Text;
            _ufukTopBrush ??= UfukTop.Foreground;
            if (Themes.IsDerin)
            {
                UfukWord.Foreground = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#F2FFFD"), 0), new GradientStop(Color.Parse("#8FF7EE"), 0.55), new GradientStop(Color.Parse("#2FE6D8"), 1) },
                };
                UfukWord.Effect = new DropShadowEffect { OffsetX = 0, OffsetY = 0, BlurRadius = 26, Color = Color.Parse("#2FE6D8"), Opacity = 0.85 };
                UfukTop.IsVisible = false;   // üst yazı Ufkun öbür ucu'na özgü
            }
            else
            {
                UfukWord.Foreground = _ufukWordBrush;
                UfukWord.Effect = _ufukWordEffect;
                UfukTop.IsVisible = true;
                UfukTop.Text = _ufukTopText;
                UfukTop.Foreground = _ufukTopBrush;
            }
        }

        private static void Rise(Control control, double p)
        {
            control.Opacity = OutEase.Ease(p);
            control.RenderTransform = new TranslateTransform(0, 5 * (1 - OutEase.Ease(p)));
        }
    }
}
