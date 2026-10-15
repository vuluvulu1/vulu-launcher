using System.Diagnostics;
using System.Text.Json;

namespace VuluLauncher
{
    // Instance'lar, Modrinth ve ayarlarla ilgili komutlar.
    internal sealed partial class LauncherApp
    {
        private const string PackPrefix = "pack:";
        private const string InstancePrefix = "inst:";

        private static readonly string[] ContentFolders = { "mods", "resourcepacks", "shaderpacks" };

        private void RegisterInstanceCommands()
        {
            _bridge.On("instances.list", _ => Task.FromResult<object?>(ListInstances()));
            _bridge.On("instances.options", async _ => await InstanceOptionsAsync());
            _bridge.On("instances.loaderVersions", async args =>
                await LoaderCatalog.LoaderVersionsAsync(args.Str("loader"), args.Str("mcVersion")));
            _bridge.On("instances.create", args => Task.FromResult<object?>(CreateInstance(args)));
            _bridge.On("instances.update", args => Task.FromResult<object?>(UpdateInstance(args)));
            _bridge.On("instances.delete", args => Task.FromResult<object?>(DeleteInstance(args.Str("id"))));
            _bridge.On("instances.content", args => Task.FromResult<object?>(ListContent(args.Str("id"))));
            _bridge.On("instances.removeContent", args => Task.FromResult<object?>(RemoveContent(args.Str("id"), args.Str("file"))));

            _bridge.On("modrinth.search", async args => await ModrinthSearchAsync(args));
            _bridge.On("modrinth.filters", async args => await ModrinthFiltersAsync(args.Str("type", "mod")));
            _bridge.On("modrinth.versions", async args => await ModrinthVersionsAsync(args.Str("projectId"), args.Str("instanceId")));
            _bridge.On("modrinth.install", async args =>
                await ModrinthInstallAsync(args.Str("instanceId"), args.Str("projectId"), args.Str("versionId")));
            _bridge.On("modrinth.installModpack", async args =>
                await ModrinthInstallModpackAsync(args.Str("projectId"), args.Str("versionId")));
            _bridge.On("modrinth.openPage", args => Task.FromResult<object?>(OpenModrinthPage(args.Str("type"), args.Str("slug"))));

            _bridge.On("instances.pickBackground", args => Task.FromResult<object?>(PickBackground(args.Str("id"))));
            _bridge.On("instances.clearBackground", args => Task.FromResult<object?>(ClearBackground(args.Str("id"))));

            _bridge.On("settings.get", _ => Task.FromResult<object?>(GetSettings()));
            _bridge.On("settings.save", args => Task.FromResult<object?>(SaveSettings(args)));
            _bridge.On("settings.pickJava", _ => Task.FromResult<object?>(PickJava()));
            _bridge.On("settings.removeJava", _ => Task.FromResult<object?>(RemoveDownloadedJava()));
            _bridge.On("files.open", args => Task.FromResult<object?>(OpenFolder(args.Str("target"), args.Str("id"))));

            _bridge.On("auth.logout", _ => Task.FromResult<object?>(Logout()));
            _bridge.On("auth.forgetPassword", _ =>
            {
                CredentialManager.Clear();
                return Task.FromResult<object?>(new { success = true });
            });
        }

        // ════════════════════════════════════════════════════════════════
        // INSTANCE'LAR
        // ════════════════════════════════════════════════════════════════
        private object ListInstances()
        {
            RequireLogin();

            var packs = PackCatalog.All.Where(IsPackAllowed).Select(p =>
            {
                string? custom = BackgroundUrl(PackPrefix + p.Id);
                return (object)new
            {
                id = PackPrefix + p.Id,
                kind = "server",
                name = p.Name,
                description = p.Description,
                mcVersion = p.McVersion,
                loader = p.Loader,
                loaderVersion = p.LoaderVersion,
                recommendedRamGb = p.RecommendedRamGb,
                logo = p.Logo,
                // Kullanıcının seçtiği resim, paketin kendi arka planının yerine geçer
                background = custom ?? p.Background,
                backgroundStyle = custom != null ? "cover" : p.BackgroundStyle,
                hasCustomBackground = custom != null,
                thumbnail = p.Thumbnail,
                iconUrl = (string?)null
            };
            });

