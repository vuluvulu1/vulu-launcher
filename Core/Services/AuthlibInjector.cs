using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace VuluLauncher
{
    /// <summary>Oyunu vulu skin sistemiyle başlatmak için gerekenler (bkz. GameLauncher).</summary>
    /// <param name="AccessToken">vulu-api'nin verdiği oyun oturumu (LAN/sunucuya katılırken doğrulanır).</param>
    /// <param name="ProfileId">Oyuncu kimliği; çevrimdışı kimliğin aynısı, eski dünyalar etkilenmez.</param>
    /// <param name="ApiRoot">vulu-api'nin Yggdrasil kökü (…/yggdrasil).</param>
    /// <param name="Metadata">Kök adresin cevabı; oyuna önceden verilir ki açılışta tekrar sorulmasın.</param>
    /// <param name="AgentJar">authlib-injector'ın yerel yolu.</param>
    public sealed record SkinSession(string AccessToken, string ProfileId, string ApiRoot, string Metadata, string AgentJar);

    /// <summary>
    /// authlib-injector: oyunun skin ve oturum isteklerini Mojang yerine vulu-api'ye yönlendiren Java ajanı.
    /// Launcher'la birlikte dağıtılmaz (AGPL); resmi adresinden indirilir, SHA-256'sı doğrulanır ve önbellekte tutulur.
    /// </summary>
    internal static class AuthlibInjector
    {
        private const string LatestUrl = "https://authlib-injector.yushi.moe/artifact/latest.json";
        private static readonly string Folder = Path.Combine(AppPaths.Base, "authlib-injector");
        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>Kullanılabilir ajan dosyasını döner; indirilemezse ve önbellekte de yoksa null.</summary>
        public static async Task<string?> EnsureAsync(Action<string, string> log)
        {
            await Gate.WaitAsync();
            try
            {
                Artifact? latest = null;
                try
                {
                    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    latest = await Downloader.Http.GetFromJsonAsync<Artifact>(LatestUrl, cancel.Token);
                }
                catch { /* çevrimdışıysa önbellektekiyle devam edilir */ }

                if (latest is { Version.Length: > 0, DownloadUrl.Length: > 0, Checksums.Sha256.Length: 64 }
                    && System.Text.RegularExpressions.Regex.IsMatch(latest.Version, @"^[0-9A-Za-z.\-]+$"))
                {
                    string path = Path.Combine(Folder, $"authlib-injector-{latest.Version}.jar");
                    if (File.Exists(path) && HashOf(path) == latest.Checksums.Sha256.ToLowerInvariant()) return path;

                    try
                    {
                        log($"authlib-injector {latest.Version} indiriliyor...", "accent");
                        await Downloader.DownloadAsync(latest.DownloadUrl, path + ".tmp");
                        if (HashOf(path + ".tmp") != latest.Checksums.Sha256.ToLowerInvariant())
                            throw new InvalidDataException("authlib-injector doğrulanamadı (hash uyuşmuyor).");
                        File.Move(path + ".tmp", path, overwrite: true);
                        foreach (var old in Directory.GetFiles(Folder, "authlib-injector-*.jar").Where(f => !f.Equals(path, StringComparison.OrdinalIgnoreCase)))
                            try { File.Delete(old); } catch { }
                        return path;
                    }
                    catch (Exception ex)
                    {
                        log("authlib-injector indirilemedi: " + ex.Message, "warn");
                    }
                    finally
                    {
                        if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
                    }
                }

                // Son çare: daha önce indirilmiş (ve o zaman doğrulanmış) sürüm
                return Directory.Exists(Folder)
                    ? Directory.GetFiles(Folder, "authlib-injector-*.jar").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                    : null;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static string HashOf(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private sealed class Artifact
        {
            [JsonPropertyName("version")] public string Version { get; set; } = "";
            [JsonPropertyName("download_url")] public string DownloadUrl { get; set; } = "";
            [JsonPropertyName("checksums")] public ArtifactChecksums Checksums { get; set; } = new();
        }

        private sealed class ArtifactChecksums
        {
            [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        }
    }
}
