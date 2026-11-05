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

    /// <summary>Pencere kurulmadan (bildirim alanında başlarken) gelen olaylar hata vermeden düşer.</summary>
    public class TrayTests
    {
        [Fact]
        public void HostWithoutWindowDropsUiMessagesWithoutThrowing()
        {
            using var host = new WindowsHost(new Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime());
            host.SendToUi("{\"id\":\"1\",\"ok\":true}");
            host.SendToUi("bozuk json");
            Assert.True(host.CanHideToTray);
            Assert.False(host.IsWindowOpen);
            Assert.False(host.IsForeground);
        }
    }

    /// <summary>Native arayüz Core komutlarını köprü üzerinden süreç içinde çağırır.</summary>
    public class BridgeInvokeTests
    {
        [Fact]
        public async Task CommandResultComesBackAsJson()
        {
            var bridge = new Bridge(_ => { });
            bridge.On("echo", args => Task.FromResult<object?>(new { value = args.Str("value"), count = 2 }));
            var result = await bridge.InvokeAsync("echo", new { value = "vulu" });
            Assert.Equal("vulu", result.GetProperty("value").GetString());
            Assert.Equal(2, result.GetProperty("count").GetInt32());
            bridge.Shutdown();
        }

        [Fact]
        public async Task CommandErrorReachesTheCaller()
        {
            var bridge = new Bridge(_ => { });
            bridge.On("fail", _ => throw new InvalidOperationException("Giriş gerekli."));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.InvokeAsync("fail"));
            Assert.Equal("Giriş gerekli.", error.Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.InvokeAsync("yok"));
            bridge.Shutdown();
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
