using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>Paketin kendi oyun ayarları: genel ayarlarla birleşme ve saklama.</summary>
    [Collection("AppPaths")]
    public class InstanceSettingsTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "vulu-instset-" + Guid.NewGuid().ToString("N"));

        public InstanceSettingsTests()
        {
            Directory.CreateDirectory(_root);
            AppPaths.Initialize(Path.Combine(_root, "data"), _root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        [Fact]
        public void EmptyOwnSettingsUseGlobal()
        {
            var global = new AppSettings { JavaMode = "custom", CustomJavaPath = @"C:\java\bin\java.exe", JvmArgs = "-Xss2m", GameWidth = 1280, GameHeight = 720, Fullscreen = true };
            var e = EffectiveGameSettings.Of(global, new InstanceSettings());
            Assert.Equal((@"C:\java\bin\java.exe", "-Xss2m", 1280, 720, true), (e.JavaPath, e.JvmArgs, e.GameWidth, e.GameHeight, e.Fullscreen));
        }

        [Fact]
        public void OwnSettingsOverrideAndJvmArgsAppend()
        {
            var global = new AppSettings { JvmArgs = "-Xss2m", GameWidth = 1280, GameHeight = 720 };
            var own = new InstanceSettings { JavaPath = "\"D:\\jdk\\java.exe\"", JvmArgs = "-XX:+UseZGC", GameWidth = 0, GameHeight = 0, Fullscreen = false };
            var e = EffectiveGameSettings.Of(global, own);
            Assert.Equal(@"D:\jdk\java.exe", e.JavaPath);
            Assert.Equal("-Xss2m -XX:+UseZGC", e.JvmArgs);
            Assert.Equal((0, 0, false), (e.GameWidth, e.GameHeight, e.Fullscreen));
            Assert.Null(EffectiveGameSettings.Of(new AppSettings(), null).JavaPath);   // otomatik Java
        }

        [Fact]
        public void StoreKeepsOnlyCustomizedPacks()
        {
            InstanceSettingsStore.Set("pack:vanilla", new InstanceSettings { JvmArgs = "-XX:+UseZGC" });
            Assert.Equal("-XX:+UseZGC", InstanceSettingsStore.Get("pack:vanilla")?.JvmArgs);
            InstanceSettingsStore.Set("pack:vanilla", new InstanceSettings());   // hepsi genel: kayıt silinir
            Assert.Null(InstanceSettingsStore.Get("pack:vanilla"));
        }
    }
}
