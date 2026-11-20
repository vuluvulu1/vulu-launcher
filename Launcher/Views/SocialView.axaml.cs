using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    internal sealed partial class SocialView : UserControl
    {
        public SocialView()
        {
            InitializeComponent();
            Themes.Attach(this);   // Ufkun öbür ucu: sayfanın kendi stillerinin üstüne
            KutTabs.ItemsSource = _links;
            ApplyLayout();
            // Tema aboneliği yalnızca sayfa ağaçtayken (uzun süre açılmayan sayfa PageHost'tan atılınca bellekte kalmasın)
            AttachedToVisualTree += (_, _) => { Themes.Changed -= ApplyLayout; Themes.Changed += ApplyLayout; ApplyLayout(); };
            DetachedFromVisualTree += (_, _) => Themes.Changed -= ApplyLayout;
            // Enter gönderir, Shift+Enter yeni satır
            Compose.AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && DataContext is SocialViewModel vm)
                {
                    e.Handled = true;
                    vm.SendCommand.Execute(null);
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

            // Yukarı kaydırınca daha eski mesajlar yüklenir
            // (yalnızca kullanıcı yukarı kaydırırken; yüklenince görünen yer kaymaz)
            ChatScroll.ScrollChanged += async (_, e) =>
            {
                if (e.OffsetDelta.Y >= 0 || ChatScroll.Offset.Y > 40 || DataContext is not SocialViewModel vm) return;
                double before = ChatScroll.Extent.Height;
                await vm.LoadOlderAsync();
                Dispatcher.UIThread.Post(() => ChatScroll.Offset = new Avalonia.Vector(0, Math.Max(0, ChatScroll.Extent.Height - before)), DispatcherPriority.Background);
            };

            DataContextChanged += (_, _) =>
            {
                if (DataContext is SocialViewModel vm)
                    vm.ScrollToEnd = () => Dispatcher.UIThread.Post(ChatScroll.ScrollToEnd, DispatcherPriority.Background);
                BindList(DataContext as SocialViewModel);
            };
        }

        // ── Kütüphane yerleşimi ──
        // Aynı parçalar başka yere konur: Akış/Liderlik/Harita üstte sekme, kişiler sağda "Arkadaşlar" çekmecesi, içerik solda.
        private readonly System.Collections.ObjectModel.ObservableCollection<SocialFeedLink> _links = new();
        private SocialViewModel? _listVm;

        private void BindList(SocialViewModel? vm)
        {
            if (_listVm == vm) return;
            if (_listVm != null) _listVm.List.CollectionChanged -= OnListChanged;
            _listVm = vm;
            if (vm != null) vm.List.CollectionChanged += OnListChanged;
            SyncLinks();
        }

        private void OnListChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => SyncLinks();

        private void SyncLinks()
        {
            _links.Clear();
            if (_listVm == null) return;
            foreach (var link in _listVm.List.OfType<SocialFeedLink>()) _links.Add(link);
        }

        private void ApplyLayout()
        {
            bool k = Themes.IsKutuphane;
            KutBar.IsVisible = k;
            Root.ColumnDefinitions[0].Width = k ? new Avalonia.Controls.GridLength(1, Avalonia.Controls.GridUnitType.Star) : new Avalonia.Controls.GridLength(300);
            Root.ColumnDefinitions[1].Width = k ? new Avalonia.Controls.GridLength(320) : new Avalonia.Controls.GridLength(1, Avalonia.Controls.GridUnitType.Star);
            Grid.SetColumn(SidePane, k ? 1 : 0);
            Grid.SetRow(SidePane, k ? 1 : 0);
            Grid.SetRowSpan(SidePane, k ? 1 : 2);
            SidePane.Margin = k ? new Avalonia.Thickness(0, 0, 20, 20) : default;
            if (!k) SidePane.ClearValue(MarginProperty);
            SideTitle.Text = k ? "Arkadaşlar" : "Sosyal";
            // NaN geçersiz yazı boyutu: atanınca istisna atar ve sayfa hiç kurulmaz (Kütüphane dışındaki temalarda Sosyal boş kalıyordu)
            if (k) SideTitle.FontSize = 18;
            else SideTitle.ClearValue(Avalonia.Controls.TextBlock.FontSizeProperty);
            FeedTitle.IsVisible = !k;
            foreach (Control pane in new Control[] { WorldPane, FeedPane, ChatPane })
            {
                Grid.SetColumn(pane, k ? 0 : 1);
                Grid.SetRow(pane, k ? 1 : 0);
                Grid.SetRowSpan(pane, k ? 1 : 2);
                pane.Margin = k ? new Avalonia.Thickness(2, -8, 12, 0) : default;
            }
        }
    }
}
