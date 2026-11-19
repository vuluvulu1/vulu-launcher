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

        private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HomeViewModel.Selected)) SyncSelection();
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
            if (_vm == null) { _items.Clear(); return; }
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
                .ToList();
            _syncing = true;
            _items.Clear();
            foreach (var i in list) _items.Add(i);
            if (_filter is "all" or "mine" && q.Length == 0) _items.Add(NewPackCard.Instance);
            _syncing = false;
            CountText.Text = _vm.Instances.Count + " paket";
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
