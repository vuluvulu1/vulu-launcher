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
                return await _api.PostAsync<JsonElement>("/feedback", new
                {
                    pack = PackKey(args),
                    kind = args.Str("kind") == "request" ? "request" : "feedback",
                    text = args.Str("text").Trim()
                });
            });
            _bridge.On("feedback.update", async args =>
            {
                RequireLogin();
                var body = new Dictionary<string, object>();
                if (args.TryGetProperty("status", out var status)) body["status"] = status.GetString() ?? "open";
                if (args.TryGetProperty("reply", out var reply)) body["reply"] = reply.GetString() ?? "";
                return await _api.PatchAsync<JsonElement>($"/feedback/{Uri.EscapeDataString(args.Str("id"))}", body);
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
