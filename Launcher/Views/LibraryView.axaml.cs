using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    /// <summary>Kapak ızgarasının sonundaki "Yeni paket" kartı.</summary>
    internal sealed class NewPackCard
    {
        public static readonly NewPackCard Instance = new();
    }

    /// <summary>
    /// Kütüphane temasının Oyna sayfası (LibraryView.axaml). Süzgeç ve arama yalnızca görünümü daraltır; seçili paket hep
    /// Oyna modelindeki Selected'dır (ızgarada süzülüp görünmese de çekmecede kalır).
    /// </summary>
    internal sealed partial class LibraryView : UserControl
    {
        private readonly ObservableCollection<object> _items = new();
        private HomeViewModel? _vm;
        private string _filter = "all";
        private string _sort = "recent";
        private bool _syncing;

        public LibraryView()
        {
            InitializeComponent();
            Posters.ItemsSource = _items;
            // Sonradan eklenen kapak (yeni paket, kurulan modpack) birden değil belirerek gelir
            Posters.ContainerPrepared += (_, e) =>
            {
                var c = e.Container;
                c.Transitions = null; c.Opacity = 1; c.RenderTransform = null;   // geri dönüştürülen kap solmuş kalmasın
                if (_welcomeShown == null || !Posters.IsEffectivelyVisible || _bulk || _prevKeys.Contains(KeyOf(Posters.ItemFromContainer(c)))) return;
                c.Opacity = 0;
                c.RenderTransform = new Avalonia.Media.ScaleTransform(0.94, 0.94);
                Dispatcher.UIThread.Post(() =>
                {
                    c.Transitions ??= new Avalonia.Animation.Transitions();
                    c.Transitions.Add(new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new Avalonia.Animation.Easings.CubicEaseOut() });
                    c.Transitions.Add(new Avalonia.Animation.TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(360), Easing = new Avalonia.Animation.Easings.CubicEaseOut() });
                    c.Opacity = 1;
                    c.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("none");
                }, DispatcherPriority.Background);
            };
            Posters.SelectionChanged += OnPosterSelected;
            SearchBox.TextChanged += (_, _) => Rebuild();
            _sort = VuluLauncher.Native.UiState.Get("librarySort") is { } saved && saved is "recent" or "name" or "playtime" or "added" or "custom" ? saved : "recent";
            InitDrag();
            SortBox.SelectedItem = SortBox.Items.OfType<ComboBoxItem>().First(i => (string?)i.Tag == _sort);
            SortBox.SelectionChanged += (_, _) =>
            {
                if (SortBox.SelectedItem is not ComboBoxItem { Tag: string tag } || tag == _sort) return;
                _sort = tag;
                VuluLauncher.Native.UiState.Set("librarySort", tag);
                Rebuild();
            };
            DataContextChanged += (_, _) => Bind(DataContext as HomeViewModel);
            KutPolish.AttachSearch(this, SearchBox);
            PosterScroll.SizeChanged += (_, e) => FitPosters(e.NewSize.Width);
            // Kapağa tıklayınca ya da seçiliyken Enter: paketin sayfası açılır (kapağın üstündeki ▶ doğrudan başlatır)
            Posters.Tapped += (_, e) =>
            {
                if (_suppressTap) { _suppressTap = false; return; }   // sürükleyip bırakmak tıklama değil
                if (e.Source is not Avalonia.Visual v || v.FindAncestorOfType<Button>(true) != null) return;
                if (v.FindAncestorOfType<ListBoxItem>(true) is { DataContext: InstanceItem item }) _vm?.OpenPageCommand.Execute(item);
            };
            Posters.KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter && Posters.SelectedItem is InstanceItem item) { _vm?.OpenPageCommand.Execute(item); e.Handled = true; }
            };
            // Esc: sayfadan kütüphaneye dön
            KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Escape && _vm is { PageOpen: true }) { _vm.ClosePageCommand.Execute(null); e.Handled = true; }
            };
        }

        /// <summary>Konsol › Kopyala: bütün satırlar panoya.</summary>
        private async void OnCopyLog(object? sender, RoutedEventArgs e)
        {
            if (_vm == null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, _vm.ConsoleText());
            _vm.Main?.ShowToast("Kopyalandı", "Konsol panoya kopyalandı.");
        }

        private void Play()
        {
            if (_vm?.PlayCommand.CanExecute(null) == true) _vm.PlayCommand.Execute(null);
        }

        /// <summary>
        /// Kapaklar satırı boşluksuz doldurur: sığan sütun sayısına göre genişlik 170–240 arasında esner, oran (180×230) korunur.
        /// </summary>
        private double _fitWidth = -1;
        private void FitPosters(double width)
        {
            const double gap = 18, min = 170, max = 240;
            if (width <= 0 || Math.Abs(width - _fitWidth) < 1) return;
            _fitWidth = width;
            double room = width - 1 - 14;   // listenin sol payı (kapak kalkınca kesilmesin diye)   // her öğe sağ boşluğuyla (gap) birlikte yer kaplar
            int cols = Math.Max(1, (int)(room / (min + gap)));
            double w = Math.Min(max, Math.Floor(room / cols - gap));
            Resources["PosterW"] = w;
            Resources["PosterH"] = Math.Round(w * 230 / 180);
        }

        private void Bind(HomeViewModel? vm)
        {
            if (_vm == vm) return;
            if (_vm != null)
            {
                _vm.Instances.CollectionChanged -= OnInstancesChanged;
                _vm.Pending.CollectionChanged -= OnInstancesChanged;
                _vm.PropertyChanged -= OnVmChanged;
                _vm.Console.CollectionChanged -= OnConsoleChanged;
            }
            _vm = vm;
            if (vm != null)
            {
                vm.Instances.CollectionChanged += OnInstancesChanged;
                vm.Pending.CollectionChanged += OnInstancesChanged;
                vm.PropertyChanged += OnVmChanged;
                vm.Console.CollectionChanged += OnConsoleChanged;
            }
            Rebuild();
        }

        // Liste yenilenirken (Clear + tek tek Add) kapaklar her adımda yeniden çizilmesin: değişiklikler tek seferde uygulanır
        private void OnInstancesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_rebuildQueued) return;
            _rebuildQueued = true;
            Dispatcher.UIThread.Post(() => { _rebuildQueued = false; Rebuild(); }, DispatcherPriority.Background);
        }

        // Güncelleme denetimi paketleri sonradan işaretler: sayılar ve "Güncellemesi olan" süzgeci tazelenir
        private readonly HashSet<InstanceItem> _watched = new();
        private bool _rebuildQueued;

        private void WatchItems()
        {
            if (_vm == null) return;
            var now = _vm.Instances.ToHashSet();
            foreach (var gone in _watched.Where(i => !now.Contains(i)).ToList()) { gone.PropertyChanged -= OnItemChanged; _watched.Remove(gone); }
            foreach (var item in now.Where(_watched.Add)) item.PropertyChanged += OnItemChanged;
        }

        private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(InstanceItem.HasUpdate) or nameof(InstanceItem.Name)) || _rebuildQueued) return;
            _rebuildQueued = true;
            Dispatcher.UIThread.Post(() => { _rebuildQueued = false; Rebuild(); }, DispatcherPriority.Background);
        }

        private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HomeViewModel.Selected))
            {
                SyncSelection();
                KutPolish.Swap(DrawerBody);
            }
        }

        private void OnFilter(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag }) return;
            _filter = tag;
            foreach (var b in new[] { FilterAll, FilterMine, FilterVulu, FilterUpdate }) b.Classes.Set("on", b == sender);
            Rebuild();
        }

        private void Rebuild()
        {
            if (_vm == null) { _items.Clear(); NoMatch.IsVisible = false; return; }
            if (_dragging) return;   // sürüklerken sıra elle değişiyor
            WatchItems();
            string q = SearchBox.Text?.Trim() ?? "";
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            var list = _vm.Instances.Where(i => _filter switch
                {
                    // Kişisel: kendi oluşturduğun ya da CurseForge/Modrinth'ten kurduğun; vulu paketleri: vulu Packs'ten kurulanlar.
                    // Hazır Vanilla ikisinde de değil (yalnızca "Tümü"de).
                    "mine" => i.IsCustom && !i.IsVuluPack,
                    "vulu" => i.IsVuluPack,
                    "update" => i.HasUpdate,
                    _ => true,
                })
                .Where(i => q.Length == 0 || tr.CompareInfo.IndexOf(i.Name, q, CompareOptions.IgnoreCase) >= 0);
            list = _sort switch
            {
                "name" => list.OrderBy(i => i.Name, StringComparer.Create(tr, ignoreCase: true)),
                "playtime" => list.OrderByDescending(i => i.PlaySeconds).ThenBy(i => i.Name, StringComparer.Create(tr, ignoreCase: true)),
                "added" => list.OrderByDescending(i => i.CreatedAt ?? DateTime.MinValue),
                "custom" => OrderCustom(list),
                // Son oynanan; hiç oynanmamışlar en yeni eklenen önce
                _ => list.OrderByDescending(i => i.LastPlayed ?? DateTime.MinValue).ThenByDescending(i => i.CreatedAt ?? DateTime.MinValue),
            };
            var items = list.ToList<object>();
            // Kurulmakta olan paketler sonda, ilerleme halkasıyla (güncelleme süzgeci ve arama dışında)
            if (_filter != "update" && q.Length == 0) items.AddRange(_vm.Pending);
            if (_filter is "all" or "mine" && q.Length == 0) items.Add(NewPackCard.Instance);
            // Süzgeç ya da arama değişince kapaklar tek tek belirmesin; yalnızca eklenen yeni paket belirsin
            _bulk = _filter != _lastFilter || q != _lastQuery;
            _lastFilter = _filter; _lastQuery = q;
            if (_removing) return;   // solma bitince yeniden kurulur
            var newKeys = items.Select(KeyOf).ToHashSet();
            bool animate = !_bulk && _welcomeShown != null && Posters.IsEffectivelyVisible;
            if (animate && !_faded)
            {
                var fading = _items.Where(o => !newKeys.Contains(KeyOf(o))).Select(o => Posters.ContainerFromItem(o)).OfType<Control>().ToList();
                if (fading.Count > 0)
                {
                    _removing = true;
                    foreach (var c in fading)
                    {
                        c.IsHitTestVisible = false;
                        c.Transitions = new Avalonia.Animation.Transitions
                        {
                            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new Avalonia.Animation.Easings.CubicEaseIn() },
                            new Avalonia.Animation.TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new Avalonia.Animation.Easings.CubicEaseIn() },
                        };
                        c.Opacity = 0;
                        c.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("scale(0.9)");
                    }
                    DispatcherTimer.RunOnce(() =>
                    {
                        _removing = false;
                        _faded = true;   // solan kapak bu kez gerçekten kaldırılsın (yeniden soldurulmasın)
                        Rebuild();
                    }, TimeSpan.FromMilliseconds(270));
                    return;
                }
            }
            _faded = false;
            // Kalan kapaklar yeni yerlerine kayarak gitsin: eski konumlar kaydedilir, yerleşimden sonra aradaki fark
            // kaydırmayla başlatılıp sıfıra indirilir (ör. paket silinince "Yeni paket" kartı sola kayar)
            var before = new Dictionary<string, Avalonia.Point>();
            if (animate)
                foreach (var o in _items)
                    if (Posters.ContainerFromItem(o) is { } c && c.Opacity > 0.01) before[KeyOf(o)] = c.Bounds.Position;
            _syncing = true;
            _items.SyncTo(items);
            _bulk = false;
            _prevKeys = newKeys;
            if (before.Count > 0)
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var o in _items)
                    {
                        if (!before.TryGetValue(KeyOf(o), out var old) || Posters.ContainerFromItem(o) is not { } c) continue;
                        var d = old - c.Bounds.Position;
                        if (Math.Abs(d.X) < 1 && Math.Abs(d.Y) < 1) continue;
                        c.Transitions = null;
                        c.Opacity = 1;
                        c.RenderTransform = new Avalonia.Media.TranslateTransform(d.X, d.Y);
                        Dispatcher.UIThread.Post(() =>
                        {
                            c.Transitions = new Avalonia.Animation.Transitions
                            {
                                new Avalonia.Animation.TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(380), Easing = new Avalonia.Animation.Easings.SplineEasing(0.16, 1, 0.3, 1) },
                            };
                            c.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("none");
                        }, DispatcherPriority.Background);
                    }
                }, DispatcherPriority.Background);
            _syncing = false;
            NoMatch.IsVisible = _items.Count == 0 && _vm.Instances.Count > 0;
            NoMatch.Title = q.Length > 0 ? $"“{q}” ile eşleşen paket yok" : "Bu süzgeçte paket yok";

            // Süzgeç haplarında sayılar; güncellemesi olan yoksa o hap gizlenir (seçiliyken kalır)
            int all = _vm.Instances.Count, mine = _vm.Instances.Count(i => i.IsCustom && !i.IsVuluPack), vulu = _vm.Instances.Count(i => i.IsVuluPack),
                updates = _vm.Instances.Count(i => i.HasUpdate);
            FilterAll.Content = KutPolish.ChipText("Tümü", all);
            FilterMine.Content = KutPolish.ChipText("Kişisel", mine);
            FilterVulu.Content = KutPolish.ChipText("vulu paketleri", vulu);
            FilterUpdate.Content = KutPolish.ChipText("Güncellemesi olan", updates);
            FilterUpdate.IsVisible = updates > 0 || _filter == "update";
            FilterMine.IsVisible = mine > 0 || _filter == "mine";
            FilterVulu.IsVisible = vulu > 0 || _filter == "vulu";
            CountText.IsVisible = false;   // sayı "Tümü" hapında
            // Kendi ya da vulu paketin yoksa karşılama alanı (yalnızca "Tümü" ve aramasızken)
            SetWelcome(!_vm.Instances.Any(i => i.IsCustom) && _vm.Pending.Count == 0 && _filter == "all" && q.Length == 0);
            SyncSelection();
        }

        // Karşılama alanı birden belirip kaybolmasın: yüksekliği ve saydamlığı birlikte kayar, altındaki kapaklar
        // yumuşakça yer değiştirir. İlk çizimde animasyon yok (kütüphane açılırken zaten olduğu gibi görünür).
        private bool? _welcomeShown;
        private bool _bulk, _removing, _faded;
        private HashSet<string> _prevKeys = new();
        private static string KeyOf(object? o) => o switch
        {
            InstanceItem i => i.Id,
            PendingInstall p => "pending:" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p),
            _ => o?.GetType().Name ?? "",
        };
        private string? _lastFilter, _lastQuery;
        private DispatcherTimer? _welcomeTimer;

        private void SetWelcome(bool show)
        {
            if (_welcomeShown == show) return;
            bool first = _welcomeShown == null || !IsEffectivelyVisible;
            _welcomeShown = show;
            _welcomeTimer?.Stop();
            if (first)
            {
                Welcome.IsVisible = show;
                Welcome.Height = double.NaN; Welcome.Opacity = 1; Welcome.Margin = WelcomeMargin; Welcome.RenderTransform = null;
                return;
            }
            // Başlangıç ve hedef yükseklik: açılırken doğal boyu ölçülür, kapanırken şimdiki boydan sıfıra
            double from, to;
            if (show)
            {
                Welcome.IsVisible = true;
                Welcome.Height = double.NaN;
                Welcome.Measure(new Avalonia.Size(Math.Max(1, PosterScroll.Bounds.Width), double.PositiveInfinity));
                from = 0; to = Welcome.DesiredSize.Height - WelcomeMargin.Bottom;
            }
            else { from = Welcome.Bounds.Height; to = 0; }
            double fromO = show ? 0 : Welcome.Opacity, toO = show ? 1 : 0;
            var started = DateTime.UtcNow;
            const double ms = 360;
            Apply(0);
            _welcomeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) =>
            {
                double p = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds / ms, 0, 1);
                Apply(p);
                if (p < 1) return;
                _welcomeTimer!.Stop();
                if (!show) Welcome.IsVisible = false;
                Welcome.Height = double.NaN; Welcome.Margin = WelcomeMargin; Welcome.RenderTransform = null;
            });
            _welcomeTimer.Start();

            void Apply(double p)
            {
                double e = 1 - Math.Pow(1 - p, 3);                       // yavaşlayarak dur
                double k = show ? e : 1 - e;                              // 0 = kapalı, 1 = açık
                Welcome.Height = from + (to - from) * e;
                Welcome.Opacity = fromO + (toO - fromO) * (show ? Math.Clamp((p - 0.25) / 0.75, 0, 1) : Math.Clamp(p / 0.6, 0, 1));
                Welcome.Margin = new Avalonia.Thickness(0, 0, WelcomeMargin.Right, WelcomeMargin.Bottom * k);
                Welcome.RenderTransform = new Avalonia.Media.TranslateTransform(0, -10 * (1 - k));
            }
        }

        private static readonly Avalonia.Thickness WelcomeMargin = new(0, 0, 18, 20);

        private void SyncSelection()
        {
            if (_vm == null) return;
            _syncing = true;
            Posters.SelectedItem = _vm.Selected != null && _items.Contains(_vm.Selected) ? _vm.Selected : null;
            _syncing = false;
        }

        private void OnPosterSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _vm == null) return;
            switch (Posters.SelectedItem)
            {
                case InstanceItem item:
                    if (_vm.Selected != item) _vm.Selected = item;
                    break;
                case PendingInstall:
                    SyncSelection();   // kurulurken seçilemez; bitince kendiliğinden seçilir
                    break;
                case NewPackCard:
                    // Kart seçili kalmaz: yeni paket kutusu açılır, seçim eski pakete döner
                    SyncSelection();
                    if (_vm.NewInstanceCommand.CanExecute(null)) _vm.NewInstanceCommand.Execute(null);
                    break;
            }
        }

        /// <summary>Sağ tıklanan paket seçilir; menüdeki işlemler seçili pakete uygulanır.</summary>
        private void OnPosterContextRequested(object? sender, Avalonia.Input.ContextRequestedEventArgs e)
        {
            if (sender is not Control { DataContext: InstanceItem item } target || _vm == null) return;
            if (_vm.Selected != item) _vm.Selected = item;
            if (target.ContextFlyout is MenuFlyout menu)
                foreach (var entry in menu.Items.OfType<Control>()) entry.DataContext = _vm;
        }

        // Konsol: en alttayken yeni satırlar gelince alta kayar (Oyna'daki konsolla aynı)
        private bool _scrollQueued;
        private ScrollViewer? LogScroll => LogList.FindDescendantOfType<ScrollViewer>();

        private void OnConsoleChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_scrollQueued || !LogList.IsEffectivelyVisible || LogScroll is not { } scroll) return;
            if (scroll.Offset.Y < scroll.Extent.Height - scroll.Viewport.Height - 40) return;
            _scrollQueued = true;
            Dispatcher.UIThread.Post(() => { _scrollQueued = false; LogScroll?.ScrollToEnd(); }, DispatcherPriority.Background);
        }
    }
}
