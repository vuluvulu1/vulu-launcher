using CommunityToolkit.Mvvm.ComponentModel;
using VuluMobile.Services;

namespace VuluMobile.ViewModels
{
    /// <summary>Uygulamanın kökü: giriş yapılmadıysa giriş ekranı, yapıldıysa sekmeli ana ekran.</summary>
    internal sealed partial class ShellViewModel : ObservableObject
    {
        private readonly Session _session = new();

        [ObservableProperty] private ObservableObject _current;

        public ShellViewModel() => _current = new LoginViewModel(_session, OnLoggedIn);

        private void OnLoggedIn() => Current = new MainViewModel(_session, OnLoggedOut);

        private void OnLoggedOut()
        {
            _session.Logout();
            Current = new LoginViewModel(_session, OnLoggedIn);
        }
    }
}
