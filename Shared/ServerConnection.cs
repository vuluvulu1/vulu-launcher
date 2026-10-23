using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace VuluShared
{
    /// <summary>
    /// vulu sunucusuna (vuluvulu.com.tr) giden bağlantılar için yedekli bağlanma. Alan adı çözülemezse ya da çözülen
    /// adrese bağlanılamazsa sırayla şunlar denenir: en son çalışan adres, DNS-over-HTTPS (Cloudflare, Google, Quad9)
    /// ile bulunan adresler ve sabit sunucu IP'si.
    ///
    /// Yalnızca TCP bağlantısının gittiği IP değişir: TLS yine vuluvulu.com.tr adına kurulur ve sertifika doğrulanır,
    /// yani yedek yol da asıl sunucu kadar güvenlidir. Diğer sitelere (Modrinth, CurseForge...) giden istekler etkilenmez.
    /// </summary>
    public static class ServerConnection
    {
        public const string Host = "vuluvulu.com.tr";

        /// <summary>Alan adına hiç ulaşılamazsa kullanılan sabit adresler (VDS).</summary>
        public static readonly IPAddress[] StaticAddresses = { IPAddress.Parse("185.157.46.169") };

        /// <summary>
        /// IP adresiyle çağrılan DoH uçları (JSON biçimi): kendileri de DNS gerektirmez. Sertifikaları bu IP'ler için geçerlidir.
        /// </summary>
        private static readonly string[] DohResolvers =
        {
            "https://1.1.1.1/dns-query?name={0}&type=A",
            "https://8.8.8.8/resolve?name={0}&type=A",
            "https://9.9.9.9:5053/dns-query?name={0}&type=A",
        };

        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(6);
        /// <summary>Sistem DNS'i/doğrudan bağlantı bir kez başarısız olunca bu süre boyunca hep yedek yol kullanılır.</summary>
        private static readonly TimeSpan FallbackPeriod = TimeSpan.FromMinutes(10);

        private static readonly object Gate = new();
        private static DateTime _fallbackUntil;
        private static IPAddress? _lastGood;
        private static IPAddress[]? _doh;
        private static DateTime _dohAt;

        static ServerConnection()
        {
            // Önceki açılışta yedek yol gerektiyse doğrudan bağlantı hemen denenmez (açılış beklemesin); süre dolunca yeniden denenir
            _lastGood = LoadLastGood();
            if (_lastGood != null) _fallbackUntil = DateTime.UtcNow + FallbackPeriod;
        }

        /// <summary>Şu an yedek yol kullanılıyor mu (arayüzde bilgi vermek için).</summary>
        public static bool UsingFallback { get { lock (Gate) return DateTime.UtcNow < _fallbackUntil; } }

        /// <summary>
        /// Önceki açılışta yedek yol gerektiyse o sırada çalışan adres; gerekmediyse null. Windows'ta arayüz (WebView2)
        /// resimleri de alan adını çözmeden bu adresten yükler.
        /// </summary>
        public static IPAddress? FallbackAddress { get { lock (Gate) return _lastGood; } }

        /// <summary>Sunucuyla konuşan bütün HttpClient'lar bunu kullanır.</summary>
        public static HttpMessageHandler CreateHandler() => new RetryHandler(new SocketsHttpHandler
        {
            ConnectCallback = ConnectAsync,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        });

        public static HttpClient CreateClient(TimeSpan timeout) => new(CreateHandler()) { Timeout = timeout };

        private static bool IsServer(string host) => string.Equals(host, Host, StringComparison.OrdinalIgnoreCase);

        private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
        {
            var target = context.DnsEndPoint;
            if (!IsServer(target.Host)) return await ConnectToAsync(target, token);

            if (!UsingFallback)
            {
#if DEBUG
                // Yalnızca geliştirme derlemesinde: "fail" doğrudan bağlantıyı engeller (DNS engeli), bir IP ise
                // doğrudan bağlantı o adrese gider (sahte DNS cevabı)
                string? test = Environment.GetEnvironmentVariable("VULU_TEST_DIRECT");
                if (test == "fail") { StartFallback(new SocketException((int)SocketError.HostNotFound)); goto fallback; }
                if (test != null && IPAddress.TryParse(test, out var fake))
                    return await ConnectToAsync(new IPEndPoint(fake, target.Port), token);
#endif
                try { return await ConnectToAsync(target, token); }
                catch (Exception e) when (!token.IsCancellationRequested) { StartFallback(e); }
            }

#if DEBUG
        fallback:
#endif
            Exception? last = null;
            foreach (var address in await CandidatesAsync(token))
            {
                try
                {
                    var stream = await ConnectToAsync(new IPEndPoint(address, target.Port), token);
                    Remember(address);
                    return stream;
                }
                catch (Exception e) when (!token.IsCancellationRequested) { last = e; }
            }
            throw new HttpRequestException($"{Host} sunucusuna ulaşılamadı (yedek adresler de denendi).", last);
        }

        /// <summary>Denenecek adresler, sırayla: en son çalışan, DNS-over-HTTPS ile bulunanlar, sabit IP'ler.</summary>
        private static async Task<List<IPAddress>> CandidatesAsync(CancellationToken token)
        {
            var list = new List<IPAddress>();
            void Add(IPAddress a) { if (!list.Contains(a)) list.Add(a); }
            lock (Gate) if (_lastGood != null) Add(_lastGood);
            foreach (var a in await ResolveOverHttpsAsync(token)) Add(a);
            foreach (var a in StaticAddresses) Add(a);
            return list;
        }

        private static async Task<IPAddress[]> ResolveOverHttpsAsync(CancellationToken token)
        {
            lock (Gate) if (_doh != null && DateTime.UtcNow - _dohAt < TimeSpan.FromMinutes(30)) return _doh;

            // DoH istekleri bu sınıfın bağlantı yolunu kullanmaz (IP'ye gider), sonsuz döngü olmaz
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
            foreach (var resolver in DohResolvers)
            {
                try
                {
                    using var response = await http.GetAsync(string.Format(resolver, Host), token);
                    if (!response.IsSuccessStatusCode) continue;
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                    if (!doc.RootElement.TryGetProperty("Answer", out var answers)) continue;
                    var found = answers.EnumerateArray()
                        .Where(a => a.TryGetProperty("type", out var t) && t.GetInt32() == 1)
                        .Select(a => IPAddress.TryParse(a.GetProperty("data").GetString(), out var ip) ? ip : null)
                        .OfType<IPAddress>().ToArray();
                    if (found.Length == 0) continue;
                    lock (Gate) { _doh = found; _dohAt = DateTime.UtcNow; }
                    return found;
                }
                catch (Exception) when (!token.IsCancellationRequested) { }
            }
            return Array.Empty<IPAddress>();
        }

        private static async Task<Stream> ConnectToAsync(EndPoint endPoint, CancellationToken token)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(ConnectTimeout);
            try
            {
                await socket.ConnectAsync(endPoint, timeout.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static void StartFallback(Exception reason)
        {
            lock (Gate) _fallbackUntil = DateTime.UtcNow + FallbackPeriod;
            Log?.Invoke($"{Host} doğrudan ulaşılamadı ({reason.GetBaseException().Message}); yedek adresler kullanılıyor.");
        }

        /// <summary>Bağlantı hatalarını launcher'ın günlüğüne yazmak için (isteğe bağlı).</summary>
        public static Action<string>? Log { get; set; }

        // ── Yedek yolda çalışan adres diskte saklanır: bir sonraki açılışta DNS'siz de hemen bağlanılsın ──────
        // Yalnızca güvenilir kaynaktan (DoH, sabit IP) gelen adresler saklanır; sistem DNS'inin verdiği adres sahte olabilir.
        // Doğrudan bağlantı yeniden çalışınca dosya silinir.
        private static string CachePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vuluLauncher", "server-address.txt");

        private static IPAddress? LoadLastGood()
        {
            try { return File.Exists(CachePath) && IPAddress.TryParse(File.ReadAllText(CachePath).Trim(), out var ip) ? ip : null; }
            catch { return null; }
        }

        private static void Forget()
        {
            lock (Gate)
            {
                if (_lastGood == null) return;
                _lastGood = null;
            }
            try { File.Delete(CachePath); } catch { }
        }

        private static void Remember(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            lock (Gate)
            {
                if (address.Equals(_lastGood)) return;
                _lastGood = address;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                File.WriteAllText(CachePath, address.ToString());
            }
            catch { }
        }

        /// <summary>
        /// TCP bağlantısı kurulup TLS ya da yanıt aşamasında kopan istekler (ör. DNS sahte bir adres döndürdüyse)
        /// yedek yola geçilip bir kez daha denenir. Gövdesi tekrar gönderilemeyen (akış) istekler denenmez.
        /// </summary>
        private sealed class RetryHandler : DelegatingHandler
        {
            public RetryHandler(HttpMessageHandler inner) : base(inner) { }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                bool server = request.RequestUri != null && IsServer(request.RequestUri.Host);
                bool direct = server && !UsingFallback;
                // Gövdesi bellekte olan (ya da hiç olmayan) istekler tekrar gönderilebilir
                bool retryable = server && request.Content is null or ByteArrayContent or System.Net.Http.Json.JsonContent;
                if (retryable && request.Content != null) await request.Content.LoadIntoBufferAsync();
                try
                {
                    var response = await base.SendAsync(request, token);
                    if (direct && !UsingFallback) Forget();   // doğrudan yol yine çalışıyor
                    return response;
                }
                catch (HttpRequestException e) when (retryable && !token.IsCancellationRequested)
                {
                    // Doğrudan yol çalışmadıysa yedeğe geçilir; yedekte saklanan adres bozulduysa (sunucu taşındı) unutulur
                    // ve DoH/sabit IP ile yeniden denenir
                    if (direct) StartFallback(e);
                    else Forget();
                    return await base.SendAsync(request, token);
                }
            }
        }
    }
}
