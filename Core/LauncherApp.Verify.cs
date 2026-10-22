using System.Security.Cryptography;

namespace VuluLauncher
{
    /// <summary>
    /// "Dosyaları doğrula": paket açılmıyorsa ya da bir şeyler bozulduysa instance'ı denetler ve onarır.
    /// - Kurulu içerikler (Modrinth/CurseForge): eksik ya da hash'i tutmayan dosya yeniden indirilir.
    ///   CurseForge'un elle indirilmesini istediği dosya bekleyenlere eklenir.
    /// - mods klasöründeki boş/bozuk jar'lar ve kopyalar mods-yedek'e taşınır (silinmez).
    /// - Oyun dosyaları (Minecraft, kütüphaneler, mod yükleyici) denetlenir, bozuk ya da eksik olan yeniden iner.
    /// - Eksik bağımlılıklar bildirilir.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private void RegisterVerifyCommands()
        {
            _bridge.On("instances.verify", async args => await VerifyInstanceAsync(args.Str("id")));
        }

        private sealed record ContentCheck(InstalledContent Content, string? Url, string? Sha1, long Size, bool ManualOnly);

        private async Task<object?> VerifyInstanceAsync(string id)
        {
            RequireLogin();
            if (_game.IsRunning) throw new InvalidOperationException("Oyun açıkken dosyalar doğrulanamaz.");
            var (target, _) = ResolveTarget(id);
            var meta = id.StartsWith(InstancePrefix, StringComparison.Ordinal) ? InstanceStore.Get(id[InstancePrefix.Length..]) : null;
            string dir = Path.Combine(AppPaths.Instances, target.Folder);

            var repaired = new List<string>();
            var notes = new List<string>();
            int checkedCount = 0;
            using var job = new DownloadJob($"{target.Name} doğrulanıyor") { Phase = "İçerikler denetleniyor" };
            Log($"{target.Name}: dosyalar doğrulanıyor...", "accent");

            // 1. Kurulu içerikler
            if (meta != null && meta.Content.Count > 0)
            {
                try
                {
                    var checks = await ContentChecksAsync(meta);
                    checkedCount = checks.Count;
                    var manualChecks = new List<ContentCheck>();
                    foreach (var check in checks)
                    {
                        string? path = Downloader.ResolveInside(dir, check.Content.File);
                        if (path == null) continue;
                        // Kapatılmış içerik ".disabled" uzantısıyla durur; onarılınca da kapalı kalır
                        string actual = File.Exists(path) ? path : File.Exists(path + ".disabled") ? path + ".disabled" : path;
                        if (File.Exists(actual) && Matches(actual, check)) continue;

                        if (check.ManualOnly || check.Url == null)
                        {
                            if (check.ManualOnly && check.Content.Source == CurseForgeService.Source) manualChecks.Add(check);
                            else notes.Add($"{check.Content.Title}: dosya bozuk ya da eksik ve kaynağında artık bulunamıyor.");
                            continue;
                        }
                        await job.Add(check.Content.Title, check.Size).DownloadAsync(check.Url, actual, check.Sha1);
                        repaired.Add(check.Content.Title);
                    }
                    if (manualChecks.Count > 0)
                    {
                        // Elle indirilecekler için dosyanın tam CurseForge sayfası
                        var mods = (await _curseforge.ModsAsync(manualChecks.Select(m => int.Parse(m.Content.ProjectId)))).ToDictionary(m => m.Id.ToString());
                        foreach (var check in manualChecks)
                            QueueManual(meta, check, mods.TryGetValue(check.Content.ProjectId, out var mod)
                                ? $"{CurseForgeService.PageUrl(mod)}/files/{check.Content.VersionId}" : "https://www.curseforge.com/minecraft");
                    }
                    InstanceStore.Save(meta);
                }
                catch (Exception ex) when (ex is HttpRequestException or ApiException or TaskCanceledException)
                {
                    notes.Add("İçerikler denetlenemedi (Modrinth ya da CurseForge'a ulaşılamadı).");
                }
            }

            // 2. Boş/bozuk jar'lar ve kopyalar yedeğe
            job.Phase = "Mod dosyaları denetleniyor";
            var moved = new List<string>();
            var problems = await Task.Run(() => ManualFiles.Check(dir, meta, target.Loader));
            if (problems != null)
            {
                var bad = problems.Broken.Select(b => b.File).Concat(problems.Duplicates.Select(d => d.File)).ToList();
                if (bad.Count > 0)
                {
                    moved = await Task.Run(() => ManualFiles.MoveToBackup(dir, bad));
                    if (meta != null && meta.Content.RemoveAll(c => moved.Contains(c.File, StringComparer.OrdinalIgnoreCase)) > 0) InstanceStore.Save(meta);
                }
            }

            // 3. Oyun dosyaları
            job.Phase = "Oyun dosyaları denetleniyor";
            string? gameError = null;
            try { await _game.VerifyGameAsync(target); }
            catch (Exception ex) { gameError = ex.Message; }

            // 4. Eksik bağımlılıklar (oyun açılırken de sorulur; burada yalnızca bildirilir)
            var missing = target.Loader == "vanilla" ? new List<string>()
                : (await Task.Run(() => ModScanner.Scan(dir, target.Loader))).Missing.Select(m => m.Id).ToList();

            var pending = meta == null ? new List<ManualDownload>()
                : meta.PendingManual.Where(m => Downloader.ResolveInside(dir, $"{m.Folder}/{m.FileName}") is not { } p || !File.Exists(p)).ToList();

            if (gameError == null) job.Complete($"{target.Name} doğrulandı");
            else job.Fail(gameError);
            Log(repaired.Count + moved.Count == 0 && gameError == null
                    ? $"{target.Name}: dosyalarda sorun bulunmadı."
                    : $"{target.Name}: {repaired.Count} içerik yeniden indirildi, {moved.Count} dosya yedeğe taşındı.",
                gameError == null ? "ok" : "warn");

            return new
            {
                checkedCount,
                repaired,
                moved,
                notes,
                missing,
                manual = ManualList(pending),
                gameError,
            };
        }

