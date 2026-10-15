using System.IO.Compression;
using System.Text.Json;
using MongoDB.Bson;

namespace VuluLauncher
{
    /// <summary>
    /// vulu Packs: adminin kendi instance'larından yayınladığı paketler. Herkes listeler ve tek tıkla kurar;
    /// yalnızca admin yayınlar ve siler.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private PackStore? _packReader;

        // Paylaşılırken listede gösterilmeyen, oyunun kendi ürettiği ya da kişisel klasör/dosyalar
        private static readonly HashSet<string> NeverShared = new(StringComparer.OrdinalIgnoreCase)
        {
            "instance.json", "saves", "logs", "crash-reports", "screenshots", "natives", "libraries", "versions",
            "assets", "downloads", "debug", ".fabric", ".mixin.out", ".cache", ".update", "local",
            "command_history.txt", "usercache.json", "usernamecache.json", "realms_persistence.json"
        };

        // Varsayılan olarak işaretli gelenler: modpack'in kendisini oluşturan klasörler (görev kitabı config ve kubejs'te)
        private static readonly HashSet<string> SharedByDefault = new(StringComparer.OrdinalIgnoreCase)
        {
            "config", "kubejs", "defaultconfigs", "mods", "resourcepacks", "shaderpacks",
            "fancymenu_data", "global_packs", "global_data", "openloader", "datapacks"
        };

        private static readonly string[] CoverExtensions = { ".png", ".jpg", ".jpeg", ".webp" };
        private const long MaxOverridesBytes = 200L * 1024 * 1024;

        private void RegisterPackCommands()
        {
            _bridge.On("packs.list", async _ => await ListPacksAsync());
            _bridge.On("packs.install", async args => await InstallPackAsync(args.Str("id")));
            _bridge.On("packs.update", async args => await UpdatePackAsync(args.Str("id")));
            _bridge.On("packs.shareInfo", async args => await ShareInfoAsync(args.Str("instanceId")));
            _bridge.On("packs.pickCover", _ => Task.FromResult<object?>(PickCover()));
            _bridge.On("packs.publish", async args => await PublishPackAsync(args));
            _bridge.On("packs.delete", async args => await DeletePackAsync(args.Str("id")));
        }

        /// <summary>Admin bağlıysa onun bağlantısı, değilse yalnızca okuma yetkili kullanıcı.</summary>
        private PackStore? ReadStore()
        {
            if (_admin != null) return new PackStore(_admin.Database);
            return _packReader ??= PackStore.CreateReader();
        }

        private PackStore RequireReadStore() => ReadStore()
            ?? throw new InvalidOperationException("vulu Packs henüz yapılandırılmadı. Admin'den okuma kullanıcısını eklemesini iste.");

        private PackStore RequireWriteStore() => new(RequireAdminConnection().Database);

        // ── Liste ─────────────────────────────────────────────────────────
        private async Task<object?> ListPacksAsync()
        {
            RequireLogin();
            var store = ReadStore();
            if (store == null)
                return new { configured = false, isAdmin = _login.IsAdmin, adminConnected = _admin != null, packs = Array.Empty<object>() };

            var packs = await store.ListAsync();
            var installed = InstanceStore.List()
                .Where(m => m.SharedPackId != null)
                .GroupBy(m => m.SharedPackId!)
                .ToDictionary(g => g.Key, g => g.First());

            var covers = await Task.WhenAll(packs.Select(p => CoverUrlAsync(store, p)));

            return new
            {
                configured = true,
                isAdmin = _login.IsAdmin,
                adminConnected = _admin != null,
                packs = packs.Select((p, i) =>
                {
                    installed.TryGetValue(p.Id.ToString(), out var meta);
                    return new
                    {
                        id = p.Id.ToString(),
                        name = p.Name,
                        description = p.Description,
                        author = p.Author,
                        mcVersion = p.McVersion,
                        loader = p.Loader,
                        loaderVersion = p.LoaderVersion,
                        revision = p.Revision,
                        updatedAt = p.UpdatedAt.ToString("o"),
                        modCount = p.Mods.Count(m => m.Type == "mod"),
                        sizeMb = Math.Round((p.OverridesSize + p.Mods.Sum(m => m.Size)) / 1024.0 / 1024.0, 1),
                        includes = p.Includes,
                        coverUrl = covers[i],
                        installed = meta == null ? null : new { instanceId = InstancePrefix + meta.Folder, revision = meta.SharedPackRevision }
                    };
                })
            };
        }

