using System.Text.Json;
using VuluLauncher;

namespace VuluMobile.Services
{
    /// <summary>
    /// Giriş yapan kullanıcının oturumu ve sunucuya istek atan istemci. Launcher'la aynı Vulu.Social kodunu kullanır.
    /// Girişten sonra telefon push bildirimi için sunucuya kaydedilir, çıkışta kaydı silinir.
    /// Şimdilik oturum bellekte durur; Faz 1'de token Android Keystore ile şifrelenip saklanacak.
    /// </summary>
    internal sealed class Session
    {
        private LoginService _login = new();
        private string? _deviceToken;

        public Session()
        {
            Api = new ApiClient(() => _login.Token);
            PushHub.TokenRefreshed += token => { if (IsLoggedIn) _ = RegisterDeviceAsync(token); };
        }

        public ApiClient Api { get; }
        public string Username => _login.Username;
        /// <summary>Admin sekmesini göstermek için. Yetkiyi sunucu her istekte ayrıca denetler.</summary>
        public bool IsAdmin => _login.IsAdmin;
        public bool IsLoggedIn => !string.IsNullOrEmpty(_login.Token);
        /// <summary>Bu telefon bildirim alabiliyor mu (Firebase hazır ve sunucuya kaydedildi).</summary>
        public bool PushReady => _deviceToken != null;
        public event Action? PushStateChanged;

        public async Task<string?> LoginAsync(string username, string password)
        {
            var login = new LoginService();
            var result = await login.LoginAsync(username.Trim(), password);
            if (!result.Success) return result.ErrorMessage ?? "Giriş başarısız.";
            _login = login;
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
        }

        private async Task RegisterDeviceAsync(string? token)
        {
            try
            {
                token ??= PushHub.Platform == null ? null : await PushHub.Platform.GetTokenAsync();
                if (token == null || !IsLoggedIn) return;
                await Api.PostAsync<JsonElement>("/users/me/devices", new { token, platform = "android" });
                _deviceToken = token;
            }
            catch { /* push olmadan da uygulama çalışır; bir sonraki girişte yeniden denenir */ }
            PushStateChanged?.Invoke();
        }
    }
}
