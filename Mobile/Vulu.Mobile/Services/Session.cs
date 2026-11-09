using System.Text.Json;
using VuluLauncher;

namespace VuluMobile.Services
{
    /// <summary>
    /// Giriş yapan kullanıcının oturumu ve sunucuya istek atan istemci. Launcher'la aynı Vulu.Social kodunu kullanır.
    /// Girişten sonra telefon push bildirimi için sunucuya kaydedilir, çıkışta kaydı silinir.
    /// Giriş bilgisi şifrelenip saklanır (<see cref="SavedLogin"/>); uygulama açılınca oturum kendiliğinden açılır.
    /// </summary>
    internal sealed class Session
    {
        private LoginService _login = new();
        private string? _deviceToken;

        public Session()
        {
            Api = new ApiClient(() => _login.Token);
            PushHub.TokenRefreshed += token => { if (IsLoggedIn) _ = RegisterDeviceAsync(token); };
            // Ayarlardan dönülünce ya da ilk denemede ağ yoksa: kayıt olmamışsa yeniden denenir
            PushHub.Resumed += () => { if (IsLoggedIn && !PushReady) _ = RegisterDeviceAsync(null); PushStateChanged?.Invoke(); };
        }

        public ApiClient Api { get; }
        public string Username => _login.Username;
        /// <summary>Admin sekmesini göstermek için. Yetkiyi sunucu her istekte ayrıca denetler.</summary>
        public bool IsAdmin => _login.IsAdmin;
        public bool IsLoggedIn => !string.IsNullOrEmpty(_login.Token);
        /// <summary>Bu telefon sunucuya bildirim almak için kaydedildi mi (Firebase token'ı alındı ve gönderildi).</summary>
        public bool PushReady => _deviceToken != null;
        /// <summary>Kayıt olmadıysa nedeni: Firebase token'ı alınamadı ya da sunucu kabul etmedi.</summary>
        public string? PushError { get; private set; }
        public event Action? PushStateChanged;

        /// <summary>Kayıtlı giriş bilgisi (açılışta otomatik giriş için); yoksa null.</summary>
        public (string Username, string Password)? Saved => SavedLogin.Load();

        public async Task<string?> LoginAsync(string username, string password)
        {
            var login = new LoginService();
            var result = await login.LoginAsync(username.Trim(), password);
            if (!result.Success)
            {
                // Şifre değişmiş ya da hesap kapanmışsa kayıtlı bilgi silinir; ağ hatasında kalır (sonra yeniden denenir)
                if (result.Rejected) SavedLogin.Clear();
                return result.ErrorMessage ?? "Giriş başarısız.";
            }
            _login = login;
            SavedLogin.Save(login.Username, password);
            PushHub.Platform?.RequestPermission();
            _ = RegisterDeviceAsync(null);
            return null;
        }

        public void Logout()
        {
            // Telefon bildirim almayı bırakır (eski oturumun token'ıyla, arka planda)
            string? device = _deviceToken, token = _login.Token;
            if (device != null && token != null)
            {
                var api = new ApiClient(() => token);
                _ = Task.Run(async () => { try { await api.DeleteAsync<JsonElement>($"/users/me/devices/{Uri.EscapeDataString(device)}"); } catch { } });
            }
            _deviceToken = null;
            _login = new LoginService();
            SavedLogin.Clear();
        }

        private async Task RegisterDeviceAsync(string? token)
        {
            try
            {
                if (PushHub.Platform == null) { PushError = "Bu cihazda push desteklenmiyor."; return; }
                token ??= await PushHub.Platform.GetTokenAsync();
                if (token == null) { PushError = "Firebase'e bağlanılamadı: " + (PushHub.Platform.LastError ?? "bilinmeyen hata"); return; }
                if (!IsLoggedIn) return;
                await Api.PostAsync<JsonElement>("/users/me/devices", new { token, platform = "android" });
                _deviceToken = token;
                PushError = null;
            }
            catch (Exception ex) { PushError = "Sunucuya kaydedilemedi: " + ex.Message; }
            finally { PushStateChanged?.Invoke(); }
        }
    }
}
