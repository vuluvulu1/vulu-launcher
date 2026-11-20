using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    internal sealed partial class HomeView : UserControl
    {
        /// <summary>Sağ tıklanan paket seçilir; menüdeki işlemler seçili pakete uygulanır.</summary>
        private void OnPackContextRequested(object? sender, Avalonia.Input.ContextRequestedEventArgs e)
        {
            if (sender is not Control { DataContext: InstanceItem item } target || DataContext is not HomeViewModel vm) return;
            if (vm.Selected != item) vm.Selected = item;
            // Menü ayrı bir katmanda açılır; öğeler komutlarını doğrudan Oyna modelinden alır
            if (target.ContextFlyout is MenuFlyout menu)
                foreach (var entry in menu.Items.OfType<Control>()) entry.DataContext = vm;
        }

        // ── Şeritte sürükleyerek sıralama (web: wireCardDrag) ──
        // Sürüklenen kart imleci takip eder, biraz büyür ve amber halkayla parlar; bırakılacağı kart amber çerçeveyle
        // işaretlenir. Bırakınca sürüklenen kart hedefin yerine geçer.
        private InstanceItem? _dragItem;
        private Control? _dragControl, _dropControl, _dragContainer;
        private Avalonia.Point _dragStart;
        private bool _dragging;

        private void OnPackPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control { DataContext: InstanceItem { IsCustom: true } item } control) return;
            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;
            _dragItem = item;
            _dragControl = control;
            _dragStart = e.GetPosition(this);
            _dragging = false;
        }

        private void OnPackMoved(object? sender, PointerEventArgs e)
        {
            if (_dragItem == null || _dragControl == null || DataContext is not HomeViewModel vm) return;
            var position = e.GetPosition(this);
            if (!_dragging)
            {
                // Küçük kaymalar tıklama sayılır
                if (Math.Abs(position.Y - _dragStart.Y) < 6) return;
                if (vm.Instances.Count(i => i.IsCustom) < 2) { _dragItem = null; return; }
                _dragging = true;
                e.Pointer.Capture(_dragControl);
                _dragControl.Classes.Add("dragging");
                // Kart öbür kartların üstünde gezsin
                _dragContainer = _dragControl.FindAncestorOfType<ListBoxItem>();
                if (_dragContainer != null) _dragContainer.ZIndex = 100;
            }
            double dy = position.Y - _dragStart.Y;
            _dragControl.RenderTransform = new Avalonia.Media.TransformGroup
            {
                Children = { new Avalonia.Media.ScaleTransform(1.08, 1.08), new Avalonia.Media.TranslateTransform(0, dy) },
            };

            // İmlecin hizasındaki öbür kişisel instance hedef olur
            Control? target = null;
            foreach (var panel in this.GetVisualDescendants().OfType<StackPanel>())
            {
                if (panel == _dragControl || !panel.Classes.Contains("pack") || panel.DataContext is not InstanceItem { IsCustom: true } || panel.Classes.Contains("dragging")) continue;
                if (panel.TranslatePoint(new Avalonia.Point(0, 0), this) is not { } top) continue;
                if (position.Y >= top.Y && position.Y <= top.Y + panel.Bounds.Height) { target = panel; break; }
            }
            if (target == _dropControl) return;
            _dropControl?.Classes.Remove("drop");
            _dropControl = target;
            _dropControl?.Classes.Add("drop");
        }

        private void OnPackReleased(object? sender, PointerReleasedEventArgs e)
        {
            var (item, target, dragging) = (_dragItem, _dropControl?.DataContext as InstanceItem, _dragging);
            EndDrag();
            if (!dragging || item == null || target == null || DataContext is not HomeViewModel vm) return;
            vm.MoveInstance(item, target);
            vm.SaveInstanceOrder();
            e.Handled = true;
        }

        private void OnPackCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_dragging) EndDrag();
        }

        private void EndDrag()
        {
            if (_dragControl != null)
            {
                _dragControl.Classes.Remove("dragging");
                _dragControl.RenderTransform = null;
            }
            if (_dragContainer != null) _dragContainer.ZIndex = 0;
            _dropControl?.Classes.Remove("drop");
            _dragItem = null;
            _dragControl = _dropControl = _dragContainer = null;
            _dragging = false;
        }

        private HomeViewModel? _consoleVm;
        private bool _consoleScrollQueued;

        private ScrollViewer? ConsoleScroll => ConsoleList.FindDescendantOfType<ScrollViewer>();

        private void OnConsoleChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_consoleScrollQueued || !ConsoleList.IsEffectivelyVisible || ConsoleScroll is not { } scroll) return;
            bool atBottom = scroll.Offset.Y >= scroll.Extent.Height - scroll.Viewport.Height - 40;
            if (!atBottom) return;
            _consoleScrollQueued = true;
            Dispatcher.UIThread.Post(() => { _consoleScrollQueued = false; ConsoleScroll?.ScrollToEnd(); }, DispatcherPriority.Background);
        }

        private bool _heroLeft;

        /// <summary>
        /// Paket şeridinin son düğmesi ("+ Yeni") paket bilgisinin üstünde bitiyorsa bilgi sola, şeridin altına kayar;
        /// şerit uzayınca ya da pencere alçalınca geri çekilir (web: syncHeroInset, .m-hero-left).
        /// </summary>
        private void SyncHeroInset()
        {
            if (Themes.IsGrafit || !IsEffectivelyVisible || !Hero.IsVisible || Hero.Bounds.Height <= 0 || !NewButton.IsVisible) return;
            var last = NewButton.TranslatePoint(new Point(0, NewButton.Bounds.Height), this);
            var top = Hero.TranslatePoint(new Point(0, 0), this);
            if (last is null || top is null) return;
            bool free = last.Value.Y + 14 < top.Value.Y;
            if (free == _heroLeft) return;
            _heroLeft = free;
            Grid.SetColumn(Hero, free ? 0 : 1);
            Grid.SetColumnSpan(Hero, free ? 2 : 1);
            Hero.Margin = free ? new Thickness(18, 0, 20, 18) : new Thickness(6, 0, 20, 18);
        }

        // ── Grafit: kartlı yerleşim ──
        // Öğeler aynı kalır (aynı bağlamalar, aynı komutlar); yalnızca yerleri ve boyutları değişir:
        //   solda kart içinde adlarıyla paket listesi ve altında radyo, ortada paket kartı (resim üstte, yazılar altta,
        //   oyun çubuğu kartın dibinde), sağda tam boy bilgi paneli. Meşale ve sahneli temalarda eski yerleşim.
        private const double Gap = 14, LeftWidth = 264, RightWidth = 314, ArtHeight = 290;
        private bool? _grafit;
        private Avalonia.Media.IEffect? _heroTitleEffect;

        private void ApplyLayout()
        {
            // Kütüphane: Oyna'nın kendi görünümü (LibraryView); eski düzen gizlenir ve hiç çizilmez
            bool kut = Themes.IsKutuphane;
            Library.IsVisible = kut;
            Classic.IsVisible = !kut;
            bool g = Themes.IsGrafit;
            if (_grafit == g) return;
            _grafit = g;
            var card = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#18191B"));
            var line = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#222428"));
            double left = Gap + LeftWidth, right = Gap + RightWidth, top = 50 + Gap;

            CenterCard.IsVisible = g;
            CenterCard.Margin = new Thickness(left, top, right, Gap);

            Grid.SetRowSpan(StageBox, g ? 2 : 1);
            StageBox.Margin = g ? new Thickness(left + 1, top + 1, right + 1, 0) : default;
            StageBox.Height = g ? ArtHeight : double.NaN;
            StageBox.VerticalAlignment = g ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Stretch;
            StageBox.CornerRadius = g ? new CornerRadius(13, 13, 0, 0) : default;
            ShadeSide.IsVisible = ShadeBottom.IsVisible = !g;
            Announce.Margin = g ? new Thickness(left + 14, top + 14, 0, 0) : new Thickness(100, 58, 394, 0);

            Grid.SetRowSpan(Body, g ? 2 : 1);
            Body.Margin = g ? new Thickness(Gap, top, Gap, Gap) : new Thickness(0, 50, 0, 0);
            Body.ColumnDefinitions[0].Width = new GridLength(g ? LeftWidth + Gap : 94);
            Body.ColumnDefinitions[2].Width = new GridLength(g ? RightWidth + Gap : 356);

            RailCard.Margin = g ? new Thickness(0, 0, Gap, 0) : default;
            RailCard.Background = g ? card : null;
            RailCard.BorderBrush = g ? line : null;
            RailCard.BorderThickness = new Thickness(g ? 1 : 0);
            RailCard.CornerRadius = new CornerRadius(g ? 14 : 0);
            RailCard.ClipToBounds = g;
            RailHeader.IsVisible = g;
            RadioHost.IsVisible = false;   // radyo artık her temada başlık çubuğundaki mini çalarda (Views/RadioPill)
            Rail.Padding = g ? new Thickness(8, 0, 8, 0) : new Thickness(18, 10, 12, 0);
            NewButton.Width = g ? double.NaN : 58;
            NewButton.Height = g ? 44 : 58;
            NewButton.Margin = g ? new Thickness(8, 4, 8, 0) : new Thickness(18, 0, 0, 0);
            NewButton.HorizontalAlignment = g ? Avalonia.Layout.HorizontalAlignment.Stretch : Avalonia.Layout.HorizontalAlignment.Left;
            NewButton.CornerRadius = new CornerRadius(g ? 10 : 0);
            NewButton.Content = g ? "+ Yeni paket" : "+ Yeni";

            _heroTitleEffect ??= HeroTitle.Effect;
            _heroLeft = false;
            Grid.SetColumn(Hero, 1);
            Grid.SetColumnSpan(Hero, 1);
            Hero.VerticalAlignment = g ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Bottom;
            Hero.Margin = g ? new Thickness(24, ArtHeight + 17, 24, 0) : new Thickness(6, 0, 20, 18);
            HeroTitle.FontSize = g ? 46 : 96;
            HeroTitle.FontWeight = g ? Avalonia.Media.FontWeight.Medium : Avalonia.Media.FontWeight.Light;
            HeroTitle.LetterSpacing = g ? -1.4 : -4.2;
            HeroTitle.Margin = g ? default : new Thickness(-4, 0, 0, 0);
            HeroTitle.Effect = g ? null : _heroTitleEffect;
            HeroLogo.MaxHeight = g ? 72 : 130;
            HeroLogo.MaxWidth = g ? 360 : 480;

            // Sayılar: Grafit'te etiket ve kutular; öbürlerinde tek satır yazı
            StatsLine.IsVisible = !g;
            ChipsRow.IsVisible = StatsRow.IsVisible = g;

            // Bilgi paneli: Grafit'te sağda hep yerinde; öbürlerinde "Ayrıntılar" ile sağdan kayan tam boy çekmece
            InfoPanel.Margin = g ? new Thickness(Gap, 0, 0, 0) : new Thickness(0, 8, 18, 16);
            InfoPanel.MaxHeight = double.PositiveInfinity;
            InfoPanel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            InfoPanel.Classes.Set("drawer", !g);
            DetailsButton.IsVisible = DetailsClose.IsVisible = !g;

            // Oyun çubuğu ortadaki kartın dibine oturur; seçili paketin adı zaten kartta yazdığı için çubukta gizlenir
            Bar.Margin = g ? new Thickness(left + 1, 0, right + 1, Gap + 1) : default;
            BarPack.IsVisible = !g;
            BarGrid.ColumnDefinitions[0].Width = new GridLength(g ? 0 : 210);

            // Radyo: Grafit'te sol kartın altına, iki satır (istasyon üstte, düğmeler altta)
            if (RadioBox.Parent is Panel oldPanel) oldPanel.Children.Remove(RadioBox);
            else if (RadioBox.Parent is Decorator oldHost) oldHost.Child = null;
            // Öbür temalarda radyo başlık çubuğunda (MainWindow RadioHost); oyun çubuğunda yalnızca paket, ilerleme, sürüm ve OYNA
            if (g) RadioHost.Child = RadioBox;
            else BarGrid.Children.Add(RadioBox);
            RadioBox.IsVisible = false;   // eski çalar kullanılmıyor; radyo başlık çubuğundaki mini çalarda
            Grid.SetColumn(RadioBox, g ? 0 : 1);
            RadioBox.Margin = g ? default : new Thickness(8, 0, 0, 0);
            RadioBox.RowSpacing = g ? 8 : 0;
            Grid.SetRow(RadioStation, 0);
            Grid.SetColumn(RadioStation, g ? 0 : 3);
            Grid.SetColumnSpan(RadioStation, g ? 4 : 1);
            RadioStation.MaxWidth = g ? double.PositiveInfinity : 190;
            foreach (var (control, column) in new (Control, int)[] { (RadioPlay, 0), (RadioMute, 1), (RadioVolume, 2) })
            {
                Grid.SetRow(control, g ? 1 : 0);
                Grid.SetColumn(control, column);
            }
            RadioVolume.Width = g ? 120 : 76;
        }

        public HomeView()
        {
            InitializeComponent();
            // Ufkun öbür ucu: sayfanın kendi stillerinin üstüne temanın ışıltıları
            Themes.Attach(this);
            ApplyLayout();
            Themes.Changed += ApplyLayout;
            LayoutUpdated += (_, _) => SyncHeroInset();
            // Konsol en alttayken yeni satırlar gelince alta kayar; kullanıcı yukarı kaydırdıysa yerinde kalır. Bir grup
            // satır (oyun çıktısı 80 ms'de bir toplu gelir) için tek kaydırma yapılır.
            // Konsol sekmesi açılınca en son satır görünür
            ConsoleList.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty && ConsoleList.IsVisible)
                    Dispatcher.UIThread.Post(() => ConsoleScroll?.ScrollToEnd(), DispatcherPriority.Background);
            };
            DataContextChanged += (_, _) =>
            {
                if (DataContext is not HomeViewModel vm || _consoleVm == vm) return;
                if (_consoleVm != null) _consoleVm.Console.CollectionChanged -= OnConsoleChanged;
                _consoleVm = vm;
                vm.Console.CollectionChanged += OnConsoleChanged;
            };
        }
    }
}
