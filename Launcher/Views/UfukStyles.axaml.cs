using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace VuluLauncher.Views
{
    /// <summary>"Ufkun öbür ucu" temasının ışıltıları (UfukStyles.axaml); Services/Themes.cs ekler ve çıkarır.</summary>
    public partial class UfukStyles : Styles
    {
        public UfukStyles() => AvaloniaXamlLoader.Load(this);
    }
}
