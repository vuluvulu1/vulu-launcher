using Avalonia;
using Avalonia.Controls;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    /// <summary>Radyo menüsü (RadioPanel.axaml); açılınca türler ve istasyonlar yüklenir.</summary>
    internal sealed partial class RadioPanel : UserControl
    {
        public RadioPanel()
        {
            InitializeComponent();
            // Menünün DataContext'i radyo; yoksa (Kütüphane rayında) pencerenin radyosu
            AttachedToVisualTree += (_, _) =>
            {
                if (DataContext is not RadioViewModel && TopLevel.GetTopLevel(this) is { } top)
                {
                    var main = (top as Window)?.DataContext as MainViewModel
                               ?? (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.DataContext as MainViewModel;
                    if (main != null) DataContext = main.Radio;
                }
                if (DataContext is RadioViewModel radio) _ = radio.OpenMenuAsync();
            };
        }
    }
}
