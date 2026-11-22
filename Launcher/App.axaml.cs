using Avalonia;
using Avalonia.Markup.Xaml;

namespace VuluLauncher
{
    internal sealed partial class App : Application
    {
        public override void Initialize()
        {
            VuluShared.L.Init(AppSettings.Current.Language);
            AvaloniaXamlLoader.Load(this);
            Native.UiTranslator.Install();
            try { Themes.Apply(AppSettings.Current.Theme); } catch { /* tema uygulanamazsa Meşale kalır */ }
            Views.SynthwaveBackdrop.Animated = AppSettings.Current.AnimatedBackground;
        }
    }
}
