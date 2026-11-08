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

        // ── Şeritte sürükleyerek sıralama (web: wireCardDrag): sürüklenen kart solar, bırakılınca hedefin yerine geçer ──
        private InstanceItem? _dragItem;
        private Control? _dragControl, _dropControl;
        private Avalonia.Point _dragStart;
        private bool _dragging;

        private void OnPackPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (sender is not Control { DataContext: InstanceItem { IsCustom: true } item } control) return;
            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;
            _dragItem = item;
            _dragControl = control;
            _dragStart = e.GetPosition(this);
            _dragging = false;
        }

        private void OnPackMoved(object? sender, Avalonia.Input.PointerEventArgs e)
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
                _dragControl.Opacity = .45;
            }
            // İmlecin altındaki kişisel instance hedef olur ve hafifçe vurgulanır
            Control? target = null;
            for (var v = this.InputHitTest(position) as Avalonia.Visual; v != null; v = v.GetVisualParent())
                if (v is StackPanel { DataContext: InstanceItem { IsCustom: true } item } panel && item != _dragItem) { target = panel; break; }
            if (target == _dropControl) return;
            if (_dropControl != null) _dropControl.Opacity = 1;
            _dropControl = target;
            if (_dropControl != null) _dropControl.Opacity = .75;
        }

        private void OnPackReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            var (item, target, dragging) = (_dragItem, _dropControl?.DataContext as InstanceItem, _dragging);
            EndDrag();
            if (!dragging || item == null || target == null || DataContext is not HomeViewModel vm) return;
            vm.MoveInstance(item, target);
            vm.SaveInstanceOrder();
            e.Handled = true;
        }

        private void OnPackCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
        {
            if (_dragging) EndDrag();
        }

        private void EndDrag()
        {
            if (_dragControl != null) _dragControl.Opacity = 1;
            if (_dropControl != null) _dropControl.Opacity = 1;
            _dragItem = null;
            _dragControl = _dropControl = null;
            _dragging = false;
        }

        public HomeView()
        {
            InitializeComponent();
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