        /// <summary>İçeriklerin kaynağındaki indirme adresi, SHA-1'i ve boyutu (sürüm kimliğine göre).</summary>
        private async Task<List<ContentCheck>> ContentChecksAsync(InstanceMeta meta)
        {
            var result = new List<ContentCheck>();
            var cf = meta.Content.Where(c => c.Source == CurseForgeService.Source && int.TryParse(c.VersionId, out _)).ToList();
            var mr = meta.Content.Where(c => c.Source != CurseForgeService.Source && c.VersionId.Length > 0).ToList();

            if (cf.Count > 0)
            {
                var files = (await _curseforge.FilesByIdsAsync(cf.Select(c => int.Parse(c.VersionId)))).ToDictionary(f => f.Id.ToString());
                foreach (var c in cf)
                {
                    if (!files.TryGetValue(c.VersionId, out var f)) { result.Add(new(c, null, null, 0, false)); continue; }
                    string? url = string.IsNullOrEmpty(f.DownloadUrl) || !CurseForgeService.IsCurseForgeCdn(f.DownloadUrl) ? null : f.DownloadUrl;
                    result.Add(new(c, url, f.Hashes.FirstOrDefault(h => h.Algo == 1)?.Value, f.FileLength, string.IsNullOrEmpty(f.DownloadUrl)));
                }
            }
            if (mr.Count > 0)
            {
                var versions = (await _modrinth.VersionsByIdsAsync(mr.Select(c => c.VersionId))).ToDictionary(v => v.Id);
                foreach (var c in mr)
                {
                    if (!versions.TryGetValue(c.VersionId, out var v)) { result.Add(new(c, null, null, 0, false)); continue; }
                    string name = Path.GetFileName(c.File);
                    var file = v.Files.FirstOrDefault(f => string.Equals(f.Filename, name, StringComparison.OrdinalIgnoreCase))
                        ?? v.Files.FirstOrDefault(f => f.Primary) ?? v.Files.FirstOrDefault();
                    string? url = file != null && file.Url.StartsWith("https://cdn.modrinth.com/", StringComparison.Ordinal) ? file.Url : null;
                    result.Add(new(c, url, file?.Hashes?.Sha1, file?.Size ?? 0, false));
                }
            }
            return result;
        }

        /// <summary>Dosya beklenenle aynı mı: hash biliniyorsa hash, yoksa boş olmaması yeter.</summary>
        private static bool Matches(string path, ContentCheck check)
        {
            var info = new FileInfo(path);
            if (info.Length == 0) return false;
            if (string.IsNullOrEmpty(check.Sha1)) return true;
            if (check.Size > 0 && info.Length != check.Size) return false;
            using var stream = info.OpenRead();
            return string.Equals(Convert.ToHexString(SHA1.HashData(stream)), check.Sha1, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Bozuk ya da eksik, ama yapımcısı başka uygulamalardan indirmeye izin vermeyen CurseForge dosyası.</summary>
        private static void QueueManual(InstanceMeta meta, ContentCheck check, string pageUrl)
        {
            var c = check.Content;
            if (meta.PendingManual.Any(p => p.FileId == c.VersionId)) return;
            string folder = c.File.Contains('/') ? c.File[..c.File.IndexOf('/')] : "mods";
            meta.PendingManual.Add(new ManualDownload(c.Title, pageUrl, folder, Path.GetFileName(c.File),
                check.Sha1?.ToLowerInvariant(), check.Size, c.ProjectId, c.VersionId, c.Type, c.IconUrl));
        }
    }
}
