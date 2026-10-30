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

namespace VuluLauncher.Tests
{
    /// <summary>Fabric çökme raporlarında suçlu modu yığın izinden bulma (CrashAnalyzer.CulpritFromStack).</summary>
    public class CrashCulpritTests
    {
        private const string BlurCrash = """
            java.lang.IllegalStateException: Can only blur once per frame
            	at net.minecraft.class_11228.method_70810(class_11228.java:412)
            	at net.minecraft.class_437.method_57734(class_437.java:470)
            	at com.erikedits.justquests.client.QuestScreen.extractRenderState(QuestScreen.java:280)
            	at net.minecraft.class_757.method_3192(class_757.java:1104)
            """;

        [Fact]
        public void FindsModClassThroughOwnerLookup()
        {
            string? asked = null;
            var owner = CrashAnalyzer.CulpritFromStack(BlurCrash, cls => { asked ??= cls; return cls.StartsWith("com.erikedits.justquests.") ? "justquests" : null; });
            Assert.Equal("justquests", owner);
            Assert.Equal("com.erikedits.justquests.client.QuestScreen", asked);   // oyun sınıfları sorulmaz
        }

        [Fact]
        public void FindsModFromMixinHandlerName()
        {
            const string stack = "java.lang.NullPointerException\n\tat net.minecraft.class_310.handler$zzb000$sodium$onRender(class_310.java:90)\n";
            Assert.Equal("sodium", CrashAnalyzer.CulpritFromStack(stack, null));
        }

        [Fact]
        public void IgnoresGameOnlyStacks() =>
            Assert.Null(CrashAnalyzer.CulpritFromStack("\tat net.minecraft.class_310.method_1(x.java:1)\n\tat java.base/java.lang.Thread.run(Thread.java:1)\n", _ => "x"));
    }
}

namespace VuluLauncher.Tests
{
    /// <summary>Renkli MOTD: bileşen JSON'u ve § kodları parçalara (ServerPing.RichText).</summary>
    public class MotdTests
    {
        private static IReadOnlyList<MotdPart> Parse(string json) => ServerPing.RichText(System.Text.Json.JsonDocument.Parse(json).RootElement);

        [Fact]
        public void GradientLettersAndInheritedStyle()
        {
            var parts = Parse("""{"text":"","extra":[{"text":"v","color":"#654127"},{"text":"u","color":"#7a5a40"},{"text":"\nsite","color":"gold","bold":true}]}""");
            Assert.Equal(3, parts.Count);
            Assert.Equal(("v", "#654127"), (parts[0].Text, parts[0].Color));
            Assert.Equal("#FFAA00", parts[2].Color);
            Assert.True(parts[2].Bold);
        }

        [Fact]
        public void LegacySectionCodes()
        {
            var parts = Parse("\"\u00a7aYe\u015fil \u00a7lkal\u0131n\u00a7r d\u00fcz\"");
            Assert.Equal(new[] { "Yeşil ", "kalın", " düz" }, parts.Select(p => p.Text));
            Assert.Equal("#55FF55", parts[0].Color);
            Assert.True(parts[1].Bold);
            Assert.Null(parts[2].Color);
        }

        [Fact]
        public void SameStyleMerges() => Assert.Single(Parse("""[{"text":"a","color":"red"},{"text":"b","color":"red"}]"""));
    }
}

namespace VuluLauncher.Tests
{
    /// <summary>Paylaşmadan önceki denetim (PackCheck): çift mod ve birbirini ezen dünya dosyaları.</summary>
    public class PackCheckTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "vulu-tests-" + Guid.NewGuid().ToString("N"));

        public PackCheckTests() => Directory.CreateDirectory(Path.Combine(_dir, "mods"));
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private void Jar(string file, string id, string name, params string[] entries)
        {
            using var zip = ZipFile.Open(Path.Combine(_dir, "mods", file), ZipArchiveMode.Create);
            using (var w = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
                w.Write($$"""{"schemaVersion":1,"id":"{{id}}","version":"1.0","name":"{{name}}"}""");
            foreach (var e in entries) using (var w = new StreamWriter(zip.CreateEntry(e).Open())) w.Write("{}");
        }

        [Fact]
        public void FindsWorldgenOverridesAndDuplicates()
        {
            Jar("geo.jar", "geophilic", "Geophilic", "data/minecraft/worldgen/biome/forest.json", "data/minecraft/worldgen/biome/desert.json");
            Jar("terra.jar", "terralith", "Terralith", "data/minecraft/worldgen/biome/forest.json", "data/minecraft/worldgen/biome/desert.json",
                "data/minecraft/tags/worldgen/biome/is_forest.json");
            Jar("bclib-a.jar", "bclib", "BCLib");
            Jar("bclib-b.jar", "bclib", "BCLib");

            var issues = PackCheck.Run(_dir, "fabric");
            var clash = Assert.Single(issues, i => i.Kind == "override");
            Assert.Contains("Geophilic + Terralith", clash.Title);
            Assert.StartsWith("2 dosyada", clash.Detail);                 // etiket dosyası sayılmaz (etiketler birleşir)
            Assert.Single(issues, i => i.Kind == "duplicate");
        }

        [Fact]
        public void CleanPackHasNoIssues()
        {
            Jar("terra.jar", "terralith", "Terralith", "data/minecraft/worldgen/biome/forest.json");
            Jar("cont.jar", "continents", "Continents", "data/minecraft/worldgen/density_function/overworld/continents.json");
            Assert.Empty(PackCheck.Run(_dir, "fabric"));
        }
    }
}

namespace VuluLauncher.Tests
{
    /// <summary>Paket güncellemesi gizli ya da salt okunur dosyaların üstüne yazabilmeli (Euphoria Patcher .data.json).</summary>
    public class OverwriteProtectedFileTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "vulu-tests-" + Guid.NewGuid().ToString("N"));

        public OverwriteProtectedFileTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Theory]
        [InlineData(FileAttributes.Hidden)]
        [InlineData(FileAttributes.ReadOnly)]
        [InlineData(FileAttributes.Hidden | FileAttributes.ReadOnly)]
        public void ExtractOverProtectedFile(FileAttributes attributes)
        {
            string zipPath = Path.Combine(_dir, "a.zip"), target = Path.Combine(_dir, ".data.json");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("x").Open())) writer.Write("yeni");
            File.WriteAllText(target, "eski");
            File.SetAttributes(target, attributes);

            using var read = ZipFile.OpenRead(zipPath);
            Assert.Throws<UnauthorizedAccessException>(() => read.Entries[0].ExtractToFile(target, overwrite: true));   // düzeltmesiz hâl

            Downloader.MakeWritable(target);
            read.Entries[0].ExtractToFile(target, overwrite: true);
            Assert.Equal("yeni", File.ReadAllText(target));
        }

        [Fact]
        public void MissingFileIsIgnored() => Downloader.MakeWritable(Path.Combine(_dir, "yok.json"));
    }
}
