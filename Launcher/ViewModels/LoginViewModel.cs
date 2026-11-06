using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>
    /// Giriş ekranı. Kayıtlı şifre arayüze gelmez: alan maskeyle dolu görünür, kullanıcı yazmaya başlarsa
    /// kayıtlı şifre yerine yazdığı kullanılır.
    /// </summary>
    internal sealed partial class LoginViewModel(Func<JsonElement, Task> enter) : ObservableObject
    {
        private const string SavedMask = "••••••••••";
        private readonly CoreClient _core = CoreClient.Instance;
        private bool _useSaved, _settingFields;

        [ObservableProperty] private string _username = "";
        [ObservableProperty] private string _password = "";
        [ObservableProperty] private bool _remember = true;
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private bool _isError;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
        private bool _busy;

        partial void OnUsernameChanged(string value)
        {
            if (_settingFields) return;
            // Yalnızca İngilizce harf, rakam ve alt çizgi
            string clean = Regex.Replace(value, "[^a-zA-Z0-9_]", "");
            if (clean != value) { Username = clean; return; }
            if (_useSaved) { _useSaved = false; SetFields(Username, ""); }
        }

        partial void OnPasswordChanged(string value)
        {
            if (!_settingFields) _useSaved = false;
        }

        private void SetFields(string username, string password)
        {
            _settingFields = true;
            Username = username;
            Password = password;
            _settingFields = false;
        }

        public void SetStatus(string text, bool error = false)
        {
            Status = text;
            IsError = error;
        }

        /// <summary>Giriş ekranını hazırlar; açılışta kayıtlı hesap varsa giriş kendiliğinden denenir.</summary>
        /// <returns>Kendiliğinden giriş denendiyse o deneme (giriş olduysa ana ekran açılınca biter).</returns>
        public Task Prepare(JsonElement init, bool auto)
        {
            string saved = init.Text("savedUsername") ?? "";
            bool hasPassword = init.Bool("hasSavedPassword");
            _useSaved = saved.Length > 0 && hasPassword;
            SetFields(saved, _useSaved ? SavedMask : "");
            Remember = saved.Length > 0;

            if (auto && _useSaved) return LoginAsync();

            SetStatus("Sunucuya bağlanılıyor...");
            _ = WakeAsync();
            return Task.CompletedTask;
        }

        private async Task WakeAsync()
        {
            try
            {
                var result = await _core.Call("app.wake");
                if (Busy) return;   // giriş denemesi sürüyor, mesajına dokunma
                bool reachable = result.Bool("reachable");
                SetStatus(reachable ? "" : "Sunucuya ulaşılamadı.", !reachable);
            }
            catch { /* giriş denemesi asıl hatayı söyler */ }
        }

        private bool CanLogin() => !Busy;

        [RelayCommand(CanExecute = nameof(CanLogin))]
        private async Task LoginAsync()
        {
            if (Username.Trim().Length == 0 || Password.Length == 0)
            {
                SetStatus("Kullanıcı adı ve şifre boş bırakılamaz.", true);
                return;
            }

            Busy = true;
            SetStatus("Giriş yapılıyor, lütfen bekleyin...");
            // Bağlantı yavaşsa kullanıcı beklediğini bilsin
            using var slow = new CancellationTokenSource();
            _ = Task.Delay(4000, slow.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled) Avalonia.Threading.Dispatcher.UIThread.Post(() => SetStatus("Sunucuya bağlanılıyor… Bağlantı yavaşsa biraz sürebilir."));
            }, TaskScheduler.Default);

            JsonElement result;
            try
            {
                result = await _core.Call("auth.login", new
                {
                    username = Username.Trim(),
                    password = _useSaved ? "" : Password,
                    useSaved = _useSaved,
                    remember = Remember,
                });
            }
            catch (Exception ex)
            {
                slow.Cancel();
                Busy = false;
                SetStatus(ex.Message, true);
                return;
            }
            slow.Cancel();
            Busy = false;

            if (!result.Bool("success"))
            {
                SetStatus(result.Text("error") ?? "Giriş başarısız.", true);
                return;
            }
            SetFields(Username, "");
            SetStatus("");
            await enter(result);
        }
    }
}
