using Avalonia.Controls;
using Avalonia.Input;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    internal sealed partial class ToastCard : UserControl
    {
        public ToastCard()
        {
            InitializeComponent();
            Themes.Attach(this);   // temaya özgü stiller kartın kendi stillerinin üstüne
        }

        /// <summary>Kartın üstüne gelince süresi durur.</summary>
        private void OnPointer(object? sender, PointerEventArgs e)
        {
            if (DataContext is ToastItem toast) toast.Paused = e.RoutedEvent == PointerEnteredEvent;
        }
    }
}
