using System.IO.Compression;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// vulu Packs: adminin kendi instance'larından yayınladığı paketler. Herkes listeler ve tek tıkla kurar;
    /// yalnızca admin işaretli hesaplar yayınlar ve siler (yetkiyi sunucu denetler).
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private PackStore Packs => new(_api);

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
            "fancymenu_data", "global_packs", "global_data", "openloader", "datapacks",
            "optionsshaders.txt" // OptiFine'da seçili shader (Iris/Oculus'unki config/ içinde)
        };

        private static readonly string[] CoverExtensions = { ".png", ".jpg", ".jpeg", ".webp" };
        private const long MaxOverridesBytes = 200L * 1024 * 1024;

        private void RegisterPackCommands()
        {
            _bridge.On("packs.list", async _ => await ListPacksAsync());
            _bridge.On("packs.install", async args => await InstallPackAsync(args.Str("id")));
            _bridge.On("packs.update", async args => await UpdatePackAsync(args.Str("id")));
            _bridge.On("packs.shareInfo", async args => await ShareInfoAsync(args.Str("instanceId")));
            _bridge.On("packs.pickCover", async _ => await PickCoverAsync());
            _bridge.On("packs.publish", async args => await PublishPackAsync(args));
            _bridge.On("packs.delete", async args => await DeletePackAsync(args.Str("id")));
            RegisterFeedbackCommands();
            RegisterRegistrationCommands();
        }

        // ── Liste ─────────────────────────────────────────────────────────
        private async Task<object?> ListPacksAsync()
        {
            RequireLogin();
            var packsTask = Packs.ListAsync();
            var countsTask = FeedbackCountsAsync();
            var packs = await packsTask;
            var feedback = await countsTask;
            var installed = InstanceStore.List()
                .Where(m => m.SharedPackId != null)
                .GroupBy(m => m.SharedPackId!)
                .ToDictionary(g => g.Key, g => g.First());

            return new
            {
                configured = true,
                isAdmin = _login.IsAdmin,
                feedbackEnabled = feedback != null,
                generalFeedback = feedback?.GetValueOrDefault("general") ?? 0,
                packs = packs.Select(p =>
                {
                    installed.TryGetValue(p.Id, out var meta);
                    return new
                    {
                        id = p.Id,
                        name = p.Name,
                        description = p.Description,
                        author = p.Author,
                        mcVersion = p.McVersion,
                        loader = p.Loader,
                        loaderVersion = p.LoaderVersion,
                        revision = p.Revision,
                        updatedAt = p.UpdatedAt.ToString("o"),
                        modCount = p.Mods.Count(m => m.Type == "mod"),
                        recommendedRamGb = p.RecommendedRamGb,
                        resourcePackCount = p.Mods.Count(m => m.Type == "resourcepack"),
                        shaderCount = p.Mods.Count(m => m.Type == "shader"),
                        sizeMb = Math.Round((p.OverridesSize + p.Mods.Sum(m => m.Size)) / 1024.0 / 1024.0, 1),
                        includes = p.Includes,
                        coverUrl = PackStore.CoverUrl(p),
                        openFeedback = feedback?.GetValueOrDefault(p.Id) ?? 0,
                        changelog = p.Changelog.OrderByDescending(c => c.Revision).Select(c => new
                        {
                            revision = c.Revision, at = c.At.ToString("o"), by = c.By, note = c.Note,
                            added = c.Added, removed = c.Removed,
                            updated = c.Updated.Select(u => new { title = u.Title, from = u.From, to = u.To }),
                            filesChanged = c.FilesChanged,
                        }),
                        installed = meta == null ? null : new { instanceId = InstancePrefix + meta.Folder, revision = meta.SharedPackRevision }
                    };
                })
            };
        }

        /// <summary>Kapağı instance'ın arka planı yapar; alınamazsa sessizce geçilir.</summary>
        private async Task UseCoverAsBackgroundAsync(PackStore store, SharedPack pack, InstanceMeta meta)
        {
            if (!pack.HasCover) return;
            string ext = CoverExtensions.Contains(pack.CoverExt) ? pack.CoverExt! : ".png";
            try
            {
                Directory.CreateDirectory(AppPaths.Backgrounds);
                string key = BackgroundKey(InstancePrefix + meta.Folder);
                DeleteBackgroundFiles(InstancePrefix + meta.Folder);
                await store.DownloadCoverAsync(pack, Path.Combine(AppPaths.Backgrounds, key + ext));
            }
            catch { /* kapak olmadan da kurulum tamamdır */ }
        }

        /// <summary>Paketin logosu instance'ın logosu olur (güncellemede de yenilenir); alınamazsa sessizce geçilir.</summary>
        private async Task<bool> UsePackLogoAsync(PackStore store, SharedPack pack, InstanceMeta meta)
        {
            if (!pack.HasLogo) return false;
            string ext = CoverExtensions.Contains(pack.LogoExt) ? pack.LogoExt! : ".png";
            string id = InstancePrefix + meta.Folder;
            string target = Path.Combine(AppPaths.Logos, BackgroundKey(id) + ext);
            // Geçici dosya logo klasörünün dışında: eski logolar "<kimlik>.*" deseniyle silinirken o da silinmesin
            string temp = Path.Combine(Path.GetTempPath(), $"vulu-logo-{Guid.NewGuid():N}{ext}");
            try
            {
                Directory.CreateDirectory(AppPaths.Logos);
                await store.DownloadLogoAsync(pack, temp);
                DeleteLogoFiles(id);
                File.Move(temp, target, overwrite: true);
                meta.PackLogoSynced = true;
                InstanceStore.Save(meta);
                return true;
            }
            catch { return false; /* logo olmadan da paket çalışır */ }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        /// <summary>
        /// Eski sürümlerdeki bir hata yüzünden paketten kurulan instance'lara logo inmiyordu. Girişten sonra bir kez,
        /// logosu hiç inmemiş paket instance'larına paketin logosu indirilir; ana ekran yenilenir.
        /// </summary>
        private async Task RepairPackLogosAsync()
        {
            try
            {
                var missing = InstanceStore.List().Where(m => m.SharedPackId != null && !m.PackLogoSynced && LogoFile(InstancePrefix + m.Folder) == null).ToList();
                if (missing.Count == 0) return;
                var store = Packs;
                bool changed = false;
                foreach (var meta in missing)
                {
                    var pack = await store.GetAsync(meta.SharedPackId!);
                    if (pack != null && await UsePackLogoAsync(store, pack, meta)) changed = true;
                }
                if (changed) _bridge.Emit("logosChanged");
            }
            catch { /* bir sonraki açılışta yeniden denenir */ }
        }

        // ── Kurulum ve güncelleme ─────────────────────────────────────────
        private async Task<object?> InstallPackAsync(string id)
        {
            RequireLogin();
            var store = Packs;
            var pack = await store.GetAsync(id) ?? throw new InvalidOperationException("Paket bulunamadı.");
            ValidatePackVersions(pack);

            if (InstanceStore.List().Any(m => m.SharedPackId == id))
                throw new InvalidOperationException($"{pack.Name} zaten kurulu.");

            var meta = InstanceStore.Create(pack.Name, pack.McVersion, pack.Loader, pack.LoaderVersion);
            using var job = new DownloadJob($"{pack.Name} kuruluyor");
            try
            {
                Log($"{pack.Name} kuruluyor...", "accent");
                await ApplyPackAsync(store, pack, meta, job);
                await UseCoverAsBackgroundAsync(store, pack, meta);
                await UsePackLogoAsync(store, pack, meta);

                Log($"{pack.Name} kuruldu.", "ok");
                job.Complete($"{pack.Name} kuruldu");
                return new { id = InstancePrefix + meta.Folder };
            }
            catch (Exception ex)
            {
                job.Fail(ex.Message);
                try { InstanceStore.Delete(meta.Folder); } catch { }
                throw;
            }
        }

        private async Task<object?> UpdatePackAsync(string id)
        {
            RequireLogin();
            if (_game.IsRunning) throw new InvalidOperationException("Oyun çalışırken paket güncellenemez.");

            var store = Packs;
            var pack = await store.GetAsync(id) ?? throw new InvalidOperationException("Paket bulunamadı.");
            ValidatePackVersions(pack);
            var meta = InstanceStore.List().FirstOrDefault(m => m.SharedPackId == id)
                ?? throw new InvalidOperationException($"{pack.Name} kurulu değil.");

            Log($"{pack.Name} güncelleniyor (rev {meta.SharedPackRevision} → {pack.Revision})...", "accent");
            // Paket config'leri ve modları değiştirebilir: önce dünyalar otomatik yedeklenir
            await Task.Run(() => AutoBackupWorlds(meta, "paket güncellemesi"));
            using var job = new DownloadJob($"{pack.Name} güncelleniyor");
            try
            {
                await ApplyPackAsync(store, pack, meta, job);
                await UsePackLogoAsync(store, pack, meta);
            }
            catch (Exception ex)
            {
                job.Fail(ex.Message);
                throw;
            }
            Log($"{pack.Name} güncellendi.", "ok");
            job.Complete($"{pack.Name} güncellendi · rev {pack.Revision}");
            return new { id = InstancePrefix + meta.Folder };
        }

        /// <summary>
        /// Paketin modlarını ve dosyalarını instance'a uygular. Güncellemede paketten gelip artık listede
        /// olmayan ya da sürümü değişen modlar kaldırılır; kullanıcının kendi eklediği modlara dokunulmaz.
        /// Paketteki config dosyaları instance'takilerin üzerine yazılır.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex PackVersion = new(@"^[A-Za-z0-9][A-Za-z0-9._+-]{0,39}$");

        /// <summary>Sürümler klasör ve dosya adlarına dönüşür; sunucudan gelse de beklenmedik karakterler reddedilir.</summary>
        private static void ValidatePackVersions(SharedPack pack)
        {
            if (!PackVersion.IsMatch(pack.McVersion) || !PackCatalog.Loaders.Contains(pack.Loader) ||
                (pack.Loader != "vanilla" && !PackVersion.IsMatch(pack.LoaderVersion ?? "")))
                throw new InvalidDataException($"{pack.Name}: paketin sürüm bilgisi geçersiz.");
        }

        private async Task ApplyPackAsync(PackStore store, SharedPack pack, InstanceMeta meta, DownloadJob job)
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

            // Panelde her dosya ayrı görünür; paket dosyaları (config, görevler) en sonda iner
            var items = missing.ToDictionary(m => m, m => job.Add(string.IsNullOrEmpty(m.Title) ? Path.GetFileName(m.File) : m.Title, m.Size));
            var overridesItem = job.Add("Paket dosyaları (config, görevler…)", pack.OverridesSize);
            job.Phase = missing.Count > 0 ? "Modlar indiriliyor" : "Paket dosyaları indiriliyor";

            int done = 0;
            await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (mod, cancel) =>
            {
                string path = Downloader.ResolveInside(dir, mod.File)
                    ?? throw new InvalidDataException($"Paket geçersiz bir dosya yolu içeriyor: {mod.File}");
                if (!Uri.TryCreate(mod.Url, UriKind.Absolute, out var uri) || uri.Host != "cdn.modrinth.com")
                    throw new InvalidDataException($"Paket izin verilmeyen bir adresten indirme istiyor: {mod.Title}");

                await items[mod].DownloadAsync(mod.Url, path, mod.Sha1, mod.Sha512, cancel);
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
            job.Phase = "Paket dosyaları indiriliyor";
            string zipPath = Path.Combine(Path.GetTempPath(), $"vulu-pack-{Guid.NewGuid():N}.zip");
            try
            {
                overridesItem.Start();
                try { await store.DownloadOverridesAsync(pack, zipPath, overridesItem); }
                catch (Exception ex) { overridesItem.Fail(ex.Message); throw; }
                overridesItem.Complete();

                job.Phase = "Dosyalar yerleştiriliyor";
                await Task.Run(() =>
                {
                    using var zip = ZipFile.OpenRead(zipPath);
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.FullName.EndsWith('/')) continue;
                        if (string.Equals(entry.FullName, "instance.json", StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(entry.FullName, PackOptions.FileName, StringComparison.Ordinal))
                        {
                            using var options = entry.Open();
                            PackOptions.Apply(dir, options);     // açık kaynak paketleri → options.txt
                            continue;
                        }
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
            meta.SharedPackId = pack.Id;
            meta.SharedPackRevision = pack.Revision;
            InstanceStore.Save(meta);
        }

        private static void DeleteContentFile(string dir, string relative)
        {
            string? path = Downloader.ResolveInside(dir, relative);
            if (path == null) return;
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + DisabledSuffix)) File.Delete(path + DisabledSuffix);   // kapatılmış hâli
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
                            note = ShareNote(e.Name)
                        };
                    })
                    .Where(e => e.files > 0)
                    .OrderBy(e => e.isDir ? 0 : 1).ThenBy(e => e.path)
                    .ToList()
                : new());

            SharedPack? existing = meta.SharedPackId == null ? null : await Packs.GetAsync(meta.SharedPackId);

            return new
            {
                name = existing?.Name ?? meta.Name,
                description = existing?.Description ?? meta.Description ?? "",
                mcVersion = meta.McVersion,
                loader = meta.Loader,
                modrinthCount = meta.Content.Count(c => File.Exists(Path.Combine(dir, c.File))),
                recommendedRamGb = existing?.RecommendedRamGb ?? DefaultRamGb(meta),
                ramOptionsGb = PackCatalog.RamOptionsGb,
                entries,
                hasLogo = LogoFile(InstancePrefix + meta.Folder) != null,
                existing = existing == null ? null : new { id = existing.Id, revision = existing.Revision, hasCover = existing.HasCover }
            };
        }

        /// <summary>Paylaşanın seçmediği durumda önerilen RAM: instance'ın ayarı ya da yükleyiciye göre varsayılan.</summary>
        private static int DefaultRamGb(InstanceMeta meta) => meta.RamGb > 0 ? meta.RamGb : (meta.Loader == "vanilla" ? 4 : 8);

        private static string? ShareNote(string entry) => entry.ToLowerInvariant() switch
        {
            "resourcepacks" => "Modrinth'te bulunanlar bağlantı olur · açık olanlar kuranlarda da açık gelir",
            "shaderpacks" => "Modrinth'te bulunanlar bağlantı olur · seçili shader config/ ile gider",
            "options.txt" => "Kişisel ayarlar; genelde paylaşılmaz",
            _ when ContentFolders.Contains(entry, StringComparer.OrdinalIgnoreCase) => "Modrinth'te bulunanlar bağlantı olur",
            _ => null
        };

        private async Task<object?> PickCoverAsync()
        {
            RequireAdmin();
            string? file = await _host.PickFileAsync("Kapak resmi seçin", "Resimler", new[] { "png", "jpg", "jpeg", "webp" });
            if (file == null) return new { path = (string?)null, url = (string?)null };

            string path = ValidateCover(file);
            return new { path, url = LocalFiles.Url(path) };
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
            RequireAdmin(); // asıl yetki denetimi sunucuda
            var store = Packs;
            var meta = RequireCustomInstance(args.Str("instanceId"));
            string dir = InstanceStore.PathOf(meta.Folder);

            string name = args.Str("name").Trim();
            if (name.Length is 0 or > 40) throw new InvalidOperationException("Paket adı 1-40 karakter olmalı.");
            string description = args.Str("description").Trim();
            if (description.Length > 600) throw new InvalidOperationException("Açıklama en fazla 600 karakter olabilir.");
            string changeNote = args.Str("changeNote").Trim();
            if (changeNote.Length > 1000) throw new InvalidOperationException("Güncelleme notu en fazla 1000 karakter olabilir.");

            var includes = args.StrList("includes")
                .Where(p => !NeverShared.Contains(p) && Downloader.ResolveInside(dir, p) != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            string? cover = string.IsNullOrWhiteSpace(args.Str("coverPath")) ? null : ValidateCover(args.Str("coverPath"));

            // Var olan paketi güncelle (yalnızca bu instance'tan yayınlanmışsa)
            var pack = new SharedPack();
            if (args.Bool("update") && meta.SharedPackId != null)
                pack.Id = meta.SharedPackId;

            Log($"{name} hazırlanıyor...", "accent");
            var (mods, fallbackFiles) = await ResolvePackModsAsync(meta, dir);
            var tracked = TrackedFiles(meta);
            mods.AddRange(await ResolveUntrackedByHashAsync(dir, includes, tracked));

            string zipPath = Path.Combine(Path.GetTempPath(), $"vulu-share-{Guid.NewGuid():N}.zip");
            try
            {
                // Kaynak paketleri paylaşılıyorsa hangilerinin açık olduğu da gider (kuranda da açık gelsin)
                var resourcePacks = includes.Contains("resourcepacks", StringComparer.OrdinalIgnoreCase) ? PackOptions.ReadResourcePacks(dir) : null;
                int fileCount = await Task.Run(() => BuildOverridesZip(zipPath, dir, includes, tracked, fallbackFiles, resourcePacks));
                if (new FileInfo(zipPath).Length > MaxOverridesBytes)
                    throw new InvalidOperationException("Paket dosyaları 200 MB'ı aşıyor. Büyük klasörleri (ör. resourcepacks) çıkarmayı dene.");

                pack.Name = name;
                pack.Description = description;
                pack.ChangeNote = changeNote;
                pack.Author = _login.Username;
                pack.McVersion = meta.McVersion;
                pack.Loader = meta.Loader;
                pack.LoaderVersion = meta.LoaderVersion;
                int ramGb = args.Int("ramGb");
                pack.RecommendedRamGb = PackCatalog.RamOptionsGb.Contains(ramGb) ? ramGb : DefaultRamGb(meta);
                pack.Mods = mods;
                pack.Includes = includes;

                Log($"{name} yükleniyor ({fileCount} dosya, {mods.Count} Modrinth içeriği)...", "replace");
                // Instance'a logo atandıysa paketle paylaşılır; logo seçilmediyse paketin logosu da kalkar
                string? logo = LogoFile(InstancePrefix + meta.Folder);
                if (logo != null && !CoverExtensions.Contains(Path.GetExtension(logo).ToLowerInvariant())) logo = null;
                pack = await store.PublishAsync(pack, zipPath, cover, logo);

                meta.SharedPackId = pack.Id;
                meta.SharedPackRevision = pack.Revision;
                meta.Description = description;
                foreach (var content in meta.Content) content.FromPack = true;
                InstanceStore.Save(meta);

                Log($"{name} vulu Packs'te yayınlandı (rev {pack.Revision}).", "ok");
                return new { id = pack.Id, revision = pack.Revision };
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
            var present = meta.Content
                .Where(c => !string.IsNullOrEmpty(c.VersionId) && File.Exists(Path.Combine(dir, c.File)))
                .ToList();
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

        /// <summary>
        /// Launcher'ın takip etmediği mod/kaynak paketi/shader dosyalarını (modpack'ten gelen ya da elle eklenen)
        /// hash'leriyle Modrinth'te arar. Bulunanlar dosya yerine bağlantı olarak pakete girer ve
        /// <paramref name="tracked"/>'a eklenir; böylece zip'e konmazlar. Modrinth'e ulaşılamazsa dosyalar zip'te kalır.
        /// </summary>
        private async Task<List<PackMod>> ResolveUntrackedByHashAsync(string dir, List<string> includes, HashSet<string> tracked)
        {
            var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mods"] = "mod", ["resourcepacks"] = "resourcepack", ["shaderpacks"] = "shader"
            };

            // Yalnızca klasörün kendisindeki dosyalar (alt klasörler mod değildir, ör. mods/.connector)
            var candidates = includes
                .Where(types.ContainsKey)
                .SelectMany(folder => SharedFiles(dir, folder, tracked)
                    .Select(file => (Folder: folder, Path: file, Relative: Path.GetRelativePath(dir, file).Replace('\\', '/'))))
                .Where(c => c.Relative.Count(ch => ch == '/') == 1)
                .ToList();
            if (candidates.Count == 0) return new();

            var hashes = await Task.Run(() => candidates.ToDictionary(
                c => c.Relative,
                c => Downloader.Sha1Of(c.Path)));

            Dictionary<string, MrVersion> found;
            try { found = await _modrinth.VersionsByHashesAsync(hashes.Values); }
            catch (Exception ex)
            {
                Log($"Modrinth'te arama yapılamadı, dosyalar pakete gömülecek: {ex.Message}", "warn");
                return new();
            }

            var mods = new List<PackMod>();
            foreach (var candidate in candidates)
            {
                string sha1 = hashes[candidate.Relative];
                if (!found.TryGetValue(sha1, out var version)) continue;
                var file = version.Files.FirstOrDefault(f => string.Equals(f.Hashes?.Sha1, sha1, StringComparison.OrdinalIgnoreCase));
                if (file == null || !file.Url.StartsWith("https://cdn.modrinth.com/", StringComparison.Ordinal)) continue;

                mods.Add(new PackMod
                {
                    ProjectId = version.ProjectId,
                    VersionId = version.Id,
                    Title = string.IsNullOrWhiteSpace(version.Name) ? Path.GetFileNameWithoutExtension(candidate.Path) : version.Name,
                    VersionNumber = version.VersionNumber,
                    Type = types[candidate.Folder],
                    File = candidate.Relative,
                    Url = file.Url,
                    Sha1 = file.Hashes?.Sha1,
                    Sha512 = file.Hashes?.Sha512,
                    Size = file.Size
                });
                tracked.Add(candidate.Relative);
            }

            if (mods.Count > 0) Log($"{mods.Count} dosya Modrinth'te bulundu, bağlantı olarak eklendi.", "muted");
            return mods;
        }

        /// <summary>Seçilen klasör/dosyaları zip'ler. Modrinth'ten bağlantı olarak gidecek içerikler hariç tutulur.</summary>
        private static int BuildOverridesZip(string zipPath, string dir, List<string> includes, HashSet<string> tracked, List<string> fallbackFiles,
            List<string>? resourcePacks)
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

            if (resourcePacks is { Count: > 0 })
            {
                using var stream = zip.CreateEntry(PackOptions.FileName).Open();
                stream.Write(PackOptions.Serialize(resourcePacks));
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
                // Kapatılmış modlar (.disabled) paylaşılmaz
                if (tracked.Contains(relative) || file.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                yield return file;
            }
        }

        /// <summary>Modrinth sürüm kimliği bilinen (bağlantı olarak paylaşılabilecek) içeriklerin dosyaları.</summary>
        private static HashSet<string> TrackedFiles(InstanceMeta meta) =>
            new(meta.Content.Where(c => !string.IsNullOrEmpty(c.VersionId)).Select(c => c.File.Replace('\\', '/')),
                StringComparer.OrdinalIgnoreCase);

        private async Task<object?> DeletePackAsync(string id)
        {
            RequireAdmin();
            await Packs.DeleteAsync(id);
            Log("Paket vulu Packs'ten kaldırıldı. Kuranların instance'ları yerinde kalır.", "muted");
            return new { success = true };
        }
    }
}
