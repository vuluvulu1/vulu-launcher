using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace VuluLauncher.Views
{
    /// <summary>Kütüphane ve Meşale'nin ortak yuvarlak görünümü (SoftStyles.axaml); pencere "soft" sınıfını taşırken geçerli.</summary>
    public partial class SoftStyles : Styles
    {
        public SoftStyles() => AvaloniaXamlLoader.Load(this);
    }
}
