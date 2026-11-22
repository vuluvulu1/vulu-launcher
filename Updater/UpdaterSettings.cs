using System.Text.Json;

namespace Updater
{
    /// <summary>Launcher'ın ayar dosyasından güncelleyicinin ihtiyaç duyduğu ayarlar (dil).</summary>
    internal static class UpdaterSettings
    {
        /// <summary>Launcher'ın ayar dosyasındaki bir metin ayarı (tema, dil); okunamazsa varsayılan.</summary>
        public static string ReadSetting(string name, string fallback)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vuluLauncher");
#if DEBUG
                // Launcher'daki gibi: geliştirmede deneme verisi kullanılabilir
                if (Environment.GetEnvironmentVariable("VULU_APP_DATA") is { Length: > 0 } test) dir = test;
#endif
                string file = Path.Combine(dir, "settings.json");
                if (!File.Exists(file)) return fallback;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                        return prop.Value.GetString() ?? fallback;
            }
            catch { /* bozuk ya da kilitli dosya: varsayılan */ }
            return fallback;
        }
    }
}
