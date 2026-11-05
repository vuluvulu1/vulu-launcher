using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace VuluLauncher.Native
{
    /// <summary>
    /// Görsel yükleyici: Core'un verdiği adresler file://, http(s):// ya da wwwroot'a göre yol ("assets/x.jpg") olabilir.
    /// Aynı adres bir kez yüklenir; büyük görseller istenen genişliğe küçültülerek çözülür (bellek).
    /// </summary>
    internal static class Images
    {
        private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new();
        private static readonly HttpClient Http = CreateHttp();

        private static HttpClient CreateHttp()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("vulu-launcher");
            return http;
        }

        /// <param name="width">Çözülecek en büyük genişlik (piksel); 0 ise asıl boyut.</param>
        public static Task<Bitmap?> LoadAsync(string? url, int width = 0)
        {
            if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<Bitmap?>(null);
            return Cache.GetOrAdd($"{width}|{url}", _ => Task.Run(() => LoadCoreAsync(url, width)));
        }

        /// <summary>Dosyası değişen yerel görselin (arka plan, logo) önbellekteki eski hâli atılır.</summary>
        public static void Forget(string? url)
        {
            if (url == null) return;
            foreach (var key in Cache.Keys.Where(k => k.EndsWith("|" + url, StringComparison.Ordinal)))
                Cache.TryRemove(key, out _);
        }

        private static async Task<Bitmap?> LoadCoreAsync(string url, int width)
        {
            try
            {
                await using var stream = await OpenAsync(url);
                if (stream == null) return null;
                // Bitmap okurken arama yapabilsin diye belleğe alınır (ağ akışı aranamaz)
                var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                memory.Position = 0;
                return width > 0 ? Bitmap.DecodeToWidth(memory, width, BitmapInterpolationMode.HighQuality) : new Bitmap(memory);
            }
            catch (Exception ex)
            {
                LogFile.Write($"[ui] görsel yüklenemedi ({url}): {ex.Message}");
                return null;
            }
        }

        private static async Task<Stream?> OpenAsync(string url)
        {
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode) { response.Dispose(); return null; }
                return await response.Content.ReadAsStreamAsync();
            }
            string path = url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                ? new Uri(url).LocalPath
                : Path.Combine(AppContext.BaseDirectory, "wwwroot", url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.OpenRead(path) : null;
        }
    }
}
