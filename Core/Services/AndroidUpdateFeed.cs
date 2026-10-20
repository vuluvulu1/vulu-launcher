using System.Net.Http.Json;
using VuluShared;

namespace VuluLauncher
{
    /// <summary>
    /// Android sürümünün güncelleme denetimi. Sitedeki android.json'da kuruludan yeni bir sürüm varsa APK tarayıcıda
    /// indirilir ve Android'in kendi yükleyicisiyle kurulur; yükleyici yalnızca aynı anahtarla imzalanmış APK'yı kabul
    /// eder. APK'nın adresi sabittir, android.json'dan alınmaz.
    /// </summary>
    internal static class AndroidUpdateFeed
    {
        public const string ManifestUrl = "https://vuluvulu.com.tr/releases/android/android.json";
        public const string ApkUrl = "https://vuluvulu.com.tr/releases/android/vulu-launcher.apk";

        public sealed record Manifest(string Version, string? Notes, long Size);

        private static readonly HttpClient Http = Releases.CreateHttpClient(TimeSpan.FromSeconds(15));

        /// <summary>Yeni sürüm varsa onu, yoksa null; sunucuya ulaşılamazsa hata fırlatır.</summary>
        public static async Task<Manifest?> CheckAsync()
        {
            var manifest = await Http.GetFromJsonAsync<Manifest>(ManifestUrl, Releases.Json)
                ?? throw new InvalidDataException("android.json okunamadı.");
            return Releases.IsNewer(manifest.Version, LauncherUpdateService.LocalVersion) ? manifest : null;
        }
    }
}
