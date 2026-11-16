using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// vulu-resmi'ye bağlı topluluk özellikleri: liderlik tablosu, seviye ve sezon görevleri, vulu Pelerinleri,
    /// skin dolabı ve canlı dünya haritası. Hepsi vulu-api üzerinden; cevaplar arayüze olduğu gibi iletilir.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private void RegisterProgressCommands()
        {
            // Liderlik: week (bu hafta), season (sezon), all (tüm zamanlar)
            _bridge.On("progress.leaderboard", async args =>
            {
                string period = args.Str("period") is "season" or "all" ? args.Str("period") : "week";
                return await _api.GetAsync<JsonElement>("/progress/leaderboard?period=" + period);
            });
            _bridge.On("progress.me", async _ => { RequireLogin(); return await _api.GetAsync<JsonElement>("/progress/me"); });
            _bridge.On("progress.user", async args =>
            {
                RequireLogin();
                return await _api.GetAsync<JsonElement>("/progress/user/" + Uri.EscapeDataString(args.Str("username")));
            });
            _bridge.On("progress.capes", async _ => await _api.GetAsync<JsonElement>("/progress/capes"));
            // Kazanılmış bir pelerini tak ("" = çıkar); oyunda bir sonraki girişte herkese görünür
            _bridge.On("progress.setCape", async args =>
            {
                RequireLogin();
                return await _api.PutAsync<JsonElement>("/progress/me/cape", new { key = args.Str("key") });
            });
            // Admin: pelerin ver / al
            _bridge.On("progress.grantCape", async args =>
            {
                RequireLogin();
                return await _api.PostAsync<JsonElement>("/progress/capes/grant", new { username = args.Str("username"), key = args.Str("key"), note = args.Str("note") });
            });
            _bridge.On("progress.revokeCape", async args =>
            {
                RequireLogin();
                return await _api.SendJsonAsync<JsonElement>(HttpMethod.Delete, "/progress/capes/grant", new { username = args.Str("username"), key = args.Str("key") });
            });

            // Admin: sezonlar, görevler (başarımlar), seviye/XP ve haftalık dallar
            _bridge.On("admin.progressConfig", async _ => { RequireLogin(); return await _api.GetAsync<JsonElement>("/progress/admin/config"); });
            _bridge.On("admin.saveProgressConfig", async args =>
            {
                RequireLogin();
                return await _api.PutAsync<JsonElement>("/progress/admin/config", new { config = args.GetProperty("config") });
            });

            // Dolap: kayıtlı skinler
            _bridge.On("wardrobe.list", async _ => { RequireLogin(); return await _api.GetAsync<JsonElement>("/users/me/wardrobe"); });
            _bridge.On("wardrobe.apply", async args =>
            {
                RequireLogin();
                return await _api.PutAsync<JsonElement>("/users/me/wardrobe/" + Hash(args.Str("hash")), new { apply = true });
            });
            _bridge.On("wardrobe.rename", async args =>
            {
                RequireLogin();
                return await _api.PutAsync<JsonElement>("/users/me/wardrobe/" + Hash(args.Str("hash")), new { apply = false, name = args.Str("name").Trim() });
            });
            _bridge.On("wardrobe.remove", async args =>
            {
                RequireLogin();
                return await _api.DeleteAsync<JsonElement>("/users/me/wardrobe/" + Hash(args.Str("hash")));
            });

            // Harita
            _bridge.On("map.info", async _ => { RequireLogin(); return await _api.GetAsync<JsonElement>("/map/info"); });
            _bridge.On("map.players", async _ => { RequireLogin(); return await _api.GetAsync<JsonElement>("/map/players"); });
            _bridge.On("map.tile", async args =>
            {
                RequireLogin();
                string dim = args.Str("dim") is "the_nether" or "the_end" ? args.Str("dim") : "overworld";
                var bytes = await _api.GetBytesAsync($"/map/tile/{dim}/{Math.Clamp(args.Int("zoom"), 0, 3)}/{args.Int("x")}/{args.Int("z")}");
                return bytes == null ? null : new { data = Convert.ToBase64String(bytes) };
            });
        }

        private static string Hash(string value) =>
            value.Length == 64 && value.All(Uri.IsHexDigit) ? value : throw new InvalidOperationException("Geçersiz skin.");
    }
}
