using Avalonia.Controls;

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

        private new void Focus() =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => QueryBox.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }
}
