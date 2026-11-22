using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Kapakları sürükleyerek sıralama: tutulan kapak imleçle gider, başka bir kapağın üstüne gelince onun yerine
    /// geçer (öbürleri kayar). Bırakınca sıralama "Özel"e geçer ve sıra saklanır; sonradan kurulan paketler sona eklenir.
    /// Süzgeç ya da arama açıkken sürüklenmez (görünen sıra bütün kütüphane değil).
    /// </summary>
    internal sealed partial class LibraryView
    {
        private const double DragThreshold = 8;
        private InstanceItem? _dragItem;
        private Control? _dragBox;
        private Point _dragStart, _dragGrab;
        private bool _dragging, _suppressTap;
        private Point _dragOffset;

        private bool CanDrag => _filter == "all" && string.IsNullOrWhiteSpace(SearchBox.Text);

        private void InitDrag()
        {
            Posters.AddHandler(PointerPressedEvent, OnDragPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            Posters.AddHandler(PointerMovedEvent, OnDragMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            Posters.AddHandler(PointerReleasedEvent, OnDragReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
            Posters.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(save: _dragging), RoutingStrategies.Bubble, handledEventsToo: true);
        }

        private void OnDragPressed(object? sender, PointerPressedEventArgs e)
        {
            _suppressTap = false;
            if (!e.GetCurrentPoint(Posters).Properties.IsLeftButtonPressed || !CanDrag) return;
            if (e.Source is not Visual v || v.FindAncestorOfType<Button>(true) != null) return;
            if (v.FindAncestorOfType<ListBoxItem>(true) is not { DataContext: InstanceItem item } box) return;
            _dragItem = item;
            _dragBox = box;
            _dragStart = e.GetPosition(Posters);
        }

        private void OnDragMoved(object? sender, PointerEventArgs e)
        {
            if (_dragItem == null || _dragBox == null) return;
            var p = e.GetPosition(Posters);
            if (!_dragging)
            {
                if (Math.Abs(p.X - _dragStart.X) < DragThreshold && Math.Abs(p.Y - _dragStart.Y) < DragThreshold) return;
                _dragging = true;
                e.Pointer.Capture(Posters);
                _dragBox.Transitions = null;
                _dragBox.ZIndex = 10;
                _dragBox.Opacity = 0.95;
                _dragGrab = _dragStart - LayoutPos(_dragBox);
                // Tutulan kapak hafifçe büyür ve gölge alır (kaldırılmış gibi)
                if (_dragBox.FindDescendantOfType<Border>() is { } art) art.BoxShadow = BoxShadows.Parse("0 18 36 -8 #CC000000");
            }

            // İmlecin altındaki kapak (sürüklenen hariç): imleç onun ortasına yaklaşınca yer değiştirilir (kenarına değer
            // değmez değil; yoksa iki kapak arasında gidip gelir), öbür kapaklar yeni yerlerine kayarak gider
            foreach (var other in _items.OfType<InstanceItem>())
            {
                if (other == _dragItem || Posters.ContainerFromItem(other) is not Control c) continue;
                var r = new Rect(LayoutPos(c), c.Bounds.Size);
                if (!r.Deflate(new Thickness(r.Width * 0.22, r.Height * 0.18)).Contains(p)) continue;
                int from = _items.IndexOf(_dragItem), to = _items.IndexOf(other);
                if (from >= 0 && to >= 0 && from != to)
                {
                    var before = SnapshotPositions();
                    _syncing = true;
                    _items.Move(from, to);
                    _syncing = false;
                    Posters.UpdateLayout();
                    SlideFrom(before);
                }
                break;
            }
            var pos = LayoutPos(_dragBox);
            _dragOffset = new Point(p.X - _dragGrab.X - pos.X, p.Y - _dragGrab.Y - pos.Y);
            _dragBox.RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(1.04, 1.04), new TranslateTransform(_dragOffset.X, _dragOffset.Y) },
            };
            e.Handled = true;
        }

        private void OnDragReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_dragging) { e.Pointer.Capture(null); e.Handled = true; }
            EndDrag(save: _dragging);
        }

        private void EndDrag(bool save)
        {
            var box = _dragBox;
            bool was = _dragging;
            _dragItem = null;
            _dragBox = null;
            _dragging = false;
            if (!was || box == null) return;
            _suppressTap = true;   // bırakış bir tıklama sayılmasın (paketin sayfası açılmasın)
            // Yerine yumuşakça oturur
            box.Transitions = new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
                new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) },
            };
            // Bırakılan kapak, imlecin yanından yeni yerine kayarak oturur
            box.Transitions = null;
            box.RenderTransform = new TranslateTransform(_dragOffset.X, _dragOffset.Y);
            var settle = box;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                settle.Transitions = new Avalonia.Animation.Transitions
                {
                    new Avalonia.Animation.TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new Avalonia.Animation.Easings.SplineEasing(0.2, 0.9, 0.3, 1) },
                    new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) },
                };
                settle.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("none");
                settle.Opacity = 1;
            }, Avalonia.Threading.DispatcherPriority.Background);
            if (box.FindDescendantOfType<Border>() is { } art) art.ClearValue(Border.BoxShadowProperty);
            box.ZIndex = 0;
            if (!save) return;

            // Sıra saklanır; sıralama "Özel"e geçer
            var order = _items.OfType<InstanceItem>().Select(i => i.Id).ToList();
            VuluLauncher.Native.UiState.Set("libraryOrder", string.Join("\n", order));
            _customOrder = order;
            if (_sort != "custom")
            {
                _sort = "custom";
                VuluLauncher.Native.UiState.Set("librarySort", "custom");
                SortBox.SelectedItem = SortBox.Items.OfType<ComboBoxItem>().First(i => (string?)i.Tag == "custom");
            }
        }

        private Dictionary<Control, Point> SnapshotPositions()
        {
            var map = new Dictionary<Control, Point>();
            foreach (var o in _items)
                if (Posters.ContainerFromItem(o) is Control c && c != _dragBox) map[c] = LayoutPos(c);
            return map;
        }

        /// <summary>Yer değiştiren kapaklar eski yerlerinden yenisine kayar (anında sıçramaz).</summary>
        private void SlideFrom(Dictionary<Control, Point> before)
        {
            foreach (var (c, old) in before)
            {
                var d = old - LayoutPos(c);
                if (Math.Abs(d.X) < 1 && Math.Abs(d.Y) < 1) continue;
                c.Transitions = null;
                c.RenderTransform = new TranslateTransform(d.X, d.Y);
                var box = c;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    box.Transitions = new Avalonia.Animation.Transitions
                    {
                        new Avalonia.Animation.TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new Avalonia.Animation.Easings.SplineEasing(0.2, 0.9, 0.3, 1) },
                    };
                    box.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("none");
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>Kapağın kaydırma alanındaki yerleşim konumu (sürükleme kaymasını saymadan).</summary>
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
