using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace VuluLauncher.Views
{
    /// <summary>"Kütüphane" temasının iskeleti ve biçimleri (KutuphaneStyles.axaml); pencere "kutuphane" sınıfını taşırken geçerli.</summary>
    public partial class KutuphaneStyles : Styles
    {
        public KutuphaneStyles() => AvaloniaXamlLoader.Load(this);
    }
}
