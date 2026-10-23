using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VuluShared
{
    /// <summary>
    /// Launcher ve Updater'ın ortak kullandığı sürüm bilgisi. En son sürüm vulu sunucusunda <see cref="FeedUrl"/>
    /// klasöründe durur: tüm launcher klasörünün zip'i, onu anlatan release.json ve release.json'un imzası.
    ///
    /// release.json zip'in ve içindeki her dosyanın SHA-256'sını taşır; imzası doğrulanınca bütün paket doğrulanmış
    /// olur. İmzalayan özel anahtar yalnızca release çıkaran bilgisayardadır (tools/ReleaseSigner), bu yüzden sunucuya
    /// erişen biri tek başına kullanıcılara sahte güncelleme kuramaz.
    /// </summary>
    public static class Releases
    {
        /// <summary>En son sürümün release.json'u; imza (.sig) ve zip aynı klasördedir.</summary>
        public const string FeedUrl = "https://vuluvulu.com.tr/releases/release.json";

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
            var http = ServerConnection.CreateClient(timeout);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("vulu-launcher");
            return http;
        }

        /// <summary>
        /// En son yayınlanan sürümün imzası doğrulanmış manifest'ini ve zip adresini getirir.
        /// Ulaşılamazsa ya da imza geçersizse hata fırlatır.
        /// </summary>
        public static async Task<LatestRelease> GetLatestAsync(HttpClient http, CancellationToken token = default)
        {
            // Test için başka bir release.json adresi verilebilir (ör. yerel sunucu); yerel sürümler de imzalı olmak zorundadır.
            string? feed = Environment.GetEnvironmentVariable("VULU_UPDATE_FEED");
            if (string.IsNullOrWhiteSpace(feed)) feed = FeedUrl;

            var manifest = await DownloadVerifiedManifestAsync(http, feed, feed + ".sig", token);

            if (string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Sha256) || string.IsNullOrWhiteSpace(manifest.Asset))
                throw new InvalidDataException("release.json eksik bilgi içeriyor.");

            return new LatestRelease(manifest, new Uri(new Uri(feed), manifest.Asset).ToString());
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
                ?? throw new InvalidDataException("release.json okunamadı.");
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

    /// <summary>release.json (sunucuda) ve install.json (kurulu klasörde) aynı biçimdedir.</summary>
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
}
