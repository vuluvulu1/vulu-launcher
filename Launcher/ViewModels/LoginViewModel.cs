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
        /// <summary>Giriş "önce e-postanı doğrula" dediyse: bağlantıyı yeniden gönderme / adresi düzeltme yolu görünür.</summary>
        [ObservableProperty] private bool _needsVerify;

        // ── Kart: giriş, kayıt ya da "postanı kontrol et" ────────────────
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsLogin), nameof(IsRegister), nameof(IsSent))]
        private string _card = "login";
        public bool IsLogin => Card == "login";
        public bool IsRegister => Card == "register";
        public bool IsSent => Card == "sent";

        // Kayıt formu
        [ObservableProperty] private string _regUsername = "";
        [ObservableProperty] private string _regEmail = "";
        [ObservableProperty] private string _regPassword = "";
        [ObservableProperty] private string _regPassword2 = "";
        [ObservableProperty] private string _regStatus = "";
        [ObservableProperty] private bool _regError;

        // "Postanı kontrol et" kartı. Şifre yalnızca bu kart açıkken bellekte durur: adres düzeltmek için gerekir.
        private string _sentUsername = "", _sentPassword = "", _sentEmail = "";
        private bool _sentUseSaved, _sentAutoApprove = true;
        [ObservableProperty] private string _sentTitle = "";
        [ObservableProperty] private string _sentText = "";
        [ObservableProperty] private bool _canResend;
        [ObservableProperty] private bool _canFix;
        [ObservableProperty] private bool _fixing;
        [ObservableProperty] private string _fixEmail = "";
        [ObservableProperty] private string _sentStatus = "";
        [ObservableProperty] private bool _sentError;
        [ObservableProperty] private bool _sending;

        partial void OnRegUsernameChanged(string value)
        {
            string clean = Regex.Replace(value, "[^a-zA-Z0-9_]", "");
            if (clean != value) RegUsername = clean;
        }

        private static readonly Regex EmailPattern = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$");

        [RelayCommand]
        private void ShowRegister()
        {
            RegStatus = "";
            Card = "register";
        }

        [RelayCommand]
        private void ShowLogin()
        {
            _sentPassword = "";
            Card = "login";
        }

        [RelayCommand]
        private async Task RegisterAsync()
        {
            string name = RegUsername.Trim(), mail = RegEmail.Trim();
            void Fail(string text) { RegStatus = text; RegError = true; }
            if (name.Length < 3) { Fail("Kullanıcı adı en az 3 karakter olmalı."); return; }
            if (!EmailPattern.IsMatch(mail)) { Fail("Geçerli bir e-posta adresi gir."); return; }
            if (RegPassword.Length < 6) { Fail("Şifre en az 6 karakter olmalı."); return; }
            if (RegPassword != RegPassword2) { Fail("Şifreler aynı değil."); return; }

            Sending = true;
            RegStatus = "Gönderiliyor…";
            RegError = false;
            try
            {
                var result = await _core.Call("auth.register", new { username = name, email = mail, password = RegPassword });
                _sentUsername = name;
                _sentPassword = RegPassword;
                _sentUseSaved = false;
                _sentEmail = mail.ToLowerInvariant();
                _sentAutoApprove = !result.TryGetProperty("autoApprove", out var auto) || auto.ValueKind != JsonValueKind.False;
                RegUsername = RegEmail = RegPassword = RegPassword2 = "";
                RegStatus = "";
                OpenSent(result.Bool("verifyEmail"));
            }
            catch (Exception ex) { Fail(ex.Message); }
            finally { Sending = false; }
        }

        private string SentToText() =>
            $"{_sentEmail} adresine bir doğrulama bağlantısı gönderdik. " +
            (_sentAutoApprove ? "Bağlantıyı açınca hesabın açılır ve buradan giriş yapabilirsin" : "Kaydını tamamlamak için bağlantıyı aç; ardından bir admin onaylayınca hesabın açılır") +
            ". Birkaç dakika içinde gelmezse spam klasörüne de bak.";

        private void OpenSent(bool verify)
        {
            Fixing = false;
            FixEmail = "";
            SentStatus = "";
            SentError = false;
            CanResend = verify && _sentEmail.Length > 0;
            CanFix = verify;
            if (verify)
            {
                SentTitle = "Postanı kontrol et";
                SentText = _sentEmail.Length > 0 ? SentToText()
                    : "Kaydını tamamlamak için e-postana gönderdiğimiz bağlantıyı açman gerekiyor.";
            }
            else
            {
                SentTitle = "İsteğin gönderildi";
                SentText = $"{_sentUsername} için isteğin bize ulaştı. Bir admin onayladığında giriş yapabilirsin.";
            }
            Card = "sent";
        }

        /// <summary>Giriş "önce doğrula" dediğinde: bağlantıyı yeniden göndermek ya da adresi düzeltmek için.</summary>
        [RelayCommand]
        private void OpenVerify()
        {
            _sentUsername = Username.Trim();
            _sentUseSaved = _useSaved;
            _sentPassword = _useSaved ? "" : Password;
            _sentEmail = "";
            OpenSent(true);
            StartFix();
            SentStatus = "Adresi yanlış yazdıysan doğrusunu gir; doğru yazdıysan aynısını gir, bağlantıyı yeniden göndeririz.";
        }

        [RelayCommand]
        private void StartFix()
        {
            Fixing = true;
            FixEmail = _sentEmail;
            SentStatus = "";
            SentError = false;
        }

        [RelayCommand]
        private void CancelFix()
        {
            Fixing = false;
            SentStatus = "";
            SentError = false;
        }

        [RelayCommand]
        private Task Resend() => SendVerificationAsync(_sentEmail);

        [RelayCommand]
        private Task SendFix() => SendVerificationAsync(FixEmail.Trim());

        private async Task SendVerificationAsync(string email)
        {
            if (!EmailPattern.IsMatch(email)) { SentStatus = "Geçerli bir e-posta adresi gir."; SentError = true; return; }
            Sending = true;
            SentStatus = "Gönderiliyor…";
            SentError = false;
            try
            {
                await _core.Call("auth.sendVerification", new
                {
                    username = _sentUsername,
                    password = _sentPassword,
                    useSaved = _sentUseSaved,
                    email,
                });
                _sentEmail = email.ToLowerInvariant();
                Fixing = false;
                CanResend = true;
                SentText = SentToText();
                SentStatus = "Bağlantı gönderildi ✓";
            }
            catch (Exception ex) { SentStatus = ex.Message; SentError = true; }
            finally { Sending = false; }
        }

        /// <summary>"Giriş ekranına dön": kayıttaki kullanıcı adı giriş alanına gelir.</summary>
        [RelayCommand]
        private void BackToLogin()
        {
            if (_sentUsername.Length > 0 && !_sentUseSaved) { _useSaved = false; SetFields(_sentUsername, ""); }
            _sentPassword = "";
            NeedsVerify = false;
            SetStatus("");
            Card = "login";
        }

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

        private static bool IsVerifyError(string message) => message.Contains("e-posta adresini doğrula", StringComparison.OrdinalIgnoreCase);

        [RelayCommand(CanExecute = nameof(CanLogin))]
        private async Task LoginAsync()
        {
            if (Username.Trim().Length == 0 || Password.Length == 0)
            {
                SetStatus("Kullanıcı adı ve şifre boş bırakılamaz.", true);
                return;
            }

            Busy = true;
            NeedsVerify = false;
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
                NeedsVerify = IsVerifyError(ex.Message);
                SetStatus(ex.Message, true);
                return;
            }
            slow.Cancel();
            Busy = false;

            if (!result.Bool("success"))
            {
                string error = result.Text("error") ?? "Giriş başarısız.";
                NeedsVerify = IsVerifyError(error);
                SetStatus(error, true);
                return;
            }
            SetFields(Username, "");
            SetStatus("");
            await enter(result);
        }
    }
}
