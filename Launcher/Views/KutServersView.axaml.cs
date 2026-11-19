using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
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
        private ServersViewModel? _vm;
        private string _filter = "all";

        public KutServersView()
        {
            InitializeComponent();
            Drawer.DataContext = null;   // sayfanın modelini devralmasın; seçili sunucu verilir
            Cards.ItemsSource = _items;
            Cards.SelectionChanged += (_, _) => Drawer.DataContext = Cards.SelectedItem as ServerCard ?? Drawer.DataContext;
            DataContextChanged += (_, _) => Bind(DataContext as ServersViewModel);
        }

        private void Bind(ServersViewModel? vm)
        {
            if (_vm == vm) return;
            if (_vm != null) _vm.Servers.CollectionChanged -= OnServersChanged;
            _vm = vm;
            if (vm != null) vm.Servers.CollectionChanged += OnServersChanged;
            Rebuild();
        }

        private void OnServersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (ServerCard card in e.NewItems) card.PropertyChanged += OnCardChanged;
            Rebuild();
        }

        // Durum ya da oradaki arkadaşlar değişince süzgeç yeniden uygulanır
        private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_filter != "all" && e.PropertyName is nameof(ServerCard.State) or nameof(ServerCard.FriendsHere)) Rebuild();
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
            var selected = Drawer.DataContext as ServerCard;
            _items.Clear();
            if (_vm == null) return;
            foreach (var s in _vm.Servers.Where(s => _filter switch { "online" => s.IsOnline, "friends" => s.HasFriendsHere, _ => true }))
                _items.Add(s);
            int open = _vm.Servers.Count(s => s.IsOnline);
            CountText.Text = $"{_vm.Servers.Count} sunucu · {open} açık";
            var pick = selected != null && _items.Contains(selected) ? selected : _items.FirstOrDefault();
            Cards.SelectedItem = pick;
            Drawer.DataContext = pick ?? (selected != null && _vm.Servers.Contains(selected) ? selected : _vm.Servers.FirstOrDefault());
        }
    }
}
