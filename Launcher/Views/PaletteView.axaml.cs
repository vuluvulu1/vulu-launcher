using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    internal sealed partial class PaletteView : UserControl
    {
        public PaletteView()
        {
            InitializeComponent();
            // Kutu açılırken (giriş animasyonu, yerleşim) hemen verilen odak tutmuyordu: yerleşim bitince, sonra bir kez daha
            AttachedToVisualTree += (_, _) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(FocusSearch, Avalonia.Threading.DispatcherPriority.Loaded);
                Avalonia.Threading.DispatcherTimer.RunOnce(FocusSearch, TimeSpan.FromMilliseconds(120));
            };
            Search.AddHandler(KeyDownEvent, (_, e) =>
            {
                if (DataContext is not PaletteModal m) return;
                switch (e.Key)
                {
                    case Key.Down: m.Move(1); e.Handled = true; break;
                    case Key.Up: m.Move(-1); e.Handled = true; break;
                    case Key.Enter: m.Choose(); e.Handled = true; break;
                }
            }, RoutingStrategies.Tunnel);
        }

        private void FocusSearch()
        {
            if (!Search.IsFocused) Search.Focus(NavigationMethod.Tab);
            Search.CaretIndex = Search.Text?.Length ?? 0;
        }
    }
}
