using Avalonia;
using Avalonia.Markup.Xaml;

namespace VuluLauncher
{
    internal sealed partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);
    }
}
