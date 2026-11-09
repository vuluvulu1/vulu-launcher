using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluMobile.Services;

namespace VuluMobile.ViewModels
{
    internal sealed partial class LoginViewModel(Session session, Action loggedIn) : ObservableObject
    {
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
            }
            finally { Busy = false; }
        }
    }
}
