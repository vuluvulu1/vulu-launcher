namespace VuluLauncher
{
    /// <summary>Launcher'ın kullanıcıya özel verilerini tuttuğu klasörler.</summary>
    internal static class AppPaths
    {
        public static readonly string AppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vuluLauncher");

        public static readonly string BrowserData = Path.Combine(AppData, "browser");
        public static readonly string Credentials = Path.Combine(AppData, "credentials.json");
        public static readonly string Settings = Path.Combine(AppData, "settings.json");

        /// <summary>Kullanıcının paketlere/instance'lara atadığı arka plan resimleri.</summary>
        public static readonly string Backgrounds = Path.Combine(AppData, "backgrounds");

        /// <summary>Ana ekranda paket adının yerine gösterilen logolar.</summary>
        public static readonly string Logos = Path.Combine(AppData, "logos");

        /// <summary>Oyun dosyaları (java, minecraft_core, instances) exe'nin yanında durur.</summary>
        public static readonly string Base = AppContext.BaseDirectory;
        public static readonly string Instances = Path.Combine(Base, "instances");
    }
}
