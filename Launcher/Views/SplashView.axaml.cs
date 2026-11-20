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
            bool openDone = open >= (_grafit || _kut ? GrafitOpenMs : BurnMs);
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
        /// Kütüphane: raf yıkılır. Raf ve kitaplar belirir; raf titrer, ortadan kırılır (iki yarısı aşağı eğilir), kitaplar
        /// raftan boşalıp yerçekimiyle aşağı düşer, ardından rafın parçaları da düşer. Ekran sarsılır, her yerden toz kalkar;
        /// büyük toz bulutu dağılırken ortada simge, yazı ve karşılama belirir. Açılırken yazı ve sahne söner.
        /// </summary>
        private void RenderKutuphane(double t, double open)
        {
            if (_books.Count == 0) BuildBooks();
            double appear = OutEase.Ease(Progress(t, 60, 380));
            KutBooks.Opacity = appear;

            // Sarsıntı: kırılma anında güçlü, hızla söner; kırılmadan önce raf titrer
            double shake = 0;
            if (t > Crack - 260 && t < Crack) shake = 1.6 * Math.Sin(t * 0.25);
            else if (t >= Crack) shake = 9 * Math.Exp(-(t - Crack) / 170) * Math.Sin(t * 0.11);
            KutBooks.RenderTransform = new TranslateTransform(shake, Math.Abs(shake) * 0.4 + 10 * (1 - appear));
            KutDust.RenderTransform = new TranslateTransform(shake * 0.6, 0);

            // Raf yarıları: sol yarı sol ucu, sağ yarı sağ ucu çevresinde aşağı eğilir; sonra düşer
            double tilt = Math.Pow(Progress(t, Crack, 240), 2) * 34;   // derece
            PlaceHalf(_shelfLeft!, t, tilt, +1);
            PlaceHalf(_shelfRight!, t, tilt, -1);

            foreach (var b in _books)
            {
                double cx = b.X + b.W / 2, cy = ShelfY - b.H / 2;
                double release = Crack + b.Delay;
                double x = cx, y = cy, angle = 0;
                if (t >= release)
                {
                    double dt = (t - release) / 1000;
                    x = cx + b.Vx * dt;
                    y = cy + b.Vy * dt + 0.5 * Gravity * dt * dt;
                    angle = b.Spin * dt;
                }
                else if (t >= Crack)
                {
                    // Kırılan raf yarısıyla birlikte biraz eğilir, ortaya doğru kayar (bırakılana kadar)
                    double k = Progress(t, Crack, b.Delay);
                    bool left = cx < Mid;
                    angle = (left ? 1 : -1) * tilt * 0.6;
                    x = cx + (left ? 1 : -1) * 6 * k;
                    y = cy + Math.Abs(cx - (left ? ShelfLeft : ShelfLeft + ShelfWidth)) * Math.Sin(tilt * Math.PI / 180) * 0.55;
                }
                var g = (TransformGroup)b.Spine.RenderTransform!;
                ((RotateTransform)g.Children[0]).Angle = angle;
                ((TranslateTransform)g.Children[1]).X = x - cx;
                ((TranslateTransform)g.Children[1]).Y = y - cy;
            }

            // Toz: kırılma çevresinde ve kitapların düştüğü yolda bulutlar; büyür, yükselir, söner
            foreach (var d in _dust)
            {
                double p = Progress(t, Crack + d.Delay, d.Life);
                if (p <= 0 || p >= 1) { d.Puff.Opacity = 0; continue; }
                double size = d.Size0 + (d.Size1 - d.Size0) * OutEase.Ease(p);
                d.Puff.Width = d.Puff.Height = size;
                Canvas.SetLeft(d.Puff, d.X + d.Vx * p - size / 2);
                Canvas.SetTop(d.Puff, d.Y + d.Vy * p - size / 2);
                d.Puff.Opacity = d.Alpha * Math.Sin(p * Math.PI);
            }
            // Büyük toz dalgası ekranı kaplar ve dağılır: "etraf açılır"
            double wave = Progress(t, Crack + 380, 1150);
            KutWave.Opacity = wave <= 0 ? 0 : 0.85 * Math.Sin(Math.Min(1, wave * 1.15) * Math.PI);
            double ws = 0.4 + 1.8 * OutEase.Ease(wave);
            KutWave.RenderTransform = new ScaleTransform(ws, ws);

            double titleAt = Crack + 950;
            KutMark.Opacity = OutEase.Ease(Progress(t, titleAt, 400));
            double mark = 0.7 + 0.3 * OutEase.Ease(Progress(t, titleAt, 400));
            KutMark.RenderTransform = new ScaleTransform(mark, mark);
            Rise(KutWord, Progress(t, titleAt + 80, 450));
            Rise(KutGreeting, Progress(t, titleAt + 450, 450));
            if (open < 0) { KutStage.Opacity = 1; KutTitle.Opacity = 1; return; }
            KutTitle.Opacity = 1 - OutEase.Ease(Progress(open, 0, 300));
            KutStage.Opacity = 1 - OutEase.Ease(Progress(open, 150, GrafitOpenMs - 150));
        }

        private void PlaceHalf(Border half, double t, double tilt, int side)
        {
            // Yarı, dış ucu çevresinde eğilir; 0,45 sn sonra kopup düşer
            double fall = Math.Max(0, (t - Crack - 450) / 1000);
            var g = (TransformGroup)half.RenderTransform!;
            ((RotateTransform)g.Children[0]).Angle = side * tilt + side * 40 * fall;
            ((TranslateTransform)g.Children[1]).Y = 0.5 * Gravity * fall * fall;
        }

        private const double Crack = 720, Gravity = 2600, ShelfY = 420, ShelfLeft = 390, ShelfWidth = 540, Mid = ShelfLeft + ShelfWidth / 2;
        private sealed record Book(Border Spine, double X, double W, double H, double Delay, double Vx, double Vy, double Spin);
        private sealed record Dust(Ellipse Puff, double X, double Y, double Vx, double Vy, double Size0, double Size1, double Delay, double Life, double Alpha);
        private readonly List<Book> _books = new();
        private readonly List<Dust> _dust = new();
        private Border? _shelfLeft, _shelfRight;
        private Ellipse KutWave = new();

        private void BuildBooks()
        {
            var rng = new Random(11);
            string[] colors = { "#7A4A22", "#5C3A1E", "#9C6B30", "#3F2B1C", "#B07A35", "#6E4B2A", "#8C3F2A", "#2F4A44", "#4A3322", "#A85F28", "#584030", "#7E5A2E" };
            KutBooks.Children.Clear();
            KutDust.Children.Clear();
            _books.Clear();
            _dust.Clear();

            Border Half(double left, double width, RelativePoint origin) => new()
            {
                Width = width, Height = 9, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.Parse("#4A3523")),
                BorderBrush = new SolidColorBrush(Color.Parse("#6B4D33")), BorderThickness = new Thickness(0, 1, 0, 0),
                RenderTransformOrigin = origin, RenderTransform = new TransformGroup { Children = { new RotateTransform(0), new TranslateTransform() } },
                [Canvas.LeftProperty] = left, [Canvas.TopProperty] = ShelfY,
            };
            _shelfLeft = Half(ShelfLeft, ShelfWidth / 2, new RelativePoint(0, 0.5, RelativeUnit.Relative));
            _shelfRight = Half(Mid, ShelfWidth / 2, new RelativePoint(1, 0.5, RelativeUnit.Relative));

            double x = ShelfLeft + 16;
            while (x < ShelfLeft + ShelfWidth - 30)
            {
                double w = 20 + rng.Next(0, 15), h = 96 + rng.Next(0, 52);
                if (x + w > ShelfLeft + ShelfWidth - 14) break;
                var color = Color.Parse(colors[_books.Count % colors.Length]);
                var lighter = Color.FromRgb((byte)Math.Min(255, color.R + 40), (byte)Math.Min(255, color.G + 30), (byte)Math.Min(255, color.B + 20));
                var spine = new Grid { RowDefinitions = new RowDefinitions("10,*,6,10") };
                spine.Children.Add(new Border { [Grid.RowProperty] = 2, Background = new SolidColorBrush(Color.Parse("#66F2A33A")), Margin = new Thickness(3, 0) });
                var book = new Border
                {
                    Width = w, Height = h, CornerRadius = new CornerRadius(3, 3, 1, 1),
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(lighter, 0), new GradientStop(color, 0.35), new GradientStop(color, 0.8), new GradientStop(Color.FromRgb((byte)(color.R * 0.7), (byte)(color.G * 0.7), (byte)(color.B * 0.7)), 1) },
                    },
                    Child = spine,
                    RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    RenderTransform = new TransformGroup { Children = { new RotateTransform(0), new TranslateTransform() } },
                    [Canvas.LeftProperty] = x, [Canvas.TopProperty] = ShelfY - h,
                };
                KutBooks.Children.Add(book);
                // Ortadakiler önce boşalır, kenarlara doğru sırayla; aşağı düşer, hafifçe döner ve biraz savrulur
                double fromMid = Math.Abs(x + w / 2 - Mid) / (ShelfWidth / 2);
                double side = x + w / 2 < Mid ? 1 : -1;
                _books.Add(new Book(book, x, w, h, 60 + fromMid * 260 + rng.Next(0, 60),
                    side * (40 + rng.Next(0, 120)) + rng.Next(-40, 40), -120 - rng.Next(0, 160), (rng.NextDouble() - 0.5) * 520));
                x += w + 3 + rng.Next(0, 3);
            }
            KutBooks.Children.Add(_shelfLeft);
            KutBooks.Children.Add(_shelfRight);

            // Toz bulutları: sıcak gri, yumuşak kenarlı
            IBrush Puff(byte a) => new RadialGradientBrush
            {
                GradientStops = { new GradientStop(Color.FromArgb(a, 0xB0, 0x9C, 0x84), 0), new GradientStop(Color.FromArgb((byte)(a / 3), 0x8C, 0x7A, 0x66), 0.55), new GradientStop(Color.FromArgb(0, 0x8C, 0x7A, 0x66), 1) },
            };
            for (int i = 0; i < 46; i++)
            {
                bool burst = i < 26;   // ilk grup kırılma noktasında patlar, kalanı düşüş yolunda
                double px = burst ? Mid + rng.Next(-90, 90) : ShelfLeft + rng.NextDouble() * ShelfWidth;
                double py = burst ? ShelfY + rng.Next(-30, 20) : ShelfY + rng.Next(40, 360);
                var e = new Ellipse { Fill = Puff(200), Opacity = 0 };
                KutDust.Children.Add(e);
                _dust.Add(new Dust(e, px, py, rng.Next(-160, 160), -rng.Next(20, 140), 20 + rng.Next(0, 30), 140 + rng.Next(0, 160),
                    burst ? rng.Next(0, 160) : 150 + rng.Next(0, 450), 900 + rng.Next(0, 600), 0.45 + rng.NextDouble() * 0.35));
            }
            // Ekranı kaplayan büyük dalga
            KutWave = new Ellipse
            {
                Width = 1000, Height = 1000, Opacity = 0, Fill = Puff(150),
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                [Canvas.LeftProperty] = Mid - 500, [Canvas.TopProperty] = ShelfY - 500,
            };
            KutDust.Children.Add(KutWave);
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
