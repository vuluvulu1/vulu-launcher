using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VuluLauncher
{
    /// <summary>Modrinth API istemcisi: arama, instance'a içerik kurma ve modpack'ten instance oluşturma.</summary>
    internal sealed class ModrinthService
    {
        private const string Api = "https://api.modrinth.com/v2";
        private const int PageSize = 20;

        // .mrpack dosyalarının indirme yapabileceği adresler (Modrinth'in modpack biçimi bunlarla sınırlar)
        private static readonly string[] AllowedPackHosts =
            { "cdn.modrinth.com", "github.com", "raw.githubusercontent.com", "gitlab.com" };

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        private readonly Action<string, string> _log;

        public ModrinthService(Action<string, string> log)
        {
            _log = log;
        }

        /// <summary>İçerik türünün instance içinde kurulduğu klasör.</summary>
        public static string? FolderFor(string projectType) => projectType switch
        {
            "mod" => "mods",
            "resourcepack" => "resourcepacks",
            "shader" => "shaderpacks",
            _ => null
        };

        // ── Arama ─────────────────────────────────────────────────────────
        public static readonly string[] SortOptions = { "relevance", "downloads", "follows", "newest", "updated" };

        private static List<MrCategory>? _categories;

        /// <summary>Modrinth'in kategori etiketleri (tür başına); bir kez alınıp bellekte tutulur.</summary>
        public async Task<List<MrCategory>> CategoriesAsync()
        {
            return _categories ??= await GetAsync<List<MrCategory>>($"{Api}/tag/category");
        }

        /// <param name="sort">SortOptions'tan biri; boşsa arama metni varsa ilgiye, yoksa indirmeye göre.</param>
        /// <param name="categories">Hepsini birden taşıyan projeler gelir (VE).</param>
        public async Task<MrSearchResult> SearchAsync(string query, string projectType, string? mcVersion, string? loader,
            int offset, string? sort = null, IEnumerable<string>? categories = null, bool openSourceOnly = false)
        {
            var facets = new List<string[]> { new[] { $"project_type:{projectType}" } };
            if (!string.IsNullOrEmpty(mcVersion)) facets.Add(new[] { $"versions:{mcVersion}" });
            foreach (var category in categories ?? Enumerable.Empty<string>())
                facets.Add(new[] { $"categories:{category}" });
            if (openSourceOnly) facets.Add(new[] { "open_source:true" });

            // Yükleyici filtresi yalnızca modlar ve modpackler için anlamlıdır
            if (projectType is "mod" or "modpack" && !string.IsNullOrEmpty(loader) && loader != "vanilla")
                facets.Add(CompatibleLoaders(loader).Select(l => $"categories:{l}").ToArray());

            string index = SortOptions.Contains(sort)
                ? sort!
                : string.IsNullOrWhiteSpace(query) ? "downloads" : "relevance";

            string url = $"{Api}/search?limit={PageSize}&offset={Math.Max(0, offset)}" +
                         $"&index={index}" +
                         $"&query={Uri.EscapeDataString(query ?? "")}" +
                         $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";

            return await GetAsync<MrSearchResult>(url);
        }

        /// <summary>Ayrıntı sayfası için projenin tamamı: açıklama (markdown), galeri, bağlantılar.</summary>
        public Task<MrProjectDetail> ProjectDetailAsync(string idOrSlug) =>
            GetAsync<MrProjectDetail>($"{Api}/project/{Uri.EscapeDataString(idOrSlug)}");

        public Task<MrProject> ProjectAsync(string idOrSlug) =>
            GetAsync<MrProject>($"{Api}/project/{Uri.EscapeDataString(idOrSlug)}");

        public Task<List<MrVersion>> VersionsAsync(string projectId, string? mcVersion = null, IEnumerable<string>? loaders = null)
        {
            string url = $"{Api}/project/{Uri.EscapeDataString(projectId)}/version";
            var query = new List<string>();
            if (!string.IsNullOrEmpty(mcVersion))
                query.Add("game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { mcVersion })));
            if (loaders != null)
                query.Add("loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(loaders)));
            if (query.Count > 0) url += "?" + string.Join("&", query);

            return GetAsync<List<MrVersion>>(url);
        }

        /// <summary>Verilen sürüm kimliklerinin ayrıntıları (dosya adresi, hash). Modrinth tek istekte çoklu sorguya izin verir.</summary>
        public async Task<List<MrVersion>> VersionsByIdsAsync(IEnumerable<string> versionIds)
        {
            var result = new List<MrVersion>();
            foreach (var chunk in versionIds.Distinct().Chunk(100))
            {
                string ids = Uri.EscapeDataString(JsonSerializer.Serialize(chunk));
                result.AddRange(await GetAsync<List<MrVersion>>($"{Api}/versions?ids={ids}"));
            }
            return result;
        }

        /// <summary>
        /// Dosyaların SHA-1'lerinden Modrinth sürümlerini bulur (elle eklenmiş ya da modpack'ten gelmiş modlar için).
        /// Modrinth'te olmayan dosyalar sonuçta yer almaz.
        /// </summary>
        public async Task<Dictionary<string, MrVersion>> VersionsByHashesAsync(IEnumerable<string> sha1Hashes)
        {
            var result = new Dictionary<string, MrVersion>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in sha1Hashes.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(500))
            {
                using var response = await Downloader.Http.PostAsJsonAsync($"{Api}/version_files", new { hashes = chunk, algorithm = "sha1" });
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException(L.F("Modrinth isteği başarısız oldu ({0}).", (int)response.StatusCode));

                var found = await response.Content.ReadFromJsonAsync<Dictionary<string, MrVersion>>(Json);
                foreach (var (hash, version) in found ?? new()) result[hash] = version;
            }
            return result;
        }

        /// <summary>Quilt, Fabric modlarını da çalıştırır; diğerleri yalnızca kendi modlarını.</summary>
        public static string[] CompatibleLoadersOf(string loader) => CompatibleLoaders(loader);

        private static string[] CompatibleLoaders(string loader) =>
            loader == "quilt" ? new[] { "quilt", "fabric" } : new[] { loader };

        // ── Instance'a içerik kurma ───────────────────────────────────────
        /// <summary>
        /// Projeyi ve zorunlu bağımlılıklarını instance'a kurar. Kurulanların başlıklarını döner.
        /// </summary>
        /// <param name="versionId">Belirli bir sürüm isteniyorsa; boşsa en yeni uyumlu sürüm kurulur.</param>
        public async Task<List<string>> InstallAsync(InstanceMeta instance, string projectId, string? versionId = null)
        {
            var installed = new List<string>();
            using var job = new DownloadJob(L.F("{0} · içerik kuruluyor", instance.Name)) { Phase = "İndiriliyor" };
            try
            {
                await InstallRecursiveAsync(instance, projectId, new HashSet<string>(), installed, isDependency: false, versionId);
            }
            catch (Exception ex)
            {
                job.Fail(ex.Message);
                throw;
            }
            InstanceStore.Save(instance);
            job.Complete(installed.Count == 1 ? $"{installed[0]} kuruldu" : L.F("{0} içerik kuruldu", installed.Count));
            return installed;
        }

        private async Task InstallRecursiveAsync(InstanceMeta instance, string projectId, HashSet<string> visited,
            List<string> installed, bool isDependency, string? versionId = null)
        {
            if (!visited.Add(projectId)) return;

            // Bağımlılık zaten kuruluysa dokunma; kullanıcının doğrudan istediği proje ise güncellenir
            if (isDependency && instance.Content.Any(c => c.ProjectId == projectId)) return;

            var project = await ProjectAsync(projectId);
            string? folder = FolderFor(project.ProjectType);
            if (folder == null && isDependency) return;
            if (folder == null)
                throw new InvalidOperationException(L.F("{0}: bu içerik türü ({1}) instance'a kurulamıyor.", project.Title, project.ProjectType));

            bool isMod = project.ProjectType == "mod";
            if (isMod && instance.Loader == "vanilla")
                throw new InvalidOperationException("Vanilla instance'a mod kurulamaz; mod yükleyicili bir instance seçin.");

            var versions = await VersionsAsync(project.Id, instance.McVersion,
                isMod ? CompatibleLoaders(instance.Loader) : null);

            // Kullanıcı sürüm seçtiyse o; yalnızca instance ile uyumlu sürümler arasından
            var version = string.IsNullOrEmpty(versionId)
                ? versions.FirstOrDefault(v => v.VersionType == "release") ?? versions.FirstOrDefault()
                : versions.FirstOrDefault(v => v.Id == versionId);
            if (version == null)
            {
                string target = isMod ? $"{instance.McVersion} / {instance.Loader}" : instance.McVersion;
                // Birden çok yükleyiciye uyan jar'lar (ör. Fabric + NeoForge) her yükleyicinin bağımlılığını birlikte
                // bildirir; Fabric API gibi başka yükleyiciye ait olanların burada sürümü yoktur ve gerekmez.
                if (isDependency)
                {
                    _log(L.F("{0} {1} için yok, atlandı (bu yükleyicide gerekmiyor).", project.Title, target), "muted");
                    return;
                }
                throw new InvalidOperationException(L.F("{0}: {1} için uyumlu sürüm yok.", project.Title, target));
            }

            var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault()
                ?? throw new InvalidOperationException(L.F("{0}: indirilecek dosya yok.", project.Title));

            // Kararlı sürüm seçildi ama daha yeni bir beta/alpha varsa haber verilir: yeni yükleyici sürümlerinde
            // düzeltme çoğu zaman yalnızca betada olur (ör. Sodium 0.9.2 ↔ NeoForge 26.3.0.65+)
            var newest = versions.OrderByDescending(v => v.DatePublished).FirstOrDefault();
            if (string.IsNullOrEmpty(versionId) && newest != null && newest.Id != version.Id && newest.VersionType != "release"
                && string.CompareOrdinal(newest.DatePublished, version.DatePublished) > 0)
                _log(L.F("Not: {0} için daha yeni bir {1} sürümü var ({2}). Oyun açılmazsa sürüm seçiciden onu deneyebilirsin.", project.Title, newest.VersionType, newest.VersionNumber), "muted");

            string instanceDir = InstanceStore.PathOf(instance.Folder);
            string relative = $"{folder}/{Path.GetFileName(file.Filename)}";
            string path = Downloader.ResolveInside(instanceDir, relative)
                ?? throw new InvalidOperationException(L.F("{0}: geçersiz dosya adı.", project.Title));

            _log($"{project.Title} indiriliyor ({version.VersionNumber})...", "accent");
            var item = DownloadJob.Current?.Add($"{project.Title} {version.VersionNumber}", file.Size);
            if (item != null) await item.DownloadAsync(file.Url, path, file.Hashes?.Sha1, file.Hashes?.Sha512);
            else await Downloader.DownloadAsync(file.Url, path, sha1: file.Hashes?.Sha1, sha512: file.Hashes?.Sha512);

            // Önceki sürümün dosyası farklı adla duruyorsa kaldır
            var previous = instance.Content.FirstOrDefault(c => c.ProjectId == project.Id);
            if (previous != null)
            {
                string? oldPath = Downloader.ResolveInside(instanceDir, previous.File);
                if (oldPath != null && !string.Equals(oldPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
                    File.Delete(oldPath);
                if (oldPath != null && File.Exists(oldPath + ".disabled")) File.Delete(oldPath + ".disabled");   // kapatılmış eski sürüm
                instance.Content.Remove(previous);
            }

            instance.Content.Add(new InstalledContent
            {
                ProjectId = project.Id,
                VersionId = version.Id,
                Title = project.Title,
                VersionNumber = version.VersionNumber,
                Type = project.ProjectType,
                File = relative,
                IconUrl = project.IconUrl
            });
            installed.Add(project.Title);

            foreach (var dependency in version.Dependencies ?? new List<MrDependency>())
            {
                if (dependency.DependencyType != "required" || string.IsNullOrEmpty(dependency.ProjectId)) continue;
                await InstallRecursiveAsync(instance, dependency.ProjectId, visited, installed, isDependency: true);
            }
        }

        /// <summary>
        /// Dosyaların (SHA-1) bu yükleyiciler ve Minecraft sürümü için en yeni sürümleri; tek istekte
        /// (POST /version_files/update). Modrinth'te olmayan ya da uyumlu sürümü olmayan dosyalar sonuçta yer almaz.
        /// </summary>
        public async Task<Dictionary<string, MrVersion>> LatestByHashesAsync(IEnumerable<string> sha1Hashes, IEnumerable<string> loaders, string mcVersion)
        {
            var result = new Dictionary<string, MrVersion>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in sha1Hashes.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(500))
            {
                using var response = await Downloader.Http.PostAsJsonAsync($"{Api}/version_files/update",
                    new { hashes = chunk, algorithm = "sha1", loaders, game_versions = new[] { mcVersion } });
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException(L.F("Modrinth isteği başarısız oldu ({0}).", (int)response.StatusCode));
                var found = await response.Content.ReadFromJsonAsync<Dictionary<string, MrVersion>>(Json);
                foreach (var (hash, version) in found ?? new()) result[hash] = version;
            }
            return result;
        }

        // ── Eksik bağımlılıkları Modrinth projelerine eşleme ──────────────
        public async Task<List<MrProject>> ProjectsAsync(IEnumerable<string> ids)
        {
            var result = new List<MrProject>();
            foreach (var chunk in ids.Distinct().Chunk(100))
                result.AddRange(await GetAsync<List<MrProject>>($"{Api}/projects?ids={Uri.EscapeDataString(JsonSerializer.Serialize(chunk))}"));
            return result;
        }

        /// <summary>
        /// Mod kimliğinden arama metinleri: kendisi, alt çizgi/tire yerine boşluk ve sık kelimeler ayrılarak
        /// ("kotlinforforge" → "kotlin for forge"; arama motorları bitişik yazılanı bulamıyor).
        /// </summary>
        internal static List<string> SearchQueries(string modId)
        {
            string spaced = modId.Replace('_', ' ').Replace('-', ' ');
            string words = System.Text.RegularExpressions.Regex.Replace(spaced,
                @"(?<=[a-z]{3})(config|tweaks|utils|core|mods|mod|lib|api|for|the|and|of)(?=[a-z]{3}|\b)", " $1 ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            words = System.Text.RegularExpressions.Regex.Replace(words, "\\s+", " ").Trim();
            return new[] { modId, spaced, words }.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        /// <summary>
        /// Mod kimliği bu projeye mi ait: adres ya da ad (parantezli kısa adlar atılarak) kimlikle aynı ya da biri ötekinin başı.
        /// "yet_another_config_lib_v3" ↔ "YetAnotherConfigLib (YACL)", "cloth_config" ↔ "cloth-config".
        /// </summary>
        internal static bool LooksLike(string modId, string slug, string title)
        {
            string key = Normalize(modId);
            string baseTitle = Normalize(System.Text.RegularExpressions.Regex.Replace(title, @"\([^)]*\)|\[[^\]]*\]", ""));
            foreach (var name in new[] { Normalize(slug), Normalize(title), baseTitle })
                if (name == key || (name.Length >= 4 && key.Length >= 4 && (key.StartsWith(name) || name.StartsWith(key)))) return true;
            return false;
        }

        /// <summary>
        /// Jar'daki mod kimliklerini (ör. "yet_another_config_lib_v3") Modrinth projelerine eşler; bulunamayanlar null döner.
        ///   1. İsteyen modun dosyası Modrinth'teyse, o sürümün bildirdiği zorunlu bağımlılıklar arasından aranır
        ///      (kimlik ile proje adresi farklı olsa da doğru projeyi verir).
        ///   2. Olmazsa kimlik doğrudan proje adresi olarak, sonra aramayla denenir.
        /// Yalnızca instance'ın Minecraft sürümü ve yükleyicisiyle uyumlu sürümü olan projeler döner.
        /// </summary>
        public async Task<Dictionary<string, MrProject?>> ResolveModIdsAsync(InstanceMeta instance, ModScan scan, IReadOnlyList<MissingDependency> missing)
        {
            var result = missing.ToDictionary(m => m.Id, _ => (MrProject?)null, StringComparer.OrdinalIgnoreCase);
            if (missing.Count == 0) return result;
            string dir = InstanceStore.PathOf(instance.Folder);
            var loaders = CompatibleLoaders(instance.Loader);
            var installedProjects = new HashSet<string>(instance.Content.Select(c => c.ProjectId));

            async Task<bool> Compatible(MrProject p) =>
                p.ProjectType == "mod" && (await VersionsAsync(p.Id, instance.McVersion, loaders)).Count > 0;

            // 1) İsteyen modların Modrinth sürümlerindeki zorunlu bağımlılıklar
            try
            {
                var requesters = scan.Mods.Where(m => missing.Any(x => x.RequiredBy.Contains(m.Name))).ToList();
                var hashes = requesters.ToDictionary(m => m.File, m => Downloader.Sha1Of(Path.Combine(dir, "mods", m.File)));
                var versions = await VersionsByHashesAsync(hashes.Values);
                var depIds = versions.Values.SelectMany(v => v.Dependencies ?? new())
                    .Where(d => d.DependencyType == "required" && !string.IsNullOrEmpty(d.ProjectId) && !installedProjects.Contains(d.ProjectId!))
                    .Select(d => d.ProjectId!).Distinct().ToList();
                // Yalnızca bu instance'ta çalışabilecek adaylar (çok yükleyicili jar'ların Fabric bağımlılıkları NeoForge'da elenir)
                var candidates = new List<MrProject>();
                foreach (var p in depIds.Count == 0 ? new List<MrProject>() : await ProjectsAsync(depIds))
                    if (await Compatible(p)) candidates.Add(p);

                var unmatched = new List<MissingDependency>();
                foreach (var dep in missing)
                {
                    string key = Normalize(dep.Id);
                    var match = candidates.FirstOrDefault(p => Normalize(p.Slug) == key || Normalize(p.Title) == key)   // önce tam eşleşme
                        ?? candidates.FirstOrDefault(p => LooksLike(dep.Id, p.Slug, p.Title));
                    if (match != null) { result[dep.Id] = match; candidates.Remove(match); }
                    else unmatched.Add(dep);
                }
                // Geriye tek eksik ve tek uyumlu aday kaldıysa ikisi aynıdır
                if (unmatched.Count == 1 && candidates.Count == 1) result[unmatched[0].Id] = candidates[0];
            }
            catch (Exception) { /* Modrinth'e ulaşılamazsa aşağıdaki yollar denenir */ }

            // 2) Kimlik doğrudan proje adresi, sonra arama
            foreach (var dep in missing.Where(d => result[d.Id] == null))
            {
                try
                {
                    foreach (var slug in new[] { dep.Id, dep.Id.Replace('_', '-') }.Distinct())
                    {
                        try
                        {
                            var p = await ProjectAsync(slug);
                            if (await Compatible(p)) { result[dep.Id] = p; break; }
                        }
                        catch (HttpRequestException) { }
                    }
                    if (result[dep.Id] != null) continue;

                    string key = Normalize(dep.Id);
                    foreach (var query in SearchQueries(dep.Id))
                    {
                        var hits = await SearchAsync(query, "mod", instance.McVersion, instance.Loader, 0);
                        var hit = hits.Hits.FirstOrDefault(h => Normalize(h.Slug) == key || Normalize(h.Title) == key)
                            ?? hits.Hits.Take(3).FirstOrDefault(h => LooksLike(dep.Id, h.Slug, h.Title));
                        if (hit == null) continue;
                        result[dep.Id] = new MrProject { Id = hit.ProjectId, Slug = hit.Slug, Title = hit.Title, ProjectType = "mod", IconUrl = hit.IconUrl };
                        break;
                    }
                }
                catch (Exception) { }
            }
            return result;
        }

        // ── Modpack ───────────────────────────────────────────────────────
        /// <summary>
        /// Modrinth modpack'ini (.mrpack) indirir ve ondan yeni bir instance oluşturur.
        /// Kurulum yarıda kalırsa oluşturulan klasör silinir.
        /// </summary>
        public async Task<InstanceMeta> InstallModpackAsync(string projectId, string? versionId)
        {
            var project = await ProjectAsync(projectId);
            if (project.ProjectType != "modpack")
                throw new InvalidOperationException(L.F("{0} bir modpack değil.", project.Title));

            var versions = await VersionsAsync(project.Id);
            var version = (string.IsNullOrEmpty(versionId) ? null : versions.FirstOrDefault(v => v.Id == versionId))
                ?? versions.FirstOrDefault(v => v.VersionType == "release")
                ?? versions.FirstOrDefault()
                ?? throw new InvalidOperationException(L.F("{0}: yayınlanmış sürüm yok.", project.Title));

            var packFile = version.Files.FirstOrDefault(f => f.Primary && f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                ?? version.Files.FirstOrDefault(f => f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(L.F("{0}: .mrpack dosyası bulunamadı.", project.Title));

            string packPath = Path.Combine(Path.GetTempPath(), $"vulu-{Guid.NewGuid():N}.mrpack");
            using var job = new DownloadJob($"{project.Title} kuruluyor") { Phase = "Modpack indiriliyor" };

            try
            {
                _log($"{project.Title} {version.VersionNumber} indiriliyor...", "accent");
                await job.Add(packFile.Filename, packFile.Size)
                    .DownloadAsync(packFile.Url, packPath, packFile.Hashes?.Sha1, packFile.Hashes?.Sha512);
                return await CreateFromPackAsync(packPath, project.Title, project.IconUrl, project.Id, version.VersionNumber, job);
            }
            catch (Exception ex) { job.Fail(ex.Message); throw; }
            finally
            {
                if (File.Exists(packPath)) File.Delete(packPath);
            }
        }

        /// <summary>Bilgisayardaki .mrpack dosyasından yeni instance (içe aktarma). Adı paketin kendi adından.</summary>
        public async Task<InstanceMeta> ImportPackAsync(string packPath)
        {
            string name = Path.GetFileNameWithoutExtension(packPath);
            using var job = new DownloadJob($"{name} kuruluyor") { Phase = "Paket okunuyor" };
            try { return await CreateFromPackAsync(packPath, null, null, null, null, job); }
            catch (Exception ex) { job.Fail(ex.Message); throw; }
        }

        /// <summary>
        /// .mrpack dosyasından instance kurar: dizindeki dosyalar indirilir, overrides açılır. Yarıda kalırsa klasör silinir.
        /// title boşsa paketin kendi adı kullanılır.
        /// </summary>
        private async Task<InstanceMeta> CreateFromPackAsync(string packPath, string? title, string? iconUrl, string? projectId, string? versionNumber, DownloadJob job)
        {
            InstanceMeta? instance = null;
            try
            {
                using var zip = ZipFile.OpenRead(packPath);
                var indexEntry = zip.GetEntry("modrinth.index.json")
                    ?? throw new InvalidDataException("Modpack bozuk: modrinth.index.json yok.");

                MrPackIndex index;
                using (var stream = indexEntry.Open())
                    index = await JsonSerializer.DeserializeAsync<MrPackIndex>(stream, PackJson)
                        ?? throw new InvalidDataException("Modpack bozuk: dizin okunamadı.");

                var (loader, loaderVersion) = ReadLoader(index);
                if (!index.Dependencies.TryGetValue("minecraft", out var mcVersion) || string.IsNullOrEmpty(mcVersion))
                    throw new InvalidDataException("Modpack hangi Minecraft sürümünü istediğini belirtmiyor.");

                string name = !string.IsNullOrWhiteSpace(title) ? title : !string.IsNullOrWhiteSpace(index.Name) ? index.Name : Path.GetFileNameWithoutExtension(packPath);
                if (name.Length > 40) name = name[..40];
                job.Title = $"{name} kuruluyor";
                instance = InstanceStore.Create(name, mcVersion, loader, loaderVersion);
                instance.IconUrl = iconUrl;
                instance.ModpackProjectId = projectId;
                instance.ModpackVersion = versionNumber ?? (string.IsNullOrWhiteSpace(index.VersionId) ? null : index.VersionId);
                InstanceStore.Save(instance);

                string instanceDir = InstanceStore.PathOf(instance.Folder);
                job.Phase = "Modlar indiriliyor";
                await DownloadPackFilesAsync(index, instanceDir, job);

                _log("Modpack ayarları açılıyor...", "replace");
                job.Phase = "Ayarlar yerleştiriliyor";
                ExtractOverrides(zip, "overrides/", instanceDir);
                ExtractOverrides(zip, "client-overrides/", instanceDir);
                instance.PackFiles = PackContent(index, zip);   // sürüm değişince eşitlenecek dosyalar
                InstanceStore.Save(instance);

                _log($"{name} kuruldu.", "ok");
                job.Complete($"{name} kuruldu");
                return instance;
            }
            catch
            {
                if (instance != null)
                {
                    try { InstanceStore.Delete(instance.Folder); } catch { }
                }
                throw;
            }
        }

        private static (string Loader, string? Version) ReadLoader(MrPackIndex index)
        {
            foreach (var (key, loader) in new[]
                     {
                         ("fabric-loader", "fabric"), ("quilt-loader", "quilt"),
                         ("neoforge", "neoforge"), ("forge", "forge")
                     })
            {
                if (index.Dependencies.TryGetValue(key, out var version) && !string.IsNullOrEmpty(version))
                    return (loader, version);
            }
            return ("vanilla", null);
        }

        /// <summary>Paketin istemcide kurulan dosyaları (sunucuya özel olanlar hariç), instance klasörüne göre yollarıyla.</summary>
        private static List<string> ClientFiles(MrPackIndex index) =>
            index.Files.Where(f => f.Env?.Client != "unsupported").Select(f => ModpackFiles.Normalize(f.Path)).ToList();

        private static List<string> PackContent(MrPackIndex index, ZipArchive zip) =>
            ClientFiles(index).Where(ModpackFiles.IsContent)
                .Concat(ModpackFiles.OverrideContent(zip, "overrides/")).Concat(ModpackFiles.OverrideContent(zip, "client-overrides/"))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private static async Task<MrPackIndex> ReadIndexAsync(ZipArchive zip)
        {
            var indexEntry = zip.GetEntry("modrinth.index.json") ?? throw new InvalidDataException("Modpack bozuk: modrinth.index.json yok.");
            using var stream = indexEntry.Open();
            return await JsonSerializer.DeserializeAsync<MrPackIndex>(stream, PackJson) ?? throw new InvalidDataException("Modpack bozuk: dizin okunamadı.");
        }

        private async Task<string> DownloadVersionPackAsync(MrVersion version, DownloadJob job)
        {
            var packFile = version.Files.FirstOrDefault(f => f.Primary && f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                ?? version.Files.FirstOrDefault(f => f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(L.F("{0}: .mrpack dosyası bulunamadı.", version.VersionNumber));
            string packPath = Path.Combine(Path.GetTempPath(), $"vulu-{Guid.NewGuid():N}.mrpack");
            await job.Add(packFile.Filename, packFile.Size).DownloadAsync(packFile.Url, packPath, packFile.Hashes?.Sha1, packFile.Hashes?.Sha512);
            return packPath;
        }

        // ── Sürüm değiştirme ──
        /// <summary>Modpack'in sürümleri (yeniden eskiye).</summary>
        public Task<List<MrVersion>> PackVersionsAsync(string projectId) => VersionsAsync(projectId);

        /// <summary>
        /// Kurulu modpack'i başka bir sürüme geçirir: eski paket dosyalarından yenisinde olmayanlar kaldırılır, yeni dosyalar
        /// indirilir, ayarlar (overrides) yeniden açılır; önce ayar klasörleri yedeklenir. Sonradan eklenen içerik ve dünyalar kalır.
        /// </summary>
        public async Task SwitchAsync(InstanceMeta instance, string versionId)
        {
            string projectId = instance.ModpackProjectId ?? throw new InvalidOperationException("Bu paket bir Modrinth modpack'i değil.");
            var versions = await VersionsAsync(projectId);
            var target = versions.FirstOrDefault(v => v.Id == versionId) ?? throw new InvalidOperationException("Sürüm bulunamadı.");
            using var job = new DownloadJob($"{instance.Name} → {target.VersionNumber}") { Phase = "Modpack indiriliyor" };
            var temp = new List<string>();
            try
            {
                // Eski sürümün dosyaları: kayıtlıysa oradan, değilse eski sürümün kendi paketinden
                List<string> oldFiles = instance.PackFiles ?? new List<string>();
                if (instance.PackFiles == null)
                {
                    var old = versions.FirstOrDefault(v => v.VersionNumber == instance.ModpackVersion)
                        ?? throw new InvalidOperationException("Kurulu sürüm Modrinth'te bulunamadı; eski dosyalar güvenle ayrılamıyor. Paketi yeniden kurmak daha güvenli.");
                    job.Phase = "Kurulu sürüm okunuyor";
                    string oldPath = await DownloadVersionPackAsync(old, job);
                    temp.Add(oldPath);
                    using var oldZip = ZipFile.OpenRead(oldPath);
                    oldFiles = PackContent(await ReadIndexAsync(oldZip), oldZip);
                }

                job.Phase = "Modpack indiriliyor";
                string packPath = await DownloadVersionPackAsync(target, job);
                temp.Add(packPath);
                using var zip = ZipFile.OpenRead(packPath);
                var index = await ReadIndexAsync(zip);
                var (loader, loaderVersion) = ReadLoader(index);
                if (!index.Dependencies.TryGetValue("minecraft", out var mcVersion) || string.IsNullOrEmpty(mcVersion))
                    throw new InvalidDataException("Modpack hangi Minecraft sürümünü istediğini belirtmiyor.");

                string dir = InstanceStore.PathOf(instance.Folder);
                job.Phase = "Ayarlar yedekleniyor";
                ModpackFiles.BackupConfigs(dir, instance.ModpackVersion);
                var newFiles = PackContent(index, zip);
                int removed = ModpackFiles.RemoveOld(dir, oldFiles, newFiles);
                _log(L.F("{0}: eski sürümden {1} dosya kaldırıldı.", instance.Name, removed), "muted");

                await DownloadPackFilesAsync(index, dir, job);
                job.Phase = "Ayarlar yerleştiriliyor";
                ExtractOverrides(zip, "overrides/", dir);
                ExtractOverrides(zip, "client-overrides/", dir);

                instance.McVersion = mcVersion;
                instance.Loader = loader;
                instance.LoaderVersion = loaderVersion;
                instance.ModpackVersion = target.VersionNumber;
                instance.PackFiles = newFiles;
                InstanceStore.Save(instance);
                _log(L.F("{0} artık {1}.", instance.Name, target.VersionNumber), "ok");
                job.Complete(L.F("{0} {1} sürümünde", instance.Name, target.VersionNumber));
            }
            catch (Exception ex) { job.Fail(ex.Message); throw; }
            finally { foreach (var t in temp) try { File.Delete(t); } catch { } }
        }

        private async Task DownloadPackFilesAsync(MrPackIndex index, string instanceDir, DownloadJob job)
        {
            // Sunucuya özel dosyalar istemcide gereksizdir
            var files = index.Files.Where(f => f.Env?.Client != "unsupported").ToList();
            var items = files.ToDictionary(f => f, f => job.Add(Path.GetFileName(f.Path), f.FileSize));
            job.Phase = "Modlar indiriliyor";
            int done = 0;

            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (file, cancellation) =>
            {
                string path = Downloader.ResolveInside(instanceDir, file.Path)
                    ?? throw new InvalidDataException(L.F("Modpack geçersiz bir dosya yolu içeriyor: {0}", file.Path));

                string url = file.Downloads.FirstOrDefault(IsAllowedPackUrl)
                    ?? throw new InvalidDataException(L.F("Modpack izin verilmeyen bir adresten indirme istiyor: {0}", file.Path));

                await items[file].DownloadAsync(url, path, file.Hashes?.Sha1, file.Hashes?.Sha512, cancellation);

                int current = Interlocked.Increment(ref done);
                _log(L.F("Modpack dosyaları indiriliyor... {0}/{1}", current, files.Count), "replace");
            });
        }

        private static bool IsAllowedPackUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            AllowedPackHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

        internal static void ExtractOverrides(ZipArchive zip, string prefix, string instanceDir)
        {
            ZipGuard.Check(zip);
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.FullName.EndsWith('/')) continue;

                string relative = entry.FullName[prefix.Length..];
                // instance.json launcher'a aittir; modpack üzerine yazamaz
                if (string.Equals(relative, "instance.json", StringComparison.OrdinalIgnoreCase)) continue;

                string? path = Downloader.ResolveInside(instanceDir, relative);
                if (path == null) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                Downloader.MakeWritable(path);
                entry.ExtractToFile(path, overwrite: true);
            }
        }

        private static async Task<T> GetAsync<T>(string url)
        {
            using var response = await Downloader.Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(L.F("Modrinth isteği başarısız oldu ({0}).", (int)response.StatusCode));

            return await response.Content.ReadFromJsonAsync<T>(Json)
                ?? throw new InvalidDataException("Modrinth boş cevap döndü.");
        }

        // modrinth.index.json camelCase kullanır (API'nin aksine)
        private static readonly JsonSerializerOptions PackJson = new(JsonSerializerDefaults.Web);
    }

    // ── Modrinth API modelleri ────────────────────────────────────────────
    internal sealed class MrSearchResult
    {
        public List<MrSearchHit> Hits { get; set; } = new();
        public int Offset { get; set; }
        public int Limit { get; set; }
        public int TotalHits { get; set; }
    }

    internal sealed class MrProjectDetail
    {
        public string Id { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string ProjectType { get; set; } = "";
        public string Description { get; set; } = "";
        public string Body { get; set; } = "";
        public string? IconUrl { get; set; }
        public long Downloads { get; set; }
        public long Followers { get; set; }
        public string? Updated { get; set; }
        public List<string> Categories { get; set; } = new();
        public List<string> Loaders { get; set; } = new();
        public List<string> GameVersions { get; set; } = new();
        public List<MrGalleryImage> Gallery { get; set; } = new();
        public string? SourceUrl { get; set; }
        public string? IssuesUrl { get; set; }
        public string? WikiUrl { get; set; }
        public string? DiscordUrl { get; set; }
    }

    internal sealed class MrGalleryImage
    {
        public string Url { get; set; } = "";
        public string? Title { get; set; }
        public bool Featured { get; set; }
    }

    internal sealed class MrSearchHit
    {
        public string ProjectId { get; set; } = "";
        public string ProjectType { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public string? IconUrl { get; set; }
        public long Downloads { get; set; }
        public long Follows { get; set; }
        public string? DateModified { get; set; }
        public List<string> Categories { get; set; } = new();
        public List<string> DisplayCategories { get; set; } = new();
        /// <summary>Desteklenen Minecraft sürümleri (Modrinth'te "versions" alanı).</summary>
        public List<string> Versions { get; set; } = new();
        public string? LatestVersion { get; set; }
    }

    internal sealed class MrCategory
    {
        public string Name { get; set; } = "";
        public string ProjectType { get; set; } = "";
        public string Header { get; set; } = "";
    }

    internal sealed class MrProject
    {
        public string Id { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string ProjectType { get; set; } = "";
        public string? IconUrl { get; set; }
        public string Description { get; set; } = "";
        public long Downloads { get; set; }
        public long Followers { get; set; }
        public string? Updated { get; set; }
        public List<string> Categories { get; set; } = new();
        public List<string> Loaders { get; set; } = new();
        public List<string> GameVersions { get; set; } = new();
    }

    internal sealed class MrVersion
    {
        public string Id { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string Name { get; set; } = "";
        public string VersionNumber { get; set; } = "";
        public string VersionType { get; set; } = "";
        public string? DatePublished { get; set; }
        /// <summary>Değişiklik notu (markdown); sürüm listesiyle birlikte gelir.</summary>
        public string? Changelog { get; set; }
        public List<string> GameVersions { get; set; } = new();
        public List<string> Loaders { get; set; } = new();
        public List<MrFile> Files { get; set; } = new();
        public List<MrDependency>? Dependencies { get; set; }
    }

    internal sealed class MrFile
    {
        public string Url { get; set; } = "";
        public string Filename { get; set; } = "";
        public bool Primary { get; set; }
        public long Size { get; set; }
        public MrHashes? Hashes { get; set; }
    }

    internal sealed class MrHashes
    {
        public string? Sha1 { get; set; }
        public string? Sha512 { get; set; }
    }

    internal sealed class MrDependency
    {
        public string? ProjectId { get; set; }
        public string? VersionId { get; set; }
        public string DependencyType { get; set; } = "";
    }

    internal sealed class MrPackIndex
    {
        public string Name { get; set; } = "";
        public string VersionId { get; set; } = "";
        public List<MrPackFile> Files { get; set; } = new();
        public Dictionary<string, string> Dependencies { get; set; } = new();
    }

    internal sealed class MrPackFile
    {
        public string Path { get; set; } = "";
        public MrHashes? Hashes { get; set; }
        public MrPackEnv? Env { get; set; }
        public List<string> Downloads { get; set; } = new();
        public long FileSize { get; set; }
    }

    internal sealed class MrPackEnv
    {
        public string? Client { get; set; }
        public string? Server { get; set; }
    }
}
