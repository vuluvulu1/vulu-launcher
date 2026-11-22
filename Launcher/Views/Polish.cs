using System.Diagnostics;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Boş durum (web: .empty-state): kesik çizgili çerçeve içinde yuvarlak simge, başlık ve kısa açıklama.
    /// ActionText verilirse altta bir düğme çıkar (ör. hata ekranında "Tekrar dene").
    /// </summary>
    internal sealed class EmptyState : Panel
    {
        public static readonly StyledProperty<string> IconProperty = AvaloniaProperty.Register<EmptyState, string>(nameof(Icon), "");
        public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<EmptyState, string>(nameof(Title), "");
        public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<EmptyState, string>(nameof(Text), "");
        public static readonly StyledProperty<bool> DashedProperty = AvaloniaProperty.Register<EmptyState, bool>(nameof(Dashed), true);
        public static readonly StyledProperty<string> ActionTextProperty = AvaloniaProperty.Register<EmptyState, string>(nameof(ActionText), "");
        public static readonly StyledProperty<System.Windows.Input.ICommand?> CommandProperty = AvaloniaProperty.Register<EmptyState, System.Windows.Input.ICommand?>(nameof(Command));

        private readonly TextBlock _icon, _title, _text;
        private readonly Button _action;
        private readonly Rectangle _frame;

        public EmptyState()
        {
            MaxWidth = 440;
            HorizontalAlignment = HorizontalAlignment.Center;
            _frame = new Rectangle
            {
                Stroke = Brush("LineStrong"),
                StrokeThickness = 1,
                StrokeDashArray = new AvaloniaList<double> { 4, 3 },
                RadiusX = 14,
                RadiusY = 14,
            };
            _icon = new TextBlock { FontSize = 22, Foreground = Brush("AccentHi"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, IsVisible = false };
            _text = new TextBlock { FontSize = 13, LineHeight = 19.5, Foreground = Brush("Muted"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            _action = new Button { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(16, 6), IsVisible = false };
            Children.Add(_frame);
            Children.Add(new StackPanel
            {
                Margin = new Thickness(24, 28),
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = Brush("AccentSoft"), HorizontalAlignment = HorizontalAlignment.Center, Child = _icon },
                    _title,
                    _text,
                    _action,
                },
            });
        }

        public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public bool Dashed { get => GetValue(DashedProperty); set => SetValue(DashedProperty, value); }
        public string ActionText { get => GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
        public System.Windows.Input.ICommand? Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IconProperty) _icon.Text = Icon;
            else if (change.Property == TitleProperty) { _title.Text = Title; _title.IsVisible = Title.Length > 0; }
            else if (change.Property == TextProperty) { _text.Text = Text; _text.IsVisible = Text.Length > 0; }
            else if (change.Property == DashedProperty) _frame.IsVisible = Dashed;
            else if (change.Property == ActionTextProperty) { _action.Content = ActionText; _action.IsVisible = ActionText.Length > 0; }
            else if (change.Property == CommandProperty) _action.Command = Command;
        }

        private static IBrush? Brush(string key) =>
            Application.Current?.TryFindResource(key, out var value) == true ? value as IBrush : null;
    }

    /// <summary>Kendi kendine canlanan küçük çizim: görünürken her karede yeniden çizilir.</summary>
    internal abstract class Animated : Control
    {
        protected readonly Stopwatch Clock = Stopwatch.StartNew();
        private bool _animating;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Animate();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && IsVisible) Animate();
        }

        private void Animate()
        {
            if (_animating) return;
            _animating = true;
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Frame);
        }

        private void Frame(TimeSpan _)
        {
            _animating = false;
            if (TopLevel.GetTopLevel(this) == null) return;
            // Sayfalar artık saklanıyor (gizli sekme): görünmezken çizmez, yarım saniyede bir yeniden görünür mü bakar
            if (!IsEffectivelyVisible)
            {
                if (IsVisible) { _animating = true; DispatcherTimer.RunOnce(() => { _animating = false; Animate(); }, TimeSpan.FromMilliseconds(500)); }
                return;
            }
            InvalidateVisual();
            Animate();
        }
    }

    /// <summary>Nabız gibi atan kırmızı canlı noktası (web: .live-dot, 1,8 sn).</summary>
    internal sealed class LiveDot : Animated
    {
        public LiveDot() { Width = 8; Height = 8; ClipToBounds = false; }

        public override void Render(DrawingContext context)
        {
            var center = new Point(4, 4);
            double t = (Clock.Elapsed.TotalMilliseconds % 1800) / 1800;
            double eased = 1 - Math.Pow(1 - t, 3);   // ease-out
            // Halka 9 piksele kadar büyürken söner
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(140 * (1 - eased)), 226, 96, 74)), null, center, 4 + 9 * eased, 4 + 9 * eased);
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#E2604A")), null, center, 4, 4);
        }
    }

    /// <summary>Radyo çalarken zıplayan üç ses çubuğu (web: .music-eq, 0,9 sn).</summary>
    internal sealed class EqBars : Animated
    {
        public EqBars() { Width = 13; Height = 14; }

        /// <summary>Temanın açık vurgu rengi (Meşale'de amber, Ufkun öbür ucu'nda camgöbeği).</summary>
        private static IBrush Bar => Application.Current?.TryGetResource("AccentHi", null, out var b) == true && b is IBrush brush
            ? brush : Brushes.Orange;

        public override void Render(DrawingContext context)
        {
            double ms = Clock.Elapsed.TotalMilliseconds;
            for (int i = 0; i < 3; i++)
            {
                double phase = ((ms + i * 300) % 900) / 900;
                double level = .3 + .7 * (.5 - .5 * Math.Cos(phase * 2 * Math.PI));   // %30 ↔ %100, ease-in-out
                double h = 14 * level;
                context.FillRectangle(Bar, new Rect(i * 5, 14 - h, 3, h), 1);
            }
        }
    }

    /// <summary>
    /// Yüklenirken yer tutan iskelet. Düz bir kutu değil, gelecek içeriğin şeklini taşır (Kind): kart (resim, başlık,
    /// satırlar, etiketler), ekran görüntüsü, sunucu, profil, kişi satırı, küçük kutu. Şekillerin üstünden çapraz,
    /// yumuşak bir ışık geçer. Hızlı yüklemelerde göz kırpmasın diye kısa bir beklemeden sonra yavaşça belirir.
    /// Çerçeve ve köşeler temanın kartından gelir: XAML'da Border.card içine konur.
    /// </summary>
    internal sealed class Skeleton : Control
    {
        public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<Skeleton, string>(nameof(Kind), "box");
        /// <summary>box, card, shot, server, profile, row, tile</summary>
        public string Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

        /// <summary>Temanın yükseltilmiş yüzey rengi (Meşale'de kahve, Ufkun öbür ucu'nda mor).</summary>
        private static Color Base => Application.Current?.TryGetResource("Raised", null, out var b) == true && b is SolidColorBrush s
            ? Color.FromRgb(s.Color.R, s.Color.G, s.Color.B) : Color.Parse("#281F17");
        private static Color Lighten(Color c, int by) =>
            Color.FromRgb((byte)Math.Min(255, c.R + by), (byte)Math.Min(255, c.G + by), (byte)Math.Min(255, c.B + by));

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _shownAt;
        private bool _animating, _wasVisible;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _wasVisible = false;
            Animate();
        }

        private void Animate()
        {
            if (_animating) return;
            _animating = true;
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Frame);
        }

        private void Frame(TimeSpan _)
        {
            _animating = false;
            if (TopLevel.GetTopLevel(this) == null) return;
            // Sayfalar saklanıyor (gizli sekme): görünmezken çizmez, yarım saniyede bir yeniden görünür mü bakar
            if (!IsEffectivelyVisible)
            {
                _wasVisible = false;
                if (IsVisible) { _animating = true; DispatcherTimer.RunOnce(() => { _animating = false; Animate(); }, TimeSpan.FromMilliseconds(500)); }
                return;
            }
            if (!_wasVisible) { _wasVisible = true; _shownAt = _clock.ElapsedMilliseconds; }
            InvalidateVisual();
            Animate();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && IsVisible) { _wasVisible = false; Animate(); }
        }

        /// <summary>Şekiller: dikdörtgen (köşe yarıçapıyla) ya da daire (yarıçap -1).</summary>
        private static IEnumerable<(Rect Box, double Radius)> Shapes(string kind, double w, double h)
        {
            switch (kind)
            {
                case "card":   // Keşfet › paket kartı: kapak, ad, bilgi, iki satır açıklama, etiketler
                {
                    double img = Math.Min(150, h * 0.52);
                    yield return (new Rect(0, 0, w, img), 0);
                    double y = img + 16;
                    yield return (new Rect(14, y, w * 0.62, 14), 4);
                    yield return (new Rect(14, y + 24, w * 0.36, 9), 4);
                    yield return (new Rect(14, y + 44, w - 28, 9), 4);
                    yield return (new Rect(14, y + 59, w * 0.72, 9), 4);
                    if (y + 104 < h) { yield return (new Rect(14, y + 78, 54, 18), 9); yield return (new Rect(74, y + 78, 66, 18), 9); }
                    break;
                }
                case "shot":   // Akış › ekran görüntüsü: resim, paylaşanın resmi ve adı, beğeni
                {
                    double img = h - 54;
                    yield return (new Rect(0, 0, w, img), 0);
                    yield return (new Rect(14, img + 14, 26, 26), -1);
                    yield return (new Rect(50, img + 16, w * 0.36, 10), 4);
                    yield return (new Rect(50, img + 31, w * 0.2, 8), 4);
                    yield return (new Rect(w - 58, img + 18, 44, 18), 9);
                    break;
                }
                case "server":   // Sunucular: simge, ad, durum satırları, sağda düğme, altta oyuncular
                    yield return (new Rect(18, 18, 64, 64), 8);
                    yield return (new Rect(98, 24, Math.Min(260, w * 0.32), 14), 4);
                    yield return (new Rect(98, 48, Math.Min(380, w * 0.46), 9), 4);
                    yield return (new Rect(98, 64, Math.Min(220, w * 0.26), 9), 4);
                    yield return (new Rect(w - 118, 26, 100, 30), 6);
                    for (int i = 0; i < 4 && h > 120; i++) yield return (new Rect(18 + i * 34, h - 46, 28, 28), -1);
                    break;
                case "profile":   // Profil: kapak, büyük resim, ad, satırlar, istatistik kutuları
                {
                    double banner = Math.Min(130, h * 0.3);
                    yield return (new Rect(0, 0, w, banner), 0);
                    yield return (new Rect(28, banner - 44, 96, 96), -1);
                    yield return (new Rect(140, banner + 12, Math.Min(240, w * 0.34), 18), 4);
                    yield return (new Rect(140, banner + 40, Math.Min(150, w * 0.22), 10), 4);
                    double boxW = (w - 56 - 24) / 3, by = banner + 80;
                    for (int i = 0; i < 3; i++) yield return (new Rect(28 + i * (boxW + 12), by, boxW, 64), 6);
                    for (int i = 0; i < 3 && by + 100 + i * 20 < h; i++) yield return (new Rect(28, by + 92 + i * 20, (w - 56) * (i == 2 ? 0.6 : 1), 9), 4);
                    break;
                }
                case "row":   // Kişi satırı: resim ve iki satır
                    yield return (new Rect(12, (h - 32) / 2, 32, 32), -1);
                    yield return (new Rect(54, h / 2 - 10, w * 0.45, 9), 4);
                    yield return (new Rect(54, h / 2 + 4, w * 0.28, 7), 4);
                    break;
                case "tile":   // Admin özet kutusu: başlık, büyük sayı, alt satır
                    yield return (new Rect(16, 18, w * 0.45, 10), 4);
                    yield return (new Rect(16, 40, w * 0.3, 28), 6);
                    yield return (new Rect(16, h - 30, w * 0.6, 9), 4);
                    break;
                default:
                    yield return (new Rect(0, 0, w, h), 6);
                    break;
            }
        }

        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || h <= 0) return;
            long ms = _clock.ElapsedMilliseconds;
            // Belirme: 120 ms bekler, 260 ms'de görünür olur (hızlı yüklemede hiç görünmez)
            double appear = Math.Clamp((ms - _shownAt - 120) / 260.0, 0, 1);
            if (appear <= 0) return;
            appear = 1 - Math.Pow(1 - appear, 3);
            // Çapraz ışık bandı: 1,4 sn'de soldan sağa geçer, 0,5 sn bekler
            double t = Math.Min(1, (ms % 1900) / 1400.0);
            t = t < .5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            double band = Math.Max(160, w * 0.5);
            double x = -band + (w + 2 * band) * t;
            var baseColor = Base;
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(x - band / 2, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(x + band / 2, band * 0.35, RelativeUnit.Absolute),
                GradientStops = { new GradientStop(baseColor, 0), new GradientStop(Lighten(baseColor, 16), .5), new GradientStop(baseColor, 1) },
            };
            using (context.PushOpacity(appear))
            {
                foreach (var (box, radius) in Shapes(Kind, w, h))
                {
                    if (radius < 0) context.DrawEllipse(brush, null, box.Center, box.Width / 2, box.Height / 2);
                    else context.DrawRectangle(brush, null, box, radius, radius);
                }
            }
        }
    }

    /// <summary>
    /// Yükleniyor göstergesi: dönen, boyu uzayıp kısalan bir yay (temanın vurgu rengi) ve soluk bir iz halkası.
    /// Hızlı işlemlerde göz kırpmasın diye 100 ms sonra belirmeye başlar.
    /// </summary>
    internal sealed class Spinner : Animated
    {
        public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<Spinner, double>(nameof(Thickness), 2.5);
        public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

        private long _shownAt;

        public Spinner() { Width = 22; Height = 22; }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _shownAt = Clock.ElapsedMilliseconds;
            base.OnAttachedToVisualTree(e);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            if (change.Property == IsVisibleProperty && IsVisible) _shownAt = Clock.ElapsedMilliseconds;
            base.OnPropertyChanged(change);
        }

        private static IBrush Resource(string key, IBrush fallback) =>
            Application.Current?.TryGetResource(key, null, out var b) == true && b is IBrush brush ? brush : fallback;

        private static double Ease(double v) => v < .5 ? 4 * v * v * v : 1 - Math.Pow(-2 * v + 2, 3) / 2;

        public override void Render(DrawingContext context)
        {
            double size = Math.Min(Bounds.Width, Bounds.Height);
            if (size <= 0) return;
            long ms = Clock.ElapsedMilliseconds;
            double appear = Math.Clamp((ms - _shownAt - 100) / 200.0, 0, 1);
            if (appear <= 0) return;
            double r = (size - Thickness) / 2;
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
            using var _ = context.PushOpacity(appear);
            context.DrawEllipse(null, new Pen(Resource("AccentSoft", Brushes.Gray), Thickness), center, r, r);

            // 1,6 sn'lik döngü: önce baş koşar (yay uzar), sonra kuyruk yetişir (yay kısalır); bütün halka da döner
            double cycle = (ms % 1600) / 1600.0;
            double head = Ease(Math.Min(1, cycle * 2)) * 250, tail = Ease(Math.Max(0, cycle * 2 - 1)) * 250;
            double start = ms / 1000.0 * 280 + tail, sweep = head - tail + 18;
            double a0 = start * Math.PI / 180, a1 = (start + sweep) * Math.PI / 180;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(center.X + r * Math.Cos(a0), center.Y + r * Math.Sin(a0)), false);
                g.ArcTo(new Point(center.X + r * Math.Cos(a1), center.Y + r * Math.Sin(a1)), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(Resource("Accent", Brushes.Orange), Thickness, lineCap: PenLineCap.Round), geometry);
        }
    }

    /// <summary>Ortalanmış yükleniyor göstergesi ve yanında kısa bir yazı ("Yükleniyor…").</summary>
    internal sealed class Loading : StackPanel
    {
        public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<Loading, string>(nameof(Text), "Yükleniyor…");
        public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

        private readonly TextBlock _text;

        public Loading()
        {
            Orientation = Orientation.Horizontal;
            Spacing = 10;
            HorizontalAlignment = HorizontalAlignment.Center;
            Margin = new Thickness(0, 14);
            _text = new TextBlock { Text = Text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Dim") };
            Children.Add(new Spinner { Width = 18, Height = 18, Thickness = 2.2, VerticalAlignment = VerticalAlignment.Center });
            Children.Add(_text);
            // Hızlı yüklemelerde göz kırpmasın: 120 ms sonra yumuşakça belirir
            Opacity = 0;
            Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) } };
        }

        private void Reveal()
        {
            Opacity = 0;
            DispatcherTimer.RunOnce(() => { if (IsVisible) Opacity = 1; }, TimeSpan.FromMilliseconds(120));
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (IsVisible) Reveal();
        }

        private static IBrush Brush(string key) =>
            Application.Current?.TryGetResource(key, null, out var b) == true && b is IBrush brush ? brush : Brushes.Gray;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == TextProperty) _text.Text = Text;
            if (change.Property == IsVisibleProperty && IsVisible) Reveal();
        }
    }

    /// <summary>
    /// Liste öğeleri gelince sırayla, hafifçe aşağıdan süzülerek belirir (ItemsControl'e v:Reveal.Stagger="True").
    /// Yalnızca yeni gelen öğeler canlanır: ilk yükleme, "daha fazla" ve boş kalıp yeniden dolan liste. Sekmeye her
    /// dönüşte arka planda aynı liste yenilenince (temizlenip hemen doldurulunca) öğeler yerinde kalır.
    /// </summary>
    internal static class Reveal
    {
        public static readonly AttachedProperty<bool> StaggerProperty =
            AvaloniaProperty.RegisterAttached<ItemsControl, bool>("Stagger", typeof(Reveal));

        public static bool GetStagger(ItemsControl control) => control.GetValue(StaggerProperty);
        public static void SetStagger(ItemsControl control, bool value) => control.SetValue(StaggerProperty, value);

        private sealed class State
        {
            public int MaxSeen;
            public long EmptySince = -1, BatchAt = -1;
            public int BatchIndex;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ItemsControl, State> States = new();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly Avalonia.Animation.Animation[] Steps = Enumerable.Range(0, 12).Select(i => new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(320),
            Delay = TimeSpan.FromMilliseconds(i * 35),
            Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            FillMode = Avalonia.Animation.FillMode.Backward,
            Children =
            {
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(0),
                    Setters = { new Avalonia.Styling.Setter(Visual.OpacityProperty, 0.0), new Avalonia.Styling.Setter(TranslateTransform.YProperty, 10.0) },
                },
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(1),
                    Setters = { new Avalonia.Styling.Setter(Visual.OpacityProperty, 1.0), new Avalonia.Styling.Setter(TranslateTransform.YProperty, 0.0) },
                },
            },
        }).ToArray();

        static Reveal()
        {
            StaggerProperty.Changed.AddClassHandler<ItemsControl>((control, e) =>
            {
                if (e.NewValue is not true) return;
                var state = States.GetOrCreateValue(control);
                control.ContainerPrepared += (_, args) => OnPrepared(control, state, args);
                control.Items.CollectionChanged += (_, _) =>
                {
                    if (control.Items.Count == 0) state.EmptySince = Clock.ElapsedMilliseconds;
                };
            });
        }

        private static void OnPrepared(ItemsControl control, State state, ContainerPreparedEventArgs e)
        {
            long now = Clock.ElapsedMilliseconds;
            // Liste bir süre boş kaldıysa (yeni yükleme beklendi) baştan canlanır
            if (state.EmptySince >= 0 && now - state.EmptySince >= 150) state.MaxSeen = 0;
            state.EmptySince = -1;
            if (e.Index < state.MaxSeen || !control.IsEffectivelyVisible) { state.MaxSeen = Math.Max(state.MaxSeen, e.Index + 1); return; }
            state.MaxSeen = e.Index + 1;
            if (now - state.BatchAt > 250) state.BatchIndex = 0;
            state.BatchAt = now;
            var step = Steps[Math.Min(state.BatchIndex++, Steps.Length - 1)];
            _ = step.RunAsync(e.Container);
        }
    }

    /// <summary>
    /// Düğme olmayan tıklanır öğelere (ör. Keşfet paket kartı) basılma hissi: sol tuş basılıyken "pressed" sınıfı.
    /// İçindeki bir düğmeye basmak sayılmaz (olayı o düğme işler).
    /// </summary>
    internal static class Pressable
    {
        public static readonly AttachedProperty<bool> EnabledProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(Pressable));

        public static bool GetEnabled(Control c) => c.GetValue(EnabledProperty);
        public static void SetEnabled(Control c, bool v) => c.SetValue(EnabledProperty, v);

        static Pressable()
        {
            EnabledProperty.Changed.AddClassHandler<Control>((c, e) =>
            {
                if (e.NewValue is not true) return;
                c.PointerPressed += (_, a) =>
                {
                    if (a.GetCurrentPoint(c).Properties.IsLeftButtonPressed) c.Classes.Set("pressed", true);
                };
                c.PointerReleased += (_, _) => c.Classes.Set("pressed", false);
                c.PointerCaptureLost += (_, _) => c.Classes.Set("pressed", false);
                c.PointerExited += (_, _) => c.Classes.Set("pressed", false);
            });
        }
    }
}
