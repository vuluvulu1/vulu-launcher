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
            // VULU_UI_FLYOUT=notifications / radio / more: açılışta o açılır panel açılır; VULU_UI_SHOT varsa panelin kendi
            // penceresi de flyout.png olarak kaydedilir (pencere görüntüsü açılır panelleri içermez)
            if (Environment.GetEnvironmentVariable("VULU_UI_FLYOUT") is { Length: > 0 } flyoutName)
                Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                {
                    var buttons = this.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Flyout != null).ToList();
                    var target = flyoutName switch
                    {
                        "notifications" => BellIcon.FindAncestorOfType<Button>(),
                        "radio" => buttons.FirstOrDefault(b => b.Classes.Contains("rpill")),
                        _ => buttons.FirstOrDefault(b => b.Content is "⋯" || b.Classes.Contains("hero") && b.Flyout is MenuFlyout),
                    };
                    if (target?.Flyout is not { } flyout) return;
                    flyout.ShowAt(target);
                    if (Environment.GetEnvironmentVariable("VULU_UI_SHOT") is not { Length: > 0 } dir) return;
                    Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                    {
                        try
                        {
                            Visual? content = flyout switch { Flyout f => f.Content as Visual, _ => null };
                            content ??= TopLevel.GetTopLevel(target)?.GetVisualDescendants().OfType<MenuFlyoutPresenter>().FirstOrDefault();
                            Visual? root = content == null ? null : TopLevel.GetTopLevel(content);
                            if (root == null && content != null) { root = content; while (root.GetVisualParent() is { } up) root = up; }
                            if (root == null) return;
                            var size = new Avalonia.PixelSize(Math.Max(1, (int)root.Bounds.Width), Math.Max(1, (int)root.Bounds.Height));
                            using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
                            bmp.Render(root);
                            bmp.Save(System.IO.Path.Combine(dir, "flyout.png"));
                        }
                        catch (Exception ex) { LogFile.Write("[ui] açılır panel görüntüsü alınamadı: " + ex.Message); }
                    }, TimeSpan.FromMilliseconds(900));
                }, TimeSpan.FromSeconds(3));
            if (Environment.GetEnvironmentVariable("VULU_UI_SHOT") is { Length: > 0 } shotDir)
            {
                int n = 0;
                // VULU_UI_SHOT_MS=200: daha sık ve numaralı görüntü (animasyonu kare kare görmek için)
                int shotMs = int.TryParse(Environment.GetEnvironmentVariable("VULU_UI_SHOT_MS"), out int ms) && ms >= 50 ? ms : 0;
                var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(shotMs > 0 ? shotMs : 4000) };
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
                            all.OfType<Button>().FirstOrDefault(b => b.Classes.Contains("rail-avatar"))?.Classes.Set("hover", true);
                        }
                        var size = new Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height);
                        using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
                        rtb.Render(this);
                        // VULU_UI_TOUR=1: her görüntüden sonra sıradaki sekmeye geçer (tek pencereyle bütün sayfalar)
                        if (Environment.GetEnvironmentVariable("VULU_UI_TOUR") == "1" && DataContext is MainViewModel tour)
                        {
                            var tabs = tour.Tabs.Where(t => t.IsVisible).ToList();
                            rtb.Save(System.IO.Path.Combine(shotDir, $"tour-{tour.SelectedTab?.Key ?? "none"}.png"));
                            if (tabs.Count > 0) tour.SelectTabCommand.Execute(tabs[(tabs.IndexOf(tour.SelectedTab!) + 1) % tabs.Count]);
                        }
                        else rtb.Save(System.IO.Path.Combine(shotDir, shotMs > 0 ? $"frame-{n++:000}.png" : $"shot-{n++ % 4}.png"));
                        // VULU_UI_RADIO=1: radyo paneli açılır; ayrı penceresi de radio.png olarak çizilir
                        if (Environment.GetEnvironmentVariable("VULU_UI_RADIO") == "1")
                        {
                            if (RadioPanel.Last is null)
                                this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("rpill"))?.Flyout?.ShowAt(
                                    this.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("rpill")));
                            else if (TopLevel.GetTopLevel(RadioPanel.Last) is { } popup && popup != this)
                            {
                                var ps = new Avalonia.PixelSize((int)popup.Bounds.Width, (int)popup.Bounds.Height);
                                using var prtb = new Avalonia.Media.Imaging.RenderTargetBitmap(ps);
                                prtb.Render(popup);
                                prtb.Save(System.IO.Path.Combine(shotDir, "radio.png"));
                            }
                        }
                    }
                    catch (Exception ex) { LogFile.Write("[ui] görüntü alınamadı: " + ex.Message); }
                };
                timer.Start();
            }
            if (PerfProbe.Enabled) Opened += (_, _) => PerfProbe.WindowOpened(this);
