using System.Security.Cryptography;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>Elle indirilen dosyaların yerine konması (ManualFiles.Place).</summary>
    public class ManualFilesTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "vulu-manual-" + Guid.NewGuid().ToString("N"));

        public ManualFilesTests()
        {
            Directory.CreateDirectory(_root);
            AppPaths.Initialize(Path.Combine(_root, "data"), _root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        private (InstanceMeta Meta, string Downloads, ManualDownload File) Setup()
        {
            byte[] content = new byte[64 * 1024];
            Random.Shared.NextBytes(content);
            string downloads = Path.Combine(_root, "Downloads");
            Directory.CreateDirectory(downloads);
            File.WriteAllBytes(Path.Combine(downloads, "ornek-mod-1.0.jar"), content);
            var file = new ManualDownload("Örnek", "https://www.curseforge.com/minecraft/mc-mods/ornek/download/1", "mods", "ornek-mod-1.0.jar",
                Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant(), content.Length, "1", "1");
            var meta = new InstanceMeta { Name = "Deneme", Folder = "deneme", McVersion = "1.20.1", Loader = "fabric" };
            meta.PendingManual.Add(file);
            Directory.CreateDirectory(InstanceStore.PathOf(meta.Folder));
            return (meta, downloads, file);
        }

        [Fact]
        public void OverlappingScansPlaceTheFileOnceWithoutErrors()
        {
            var (meta, downloads, _) = Setup();
            // Arayüzün 2 saniyelik taraması üst üste binince aynı dosya aynı anda iki kez kopyalanıyordu
            var results = Enumerable.Range(0, 6).AsParallel().Select(_ => ManualFiles.Place(meta, downloads)).ToList();

            Assert.Contains(results, r => r.Any(p => p.Status == "placed"));
            Assert.Empty(meta.PendingManual);
            Assert.True(File.Exists(Path.Combine(InstanceStore.PathOf(meta.Folder), "mods", "ornek-mod-1.0.jar")));
            Assert.False(File.Exists(Path.Combine(InstanceStore.PathOf(meta.Folder), "mods", "ornek-mod-1.0.jar.vulu-part")));
        }

        [Fact]
        public void LockedTargetWaitsInsteadOfFailing()
        {
            var (meta, downloads, _) = Setup();
            string target = Path.Combine(InstanceStore.PathOf(meta.Folder), "mods", "ornek-mod-1.0.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (new FileStream(target, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                var result = Assert.Single(ManualFiles.Place(meta, downloads));
                Assert.Equal("problem", result.Status);
                Assert.Contains("kullanımda", result.Problem);
            }
            // Dosya serbest kalınca bir sonraki tur yerine koyar
            Assert.Equal("placed", Assert.Single(ManualFiles.Place(meta, downloads)).Status);
        }
    }
}
