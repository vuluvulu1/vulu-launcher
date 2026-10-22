using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Admin sayfasının kullanıcı yönetimi. Hepsi vulu-api üzerinden ve giriş yapan admin hesabıyla yapılır;
    /// launcher veritabanına doğrudan bağlanmaz. Yetkiyi ve kuralları (kendini kilitleyememe, son admin) sunucu
    /// denetler. Kullanıcı adı sonradan değiştirilemez.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private void RegisterAdminCommands()
        {
            _bridge.On("admin.users", async _ =>
            {
                RequireAdmin();
                return await _api.GetAsync<JsonElement>("/admin/users");
            });

            _bridge.On("admin.saveUser", async args =>
            {
                RequireAdmin();
                string id = args.Str("id");
                if (string.IsNullOrEmpty(id))
                {
                    var created = await _api.PostAsync<JsonElement>("/admin/users", new
                    {
                        username = args.Str("username").Trim(),
                        email = args.Str("email").Trim(),
                        password = args.Str("password"),
                        isAdmin = args.Bool("isAdmin"),
                        isActive = args.Bool("isActive")
                    });
                    Log($"{args.Str("username").Trim()} kullanıcısı eklendi.", "ok");
                    // Yeni kullanıcının rolleri ayrıca yazılır
                    if (args.StrList("roles").Count == 0) return created;
                    return await _api.PatchAsync<JsonElement>($"/admin/users/{Uri.EscapeDataString(created.GetProperty("id").GetString()!)}",
                        new { roles = args.StrList("roles") });
                }

                // Şifre boşsa gönderilmez, değişmez; değişirse kullanıcının açık oturumları kapanır
                var body = new Dictionary<string, object>
                {
                    ["isAdmin"] = args.Bool("isAdmin"), ["isActive"] = args.Bool("isActive"),
                };
                // Roller yalnızca arayüz onları yükleyip gönderdiyse yazılır (yüklenemediyse eskisi kalır)
                if (args.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array) body["roles"] = args.StrList("roles");
                if (args.Str("password").Length > 0) body["password"] = args.Str("password");
                return await _api.PatchAsync<JsonElement>($"/admin/users/{Uri.EscapeDataString(id)}", body);
            });

            // Roller: paketlere erişimi sınırlar, profillerde rozet olarak görünür
            _bridge.On("admin.roles", async _ =>
            {
                RequireAdmin();
                return await _api.GetAsync<JsonElement>("/admin/roles");
            });

            _bridge.On("admin.saveRole", async args =>
            {
                RequireAdmin();
                var role = new { name = args.Str("name").Trim(), color = args.Str("color") };
                string id = args.Str("id");
                return string.IsNullOrEmpty(id)
                    ? await _api.PostAsync<JsonElement>("/admin/roles", role)
                    : await _api.PatchAsync<JsonElement>($"/admin/roles/{Uri.EscapeDataString(id)}", role);
            });

            _bridge.On("admin.deleteRole", async args =>
            {
                RequireAdmin();
                return await _api.DeleteAsync<JsonElement>($"/admin/roles/{Uri.EscapeDataString(args.Str("id"))}");
            });

            _bridge.On("admin.deleteUser", async args =>
            {
                RequireAdmin();
                var result = await _api.DeleteAsync<JsonElement>($"/admin/users/{Uri.EscapeDataString(args.Str("id"))}");
                Log($"{args.Str("username")} kullanıcısı silindi.", "muted");
                return result;
            });
        }
    }
}
