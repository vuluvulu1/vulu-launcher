using VuluShared;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>
    /// vulu sunucusu tamamen engelliyken sürüm bilgisi ve zip GitHub'dan alınır. Gerçek ağa çıktığı için yalnızca
    /// VULU_NET_TESTS=1 ile çalışır.
    /// </summary>
    public class ReleaseMirrorTests
    {
        [Fact]
        public async Task BlockedServerFallsBackToGitHub()
        {
            if (Environment.GetEnvironmentVariable("VULU_NET_TESTS") != "1") return;
            Environment.SetEnvironmentVariable("VULU_TEST_DIRECT", "block");
            try
            {
                using var http = Releases.CreateHttpClient(TimeSpan.FromSeconds(60));
                var latest = await Releases.GetLatestAsync(http);
                Assert.True(latest.FromMirror);
                Assert.StartsWith("https://github.com/", latest.ZipUrl);
                Assert.False(string.IsNullOrEmpty(latest.Manifest.Version));

                // Zip de GitHub'dan iner ve manifest'teki hash'le tutar
                string path = Path.Combine(Path.GetTempPath(), "vulu-mirror-" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    using var big = Releases.CreateHttpClient(TimeSpan.FromMinutes(10));
                    await using (var stream = await big.GetStreamAsync(latest.ZipUrl))
                    await using (var file = File.Create(path))
                        await stream.CopyToAsync(file);
                    Assert.Equal(latest.Manifest.Sha256.ToLowerInvariant(), Releases.Sha256Of(path));
                }
                finally { File.Delete(path); }
            }
            finally { Environment.SetEnvironmentVariable("VULU_TEST_DIRECT", null); }
        }
    }
}
