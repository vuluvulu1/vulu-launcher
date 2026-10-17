using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    /// <summary>
    /// Dünyalar ve instance araçları: dünya yedekleme/geri yükleme (paket güncellemesinden önce otomatik),
    /// instance çoğaltma ve .mrpack olarak dışa aktarma.
    /// Yedekler oyun klasörünün dışında, %LocalAppData%\vuluLauncher\backups altında durur.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private static readonly string BackupsRoot = Path.Combine(AppPaths.AppData, "backups");
        private static readonly Regex BackupName = new(@"^[^\\/:*?""<>|]{1,120}\.zip$", RegexOptions.Compiled);

        private void RegisterWorldCommands()
        {
            _bridge.On("worlds.list", async args => await Task.Run(() => ListWorlds(args.Str("id"))));
            _bridge.On("worlds.backup", async args => await Task.Run(() => BackupWorld(args.Str("id"), args.Str("world"), "elle")));
            _bridge.On("worlds.restore", async args => await Task.Run(() => RestoreWorld(args.Str("id"), args.Str("world"), args.Str("backup"))));
            _bridge.On("worlds.deleteBackup", args => Task.FromResult<object?>(DeleteBackup(args.Str("id"), args.Str("world"), args.Str("backup"))));
            _bridge.On("worlds.openBackups", args => Task.FromResult<object?>(OpenBackups(args.Str("id"))));
            _bridge.On("instances.duplicate", async args => await Task.Run(() => DuplicateInstance(args.Str("id"), args.Str("name"))));
            _bridge.On("instances.exportMrpack", async args => await ExportMrpackAsync(args.Str("id")));
        }

        // ── Dünyalar ──────────────────────────────────────────────────────
        private string InstanceDirOf(string id) => Path.Combine(AppPaths.Instances, ResolveTarget(id).Target.Folder);
        private string BackupDirOf(string id, string world) => Path.Combine(BackupsRoot, ResolveTarget(id).Target.Folder, SafeWorld(world));

        /// <summary>Dünya klasörü adı: tek seviye, yol karakteri yok.</summary>
        private static string SafeWorld(string world)
        {
            if (string.IsNullOrWhiteSpace(world) || world.Length > 120 || world.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || world is "." or "..")
                throw new InvalidOperationException("Geçersiz dünya adı.");
            return world;
        }

        private object ListWorlds(string id)
        {
            RequireLogin();
            string saves = Path.Combine(InstanceDirOf(id), "saves");
            var worlds = !Directory.Exists(saves) ? new List<object>() : new DirectoryInfo(saves).EnumerateDirectories()
                .Where(d => File.Exists(Path.Combine(d.FullName, "level.dat")))
                .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d.FullName, "level.dat")))
                .Select(d =>
                {
                    string backupDir = BackupDirOf(id, d.Name);
                    var backups = Directory.Exists(backupDir)
                        ? new DirectoryInfo(backupDir).EnumerateFiles("*.zip").OrderByDescending(f => f.LastWriteTimeUtc)
                            .Select(f => (object)new { name = f.Name, sizeMb = Math.Round(f.Length / 1048576.0, 1), createdAt = f.LastWriteTimeUtc.ToString("o"), auto = f.Name.Contains("-otomatik-") }).ToList()
                        : new List<object>();
                    string icon = Path.Combine(d.FullName, "icon.png");
                    return (object)new
                    {
                        name = d.Name,
                        lastPlayed = File.GetLastWriteTimeUtc(Path.Combine(d.FullName, "level.dat")).ToString("o"),
                        sizeMb = Math.Round(DirSize(d) / 1048576.0, 1),
                        icon = File.Exists(icon) && new FileInfo(icon).Length < 256 * 1024 ? "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(icon)) : null,
                        backups,
                    };
                }).ToList();
            return new { worlds, running = _game.IsRunning };
        }

        private static long DirSize(DirectoryInfo dir) =>
            dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => { try { return f.Length; } catch { return 0L; } });

        private object BackupWorld(string id, string world, string kind)
        {
            RequireLogin();
            if (_game.IsRunning && kind != "otomatik") throw new InvalidOperationException("Oyun açıkken dünya yedeklenemez; önce oyunu kapat.");
            string name = WorldBackups.Backup(Path.Combine(InstanceDirOf(id), "saves", SafeWorld(world)), BackupDirOf(id, world), kind);
            Log($"{world} yedeklendi ({name}).", "ok");
            return new { name };
        }

        private object RestoreWorld(string id, string world, string backup)
        {
            RequireLogin();
            if (_game.IsRunning) throw new InvalidOperationException("Oyun açıkken dünya geri yüklenemez; önce oyunu kapat.");
            if (!BackupName.IsMatch(backup)) throw new InvalidOperationException("Geçersiz yedek.");
            string backupDir = BackupDirOf(id, world);
            WorldBackups.Restore(Path.Combine(backupDir, backup), Path.Combine(InstanceDirOf(id), "saves", SafeWorld(world)), backupDir);
            Log($"{world}, {backup} yedeğinden geri yüklendi.", "ok");
            return new { restored = true };
        }

        private object DeleteBackup(string id, string world, string backup)
        {
            RequireLogin();
            if (!BackupName.IsMatch(backup)) throw new InvalidOperationException("Geçersiz yedek.");
            string path = Path.Combine(BackupDirOf(id, world), backup);
            if (File.Exists(path)) File.Delete(path);
            return new { deleted = true };
        }

        private object OpenBackups(string id)
        {
            RequireLogin();
            string dir = Path.Combine(BackupsRoot, ResolveTarget(id).Target.Folder);
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { dir } });
            return new { opened = true };
        }

        /// <summary>Paket güncellemesinden önce: instance'taki bütün dünyalar otomatik yedeklenir.</summary>
        private void AutoBackupWorlds(InstanceMeta meta, string reason)
        {
            string saves = Path.Combine(InstanceStore.PathOf(meta.Folder), "saves");
            if (!Directory.Exists(saves)) return;
            foreach (var world in new DirectoryInfo(saves).EnumerateDirectories().Where(d => File.Exists(Path.Combine(d.FullName, "level.dat"))))
            {
                try { BackupWorld(InstancePrefix + meta.Folder, world.Name, "otomatik"); }
                catch (Exception ex) { Log($"{world.Name} yedeklenemedi ({reason}): {ex.Message}", "warn"); }
            }
        }

        // ── Çoğaltma ──────────────────────────────────────────────────────
        private static readonly HashSet<string> NotCopied = new(StringComparer.OrdinalIgnoreCase) { "logs", "crash-reports", ".cache", "debug" };

        private object DuplicateInstance(string id, string name)
        {
            var source = RequireCustomInstance(id);
            if (_game.IsRunning) throw new InvalidOperationException("Oyun açıkken instance çoğaltılamaz.");
            var copy = InstanceStore.Create(string.IsNullOrWhiteSpace(name) ? source.Name + " (kopya)" : name, source.McVersion, source.Loader, source.LoaderVersion);
            string from = InstanceStore.PathOf(source.Folder), to = InstanceStore.PathOf(copy.Folder);

            foreach (var entry in new DirectoryInfo(from).EnumerateFileSystemInfos())
            {
                if (NotCopied.Contains(entry.Name) || entry.Name == "instance.json") continue;
                if (entry is DirectoryInfo dir) CopyDirectory(dir.FullName, Path.Combine(to, dir.Name));
                else File.Copy(entry.FullName, Path.Combine(to, entry.Name), overwrite: true);
            }

            // Kopya kendi başına bir instance: paket bağı kopyalanmaz (aynı paketin iki "kurulu" kopyası olmasın)
            copy.RamGb = source.RamGb;
            copy.Description = source.Description;
            copy.IconUrl = source.IconUrl;
            copy.Content = source.Content.Select(c => new InstalledContent
            {
                ProjectId = c.ProjectId, VersionId = c.VersionId, Title = c.Title, VersionNumber = c.VersionNumber,
                Type = c.Type, File = c.File, IconUrl = c.IconUrl, FromPack = false,
            }).ToList();
            InstanceStore.Save(copy);

            // Görünüm (arka plan, logo) de kopyalanır
            foreach (var folder in new[] { AppPaths.Backgrounds, AppPaths.Logos })
            {
                string? file = ImageFile(folder, id);
                if (file != null) File.Copy(file, Path.Combine(folder, BackgroundKey(InstancePrefix + copy.Folder) + Path.GetExtension(file)), overwrite: true);
            }
            Log($"{source.Name} çoğaltıldı: {copy.Name}.", "ok");
            return new { id = InstancePrefix + copy.Folder };
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from))
            {
                if (Path.GetFileName(file) == "session.lock") continue;
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
            }
            foreach (var dir in Directory.EnumerateDirectories(from))
                CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        // ── .mrpack dışa aktarma ──────────────────────────────────────────
        /// <summary>
        /// Instance'ı Modrinth modpack'i (.mrpack) olarak dışa aktarır: Modrinth'te bulunan modlar bağlantı olarak
        /// (modrinth.index.json), diğer dosyalar (config, kubejs, Modrinth dışı modlar…) overrides/ altında.
        /// Dünyalar, kişisel ayarlar ve loglar girmez. Dosya İndirilenler klasörüne yazılır.
        /// </summary>
        private async Task<object?> ExportMrpackAsync(string id)
        {
            var meta = RequireCustomInstance(id);
            if (meta.Loader == "vanilla") throw new InvalidOperationException("Vanilla instance'ın dışa aktarılacak modu yok.");
            string dir = InstanceStore.PathOf(meta.Folder);
            Log($"{meta.Name} dışa aktarılıyor...", "accent");

            var (mods, fallback) = await ResolvePackModsAsync(meta, dir);
            var tracked = TrackedFiles(meta);
            var includes = SharedByDefault.Where(e => Downloader.ResolveInside(dir, e) is { } p && (Directory.Exists(p) || File.Exists(p)))
                .Where(e => !e.Equals("optionsshaders.txt", StringComparison.OrdinalIgnoreCase)).ToList();
            mods.AddRange(await ResolveUntrackedByHashAsync(dir, includes, tracked));

            // Bağlantı olamayanlar (Modrinth'te SHA-512'si ya da adresi yok) pakete dosya olarak girer, kaybolmaz
            fallback.AddRange(mods.Where(m => string.IsNullOrEmpty(m.Sha512) || string.IsNullOrEmpty(m.Url)).Select(m => m.File));
            var files = mods.Where(m => !string.IsNullOrEmpty(m.Sha512) && !string.IsNullOrEmpty(m.Url)).Select(m => new
            {
                path = m.File,
                hashes = new { sha1 = m.Sha1, sha512 = m.Sha512 },
                env = new { client = "required", server = m.Type == "mod" ? "required" : "unsupported" },
                downloads = new[] { m.Url },
                fileSize = m.Size,
            }).ToList();
            var linked = new HashSet<string>(files.Select(f => f.path), StringComparer.OrdinalIgnoreCase);

            string loaderKey = meta.Loader switch { "fabric" => "fabric-loader", "quilt" => "quilt-loader", "forge" => "forge", _ => "neoforge" };
            var index = new
            {
                formatVersion = 1, game = "minecraft", versionId = DateTime.Now.ToString("yyyy.MM.dd"), name = meta.Name,
                summary = meta.Description ?? "", files,
                dependencies = new Dictionary<string, string> { ["minecraft"] = meta.McVersion, [loaderKey] = meta.LoaderVersion ?? "" },
            };

            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloads);
            string safeName = string.Concat(meta.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
            string target = Path.Combine(downloads, $"{safeName}.mrpack");
            for (int n = 2; File.Exists(target); n++) target = Path.Combine(downloads, $"{safeName} ({n}).mrpack");

            int count = await Task.Run(() =>
            {
                int added = 0;
                using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
                using (var s = zip.CreateEntry("modrinth.index.json").Open())
                    JsonSerializer.Serialize(s, index, new JsonSerializerOptions { WriteIndented = true });
                foreach (var include in includes)
                    foreach (var file in SharedFiles(dir, include, tracked))
                    {
                        string relative = Path.GetRelativePath(dir, file).Replace('\\', '/');
                        if (linked.Contains(relative)) continue;
                        zip.CreateEntryFromFile(file, "overrides/" + relative, CompressionLevel.Optimal);
                        added++;
                    }
                foreach (var relative in fallback.Where(f => !linked.Contains(f)))
                    if (Downloader.ResolveInside(dir, relative) is { } p && File.Exists(p)) { zip.CreateEntryFromFile(p, "overrides/" + relative); added++; }
                return added;
            });

            Log($"{meta.Name} dışa aktarıldı: {Path.GetFileName(target)} ({files.Count} Modrinth bağlantısı, {count} dosya).", "ok");
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", target } });
            return new { path = target, links = files.Count, files = count };
        }
    }
}