#endif
            Current = this;
            Opened += (_, _) => { if (AppSettings.Current.LauncherFullscreen) SetFullscreen(true); };
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
            // İndirme paneli: Kütüphane'de sağ üst köşe paketin çekmecesiyle çakışıyordu; orada sol altta, rayın yanında durur
            void PlaceDownloads()
            {
                bool k = Themes.IsKutuphane;
                DownloadsPanel.HorizontalAlignment = k ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Right;
                DownloadsPanel.VerticalAlignment = k ? Avalonia.Layout.VerticalAlignment.Bottom : Avalonia.Layout.VerticalAlignment.Top;
                DownloadsPanel.Margin = k ? new Thickness(72, 0, 0, 20) : new Thickness(0, 14, 18, 0);
                DownloadsPanel.CornerRadius = new CornerRadius(k ? 14 : 0);
            }
            PlaceDownloads();
            Themes.Changed += PlaceDownloads;
            InitDownloadsDrag();
            InitFileDrop();
            RailTabs.SizeChanged += (_, _) => QueueRailMark();
            // Simge rayından da pencere sürüklenebilir (düğmelerin üstünde değilse)
            Rail.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !IsOnButton(e.Source as Visual))
                    if (!IsFullscreen) BeginMoveDrag(e);   // tam ekranda pencere sürüklenmez
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
                    if (!IsFullscreen) BeginMoveDrag(e);   // tam ekranda pencere sürüklenmez
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
                if (e.Key == Key.F11)
                {
                    SetFullscreen(!IsFullscreen);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control) && DataContext is MainViewModel main)
                {
                    main.OpenPaletteCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
                // Tanıtım turu: Esc çıkar, sağ ok / Enter ileri, sol ok geri
                if (DataContext is MainViewModel { Tour: { } tour })
                {
                    if (e.Key == Key.Escape) tour.Finish();
                    else if (e.Key is Key.Right or Key.Enter or Key.Space) tour.NextCommand.Execute(null);
                    else if (e.Key == Key.Left) tour.BackCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Escape && DataContext is MainViewModel { Modals.Count: > 0 } vm)
                {
                    // Galerideki büyük görünüm açıksa önce o kapanır
                    if (vm.Modals[^1] is ProjectDetailsModal { HasViewer: true } details) details.Viewer = null;
                    else vm.Modals[^1].DismissCommand.Execute(null);
                    e.Handled = true;
                }
            };
        }

        /// <summary>Kutunun dışına (karartmaya) tıklandı: kutu kapanır. Kutunun içine tıklamak kapatmaz.</summary>
        // Kutu açılışı: ilk çizim (görünmez hâlde) bittikten sonra başlar; içi ağır kutularda da kare atlamadan oynar.
        private void OnModalLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is not Border { DataContext: ModalViewModel modal } back || back.Child is not Control card || back.Tag is "anim") return;
            back.Tag = "anim";
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (modal.Closing) return;
                back.Transitions = new Avalonia.Animation.Transitions { Fade(200, new Avalonia.Animation.Easings.CubicEaseOut()) };
                card.Transitions = new Avalonia.Animation.Transitions
                {
                    Fade(240, new Avalonia.Animation.Easings.CubicEaseOut()),
                    Move(380, new Avalonia.Animation.Easings.SplineEasing(0.16, 1, 0.3, 1)),
                };
                back.Opacity = 1;
                card.Opacity = 1;
                card.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("none");
            }, Avalonia.Threading.DispatcherPriority.Background);
            modal.PropertyChanged += (_, a) =>
            {
                if (a.PropertyName != nameof(ModalViewModel.Closing) || !modal.Closing) return;
                back.Transitions = new Avalonia.Animation.Transitions { Fade(150, new Avalonia.Animation.Easings.CubicEaseIn()) };
                card.Transitions = new Avalonia.Animation.Transitions { Move(150, new Avalonia.Animation.Easings.CubicEaseIn()) };
                back.Opacity = 0;
                card.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("scale(0.97)");
            };

            static Avalonia.Animation.DoubleTransition Fade(int ms, Avalonia.Animation.Easings.Easing easing) =>
                new() { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = easing };
            static Avalonia.Animation.TransformOperationsTransition Move(int ms, Avalonia.Animation.Easings.Easing easing) =>
                new() { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = easing };
        }

        private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source == sender && sender is Border { DataContext: ModalViewModel modal } && !modal.Closing)
            {
                modal.DismissCommand.Execute(null);
                e.Handled = true;
            }
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
            else if (Math.Abs(Canvas.GetTop(RailMark) - top) > 1)
            {
                // Kayarken hap biraz uzar, yerine varınca eski boyuna döner (taşma ve sekme yok)
                Canvas.SetTop(RailMark, top);
                RailPill.Height = 28;
                Avalonia.Threading.DispatcherTimer.RunOnce(() => RailPill.Height = 24, TimeSpan.FromMilliseconds(160));
            }
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
            // Sekmenin yazısının çevresinde yumuşak zemin (sekme 50 yüksekliğinde, zemin 32)
            var target = new Rect(p.X + 2, 9, active.Bounds.Width - 4, 32);
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

        // ── Dosya bırakma: pencereye sürüklenen modpack dosyası içe aktarılır ──
        private static readonly string[] DropExtensions = [".mrpack", ".zip"];

        private void InitFileDrop()
        {
            Avalonia.Input.DragDrop.SetAllowDrop(this, true);
            AddHandler(Avalonia.Input.DragDrop.DragEnterEvent, (_, e) => OnFileDrag(e));
            AddHandler(Avalonia.Input.DragDrop.DragOverEvent, (_, e) => OnFileDrag(e));
            AddHandler(Avalonia.Input.DragDrop.DragLeaveEvent, (_, _) => DropHint.IsVisible = false);
            AddHandler(Avalonia.Input.DragDrop.DropEvent, (_, e) =>
            {
                DropHint.IsVisible = false;
                if (DroppedPack(e) is { } path && DataContext is MainViewModel m && m.LoggedIn) _ = m.ImportFileAsync(path);
            });
        }

        private void OnFileDrag(Avalonia.Input.DragEventArgs e)
        {
            bool ok = DroppedPack(e) != null && DataContext is MainViewModel { LoggedIn: true };
            e.DragEffects = ok ? Avalonia.Input.DragDropEffects.Copy : Avalonia.Input.DragDropEffects.None;
            DropHint.IsVisible = ok;
            e.Handled = true;
        }

        /// <summary>Bırakılan ilk modpack dosyasının yolu (.mrpack ya da .zip); yoksa null.</summary>
        private static string? DroppedPack(Avalonia.Input.DragEventArgs e)
        {
            var files = Avalonia.Input.DataTransferExtensions.TryGetFiles(e.DataTransfer);
            if (files == null) return null;
            foreach (var f in files)
                if (Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(f) is { } p
                    && DropExtensions.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant())) return p;
            return null;
        }

        // ── İndirme kutusu: tutup pencerenin içinde istenen yere taşınır ──
        // Yeri, temanın varsayılan köşesinden kayma olarak saklanır (Kütüphane'nin köşesi başka olduğu için ayrı);
        // pencere küçülünce dışarı taşmaz; çift tıklayınca varsayılan köşeye döner.
        private readonly Avalonia.Media.TranslateTransform _dlShift = new();
        private Point? _dlDragFrom;
        private Point _dlShiftAtDrag;
        private string DownloadsKey => Themes.IsKutuphane ? "downloadsShiftK" : "downloadsShift";

        private void InitDownloadsDrag()
        {
            DownloadsPanel.RenderTransform = _dlShift;
            DownloadsPanel.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeAll);
            LoadDownloadsShift();
            Themes.Changed += LoadDownloadsShift;
            DownloadsPanel.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(DownloadsPanel).Properties.IsLeftButtonPressed || IsOnButton(e.Source as Visual) || IsOnScrollBar(e.Source as Visual)) return;
                if (e.ClickCount == 2) { SetDownloadsShift(0, 0); SaveDownloadsShift(); e.Handled = true; return; }
                _dlDragFrom = e.GetPosition(this);
                _dlShiftAtDrag = new Point(_dlShift.X, _dlShift.Y);
                e.Pointer.Capture(DownloadsPanel);
                e.Handled = true;
            };
            DownloadsPanel.PointerMoved += (_, e) =>
            {
                if (_dlDragFrom is not { } from) return;
                var d = e.GetPosition(this) - from;
                SetDownloadsShift(_dlShiftAtDrag.X + d.X, _dlShiftAtDrag.Y + d.Y);
            };
            void EndDrag()
            {
                if (_dlDragFrom == null) return;
                _dlDragFrom = null;
                SaveDownloadsShift();
            }
            DownloadsPanel.PointerReleased += (_, e) => { e.Pointer.Capture(null); EndDrag(); };
            DownloadsPanel.PointerCaptureLost += (_, _) => EndDrag();
            // Belirince ya da pencere küçülünce kutu pencerenin içine çekilir
            DownloadsPanel.SizeChanged += (_, _) => SetDownloadsShift(_dlShift.X, _dlShift.Y);
            SizeChanged += (_, _) => SetDownloadsShift(_dlShift.X, _dlShift.Y);
        }

        private void LoadDownloadsShift()
        {
            var parts = (VuluLauncher.Native.UiState.Get(DownloadsKey) ?? "").Split(';');
            double x = 0, y = 0;
            if (parts.Length == 2)
            {
                double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x);
                double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y);
            }
            _dlShift.X = x; _dlShift.Y = y;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SetDownloadsShift(_dlShift.X, _dlShift.Y), Avalonia.Threading.DispatcherPriority.Loaded);
        }

        private void SaveDownloadsShift() =>
            VuluLauncher.Native.UiState.Set(DownloadsKey, FormattableString.Invariant($"{Math.Round(_dlShift.X)};{Math.Round(_dlShift.Y)}"));

        /// <summary>Kaymayı uygular; kutu pencerenin içinde (başlık çubuğunun altında) kalacak şekilde sınırlanır.</summary>
        private void SetDownloadsShift(double x, double y)
        {
            if (DownloadsPanel.GetVisualParent() is not Visual parent || !DownloadsPanel.IsVisible || DownloadsPanel.Bounds.Width < 1)
            {
                _dlShift.X = x; _dlShift.Y = y;
                return;
            }
            // Kutunun varsayılan (kaydırılmamış) yeri pencereye göre
            var origin = parent.TranslatePoint(DownloadsPanel.Bounds.Position, this) ?? default;
            double w = DownloadsPanel.Bounds.Width, h = DownloadsPanel.Bounds.Height, top = TitleBar.Bounds.Height + 6, gap = 8;
            double minX = gap - origin.X, maxX = ClientSize.Width - gap - w - origin.X;
            double minY = top - origin.Y, maxY = ClientSize.Height - gap - h - origin.Y;
            _dlShift.X = maxX < minX ? minX : Math.Clamp(x, minX, maxX);
            _dlShift.Y = maxY < minY ? minY : Math.Clamp(y, minY, maxY);        }

        private static bool IsOnScrollBar(Visual? visual)
        {
            for (var v = visual; v != null; v = v.GetVisualParent())
                if (v is Avalonia.Controls.Primitives.ScrollBar) return true;
            return false;
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
            _windowed = (width, height, scale);
            if (WindowState == WindowState.FullScreen) return;   // tam ekrandan çıkınca uygulanır
            Width = width;
            Height = height;
            Scaler.LayoutTransform = new ScaleTransform(scale, scale);
        }

        /// <summary>Açık ana pencere (Ayarlar'daki tam ekran seçeneği için).</summary>
        public static MainWindow? Current { get; private set; }

        private (double Width, double Height, double Scale) _windowed;
        public bool IsFullscreen => WindowState == WindowState.FullScreen;

        /// <summary>
        /// Tam ekran: pencere ekranı kaplar, kenarlardan boyutlandırılamaz; arayüz ekranın yüksekliğine göre ölçeklenir
        /// (tasarım 820 birim yüksekliğinde; geniş ekranda içerik yana doğru genişler).
        /// </summary>
        public void SetFullscreen(bool on)
        {
            if (on == IsFullscreen) return;
            if (on)
            {
                var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
                double heightDip = screen != null ? screen.Bounds.Height / screen.Scaling : Height;
                double scale = heightDip / 820 * Math.Clamp(AppSettings.Current.UiScale, 60, 300) / 100.0;
                WindowState = WindowState.FullScreen;
                Scaler.LayoutTransform = new ScaleTransform(scale, scale);
            }
            else
            {
                WindowState = WindowState.Normal;
                if (_windowed.Width > 0)
                {
                    Width = _windowed.Width;
                    Height = _windowed.Height;
                    Scaler.LayoutTransform = new ScaleTransform(_windowed.Scale, _windowed.Scale);
                }
            }
            if (DataContext is MainViewModel vm) vm.IsFullscreen = on;
        }
    }
}
