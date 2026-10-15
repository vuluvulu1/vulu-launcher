using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace VuluLauncher
{
    public enum UpdateCheckStatus
    {
        UpToDate,
        UpdateAvailable,
        CheckFailed
    }

    /// <summary>
    /// API üzerinden launcher sürüm kontrolünü yapar. İndirme ve kurulum Updater.exe'nin işidir.
    /// </summary>
    public class LauncherUpdateService
    {
        public const string LocalVersion = "0.7";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

        /// <summary>
        /// Güncelleme gerekip gerekmediğini kontrol eder.
        /// Sunucuya ulaşılamazsa CheckFailed döner ("güncel" sayılmaz).
        /// </summary>
        public async Task<(UpdateCheckStatus Status, VersionInfo? Latest)> CheckForUpdateAsync()
        {
            VersionInfo? latest;
            try
            {
                latest = await _http.GetFromJsonAsync<VersionInfo>($"{ApiConfig.BaseUrl}/auth/version");
            }
            catch
            {
                return (UpdateCheckStatus.CheckFailed, null);
            }

            if (latest == null || string.IsNullOrWhiteSpace(latest.Version))
                return (UpdateCheckStatus.CheckFailed, null);

            return IsNewer(latest.Version, LocalVersion)
                ? (UpdateCheckStatus.UpdateAvailable, latest)
                : (UpdateCheckStatus.UpToDate, latest);
        }

        /// <summary>
        /// Sürümler "0.7", "1.2.3" gibi sayısal ise gerçekten büyük mü diye bakar;
        /// ayrıştırılamıyorsa farklı olmasını yeterli sayar.
        /// </summary>
        internal static bool IsNewer(string remote, string local)
        {
            if (Version.TryParse(Normalize(remote), out var r) && Version.TryParse(Normalize(local), out var l))
                return r > l;

            return !string.Equals(remote.Trim(), local.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string version)
        {
            string v = version.Trim().TrimStart('v', 'V');
            return v.Contains('.') ? v : v + ".0";
        }
    }

    public class VersionInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;

        /// <summary>
        /// İndirilecek exe'nin SHA-256'sı (hex). API göndermiyorsa boş kalır ve doğrulama atlanır.
        /// </summary>
        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }
    }
}
