using System.Net;
using System.Text;
using VuluShared;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>Güncelleme imzası: yalnızca release anahtarıyla imzalanmış release.json kabul edilir.</summary>
    public class ReleaseSignatureTests
    {
        private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        [Fact]
        public void PublishedManifestVerifies()
        {
            byte[] data = File.ReadAllBytes(Path.Combine(Dir, "release.json"));
            string signature = File.ReadAllText(Path.Combine(Dir, "release.json.sig")).Trim();
            Assert.True(Releases.VerifySignature(data, signature));
        }

        [Fact]
        public void TamperedManifestIsRejected()
        {
            byte[] data = File.ReadAllBytes(Path.Combine(Dir, "release.json"));
            string signature = File.ReadAllText(Path.Combine(Dir, "release.json.sig")).Trim();
            byte[] tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(data).Replace("0.13.1", "9.9.9"));
            Assert.False(Releases.VerifySignature(tampered, signature));
        }

        [Fact]
        public void GarbageSignatureIsRejected() =>
            Assert.False(Releases.VerifySignature(Encoding.UTF8.GetBytes("{}"), "bm90IGEgc2lnbmF0dXJl"));
    }

    /// <summary>
    /// Bildirim alanına küçültme Photino'nun gizli "_messageLoopIsStarted" alanına dayanır. Photino güncellenip alan
    /// kaybolursa özellik sessizce kapanır; bu test bunu yayından önce yakalar.
    /// </summary>
    public class TrayTests
    {
        [Fact]
        public void PhotinoStillHasTheMessageLoopFlag()
        {
            using var host = new WindowsHost(() => throw new InvalidOperationException("pencere açılmaz"), "index.html");
            Assert.True(host.CanHideToTray);
        }

        [Fact]
        public void HiddenHostDropsUiMessagesWithoutThrowing()
        {
            using var host = new WindowsHost(() => throw new InvalidOperationException(), "index.html");
            host.SendToUi("{\"event\":\"x\"}");
            Assert.False(host.IsWindowOpen);
            Assert.False(host.IsForeground);
        }
    }

    /// <summary>
    /// Yedek bağlantı: alan adı çözülemese de sunucuya ulaşılır. Ağ gerektirir; yalnızca VULU_ONLINE_TESTS=1 iken çalışır.
    /// (Geliştirme derlemesindeki VULU_TEST_DIRECT ile doğrudan bağlantı engellenir.)
    /// </summary>
    public class ServerFallbackTests
    {
        private static bool Online => Environment.GetEnvironmentVariable("VULU_ONLINE_TESTS") == "1";

        [Fact]
        public async Task ReachesServerWhenDnsIsBlocked()
        {
            if (!Online) return;
            Environment.SetEnvironmentVariable("VULU_TEST_DIRECT", "fail");
            try
            {
                using var http = ServerConnection.CreateClient(TimeSpan.FromSeconds(30));
                using var response = await http.GetAsync("https://vuluvulu.com.tr/api/stats");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(ServerConnection.UsingFallback);
            }
            finally { Environment.SetEnvironmentVariable("VULU_TEST_DIRECT", null); }
        }
    }
}