        /// <summary>Kapak resmi bir kez indirilip saklanır; revizyon değişince yenisi alınır.</summary>
        private static async Task<string?> CoverUrlAsync(PackStore store, SharedPack pack)
        {
            if (pack.CoverFileId is not ObjectId coverId) return null;
            string ext = CoverExtensions.Contains(pack.CoverExt) ? pack.CoverExt! : ".png";
            string dir = Path.Combine(AppPaths.AppData, "packcache");
            string path = Path.Combine(dir, $"{pack.Id}-{pack.Revision}{ext}");

            try
            {
                if (!File.Exists(path))
                {
                    foreach (var old in Directory.Exists(dir) ? Directory.GetFiles(dir, $"{pack.Id}-*") : Array.Empty<string>())
                        File.Delete(old);
                    await store.DownloadFileAsync(coverId, path);
                }
                return new Uri(path).AbsoluteUri;
            }
            catch
            {
                return null; // kapak alınamazsa kart düz gösterilir
            }
        }

        // ── Kurulum ve güncelleme ─────────────────────────────────────────
        private async Task<object?> InstallPackAsync(string id)
        {
            RequireLogin();
            var store = RequireReadStore();
            var pack = await store.GetAsync(id) ?? throw new InvalidOperationException("Paket bulunamadı.");

            if (InstanceStore.List().Any(m => m.SharedPackId == id))
                throw new InvalidOperationException($"{pack.Name} zaten kurulu.");

            var meta = InstanceStore.Create(pack.Name, pack.McVersion, pack.Loader, pack.LoaderVersion);
            try
            {
                Log($"{pack.Name} kuruluyor...", "accent");
                await ApplyPackAsync(store, pack, meta);

                // Kapak, instance'ın arka planı olur
                string? cover = await CoverUrlAsync(store, pack) is string url ? new Uri(url).LocalPath : null;
                if (cover != null)
                {
                    Directory.CreateDirectory(AppPaths.Backgrounds);
                    File.Copy(cover, Path.Combine(AppPaths.Backgrounds, BackgroundKey(InstancePrefix + meta.Folder) + Path.GetExtension(cover)), overwrite: true);
                }

                Log($"{pack.Name} kuruldu.", "ok");
                return new { id = InstancePrefix + meta.Folder };
            }
            catch
            {
                try { InstanceStore.Delete(meta.Folder); } catch { }
                throw;
            }
        }

        private async Task<object?> UpdatePackAsync(string id)
        {
            RequireLogin();
            if (_game.IsRunning) throw new InvalidOperationException("Oyun çalışırken paket güncellenemez.");

            var store = RequireReadStore();
            var pack = await store.GetAsync(id) ?? throw new InvalidOperationException("Paket bulunamadı.");
            var meta = InstanceStore.List().FirstOrDefault(m => m.SharedPackId == id)
                ?? throw new InvalidOperationException($"{pack.Name} kurulu değil.");

            Log($"{pack.Name} güncelleniyor (rev {meta.SharedPackRevision} → {pack.Revision})...", "accent");
            await ApplyPackAsync(store, pack, meta);
            Log($"{pack.Name} güncellendi.", "ok");
            return new { id = InstancePrefix + meta.Folder };
        }

