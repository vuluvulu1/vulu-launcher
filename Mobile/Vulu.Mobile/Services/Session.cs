using VuluLauncher;

namespace VuluMobile.Services
{
    /// <summary>
    /// Giriş yapan kullanıcının oturumu ve sunucuya istek atan istemci. Launcher'la aynı Vulu.Social kodunu kullanır.
    /// Şimdilik oturum bellekte durur; Faz 1'de token Android Keystore ile şifrelenip saklanacak.
    /// </summary>
    internal sealed class Session
    {
        private LoginService _login = new();

        public Session() => Api = new ApiClient(() => _login.Token);

        public ApiClient Api { get; }
        public string Username => _login.Username;
        /// <summary>Admin sekmesini göstermek için. Yetkiyi sunucu her istekte ayrıca denetler.</summary>
        public bool IsAdmin => _login.IsAdmin;
        public bool IsLoggedIn => !string.IsNullOrEmpty(_login.Token);

        public async Task<string?> LoginAsync(string username, string password)
        {
            var login = new LoginService();
            var result = await login.LoginAsync(username.Trim(), password);
            if (!result.Success) return result.ErrorMessage ?? "Giriş başarısız.";
            _login = login;
            return null;
        }

        public void Logout() => _login = new LoginService();
    }
}
