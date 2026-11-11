using System.Net;
using VuluShared;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>
    /// Sunucuya hiçbir yoldan ulaşılamayınca istekler Cloudflare Worker yedeğine gider. Yerel bir sahte Worker'la
    /// denenir; ortam değişkenleri statik alanlara işlendiği için ayrı süreçte (VULU_RELAY_TEST=1) çalışır.
    /// </summary>
    public class RelayTests
    {
        [Fact]
        public async Task BlockedServerGoesThroughRelay()
        {
            if (Environment.GetEnvironmentVariable("VULU_RELAY_TEST") != "1") return;
            string prefix = Environment.GetEnvironmentVariable("VULU_TEST_RELAY")!.TrimEnd('/') + "/";
            using var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();
            var seen = new List<string>();
            _ = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await listener.GetContextAsync(); } catch { return; }
                    lock (seen) seen.Add($"{ctx.Request.HttpMethod} {ctx.Request.Url!.PathAndQuery} auth={ctx.Request.Headers["Authorization"]}");
                    byte[] body = System.Text.Encoding.UTF8.GetBytes("{\"ok\":true}");
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(body);
                    ctx.Response.Close();
                }
            });

            using var http = ServerConnection.CreateClient(TimeSpan.FromSeconds(60));
            using var first = new HttpRequestMessage(HttpMethod.Get, "https://vuluvulu.com.tr/api/users/me?x=1");
            first.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "abc");
            var r1 = await http.SendAsync(first);
            Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
            Assert.NotNull(ServerConnection.ActiveRelay);

            // Yedek etkinken sonraki istek (gövdeli POST) beklemeden oraya gider
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var r2 = await http.PostAsync("https://vuluvulu.com.tr/api/auth/login", new StringContent("{}"));
            Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), watch.Elapsed.ToString());
            Assert.Equal(prefix.TrimEnd('/') + "/api/yggdrasil", ServerConnection.Reachable("https://vuluvulu.com.tr/api/yggdrasil"));

            lock (seen)
            {
                Assert.Contains("GET /api/users/me?x=1 auth=Bearer abc", seen);
                Assert.Contains(seen, s => s.StartsWith("POST /api/auth/login"));
            }
            listener.Stop();
        }
    }
}
