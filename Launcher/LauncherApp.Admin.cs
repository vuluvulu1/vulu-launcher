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
                    return created;
                }

                // Şifre boşsa gönderilmez, değişmez; değişirse kullanıcının açık oturumları kapanır
                var body = new Dictionary<string, object> { ["isAdmin"] = args.Bool("isAdmin"), ["isActive"] = args.Bool("isActive") };
                if (args.Str("password").Length > 0) body["password"] = args.Str("password");
                return await _api.PatchAsync<JsonElement>($"/admin/users/{Uri.EscapeDataString(id)}", body);
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
