using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Instance kitaplığı araçları: oynama süresi, ekran görüntüsü galerisi ve kurulu vulu pack'lerin
    /// güncelleme denetimi.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private static readonly string[] ScreenshotExtensions = { ".png", ".jpg", ".jpeg" };
        private const int ScreenshotLimit = 1000;
        private static readonly TimeSpan PackCheckInterval = TimeSpan.FromMinutes(20);

        private readonly object _playGate = new();
        private string? _playId;
        private DateTime _playMark;
        private Timer? _playTimer;

        private readonly object _actionGate = new();
        private StartupAction? _pendingAction;
        private bool _packChecking;
        private readonly HashSet<string> _packUpdatesNotified = new();

        private void RegisterLibraryCommands()
        {
            _bridge.On("screenshots.list", async args => await Task.Run(() => ListScreenshots(args.Str("id"))));
            _bridge.On("screenshots.open", args => Task.FromResult<object?>(OpenScreenshot(args.Str("id"), args.Str("name"), reveal: false)));
            _bridge.On("screenshots.reveal", args => Task.FromResult<object?>(OpenScreenshot(args.Str("id"), args.Str("name"), reveal: true)));
            _bridge.On("screenshots.copy", args => Task.FromResult<object?>(CopyScreenshot(args.Str("id"), args.Str("name"))));
            _bridge.On("screenshots.delete", args => Task.FromResult<object?>(DeleteScreenshot(args.Str("id"), args.Str("name"))));
            _bridge.On("packs.updates", async _ => await PackUpdatesAsync());
            _bridge.On("app.takeAction", _ =>
            {
                StartupAction? a;
                lock (_actionGate) { a = _pendingAction; _pendingAction = null; }
                return Task.FromResult<object?>(a == null ? null : new { kind = a.Kind, id = a.Id });
            });
            _bridge.On("shortcuts.icon", async args => await ShortcutIconSourceAsync(args.Str("id")));
            _bridge.On("shortcuts.create", args => Task.FromResult<object?>(CreateDesktopShortcut(args)));
            _bridge.On("announcement.get", async _ => { RequireLogin(); return await _api.GetAsync<JsonElement>("/announcements"); });
            _bridge.On("announcement.post", async args =>
            {
                RequireAdmin();
                return await _api.PostAsync<JsonElement>("/announcements", new { text = args.Str("text"), hours = args.Int("hours") });
            });
            _bridge.On("announcement.delete", async args =>
            {
                RequireAdmin();
                if (!System.Text.RegularExpressions.Regex.IsMatch(args.Str("id"), "^[0-9a-f]{24}$")) throw new InvalidOperationException("Duyuru bulunamadı.");
                return await _api.DeleteAsync<JsonElement>($"/announcements/{args.Str("id")}");
            });
            _bridge.On("admin.storage", async _ => { RequireAdmin(); return await _api.GetAsync<JsonElement>("/admin/storage"); });
        }

        // ════════════════════════════════════════════════════════════════
        // MASAÜSTÜ KISAYOLLARI
        // ════════════════════════════════════════════════════════════════
        private static readonly string ShortcutIcons = Path.Combine(AppPaths.AppData, "shortcut-icons");

        public void SetStartupAction(StartupAction? action)
        {
            lock (_actionGate) _pendingAction = action;
        }

        /// <summary>Başka bir kopyadan (kısayoldan) gelen istek: pencere öne gelir, istek arayüze iletilir.</summary>
        public void OnRemoteCommand(string message)
        {
            _host.BringToFront();
            var action = StartupAction.FromMessage(message);
            if (action == null) return;
            lock (_actionGate) _pendingAction = action;
            _bridge.Emit("startupAction");
        }

        /// <summary>Kısayol simgesi için paketin logosu (yoksa Modrinth simgesi) data adresi olarak; yoksa null.</summary>
        private async Task<object?> ShortcutIconSourceAsync(string id)
        {
            RequireLogin();
            _ = ResolveTarget(id);
            string? file = LogoFile(id);
            if (file != null && new FileInfo(file).Length < 4 * 1024 * 1024)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                string mime = ext switch { ".png" => "image/png", ".webp" => "image/webp", ".gif" => "image/gif", _ => "image/jpeg" };
                return new { dataUrl = $"data:{mime};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(file))}" };
            }
            string? iconUrl = id.StartsWith(InstancePrefix, StringComparison.Ordinal) ? InstanceStore.TryLoad(id[InstancePrefix.Length..])?.IconUrl : null;
            if (iconUrl != null && iconUrl.StartsWith("https://cdn.modrinth.com/", StringComparison.Ordinal))
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                    var bytes = await http.GetByteArrayAsync(iconUrl);
                    if (bytes.Length < 4 * 1024 * 1024)
                        return new { dataUrl = $"data:image/png;base64,{Convert.ToBase64String(bytes)}" };
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Masaüstüne kısayol: paket ("play") ya da sunucu ("join"). Simge arayüzün hazırladığı 256 px PNG'den ICO'ya
        /// çevrilir; yoksa launcher'ın simgesi kullanılır.
        /// </summary>
        private object CreateDesktopShortcut(JsonElement args)
        {
            RequireLogin();
            string kind = args.Str("kind"), id = args.Str("id");
            if (StartupAction.FromMessage($"{kind}\t{id}") == null) throw new InvalidOperationException("Kısayol oluşturulamadı.");
            if (kind == "play") _ = ResolveTarget(id);

            string name = new string(args.Str("name").Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim().TrimEnd('.');
            if (name.Length == 0) name = "vulu";
            if (name.Length > 60) name = name[..60];

            if (!_host.IsDesktop) throw new InvalidOperationException("Kısayollar bu cihazda desteklenmiyor.");
            string? icon = null;
            var png = System.Text.RegularExpressions.Regex.Match(args.Str("iconDataUrl"), "^data:image/png;base64,([A-Za-z0-9+/]+={0,2})$");
            if (png.Success)
            {
                byte[] bytes = Convert.FromBase64String(png.Groups[1].Value);
                if (bytes.Length < 1024 * 1024)
                {
                    Directory.CreateDirectory(ShortcutIcons);
                    icon = Path.Combine(ShortcutIcons, $"{kind}-{string.Concat(id.Where(char.IsLetterOrDigit))}.ico");
                    File.WriteAllBytes(icon, PngToIco(bytes));
                }
            }

            string argument = kind == "play" ? $"--play \"{id}\"" : $"--join {id}";
            string path = _host.CreateShortcut(name, argument,
                kind == "play" ? $"{name} · vulu Launcher ile oyna" : $"{name} sunucusuna vulu Launcher ile bağlan", icon);
            Log($"Masaüstüne kısayol eklendi: {name}", "ok");
            return new { path };
        }

        /// <summary>Tek PNG içeren ICO (Windows Vista ve sonrası PNG simgeleri doğrudan okur).</summary>
        private static byte[] PngToIco(byte[] png)
        {
            int w = png.Length > 24 ? (png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19]) : 256;
            int h = png.Length > 24 ? (png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23]) : 256;
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)1);
            bw.Write((byte)(w >= 256 ? 0 : w)); bw.Write((byte)(h >= 256 ? 0 : h));
            bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((short)1); bw.Write((short)32);
            bw.Write(png.Length); bw.Write(22);
            bw.Write(png);
            return ms.ToArray();
        }

        // ════════════════════════════════════════════════════════════════
        // OYNAMA SÜRESİ
        // ════════════════════════════════════════════════════════════════
        /// <summary>Oyun açıldı: süre dakikada bir kaydedilir (launcher beklenmedik kapanırsa da kaybolmasın).</summary>
        private void StartPlayTime(string instanceId)
        {
            StopPlayTime();
            lock (_playGate)
            {
                _playId = instanceId;
                _playMark = DateTime.UtcNow;
                PlayTimeStore.Add(instanceId, TimeSpan.Zero, newSession: true);
                _playTimer = new Timer(_ => FlushPlayTime(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            }
        }

        private void FlushPlayTime()
        {
            lock (_playGate)
            {
                if (_playId == null) return;
                var now = DateTime.UtcNow;
                try { PlayTimeStore.Add(_playId, now - _playMark, newSession: false); _playMark = now; }
                catch { /* bir sonraki dakikada yeniden denenir */ }
            }
        }

        private void StopPlayTime()
        {
            FlushPlayTime();
            bool wasPlaying;
            lock (_playGate)
            {
                wasPlaying = _playId != null;
                _playTimer?.Dispose();
                _playTimer = null;
                _playId = null;
            }
            if (wasPlaying) _ = SyncPlaytimeAsync();
        }

        /// <summary>
        /// Profildeki oynama istatistiği: toplam süre ve en çok oynanan 3 paket (adlarıyla) vulu-api'ye gönderilir.
        /// Ayarlarda kapatıldıysa sunucudaki kayıt silinir. Hata olursa sessizce bir sonraki fırsata kalır.
        /// </summary>
        private async Task SyncPlaytimeAsync()
        {
            if (string.IsNullOrEmpty(_login.Token)) return;
            try
            {
                if (!AppSettings.Current.ShowPlaytimeOnProfile)
                {
                    await _api.DeleteAsync<JsonElement>("/users/me/playtime");
                    return;
                }
                var named = PlayTimeStore.All()
                    .Select(kv => (Name: PlayTimeName(kv.Key), kv.Value.Seconds))
                    .Where(x => x.Name != null && x.Seconds > 0)
                    .GroupBy(x => x.Name!)
                    .Select(g => (Name: g.Key, Seconds: g.Sum(x => x.Seconds)))
                    .OrderByDescending(x => x.Seconds)
                    .ToList();
                long total = named.Sum(x => x.Seconds);
                if (total < 60) return;   // henüz göstermeye değecek süre yok
                await _api.PutAsync<JsonElement>("/users/me/playtime", new
                {
                    totalSeconds = total,
                    top = named.Take(3).Select(x => new { name = x.Name, seconds = x.Seconds }),
                });
            }
            catch (Exception) { /* sunucuya ulaşılamazsa bir sonraki oyunda */ }
        }

        /// <summary>Oynama kaydının adı: sunucu paketi ya da instance'ın şu anki adı (silinmişse null).</summary>
        private static string? PlayTimeName(string id)
        {
            if (id.StartsWith(PackPrefix, StringComparison.Ordinal)) return PackCatalog.Find(id[PackPrefix.Length..])?.Name;
            if (id.StartsWith(InstancePrefix, StringComparison.Ordinal)) return InstanceStore.TryLoad(id[InstancePrefix.Length..])?.Name;
            return null;
        }

        private static object PlayTimeOf(string id)
        {
            var entry = PlayTimeStore.Get(id);
            return new { seconds = entry?.Seconds ?? 0, sessions = entry?.Sessions ?? 0, lastPlayed = entry?.LastPlayed?.ToString("o") };
        }

        // ════════════════════════════════════════════════════════════════
        // EKRAN GÖRÜNTÜLERİ
        // ════════════════════════════════════════════════════════════════
        private string ScreenshotDirOf(string id) => Path.Combine(InstanceDirOf(id), "screenshots");

        /// <summary>Galerideki bir dosya: tek seviye, yalnızca resim uzantıları.</summary>
        private string ScreenshotPath(string id, string name)
        {
            RequireLogin();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !ScreenshotExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
                throw new InvalidOperationException("Geçersiz dosya adı.");
            string path = Path.Combine(ScreenshotDirOf(id), name);
            if (!File.Exists(path)) throw new InvalidOperationException("Ekran görüntüsü bulunamadı.");
            return path;
        }

        private object ListScreenshots(string id)
        {
            RequireLogin();
            string dir = ScreenshotDirOf(id);
            var files = !Directory.Exists(dir) ? new List<FileInfo>() : new DirectoryInfo(dir).EnumerateFiles()
                .Where(f => ScreenshotExtensions.Contains(f.Extension.ToLowerInvariant()))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            return new
            {
                total = files.Count,
                totalMb = Math.Round(files.Sum(f => f.Length) / 1048576.0, 1),
                shots = files.Take(ScreenshotLimit).Select(f => new
                {
                    name = f.Name,
                    url = $"{LocalFiles.Url(f.FullName)}?v={f.LastWriteTimeUtc.Ticks}",
                    takenAt = f.LastWriteTimeUtc.ToString("o"),
                    sizeKb = (int)Math.Ceiling(f.Length / 1024.0),
                }),
            };
        }

        private object OpenScreenshot(string id, string name, bool reveal)
        {
            string path = ScreenshotPath(id, name);
            if (reveal) _host.ShowInFolder(path, selectFile: true);
            else _host.OpenFile(path);
            return new { opened = true };
        }

        /// <summary>
        /// Dosyayı panoya kopyalar (Gezgin'deki "Kopyala" gibi): Discord'a, sohbete ya da bir klasöre yapıştırılabilir.
        /// </summary>
        private object CopyScreenshot(string id, string name)
        {
            string path = ScreenshotPath(id, name);
            if (!_host.CopyFileToClipboard(path)) throw new InvalidOperationException("Pano şu an kullanılamıyor, tekrar dene.");
            return new { copied = true };
        }

        /// <summary>Geri dönüşüm kutusuna taşır (kalıcı silmez).</summary>
        private object DeleteScreenshot(string id, string name)
        {
            string path = ScreenshotPath(id, name);
            if (!_host.MoveToTrash(path)) throw new InvalidOperationException("Ekran görüntüsü silinemedi.");
            return new { deleted = true };
        }

        // ════════════════════════════════════════════════════════════════
        // VULU PACK GÜNCELLEMELERİ
        // ════════════════════════════════════════════════════════════════
        /// <summary>Kurulu vulu pack'lerden yeni revizyonu yayınlanmış olanlar.</summary>
        private async Task<object?> PackUpdatesAsync()
        {
            RequireLogin();
            var installed = InstanceStore.List().Where(m => m.SharedPackId != null).ToList();
            if (installed.Count == 0) return new { updates = Array.Empty<object>() };
            var packs = (await Packs.ListAsync()).ToDictionary(p => p.Id);
            var updates = installed
                .Where(m => packs.TryGetValue(m.SharedPackId!, out var p) && p.Revision > m.SharedPackRevision)
                .Select(m =>
                {
                    var p = packs[m.SharedPackId!];
                    return new { instanceId = InstancePrefix + m.Folder, packId = p.Id, name = p.Name, from = m.SharedPackRevision, to = p.Revision };
                })
                .ToList();
            return new { updates };
        }

        /// <summary>Girişten sonra ve sonra her 20 dakikada bir: yeni güncelleme varsa arayüze ve (launcher arkadaysa) bildirime.</summary>
        private void StartPackUpdateChecks()
        {
            lock (_packUpdatesNotified)
            {
                if (_packChecking) return;
                _packChecking = true;
            }
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(20));   // girişteki diğer isteklerin önüne geçmesin
                while (true)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(_login.Token))
                        {
                            var result = JsonSerializer.SerializeToElement(await PackUpdatesAsync());
                            var updates = result.GetProperty("updates").EnumerateArray().ToList();
                            _bridge.Emit("packUpdates", result);
                            foreach (var u in updates)
                            {
                                string key = $"{u.GetProperty("packId").GetString()}@{u.GetProperty("to").GetInt32()}";
                                bool fresh;
                                lock (_packUpdatesNotified) fresh = _packUpdatesNotified.Add(key);
                                if (fresh && AppSettings.Current.PackUpdateNotifications)
                                    NotifyIfAway($"{u.GetProperty("name").GetString()} güncellendi",
                                        $"Paketin yeni sürümü (rev {u.GetProperty("to").GetInt32()}) hazır. Güncellemek için tıkla.",
                                        "pack:" + u.GetProperty("instanceId").GetString());
                            }
                        }
                    }
                    catch (Exception) { /* bağlantı yoksa bir sonraki turda */ }
                    await Task.Delay(PackCheckInterval);
                }
            });
        }
    }
}
