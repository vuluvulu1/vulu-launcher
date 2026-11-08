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
        /// <summary>Radyo menüsü açılınca türler ve istasyonlar yüklenir.</summary>
        private void OnRadioMenuOpened(object? sender, EventArgs e)
        {
            if (TopLevel.GetTopLevel(this)?.DataContext is MainViewModel main) _ = main.Radio.OpenMenuAsync();
        }

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
                if (panel == _dragControl || panel.DataContext is not InstanceItem { IsCustom: true } || panel.Classes.Contains("dragging")) continue;
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

        private bool _heroLeft;

        /// <summary>
        /// Paket şeridinin son düğmesi ("+ Yeni") paket bilgisinin üstünde bitiyorsa bilgi sola, şeridin altına kayar;
        /// şerit uzayınca ya da pencere alçalınca geri çekilir (web: syncHeroInset, .m-hero-left).
        /// </summary>
        private void SyncHeroInset()
        {
            if (!Hero.IsVisible || Hero.Bounds.Height <= 0 || !NewButton.IsVisible) return;
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

        public HomeView()
        {
            InitializeComponent();
            LayoutUpdated += (_, _) => SyncHeroInset();
            // Konsol en alttayken yeni satırlar gelince alta kayar; kullanıcı yukarı kaydırdıysa yerinde kalır
            DataContextChanged += (_, _) =>
            {
                if (DataContext is not HomeViewModel vm) return;
                vm.Console.CollectionChanged += (_, _) =>
                {
                    bool atBottom = ConsoleScroll.Offset.Y >= ConsoleScroll.Extent.Height - ConsoleScroll.Viewport.Height - 40;
                    if (atBottom) Dispatcher.UIThread.Post(ConsoleScroll.ScrollToEnd, DispatcherPriority.Background);
                };
            };
        }
    }
}
