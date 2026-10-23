using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Keşfet → CurseForge: arama, filtreler, sürümler, kurulum. Sonuçlar Modrinth'inkilerle aynı biçimde döner;
    /// arayüz iki kaynağı aynı ekranda gösterir. Dağıtıma kapalı dosyalar "manual" listesinde gelir.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private void RegisterCurseForgeCommands()
        {
            _bridge.On("cf.search", async args => await CurseForgeSearchAsync(args));
            _bridge.On("cf.filters", async args => await CurseForgeFiltersAsync(args.Str("type", "mod")));
            _bridge.On("cf.versions", async args => await CurseForgeVersionsAsync(args.Str("projectId"), args.Str("instanceId")));
            _bridge.On("cf.install", async args =>
                await CurseForgeInstallAsync(RequireGameDevice(args.Str("instanceId")), args.Str("projectId"), args.Str("versionId")));
            _bridge.On("cf.installModpack", async args =>
                await CurseForgeInstallModpackAsync(RequireGameDevice(args.Str("projectId")), args.Str("versionId")));
            _bridge.On("cf.openPage", args =>
            {
                string url = args.Str("url");
                // Yalnızca CurseForge'un kendi sayfaları açılır
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                    || !(uri.Host == "www.curseforge.com" || uri.Host == "curseforge.com"))
                    throw new InvalidOperationException("Geçersiz adres.");
                _host.OpenUrl(url);
                return Task.FromResult<object?>(new { opened = true });
            });
            _bridge.On("cf.manualPlace", async args => await PlaceManualAsync(args.Str("instanceId")));
            _bridge.On("mods.backupFiles", async args =>
            {
                var instance = RequireCustomInstance(args.Str("instanceId"));
                if (_game.IsRunning) throw new InvalidOperationException("Oyun çalışırken dosyalar taşınamaz.");
                var moved = await Task.Run(() => ManualFiles.MoveToBackup(InstanceStore.PathOf(instance.Folder), args.StrList("files")));
                // Taşınanlar artık kurulu sayılmaz
                if (instance.Content.RemoveAll(c => moved.Contains(c.File, StringComparer.OrdinalIgnoreCase)) > 0) InstanceStore.Save(instance);
                if (moved.Count > 0) Log($"{instance.Name}: {moved.Count} dosya {ManualFiles.BackupFolder} klasörüne taşındı.", "ok");
                return new { moved };
            });
        }

        /// <summary>Elle indirilen dosyaları İndirilenler klasöründe bulup doğrular ve instance'taki yerlerine koyar.</summary>
        private async Task<object?> PlaceManualAsync(string instanceId)
        {
            var instance = RequireCustomInstance(instanceId);
            // Android'deki DownloadsDirectory uygulamanın kendi klasörü; orada kullanıcının indirdikleri yok
            string? downloads = OperatingSystem.IsWindows() && Directory.Exists(_host.DownloadsDirectory) ? _host.DownloadsDirectory : null;
            var results = await Task.Run(() => ManualFiles.Place(instance, downloads));
            int placed = results.Count(r => r.Status == "placed");
            if (placed > 0) Log($"{instance.Name}: elle indirilen {placed} dosya yerine kondu.", "ok");
            return new
            {
                downloads,
                results = results.Select(r => new { fileId = r.File.FileId, status = r.Status, problem = r.Problem }),
                pending = ManualList(instance.PendingManual),
            };
        }

        private static int RequireCfId(string value, string what) =>
            int.TryParse(value, out var id) && id > 0 ? id : throw new InvalidOperationException($"Geçersiz {what}.");

        private static string ContentType(string type) => type is "mod" or "modpack" or "resourcepack" or "shader" ? type : "mod";

        private async Task<object?> CurseForgeSearchAsync(JsonElement args)
        {
            RequireLogin();
            string type = ContentType(args.Str("type", "mod"));

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

            // Arayüz kategori adlarını gönderir; CurseForge kimlikleriyle arar
            var categories = await _curseforge.CategoriesAsync(type);
            var wanted = args.StrList("categories").ToHashSet();
            var categoryIds = categories.Where(c => wanted.Contains(c.Name)).Select(c => c.Id).Distinct();

            var (hits, total) = await _curseforge.SearchAsync(args.Str("query"), type, mcVersion, loader, args.Int("offset"),
                args.Str("sort"), categoryIds);

            var installed = instance?.Content.Where(c => c.Source == CurseForgeService.Source).Select(c => c.ProjectId).ToHashSet()
                ?? new HashSet<string>();
            return new
            {
                total,
                offset = args.Int("offset"),
                hits = hits.Select(m => new
                {
                    id = m.Id.ToString(),
                    slug = m.Slug,
                    type = CurseForgeService.TypeOf(m.ClassId) ?? type,
                    title = m.Name,
                    description = m.Summary,
                    author = m.Authors.FirstOrDefault()?.Name ?? "",
                    iconUrl = m.Logo?.ThumbnailUrl,
                    downloads = m.DownloadCount,
                    follows = m.ThumbsUpCount,
                    updated = m.DateModified?.ToString("o"),
                    categories = m.Categories.Select(c => c.Name).Take(4),
                    loaders = m.LatestFilesIndexes.Select(f => f.ModLoader switch { 1 => "forge", 4 => "fabric", 5 => "quilt", 6 => "neoforge", _ => null })
                        .Where(l => l != null).Distinct(),
                    gameVersions = m.LatestFilesIndexes.Select(f => f.GameVersion).Where(v => ReleaseVersion.IsMatch(v)).Distinct()
                        .OrderBy(v => Version.TryParse(v, out var x) ? x : new Version(0, 0)).TakeLast(30),
                    installed = installed.Contains(m.Id.ToString()),
                    pageUrl = CurseForgeService.PageUrl(m),
                    latestVersion = m.MainFileId > 0 ? m.MainFileId.ToString() : null,   // modpack özeti (mod sayısı) için
                    // Yapımcı başka uygulamalardan indirmeye izin vermiyorsa arayüz önceden haber verir
                    manualOnly = m.AllowModDistribution == false,
                }),
            };
        }

        private async Task<object?> CurseForgeFiltersAsync(string type)
        {
            RequireLogin();
            var categories = await _curseforge.CategoriesAsync(ContentType(type));
            return new
            {
                categories = new[] { new { header = "categories", names = categories.Select(c => c.Name).Distinct().OrderBy(n => n) } },
                sorts = CurseForgeService.SortOptions,
                minecraftVersions = await LoaderCatalog.MinecraftVersionsAsync(),
                loaders = PackCatalog.Loaders.Where(l => l != "vanilla"),
            };
        }

        /// <summary>Projenin kurulabilir dosyaları; instance verildiyse yalnızca onunla uyumlu olanlar.</summary>
        private async Task<object?> CurseForgeVersionsAsync(string projectId, string instanceId)
        {
            RequireLogin();
            int modId = RequireCfId(projectId, "proje");
            List<CfFile> files;
            if (instanceId.Length > 0)
            {
                var instance = RequireCustomInstance(instanceId);
                var mod = await _curseforge.ModAsync(modId);
                files = await _curseforge.CompatibleFilesAsync(modId, instance.McVersion, instance.Loader, mod.ClassId == 6);
            }
            else files = await _curseforge.FilesAsync(modId);

            return files.Take(60).Select(f => new
            {
                id = f.Id.ToString(),
                name = f.DisplayName,
                number = f.FileName,
                type = CurseForgeService.ReleaseTypeName(f.ReleaseType),
                published = f.FileDate.ToString("o"),
                gameVersions = CurseForgeService.GameVersionsOf(f),
                loaders = CurseForgeService.LoadersOf(f),
                manualOnly = string.IsNullOrEmpty(f.DownloadUrl),
            });
        }

        private static object ManualList(IEnumerable<ManualDownload> manual) =>
            manual.Select(m => new { title = m.Title, url = m.Url, folder = m.Folder, fileName = m.FileName, fileId = m.FileId, iconUrl = m.IconUrl }).ToList();

        private async Task<object?> CurseForgeInstallAsync(string instanceId, string projectId, string versionId)
        {
            var instance = RequireCustomInstance(instanceId);
            try
            {
                int? fileId = string.IsNullOrEmpty(versionId) ? null : RequireCfId(versionId, "sürüm");
                var (installed, manual) = await _curseforge.InstallAsync(instance, RequireCfId(projectId, "proje"), fileId);
                if (installed.Count > 0) Log($"{instance.Name}: {string.Join(", ", installed)} kuruldu.", "ok");
                return new { success = true, error = (string?)null, installed, manual = ManualList(manual) };
            }
            catch (Exception ex)
            {
                try { InstanceStore.Save(instance); } catch { }
                Log(ex.Message, "error");
                return new { success = false, error = ex.Message, installed = new List<string>(), manual = ManualList(Array.Empty<ManualDownload>()) };
            }
        }

        private async Task<object?> CurseForgeInstallModpackAsync(string projectId, string versionId)
        {
            RequireLogin();
            try
            {
                int? fileId = string.IsNullOrEmpty(versionId) ? null : RequireCfId(versionId, "sürüm");
                var (instance, manual) = await _curseforge.InstallModpackAsync(RequireCfId(projectId, "proje"), fileId);
                await UseModpackIconAsLogoAsync(instance);
                return new { success = true, error = (string?)null, id = InstancePrefix + instance.Folder, name = instance.Name, manual = ManualList(manual) };
            }
            catch (Exception ex)
            {
                Log($"Modpack kurulamadı: {ex.Message}", "error");
                return new { success = false, error = ex.Message, id = (string?)null, name = (string?)null, manual = ManualList(Array.Empty<ManualDownload>()) };
            }
        }
    }
}
