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
                _dragBox.Opacity = 0.92;
                _dragGrab = _dragStart - LayoutPos(_dragBox);
            }

            // İmlecin altındaki kapak (sürüklenen hariç): onun yerine geçilir
            foreach (var other in _items.OfType<InstanceItem>())
            {
                if (other == _dragItem || Posters.ContainerFromItem(other) is not Control c) continue;
                if (!new Rect(LayoutPos(c), c.Bounds.Size).Contains(p)) continue;
                int from = _items.IndexOf(_dragItem), to = _items.IndexOf(other);
                if (from >= 0 && to >= 0 && from != to)
                {
                    _syncing = true;
                    _items.Move(from, to);
                    _syncing = false;
                    Posters.UpdateLayout();
                }
                break;
            }
            var pos = LayoutPos(_dragBox);
            _dragBox.RenderTransform = new TranslateTransform(p.X - _dragGrab.X - pos.X, p.Y - _dragGrab.Y - pos.Y);
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
            box.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("none");
            box.Opacity = 1;
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
