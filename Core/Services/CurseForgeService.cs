using System.IO.Compression;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// CurseForge istemcisi: arama, instance'a içerik kurma ve CurseForge modpack'inden instance oluşturma.
    /// CurseForge API anahtarı launcher'da durmaz; istekler vulu-api'nin /curseforge vekilinden geçer.
    /// Yapımcısı başka uygulamalardan indirmeye kapatmış dosyalarda CurseForge indirme adresi vermez: bunlar
    /// <see cref="ManualDownload"/> olarak döner, kullanıcı CurseForge sitesinden indirip klasöre koyar.
    /// </summary>
    internal sealed class CurseForgeService
    {
        public const string Source = "curseforge";
        private const int PageSize = 20;
        private const int RelationRequired = 3;

        private readonly ApiClient _api;
        private readonly Action<string, string> _log;
        private readonly Dictionary<string, List<CfCategory>> _categories = new();

        public CurseForgeService(ApiClient api, Action<string, string> log)
        {
            _api = api;
            _log = log;
        }

        // ── Türler ve yükleyiciler ────────────────────────────────────────
        public static int ClassIdOf(string type) => type switch
        {
            "mod" => 6, "resourcepack" => 12, "shader" => 6552, "modpack" => 4471, _ => 6,
        };

        public static string? TypeOf(int classId) => classId switch
        {
            6 => "mod", 12 => "resourcepack", 6552 => "shader", 4471 => "modpack", _ => null,
        };

        public static int? LoaderTypeOf(string? loader) => loader switch
        {
            "forge" => 1, "fabric" => 4, "quilt" => 5, "neoforge" => 6, _ => null,
        };

        private static readonly Dictionary<string, string> LoaderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Forge"] = "forge", ["Fabric"] = "fabric", ["Quilt"] = "quilt", ["NeoForge"] = "neoforge",
        };

        /// <summary>Dosyanın sürüm etiketlerindeki yükleyiciler ("Forge", "NeoForge"...).</summary>
        public static IEnumerable<string> LoadersOf(CfFile file) =>
            file.GameVersions.Where(LoaderNames.ContainsKey).Select(v => LoaderNames[v]).Distinct();

        public static IEnumerable<string> GameVersionsOf(CfFile file) =>
            file.GameVersions.Where(v => v.Length > 0 && char.IsDigit(v[0]) && v.Contains('.'));

        public static string ReleaseTypeName(int releaseType) => releaseType switch { 2 => "beta", 3 => "alpha", _ => "release" };

        public static string PageUrl(CfMod mod) => mod.Links?.WebsiteUrl is { Length: > 0 } url ? url
            : $"https://www.curseforge.com/minecraft/{ClassSlug(mod.ClassId)}/{mod.Slug}";

        public static string ClassSlug(int classId) => classId switch
        {
            12 => "texture-packs", 6552 => "shaders", 4471 => "modpacks", _ => "mc-mods",
        };

        // ── Arama ─────────────────────────────────────────────────────────
        public static readonly string[] SortOptions = { "relevance", "downloads", "follows", "newest", "updated" };

        /// <summary>Modrinth'teki sıralama adları CurseForge'un sıralama alanlarına çevrilir.</summary>
        private static int? SortField(string? sort, bool hasQuery) => sort switch
        {
            "downloads" => 6, "follows" => 2, "newest" => 11, "updated" => 3, "relevance" => null,
            // "Otomatik": popülerlik. CurseForge sıralama verilmeden aranınca kelimelerden birini tutan yüzlerce projeyi
            // anlamsız bir sırayla döndürüyor (ör. "all the mods 10" ilk sayfada çıkmıyordu)
            _ => 2,
        };

        public async Task<(List<CfMod> Hits, int Total)> SearchAsync(string query, string type, string? mcVersion, string? loader,
            int offset, string? sort, IEnumerable<int>? categoryIds, int pageSize = PageSize)
        {
            var q = new List<string> { $"classId={ClassIdOf(type)}", $"index={Math.Max(0, offset)}", $"pageSize={pageSize}" };
            if (!string.IsNullOrWhiteSpace(query)) q.Add("searchFilter=" + Uri.EscapeDataString(query.Trim()));
            if (!string.IsNullOrEmpty(mcVersion)) q.Add("gameVersion=" + Uri.EscapeDataString(mcVersion));
            if (type is "mod" or "modpack" && LoaderTypeOf(loader) is { } lt) q.Add($"modLoaderType={lt}");
            var ids = (categoryIds ?? Enumerable.Empty<int>()).Take(10).ToList();
            if (ids.Count > 0) q.Add("categoryIds=" + string.Join(',', ids));
            if (SortField(sort, !string.IsNullOrWhiteSpace(query)) is { } field) q.Add($"sortField={field}&sortOrder=desc");

            var result = await _api.GetAsync<CfResponse<List<CfMod>>>("/curseforge/search?" + string.Join('&', q));
            return (result.Data ?? new(), result.Pagination?.TotalCount ?? 0);
        }

        /// <summary>Türün kategorileri (CurseForge'da her tür kendi kategori ağacına sahiptir); bir kez alınır.</summary>
        public async Task<List<CfCategory>> CategoriesAsync(string type)
        {
            lock (_categories) if (_categories.TryGetValue(type, out var cached)) return cached;
            var list = (await _api.GetAsync<CfResponse<List<CfCategory>>>($"/curseforge/categories?classId={ClassIdOf(type)}")).Data ?? new();
            list = list.Where(c => !c.IsClass).ToList();
            lock (_categories) _categories[type] = list;
            return list;
        }

        /// <summary>
        /// Kısa adı (bağlantıdaki) tutan proje. Önce CurseForge'un kısa ad süzgeciyle, olmazsa kısa addan türeyen metinle
        /// aranır ve kısa adı aynı olan seçilir; bulunamazsa null.
        /// </summary>
        public async Task<CfMod?> BySlugAsync(string slug, string type)
        {
            try
            {
                var exact = await _api.GetAsync<CfResponse<List<CfMod>>>($"/curseforge/search?classId={ClassIdOf(type)}&slug={Uri.EscapeDataString(slug)}&pageSize=5");
                if (exact.Data?.FirstOrDefault(m => string.Equals(m.Slug, slug, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit;
            }
            catch { /* eski API kısa adı iletmeyebilir; metinle aranır */ }
            // Kısa addan türeyen metinle, en popülerler önce, 50 sonuç taranır
            var (hits, _) = await SearchAsync(slug.Replace('-', ' '), type, null, null, 0, "follows", null, pageSize: 50);
            return hits.FirstOrDefault(m => string.Equals(m.Slug, slug, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<CfMod> ModAsync(int modId) =>
            (await _api.GetAsync<CfResponse<CfMod>>($"/curseforge/mods/{modId}")).Data
                ?? throw new InvalidOperationException("CurseForge projesi bulunamadı.");

        /// <summary>Parmak izleri CurseForge'daki dosyalarla eşleşen projelerin kimlikleri (en fazla 1000'lik parçalarla).</summary>
        public async Task<HashSet<int>> MatchFingerprintsAsync(IEnumerable<uint> fingerprints)
        {
            var result = new HashSet<int>();
            foreach (var chunk in fingerprints.Distinct().Chunk(1000))
            {
                var response = await _api.PostAsync<CfResponse<CfFingerprintMatches>>("/curseforge/fingerprints", new { fingerprints = chunk });
                foreach (var match in response.Data?.ExactMatches ?? new()) result.Add(match.Id);
            }
            return result;
        }

        /// <summary>Projenin açıklaması (HTML).</summary>
        public async Task<string> DescriptionAsync(int modId) =>
            (await _api.GetAsync<CfResponse<string>>($"/curseforge/mods/{modId}/description")).Data ?? "";

        public async Task<List<CfMod>> ModsAsync(IEnumerable<int> modIds)
        {
            var result = new List<CfMod>();
            foreach (var chunk in modIds.Distinct().Chunk(1000))
                result.AddRange((await _api.PostAsync<CfResponse<List<CfMod>>>("/curseforge/mods", new { modIds = chunk })).Data ?? new());
            return result;
        }

        /// <summary>Projenin dosyaları (en yeniden eskiye); sürüm ve yükleyici verilirse yalnızca uyumlular.</summary>
        public async Task<List<CfFile>> FilesAsync(int modId, string? mcVersion = null, string? loader = null)
        {
            var q = new List<string> { "pageSize=50" };
            if (!string.IsNullOrEmpty(mcVersion)) q.Add("gameVersion=" + Uri.EscapeDataString(mcVersion));
            if (LoaderTypeOf(loader) is { } lt) q.Add($"modLoaderType={lt}");
            var files = (await _api.GetAsync<CfResponse<List<CfFile>>>($"/curseforge/mods/{modId}/files?{string.Join('&', q)}")).Data ?? new();
            return files.Where(f => f.IsAvailable).OrderByDescending(f => f.FileDate).ToList();
        }

        public async Task<List<CfFile>> FilesByIdsAsync(IEnumerable<int> fileIds)
        {
            var result = new List<CfFile>();
            foreach (var chunk in fileIds.Distinct().Chunk(1000))
                result.AddRange((await _api.PostAsync<CfResponse<List<CfFile>>>("/curseforge/files", new { fileIds = chunk })).Data ?? new());
            return result;
        }

        /// <summary>Quilt, Fabric modlarını da çalıştırır; onda önce Quilt, yoksa Fabric dosyası aranır.</summary>
        public async Task<List<CfFile>> CompatibleFilesAsync(int modId, string mcVersion, string loader, bool isMod)
        {
            if (!isMod) return await FilesAsync(modId, mcVersion);
            var files = await FilesAsync(modId, mcVersion, loader);
            if (files.Count == 0 && loader == "quilt") files = await FilesAsync(modId, mcVersion, "fabric");
            return files;
        }

        // ── Instance'a içerik kurma ───────────────────────────────────────
        /// <summary>
        /// Projeyi ve zorunlu bağımlılıklarını kurar. Kurulanların başlıkları ve elle indirilmesi gerekenler döner.
        /// Doğrudan istenen proje indirilemiyorsa (yapımcı izin vermiyorsa) da elle indirilecekler listesinde döner.
        /// </summary>
        public async Task<(List<string> Installed, List<ManualDownload> Manual)> InstallAsync(InstanceMeta instance, int modId, int? fileId = null)
        {
            var installed = new List<string>();
            var manual = new List<ManualDownload>();
            using var job = new DownloadJob($"{instance.Name} · içerik kuruluyor") { Phase = "İndiriliyor" };
            try
            {
                await InstallRecursiveAsync(instance, modId, new HashSet<int>(), installed, manual, isDependency: false, fileId);
            }
            catch (Exception ex)
            {
                job.Fail(ex.Message);
                throw;
            }
            // Elle indirilecekler yerine konana kadar instance'ta kalır; aynı projenin eski bekleyeni yenisiyle değişir
            foreach (var m in manual)
            {
                instance.PendingManual.RemoveAll(p => p.ProjectId == m.ProjectId);
                instance.PendingManual.Add(m);
            }
            InstanceStore.Save(instance);
            job.Complete(installed.Count == 0 ? "Elle indirilmesi gerekiyor" : installed.Count == 1 ? $"{installed[0]} kuruldu" : $"{installed.Count} içerik kuruldu");
            return (installed, manual);
        }

        private async Task InstallRecursiveAsync(InstanceMeta instance, int modId, HashSet<int> visited,
            List<string> installed, List<ManualDownload> manual, bool isDependency, int? fileId = null)
        {
            if (!visited.Add(modId)) return;
            string id = modId.ToString();
            if (isDependency && instance.Content.Any(c => c.Source == Source && c.ProjectId == id)) return;

            var mod = await ModAsync(modId);
            string? type = TypeOf(mod.ClassId);
            string? folder = type == null ? null : ModrinthService.FolderFor(type);
            if (folder == null)
            {
                if (isDependency) return;
                throw new InvalidOperationException($"{mod.Name}: bu içerik türü instance'a kurulamıyor.");
            }
            bool isMod = type == "mod";
            if (isMod && instance.Loader == "vanilla")
                throw new InvalidOperationException("Vanilla instance'a mod kurulamaz; mod yükleyicili bir instance seçin.");

            var files = await CompatibleFilesAsync(mod.Id, instance.McVersion, instance.Loader, isMod);
            var file = fileId is { } wanted
                ? files.FirstOrDefault(f => f.Id == wanted)
                : files.FirstOrDefault(f => f.ReleaseType == 1) ?? files.FirstOrDefault();
            if (file == null)
            {
                string target = isMod ? $"{instance.McVersion} / {instance.Loader}" : instance.McVersion;
                if (isDependency) { _log($"{mod.Name} {target} için yok, atlandı.", "muted"); return; }
                throw new InvalidOperationException($"{mod.Name}: {target} için uyumlu sürüm yok.");
            }

            var newest = files.FirstOrDefault();
            if (fileId == null && newest != null && newest.Id != file.Id && newest.ReleaseType != 1 && newest.FileDate > file.FileDate)
                _log($"Not: {mod.Name} için daha yeni bir {ReleaseTypeName(newest.ReleaseType)} sürümü var ({newest.DisplayName}). Oyun açılmazsa sürüm seçiciden onu deneyebilirsin.", "muted");

            string instanceDir = InstanceStore.PathOf(instance.Folder);
            string relative = $"{folder}/{Path.GetFileName(file.FileName)}";
            string path = Downloader.ResolveInside(instanceDir, relative)
                ?? throw new InvalidOperationException($"{mod.Name}: geçersiz dosya adı.");

            if (string.IsNullOrEmpty(file.DownloadUrl))
            {
                // Yapımcı başka uygulamalardan indirmeye izin vermiyor: kullanıcı CurseForge'dan indirecek
                manual.Add(ManualDownload.Of(mod, file, folder));
                _log($"{mod.Name}: yapımcısı başka uygulamalardan indirilmesine izin vermiyor; CurseForge sitesinden indirilmeli.", "warn");
            }
            else
            {
                _log($"{mod.Name} indiriliyor ({file.DisplayName})...", "accent");
                string? sha1 = file.Hashes.FirstOrDefault(h => h.Algo == 1)?.Value;
                var item = DownloadJob.Current?.Add(mod.Name, file.FileLength);
                if (item != null) await item.DownloadAsync(file.DownloadUrl, path, sha1, null);
                else await Downloader.DownloadAsync(file.DownloadUrl, path, sha1: sha1);

                var previous = instance.Content.FirstOrDefault(c => c.Source == Source && c.ProjectId == id);
                if (previous != null)
                {
                    string? oldPath = Downloader.ResolveInside(instanceDir, previous.File);
                    if (oldPath != null && !string.Equals(oldPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath)) File.Delete(oldPath);
                    if (oldPath != null && File.Exists(oldPath + ".disabled")) File.Delete(oldPath + ".disabled");
                    instance.Content.Remove(previous);
                }
                instance.PendingManual.RemoveAll(p => p.ProjectId == id);
                instance.Content.Add(new InstalledContent
                {
                    Source = Source,
                    ProjectId = id,
                    VersionId = file.Id.ToString(),
                    Title = mod.Name,
                    VersionNumber = file.DisplayName,
                    Type = type!,
                    File = relative,
                    IconUrl = mod.Logo?.ThumbnailUrl,
                });
                installed.Add(mod.Name);
            }

            foreach (var dependency in file.Dependencies.Where(d => d.RelationType == RelationRequired))
                await InstallRecursiveAsync(instance, dependency.ModId, visited, installed, manual, isDependency: true);
        }

        // ── Eksik bağımlılıklar ───────────────────────────────────────────
        /// <summary>
        /// Eksik mod kimliklerinin CurseForge karşılığı. Önce onları isteyen modların CurseForge dosyalarındaki zorunlu
        /// bağımlılıklara bakılır ("kotlinforforge" → Kotlin for Forge), bulunamazsa CurseForge'da aranır. Yalnızca bu
        /// instance'ın sürümü ve yükleyicisiyle uyumlu dosyası olan projeler döner.
        /// </summary>
        public async Task<Dictionary<string, CfMod>> ResolveModIdsAsync(InstanceMeta instance, ModScan scan, IEnumerable<MissingDependency> missing)
        {
            var result = new Dictionary<string, CfMod>(StringComparer.OrdinalIgnoreCase);
            var wanted = missing.ToList();
            if (wanted.Count == 0) return result;

            async Task<bool> Compatible(CfMod mod) =>
                mod.ClassId == 6 && (await CompatibleFilesAsync(mod.Id, instance.McVersion, instance.Loader, isMod: true)).Count > 0;
            bool Matches(string id, CfMod mod) => ModrinthService.Normalize(mod.Slug) == ModrinthService.Normalize(id)
                || ModrinthService.Normalize(mod.Name) == ModrinthService.Normalize(id);

            // 1) İsteyen modların CurseForge dosyalarındaki zorunlu bağımlılıklar
            try
            {
                var requesterFiles = scan.Mods.Where(m => wanted.Any(w => w.RequiredBy.Contains(m.Name))).Select(m => "mods/" + m.File)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var fileIds = instance.Content.Where(c => c.Source == Source && requesterFiles.Contains(c.File) && int.TryParse(c.VersionId, out _))
                    .Select(c => int.Parse(c.VersionId)).ToList();
                var depIds = fileIds.Count == 0 ? new List<int>()
                    : (await FilesByIdsAsync(fileIds)).SelectMany(f => f.Dependencies).Where(d => d.RelationType == RelationRequired).Select(d => d.ModId).Distinct().ToList();
                var candidates = depIds.Count == 0 ? new List<CfMod>() : await ModsAsync(depIds);
                foreach (var dep in wanted)
                {
                    var match = candidates.FirstOrDefault(c => Matches(dep.Id, c)) ?? candidates.FirstOrDefault(c => ModrinthService.LooksLike(dep.Id, c.Slug, c.Name));
                    if (match != null && await Compatible(match)) result[dep.Id] = match;
                }
            }
            catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { }

            // 2) Arama: kimliğin kendisiyle ve alt çizgiler boşluk yapılarak
            foreach (var dep in wanted.Where(d => !result.ContainsKey(d.Id)))
            {
                try
                {
                    foreach (var query in ModrinthService.SearchQueries(dep.Id))
                    {
                        var (hits, _) = await SearchAsync(query, "mod", instance.McVersion, instance.Loader, 0, "relevance", null);
                        var hit = hits.FirstOrDefault(h => Matches(dep.Id, h)) ?? hits.Take(3).FirstOrDefault(h => ModrinthService.LooksLike(dep.Id, h.Slug, h.Name));
                        if (hit != null && await Compatible(hit)) { result[dep.Id] = hit; break; }
                    }
                }
                catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { }
            }
            return result;
        }

        // ── Modpack ───────────────────────────────────────────────────────
        /// <summary>
        /// CurseForge modpack'ini indirir ve ondan yeni bir instance oluşturur. İndirilemeyen (dağıtıma kapalı)
        /// dosyalar listelenir; instance yine de oluşturulur. Kurulum yarıda kalırsa klasör silinir.
        /// </summary>
        public async Task<(InstanceMeta Instance, List<ManualDownload> Manual)> InstallModpackAsync(int modId, int? fileId)
        {
            var mod = await ModAsync(modId);
            if (mod.ClassId != 4471) throw new InvalidOperationException($"{mod.Name} bir modpack değil.");
            var files = await FilesAsync(mod.Id);
            var packFile = (fileId is { } wanted ? files.FirstOrDefault(f => f.Id == wanted) : null)
                ?? files.FirstOrDefault(f => f.ReleaseType == 1) ?? files.FirstOrDefault()
                ?? throw new InvalidOperationException($"{mod.Name}: yayınlanmış sürüm yok.");
            if (string.IsNullOrEmpty(packFile.DownloadUrl))
                throw new InvalidOperationException($"{mod.Name}: yapımcısı modpack'in başka uygulamalardan indirilmesine izin vermiyor. CurseForge uygulamasıyla kurulabilir.");

            string zipPath = Path.Combine(Path.GetTempPath(), $"vulu-{Guid.NewGuid():N}.zip");
            using var job = new DownloadJob($"{mod.Name} kuruluyor") { Phase = "Modpack indiriliyor" };
            try
            {
                _log($"{mod.Name} {packFile.DisplayName} indiriliyor...", "accent");
                await job.Add(packFile.FileName, packFile.FileLength).DownloadAsync(packFile.DownloadUrl, zipPath,
                    packFile.Hashes.FirstOrDefault(h => h.Algo == 1)?.Value, null);
                return await CreateFromZipAsync(zipPath, mod.Name, mod.Logo?.ThumbnailUrl, $"{Source}:{mod.Id}", packFile.DisplayName, job);
            }
            catch (Exception ex) { job.Fail(ex.Message); throw; }
            finally
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
            }
        }

        /// <summary>Bilgisayardaki CurseForge modpack zip'inden yeni instance (içe aktarma). Adı manifestteki addan.</summary>
        public async Task<(InstanceMeta Instance, List<ManualDownload> Manual)> ImportZipAsync(string zipPath)
        {
            string name = Path.GetFileNameWithoutExtension(zipPath);
            using var job = new DownloadJob($"{name} kuruluyor") { Phase = "Paket okunuyor" };
            try { return await CreateFromZipAsync(zipPath, null, null, null, null, job); }
            catch (Exception ex) { job.Fail(ex.Message); throw; }
        }

        /// <summary>CurseForge modpack zip'inden instance kurar (manifest + overrides). Yarıda kalırsa klasör silinir.</summary>
        private async Task<(InstanceMeta Instance, List<ManualDownload> Manual)> CreateFromZipAsync(string zipPath, string? title, string? iconUrl, string? projectRef, string? versionName, DownloadJob job)
        {
            InstanceMeta? instance = null;
            var manual = new List<ManualDownload>();
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Modpack bozuk: manifest.json yok.");
                CfManifest manifest;
                using (var stream = manifestEntry.Open())
                    manifest = await JsonSerializer.DeserializeAsync<CfManifest>(stream, ManifestJson)
                        ?? throw new InvalidDataException("Modpack bozuk: manifest okunamadı.");
                string mcVersion = manifest.Minecraft?.Version ?? throw new InvalidDataException("Modpack hangi Minecraft sürümünü istediğini belirtmiyor.");
                var (loader, loaderVersion) = ReadLoader(manifest);

                string name = !string.IsNullOrWhiteSpace(title) ? title : !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name! : Path.GetFileNameWithoutExtension(zipPath);
                if (name.Length > 40) name = name[..40];
                job.Title = $"{name} kuruluyor";
                instance = InstanceStore.Create(name, mcVersion, loader, loaderVersion);
                instance.IconUrl = iconUrl;
                instance.ModpackProjectId = projectRef;
                instance.ModpackVersion = versionName ?? manifest.Version;
                InstanceStore.Save(instance);
                string instanceDir = InstanceStore.PathOf(instance.Folder);

                job.Phase = "Mod listesi alınıyor";
                var (downloads, found) = await ResolveManifestAsync(manifest);
                manual.AddRange(found);
                await DownloadManifestFilesAsync(downloads, instanceDir, job);
                foreach (var d in downloads) instance.Content.Add(ContentOf(d));
                instance.PendingManual = manual;
                InstanceStore.Save(instance);

                _log("Modpack ayarları açılıyor...", "replace");
                job.Phase = "Ayarlar yerleştiriliyor";
                ModrinthService.ExtractOverrides(zip, OverridesPrefix(manifest), instanceDir);
                instance.PackFiles = PackContent(downloads, manual, zip, manifest);   // sürüm değişince eşitlenecek dosyalar
                InstanceStore.Save(instance);

                _log(manual.Count == 0 ? $"{name} kuruldu." : $"{name} kuruldu; {manual.Count} dosyanın CurseForge'dan elle indirilmesi gerekiyor.", manual.Count == 0 ? "ok" : "warn");
                job.Complete($"{name} kuruldu");
                return (instance, manual);
            }
            catch
            {
                if (instance != null) { try { InstanceStore.Delete(instance.Folder); } catch { } }
                throw;
            }
        }

        private static string OverridesPrefix(CfManifest manifest) => (manifest.Overrides is { Length: > 0 } o ? o.TrimEnd('/') : "overrides") + "/";

        private static async Task<CfManifest> ReadManifestAsync(ZipArchive zip)
        {
            var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Modpack bozuk: manifest.json yok.");
            using var stream = entry.Open();
            return await JsonSerializer.DeserializeAsync<CfManifest>(stream, ManifestJson) ?? throw new InvalidDataException("Modpack bozuk: manifest okunamadı.");
        }

        /// <summary>Manifestteki dosyaların indirme adresleri ve gittikleri klasör (projelerin türünden); indirilemeyenler elle listesine.</summary>
        private async Task<(List<(CfFile File, CfMod? Mod, string Relative)> Downloads, List<ManualDownload> Manual)> ResolveManifestAsync(CfManifest manifest)
        {
            var wantedFiles = manifest.Files.Where(f => f.Required).ToList();
            var cfFiles = (await FilesByIdsAsync(wantedFiles.Select(f => f.FileID))).ToDictionary(f => f.Id);
            var projects = (await ModsAsync(wantedFiles.Select(f => f.ProjectID))).ToDictionary(m => m.Id);
            var downloads = new List<(CfFile File, CfMod? Mod, string Relative)>();
            var manual = new List<ManualDownload>();
            foreach (var entry in wantedFiles)
            {
                if (!cfFiles.TryGetValue(entry.FileID, out var file)) continue;
                projects.TryGetValue(entry.ProjectID, out var project);
                string folder = ModrinthService.FolderFor(TypeOf(project?.ClassId ?? 6) ?? "mod") ?? "mods";
                string relative = $"{folder}/{Path.GetFileName(file.FileName)}";
                if (string.IsNullOrEmpty(file.DownloadUrl)) manual.Add(ManualDownload.Of(project, file, folder));
                else downloads.Add((file, project, relative));
            }
            return (downloads, manual);
        }

        private async Task DownloadManifestFilesAsync(List<(CfFile File, CfMod? Mod, string Relative)> downloads, string instanceDir, DownloadJob job)
        {
            job.Phase = "Modlar indiriliyor";
            var items = downloads.ToDictionary(d => d.File.Id, d => job.Add(d.Mod?.Name ?? d.File.DisplayName, d.File.FileLength));
            int done = 0;
            await Parallel.ForEachAsync(downloads, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = job.Token }, async (d, cancellation) =>
            {
                string path = Downloader.ResolveInside(instanceDir, d.Relative)
                    ?? throw new InvalidDataException($"Modpack geçersiz bir dosya yolu içeriyor: {d.Relative}");
                if (!IsCurseForgeCdn(d.File.DownloadUrl!)) throw new InvalidDataException($"Modpack izin verilmeyen bir adresten indirme istiyor: {d.Relative}");
                await items[d.File.Id].DownloadAsync(d.File.DownloadUrl!, path, d.File.Hashes.FirstOrDefault(h => h.Algo == 1)?.Value, null, cancellation);
                _log($"Modpack dosyaları indiriliyor... {Interlocked.Increment(ref done)}/{downloads.Count}", "replace");
            });
        }

        private static InstalledContent ContentOf((CfFile File, CfMod? Mod, string Relative) d) => new()
        {
            Source = Source, ProjectId = d.File.ModId.ToString(), VersionId = d.File.Id.ToString(),
            Title = d.Mod?.Name ?? d.File.DisplayName, VersionNumber = d.File.DisplayName,
            Type = TypeOf(d.Mod?.ClassId ?? 6) ?? "mod", File = d.Relative, IconUrl = d.Mod?.Logo?.ThumbnailUrl,
        };

        private static List<string> PackContent(List<(CfFile File, CfMod? Mod, string Relative)> downloads, List<ManualDownload> manual, ZipArchive zip, CfManifest manifest) =>
            downloads.Select(d => d.Relative).Concat(manual.Select(m => $"{m.Folder}/{m.FileName}"))
                .Concat(ModpackFiles.OverrideContent(zip, OverridesPrefix(manifest)))
                .Select(ModpackFiles.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private async Task<string> DownloadPackFileAsync(CfFile packFile, DownloadJob job)
        {
            if (string.IsNullOrEmpty(packFile.DownloadUrl))
                throw new InvalidOperationException("Yapımcısı bu sürümün başka uygulamalardan indirilmesine izin vermiyor.");
            string zipPath = Path.Combine(Path.GetTempPath(), $"vulu-{Guid.NewGuid():N}.zip");
            await job.Add(packFile.FileName, packFile.FileLength).DownloadAsync(packFile.DownloadUrl, zipPath, packFile.Hashes.FirstOrDefault(h => h.Algo == 1)?.Value, null);
            return zipPath;
        }

        // ── Sürüm değiştirme ──
        /// <summary>Modpack'in sürümleri (yeniden eskiye).</summary>
        public Task<List<CfFile>> PackVersionsAsync(int modId) => FilesAsync(modId);

        /// <summary>
        /// Kurulu modpack'i başka bir sürüme geçirir: eski paket dosyalarından yenisinde olmayanlar kaldırılır, yeni dosyalar
        /// indirilir, ayarlar (overrides) yeniden açılır; önce ayar klasörleri yedeklenir. Sonradan eklenen içerik ve dünyalar kalır.
        /// Elle indirilmesi gereken dosyalar döner.
        /// </summary>
        public async Task<List<ManualDownload>> SwitchAsync(InstanceMeta instance, int fileId)
        {
            if (instance.ModpackProjectId is not { } projectRef || !projectRef.StartsWith(Source + ":", StringComparison.Ordinal)
                || !int.TryParse(projectRef[(Source.Length + 1)..], out int modId))
                throw new InvalidOperationException("Bu paket bir CurseForge modpack'i değil.");
            var files = await FilesAsync(modId);
            var target = files.FirstOrDefault(f => f.Id == fileId) ?? throw new InvalidOperationException("Sürüm bulunamadı.");
            using var job = new DownloadJob($"{instance.Name} → {target.DisplayName}") { Phase = "Modpack indiriliyor" };
            var temp = new List<string>();
            try
            {
                List<string> oldFiles = instance.PackFiles ?? new List<string>();
                if (instance.PackFiles == null)
                {
                    var old = files.FirstOrDefault(f => f.DisplayName == instance.ModpackVersion)
                        ?? throw new InvalidOperationException("Kurulu sürüm CurseForge'da bulunamadı; eski dosyalar güvenle ayrılamıyor. Paketi yeniden kurmak daha güvenli.");
                    job.Phase = "Kurulu sürüm okunuyor";
                    string oldPath = await DownloadPackFileAsync(old, job);
                    temp.Add(oldPath);
                    using var oldZip = ZipFile.OpenRead(oldPath);
                    var oldManifest = await ReadManifestAsync(oldZip);
                    var (oldDownloads, oldManual) = await ResolveManifestAsync(oldManifest);
                    oldFiles = PackContent(oldDownloads, oldManual, oldZip, oldManifest);
                }

                job.Phase = "Modpack indiriliyor";
                string zipPath = await DownloadPackFileAsync(target, job);
                temp.Add(zipPath);
                using var zip = ZipFile.OpenRead(zipPath);
                var manifest = await ReadManifestAsync(zip);
                string mcVersion = manifest.Minecraft?.Version ?? throw new InvalidDataException("Modpack hangi Minecraft sürümünü istediğini belirtmiyor.");
                var (loader, loaderVersion) = ReadLoader(manifest);
                job.Phase = "Mod listesi alınıyor";
                var (downloads, manual) = await ResolveManifestAsync(manifest);
                var newFiles = PackContent(downloads, manual, zip, manifest);

                string dir = InstanceStore.PathOf(instance.Folder);
                job.Phase = "Ayarlar yedekleniyor";
                ModpackFiles.BackupConfigs(dir, instance.ModpackVersion);
                int removed = ModpackFiles.RemoveOld(dir, oldFiles, newFiles);
                _log($"{instance.Name}: eski sürümden {removed} dosya kaldırıldı.", "muted");

                await DownloadManifestFilesAsync(downloads, dir, job);
                job.Phase = "Ayarlar yerleştiriliyor";
                ModrinthService.ExtractOverrides(zip, OverridesPrefix(manifest), dir);

                // Kayıtlar: eski paketin içerik kayıtları yenileriyle değişir, sonradan eklenenler kalır
                var oldSet = oldFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
                instance.Content.RemoveAll(c => oldSet.Contains(ModpackFiles.Normalize(c.File)));
                foreach (var d in downloads) instance.Content.Add(ContentOf(d));
                instance.PendingManual = manual;
                instance.McVersion = mcVersion;
                instance.Loader = loader;
                instance.LoaderVersion = loaderVersion;
                instance.ModpackVersion = target.DisplayName;
                instance.PackFiles = newFiles;
                InstanceStore.Save(instance);
                _log($"{instance.Name} artık {target.DisplayName}.", "ok");
                job.Complete($"{instance.Name} {target.DisplayName} sürümünde");
                return manual;
            }
            catch (Exception ex) { job.Fail(ex.Message); throw; }
            finally { foreach (var t in temp) try { File.Delete(t); } catch { } }
        }

        /// <summary>"forge-47.2.0", "neoforge-20.4.190", "fabric-0.15.7", "quilt-0.20.0" biçiminden.</summary>
        private static (string Loader, string? Version) ReadLoader(CfManifest manifest)
        {
            var entry = manifest.Minecraft?.ModLoaders?.FirstOrDefault(l => l.Primary) ?? manifest.Minecraft?.ModLoaders?.FirstOrDefault();
            if (entry?.Id is not { Length: > 0 } id) return ("vanilla", null);
            int dash = id.IndexOf('-');
            if (dash <= 0) return ("vanilla", null);
            string loader = id[..dash].ToLowerInvariant(), version = id[(dash + 1)..];
            return loader is "forge" or "neoforge" or "fabric" or "quilt" ? (loader, version) : ("vanilla", null);
        }

        /// <summary>CurseForge dosyaları yalnızca kendi dağıtım sunucularından iner.</summary>
        public static bool IsCurseForgeCdn(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host.Equals("edge.forgecdn.net", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("mediafilez.forgecdn.net", StringComparison.OrdinalIgnoreCase));

        private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web);
    }

    /// <summary>
    /// Elle indirilmesi gereken dosya: CurseForge sayfası, instance'ta konacağı klasör ve doğrulama için SHA-1'i ve boyutu.
    /// Yerine konana kadar instance'ta saklanır (<see cref="InstanceMeta.PendingManual"/>).
    /// </summary>
    public sealed record ManualDownload(string Title, string Url, string Folder, string FileName,
        string? Sha1 = null, long Length = 0, string? ProjectId = null, string? FileId = null, string Type = "mod", string? IconUrl = null)
    {
        internal static ManualDownload Of(CfMod? mod, CfFile file, string folder) => new(
            mod?.Name ?? file.DisplayName,
            mod != null ? $"{CurseForgeService.PageUrl(mod)}/download/{file.Id}" : "https://www.curseforge.com/minecraft",
            folder, Path.GetFileName(file.FileName),
            file.Hashes.FirstOrDefault(h => h.Algo == 1)?.Value?.ToLowerInvariant(), file.FileLength,
            file.ModId.ToString(), file.Id.ToString(), CurseForgeService.TypeOf(mod?.ClassId ?? 6) ?? "mod", mod?.Logo?.ThumbnailUrl);
    }

    // ── CurseForge API modelleri ──────────────────────────────────────────
    internal sealed class CfResponse<T>
    {
        public T? Data { get; set; }
        public CfPagination? Pagination { get; set; }
    }

    internal sealed class CfPagination
    {
        public int Index { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
    }

    internal sealed class CfMod
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Summary { get; set; } = "";
        public long DownloadCount { get; set; }
        public long ThumbsUpCount { get; set; }
        public int ClassId { get; set; }
        public List<CfCategory> Categories { get; set; } = new();
        public List<CfAuthor> Authors { get; set; } = new();
        public CfLogo? Logo { get; set; }
        public DateTime? DateModified { get; set; }
        public List<CfFileIndex> LatestFilesIndexes { get; set; } = new();
        public CfLinks? Links { get; set; }
        public bool? AllowModDistribution { get; set; }
        public int MainFileId { get; set; }
        public List<CfScreenshot> Screenshots { get; set; } = new();
    }

    internal sealed class CfFingerprintMatches { public List<CfFingerprintMatch> ExactMatches { get; set; } = new(); }
    internal sealed class CfFingerprintMatch { public int Id { get; set; } }

    internal sealed class CfScreenshot
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string? ThumbnailUrl { get; set; }
    }

    internal sealed class CfCategory
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ClassId { get; set; }
        public int? ParentCategoryId { get; set; }
        public bool IsClass { get; set; }
    }

    internal sealed class CfAuthor { public string Name { get; set; } = ""; }
    internal sealed class CfLogo { public string? ThumbnailUrl { get; set; } public string? Url { get; set; } }
    internal sealed class CfLinks
    {
        public string? WebsiteUrl { get; set; }
        public string? WikiUrl { get; set; }
        public string? IssuesUrl { get; set; }
        public string? SourceUrl { get; set; }
    }

    internal sealed class CfFileIndex
    {
        public string GameVersion { get; set; } = "";
        public int FileId { get; set; }
        public int ReleaseType { get; set; }
        public int? ModLoader { get; set; }
    }

    internal sealed class CfFile
    {
        public int Id { get; set; }
        public int ModId { get; set; }
        public string DisplayName { get; set; } = "";
        public string FileName { get; set; } = "";
        public int ReleaseType { get; set; }
        public DateTime FileDate { get; set; }
        public string? DownloadUrl { get; set; }
        public List<string> GameVersions { get; set; } = new();
        public List<CfDependency> Dependencies { get; set; } = new();
        public List<CfHash> Hashes { get; set; } = new();
        public long FileLength { get; set; }
        public bool IsAvailable { get; set; } = true;
    }

    internal sealed class CfDependency { public int ModId { get; set; } public int RelationType { get; set; } }
    internal sealed class CfHash { public string Value { get; set; } = ""; public int Algo { get; set; } }

    // manifest.json (CurseForge modpack biçimi)
    internal sealed class CfManifest
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public CfManifestMinecraft? Minecraft { get; set; }
        public List<CfManifestFile> Files { get; set; } = new();
        public string? Overrides { get; set; }
    }

    internal sealed class CfManifestMinecraft
    {
        public string? Version { get; set; }
        public List<CfManifestLoader>? ModLoaders { get; set; }
    }

    internal sealed class CfManifestLoader { public string? Id { get; set; } public bool Primary { get; set; } }

    internal sealed class CfManifestFile
    {
        public int ProjectID { get; set; }
        public int FileID { get; set; }
        public bool Required { get; set; } = true;
    }
}
