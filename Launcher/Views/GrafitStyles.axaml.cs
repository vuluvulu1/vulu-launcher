using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace VuluLauncher.Views
{
    /// <summary>"Grafit" temasının kart, sekme ve düğme biçimleri (GrafitStyles.axaml); pencere "grafit" sınıfını taşırken geçerli.</summary>
    public partial class GrafitStyles : Styles
    {
        public GrafitStyles() => AvaloniaXamlLoader.Load(this);
    }
}
