using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VuluShared
{
    /// <summary>
    /// Launcher ve Updater'ın ortak kullandığı sürüm bilgisi. Sürümler GitHub Releases'ta yayınlanır;
    /// her release'te tüm launcher klasörünün zip'i, onu anlatan release.json ve release.json'un imzası bulunur.
    ///
    /// release.json zip'in ve içindeki her dosyanın SHA-256'sını taşır; imzası doğrulanınca bütün paket doğrulanmış
    /// olur. İmzalayan özel anahtar yalnızca release çıkaran bilgisayardadır (tools/ReleaseSigner), bu yüzden GitHub
    /// hesabına erişen biri tek başına kullanıcılara sahte güncelleme kuramaz.
    /// </summary>
    public static class Releases
    {
        public const string Repository = "vuluvulu1/vulu-launcher";
        public const string ManifestAssetName = "release.json";
        public const string SignatureAssetName = "release.json.sig";

        /// <summary>Release imzasını doğrulayan ECDSA P-256 açık anahtarı (SubjectPublicKeyInfo, base64).</summary>
        public const string ReleasePublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE3TeeU2nXv6fbTkri3BObKy19SbNBbWMMqC+D7e4JSDC8jiGZU62lz7Nvk1tZcKSQAh3Bx08rEv7wBW4XbTrxzQ==";

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
            // Test için: GitHub yerine bir release.json adresi (ör. yerel sunucu). İmza ve zip aynı klasörde aranır;
            // yerel sürümler de imzalı olmak zorundadır.
            string? feed = Environment.GetEnvironmentVariable("VULU_UPDATE_FEED");
            if (!string.IsNullOrWhiteSpace(feed))
            {
                var test = await DownloadVerifiedManifestAsync(http, feed, feed + ".sig", token);
                return new LatestRelease(test, new Uri(new Uri(feed), test.Asset).ToString());
            }

            var release = await http.GetFromJsonAsync<GitHubRelease>(
                $"https://api.github.com/repos/{Repository}/releases/latest", Json, token)
                ?? throw new InvalidDataException("GitHub boş cevap döndürdü.");

            var manifestAsset = release.Assets.FirstOrDefault(a => a.Name == ManifestAssetName)
                ?? throw new InvalidDataException($"{release.TagName} sürümünde {ManifestAssetName} yok.");

            var signatureAsset = release.Assets.FirstOrDefault(a => a.Name == SignatureAssetName)
                ?? throw new ReleaseSignatureException($"{release.TagName} sürümü imzasız; güvenlik için kurulmadı.");

            var manifest = await DownloadVerifiedManifestAsync(http, manifestAsset.DownloadUrl, signatureAsset.DownloadUrl, token);

            var zipAsset = release.Assets.FirstOrDefault(a => a.Name == manifest.Asset)
                ?? throw new InvalidDataException($"{manifest.Asset} release'te bulunamadı.");

            if (string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Sha256))
                throw new InvalidDataException($"{ManifestAssetName} eksik bilgi içeriyor.");

            // Notlar release.json'da yoksa GitHub'daki release açıklaması kullanılır
            if (string.IsNullOrWhiteSpace(manifest.Notes)) manifest.Notes = release.Body ?? "";

            return new LatestRelease(manifest, zipAsset.DownloadUrl);
        }

        /// <summary>release.json'u ham bayt olarak indirir, imzasını doğrular ve ancak ondan sonra ayrıştırır.</summary>
        private static async Task<ReleaseManifest> DownloadVerifiedManifestAsync(HttpClient http, string manifestUrl, string signatureUrl, CancellationToken token)
        {
            byte[] data = await http.GetByteArrayAsync(manifestUrl, token);
            string signature;
            try { signature = await http.GetStringAsync(signatureUrl, token); }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new ReleaseSignatureException("Güncelleme imzası bulunamadı; güvenlik için kurulmadı.");
            }

            if (!VerifySignature(data, signature))
                throw new ReleaseSignatureException("Güncellemenin imzası geçersiz; dosyalar değiştirilmiş olabilir. Güvenlik için kurulmadı.");

            return JsonSerializer.Deserialize<ReleaseManifest>(data, Json)
                ?? throw new InvalidDataException($"{ManifestAssetName} okunamadı.");
        }

        /// <summary>Verinin, gömülü açık anahtara karşılık gelen özel anahtarla imzalandığını doğrular.</summary>
        public static bool VerifySignature(byte[] data, string signatureBase64)
        {
            try
            {
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(ReleasePublicKey), out _);
                return key.VerifyData(data, Convert.FromBase64String(signatureBase64.Trim()), HashAlgorithmName.SHA256);
            }
            catch (FormatException) { return false; }
            catch (CryptographicException) { return false; }
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

    /// <summary>Release imzası eksik ya da geçersiz: güncelleme kurulmaz.</summary>
    public sealed class ReleaseSignatureException(string message) : Exception(message);

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
