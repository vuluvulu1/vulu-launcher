using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VuluMobile.ViewModels;
using VuluMobile.Views;

namespace VuluMobile
{
    public sealed partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            var shell = new ShellView { DataContext = new ShellViewModel() };
            switch (ApplicationLifetime)
            {
                // Android: tek görünüm
                case ISingleViewApplicationLifetime single:
                    single.MainView = shell;
                    break;
                // Geliştirme (Vulu.Mobile.Desktop): telefon boyutunda pencere
                case IClassicDesktopStyleApplicationLifetime desktop:
                    desktop.MainWindow = new Window { Title = "vulu Sosyal", Width = 400, Height = 820, Content = shell };
                    break;
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
