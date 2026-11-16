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
                        rtb.Save(System.IO.Path.Combine(shotDir, $"shot-{n++ % 4}.png"));
                    }
                    catch (Exception ex) { LogFile.Write("[ui] görüntü alınamadı: " + ex.Message); }
                };
                timer.Start();
            }
            if (PerfProbe.Enabled) Opened += (_, _) => PerfProbe.WindowOpened(this);
#endif
            Themes.Mark(this);
            Themes.Attach(NavTabs);
            Themes.Attach(TitleBar);  // başlık çubuğunun da kendi stilleri var (neon alt çizgi)   // sekmelerin kendi stilleri var; temanınkiler onların üstüne
            // Tema geçişi animasyonu (Ayarlar › Tema)
            DataContextChanged += (_, _) =>
            {
                if (DataContext is MainViewModel m) m.PlayThemeTransition = (theme, atMiddle) => Transition.PlayAsync(theme, atMiddle);
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
                            Avalonia.Threading.Dispatcher.UIThread.Post(PlaceTabIndicator, Avalonia.Threading.DispatcherPriority.Loaded);
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
        }

        /// <summary>Bildirim listesi kapanınca hepsi okundu sayılır.</summary>
        private void OnNotificationsClosed(object? sender, EventArgs e)
        {
            if (DataContext is MainViewModel vm) vm.Notifications.IsOpen = false;
        }

        private Rect _indicator;

        private void PlaceTabIndicator()
        {
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
