using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    /// <summary>
    /// Oyunun sağlığı: çökme sonrası özet ve tek tıklık çözümler, modları açıp kapatma, mod güncellemeleri.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        /// <summary>Kapatılmış modlar bu ekle durur; oyun yüklemez (mods/x.jar → mods/x.jar.disabled).</summary>
        private const string DisabledSuffix = ".disabled";
        private static readonly Regex ReportName = new(@"^crash-[A-Za-z0-9_.\-]{1,80}\.txt$", RegexOptions.Compiled);

        private void RegisterHealthCommands()
        {
            _bridge.On("crash.openReport", args => Task.FromResult<object?>(OpenCrashReport(args.Str("instanceId"), args.Str("file"))));
            _bridge.On("instances.setContentEnabled", args => Task.FromResult<object?>(SetContentEnabled(args.Str("id"), args.Str("file"), args.Bool("enabled"))));
            _bridge.On("instances.checkUpdates", async args => await CheckUpdatesAsync(args.Str("id")));
            _bridge.On("mods.update", async args => await UpdateModAsync(args.Str("instanceId"), args.Str("projectId"), args.Str("versionId")));
        }

        // ── Çökme yardımcısı ──────────────────────────────────────────────
        /// <summary>Oyun kapanınca çağrılır: çöktüyse nedenini bulup arayüze sade bir özet gönderir.</summary>
        private async Task ReportCrashIfAnyAsync()
        {
            var exit = _game.LastExit;
            if (exit == null) return;
            try
            {
                var analysis = await Task.Run(() => CrashAnalyzer.Analyze(exit));
                if (!analysis.Crashed) return;

                string instanceId = InstanceStore.List().Any(m => m.Folder == exit.Target.Folder)
                    ? InstancePrefix + exit.Target.Folder
                    : PackPrefix + (PackCatalog.All.FirstOrDefault(p => p.FolderName == exit.Target.Folder)?.Id ?? "");
                var meta = instanceId.StartsWith(InstancePrefix, StringComparison.Ordinal) ? InstanceStore.Get(exit.Target.Folder) : null;
                var scan = exit.Target.Loader == "vanilla" ? null : ModScanner.Scan(exit.InstanceDir, exit.Target.Loader);

                var findings = new List<object>();
                foreach (var f in analysis.Findings)
                    findings.Add(new { kind = f.Kind, title = f.Title, detail = f.Detail, fix = f.Fix, actions = await CrashActionsAsync(f, meta, scan, exit) });

                Log($"Oyun çöktü: {string.Join(" · ", analysis.Findings.Select(f => f.Title))}", "error");
                _bridge.Emit("gameCrashed", new
                {
                    instanceId,
                    name = exit.Target.Name,
                    report = analysis.ReportFile,
                    description = analysis.Description,
                    exitCode = exit.ExitCode,
                    findings,
                });
            }
            catch (Exception ex) { Log("Çökme raporu okunamadı: " + ex.Message, "muted"); }
        }

        /// <summary>Bir bulgu için tek tıklık çözümler (yalnızca kişisel instance'larda; sunucu paketleri değiştirilmez).</summary>
        private async Task<List<object>> CrashActionsAsync(CrashFinding finding, InstanceMeta? meta, ModScan? scan, GameExit exit)
        {
            var actions = new List<object>();
            if (meta == null || scan == null || finding.ModIds.Count == 0) return actions;
            try
            {
                if (finding.Kind == "missing")
                {
                    var missing = finding.ModIds.Select(id =>
                        scan.Missing.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? new MissingDependency(id, Array.Empty<string>())).ToList();
                    var lookup = _modrinth.ResolveModIdsAsync(meta, scan, missing);
                    if (await Task.WhenAny(lookup, Task.Delay(TimeSpan.FromSeconds(12))) != lookup) return actions;
                    var projects = (await lookup).Values.Where(p => p != null).Select(p => p!).ToList();
                    if (projects.Count > 0)
                        actions.Add(new { type = "install", label = projects.Count == 1 ? $"{projects[0].Title} kur" : $"Eksikleri kur ({projects.Count})", projectIds = projects.Select(p => p.Id).ToList() });
                    return actions;
                }

                // Suçlu mod: önce güncellemesi (gerekirse beta), sonra kapatma
                foreach (var id in finding.ModIds.Take(2))
                {
                    var mod = scan.Find(id);
                    if (mod == null) continue;
                    var newer = await NewerVersionAsync(meta, Path.Combine(exit.InstanceDir, "mods", mod.File));
                    if (newer is { } n)
                        actions.Add(new
                        {
                            type = "update", projectId = n.Version.ProjectId, versionId = n.Version.Id,
                            label = $"{mod.Name} güncelle ({n.Version.VersionNumber}{(n.Version.VersionType == "release" ? "" : " · " + n.Version.VersionType)})",
                        });
                    actions.Add(new { type = "disable", file = "mods/" + mod.File, label = $"{mod.Name} kapat" });
                }
            }
            catch (Exception) { /* çözüm önerilemese de özet gösterilir */ }
            return actions;
        }

        /// <summary>
        /// Dosyanın Modrinth'te bu Minecraft sürümü ve yükleyici için daha yeni bir sürümü var mı (beta/alpha dahil).
        /// Kararlı sürüm uyumsuz kaldığında düzeltme çoğu zaman yalnızca betada olur (ör. Sodium ve yeni NeoForge).
        /// </summary>
        private async Task<(MrVersion Version, MrVersion Current)?> NewerVersionAsync(InstanceMeta meta, string path)
        {
            if (!File.Exists(path)) return null;
            string sha1 = Downloader.Sha1Of(path);
            var found = await _modrinth.VersionsByHashesAsync(new[] { sha1 });
            if (!found.TryGetValue(sha1, out var current)) return null;
            var versions = await _modrinth.VersionsAsync(current.ProjectId, meta.McVersion, ModrinthService.CompatibleLoadersOf(meta.Loader));
            var newest = versions.OrderByDescending(v => v.DatePublished).FirstOrDefault();
            if (newest == null || newest.Id == current.Id || string.CompareOrdinal(newest.DatePublished, current.DatePublished) <= 0) return null;
            return (newest, current);
        }

        private object OpenCrashReport(string instanceId, string file)
        {
            if (!ReportName.IsMatch(file)) throw new InvalidOperationException("Geçersiz rapor adı.");
            string path = Path.Combine(AppPaths.Instances, ResolveTarget(instanceId).Target.Folder, "crash-reports", file);
            if (!File.Exists(path)) throw new FileNotFoundException("Çökme raporu bulunamadı.");
            _host.OpenFile(path);
            return new { opened = true };
        }

        // ── Modları açma / kapatma ────────────────────────────────────────
        private object SetContentEnabled(string id, string file, bool enabled)
        {
            var meta = RequireCustomInstance(id);
            if (_game.IsRunning) throw new InvalidOperationException("Oyun çalışırken modlar açılıp kapatılamaz.");
            string normalized = file.Replace('\\', '/');
            string dir = InstanceStore.PathOf(meta.Folder);
            string baseFile = normalized.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase) ? normalized[..^DisabledSuffix.Length] : normalized;
            var parts = baseFile.Split('/');
            if (parts.Length != 2 || !ContentFolders.Contains(parts[0]))
                throw new InvalidOperationException("Geçersiz dosya.");

            string? on = Downloader.ResolveInside(dir, baseFile);
            if (on == null) throw new InvalidOperationException("Geçersiz dosya.");
            string off = on + DisabledSuffix;
            if (enabled && File.Exists(off)) File.Move(off, on, overwrite: true);
            else if (!enabled && File.Exists(on)) File.Move(on, off, overwrite: true);
            Log($"{Path.GetFileName(baseFile)} {(enabled ? "açıldı" : "kapatıldı")}.", "muted");
            return new { file = enabled ? baseFile : baseFile + DisabledSuffix, enabled };
        }

        // ── Mod güncellemeleri ────────────────────────────────────────────
        /// <summary>
        /// İçerik klasörlerindeki dosyaların Modrinth'teki güncellemeleri: hash'lerden kurulu sürüm bulunur, aynı
        /// Minecraft sürümü ve yükleyici için daha yeni sürüm aranır. Kararlı güncellemeler "Tümünü güncelle"ye girer;
        /// yalnızca beta/alpha varsa ayrıca işaretlenir.
        /// </summary>
        private async Task<object?> CheckUpdatesAsync(string id)
        {
            var meta = RequireCustomInstance(id);
            string dir = InstanceStore.PathOf(meta.Folder);
            var files = ContentFolders
                .Where(f => Directory.Exists(Path.Combine(dir, f)))
                .SelectMany(f => Directory.GetFiles(Path.Combine(dir, f)).Select(p => (Folder: f, Path: p)))
                .Where(x => !x.Path.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var hashes = await Task.Run(() => files.ToDictionary(x => x.Path, x => Downloader.Sha1Of(x.Path)));
            var current = await _modrinth.VersionsByHashesAsync(hashes.Values);

            // Klasöre göre "yükleyici": modlarda instance'ınki, kaynak paketlerinde minecraft, shaderlarda shader yükleyicileri
            var loadersOf = new Dictionary<string, string[]>
            {
                ["mods"] = ModrinthService.CompatibleLoadersOf(meta.Loader),
                ["resourcepacks"] = new[] { "minecraft" },
                ["shaderpacks"] = new[] { "iris", "optifine", "canvas", "vanilla" },
            };
            var latest = new Dictionary<string, MrVersion>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in files.Where(x => current.ContainsKey(hashes[x.Path])).GroupBy(x => x.Folder))
            {
                try
                {
                    foreach (var (hash, version) in await _modrinth.LatestByHashesAsync(group.Select(x => hashes[x.Path]), loadersOf[group.Key], meta.McVersion))
                        latest[hash] = version;
                }
                catch (Exception) { /* bu tür denetlenemedi; diğerleri gösterilir */ }
            }

            var updates = new List<object>();
            foreach (var x in files)
            {
                string hash = hashes[x.Path];
                if (!current.TryGetValue(hash, out var cur) || !latest.TryGetValue(hash, out var newest)) continue;
                if (newest.Id == cur.Id || string.CompareOrdinal(newest.DatePublished, cur.DatePublished) <= 0) continue;

                // En yenisi beta/alpha ise daha yeni bir kararlı sürüm var mı (yalnızca bu birkaç proje için ayrı istek)
                var pick = newest;
                bool stable = newest.VersionType == "release";
                if (!stable)
                {
                    try
                    {
                        var release = (await _modrinth.VersionsAsync(cur.ProjectId, meta.McVersion, loadersOf[x.Folder]))
                            .Where(v => v.VersionType == "release" && string.CompareOrdinal(v.DatePublished, cur.DatePublished) > 0)
                            .OrderByDescending(v => v.DatePublished).FirstOrDefault();
                        if (release != null) { pick = release; stable = true; }
                    }
                    catch (Exception) { }
                }
                updates.Add(new
                {
                    file = $"{x.Folder}/{Path.GetFileName(x.Path)}", projectId = cur.ProjectId, versionId = pick.Id,
                    from = cur.VersionNumber, to = pick.VersionNumber, type = pick.VersionType, stable,
                });
            }
            return new { updates };
        }

        private async Task<object?> UpdateModAsync(string instanceId, string projectId, string versionId)
        {
            var meta = RequireCustomInstance(instanceId);
            if (_game.IsRunning) throw new InvalidOperationException("Oyun çalışırken mod güncellenemez.");
            if (!Regex.IsMatch(projectId, "^[A-Za-z0-9]{6,16}$") || !Regex.IsMatch(versionId, "^[A-Za-z0-9]{6,16}$"))
                throw new InvalidOperationException("Geçersiz sürüm.");

            // Elle eklenmiş (izlenmeyen) dosyalar hash'ten bulunur. Önce yenisi kurulur, eskiler ancak ondan sonra silinir:
            // indirme başarısız olursa mod kaybolmaz. İzlenen dosyaları InstallAsync kendisi değiştirir.
            string dir = InstanceStore.PathOf(meta.Folder);
            var stale = meta.Content.Any(c => c.ProjectId == projectId) ? new List<string>() : await FilesOfProjectAsync(dir, projectId);
            var installed = await _modrinth.InstallAsync(meta, projectId, versionId);
            var keep = meta.Content.Where(c => c.ProjectId == projectId)
                .Select(c => Downloader.ResolveInside(dir, c.File)).Where(p => p != null).Select(p => p!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in stale.Where(p => !keep.Contains(p) && File.Exists(p))) File.Delete(path);
            return new { installed };
        }

        /// <summary>İçerik klasörlerinde bu Modrinth projesine ait (izlenmeyen) dosyalar.</summary>
        private async Task<List<string>> FilesOfProjectAsync(string dir, string projectId)
        {
            var files = ContentFolders.Where(f => Directory.Exists(Path.Combine(dir, f)))
                .SelectMany(f => Directory.GetFiles(Path.Combine(dir, f))).ToList();
            var hashes = await Task.Run(() => files.ToDictionary(p => p, Downloader.Sha1Of));
            var found = await _modrinth.VersionsByHashesAsync(hashes.Values);
            return files.Where(p => found.TryGetValue(hashes[p], out var v) && v.ProjectId == projectId).ToList();
        }
    }
}
