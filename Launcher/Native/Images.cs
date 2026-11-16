using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace VuluLauncher.Native
{
    /// <summary>
    /// Görsel yükleyici: Core'un verdiği adresler file://, http(s):// ya da uygulama klasörüne göre yol ("assets/x.jpg") olabilir.
    /// Aynı adres bir kez yüklenir; büyük görseller istenen genişliğe küçültülerek çözülür (bellek).
    /// </summary>
    internal static class Images
    {
        private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new();
        private static readonly HttpClient Http = CreateHttp();

        private static HttpClient CreateHttp()
        {
            // vulu sunucusundaki resimler (profil, banner, paket kapakları) da yedek yollarla gelsin
            var http = VuluShared.ServerConnection.CreateClient(TimeSpan.FromSeconds(30));
            http.DefaultRequestHeaders.UserAgent.ParseAdd("vulu-launcher");
            return http;
        }

        /// <param name="width">Çözülecek en büyük genişlik (piksel); 0 ise asıl boyut.</param>
        public static Task<Bitmap?> LoadAsync(string? url, int width = 0)
        {
            if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<Bitmap?>(null);
            return Cache.GetOrAdd($"{width}|{url}", _ => Task.Run(() => LoadCoreAsync(url, width)));
        }

        /// <summary>
        /// Görselin önceden bulanıklaştırılmış küçük kopyası (Meşale'de Oyna dışı sayfaların zemini). Eskiden tam boy resim
        /// canlı BlurEffect ile çiziliyordu: kaydırma, hover, sayfa geçişi gibi her yeniden çizimde bulanıklık baştan
        /// hesaplanıyordu. Şimdi bir kez, arka planda, küçük boyutta hesaplanır.
        /// </summary>
        public static Task<Bitmap?> LoadBlurredAsync(string? url, int width = 640, float sigma = 5f)
        {
            if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<Bitmap?>(null);
            return Cache.GetOrAdd($"blur{width}:{sigma}|{url}", _ => Task.Run(async () =>
            {
                try
                {
                    var bytes = await LoadBytesAsync(url);
                    if (bytes == null) return null;
                    using var original = SkiaSharp.SKBitmap.Decode(bytes);
                    if (original == null) return null;
                    double scale = Math.Min(1, (double)width / original.Width);
                    int w = Math.Max(1, (int)(original.Width * scale)), h = Math.Max(1, (int)(original.Height * scale));
                    using var small = original.Resize(new SkiaSharp.SKImageInfo(w, h), new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.Linear));
                    using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(w, h));
                    using var paint = new SkiaSharp.SKPaint { ImageFilter = SkiaSharp.SKImageFilter.CreateBlur(sigma, sigma, SkiaSharp.SKShaderTileMode.Clamp) };
                    surface.Canvas.DrawBitmap(small, 0, 0, paint);
                    using var image = surface.Snapshot();
                    using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                    return new Bitmap(data.AsStream());
                }
                catch (Exception ex)
                {
                    LogFile.Write($"[ui] bulanık zemin hazırlanamadı ({url}): {ex.Message}");
                    return null;
                }
            }));
        }

        /// <summary>Görselin baytları (skin gibi önce işlenecek resimler için); alınamazsa null.</summary>
        public static async Task<byte[]?> LoadBytesAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            try
            {
                await using var stream = await OpenAsync(url);
                if (stream == null) return null;
                var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                return memory.ToArray();
            }
            catch { return null; }
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
                var bitmap = new Bitmap(memory);
                if (width <= 0 || bitmap.PixelSize.Width <= width) return bitmap;   // küçük resim büyütülmez
                memory.Position = 0;
                bitmap.Dispose();
                return Bitmap.DecodeToWidth(memory, width, BitmapInterpolationMode.HighQuality);
            }
            catch (Exception ex)
            {
                LogFile.Write($"[ui] görsel yüklenemedi ({url}): {ex.Message}");
                return null;
            }
        }

        private static async Task<Stream?> OpenAsync(string url)
        {
            // Küçük gömülü görseller (dünya simgeleri): data:image/png;base64,...
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = url.IndexOf(',');
                return comma < 0 ? null : new MemoryStream(Convert.FromBase64String(url[(comma + 1)..]));
            }
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode) { response.Dispose(); return null; }
                return await response.Content.ReadAsStreamAsync();
            }
            string path = url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                ? new Uri(url).LocalPath
                : Path.Combine(AppContext.BaseDirectory, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.OpenRead(path) : null;
        }
    }
}
