using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VuluLauncher.ViewModels;

namespace VuluLauncher.ViewModels
{
    internal static class ServerStates
    {
        public static readonly IValueConverter IsChecking = new FuncValueConverter<string?, bool>(s => s == "checking");
    }
}

namespace VuluLauncher.Views
{
    /// <summary>Kütüphane temasının Sunucular sayfası (KutServersView.axaml). Süzgeç yalnızca görünümü daraltır.</summary>
    internal sealed partial class KutServersView : UserControl
    {
        private readonly ObservableCollection<ServerCard> _items = new();
        private readonly HashSet<ServerCard> _watched = new();
        private ServersViewModel? _vm;
        private string _filter = "all";
        private bool _rebuildQueued;

        public KutServersView()
        {
            InitializeComponent();
            Drawer.DataContext = null;   // sayfanın modelini devralmasın; seçili sunucu verilir
            Cards.ItemsSource = _items;
            Cards.SelectionChanged += (_, _) =>
            {
                if (Cards.SelectedItem is ServerCard card && card != Drawer.DataContext) ShowInDrawer(card);
            };
            DataContextChanged += (_, _) => Bind(DataContext as ServersViewModel);
        }

        private void ShowInDrawer(ServerCard? card)
        {
            if (Drawer.DataContext == card) return;
            bool had = Drawer.DataContext != null;
            // Çekmecedeki sunucunun sohbeti hep açık (yoklanır); önceki sunucununki durur
            if (Drawer.DataContext is ServerCard old && old != card) old.ChatOpen = false;
            if (card is { HasChat: true }) card.ChatOpen = true;
            Drawer.DataContext = card;
            if (had && card != null) KutPolish.Swap(Drawer);
        }

        private void Bind(ServersViewModel? vm)
        {
            if (_vm == vm) return;
            if (_vm != null) _vm.Servers.CollectionChanged -= OnServersChanged;
            _vm = vm;
            if (vm != null) vm.Servers.CollectionChanged += OnServersChanged;
            Rebuild();
        }

        // Liste doldurulurken her eklemede değil, hepsi gelince bir kez süzülür
        private void OnServersChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRebuild();

        private void QueueRebuild()
        {
            if (_rebuildQueued) return;
            _rebuildQueued = true;
            Dispatcher.UIThread.Post(() => { _rebuildQueued = false; Rebuild(); }, DispatcherPriority.Background);
        }

        /// <summary>Kartların durum ve oradaki arkadaş değişiklikleri dinlenir (sayılar ve süzgeç için); giden kartlar bırakılır.</summary>
        private void WatchCards()
        {
            var now = _vm?.Servers.ToHashSet() ?? new HashSet<ServerCard>();
            foreach (var gone in _watched.Where(c => !now.Contains(c)).ToList()) { gone.PropertyChanged -= OnCardChanged; _watched.Remove(gone); }
            foreach (var card in now.Where(_watched.Add)) card.PropertyChanged += OnCardChanged;
        }

        private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ServerCard.State) or nameof(ServerCard.FriendsHere)) QueueRebuild();
        }

        private void OnFilter(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag }) return;
            _filter = tag;
            foreach (var b in new[] { FilterAll, FilterOnline, FilterFriends }) b.Classes.Set("on", b == sender);
            Rebuild();
        }

        private void Rebuild()
        {
            WatchCards();
            if (_vm == null) { _items.Clear(); ShowInDrawer(null); NoMatch.IsVisible = false; return; }
            var selected = Drawer.DataContext as ServerCard;
            _items.SyncTo(_vm.Servers.Where(s => _filter switch { "online" => s.IsOnline, "friends" => s.HasFriendsHere, _ => true }).ToList());

            int open = _vm.Servers.Count(s => s.IsOnline), friends = _vm.Servers.Count(s => s.HasFriendsHere);
            FilterAll.Content = KutPolish.ChipText("Tümü", _vm.Servers.Count);
            FilterOnline.Content = KutPolish.ChipText("Açık olanlar", open);
            FilterFriends.Content = KutPolish.ChipText("Arkadaşlarım orada", friends);
            CountText.IsVisible = false;   // sayılar haplarda
            if (FilterAll.Parent is Control chips) chips.IsVisible = _vm.Servers.Count > 0;
            NoMatch.IsVisible = _items.Count == 0 && _vm.Servers.Count > 0;

            var pick = selected != null && _items.Contains(selected) ? selected : _items.FirstOrDefault();
            if (Cards.SelectedItem != pick) Cards.SelectedItem = pick;
            ShowInDrawer(pick ?? (selected != null && _vm.Servers.Contains(selected) ? selected : _vm.Servers.FirstOrDefault()));
        }
    }
}