        /// <summary>
        /// Paketin modlarını ve dosyalarını instance'a uygular. Güncellemede paketten gelip artık listede
        /// olmayan ya da sürümü değişen modlar kaldırılır; kullanıcının kendi eklediği modlara dokunulmaz.
        /// Paketteki config dosyaları instance'takilerin üzerine yazılır.
        /// </summary>
        private async Task ApplyPackAsync(PackStore store, SharedPack pack, InstanceMeta meta)
        {
            string dir = InstanceStore.PathOf(meta.Folder);
            var wanted = pack.Mods.GroupBy(m => m.ProjectId).ToDictionary(g => g.Key, g => g.First());

            foreach (var old in meta.Content.Where(c => c.FromPack).ToList())
            {
                if (wanted.TryGetValue(old.ProjectId, out var mod) && mod.VersionId == old.VersionId) continue;
                DeleteContentFile(dir, old.File);
                meta.Content.Remove(old);
            }

            var missing = wanted.Values
                .Where(m => !meta.Content.Any(c => c.FromPack && c.ProjectId == m.ProjectId))
                .ToList();

            int done = 0;
            await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (mod, cancel) =>
            {
                string path = Downloader.ResolveInside(dir, mod.File)
                    ?? throw new InvalidDataException($"Paket geçersiz bir dosya yolu içeriyor: {mod.File}");
                if (!Uri.TryCreate(mod.Url, UriKind.Absolute, out var uri) || uri.Host != "cdn.modrinth.com")
                    throw new InvalidDataException($"Paket izin verilmeyen bir adresten indirme istiyor: {mod.Title}");

                await Downloader.DownloadAsync(mod.Url, path, sha1: mod.Sha1, sha512: mod.Sha512, cancellation: cancel);
                int current = Interlocked.Increment(ref done);
                Log($"Modlar indiriliyor... {current}/{missing.Count}", "replace");
            });

            foreach (var mod in missing)
            {
                // Kullanıcı aynı modu kendisi kurduysa paketinki onun yerini alır
                var own = meta.Content.FirstOrDefault(c => c.ProjectId == mod.ProjectId);
                if (own != null)
                {
                    if (!string.Equals(own.File, mod.File, StringComparison.OrdinalIgnoreCase)) DeleteContentFile(dir, own.File);
                    meta.Content.Remove(own);
                }

                meta.Content.Add(new InstalledContent
                {
                    ProjectId = mod.ProjectId,
                    VersionId = mod.VersionId,
                    Title = mod.Title,
                    VersionNumber = mod.VersionNumber,
                    Type = mod.Type,
                    File = mod.File,
                    IconUrl = mod.IconUrl,
                    FromPack = true
                });
            }

