namespace VuluLauncher
{
    /// <summary>Launcher'ın kullanıcıya özel verilerini tuttuğu klasörler.</summary>
    internal static class AppPaths
    {
        public static string AppData { get; private set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vuluLauncher");

        public static string BrowserData => Path.Combine(AppData, "browser");
        public static string Credentials => Path.Combine(AppData, "credentials.json");
        public static string Settings => Path.Combine(AppData, "settings.json");

        /// <summary>Kullanıcının paketlere/instance'lara atadığı arka plan resimleri.</summary>
        public static string Backgrounds => Path.Combine(AppData, "backgrounds");

        /// <summary>Ana ekranda paket adının yerine gösterilen logolar.</summary>
        public static string Logos => Path.Combine(AppData, "logos");

        /// <summary>Oyun dosyaları (java, minecraft_core, instances) Windows'ta exe'nin yanında durur.</summary>
        public static string Base { get; private set; } = AppContext.BaseDirectory;
        public static string Instances => Path.Combine(Base, "instances");

        /// <summary>
        /// Instance'ların paylaştığı oyun dosyaları (assets, libraries, versions). Windows'ta minecraft_core; Android'de
        /// oyunu açan Amethyst motorunun dosyaları aradığı .minecraft klasörü.
        /// </summary>
        public static string GameCore => _gameCore ?? Path.Combine(Base, "minecraft_core");
        private static string? _gameCore;

        /// <summary>
        /// Uygulama klasörü yazılamayan platformlar (Android) için: başka bir şey kullanılmadan, en başta çağrılır.
        /// </summary>
        public static void Initialize(string appData, string gameBase, string? gameCore = null)
        {
            AppData = appData;
            Base = gameBase;
            _gameCore = gameCore;
        }
    }
}
