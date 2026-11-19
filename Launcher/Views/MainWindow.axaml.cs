using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VuluLauncher.ViewModels;
using Avalonia.VisualTree;

namespace VuluLauncher.Views
{
    internal sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            PerfProbe.Mark("  xaml yüklendi");
            // Yalnızca geliştirmede: VULU_UI_SHOT=<klasör> ile pencerenin kendisi birkaç saniyede bir PNG'ye çizilir
            // (arayüz denemesi; ekran değil, yalnızca bu pencerenin görsel ağacı)
            if (Environment.GetEnvironmentVariable("VULU_UI_SHOT") is { Length: > 0 } shotDir)
            {
                int n = 0;
                var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                timer.Tick += (_, _) =>
                {
                    try
                    {
                        // VULU_UI_HOVER=1: fare yokken de "üstüne gelinmiş" hâli görünsün (ilk tıklanır kart, ilk paket resmi)
                        if (Environment.GetEnvironmentVariable("VULU_UI_HOVER") == "1")
                        {
                            var all = this.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible).ToList();
                            all.FirstOrDefault(c => c is Border b && b.Classes.Contains("lift"))?.Classes.Set("hover", true);
                            all.OfType<ListBoxItem>().Skip(1).FirstOrDefault()?.Classes.Set("hover", true);
                        }
                        var size = new Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height);
                        using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
                        // Tema sahnesi compositor görseli; görüntüye girsin diye o an normal çizimle de verilir
                        SynthwaveBackdrop.Snapshotting = true;
                        try { rtb.Render(this); } finally { SynthwaveBackdrop.Snapshotting = false; }
                        // VULU_UI_TOUR=1: her görüntüden sonra sıradaki sekmeye geçer (tek pencereyle bütün sayfalar)
                        if (Environment.GetEnvironmentVariable("VULU_UI_TOUR") == "1" && DataContext is MainViewModel tour)
                        {
                            var tabs = tour.Tabs.Where(t => t.IsVisible).ToList();
                            rtb.Save(System.IO.Path.Combine(shotDir, $"tour-{tour.SelectedTab?.Key ?? "none"}.png"));
                            if (tabs.Count > 0) tour.SelectTabCommand.Execute(tabs[(tabs.IndexOf(tour.SelectedTab!) + 1) % tabs.Count]);
                        }
                        else rtb.Save(System.IO.Path.Combine(shotDir, $"shot-{n++ % 4}.png"));
                    }
                    catch (Exception ex) { LogFile.Write("[ui] görüntü alınamadı: " + ex.Message); }
                };
                timer.Start();
            }
            if (PerfProbe.Enabled) Opened += (_, _) => PerfProbe.WindowOpened(this);
