using VuluShared;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>Gerçek Cloudflare Worker yedeği: sunucu engelliyken API cevap veriyor mu (VULU_NET_TESTS=1 ile).</summary>
    public class RelayLiveTests
    {
        [Fact]
        public async Task BlockedServerAnswersThroughTheWorker()
        {
            if (Environment.GetEnvironmentVariable("VULU_NET_TESTS") != "1" || Environment.GetEnvironmentVariable("VULU_TEST_DIRECT") != "block") return;
            using var http = ServerConnection.CreateClient(TimeSpan.FromSeconds(60));
            string json = await http.GetStringAsync("https://vuluvulu.com.tr/api/auth/microsoft");
            Assert.Contains("clientId", json);
            Assert.Equal("https://vulu-relay.ardakaradag81.workers.dev", ServerConnection.ActiveRelay);
        }
    }
}
