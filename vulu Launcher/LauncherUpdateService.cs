using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AGLR_Launcher
{
    /// <summary>
    /// API üzerinden launcher sürüm kontrolü ve güncelleme indirme işlemlerini yönetir.
    /// MongoDB bağlantısı yoktur — tüm işlemler vulu-api üzerinden yapılır.
    /// </summary>
    public class LauncherUpdateService
    {
        private const string LocalVersion = "0.8";
        private const string ApiBase = "https://vulu-api.onrender.com"; // Deploy sonrası Render URL'si

        private static readonly HttpClient _http = new();

        /// <summary>
        /// API'den en güncel sürümü getirir.
        /// </summary>
        public async Task<VersionInfo?> GetLatestVersionAsync()
        {
            try
            {
                return await _http.GetFromJsonAsync<VersionInfo>($"{ApiBase}/auth/version");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Güncelleme gerekip gerekmediğini kontrol eder.
        /// </summary>
        public async Task<(bool UpdateAvailable, VersionInfo? Latest)> CheckForUpdateAsync()
        {
            var latest = await GetLatestVersionAsync();

            if (latest == null)
                return (false, null);

            bool updateAvailable = latest.Version != LocalVersion;
            return (updateAvailable, latest);
        }

        /// <summary>
        /// Güncellemeyi arka planda indirir, ilerlemeyi raporlar.
        /// </summary>
        public async Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<int>? progress = null)
        {
            string path = Path.Combine(Application.StartupPath, "update.zip");

#pragma warning disable SYSLIB0014
            using var client = new System.Net.WebClient();
            if (progress != null)
                client.DownloadProgressChanged += (s, e) => progress.Report(e.ProgressPercentage);
            await client.DownloadFileTaskAsync(downloadUrl, path);
#pragma warning restore SYSLIB0014

            return path;
        }
    }

    public class VersionInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;
    }
}