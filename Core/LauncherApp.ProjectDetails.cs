using System.Collections.Concurrent;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Keşfet'teki bir projenin ayrıntıları (açıklama, galeri, bağlantılar) ve modpack'lerin özeti (mod sayısı,
    /// Minecraft sürümü, yükleyici). Modpack özeti paketin manifest'inden okunur: zip'in tamamı indirilmez
    /// (bkz. <see cref="RemoteZip"/>); sonuç dosya kimliğine göre diskte saklanır.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private static readonly string PackInfoCachePath = Path.Combine(AppPaths.AppData, "cache", "packinfo.json");
        private readonly ConcurrentDictionary<string, PackInfo?> _packInfo = new();
        private readonly SemaphoreSlim _packInfoGate = new(4);
        private bool _packInfoLoaded;

        internal sealed record PackInfo(int ModCount, string? McVersion, string? Loader);

        private void RegisterProjectDetailCommands()
        {
            _bridge.On("content.details", async args => await ProjectDetailsAsync(args.Str("source"), args.Str("id")));
            _bridge.On("content.packInfo", async args => await PackInfoAsync(args.Str("source"), args.Str("id"), args.Str("versionId")));
            // Proje açıklamalarındaki ve bağlantılarındaki adresler varsayılan tarayıcıda açılır (yalnızca http/https)
            _bridge.On("links.open", args =>
            {
                string url = args.Str("url");
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                    throw new InvalidOperationException("Geçersiz adres.");
                _host.OpenUrl(uri.AbsoluteUri);
                return Task.FromResult<object?>(new { opened = true });
            });
        }

        private async Task<object?> ProjectDetailsAsync(string source, string id)
        {
            RequireLogin();
            if (source == CurseForgeService.Source)
            {
                int modId = RequireCfId(id, "proje");
                var modTask = _curseforge.ModAsync(modId);
                var descriptionTask = _curseforge.DescriptionAsync(modId);
                var mod = await modTask;
                string description = "";
                try { description = await descriptionTask; } catch (ApiException) { }
                return new
                {
                    html = description,
                    markdown = (string?)null,
                    gallery = mod.Screenshots.Where(s => IsHttps(s.Url)).Select(s => new { url = s.Url, thumb = IsHttps(s.ThumbnailUrl) ? s.ThumbnailUrl : s.Url, title = s.Title }),
                    links = new[]
                    {
                        Link("Kaynak kod", mod.Links?.SourceUrl), Link("Hata bildir", mod.Links?.IssuesUrl), Link("Wiki", mod.Links?.WikiUrl),
                    }.Where(l => l != null),
                };
            }

            var project = await _modrinth.ProjectDetailAsync(id);
            return new
            {
                html = (string?)null,
                markdown = project.Body,
                gallery = project.Gallery.Where(g => IsHttps(g.Url)).OrderByDescending(g => g.Featured).Select(g => new { url = g.Url, thumb = g.Url, title = g.Title ?? "" }),
                links = new[]
                {
                    Link("Kaynak kod", project.SourceUrl), Link("Hata bildir", project.IssuesUrl), Link("Wiki", project.WikiUrl), Link("Discord", project.DiscordUrl),
                }.Where(l => l != null),
            };
        }

        private static bool IsHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
        private static object? Link(string label, string? url) => IsHttps(url) ? new { label, url } : null;

        /// <summary>
        /// Modpack'in son sürümünün mod sayısı, Minecraft sürümü ve yükleyicisi; okunamazsa null. versionId arama
        /// sonucundan gelir (CurseForge'da ana dosya, Modrinth'te son sürüm): önbellekteyse hiç istek atılmaz.
        /// </summary>
        private async Task<object?> PackInfoAsync(string source, string id, string versionId)
        {
            RequireLogin();
            LoadPackInfoCache();
            string? fileKey = null, url = null, entry = null;
            if (source == CurseForgeService.Source)
            {
                int fileId = int.TryParse(versionId, out var given) && given > 0 ? given
                    : (await _curseforge.ModAsync(RequireCfId(id, "proje"))) is { ClassId: 4471, MainFileId: > 0 } mod ? mod.MainFileId : 0;
                if (fileId <= 0) return null;
                fileKey = $"cf:{fileId}";
                if (!_packInfo.ContainsKey(fileKey))
                {
                    var file = (await _curseforge.FilesByIdsAsync(new[] { fileId })).FirstOrDefault();
                    // edge.forgecdn.net parça isteklerine 404 veriyor; aynı dosya mediafilez'de parça parça okunabiliyor
                    if (file?.DownloadUrl is { } download && CurseForgeService.IsCurseForgeCdn(download))
                        url = download.Replace("://edge.forgecdn.net/", "://mediafilez.forgecdn.net/");
                    entry = "manifest.json";
                }
            }
            else
            {
                fileKey = versionId.Length > 0 ? $"mr:{versionId}" : null;
                if (fileKey == null || !_packInfo.ContainsKey(fileKey))
                {
                    var version = versionId.Length > 0
                        ? (await _modrinth.VersionsByIdsAsync(new[] { versionId })).FirstOrDefault()
                        : (await _modrinth.VersionsAsync(id)).FirstOrDefault();
                    var file = version?.Files.FirstOrDefault(f => f.Primary) ?? version?.Files.FirstOrDefault();
                    if (version == null || file == null) return null;
                    fileKey = $"mr:{version.Id}";
                    if (file.Url.StartsWith("https://cdn.modrinth.com/", StringComparison.Ordinal))
                    {
                        url = file.Url;
                        entry = "modrinth.index.json";
                    }
                }
            }

            if (!_packInfo.TryGetValue(fileKey, out var info))
            {
                info = null;
                if (url != null)
                {
                    await _packInfoGate.WaitAsync();
                    try { info = ParsePackInfo(await RemoteZip.ReadTextAsync(url, entry!), entry!); }
                    catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidDataException or JsonException or TaskCanceledException) { }
                    finally { _packInfoGate.Release(); }
                }
                _packInfo[fileKey] = info;
                SavePackInfoCache();
            }
            return info == null ? null : new { modCount = info.ModCount, mcVersion = info.McVersion, loader = info.Loader };
        }

        private static PackInfo? ParsePackInfo(string? json, string entry)
        {
            if (json == null) return null;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (entry == "manifest.json")
            {
                int count = root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array ? files.GetArrayLength() : 0;
                string? mc = null, loader = null;
                if (root.TryGetProperty("minecraft", out var minecraft))
                {
                    mc = minecraft.TryGetProperty("version", out var v) ? v.GetString() : null;
                    if (minecraft.TryGetProperty("modLoaders", out var loaders) && loaders.ValueKind == JsonValueKind.Array && loaders.GetArrayLength() > 0
                        && loaders[0].TryGetProperty("id", out var lid) && lid.GetString() is { } loaderId)
                        loader = loaderId.Split('-')[0].ToLowerInvariant();
                }
                return new PackInfo(count, mc, loader);
            }
            // Modrinth: mods/ altındaki dosyalar sayılır (kaynak paketleri ve shaderlar hariç)
            int mods = root.TryGetProperty("files", out var mrFiles) && mrFiles.ValueKind == JsonValueKind.Array
                ? mrFiles.EnumerateArray().Count(f => f.TryGetProperty("path", out var p) && (p.GetString() ?? "").StartsWith("mods/", StringComparison.Ordinal))
                : 0;
            string? mrMc = null, mrLoader = null;
            if (root.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Object)
                foreach (var dep in deps.EnumerateObject())
                {
                    if (dep.Name == "minecraft") mrMc = dep.Value.GetString();
                    else if (dep.Name is "fabric-loader" or "quilt-loader" or "forge" or "neoforge") mrLoader = dep.Name.Replace("-loader", "");
                }
            return new PackInfo(mods, mrMc, mrLoader);
        }

        private void LoadPackInfoCache()
        {
            if (_packInfoLoaded) return;
            _packInfoLoaded = true;
            try
            {
                if (!File.Exists(PackInfoCachePath)) return;
                var saved = JsonSerializer.Deserialize<Dictionary<string, PackInfo?>>(File.ReadAllText(PackInfoCachePath));
                if (saved != null) foreach (var (key, value) in saved) _packInfo[key] = value;
            }
            catch { /* bozuk önbellek: baştan doldurulur */ }
        }

        private void SavePackInfoCache()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PackInfoCachePath)!);
                // Okunamayanlar (null) kaydedilmez: bir dahaki açılışta yeniden denenir
                var snapshot = _packInfo.Where(p => p.Value != null).Take(2000).ToDictionary(p => p.Key, p => p.Value);
                File.WriteAllText(PackInfoCachePath, JsonSerializer.Serialize(snapshot));
            }
            catch { }
        }
    }
}