            Log("Paket dosyaları açılıyor...", "replace");
            string zipPath = Path.Combine(Path.GetTempPath(), $"vulu-pack-{Guid.NewGuid():N}.zip");
            try
            {
                await store.DownloadFileAsync(pack.OverridesFileId, zipPath);
                await Task.Run(() =>
                {
                    using var zip = ZipFile.OpenRead(zipPath);
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.FullName.EndsWith('/')) continue;
                        if (string.Equals(entry.FullName, "instance.json", StringComparison.OrdinalIgnoreCase)) continue;
                        string? path = Downloader.ResolveInside(dir, entry.FullName);
                        if (path == null) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        entry.ExtractToFile(path, overwrite: true);
                    }
                });
            }
            finally
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
            }

            meta.McVersion = pack.McVersion;
            meta.Loader = pack.Loader;
            meta.LoaderVersion = pack.LoaderVersion;
            meta.Description = pack.Description;
            meta.RamGb = pack.RecommendedRamGb;
            meta.SharedPackId = pack.Id.ToString();
            meta.SharedPackRevision = pack.Revision;
            InstanceStore.Save(meta);
        }

        private static void DeleteContentFile(string dir, string relative)
        {
            string? path = Downloader.ResolveInside(dir, relative);
            if (path != null && File.Exists(path)) File.Delete(path);
        }

        // ── Paylaşma (admin) ──────────────────────────────────────────────
        /// <summary>Paylaş penceresi için: instance'taki klasörler, boyutları ve varsa daha önce yayınlanmış paket.</summary>
        private async Task<object?> ShareInfoAsync(string instanceId)
        {
            RequireAdmin();
            var meta = RequireCustomInstance(instanceId);
            string dir = InstanceStore.PathOf(meta.Folder);
            var tracked = TrackedFiles(meta);

            var entries = await Task.Run(() => Directory.Exists(dir)
                ? new DirectoryInfo(dir).EnumerateFileSystemInfos()
                    .Where(e => !NeverShared.Contains(e.Name))
                    .Select(e =>
                    {
                        var files = SharedFiles(dir, e.Name, tracked).ToList();
                        return new
                        {
                            path = e.Name,
                            isDir = e is DirectoryInfo,
                            files = files.Count,
                            sizeMb = Math.Round(files.Sum(f => new FileInfo(f).Length) / 1024.0 / 1024.0, 2),
                            checkedByDefault = SharedByDefault.Contains(e.Name),
                            note = ContentFolders.Contains(e.Name, StringComparer.OrdinalIgnoreCase) ? "Modrinth dışından eklenenler" : null
                        };
                    })
                    .Where(e => e.files > 0)
                    .OrderBy(e => e.isDir ? 0 : 1).ThenBy(e => e.path)
                    .ToList()
                : new());

            SharedPack? existing = null;
            if (meta.SharedPackId != null && _admin != null)
                existing = await new PackStore(_admin.Database).GetAsync(meta.SharedPackId);

            return new
            {
                name = existing?.Name ?? meta.Name,
                description = existing?.Description ?? meta.Description ?? "",
                mcVersion = meta.McVersion,
                loader = meta.Loader,
                modrinthCount = meta.Content.Count(c => File.Exists(Path.Combine(dir, c.File))),
                entries,
                existing = existing == null ? null : new { id = existing.Id.ToString(), revision = existing.Revision, hasCover = existing.CoverFileId != null }
            };
        }

        private object PickCover()
        {
            RequireAdmin();
            var files = _window.ShowOpenFile("Kapak resmi seçin", null, false,
                new[] { ("Resimler", new[] { "png", "jpg", "jpeg", "webp" }) });
            if (files is not { Length: > 0 }) return new { path = (string?)null, url = (string?)null };

            string path = ValidateCover(files[0]);
            return new { path, url = new Uri(path).AbsoluteUri };
        }

        private static string ValidateCover(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Kapak resmi bulunamadı.");
            if (!CoverExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                throw new InvalidOperationException("Desteklenen biçimler: PNG, JPG, WEBP.");
            if (new FileInfo(path).Length > 8L * 1024 * 1024)
                throw new InvalidOperationException("Kapak resmi 8 MB'tan büyük olamaz.");
            return path;
        }

        private async Task<object?> PublishPackAsync(JsonElement args)
        {
            var store = RequireWriteStore();
            var meta = RequireCustomInstance(args.Str("instanceId"));
            string dir = InstanceStore.PathOf(meta.Folder);

            string name = args.Str("name").Trim();
            if (name.Length is 0 or > 40) throw new InvalidOperationException("Paket adı 1-40 karakter olmalı.");
            string description = args.Str("description").Trim();
            if (description.Length > 600) throw new InvalidOperationException("Açıklama en fazla 600 karakter olabilir.");

            var includes = args.StrList("includes")
                .Where(p => !NeverShared.Contains(p) && Downloader.ResolveInside(dir, p) != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            string? cover = string.IsNullOrWhiteSpace(args.Str("coverPath")) ? null : ValidateCover(args.Str("coverPath"));

            // Var olan paketi güncelle (yalnızca bu instance'tan yayınlanmışsa)
            var pack = new SharedPack();
            if (args.Bool("update") && meta.SharedPackId != null && ObjectId.TryParse(meta.SharedPackId, out var existingId))
                pack.Id = existingId;

            Log($"{name} hazırlanıyor...", "accent");
            var (mods, fallbackFiles) = await ResolvePackModsAsync(meta, dir);

            string zipPath = Path.Combine(Path.GetTempPath(), $"vulu-share-{Guid.NewGuid():N}.zip");
            try
            {
                var tracked = TrackedFiles(meta);
                int fileCount = await Task.Run(() => BuildOverridesZip(zipPath, dir, includes, tracked, fallbackFiles));
                if (new FileInfo(zipPath).Length > MaxOverridesBytes)
                    throw new InvalidOperationException("Paket dosyaları 200 MB'ı aşıyor. Büyük klasörleri (ör. resourcepacks) çıkarmayı dene.");

                pack.Name = name;
                pack.Description = description;
                pack.Author = _login.Username;
                pack.McVersion = meta.McVersion;
                pack.Loader = meta.Loader;
                pack.LoaderVersion = meta.LoaderVersion;
                pack.RecommendedRamGb = meta.RamGb > 0 ? meta.RamGb : (meta.Loader == "vanilla" ? 4 : 8);
                pack.Mods = mods;
                pack.Includes = includes;

                Log($"{name} yükleniyor ({fileCount} dosya, {mods.Count} Modrinth içeriği)...", "replace");
                pack = await store.PublishAsync(pack, zipPath, cover);

                meta.SharedPackId = pack.Id.ToString();
                meta.SharedPackRevision = pack.Revision;
                meta.Description = description;
                foreach (var content in meta.Content) content.FromPack = true;
                InstanceStore.Save(meta);

                Log($"{name} vulu Packs'te yayınlandı (rev {pack.Revision}).", "ok");
                return new { id = pack.Id.ToString(), revision = pack.Revision };
            }
            finally
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
            }
        }

        /// <summary>
        /// Instance'taki Modrinth içeriklerinin sabit indirme adreslerini toplar. Modrinth'te artık bulunmayan
        /// (silinmiş) sürümlerin dosyaları bağlantı yerine pakete gömülür.
        /// </summary>
        private async Task<(List<PackMod> Mods, List<string> FallbackFiles)> ResolvePackModsAsync(InstanceMeta meta, string dir)
        {
            var present = meta.Content.Where(c => File.Exists(Path.Combine(dir, c.File))).ToList();
            var versions = present.Count == 0
                ? new List<MrVersion>()
                : await _modrinth.VersionsByIdsAsync(present.Select(c => c.VersionId));
            var byId = versions.ToDictionary(v => v.Id);

            var mods = new List<PackMod>();
            var fallback = new List<string>();
            foreach (var content in present)
            {
                var file = byId.TryGetValue(content.VersionId, out var version)
                    ? version.Files.FirstOrDefault(f => string.Equals(f.Filename, Path.GetFileName(content.File), StringComparison.OrdinalIgnoreCase))
                      ?? version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault()
                    : null;

                if (file == null) { fallback.Add(content.File); continue; }

                mods.Add(new PackMod
                {
                    ProjectId = content.ProjectId,
                    VersionId = content.VersionId,
                    Title = content.Title,
                    VersionNumber = content.VersionNumber,
                    Type = content.Type,
                    File = content.File,
                    IconUrl = content.IconUrl,
                    Url = file.Url,
                    Sha1 = file.Hashes?.Sha1,
                    Sha512 = file.Hashes?.Sha512,
                    Size = file.Size
                });
            }
            return (mods, fallback);
        }

        /// <summary>Seçilen klasör/dosyaları zip'ler. Modrinth'ten bağlantı olarak gidecek içerikler hariç tutulur.</summary>
        private static int BuildOverridesZip(string zipPath, string dir, List<string> includes, HashSet<string> tracked, List<string> fallbackFiles)
        {
            int count = 0;
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string fullPath)
            {
                string relative = Path.GetRelativePath(dir, fullPath).Replace('\\', '/');
                if (!added.Add(relative)) return;
                zip.CreateEntryFromFile(fullPath, relative, CompressionLevel.Optimal);
                count++;
            }

            foreach (var include in includes)
                foreach (var file in SharedFiles(dir, include, tracked))
                    Add(file);

            foreach (var relative in fallbackFiles)
            {
                string? path = Downloader.ResolveInside(dir, relative);
                if (path != null && File.Exists(path)) Add(path);
            }
            return count;
        }

        /// <summary>Bir üst düzey girdinin pakete girecek dosyaları.</summary>
        private static IEnumerable<string> SharedFiles(string dir, string entry, HashSet<string> tracked)
        {
            string? path = Downloader.ResolveInside(dir, entry);
            if (path == null) yield break;

            if (File.Exists(path)) { yield return path; yield break; }
            if (!Directory.Exists(path)) yield break;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(dir, file).Replace('\\', '/');
                if (tracked.Contains(relative) || file.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
                yield return file;
            }
        }

        private static HashSet<string> TrackedFiles(InstanceMeta meta) =>
            new(meta.Content.Select(c => c.File.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);

        private async Task<object?> DeletePackAsync(string id)
        {
            await RequireWriteStore().DeleteAsync(id);
            Log("Paket vulu Packs'ten kaldırıldı. Kuranların instance'ları yerinde kalır.", "muted");
            return new { success = true };
        }
    }
}
