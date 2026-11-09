using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluMobile.Services;

namespace VuluMobile.ViewModels
{
    internal sealed partial class LoginViewModel : ObservableObject
    {
        private readonly Session session;
        private readonly Action loggedIn;

        /// <param name="autoLogin">Uygulama açılırken: kayıtlı giriş bilgisi varsa kendiliğinden giriş yapılır.</param>
        public LoginViewModel(Session session, Action loggedIn, bool autoLogin = false)
        {
            this.session = session;
            this.loggedIn = loggedIn;
            if (autoLogin && session.Saved is { } saved)
            {
                _username = saved.Username;
                _password = saved.Password;
                Restoring = true;
                _ = LoginAsync();
            }
        }

        /// <summary>Kayıtlı bilgiyle giriş sürüyor: form yerine "Oturum açılıyor" gösterilir.</summary>
        [ObservableProperty] private bool _restoring;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
        private string _username = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
        private string _password = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
        private bool _busy;

        [ObservableProperty] private string? _error;

        private bool CanLogin => !Busy && Username.Trim().Length > 0 && Password.Length > 0;

        [RelayCommand(CanExecute = nameof(CanLogin))]
        private async Task LoginAsync()
        {
            Busy = true;
            Error = null;
            try
            {
                Error = await session.LoginAsync(Username, Password);
                if (Error == null)
                {
                    Password = "";
                    loggedIn();
                }
                // Kayıtlı şifre reddedildiyse (bilgi silindi) eski şifre formda kalmasın; ağ hatasında kalır, "Giriş yap" yeniden dener
                else if (Restoring && session.Saved == null) Password = "";
            }
            finally { Busy = false; Restoring = false; }
        }
    }
}
