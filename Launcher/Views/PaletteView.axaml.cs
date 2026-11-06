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
            AttachedToVisualTree += (_, _) => Search.Focus();
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
    }
}
