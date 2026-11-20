using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    /// <summary>CurseForge / Modrinth araması (ContentSearchView.axaml). Görünür olunca arama kutusu odaklanır.</summary>
    internal sealed partial class ContentSearchView : UserControl
    {
        public ContentSearchView()
        {
            InitializeComponent();
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty && IsVisible) Focus();
            };
            AttachedToVisualTree += (_, _) => { if (IsEffectivelyVisible) Focus(); };
        }

        /// <summary>Kartın boş yerine tıklanınca ayrıntılar açılır (sürümler, galeri, sayfa); Kur düğmesi kendi işini yapar.</summary>
        private void OnHitTapped(object? sender, TappedEventArgs e)
        {
            for (var v = e.Source as Avalonia.Visual; v != null && v != sender; v = v.GetVisualParent())
                if (v is Button) return;
            if (sender is Control { DataContext: ContentHit hit }) hit.DetailsCommand.Execute(null);
        }

        private new void Focus() =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => QueryBox.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }
}
