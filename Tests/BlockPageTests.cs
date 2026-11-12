using System.Net;
using System.Text;
using VuluShared;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>
    /// Bazı ağlar (okul, iş yeri) bağlantıyı kesmek yerine kendi "engellendi" sayfasını döndürür. API JSON yerine HTML
    /// döndürünce istek Cloudflare Worker yedeğiyle yinelenmeli; yedek de engelliyse kullanıcı anlaşılır bir mesaj görmeli.
    /// </summary>
    public class BlockPageTests
    {
        /// <summary>Sunucuya giden istekleri ağın filtresi gibi HTML ile cevaplar; Worker'a gidenler isteğe göre.</summary>
        private sealed class FilterHandler(bool relayBlocked) : HttpMessageHandler
        {
            public List<string> Seen { get; } = new();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var uri = request.RequestUri!;
                lock (Seen) Seen.Add(uri.Host + uri.PathAndQuery);
                bool blocked = uri.Host == ServerConnection.Host || relayBlocked;
                var response = new HttpResponseMessage(blocked ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
                {
                    Content = blocked
                        ? new StringContent("<html><body>Bu site engellenmiştir</body></html>", Encoding.UTF8, "text/html")
                        : new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json"),
                };
                return Task.FromResult(response);
            }
        }

        [Fact]
        public async Task HtmlFromApiGoesThroughRelay()
        {
            var filter = new FilterHandler(relayBlocked: false);
            using var http = new HttpClient(ServerConnection.CreateHandler(filter));
            try
            {
                var response = await http.PostAsync("https://vuluvulu.com.tr/api/auth/login", new ByteArrayContent(Encoding.UTF8.GetBytes("{}")));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("{\"ok\":true}", await response.Content.ReadAsStringAsync());
                Assert.NotNull(ServerConnection.ActiveRelay);
                Assert.Contains(filter.Seen, s => s.StartsWith(new Uri(ServerConnection.Relays[0]).Host + "/api/auth/login"));
            }
            finally { ServerConnection.StopRelay(); }
        }

        [Fact]
        public async Task HtmlEverywhereKeepsOriginalResponse()
        {
            var filter = new FilterHandler(relayBlocked: true);
            using var http = new HttpClient(ServerConnection.CreateHandler(filter));
            try
            {
                var response = await http.GetAsync("https://vuluvulu.com.tr/api/users/me");
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Null(ServerConnection.ActiveRelay);
            }
            finally { ServerConnection.StopRelay(); }
        }

        [Fact]
        public void OnlyApiAndReleasesCount()
        {
            using var html = new HttpResponseMessage { Content = new StringContent("<html></html>", Encoding.UTF8, "text/html") };
            Assert.True(ServerConnection.LooksBlocked(html, new Uri("https://vuluvulu.com.tr/api/friends")));
            Assert.True(ServerConnection.LooksBlocked(html, new Uri("https://vuluvulu.com.tr/releases/release.json")));
            Assert.False(ServerConnection.LooksBlocked(html, new Uri("https://vuluvulu.com.tr/kayit")));
            Assert.False(ServerConnection.LooksBlocked(html, new Uri("https://modrinth.com/api/x")));
            using var json = new HttpResponseMessage { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            Assert.False(ServerConnection.LooksBlocked(json, new Uri("https://vuluvulu.com.tr/api/friends")));
        }
    }
}
