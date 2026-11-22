using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>Mod güncellemesinde yedek ve geri alma (ModBackups), ağsız: güncelleme elle taklit edilir.</summary>
    [Collection("AppPaths")]
    public class ModBackupsTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "vulu-modbak-" + Guid.NewGuid().ToString("N"));

        public ModBackupsTests()
        {
            Directory.CreateDirectory(_root);
            AppPaths.Initialize(Path.Combine(_root, "data"), _root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        private static InstalledContent Mod(string version, string file) =>
            new() { ProjectId = "AANodium1", VersionId = "v" + version, Title = "Sodium", VersionNumber = version, File = file };

        private (InstanceMeta Meta, string Dir) Setup()
        {
            var meta = new InstanceMeta { Name = "Deneme", Folder = "deneme", McVersion = "1.21.1", Loader = "fabric" };
            string dir = InstanceStore.PathOf(meta.Folder);
            Directory.CreateDirectory(Path.Combine(dir, "mods"));
            File.WriteAllText(Path.Combine(dir, "mods", "sodium-0.5.jar"), "eski");
            meta.Content.Add(Mod("0.5", "mods/sodium-0.5.jar"));
            return (meta, dir);
        }

        /// <summary>Servislerin yaptığı gibi: yeni dosya iner, eskisi silinir, kayıt değişir.</summary>
        private static void FakeUpdate(InstanceMeta meta, string dir)
        {
            File.WriteAllText(Path.Combine(dir, "mods", "sodium-0.6.jar"), "yeni");
            File.Delete(Path.Combine(dir, "mods", "sodium-0.5.jar"));
            meta.Content.Clear();
            meta.Content.Add(Mod("0.6", "mods/sodium-0.6.jar"));
        }

        [Fact]
        public void RollbackRestoresOldFileAndRecord()
        {
            var (meta, dir) = Setup();
            var record = ModBackups.Prepare(dir, meta, "AANodium1", "", ModBackups.FilesOf(dir, meta.Content[0]));
            FakeUpdate(meta, dir);
            Assert.True(ModBackups.Commit(dir, meta, record));
            Assert.Equal(("0.5", "0.6"), (record.From, record.To));

            var back = ModBackups.Rollback(dir, meta, "AANodium1", "modrinth");
            Assert.Equal("0.5", back.From);
            Assert.True(File.Exists(Path.Combine(dir, "mods", "sodium-0.5.jar")));
            Assert.False(File.Exists(Path.Combine(dir, "mods", "sodium-0.6.jar")));
            Assert.Equal("0.5", Assert.Single(meta.Content).VersionNumber);
            Assert.Empty(meta.UpdateHistory);
            Assert.Empty(Directory.GetFiles(Path.Combine(dir, ModBackups.Folder)));   // yedek yerine taşındı
        }

        [Fact]
        public void DisabledModStaysDisabledAfterRollback()
        {
            var (meta, dir) = Setup();
            File.Move(Path.Combine(dir, "mods", "sodium-0.5.jar"), Path.Combine(dir, "mods", "sodium-0.5.jar.disabled"));
            var record = ModBackups.Prepare(dir, meta, "AANodium1", null, ModBackups.FilesOf(dir, meta.Content[0]));
            File.Delete(Path.Combine(dir, "mods", "sodium-0.5.jar.disabled"));
            File.WriteAllText(Path.Combine(dir, "mods", "sodium-0.6.jar"), "yeni");
            meta.Content.Clear();
            meta.Content.Add(Mod("0.6", "mods/sodium-0.6.jar"));
            ModBackups.Commit(dir, meta, record);

            ModBackups.Rollback(dir, meta, "AANodium1", null);
            Assert.True(File.Exists(Path.Combine(dir, "mods", "sodium-0.5.jar.disabled")));
        }

        [Fact]
        public void NothingChangedKeepsNoBackup()
        {
            var (meta, dir) = Setup();
            // CurseForge elle indirme isteyince dosya değişmez: kayıt yazılmaz, yedek silinir
            var record = ModBackups.Prepare(dir, meta, "AANodium1", "", ModBackups.FilesOf(dir, meta.Content[0]));
            Assert.False(ModBackups.Commit(dir, meta, record));
            Assert.Empty(meta.UpdateHistory);
            Assert.Empty(Directory.GetFiles(Path.Combine(dir, ModBackups.Folder)));
        }

        [Fact]
        public void SecondUpdateReplacesFirstRecord()
        {
            var (meta, dir) = Setup();
            var first = ModBackups.Prepare(dir, meta, "AANodium1", "", ModBackups.FilesOf(dir, meta.Content[0]));
            FakeUpdate(meta, dir);
            ModBackups.Commit(dir, meta, first);

            var second = ModBackups.Prepare(dir, meta, "AANodium1", "", ModBackups.FilesOf(dir, meta.Content[0]));
            File.WriteAllText(Path.Combine(dir, "mods", "sodium-0.7.jar"), "en yeni");
            File.Delete(Path.Combine(dir, "mods", "sodium-0.6.jar"));
            meta.Content.Clear();
            meta.Content.Add(Mod("0.7", "mods/sodium-0.7.jar"));
            ModBackups.Commit(dir, meta, second);

            Assert.Equal("0.6", Assert.Single(meta.UpdateHistory).From);
            Assert.Single(Directory.GetFiles(Path.Combine(dir, ModBackups.Folder)));
        }
    }
}