            var custom = InstanceStore.List().Select(m =>
            {
                string? background = BackgroundUrl(InstancePrefix + m.Folder);
                return (object)new
            {
                id = InstancePrefix + m.Folder,
                kind = "custom",
                name = m.Name,
                description = m.ModpackProjectId != null
                    ? $"Modrinth modpack · {m.ModpackVersion}"
                    : "Kişisel instance",
                mcVersion = m.McVersion,
                loader = m.Loader,
                loaderVersion = m.LoaderVersion,
                recommendedRamGb = m.Loader == "vanilla" ? 4 : 8,
                logo = (string?)null,
                background,
                backgroundStyle = "cover",
                hasCustomBackground = background != null,
                thumbnail = background,
                iconUrl = m.IconUrl
            };
            });

            return packs.Concat(custom);
        }

        /// <summary>Arayüzden gelen kimliği başlatılabilir hedefe çevirir; izinleri burada denetler.</summary>
        private (LaunchTarget Target, int RecommendedRamGb) ResolveTarget(string id)
        {
            if (id.StartsWith(PackPrefix, StringComparison.Ordinal))
            {
                var pack = PackCatalog.Find(id[PackPrefix.Length..])
                    ?? throw new InvalidOperationException("Lütfen bir sürüm seçin!");
                if (!IsPackAllowed(pack))
                    throw new InvalidOperationException($"{pack.Name} paketine erişim izniniz yok.");

                return (new LaunchTarget(pack.Name, pack.FolderName, pack.McVersion, pack.Loader, pack.LoaderVersion),
                    pack.RecommendedRamGb);
            }

            var meta = RequireCustomInstance(id);
            return (new LaunchTarget(meta.Name, meta.Folder, meta.McVersion, meta.Loader, meta.LoaderVersion),
                meta.Loader == "vanilla" ? 4 : 8);
        }

