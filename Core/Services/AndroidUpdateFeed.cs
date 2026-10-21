using System.Net.Http.Json;
using System.Security.Cryptography;
using VuluShared;

namespace VuluLauncher
{
    /// <summary>
    /// Android sürümünün güncellemesi. Sitedeki android.json'da kuruludan yeni bir sürüm varsa APK uygulamanın içinde
    /// indirilir, SHA-256'sı android.json'dakiyle karşılaştırılır ve Android'in yükleyicisine verilir. Yükleyici yalnızca
    /// aynı anahtarla imzalanmış APK'yı güncelleme olarak kabul eder. APK'nın adresi sabittir, android.json'dan alınmaz.
    /// </summary>
    internal static class AndroidUpdateFeed
    {
        public const string ManifestUrl = "https://vuluvulu.com.tr/releases/android/android.json";
        public const string ApkUrl = "https://vuluvulu.com.tr/releases/android/vulu-launcher.apk";

        public sealed record Manifest(string Version, string? Notes, long Size, string? Sha256);

        private static readonly HttpClient Http = Releases.CreateHttpClient(TimeSpan.FromSeconds(15));
        private static readonly HttpClient Download = Releases.CreateHttpClient(TimeSpan.FromMinutes(15));

        private static string Folder => Path.Combine(AppPaths.AppData, "updates");

        /// <summary>Yeni sürüm varsa onu, yoksa null; sunucuya ulaşılamazsa hata fırlatır.</summary>
        public static async Task<Manifest?> CheckAsync()
        {
            var manifest = await Http.GetFromJsonAsync<Manifest>(ManifestUrl, Releases.Json)
                ?? throw new InvalidDataException("android.json okunamadı.");
            return Releases.IsNewer(manifest.Version, LauncherUpdateService.LocalVersion) ? manifest : null;
        }

        /// <summary>
        /// APK'yı indirir ve doğrular; aynı sürüm daha önce indirildiyse (kurulum ertelendiyse) yeniden indirmez.
        /// Eski sürümlerin dosyaları silinir. İlerleme 0–100 arası bildirilir.
        /// </summary>
        public static async Task<string> DownloadAsync(Manifest manifest, Action<int> progress)
        {
            if (string.IsNullOrEmpty(manifest.Sha256))
                throw new InvalidDataException("Güncellemenin doğrulama bilgisi eksik; indirilmedi.");

            Directory.CreateDirectory(Folder);
            string target = Path.Combine(Folder, $"vulu-launcher-{Releases.Clean(manifest.Version)}.apk");
            foreach (var old in Directory.GetFiles(Folder).Where(f => f != target))
                try { File.Delete(old); } catch { }

            if (File.Exists(target) && Releases.Sha256Of(target) == manifest.Sha256.ToLowerInvariant())
            {
                progress(100);
                return target;
            }

            string partial = target + ".part";
            using (var response = await Download.GetAsync(ApkUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? manifest.Size;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(partial);
                var buffer = new byte[81920];
                long read = 0;
                int lastPercent = -1, count;
                while ((count = await source.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, count));
                    hash.AppendData(buffer, 0, count);
                    read += count;
                    int percent = total > 0 ? (int)(read * 100 / total) : 0;
                    if (percent != lastPercent) { progress(Math.Min(percent, 99)); lastPercent = percent; }
                }
                string sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (sha != manifest.Sha256.ToLowerInvariant())
                {
                    file.Close();
                    File.Delete(partial);
                    throw new InvalidDataException("İndirilen güncelleme doğrulanamadı (dosya bozuk ya da değiştirilmiş); kurulmadı.");
                }
            }
            File.Move(partial, target, overwrite: true);
            progress(100);
            return target;
        }
    }
}
