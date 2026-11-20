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
        private bool _syncing;

        public LibraryView()
        {
            InitializeComponent();
            Posters.ItemsSource = _items;
            Posters.SelectionChanged += OnPosterSelected;
            SearchBox.TextChanged += (_, _) => Rebuild();
            DataContextChanged += (_, _) => Bind(DataContext as HomeViewModel);
            KutPolish.AttachSearch(this, SearchBox);
            PosterScroll.SizeChanged += (_, e) => FitPosters(e.NewSize.Width);
            // Kapağa tıklayınca ya da seçiliyken Enter: paketin sayfası açılır (kapağın üstündeki ▶ doğrudan başlatır)
            Posters.Tapped += (_, e) =>
            {
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
            double room = width - 1;   // her öğe sağ boşluğuyla (gap) birlikte yer kaplar
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
                _vm.PropertyChanged -= OnVmChanged;
                _vm.Console.CollectionChanged -= OnConsoleChanged;
            }
            _vm = vm;
            if (vm != null)
            {
                vm.Instances.CollectionChanged += OnInstancesChanged;
                vm.PropertyChanged += OnVmChanged;
                vm.Console.CollectionChanged += OnConsoleChanged;
            }
            Rebuild();
        }

        private void OnInstancesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

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
            WatchItems();
            string q = SearchBox.Text?.Trim() ?? "";
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            var list = _vm.Instances.Where(i => _filter switch
                {
                    "mine" => i.IsCustom,
                    "vulu" => !i.IsCustom,
                    "update" => i.HasUpdate,
                    _ => true,
                })
                .Where(i => q.Length == 0 || tr.CompareInfo.IndexOf(i.Name, q, CompareOptions.IgnoreCase) >= 0)
                .ToList<object>();
            if (_filter is "all" or "mine" && q.Length == 0) list.Add(NewPackCard.Instance);
            _syncing = true;
            _items.SyncTo(list);
            _syncing = false;
            NoMatch.IsVisible = _items.Count == 0 && _vm.Instances.Count > 0;
            NoMatch.Title = q.Length > 0 ? $"“{q}” ile eşleşen paket yok" : "Bu süzgeçte paket yok";

            // Süzgeç haplarında sayılar; güncellemesi olan yoksa o hap gizlenir (seçiliyken kalır)
            int all = _vm.Instances.Count, mine = _vm.Instances.Count(i => i.IsCustom), updates = _vm.Instances.Count(i => i.HasUpdate);
            FilterAll.Content = KutPolish.ChipText("Tümü", all);
            FilterMine.Content = KutPolish.ChipText("Kişisel", mine);
            FilterVulu.Content = KutPolish.ChipText("vulu paketleri", all - mine);
            FilterUpdate.Content = KutPolish.ChipText("Güncellemesi olan", updates);
            FilterUpdate.IsVisible = updates > 0 || _filter == "update";
            FilterMine.IsVisible = mine > 0 || _filter == "mine";
            FilterVulu.IsVisible = all - mine > 0 || _filter == "vulu";
            CountText.IsVisible = false;   // sayı "Tümü" hapında
            SyncSelection();
        }

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
