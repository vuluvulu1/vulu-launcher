using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Updater
{
    internal sealed partial class App : Application
    {
        /// <summary>Program.Main'in kurduğu model; pencere bununla açılır.</summary>
        public static UpdaterViewModel? Model { get; set; }

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Model != null)
                desktop.MainWindow = new MainWindow(Model);
            base.OnFrameworkInitializationCompleted();
        }
    }
}
