using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VuluLauncher.ViewModels;
using Avalonia.VisualTree;

namespace VuluLauncher.Views
{
    internal sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Başlık çubuğundan sürükleme (düğmelerin üstünde değilse)
            TitleBar.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !IsOnButton(e.Source as Visual))
                    BeginMoveDrag(e);
            };
            // Esc en üstteki kutuyu kapatır
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && DataContext is MainViewModel { Modals.Count: > 0 } vm)
                {
                    vm.Modals[^1].DismissCommand.Execute(null);
                    e.Handled = true;
                }
            };
        }

        private static bool IsOnButton(Visual? visual)
        {
            for (var v = visual; v != null; v = v.GetVisualParent())
                if (v is Button) return true;
            return false;
        }

        /// <summary>Pencere boyutu (DIP) ve arayüz ölçeği (Ayarlar → launcher boyutu, yazı ve arayüz boyutu).</summary>
        public void ApplyLayout(double width, double height, double scale)
        {
            Width = width;
            Height = height;
            Scaler.LayoutTransform = new ScaleTransform(scale, scale);
        }
    }
}
