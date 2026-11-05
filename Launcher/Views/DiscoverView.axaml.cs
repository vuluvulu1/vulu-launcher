using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    internal sealed partial class DiscoverView : UserControl
    {
        public DiscoverView() => InitializeComponent();

        /// <summary>Paket kartına tıklanınca ayrıntılar açılır (karttaki düğmeler kendi işini yapar).</summary>
        private void OnPackTapped(object? sender, TappedEventArgs e)
        {
            for (var v = e.Source as Avalonia.Visual; v != null && v != sender; v = v.GetVisualParent())
                if (v is Button) return;
            if (sender is Control { DataContext: PackCard card }) card.DetailsCommand.Execute(null);
        }
    }
}
