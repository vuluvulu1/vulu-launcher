using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VuluShared
{
    /// <summary>
    /// Launcher ve Updater'ın ortak kullandığı sürüm bilgisi. Sürümler GitHub Releases'ta yayınlanır;
    /// her release'te iki dosya bulunur: tüm launcher klasörünün zip'i ve onu anlatan release.json.
    /// </summary>
    public static class Releases
    {
        public const string Repository = "vuluvulu1/vulu-launcher";
        public const string ManifestAssetName = "release.json";

        /// <summary>Kurulu sürümün dosya listesi; exe'nin yanında durur.</summary>
        public const string InstallFileName = "install.json";

        public const string LauncherExeName = "vulu Launcher.exe";
        public const string UpdaterExeName = "Updater.exe";

        public static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static HttpClient CreateHttpClient(TimeSpan timeout)
        {
            var http = new HttpClient { Timeout = timeout };
            // GitHub API User-Agent başlığı olmadan isteği reddeder
            http.DefaultRequestHeaders.UserAgent.ParseAdd("vulu-launcher");
            return http;
        }

        /// <summary>
        /// En son yayınlanan release'in manifest'ini ve zip adresini getirir.
        /// Ulaşılamazsa ya da release eksikse hata fırlatır.
        /// </summary>
        public static async Task<LatestRelease> GetLatestAsync(HttpClient http, CancellationToken token = default)
        {
            // Test için: GitHub yerine bir release.json adresi (ör. yerel sunucu). Zip aynı klasörde aranır.
            string? feed = Environment.GetEnvironmentVariable("VULU_UPDATE_FEED");
            if (!string.IsNullOrWhiteSpace(feed))
            {
                var test = await http.GetFromJsonAsync<ReleaseManifest>(feed, Json, token)
                    ?? throw new InvalidDataException($"{ManifestAssetName} okunamadı.");
                return new LatestRelease(test, new Uri(new Uri(feed), test.Asset).ToString());
            }

            var release = await http.GetFromJsonAsync<GitHubRelease>(
                $"https://api.github.com/repos/{Repository}/releases/latest", Json, token)
                ?? throw new InvalidDataException("GitHub boş cevap döndürdü.");

            var manifestAsset = release.Assets.FirstOrDefault(a => a.Name == ManifestAssetName)
                ?? throw new InvalidDataException($"{release.TagName} sürümünde {ManifestAssetName} yok.");

            var manifest = await http.GetFromJsonAsync<ReleaseManifest>(manifestAsset.DownloadUrl, Json, token)
                ?? throw new InvalidDataException($"{ManifestAssetName} okunamadı.");

            var zipAsset = release.Assets.FirstOrDefault(a => a.Name == manifest.Asset)
                ?? throw new InvalidDataException($"{manifest.Asset} release'te bulunamadı.");

            if (string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Sha256))
                throw new InvalidDataException($"{ManifestAssetName} eksik bilgi içeriyor.");

            // Notlar release.json'da yoksa GitHub'daki release açıklaması kullanılır
            if (string.IsNullOrWhiteSpace(manifest.Notes)) manifest.Notes = release.Body ?? "";

            return new LatestRelease(manifest, zipAsset.DownloadUrl);
        }

        /// <summary>"0.8", "v1.2.3" gibi sürümleri karşılaştırır; ayrıştırılamıyorsa farklı olmasını yeterli sayar.</summary>
        public static bool IsNewer(string remote, string local)
        {
            if (Version.TryParse(Normalize(remote), out var r) && Version.TryParse(Normalize(local), out var l))
                return r > l;

            return !string.Equals(remote.Trim(), local.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"v0.8.0+abc123" → "0.8.0"</summary>
        public static string Clean(string? version)
        {
            string v = (version ?? "").Trim().TrimStart('v', 'V');
            int plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }

        private static string Normalize(string version)
        {
            string v = Clean(version);
            return v.Contains('.') ? v : v + ".0";
        }

        /// <summary>Manifest'teki göreli yolu klasör içindeki tam yola çevirir; klasör dışına çıkıyorsa null.</summary>
        public static string? ResolveInside(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized)) return null;

            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(fullRoot, normalized));
            return full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        public static string Sha256Of(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        }
    }

    public sealed record LatestRelease(ReleaseManifest Manifest, string ZipUrl);

    /// <summary>release.json (GitHub'da) ve install.json (kurulu klasörde) aynı biçimdedir.</summary>
    public sealed class ReleaseManifest
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("notes")] public string Notes { get; set; } = "";
        [JsonPropertyName("publishedAt")] public string PublishedAt { get; set; } = "";
        [JsonPropertyName("asset")] public string Asset { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("files")] public List<ReleaseFile> Files { get; set; } = new();
    }

    public sealed class ReleaseFile
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    internal sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = new();
    }

    internal sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
    }
}
