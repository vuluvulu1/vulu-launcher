using System.Security.Cryptography;

namespace VuluLauncher
{
    /// <summary>Launcher'ın dış servislere (Modrinth, Adoptium) yaptığı indirmeler için ortak yardımcılar.</summary>
    internal static class Downloader
    {
        public static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            // Modrinth, kimliği belli bir User-Agent ister
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"vulu-launcher/{LauncherUpdateService.LocalVersion} (+https://github.com/vuluvulu1/vulu-launcher)");
            return client;
        }

        /// <summary>
        /// Dosyayı önce ".part" olarak indirir, verilen hash'leri doğrular, sonra hedefe taşır.
        /// Yalnızca https adresleri kabul edilir.
        /// </summary>
        public static async Task DownloadAsync(string url, string path, IProgress<long>? bytesRead = null,
            string? sha1 = null, string? sha512 = null, CancellationToken cancellation = default, Action<long>? contentLength = null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException($"Geçersiz indirme adresi: {url}");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string partPath = path + ".part";

            try
            {
                using (var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is long length) contentLength?.Invoke(length);
                    using var stream = await response.Content.ReadAsStreamAsync(cancellation);
                    using var file = File.Create(partPath);

                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, cancellation)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
                        bytesRead?.Report(read);
                    }
                }

                if (!string.IsNullOrEmpty(sha512)) Verify(partPath, sha512, SHA512.HashData);
                else if (!string.IsNullOrEmpty(sha1)) Verify(partPath, sha1, SHA1.HashData);

                File.Move(partPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(partPath)) File.Delete(partPath);
            }
        }

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