#endif
            Themes.Mark(this);
            Themes.Attach(NavTabs);
            Themes.Attach(TitleBar);
            Themes.Attach(Rail);
            // Tema başlık çubuğundaki öğeleri stille gizleyip gösterince (Kütüphane: logo, sekmeler, profil) çubuk kendiliğinden
            // yeniden ölçülmüyor, gizlenenlerin yeri boş kalıyordu (pencere düğmeleri köşeden içeri kayıyordu)
            void Remeasure() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                foreach (var c in TitleGrid.Children.Concat(TitleButtons.Children)) c.InvalidateMeasure();
                TitleButtons.InvalidateMeasure();
                TitleGrid.InvalidateMeasure();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
            Opened += (_, _) => Remeasure();
            Themes.Changed += Remeasure;
            // Kütüphane rayı: seçili sekmenin işareti
            void QueueRailMark() => Avalonia.Threading.Dispatcher.UIThread.Post(PlaceRailMark, Avalonia.Threading.DispatcherPriority.Loaded);
            Opened += (_, _) => QueueRailMark();
            Themes.Changed += QueueRailMark;
            RailTabs.SizeChanged += (_, _) => QueueRailMark();
            // Simge rayından da pencere sürüklenebilir (düğmelerin üstünde değilse)
            Rail.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !IsOnButton(e.Source as Visual))
                    BeginMoveDrag(e);
            };  // başlık çubuğunun da kendi stilleri var (neon alt çizgi)   // sekmelerin kendi stilleri var; temanınkiler onların üstüne
            // Tema geçişi animasyonu (Ayarlar › Tema)
            DataContextChanged += (_, _) =>
            {
                if (DataContext is MainViewModel m)
                {
                    m.PlayThemeTransition = (theme, atMiddle) => Transition.PlayAsync(theme, atMiddle);
                    WatchBell(m);
                }
            };
            // Başlık çubuğundan sürükleme (düğmelerin üstünde değilse)
            TitleBar.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !IsOnButton(e.Source as Visual))
                    BeginMoveDrag(e);
            };
            // Seçili sekmenin çizgisi yeni sekmeye kayar; ilk konumda ve sekme gizliyken (Profil) animasyonsuz
            NavTabs.LayoutUpdated += (_, _) => PlaceTabIndicator();
            DataContextChanged += (_, _) =>
            {
                if (DataContext is MainViewModel m)
                    m.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(MainViewModel.SelectedTab))
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(PlaceTabIndicator, Avalonia.Threading.DispatcherPriority.Loaded);
                            Avalonia.Threading.Dispatcher.UIThread.Post(PlaceRailMark, Avalonia.Threading.DispatcherPriority.Loaded);
                        }
                    };
            };
            // İndirme paneli: üstüne gelindiyse tamamlanınca kapanmaz
            DownloadsPanel.PointerEntered += (_, _) => { if (DataContext is MainViewModel m) m.Downloads.Hovered = true; };
            DownloadsPanel.PointerExited += (_, _) => { if (DataContext is MainViewModel m) m.Downloads.Hovered = false; };
            // Esc en üstteki kutuyu kapatır; Ctrl+K hızlı aramayı açar
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control) && DataContext is MainViewModel main)
                {
                    main.OpenPaletteCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Escape && DataContext is MainViewModel { Modals.Count: > 0 } vm)
                {
                    vm.Modals[^1].DismissCommand.Execute(null);
                    e.Handled = true;
                }
            };
        }

        private void OnNotificationsOpened(object? sender, EventArgs e)
        {
            if (DataContext is MainViewModel vm) vm.Notifications.IsOpen = true;
            // Liste zildan aşağı doğru solarak açılır
            if (sender is Flyout { Content: Control panel }) _ = PopoverIn.RunAsync(panel);
        }

        private int _lastUnread;

        /// <summary>Okunmamış sayısı artınca zil kısa bir sallanır, rozet "pıt" diye büyüyüp yerine oturur.</summary>
        private void WatchBell(MainViewModel vm)
        {
            _lastUnread = vm.Notifications.Unread;
            vm.Notifications.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(NotificationsViewModel.Unread)) return;
                int now = vm.Notifications.Unread;
                if (now > _lastUnread)
                {
                    _ = BellRing.RunAsync(BellIcon);
                    _ = BadgePop.RunAsync(NotifBadge);
                }
                _lastUnread = now;
            };
        }

        private static Avalonia.Animation.KeyFrame Frame(double cue, params (AvaloniaProperty p, object v)[] setters)
        {
            var f = new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(cue) };
            foreach (var (prop, v) in setters) f.Setters.Add(new Avalonia.Styling.Setter(prop, v));
            return f;
        }

        private static readonly Avalonia.Animation.Animation PopoverIn = new()
        {
            Duration = TimeSpan.FromMilliseconds(200),
            Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            Children =
            {
                Frame(0, (OpacityProperty, 0.0), (Avalonia.Media.TranslateTransform.YProperty, -6.0)),
                Frame(1, (OpacityProperty, 1.0), (Avalonia.Media.TranslateTransform.YProperty, 0.0)),
            },
        };

        private static readonly Avalonia.Animation.Animation BellRing = new()
        {
            Duration = TimeSpan.FromMilliseconds(650),
            Easing = new Avalonia.Animation.Easings.LinearEasing(),
            Children =
            {
                Frame(0, (Avalonia.Media.RotateTransform.AngleProperty, 0.0)),
                Frame(0.15, (Avalonia.Media.RotateTransform.AngleProperty, 16.0)),
                Frame(0.35, (Avalonia.Media.RotateTransform.AngleProperty, -12.0)),
                Frame(0.55, (Avalonia.Media.RotateTransform.AngleProperty, 7.0)),
                Frame(0.75, (Avalonia.Media.RotateTransform.AngleProperty, -3.0)),
                Frame(1, (Avalonia.Media.RotateTransform.AngleProperty, 0.0)),
            },
        };

        private static readonly Avalonia.Animation.Animation BadgePop = new()
        {
            Duration = TimeSpan.FromMilliseconds(380),
            Easing = new Avalonia.Animation.Easings.BackEaseOut(),
            Children =
            {
                Frame(0, (Avalonia.Media.ScaleTransform.ScaleXProperty, 0.4), (Avalonia.Media.ScaleTransform.ScaleYProperty, 0.4)),
                Frame(1, (Avalonia.Media.ScaleTransform.ScaleXProperty, 1.0), (Avalonia.Media.ScaleTransform.ScaleYProperty, 1.0)),
            },
        };

        /// <summary>Bildirim listesi kapanınca hepsi okundu sayılır.</summary>
        private void OnNotificationsClosed(object? sender, EventArgs e)
        {
            if (DataContext is MainViewModel vm) vm.Notifications.IsOpen = false;
        }

        private Rect _indicator;

        /// <summary>Raydaki seçili sekmenin zemini ve amber çizgisi o sekmeye kayar; ilk konumda ve gizli sekmede (Profil) kaymadan.</summary>
        private void PlaceRailMark()
        {
            if (!Rail.IsEffectivelyVisible) return;
            var active = Rail.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("active") && b.IsEffectivelyVisible);
            if (active?.TranslatePoint(default, RailCanvas) is not { } p || active.Bounds.Height < 1)
            {
                RailMark.Opacity = 0;
                return;
            }
            double top = p.Y + (active.Bounds.Height - RailMark.Height) / 2;
            if (RailMark.Opacity == 0)
            {
                var transitions = RailMark.Transitions;
                RailMark.Transitions = null;
                Canvas.SetTop(RailMark, top);
                RailMark.Transitions = transitions;
            }
            else Canvas.SetTop(RailMark, top);
            RailMark.Opacity = 1;
        }

        private void PlaceTabIndicator()
        {
            // Sekmeler gizliyken (Kütüphane: sekmeler rayda) her yerleşim geçişinde boşuna aranmaz
            if (!NavTabs.IsEffectivelyVisible) return;
            var active = NavTabs.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("active") && b.IsEffectivelyVisible);
            if (active?.TranslatePoint(default, TabIndicator.GetVisualParent<Visual>()!) is not { } p || active.Bounds.Width < 1)
            {
                TabIndicator.Opacity = 0;
                return;
            }
            var target = new Rect(p.X + 8, 0, active.Bounds.Width - 16, 2);
            if (target == _indicator && TabIndicator.Opacity > 0) return;
            bool first = TabIndicator.Opacity == 0 || _indicator == default;
            _indicator = target;
            if (first)
            {
                // İlk kez ya da gizli sekmeden dönüşte kaymadan yerinde belirir
                var transitions = TabIndicator.Transitions;
                TabIndicator.Transitions = null;
                Canvas.SetLeft(TabIndicator, target.X);
                TabIndicator.Width = target.Width;
                TabIndicator.Transitions = transitions;
            }
            else
            {
                Canvas.SetLeft(TabIndicator, target.X);
                TabIndicator.Width = target.Width;
            }
            TabIndicator.Opacity = 1;
        }

        private static bool IsOnButton(Visual? visual)
        {
            for (var v = visual; v != null; v = v.GetVisualParent())
                if (v is Button) return true;
            return false;
        }

        /// <summary>Pencere boyutu (DIP) ve arayüz ölçeği (Ayarlar → launcher boyutu, yazı ve arayüz boyutu).</summary>
        public void ApplyLayout(double width, double height, double scale)
        {
            Width = width;
            Height = height;
            Scaler.LayoutTransform = new ScaleTransform(scale, scale);
        }
    }
}
