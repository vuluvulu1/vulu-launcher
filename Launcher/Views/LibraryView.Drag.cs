using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Kapakları sürükleyerek sıralama.
    ///   • Kapağa basmak onu seçmez (arka plan değişmez); bırakınca sürüklenmediyse tıklama sayılır (paketin sayfası açılır).
    ///   • Sürükleme başlayınca kapağın görüntüsü pencerenin en üst katmanında (DragLayer) imleci izler: hiçbir yerde kesilmez;
    ///     kapağın asıl yeri soluk bir yer tutucu olarak kalır.
    ///   • İmleç başka bir kapağın ortasına yaklaşınca yer tutucu oraya geçer, öbür kapaklar yeni yerlerine kayarak gider.
    ///   • Bırakınca görüntü yer tutucunun üstüne süzülüp oturur; sıralama "Özel"e geçer ve sıra saklanır (sonradan
    ///     kurulan paketler sona eklenir). Süzgeç ya da arama açıkken sürüklenmez, yalnızca tıklanır.
    /// </summary>
    internal sealed partial class LibraryView
    {
        private const double DragThreshold = 6;
        private static readonly Easing Soft = new SplineEasing(0.2, 0.9, 0.3, 1);

        private InstanceItem? _pressItem;
        private Point _pressAt, _grab;
        private bool _dragging;
        private Border? _ghost;

        private bool CanDrag => _filter == "all" && string.IsNullOrWhiteSpace(SearchBox.Text);

        private void InitDrag()
        {
            Posters.AddHandler(PointerPressedEvent, OnDragPressed, RoutingStrategies.Tunnel);
            Posters.AddHandler(PointerMovedEvent, OnDragMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            Posters.AddHandler(PointerReleasedEvent, OnDragReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
            Posters.AddHandler(PointerCaptureLostEvent, (_, _) => { if (_dragging) FinishDrag(); _pressItem = null; }, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        private void OnDragPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(Posters).Properties.IsLeftButtonPressed) return;
            if (e.Source is not Visual v || v.FindAncestorOfType<Button>(true) != null) return;   // ▶ ve öbür düğmeler kendi işini yapar
            if (v.FindAncestorOfType<ListBoxItem>(true) is not { DataContext: InstanceItem item }) return;
            // Seçim basınca değil, bırakınca (tıklamaysa) yapılır: sürüklerken arka plan değişmesin
            _pressItem = item;
            _pressAt = e.GetPosition(Posters);
            e.Pointer.Capture(Posters);
            e.Handled = true;
        }

        private void OnDragMoved(object? sender, PointerEventArgs e)
        {
            if (_pressItem == null) return;
            var p = e.GetPosition(Posters);
            if (!_dragging)
            {
                if (!CanDrag || Math.Abs(p.X - _pressAt.X) < DragThreshold && Math.Abs(p.Y - _pressAt.Y) < DragThreshold) return;
                if (!StartDrag()) { _pressItem = null; return; }
            }
            MoveGhost(e.GetPosition(DragLayer));
            Reorder(p);
            e.Handled = true;
        }

        private void OnDragReleased(object? sender, PointerReleasedEventArgs e)
        {
            var item = _pressItem;
            if (item == null) return;
            e.Handled = true;
            if (_dragging) { FinishDrag(); _pressItem = null; e.Pointer.Capture(null); return; }
            _pressItem = null;
            e.Pointer.Capture(null);
            // Sürüklenmedi: tıklama — seçilir ve paketin sayfası açılır
            if (_vm == null) return;
            _syncing = true;
            Posters.SelectedItem = item;
            _syncing = false;
            if (_vm.Selected != item) _vm.Selected = item;
            _vm.OpenPageCommand.Execute(item);
        }

        // ── Sürükleme ──
        private bool StartDrag()
        {
            if (_pressItem == null || Posters.ContainerFromItem(_pressItem) is not Control box || box.Bounds.Width < 1) return false;
            _dragging = true;
            box.Transitions = null;
            box.RenderTransform = null;
            var origin = box.TranslatePoint(default, DragLayer) ?? default;
            _grab = _pressAt - LayoutPos(box);

            // Kapağın görüntüsü üst katmanda imleci izler
            var size = box.Bounds.Size;
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            var shot = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale))),
                new Vector(96 * scale, 96 * scale));
            shot.Render(box);
            _ghost = new Border
            {
                Width = size.Width,
                Height = size.Height,
                Child = new Image { Source = shot, Stretch = Stretch.Fill },
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = TransformOperations.Parse("scale(1) rotate(0deg)"),
                Transitions = new Transitions
                {
                    new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = Soft },
                },
            };
            Canvas.SetLeft(_ghost, origin.X);
            Canvas.SetTop(_ghost, origin.Y);
            DragLayer.Children.Add(_ghost);
            // Hafifçe büyüyüp eğilir (kaldırılmış gibi)
            var lift = _ghost;
            Dispatcher.UIThread.Post(() => lift.RenderTransform = TransformOperations.Parse("scale(1.05) rotate(-1.5deg)"), DispatcherPriority.Background);

            // Asıl yer: soluk yer tutucu
            SetPlaceholder(box, true);
            return true;
        }

        private void MoveGhost(Point pointerInLayer)
        {
            if (_ghost == null) return;
            Canvas.SetLeft(_ghost, pointerInLayer.X - _grab.X);
            Canvas.SetTop(_ghost, pointerInLayer.Y - _grab.Y);
        }

        /// <summary>
        /// İmleç başka bir kapağın ortasına yaklaşınca yer tutucu oraya geçer; öbür kapaklar kayarak yer açar. Bir grubun
        /// başlığının üstüne gelince kapak o grubun başına geçer (bırakınca o gruba taşınır).
        /// </summary>
        private void Reorder(Point p)
        {
            if (_pressItem is not { } dragged) return;
            foreach (var other in _items.Where(o => o is InstanceItem or GroupHeader).ToList())
            {
                if (other == dragged || Posters.ContainerFromItem(other) is not Control c) continue;
                var r = new Rect(LayoutPos(c), c.Bounds.Size);
                bool header = other is GroupHeader;
                if (!(header ? r : r.Deflate(new Thickness(r.Width * 0.2, r.Height * 0.15))).Contains(p)) continue;
                int from = _items.IndexOf(dragged), to = _items.IndexOf(other);
                if (header && from > to) to++;   // başlığın hemen arkası
                if (from < 0 || to < 0 || from == to) return;
                var before = SnapshotPositions();
                _syncing = true;
                _items.Move(from, to);
                _syncing = false;
                Posters.UpdateLayout();
                SlideFrom(before, dragged);
                // Kap yeniden kurulmuş olabilir: yer tutucu yeniden işaretlenir
                if (Posters.ContainerFromItem(dragged) is Control placeholder) SetPlaceholder(placeholder, true);
                return;
            }
        }

        private void FinishDrag()
        {
            _dragging = false;
            var ghost = _ghost;
            _ghost = null;
            var target = _pressItem != null ? Posters.ContainerFromItem(_pressItem) as Control : null;

            // Görüntü yer tutucunun üstüne süzülür, sonra kalkar ve asıl kapak belirir
            if (ghost != null && target != null && target.TranslatePoint(default, DragLayer) is { } end)
            {
                ghost.Transitions = new Transitions
                {
                    new DoubleTransition { Property = Canvas.LeftProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = Soft },
                    new DoubleTransition { Property = Canvas.TopProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = Soft },
                    new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = Soft },
                };
                Canvas.SetLeft(ghost, end.X);
                Canvas.SetTop(ghost, end.Y);
                ghost.RenderTransform = TransformOperations.Parse("scale(1) rotate(0deg)");
                DispatcherTimer.RunOnce(() =>
                {
                    SetPlaceholder(target, false);
                    DragLayer.Children.Remove(ghost);
                }, TimeSpan.FromMilliseconds(230));
            }
            else
            {
                if (ghost != null) DragLayer.Children.Remove(ghost);
                if (target != null) SetPlaceholder(target, false);
            }

            // Grubu değiştiyse (başka bir grubun arasına bırakıldı) yeni yerine göre yeniden dizilir
            if (_pressItem != null && AssignFromPosition(_pressItem))
                DispatcherTimer.RunOnce(Rebuild, TimeSpan.FromMilliseconds(260));

            // Sıra saklanır (katlı gruplardaki paketler görünmese de sıradaki yerlerini korur); sıralama "Özel"e geçer
            var order = _items.OfType<InstanceItem>().Select(i => i.Id).ToList();
            order.AddRange(CustomOrder.Where(id => !order.Contains(id)));
            VuluLauncher.Native.UiState.Set("libraryOrder", string.Join("\n", order));
            _customOrder = order;
            if (_sort != "custom")
            {
                _sort = "custom";
                VuluLauncher.Native.UiState.Set("librarySort", "custom");
                SortBox.SelectedItem = SortBox.Items.OfType<ComboBoxItem>().First(i => (string?)i.Tag == "custom");
            }
        }

        private static void SetPlaceholder(Control box, bool on)
        {
            box.Transitions = null;
            box.Opacity = on ? 0.2 : 1;
        }

        private Dictionary<object, Point> SnapshotPositions()
        {
            var map = new Dictionary<object, Point>();
            foreach (var o in _items)
                if (Posters.ContainerFromItem(o) is Control c) map[o] = LayoutPos(c);
            return map;
        }

        /// <summary>Kayma başlangıcı: geçiş yalnızca TransformOperations'tan TransformOperations'a canlanıyor.</summary>
        private static TransformOperations Shift(double x, double y)
        {
            var b = TransformOperations.CreateBuilder(1);
            b.AppendTranslate(x, y);
            return b.Build();
        }

        /// <summary>Yer değiştiren kapaklar eski yerlerinden yenisine kayar (anında sıçramaz). Yer tutucu kaymaz, yerine geçer.</summary>
        private void SlideFrom(Dictionary<object, Point> before, object placeholder)
        {
            foreach (var (item, old) in before)
            {
                if (item == placeholder || Posters.ContainerFromItem(item) is not Control c) continue;
                var d = old - LayoutPos(c);
                if (Math.Abs(d.X) < 1 && Math.Abs(d.Y) < 1) continue;
                c.Transitions = null;
                c.RenderTransform = Shift(d.X, d.Y);
                var box = c;
                Dispatcher.UIThread.Post(() =>
                {
                    box.Transitions = new Transitions
                    {
                        new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = Soft },
                    };
                    box.RenderTransform = Shift(0, 0);
                }, DispatcherPriority.Background);
            }
        }

        /// <summary>Kapağın kaydırma alanındaki yerleşim konumu (kayma animasyonunu saymadan).</summary>
        private Point LayoutPos(Control box)
        {
            var parent = box.GetVisualParent<Visual>();
            var origin = parent?.TranslatePoint(default, Posters) ?? default;
            return origin + box.Bounds.Position;
        }

        // ── Özel sıra ──
        private List<string>? _customOrder;

        private List<string> CustomOrder => _customOrder ??=
            (VuluLauncher.Native.UiState.Get("libraryOrder") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

        /// <summary>Saklı sıraya göre; sırada olmayanlar (sonradan kurulanlar) sona, kuruluş sırasıyla.</summary>
        private IEnumerable<InstanceItem> OrderCustom(IEnumerable<InstanceItem> items)
        {
            var index = CustomOrder.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
            return items.OrderBy(i => index.TryGetValue(i.Id, out int n) ? n : int.MaxValue)
                        .ThenBy(i => i.CreatedAt ?? DateTime.MinValue);
        }
    }
}
