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
        private static readonly ConcurrentDictionary<string, Entry> Cache = new();

        /// <summary>Önbellekteki görsel: en son ne zaman istendiği ve çözülmüş hâlinin kapladığı bellek.</summary>
        private sealed class Entry(Task<Bitmap?> task)
        {
            public readonly Task<Bitmap?> Task = task;
            public long LastUse = Environment.TickCount64;
            public long Bytes;
        }

        /// <summary>
        /// Önbellek bütçesi (çözülmüş piksel). Aşılınca en uzun süredir istenmeyenler önbellekten çıkar; ekranda hâlâ
        /// gösterilen görseli onu tutan sayfa tutmaya devam eder, yalnızca kimsenin tutmadığı eski görseller boşalır
        /// (önceden bir oturumda görülen her kapak, galeri resmi ve akış görseli launcher kapanana kadar bellekte kalıyordu).
        /// </summary>
        private const long Budget = 48L * 1024 * 1024;
        /// <summary>Genişlik verilmeyen görseller de en fazla bu genişlikte çözülür (ör. 4K ekran görüntüsü).</summary>
        private const int MaxWidth = 2560;
        private static long _total;
        private static int _evicting;

        private static Task<Bitmap?> Get(string key, Func<Task<Bitmap?>> load)
        {
            if (Cache.TryGetValue(key, out var hit)) { hit.LastUse = Environment.TickCount64; return hit.Task; }
            var entry = Cache.GetOrAdd(key, _ => new Entry(System.Threading.Tasks.Task.Run(load)));
            entry.LastUse = Environment.TickCount64;
            _ = entry.Task.ContinueWith(t =>
            {
                if (t.Result is not { } bmp || entry.Bytes != 0 || !Cache.TryGetValue(key, out var current) || current != entry) return;
                entry.Bytes = (long)bmp.PixelSize.Width * bmp.PixelSize.Height * 4;
                if (Interlocked.Add(ref _total, entry.Bytes) > Budget) Evict();
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            return entry.Task;
        }

        private static void Evict()
        {
            if (Interlocked.Exchange(ref _evicting, 1) == 1) return;
            try
            {
                foreach (var (key, entry) in Cache.Where(kv => kv.Value.Task.IsCompleted).OrderBy(kv => kv.Value.LastUse).ToList())
                {
                    if (Interlocked.Read(ref _total) <= Budget * 3 / 4) break;
                    if (Cache.TryRemove(key, out var gone)) Interlocked.Add(ref _total, -gone.Bytes);
                }
            }
            finally { Volatile.Write(ref _evicting, 0); }
        }
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
            if (width <= 0 || width > MaxWidth) width = MaxWidth;
            return Get($"{width}|{url}", () => LoadCoreAsync(url, width));
        }

        /// <summary>
        /// Görselin önceden bulanıklaştırılmış küçük kopyası (Meşale'de Oyna dışı sayfaların zemini). Eskiden tam boy resim
        /// canlı BlurEffect ile çiziliyordu: kaydırma, hover, sayfa geçişi gibi her yeniden çizimde bulanıklık baştan
        /// hesaplanıyordu. Şimdi bir kez, arka planda, küçük boyutta hesaplanır.
        /// </summary>
        public static Task<Bitmap?> LoadBlurredAsync(string? url, int width = 640, float sigma = 5f)
        {
            if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<Bitmap?>(null);
            return Get($"blur{width}:{sigma}|{url}", async () =>
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
            });
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
                if (Cache.TryRemove(key, out var gone)) Interlocked.Add(ref _total, -gone.Bytes);
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
