using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace VuluMobile.Services
{
    /// <summary>
    /// Uygulama içi güncelleme. GitHub'daki derleme her seferinde APK'nın yanına social.json koyar:
    /// { versionCode, version, sha256, url }. Kurulu sürümden yeniyse APK indirilir, SHA-256'sı denetlenir ve
    /// Android'in yükleyicisine verilir. Android aynı anahtarla imzalanmamış APK'yı zaten güncelleme olarak kabul etmez.
    /// </summary>
    internal sealed class AppUpdate
    {
        private const string FeedUrl = "https://github.com/vuluvulu1/vulu-launcher/releases/download/social-test/social.json";
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

        internal sealed record Feed(
            [property: JsonPropertyName("versionCode")] int VersionCode,
            [property: JsonPropertyName("version")] string Version,
            [property: JsonPropertyName("sha256")] string Sha256,
            [property: JsonPropertyName("url")] string Url);

        public Feed? Available { get; private set; }

        /// <summary>Yeni sürüm varsa döner; yoksa, ağ hatasında ya da telefon dışında null.</summary>
        public async Task<Feed?> CheckAsync()
        {
            var platform = AppPlatform.Current;
            if (platform == null) return null;
            try
            {
                var feed = await Http.GetFromJsonAsync<Feed>(FeedUrl + "?t=" + DateTime.UtcNow.Ticks);
                // Yalnızca GitHub'daki kendi sürüm adresimizden indirilir
                Available = feed != null && feed.VersionCode > platform.VersionCode
                    && feed.Url.StartsWith("https://github.com/vuluvulu1/vulu-launcher/releases/download/", StringComparison.Ordinal)
                    && feed.Sha256.Length == 64 ? feed : null;
            }
            catch { Available = null; }
            return Available;
        }

        /// <summary>İndirir, denetler, kurdurur. Hata varsa nedeni, kurulum başladıysa null.</summary>
        public async Task<string?> InstallAsync(IProgress<double>? progress = null)
        {
            var platform = AppPlatform.Current;
            var feed = Available;
            if (platform == null || feed == null) return "Güncelleme yok.";
            if (!platform.CanInstallPackages)
            {
                platform.OpenInstallSettings();
                return "Önce vulu Sosyal'a uygulama yükleme izni ver, sonra tekrar dene.";
            }
            string path = Path.Combine(platform.CacheDirectory, "vulu-sosyal-update.apk");
            try
            {
                using (var response = await Http.GetAsync(feed.Url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? 0;
                    await using var source = await response.Content.ReadAsStreamAsync();
                    await using var file = File.Create(path);
                    var buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read));
                        done += read;
                        if (total > 0) progress?.Report((double)done / total);
                    }
                }
                string hash;
                await using (var check = File.OpenRead(path)) hash = Convert.ToHexString(await SHA256.HashDataAsync(check));
                if (!hash.Equals(feed.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                    return "İndirilen dosya bozuk (SHA-256 tutmadı). Tekrar dene.";
                }
                return await platform.InstallAsync(path);
            }
            catch (Exception ex) { return "İndirilemedi: " + ex.Message; }
        }
    }
}
