using System.IO.Compression;
using System.Text;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>Yarım inmiş kütüphaneleri ayırt eden zip denetimi (GameLauncher.IsCompleteZip).</summary>
    public class BrokenLibraryTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "vulu-tests-" + Guid.NewGuid().ToString("N"));

        public BrokenLibraryTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private string MakeJar(int entries)
        {
            string path = Path.Combine(_dir, $"lib-{entries}.jar");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            var random = new Random(entries);
            for (int i = 0; i < entries; i++)
            {
                var data = new byte[4096];
                random.NextBytes(data);   // sıkıştırılamaz içerik: dosya gerçekçi boyutta olsun
                using var stream = zip.CreateEntry($"pkg/C{i}.class").Open();
                stream.Write(data);
            }
            return path;
        }

        [Fact]
        public void CompleteJarPasses() => Assert.True(GameLauncher.IsCompleteZip(MakeJar(50)));

        [Fact]
        public void TruncatedJarFails()
        {
            string path = MakeJar(50);
            using (var file = new FileStream(path, FileMode.Open)) file.SetLength(file.Length / 2);   // yarıda kesilmiş indirme
            Assert.False(GameLauncher.IsCompleteZip(path));
        }

        [Fact]
        public void NonZipAndTinyFilesFail()
        {
            string html = Path.Combine(_dir, "error.jar");
            File.WriteAllText(html, "<html>404 Not Found</html>");
            Assert.False(GameLauncher.IsCompleteZip(html));
            string empty = Path.Combine(_dir, "empty.jar");
            File.WriteAllBytes(empty, Array.Empty<byte>());
            Assert.False(GameLauncher.IsCompleteZip(empty));
        }
    }

    /// <summary>Oyun açılmadan önceki RAM denetimi (MemoryCheck).</summary>
    public class MemoryCheckTests
    {
        [Fact]
        public void EnoughMemoryKeepsTheChoice()
        {
            var result = MemoryCheck.Fit(8, modded: true, new MemoryCheck.Status(TotalGb: 32, AvailCommitGb: 20), systemDriveFreeGb: 100);
            Assert.Equal(8, result.RamGb);
            Assert.Null(result.Warning);
        }

        [Fact]
        public void LowCommitLowersRamAndExplains()
        {
            // Kullanıcının çöken durumu: 16 GB RAM, ~4.6 GB boş commit, C: dolu
            var result = MemoryCheck.Fit(8, modded: true, new MemoryCheck.Status(TotalGb: 15.9, AvailCommitGb: 4.6), systemDriveFreeGb: 0.2);
            Assert.Equal(3, result.RamGb);
            Assert.Contains("8 GB yerine 3 GB", result.Warning);
            Assert.Contains("sanal belleği büyütemiyor", result.Warning);
        }

        [Fact]
        public void SmallMachineLeavesRoomForWindows()
        {
            var result = MemoryCheck.Fit(8, modded: true, new MemoryCheck.Status(TotalGb: 8, AvailCommitGb: 12), systemDriveFreeGb: 50);
            Assert.Equal(5, result.RamGb);
            Assert.Contains("toplam 8 GB RAM", result.Warning);
        }

        [Fact]
        public void AlmostNoMemoryStillTriesMinimum()
        {
            var result = MemoryCheck.Fit(4, modded: false, new MemoryCheck.Status(TotalGb: 8, AvailCommitGb: 1.5), systemDriveFreeGb: 50);
            Assert.Equal(2, result.RamGb);
            Assert.Contains("açılmayabilir", result.Warning);
        }
    }

    /// <summary>Ayarlar → Launcher boyutu.</summary>
    public class WindowSizeTests
    {
        [Theory]
        [InlineData("1280x720", 1280, 720)]
        [InlineData("2560x1440", 2560, 1440)]
        public void ValidSizesParse(string value, int width, int height) =>
            Assert.Equal((width, height), AppSettings.ParseWindowSize(value));

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("1280")]
        [InlineData("abcxdef")]
        [InlineData("100x100")]      // çok küçük
        [InlineData("99999x99999")]  // çok büyük
        public void InvalidSizesMeanAuto(string? value) => Assert.Null(AppSettings.ParseWindowSize(value));
    }

    /// <summary>CurseForge parmak izi: MurmurHash2 (tohum 1), 9/10/13/32 baytları atlanır. Değerler Python ile ayrıca hesaplandı.</summary>
    public class CurseForgeFingerprintTests : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), "vulu-fp-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { File.Delete(_file); } catch { } }

        [Theory]
        [InlineData("hello world\r\n\tvulu launcher 0123456789", 180164384u)]
        [InlineData("", 1540447798u)]
        [InlineData("   \t\r\n", 1540447798u)]   // yalnızca atlanan baytlar: boş dosyayla aynı
        public void MatchesReference(string content, uint expected)
        {
            File.WriteAllBytes(_file, Encoding.ASCII.GetBytes(content));
            Assert.Equal(expected, FileHashes.CurseForgeFingerprint(_file));
        }
    }

    /// <summary>Eksik mod adından arama sorguları (ModrinthService.SearchQueries).</summary>
    public class SearchQueryTests
    {
        [Theory]
        [InlineData("kotlinforforge", "kotlin for forge")]
        [InlineData("geckolib", "gecko lib")]
        public void SplitsJoinedWords(string modId, string expected) =>
            Assert.Contains(expected, ModrinthService.SearchQueries(modId));

        [Fact]
        public void DoesNotBreakForge() =>
            Assert.DoesNotContain(ModrinthService.SearchQueries("forgeconfigapiport"), q => q.Contains("for ge"));
    }
}
