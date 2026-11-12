using Avalonia;
using Avalonia.Markup.Xaml;

namespace VuluLauncher
{
    internal sealed partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            try { Themes.Apply(AppSettings.Current.Theme); } catch { /* tema uygulanamazsa Meşale kalır */ }
        }
    }
}
