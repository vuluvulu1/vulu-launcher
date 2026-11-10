using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace VuluMobile.Services
{
    /// <summary>
    /// Güncelleme denetimi. GitHub'daki derleme her seferinde APK'nın yanına social.json koyar:
    /// { versionCode, version, sha256, url }. Kurulu sürümden yeniyse APK'nın indirme bağlantısı tarayıcıda açılır;
    /// kişi inen dosyaya dokunup kurar. Uygulama APK'yı kendisi kurmaz, bu yüzden "uygulama yükleme" izni istemez.
    /// Android aynı anahtarla imzalanmamış APK'yı zaten güncelleme olarak kabul etmez.
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

        /// <summary>APK'nın indirme bağlantısını tarayıcıda açar. Hata varsa nedeni, açıldıysa null.</summary>
        public string? OpenDownload()
        {
            var platform = AppPlatform.Current;
            var feed = Available;
            if (platform == null || feed == null) return "Güncelleme yok.";
            try { platform.OpenUrl(feed.Url); return null; }
            catch (Exception ex) { return "Tarayıcı açılamadı: " + ex.Message; }
        }
    }
}
