using System.Security.Cryptography;

namespace VuluLauncher
{
    /// <summary>Launcher'ın dış servislere (Modrinth, Adoptium) yaptığı indirmeler için ortak yardımcılar.</summary>
    internal static class Downloader
    {
        public static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            // vulu sunucusuna giden istekler (paket dosyaları, ping) yedek adreslerle de bağlanabilir; diğer siteler etkilenmez
            var client = VuluShared.ServerConnection.CreateClient(TimeSpan.FromMinutes(10));
            // Modrinth, kimliği belli bir User-Agent ister
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"vulu-launcher/{LauncherUpdateService.LocalVersion} (+https://vuluvulu.com.tr)");
            return client;
        }

        /// <summary>Yalnızca geliştirme derlemesinde, yerel bir test API'si seçilmişken: bu bilgisayardaki test sunucusu.</summary>
        private static bool IsLocalTestServer(Uri uri) =>
#if DEBUG
            uri.IsLoopback && Environment.GetEnvironmentVariable("VULU_API_URL") != null;
#else
            false;
#endif

        /// <summary>
        /// Dosyayı önce ".part" olarak indirir, verilen hash'leri doğrular, sonra hedefe taşır.
        /// Yalnızca https adresleri kabul edilir.
        /// </summary>
        public static async Task DownloadAsync(string url, string path, IProgress<long>? bytesRead = null,
            string? sha1 = null, string? sha512 = null, CancellationToken cancellation = default, Action<long>? contentLength = null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !(uri.Scheme == Uri.UriSchemeHttps || IsLocalTestServer(uri)))
                throw new InvalidOperationException($"Geçersiz indirme adresi: {url}");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string partPath = path + ".part";

            // Daha önce indirilmiş aynı mod dosyası (hash'iyle) yeniden indirilmez
            string? cacheKey = ModCache.KeyFor(uri, sha1, sha512);
            if (cacheKey != null && ModCache.TryPlace(cacheKey, path))
            {
                long size = new FileInfo(path).Length;
                contentLength?.Invoke(size);
                bytesRead?.Report(size);
                return;
            }

            // İndirme panelindeki iş: iptal edilirse durur, duraklatılırsa parça arasında bekler
            var job = DownloadJob.Current;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, job?.Token ?? CancellationToken.None);
            var token = linked.Token;

            try
            {
                // Geçici ağ hataları (bağlantı koptu, sunucu 5xx) birkaç kez yeniden denenir
                for (int attempt = 1; ; attempt++)
                {
                    long written = 0;
                    try
                    {
                        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength is long length) contentLength?.Invoke(length);
                        using var stream = await response.Content.ReadAsStreamAsync(token);
                        using var file = File.Create(partPath);

                        byte[] buffer = new byte[81920];
                        int read;
                        while ((read = await stream.ReadAsync(buffer, token)) > 0)
                        {
                            await file.WriteAsync(buffer.AsMemory(0, read), token);
                            written += read;
                            bytesRead?.Report(read);
                            if (job != null) await job.WaitIfPausedAsync(token);
                        }
                        break;
                    }
                    catch (Exception ex) when (attempt < 3 && !token.IsCancellationRequested && IsTransient(ex))
                    {
                        bytesRead?.Report(-written);   // yarım kalan denemenin baytları geri alınır
                        await Task.Delay(TimeSpan.FromSeconds(attempt * 2), token);
                    }
                }

                if (!string.IsNullOrEmpty(sha512)) Verify(partPath, sha512, SHA512.HashData);
                else if (!string.IsNullOrEmpty(sha1)) Verify(partPath, sha1, SHA1.HashData);

                File.Move(partPath, path, overwrite: true);
                if (cacheKey != null) ModCache.Remember(cacheKey, path);
            }
            finally
            {
                if (File.Exists(partPath)) File.Delete(partPath);
            }
        }

        /// <summary>Yeniden denemeye değer hata: ağ ya da sunucu kaynaklı (4xx değil), zaman aşımı.</summary>
        private static bool IsTransient(Exception ex) => ex switch
        {
            HttpRequestException http => http.StatusCode is null || (int)http.StatusCode >= 500,
            IOException => true,
            TaskCanceledException => true,   // HttpClient zaman aşımı (iptal edildiyse yukarıda elenir)
            _ => false,
        };

        private static void Verify(string path, string expected, Func<Stream, byte[]> hash)
        {
            using var stream = File.OpenRead(path);
            if (!string.Equals(Convert.ToHexString(hash(stream)), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Dosya doğrulanamadı (hash uyuşmuyor): {Path.GetFileName(path)}");
        }

        /// <summary>
        /// Göreli yolu kök klasörün içindeki tam yola çevirir; kökün dışına çıkıyorsa null döner.
        /// Modpack ve zip içeriklerindeki yollar buradan geçer.
        /// </summary>
        /// <summary>Dosyanın SHA-1'i (küçük harf onaltılık); Modrinth'te dosyadan sürüm bulmak için.</summary>
        public static string Sha1Of(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(stream)).ToLowerInvariant();
        }

        public static string? ResolveInside(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;

            string normalized = relativePath.Replace('\\', '/');
            if (Path.IsPathRooted(normalized) || normalized.Contains(':')) return null;

            string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(rootFull, normalized));
            return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) && full.Length > rootFull.Length ? full : null;
        }
    }

    /// <summary>İlerlemeyi çağıran thread'de bildirir; isteğe bağlı olarak en çok belirli aralıkla.</summary>
    internal sealed class ThrottledProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        private readonly long _intervalTicks;
        private long _last;

        public ThrottledProgress(Action<T> report, int intervalMs = 120)
        {
            _report = report;
            _intervalTicks = TimeSpan.FromMilliseconds(intervalMs).Ticks;
        }

        public void Report(T value)
        {
            long now = DateTime.UtcNow.Ticks;
            long last = Interlocked.Read(ref _last);
            if (now - last < _intervalTicks) return;
            if (Interlocked.CompareExchange(ref _last, now, last) == last) _report(value);
        }
    }
}
