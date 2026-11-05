using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    internal sealed partial class CropView : UserControl
    {
        private Point? _dragStart;
        private double _startX, _startY;

        public CropView()
        {
            InitializeComponent();
            Frame.PointerPressed += (_, e) =>
            {
                if (DataContext is not CropModal m) return;
                _dragStart = e.GetPosition(Frame);
                _startX = m.X;
                _startY = m.Y;
                e.Pointer.Capture(Frame);
            };
            Frame.PointerMoved += (_, e) =>
            {
                if (_dragStart is not { } start || DataContext is not CropModal m) return;
                var p = e.GetPosition(Frame);
                m.MoveTo(_startX + p.X - start.X, _startY + p.Y - start.Y);
            };
            Frame.PointerReleased += (_, e) => { _dragStart = null; e.Pointer.Capture(null); };
            Frame.PointerWheelChanged += (_, e) =>
            {
                if (DataContext is not CropModal m) return;
                var p = e.GetPosition(Frame);
                m.ZoomTo(m.Scale * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), p.X, p.Y);
                e.Handled = true;
            };
        }
    }
}
