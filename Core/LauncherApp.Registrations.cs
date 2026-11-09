using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    /// <summary>
    /// Kayıt: giriş ekranından kullanıcı adı, e-posta ve şifreyle istek gönderilir. Sunucu e-posta doğrulaması
    /// kullanıyorsa (verifyEmail) kişi önce postadaki bağlantıyı açar; ardından bir admin onaylayınca hesap açılır. Admin bekleyen istekleri Admin sayfasında görür ve kabul eder ya da reddeder (yetkiyi sunucu denetler).
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

                // Sunucunun cevabı (verifyEmail dahil) olduğu gibi arayüze gider
                var result = await _api.PostAsync<JsonElement>("/auth/register", new { username, email, password });
                Log($"{username} için kayıt isteği gönderildi.", "ok");
                return result;
            });

            // Doğrulama bağlantısını (yeniden) gönderir. Adres yanlış yazıldıysa düzeltilir; aynı adres girildiyse
            // bağlantı yeniden gönderilir. Kaydın şifresiyle yapılır; giriş ekranındaki kayıtlı şifre de kullanılabilir.
            _bridge.On("auth.sendVerification", async args =>
            {
                string username = args.Str("username").Trim();
                string email = args.Str("email").Trim();
                string password = args.Str("password");
                if (args.Bool("useSaved"))
                {
                    var saved = CredentialManager.Load();
                    if (string.Equals(saved.Username, username, StringComparison.OrdinalIgnoreCase)) password = saved.Password ?? "";
                }
                if (username.Length == 0 || password.Length == 0)
                    throw new InvalidOperationException("Kullanıcı adı ve şifre gerekli.");
                try
                {
                    return await _api.PostAsync<JsonElement>("/auth/registration/email", new { username, password, email });
                }
                catch (ApiException ex) when (ex.Status == HttpStatusCode.BadRequest && ex.Message.Contains("zaten bu"))
                {
                    // Adres doğruymuş: bağlantı aynı adrese yeniden gider
                    return await _api.PostAsync<JsonElement>("/auth/resend-verification", new { email });
                }
            });

            _bridge.On("admin.registrations", async _ =>
            {
                RequireAdmin();
                try { return await _api.GetAsync<JsonElement>("/admin/registrations"); }
                // Sunucu bu özelliği henüz desteklemiyorsa sekme boş görünür
                catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound) { return null; }
            });
            // Genel ayarlar: registrationApproval 'auto' (e-posta doğrulanınca hesap açılır) ya da 'admin'
            _bridge.On("admin.settings", async _ =>
            {
                RequireAdmin();
                try { return await _api.GetAsync<JsonElement>("/admin/settings"); }
                catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound) { return null; }
            });
            _bridge.On("admin.saveSettings", async args =>
            {
                RequireAdmin();
                var changes = new Dictionary<string, object>();
                if (args.TryGetProperty("registrationApproval", out _)) changes["registrationApproval"] = args.Str("registrationApproval") == "admin" ? "admin" : "auto";
                if (args.TryGetProperty("requireMinecraftOwnership", out _)) changes["requireMinecraftOwnership"] = args.Bool("requireMinecraftOwnership");
                return await _api.PutAsync<JsonElement>("/admin/settings", changes);
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

        // ── Yeni kayıt isteği bildirimi (yalnızca adminler) ──────────────
        private readonly object _registrationGate = new();
        private bool _registrationPolling;
        /// <summary>Bildirimi gösterilmiş (e-postası doğrulanmış) istekler. null: henüz ilk liste alınmadı, açılışta bildirim yok.</summary>
        private HashSet<string>? _knownRegistrations;

        /// <summary>
        /// Admin girişinden sonra: bekleyen kayıt istekleri düzenli aralıklarla sorulur. Onaylanabilir hâle gelen
        /// (e-postası doğrulanmış) yeni bir istek bildirim olur; arayüze Admin sekmesinin rozeti için sayı gider.
        /// Doğrulanmamış istekler bildirim olmaz, admin onları henüz onaylayamaz. Yetkiyi sunucu ayrıca denetler.
        /// </summary>
        private void StartRegistrationPolling()
        {
            lock (_registrationGate)
            {
                if (_registrationPolling) return;
                _registrationPolling = true;
            }
            _ = Task.Run(async () =>
            {
                string? user = null;
                while (true)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(_login.Token) && _login.IsAdmin)
                        {
                            if (user != _login.Username) { user = _login.Username; _knownRegistrations = null; }   // hesap değişti
                            await CheckRegistrationsAsync();
                        }
                    }
                    catch (ApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                    {
                        // Sunucu desteklemiyor ya da adminlik alınmış: bir sonraki turda yeniden denenir
                    }
                    catch (Exception) { /* bağlantı yoksa bir sonraki turda */ }
                    await Task.Delay(TimeSpan.FromSeconds(_host.IsWindowOpen ? 60 : 120));
                }
            });
        }

        private async Task CheckRegistrationsAsync()
        {
            var list = await _api.GetAsync<JsonElement>("/admin/registrations");
            if (list.ValueKind != JsonValueKind.Array) return;
            var pending = list.EnumerateArray().ToList();
            _bridge.Emit("registrations", new { count = pending.Count });

            var ready = pending
                .Where(r => !r.TryGetProperty("emailVerified", out var v) || v.ValueKind != JsonValueKind.False)
                .Select(r => (Id: r.GetProperty("id").GetString() ?? "", Username: r.GetProperty("username").GetString() ?? ""))
                .Where(r => r.Id.Length > 0)
                .ToList();

            var known = _knownRegistrations;
            _knownRegistrations = ready.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            if (known == null || !AppSettings.Current.RegistrationNotifications) return;

            var fresh = ready.Where(r => !known.Contains(r.Id)).ToList();
            if (fresh.Count == 1)
                ShowNotification("Yeni kayıt isteği", $"{fresh[0].Username} katılmak istiyor. İncelemek için tıkla.", "registration:" + fresh[0].Id);
            else if (fresh.Count > 1)
                ShowNotification($"{fresh.Count} yeni kayıt isteği",
                    string.Join(", ", fresh.Take(3).Select(r => r.Username)) + (fresh.Count > 3 ? "…" : "") + " katılmak istiyor.", "registration:");
        }
    }
}
