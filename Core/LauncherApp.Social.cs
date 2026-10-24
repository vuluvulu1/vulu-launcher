using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    /// <summary>
    /// Sosyal → Ekran görüntüleri: üyelerin paylaştığı ekran görüntüleri. Resimler yalnızca giriş yapmış üyelere
    /// açık olduğundan arayüz onları doğrudan yükleyemez; launcher oturumla indirip önbelleğe alır ve dosya adresini verir.
    /// Paylaşırken resim arayüzde (tarayıcının kodlayıcısıyla) WebP'ye çevrilip küçültülür, buraya hazır gelir.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private static readonly string ShotCache = Path.Combine(AppPaths.AppData, "shot-cache");
        private static readonly Regex ShotId = new("^[0-9a-f]{24}$", RegexOptions.Compiled);
        private const long LocalShotMaxBytes = 32 * 1024 * 1024;
        private const int UploadImageMax = 1200 * 1024, UploadThumbMax = 160 * 1024;

        private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _shotDownloads = new();
        private int _shotCacheCleaned;

        private void RegisterSocialCommands()
        {
            _bridge.On("social.feed", async args => await ShotFeedAsync(args.Str("before"), args.Str("author")));
            _bridge.On("social.image", async args => new { url = FileUrl(await CachedShotAsync(RequireShotId(args.Str("id")), "image")) });
            _bridge.On("social.like", async args => { RequireLogin(); return await _api.PostAsync<JsonElement>($"/shots/{RequireShotId(args.Str("id"))}/like", new { like = args.Bool("like") }); });
            _bridge.On("social.delete", async args => { RequireLogin(); return await _api.DeleteAsync<JsonElement>($"/shots/{RequireShotId(args.Str("id"))}"); });
            _bridge.On("social.save", async args => await SaveShotAsync(RequireShotId(args.Str("id")), args.Str("author")));
            _bridge.On("social.localImage", async args => await LocalShotAsync(args.Str("id"), args.Str("name")));
            _bridge.On("social.share", async args => await ShareShotAsync(args));
            _bridge.On("social.shot", async args => await SingleShotAsync(RequireShotId(args.Str("id"))));
            _bridge.On("social.comments", async args => { RequireLogin(); return await _api.GetAsync<JsonElement>($"/shots/{RequireShotId(args.Str("id"))}/comments"); });
            _bridge.On("social.comment", async args =>
            {
                RequireLogin();
                string text = args.Str("text").Trim();
                if (text.Length is 0 or > 300) throw new InvalidOperationException("Yorum 1-300 karakter olmalı.");
                return await _api.PostAsync<JsonElement>($"/shots/{RequireShotId(args.Str("id"))}/comments", new { text });
            });
            _bridge.On("social.deleteComment", async args =>
            {
                RequireLogin();
                return await _api.DeleteAsync<JsonElement>($"/shots/{RequireShotId(args.Str("id"))}/comments/{RequireShotId(args.Str("commentId"))}");
            });
        }

        private static string RequireShotId(string id) =>
            ShotId.IsMatch(id ?? "") ? id! : throw new InvalidOperationException("Ekran görüntüsü bulunamadı.");

        private static string FileUrl(string path) => LocalFiles.Url(path);

        // ── Akış ──────────────────────────────────────────────────────────
        private async Task<object?> ShotFeedAsync(string before, string author)
        {
            RequireLogin();
            CleanShotCache();
            var query = new List<string>();
            if (!string.IsNullOrEmpty(before)) query.Add("before=" + Uri.EscapeDataString(before));
            if (!string.IsNullOrEmpty(author)) query.Add("author=" + Uri.EscapeDataString(author));
            var feed = await _api.GetAsync<JsonElement>("/shots" + (query.Count > 0 ? "?" + string.Join('&', query) : ""));

            // Önizlemeler paralel (en fazla 6) indirilir; inemeyen boş kalır, arayüz yer tutucu gösterir
            using var gate = new SemaphoreSlim(6);
            var shots = await Task.WhenAll(feed.GetProperty("shots").EnumerateArray().Select(async s =>
            {
                string id = s.GetProperty("id").GetString()!;
                string? thumb = null;
                await gate.WaitAsync();
                try { thumb = FileUrl(await CachedShotAsync(id, "thumb")); }
                catch { }
                finally { gate.Release(); }
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(s.GetRawText())!;
                return new Dictionary<string, object?>(dict.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value))) { ["thumbUrl"] = thumb };
            }));
            return new { shots, more = feed.GetProperty("more").GetBoolean() };
        }

        /// <summary>Tek bir paylaşım (bildirimden açılınca), önizlemesiyle.</summary>
        private async Task<object?> SingleShotAsync(string id)
        {
            RequireLogin();
            var shot = await _api.GetAsync<JsonElement>($"/shots/{id}");
            string? thumb = null;
            try { thumb = FileUrl(await CachedShotAsync(id, "thumb")); } catch { }
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(shot.GetRawText())!;
            return new Dictionary<string, object?>(dict.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value))) { ["thumbUrl"] = thumb };
        }

        // ── Beğeni ve yorum bildirimleri ──────────────────────────────────
        private readonly object _shotEventsGate = new();
        private bool _shotEventsPolling;
        private DateTime? _shotEventsSince;

        /// <summary>Girişten sonra 20 sn'de bir: ekran görüntülerime gelen beğeni ve yorumlar.</summary>
        private void StartShotEventPolling()
        {
            lock (_shotEventsGate)
            {
                if (_shotEventsPolling) return;
                _shotEventsPolling = true;
            }
            _ = Task.Run(async () =>
            {
                string? user = null;
                while (true)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(_login.Token))
                        {
                            if (user != _login.Username) { user = _login.Username; _shotEventsSince = null; }   // hesap değişti
                            string query = _shotEventsSince is { } since ? $"?since={Uri.EscapeDataString(since.ToString("o"))}" : "";
                            var result = await _api.GetAsync<JsonElement>("/shots/events" + query);
                            if (result.TryGetProperty("now", out var now) && now.TryGetDateTime(out var at)) _shotEventsSince = at.ToUniversalTime();
                            foreach (var e in result.GetProperty("events").EnumerateArray())
                            {
                                string from = e.GetProperty("from").GetString()!, kind = e.GetProperty("kind").GetString()!;
                                string text = e.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                                string shotId = e.GetProperty("shotId").GetString()!;
                                _bridge.Emit("shotEvent", new { from, kind, text, shotId });
                                if (AppSettings.Current.SocialNotifications)
                                    ShowNotification(kind == "like" ? $"{from} ekran görüntünü beğendi" : $"{from} yorum yaptı",
                                        kind == "like" ? "Sosyal'de görmek için tıkla." : text, "shot:" + shotId);
                            }
                        }
                    }
                    catch (Exception) { /* bağlantı yoksa bir sonraki turda */ }
                    await Task.Delay(TimeSpan.FromSeconds(_host.IsWindowOpen ? 20 : 60));
                }
            });
        }

        /// <summary>Resmi bir kez indirir (aynı anda istenirse tek indirme), sonra diskten verir. Paylaşımlar değişmez.</summary>
        private Task<string> CachedShotAsync(string id, string kind)
        {
            string path = Path.Combine(ShotCache, $"{id}-{kind}");
            if (File.Exists(path)) return Task.FromResult(path);
            // Lazy: sözlük fabrikayı iki kez çağırsa bile tek indirme başlar
            var lazy = _shotDownloads.GetOrAdd(path, p => new Lazy<Task<string>>(async () =>
            {
                await _api.DownloadAsync($"/shots/{id}/{kind}", p);
                return p;
            }));
            var task = lazy.Value;
            // Bitince (hata verse de) kayıt kalkar; hatalı indirme bir sonraki istekte yeniden denenir
            _ = task.ContinueWith(_ => _shotDownloads.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(path, lazy)), TaskScheduler.Default);
            return task;
        }

        /// <summary>Oturum başına bir kez: 30 günden eski önbellek dosyaları silinir.</summary>
        private void CleanShotCache()
        {
            if (Interlocked.Exchange(ref _shotCacheCleaned, 1) == 1 || !Directory.Exists(ShotCache)) return;
            _ = Task.Run(() =>
            {
                foreach (var file in new DirectoryInfo(ShotCache).EnumerateFiles())
                    try { if (file.LastAccessTimeUtc < DateTime.UtcNow.AddDays(-30) && file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-30)) file.Delete(); }
                    catch { }
            });
        }

        /// <summary>Resimler klasörüne (Resimler\vulu Launcher) kaydeder ve Gezgin'de gösterir.</summary>
        private async Task<object?> SaveShotAsync(string id, string author)
        {
            RequireLogin();
            string source = await CachedShotAsync(id, "image");
            string dir = Path.Combine(_host.PicturesDirectory, "vulu Launcher");
            Directory.CreateDirectory(dir);
            string safeAuthor = Regex.IsMatch(author ?? "", "^[A-Za-z0-9_]{3,16}$") ? author! : "vulu";
            byte[] head = new byte[4];
            await using (var f = File.OpenRead(source)) _ = await f.ReadAsync(head);
            string ext = head[0] == 0xFF && head[1] == 0xD8 ? ".jpg" : ".webp";
            string target = Path.Combine(dir, $"{safeAuthor}-{id[^8..]}{ext}");
            File.Copy(source, target, overwrite: true);
            _host.ShowInFolder(target, selectFile: true);
            return new { path = target };
        }

        // ── Paylaşma ──────────────────────────────────────────────────────
        /// <summary>Yerel ekran görüntüsü arayüze data adresi olarak verilir (tuval "kirlenmesin", küçültülebilsin).</summary>
        private async Task<object?> LocalShotAsync(string instanceId, string name)
        {
            string path = ScreenshotPath(instanceId, name);
            if (new FileInfo(path).Length > LocalShotMaxBytes) throw new InvalidOperationException("Bu ekran görüntüsü çok büyük.");
            string mime = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            return new { dataUrl = $"data:{mime};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(path))}" };
        }

        private async Task<object?> ShareShotAsync(JsonElement args)
        {
            RequireLogin();
            static string Base64Of(string dataUrl, int max, string what)
            {
                int comma = dataUrl.IndexOf(',');
                string b64 = comma >= 0 && dataUrl.StartsWith("data:image/", StringComparison.Ordinal) ? dataUrl[(comma + 1)..] : "";
                if (b64.Length == 0 || b64.Length > (max + 2) / 3 * 4) throw new InvalidOperationException($"{what} hazırlanamadı ya da çok büyük.");
                return b64;
            }
            string image = Base64Of(args.Str("image"), UploadImageMax, "Resim");
            string thumb = Base64Of(args.Str("thumb"), UploadThumbMax, "Önizleme");

            // Hangi paketten olduğu instance bilgisinden alınır (arayüzün söylediğine değil)
            var (target, _) = ResolveTarget(args.Str("instanceId"));
            string? packId = args.Str("instanceId").StartsWith(InstancePrefix, StringComparison.Ordinal)
                ? InstanceStore.TryLoad(args.Str("instanceId")[InstancePrefix.Length..])?.SharedPackId : null;
            string caption = args.Str("caption").Trim();
            if (caption.Length > 200) caption = caption[..200];

            var shot = await _api.PostAsync<JsonElement>("/shots", new
            {
                image, thumb, caption,
                packName = target.Name.Length > 64 ? target.Name[..64] : target.Name,
                packId,
                mcVersion = target.McVersion,
                width = args.Int("width"),
                height = args.Int("height"),
            });
            Log("Ekran görüntüsü paylaşıldı.", "ok");
            return shot;
        }
    }
}
