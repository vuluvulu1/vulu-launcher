using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>Dosyadan paket içe aktarma: Modrinth .mrpack ve Prism/MultiMC dışa aktarması (ağsız: dosyasız paketler).</summary>
    [Collection("AppPaths")]
    public class ImportTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "vulu-import-" + Guid.NewGuid().ToString("N"));

        public ImportTests()
        {
            Directory.CreateDirectory(_root);
            AppPaths.Initialize(Path.Combine(_root, "data"), _root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        private string Zip(string name, params (string Path, string Content)[] entries)
        {
            string path = Path.Combine(_root, name);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var (p, c) in entries)
                using (var w = new StreamWriter(zip.CreateEntry(p).Open())) w.Write(c);
            return path;
        }

        [Fact]
        public async Task ImportsMrpack()
        {
            string pack = Zip("Deneme.mrpack",
                ("modrinth.index.json", JsonSerializer.Serialize(new { formatVersion = 1, game = "minecraft", versionId = "1.0", name = "Deneme Paketi", files = Array.Empty<object>(), dependencies = new Dictionary<string, string> { ["minecraft"] = "1.21.1", ["fabric-loader"] = "0.16.9" } })),
                ("overrides/options.txt", "lang:tr_tr"));
            var instance = await new ModrinthService((_, _) => { }).ImportPackAsync(pack);
            Assert.Equal("Deneme Paketi", instance.Name);
            Assert.Equal(("1.21.1", "fabric", "0.16.9"), (instance.McVersion, instance.Loader, instance.LoaderVersion));
            Assert.Equal("1.0", instance.ModpackVersion);
            Assert.True(File.Exists(Path.Combine(InstanceStore.PathOf(instance.Folder), "options.txt")));
        }

        [Fact]
        public async Task RecordsPackFilesAndSwitchKeepsOwnMods()
        {
            string pack = Zip("Liste.mrpack",
                ("modrinth.index.json", JsonSerializer.Serialize(new { formatVersion = 1, game = "minecraft", versionId = "2.0", name = "Liste", files = Array.Empty<object>(), dependencies = new Dictionary<string, string> { ["minecraft"] = "1.21.1" } })),
                ("overrides/mods/paket-modu.jar", "x"),
                ("overrides/config/a.cfg", "1"));
            var instance = await new ModrinthService((_, _) => { }).ImportPackAsync(pack);
            Assert.Equal(new[] { "mods/paket-modu.jar" }, instance.PackFiles);   // ayar dosyaları içerik sayılmaz

            // Sonradan eklenen mod kalır; eski sürümün yenisinde olmayan dosyası gider
            string dir = InstanceStore.PathOf(instance.Folder);
            File.WriteAllText(Path.Combine(dir, "mods", "benim-modum.jar"), "y");
            int removed = ModpackFiles.RemoveOld(dir, instance.PackFiles!, new[] { "mods/yeni.jar" });
            Assert.Equal(1, removed);
            Assert.False(File.Exists(Path.Combine(dir, "mods", "paket-modu.jar")));
            Assert.True(File.Exists(Path.Combine(dir, "mods", "benim-modum.jar")));
            Assert.NotNull(ModpackFiles.BackupConfigs(dir, "2.0"));
        }

        [Fact]
        public void ZipGuardRejectsHugeUnpackedSize()
        {
            string path = Zip("Buyuk.zip", ("a.txt", new string('x', 5000)));
            using var zip = ZipFile.OpenRead(path);
            ZipGuard.Check(zip);                                                   // varsayılan sınırın çok altında
            Assert.Throws<InvalidDataException>(() => ZipGuard.Check(zip, maxBytes: 1000));
        }

        [Fact]
        public async Task ImportsPrismExport()
        {
            string pack = Zip("Prism.zip",
                ("Prism Deneme/mmc-pack.json", JsonSerializer.Serialize(new { components = new[] { new { uid = "net.minecraft", version = "1.20.1" }, new { uid = "net.fabricmc.fabric-loader", version = "0.15.11" } }, formatVersion = 1 })),
                ("Prism Deneme/instance.cfg", "InstanceType=OneSix\nname=Prism Deneme\n"),
                ("Prism Deneme/.minecraft/config/test.cfg", "a=1"));
            using (var zip = ZipFile.OpenRead(pack)) Assert.Equal("Prism Deneme/", LauncherApp.FindPrismRoot(zip));
            var instance = await LauncherApp.ImportPrismAsync(pack);
            Assert.Equal("Prism Deneme", instance.Name);
            Assert.Equal(("1.20.1", "fabric", "0.15.11"), (instance.McVersion, instance.Loader, instance.LoaderVersion));
            Assert.True(File.Exists(Path.Combine(InstanceStore.PathOf(instance.Folder), "config", "test.cfg")));
        }
    }
}
