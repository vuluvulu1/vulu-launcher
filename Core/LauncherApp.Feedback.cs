using System.Net;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// vulu Packs geri bildirimleri ve istekleri. Herkes yazar ve kendininkini siler; admin "yapıldı" olarak
    /// işaretler, yanıt yazar ve her şeyi silebilir (yetkiyi sunucu denetler). Sunucunun cevabı arayüze
    /// olduğu gibi iletilir.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private void RegisterFeedbackCommands()
        {
            _bridge.On("feedback.list", async args =>
            {
                RequireLogin();
                return await _api.GetAsync<JsonElement>($"/feedback?pack={Uri.EscapeDataString(PackKey(args))}");
            });
            _bridge.On("feedback.post", async args =>
            {
                RequireLogin();
                string pack = PackKey(args);
                string kind = args.Str("kind");
                return await _api.PostAsync<JsonElement>("/feedback", new
                {
                    pack,
                    kind = kind is "request" or "bug" or "idea" ? kind : "feedback",
                    text = args.Str("text").Trim(),
                    attachments = args.StrList("attachments"),
                    // Launcher hakkındaki bildirimlerde sürüm ve sistem gider (yalnızca adminler görür)
                    meta = pack == "launcher"
                        ? new { version = LauncherUpdateService.LocalVersion, platform = $"{_host.Platform} · {System.Runtime.InteropServices.RuntimeInformation.OSDescription}" }
                        : null,
                });
            });
            _bridge.On("feedback.counts", async _ => { RequireLogin(); return await FeedbackCountsAsync(); });
            _bridge.On("feedback.update", async args =>
            {
                RequireLogin();
                var body = new Dictionary<string, object>();
                if (args.TryGetProperty("status", out var status)) body["status"] = status.GetString() ?? "open";
                if (args.TryGetProperty("reply", out var reply)) body["reply"] = reply.GetString() ?? "";
                return await _api.PatchAsync<JsonElement>($"/feedback/{Uri.EscapeDataString(args.Str("id"))}", body);
            });
            // Ek: dosya seçilir ve yüklenir (resim ya da günlük dosyası, en çok 5 MB); kimliği yazıya eklenir. Vazgeçilirse null.
            _bridge.On("feedback.attach", async _ =>
            {
                RequireLogin();
                string? path = await _host.PickFileAsync("Eklenecek resmi ya da günlük dosyasını seç", "Resim ya da günlük", ["png", "jpg", "jpeg", "webp", "gif", "txt", "log"]);
                if (path == null) return null;
                var info = new FileInfo(path);
                if (info.Length > 5 * 1024 * 1024) throw new InvalidOperationException("Dosya en fazla 5 MB olabilir.");
                return await _api.PostBytesAsync<JsonElement>($"/feedback/attachments?name={Uri.EscapeDataString(info.Name)}", await File.ReadAllBytesAsync(path));
            });
            // Yanıt: yazan kişi ya da admin konuşmaya ekler
            _bridge.On("feedback.message", async args =>
            {
                RequireLogin();
                return await _api.PostAsync<JsonElement>($"/feedback/{Uri.EscapeDataString(args.Str("id"))}/messages", new { text = args.Str("text").Trim() });
            });
            // Ekin kendisi: önbelleğe indirilir, dosya yolu döner (arayüz gösterir ya da açar)
            _bridge.On("feedback.file", async args =>
            {
                RequireLogin();
                string id = args.Str("id");
                if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9a-f]{24}$")) throw new InvalidOperationException("Dosya bulunamadı.");
                string ext = args.Str("mime") switch { "image/png" => ".png", "image/jpeg" => ".jpg", "image/webp" => ".webp", "image/gif" => ".gif", _ => ".txt" };
                string dir = Path.Combine(AppPaths.AppData, "cache", "feedback");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, id + ext);
                if (!File.Exists(path))
                {
                    byte[] data = await _api.GetBytesAsync($"/feedback/attachments/{id}") ?? throw new InvalidOperationException("Dosya artık yok.");
                    await File.WriteAllBytesAsync(path, data);
                }
                return new { path };
            });
            _bridge.On("feedback.delete", async args =>
            {
                RequireLogin();
                return await _api.DeleteAsync<JsonElement>($"/feedback/{Uri.EscapeDataString(args.Str("id"))}");
            });
        }

        private static string PackKey(JsonElement args)
        {
            string pack = args.Str("pack");
            return string.IsNullOrEmpty(pack) ? "general" : pack;
        }

        /// <summary>Paket başına açık geri bildirim sayısı. Sunucu bu özelliği henüz desteklemiyorsa null.</summary>
        private async Task<Dictionary<string, int>?> FeedbackCountsAsync()
        {
            try { return await _api.GetAsync<Dictionary<string, int>>("/feedback/counts"); }
            catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound) { return null; }
            catch { return new Dictionary<string, int>(); }
        }
    }
}