        private InstanceMeta RequireCustomInstance(string id)
        {
            RequireLogin();
            if (!id.StartsWith(InstancePrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Bu işlem yalnızca kendi oluşturduğunuz instance'larda yapılabilir.");
            return InstanceStore.Get(id[InstancePrefix.Length..]);
        }

        private async Task<object?> InstanceOptionsAsync()
        {
            RequireLogin();
            return new
            {
                minecraftVersions = await LoaderCatalog.MinecraftVersionsAsync(),
                loaders = PackCatalog.Loaders
            };
        }

        private object CreateInstance(JsonElement args)
        {
            RequireLogin();
            var meta = InstanceStore.Create(args.Str("name"), args.Str("mcVersion"), args.Str("loader"), args.Str("loaderVersion"));
            Log($"{meta.Name} oluşturuldu ({meta.McVersion}, {meta.Loader}). İlk açılışta oyun dosyaları indirilecek.", "ok");
            return new { id = InstancePrefix + meta.Folder };
        }

        private object UpdateInstance(JsonElement args)
        {
            var meta = RequireCustomInstance(args.Str("id"));
            if (_game.IsRunning)
                throw new InvalidOperationException("Oyun çalışırken instance düzenlenemez.");

            bool versionChanged = meta.McVersion != args.Str("mcVersion").Trim() || meta.Loader != args.Str("loader");
            InstanceStore.Update(meta.Folder, args.Str("name"), args.Str("mcVersion"), args.Str("loader"), args.Str("loaderVersion"));

            if (versionChanged && meta.Content.Count > 0)
                Log("Sürüm ya da yükleyici değişti: kurulu modlar uyumsuz olabilir, İçerik bölümünden kontrol et.", "warn");
            return new { id = args.Str("id") };
        }

        private object DeleteInstance(string id)
        {
            var meta = RequireCustomInstance(id);
            if (_game.IsRunning)
                throw new InvalidOperationException("Oyun çalışırken instance silinemez.");

            InstanceStore.Delete(meta.Folder);
            DeleteBackgroundFiles(id);
            Log($"{meta.Name} silindi.", "muted");
            return new { success = true };
        }

        /// <summary>Instance'taki mod / kaynak paketi / shader dosyalarını, Modrinth bilgileriyle birleştirip listeler.</summary>
        private object ListContent(string id)
        {
            var meta = RequireCustomInstance(id);
            string instanceDir = InstanceStore.PathOf(meta.Folder);
            var tracked = meta.Content.ToDictionary(c => c.File, StringComparer.OrdinalIgnoreCase);
            var items = new List<object>();

            foreach (var folder in ContentFolders)
            {
                string dir = Path.Combine(instanceDir, folder);
                if (!Directory.Exists(dir)) continue;

                foreach (var path in Directory.GetFiles(dir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                {
                    string relative = $"{folder}/{Path.GetFileName(path)}";
                    tracked.TryGetValue(relative, out var info);
                    items.Add(new
                    {
                        file = relative,
                        type = folder == "mods" ? "mod" : folder == "resourcepacks" ? "resourcepack" : "shader",
                        title = info?.Title ?? Path.GetFileName(path),
                        version = info?.VersionNumber,
                        iconUrl = info?.IconUrl,
                        sizeMb = Math.Round(new FileInfo(path).Length / 1024.0 / 1024.0, 1)
                    });
                }
            }

            return new { name = meta.Name, mcVersion = meta.McVersion, loader = meta.Loader, items };
        }

        private object RemoveContent(string id, string file)
        {
            var meta = RequireCustomInstance(id);

            // Yalnızca içerik klasörlerindeki dosyalar silinebilir
            string normalized = file.Replace('\\', '/');
            string? path = Downloader.ResolveInside(InstanceStore.PathOf(meta.Folder), normalized);
            if (path == null || !ContentFolders.Contains(normalized.Split('/')[0]) || normalized.Split('/').Length != 2)
                throw new InvalidOperationException("Geçersiz dosya.");

            if (File.Exists(path)) File.Delete(path);

            meta.Content.RemoveAll(c => string.Equals(c.File, normalized, StringComparison.OrdinalIgnoreCase));
            InstanceStore.Save(meta);
            return new { success = true };
        }

        // ════════════════════════════════════════════════════════════════
        // MODRINTH
        // ════════════════════════════════════════════════════════════════
        private async Task<object?> ModrinthSearchAsync(JsonElement args)
        {
            RequireLogin();

            string type = args.Str("type", "mod");
            if (type is not ("mod" or "modpack" or "resourcepack" or "shader")) type = "mod";

            // Modpack dışındaki içerikler seçili instance'ın sürümüne ve yükleyicisine göre süzülür;
            // modpack aramasında sürüm ve yükleyici kullanıcının seçtiği filtredir.
            InstanceMeta? instance = null;
            string? mcVersion, loader;
            string instanceId = args.Str("instanceId");
            if (type != "modpack" && instanceId.Length > 0)
            {
                instance = RequireCustomInstance(instanceId);
                mcVersion = instance.McVersion;
                loader = instance.Loader;
            }
            else
            {
                mcVersion = NullIfEmpty(args.Str("mcVersion"));
                loader = PackCatalog.Loaders.Contains(args.Str("loader")) ? args.Str("loader") : null;
            }

            // Yalnızca Modrinth'in bu tür için tanımladığı kategoriler kabul edilir
            var known = (await _modrinth.CategoriesAsync()).Where(c => c.ProjectType == type).Select(c => c.Name).ToHashSet();
            var categories = args.StrList("categories").Where(known.Contains).Distinct().ToList();

            var result = await _modrinth.SearchAsync(args.Str("query"), type, mcVersion, loader, args.Int("offset"),
                args.Str("sort"), categories, args.Bool("openSource"));

            var installed = instance?.Content.Select(c => c.ProjectId).ToHashSet() ?? new HashSet<string>();

            return new
            {
                total = result.TotalHits,
                offset = result.Offset,
                hits = result.Hits.Select(h => new
                {
                    id = h.ProjectId,
                    slug = h.Slug,
                    type = h.ProjectType,
                    title = h.Title,
                    description = h.Description,
                    author = h.Author,
                    iconUrl = h.IconUrl,
                    downloads = h.Downloads,
                    follows = h.Follows,
                    updated = h.DateModified,
                    // Yükleyici adları da kategori olarak gelir; onlar ayrı gösterilir
                    categories = h.DisplayCategories.Where(c => !PackCatalog.Loaders.Contains(c)).Take(4),
                    loaders = h.Categories.Where(c => PackCatalog.Loaders.Contains(c) && c != "vanilla"),
                    installed = installed.Contains(h.ProjectId)
                })
            };
        }

        private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>Arama ekranındaki filtre seçenekleri: kategoriler, sıralama, sürümler, yükleyiciler.</summary>
        private async Task<object?> ModrinthFiltersAsync(string type)
        {
            RequireLogin();

            var categories = (await _modrinth.CategoriesAsync())
                .Where(c => c.ProjectType == type)
                .GroupBy(c => c.Header)
                .Select(g => new { header = g.Key, names = g.Select(c => c.Name).OrderBy(n => n) });

            return new
            {
                categories,
                sorts = ModrinthService.SortOptions,
                minecraftVersions = await LoaderCatalog.MinecraftVersionsAsync(),
                loaders = PackCatalog.Loaders.Where(l => l != "vanilla")
            };
        }

        /// <summary>Projenin kurulabilir sürümleri; instance verildiyse yalnızca onunla uyumlu olanlar.</summary>
        private async Task<object?> ModrinthVersionsAsync(string projectId, string instanceId)
        {
            RequireLogin();

            List<MrVersion> versions;
            if (instanceId.Length > 0)
            {
                var instance = RequireCustomInstance(instanceId);
                var project = await _modrinth.ProjectAsync(projectId);
                versions = await _modrinth.VersionsAsync(project.Id, instance.McVersion,
                    project.ProjectType == "mod"
                        ? (instance.Loader == "quilt" ? new[] { "quilt", "fabric" } : new[] { instance.Loader })
                        : null);
            }
            else
            {
                versions = await _modrinth.VersionsAsync(projectId);
            }

            return versions.Take(60).Select(v => new
            {
                id = v.Id,
                name = v.Name,
                number = v.VersionNumber,
                type = v.VersionType,
                published = v.DatePublished,
                gameVersions = v.GameVersions,
                loaders = v.Loaders
            });
        }

        private async Task<object?> ModrinthInstallAsync(string instanceId, string projectId, string versionId)
        {
            var instance = RequireCustomInstance(instanceId);
            try
            {
                var installed = await _modrinth.InstallAsync(instance, projectId, NullIfEmpty(versionId));
                Log($"{instance.Name}: {string.Join(", ", installed)} kuruldu.", "ok");
                return new { success = true, error = (string?)null, installed };
            }
            catch (Exception ex)
            {
                // Yarıda kalan kurulumda o ana kadar inenler kayıtlı kalsın
                try { InstanceStore.Save(instance); } catch { }
                Log(ex.Message, "error");
                return new { success = false, error = ex.Message, installed = new List<string>() };
            }
        }

        private async Task<object?> ModrinthInstallModpackAsync(string projectId, string versionId)
        {
            RequireLogin();
            try
            {
                var instance = await _modrinth.InstallModpackAsync(projectId, NullIfEmpty(versionId));
                return new { success = true, error = (string?)null, id = InstancePrefix + instance.Folder, name = instance.Name };
            }
            catch (Exception ex)
            {
                Log($"Modpack kurulamadı: {ex.Message}", "error");
                return new { success = false, error = ex.Message, id = (string?)null, name = (string?)null };
            }
        }

        /// <summary>Projenin Modrinth sayfasını varsayılan tarayıcıda açar.</summary>
        private object OpenModrinthPage(string type, string slug)
        {
            RequireLogin();
            if (type is not ("mod" or "modpack" or "resourcepack" or "shader") ||
                !System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[A-Za-z0-9_\-!@$()`.+,""]{1,64}$"))
                throw new InvalidOperationException("Geçersiz proje.");

            Process.Start(new ProcessStartInfo($"https://modrinth.com/{type}/{Uri.EscapeDataString(slug)}") { UseShellExecute = true });
            return new { opened = true };
        }

        // ════════════════════════════════════════════════════════════════
        // ARKA PLAN RESİMLERİ
        // ════════════════════════════════════════════════════════════════
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif" };

        /// <summary>"pack:vanilla" → "pack_vanilla": dosya adında kullanılabilir hale getirir.</summary>
        private static string BackgroundKey(string id) =>
            string.Concat(id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

        private static string? BackgroundFile(string id)
        {
            if (!Directory.Exists(AppPaths.Backgrounds)) return null;
            return Directory.GetFiles(AppPaths.Backgrounds, BackgroundKey(id) + ".*")
                .FirstOrDefault(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        }

        /// <summary>Arayüzün doğrudan gösterebileceği adres; resim değişince yenilensin diye sonuna zaman eklenir.</summary>
        private static string? BackgroundUrl(string id)
        {
            string? file = BackgroundFile(id);
            return file == null ? null : $"{new Uri(file).AbsoluteUri}?v={File.GetLastWriteTimeUtc(file).Ticks}";
        }

        private static void DeleteBackgroundFiles(string id)
        {
            if (!Directory.Exists(AppPaths.Backgrounds)) return;
            foreach (var file in Directory.GetFiles(AppPaths.Backgrounds, BackgroundKey(id) + ".*"))
                File.Delete(file);
        }

        private object PickBackground(string id)
        {
            _ = ResolveTarget(id); // kimlik geçerli ve kullanıcıya açık mı

            var files = _window.ShowOpenFile("Arka plan resmi seçin", null, false,
                new[] { ("Resimler", new[] { "png", "jpg", "jpeg", "webp", "gif" }) });
            if (files is not { Length: > 0 }) return new { changed = false };

            string source = files[0];
            string extension = Path.GetExtension(source).ToLowerInvariant();
            if (!ImageExtensions.Contains(extension))
                throw new InvalidOperationException("Desteklenen biçimler: PNG, JPG, WEBP, GIF.");
            if (new FileInfo(source).Length > 25L * 1024 * 1024)
                throw new InvalidOperationException("Resim 25 MB'tan büyük olamaz.");

            Directory.CreateDirectory(AppPaths.Backgrounds);
            DeleteBackgroundFiles(id);
            File.Copy(source, Path.Combine(AppPaths.Backgrounds, BackgroundKey(id) + extension), overwrite: true);
            return new { changed = true };
        }

        private object ClearBackground(string id)
        {
            _ = ResolveTarget(id);
            DeleteBackgroundFiles(id);
            return new { changed = true };
        }

        // ════════════════════════════════════════════════════════════════
        // AYARLAR
        // ════════════════════════════════════════════════════════════════
        private object GetSettings()
        {
            var (savedUsername, savedPassword) = CredentialManager.Load();
            return new
            {
                settings = AppSettings.Current,
                ramOptionsGb = PackCatalog.RamOptionsGb,
                installedJava = JavaManager.InstalledVersions().OrderBy(v => v),
                version = LauncherUpdateService.LocalVersion,
                gameFolder = AppPaths.Base,
                hasSavedPassword = !string.IsNullOrEmpty(savedPassword),
                savedUsername
            };
        }

        private object SaveSettings(JsonElement args)
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(args.GetRawText(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new AppSettings();

            if (settings.JavaMode == "custom" && !File.Exists(settings.CustomJavaPath.Trim().Trim('"')))
                throw new InvalidOperationException("Seçilen Java dosyası bulunamadı. Otomatik moda dönün ya da geçerli bir java.exe seçin.");

            AppSettings.Save(settings);
            return new { settings = AppSettings.Current };
        }

        private object PickJava()
        {
            var files = _window.ShowOpenFile("java.exe seçin", null, false,
                new[] { ("Java", new[] { "exe" }) });
            return new { path = files is { Length: > 0 } ? files[0] : null };
        }

        private object RemoveDownloadedJava()
        {
            if (_game.IsRunning)
                throw new InvalidOperationException("Oyun çalışırken Java silinemez.");

            int removed = 0;
            foreach (int major in JavaManager.InstalledVersions().ToList())
            {
                Directory.Delete(Path.Combine(JavaManager.JavaRoot, $"temurin-{major}"), recursive: true);
                removed++;
            }
            return new { removed };
        }

        private object OpenFolder(string target, string id)
        {
            RequireLogin();

            string path = target switch
            {
                "instances" => AppPaths.Instances,
                "instance" => Path.Combine(AppPaths.Instances, ResolveTarget(id).Target.Folder),
                _ => AppPaths.Base
            };

            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path } });
            return new { opened = true };
        }

        private object Logout()
        {
            if (_game.IsRunning)
                throw new InvalidOperationException("Oyun çalışırken çıkış yapılamaz.");

            _login = new LoginService();
            _admin = null;
            _updatePlan = new List<InstanceUpdatePlan>();
            return new { success = true };
        }
    }
}
