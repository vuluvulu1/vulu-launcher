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

        /// <summary>Quilt, Fabric modlarını da çalıştırır; diğerleri yalnızca kendi modlarını.</summary>
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
            await InstallRecursiveAsync(instance, projectId, new HashSet<string>(), installed, isDependency: false, versionId);
            InstanceStore.Save(instance);
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
            if (folder == null)
                throw new InvalidOperationException($"{project.Title}: bu içerik türü ({project.ProjectType}) instance'a kurulamıyor.");

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
                throw new InvalidOperationException($"{project.Title}: {target} için uyumlu sürüm yok.");
            }

            var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault()
                ?? throw new InvalidOperationException($"{project.Title}: indirilecek dosya yok.");

            string instanceDir = InstanceStore.PathOf(instance.Folder);
            string relative = $"{folder}/{Path.GetFileName(file.Filename)}";
            string path = Downloader.ResolveInside(instanceDir, relative)
                ?? throw new InvalidOperationException($"{project.Title}: geçersiz dosya adı.");

            _log($"{project.Title} indiriliyor ({version.VersionNumber})...", "accent");
            await Downloader.DownloadAsync(file.Url, path, sha1: file.Hashes?.Sha1, sha512: file.Hashes?.Sha512);

            // Önceki sürümün dosyası farklı adla duruyorsa kaldır
            var previous = instance.Content.FirstOrDefault(c => c.ProjectId == project.Id);
            if (previous != null)
            {
                string? oldPath = Downloader.ResolveInside(instanceDir, previous.File);
                if (oldPath != null && !string.Equals(oldPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
                    File.Delete(oldPath);
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

        // ── Modpack ───────────────────────────────────────────────────────
        /// <summary>
        /// Modrinth modpack'ini (.mrpack) indirir ve ondan yeni bir instance oluşturur.
        /// Kurulum yarıda kalırsa oluşturulan klasör silinir.
        /// </summary>
        public async Task<InstanceMeta> InstallModpackAsync(string projectId, string? versionId)
        {
            var project = await ProjectAsync(projectId);
            if (project.ProjectType != "modpack")
                throw new InvalidOperationException($"{project.Title} bir modpack değil.");

            var versions = await VersionsAsync(project.Id);
            var version = (string.IsNullOrEmpty(versionId) ? null : versions.FirstOrDefault(v => v.Id == versionId))
                ?? versions.FirstOrDefault(v => v.VersionType == "release")
                ?? versions.FirstOrDefault()
                ?? throw new InvalidOperationException($"{project.Title}: yayınlanmış sürüm yok.");

            var packFile = version.Files.FirstOrDefault(f => f.Primary && f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                ?? version.Files.FirstOrDefault(f => f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"{project.Title}: .mrpack dosyası bulunamadı.");

            string packPath = Path.Combine(Path.GetTempPath(), $"vulu-{Guid.NewGuid():N}.mrpack");
            InstanceMeta? instance = null;

            try
            {
                _log($"{project.Title} {version.VersionNumber} indiriliyor...", "accent");
                await Downloader.DownloadAsync(packFile.Url, packPath, sha1: packFile.Hashes?.Sha1, sha512: packFile.Hashes?.Sha512);

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

                instance = InstanceStore.Create(project.Title, mcVersion, loader, loaderVersion);
                instance.IconUrl = project.IconUrl;
                instance.ModpackProjectId = project.Id;
                instance.ModpackVersion = version.VersionNumber;
                InstanceStore.Save(instance);

                string instanceDir = InstanceStore.PathOf(instance.Folder);
                await DownloadPackFilesAsync(index, instanceDir);

                _log("Modpack ayarları açılıyor...", "replace");
                ExtractOverrides(zip, "overrides/", instanceDir);
                ExtractOverrides(zip, "client-overrides/", instanceDir);

                _log($"{project.Title} kuruldu.", "ok");
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
            finally
            {
                if (File.Exists(packPath)) File.Delete(packPath);
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

        private async Task DownloadPackFilesAsync(MrPackIndex index, string instanceDir)
        {
            // Sunucuya özel dosyalar istemcide gereksizdir
            var files = index.Files.Where(f => f.Env?.Client != "unsupported").ToList();
            int done = 0;

            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (file, cancellation) =>
            {
                string path = Downloader.ResolveInside(instanceDir, file.Path)
                    ?? throw new InvalidDataException($"Modpack geçersiz bir dosya yolu içeriyor: {file.Path}");

                string url = file.Downloads.FirstOrDefault(IsAllowedPackUrl)
                    ?? throw new InvalidDataException($"Modpack izin verilmeyen bir adresten indirme istiyor: {file.Path}");

                await Downloader.DownloadAsync(url, path, sha1: file.Hashes?.Sha1, sha512: file.Hashes?.Sha512, cancellation: cancellation);

                int current = Interlocked.Increment(ref done);
                _log($"Modpack dosyaları indiriliyor... {current}/{files.Count}", "replace");
            });
        }

        private static bool IsAllowedPackUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            AllowedPackHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

        private static void ExtractOverrides(ZipArchive zip, string prefix, string instanceDir)
        {
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.FullName.EndsWith('/')) continue;

                string relative = entry.FullName[prefix.Length..];
                // instance.json launcher'a aittir; modpack üzerine yazamaz
                if (string.Equals(relative, "instance.json", StringComparison.OrdinalIgnoreCase)) continue;

                string? path = Downloader.ResolveInside(instanceDir, relative);
                if (path == null) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
            }
        }

        private static async Task<T> GetAsync<T>(string url)
        {
            using var response = await Downloader.Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Modrinth isteği başarısız oldu ({(int)response.StatusCode}).");

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
    }

    internal sealed class MrVersion
    {
        public string Id { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string Name { get; set; } = "";
        public string VersionNumber { get; set; } = "";
        public string VersionType { get; set; } = "";
        public string? DatePublished { get; set; }
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
