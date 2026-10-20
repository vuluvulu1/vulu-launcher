using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    /// <summary>
    /// Kayıt: giriş ekranından kullanıcı adı, e-posta ve şifreyle istek gönderilir; bir admin onaylayınca hesap
    /// açılır. Admin bekleyen istekleri Admin sayfasında görür ve kabul eder ya da reddeder (yetkiyi sunucu denetler).
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private static readonly Regex RegisterUsername = new(@"^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);

        private void RegisterRegistrationCommands()
        {
            // Giriş yapılmadan çağrılır; sunucu da ayrıca denetler
            _bridge.On("auth.register", async args =>
            {
                string username = args.Str("username").Trim();
                string email = args.Str("email").Trim();
                string password = args.Str("password");
                if (!RegisterUsername.IsMatch(username))
                    throw new InvalidOperationException("Kullanıcı adı 3-16 karakter olmalı; yalnızca İngilizce harf, rakam ve _ kullanılabilir.");
                if (password.Length < 6)
                    throw new InvalidOperationException("Şifre en az 6 karakter olmalı.");

                await _api.PostAsync<JsonElement>("/auth/register", new { username, email, password });
                Log($"{username} için kayıt isteği gönderildi.", "ok");
                return new { success = true };
            });

            _bridge.On("admin.registrations", async _ =>
            {
                RequireAdmin();
                try { return await _api.GetAsync<JsonElement>("/admin/registrations"); }
                // Sunucu bu özelliği henüz desteklemiyorsa sekme boş görünür
                catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound) { return null; }
            });
            _bridge.On("admin.approveRegistration", async args =>
            {
                RequireAdmin();
                var result = await _api.PostAsync<JsonElement>($"/admin/registrations/{Uri.EscapeDataString(args.Str("id"))}/approve", new { });
                Log($"{args.Str("username")} kaydı onaylandı.", "ok");
                return result;
            });
            _bridge.On("admin.rejectRegistration", async args =>
            {
                RequireAdmin();
                var result = await _api.PostAsync<JsonElement>($"/admin/registrations/{Uri.EscapeDataString(args.Str("id"))}/reject", new { });
                Log($"{args.Str("username")} kaydı reddedildi.", "muted");
                return result;
            });
        }
    }
}
