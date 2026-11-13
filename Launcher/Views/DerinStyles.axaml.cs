using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace VuluLauncher.Views
{
    /// <summary>"Derin Karanlık" temasının ışıltıları (DerinStyles.axaml); pencere "derin" sınıfını taşırken geçerli.</summary>
    public partial class DerinStyles : Styles
    {
        public DerinStyles() => AvaloniaXamlLoader.Load(this);
    }
}
