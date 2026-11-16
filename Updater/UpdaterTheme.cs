using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;

namespace Updater
{
    /// <summary>
    /// Launcher'da seçili temayı (Meşale, Ufkun öbür ucu, Derin Karanlık) güncelleyiciye de uygular. Ayar dosyasından
    /// yalnızca tema adı okunur; dosya yoksa ya da okunamazsa App.axaml'daki Meşale renkleri kalır.
    /// Renkler Launcher/Services/Themes.cs'deki değerlerin güncelleyicinin kullandığı küçük alt kümesidir.
    /// </summary>
    internal static class UpdaterTheme
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Palettes = new()
        {
            ["ufuk"] = new()
            {
                ["Bg"] = "#07000F", ["Panel"] = "#100022", ["Surface"] = "#1A0533", ["Raised"] = "#2A0A4A",
                ["Hover"] = "#1FFF2DF1", ["Line"] = "#38FF2DF1", ["LineStrong"] = "#6600D7FF",
                ["ButtonBg"] = "#C7140030", ["ButtonLine"] = "#8000D7FF",
                ["Text"] = "#F1E8FF", ["Muted"] = "#D2C3EE", ["Dim"] = "#A391C4",
                ["Accent"] = "#FF2DF1", ["AccentHi"] = "#FF5CF5", ["AccentInk"] = "#FFFFFF",
                ["Ok"] = "#9FF4FF", ["Error"] = "#FF5A7A",
            },
            ["derin"] = new()
            {
                ["Bg"] = "#030809", ["Panel"] = "#061416", ["Surface"] = "#081C1F", ["Raised"] = "#0C2629",
                ["Hover"] = "#1A2FE6D8", ["Line"] = "#2E2FE6D8", ["LineStrong"] = "#5C2FE6D8",
                ["ButtonBg"] = "#D1061618", ["ButtonLine"] = "#662FE6D8",
                ["Text"] = "#DCF6F2", ["Muted"] = "#A9CBC7", ["Dim"] = "#7AA19D",
                ["Accent"] = "#2FE6D8", ["AccentHi"] = "#8FF7EE", ["AccentInk"] = "#021F1D",
                ["Ok"] = "#8FF7EE", ["Error"] = "#FF6B6B",
            },
        };

        /// <summary>Launcher'ın ayar dosyasındaki tema adı ("mesale", "ufuk", "derin").</summary>
        public static string ReadThemeName()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vuluLauncher");
#if DEBUG
                // Launcher'daki gibi: geliştirmede deneme verisi kullanılabilir
                if (Environment.GetEnvironmentVariable("VULU_APP_DATA") is { Length: > 0 } test) dir = test;
#endif
                string file = Path.Combine(dir, "settings.json");
                if (!File.Exists(file)) return "mesale";
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (prop.Name.Equals("Theme", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                        return prop.Value.GetString() ?? "mesale";
            }
            catch { /* bozuk ya da kilitli dosya: varsayılan tema */ }
            return "mesale";
        }

        /// <summary>Uygulamanın fırçalarının rengini seçili temaya çevirir (pencere açılmadan önce çağrılır).</summary>
        public static void Apply(IResourceDictionary resources, string theme)
        {
            if (!Palettes.TryGetValue(theme, out var palette)) return;
            foreach (var (key, hex) in palette)
                if (resources.TryGetResource(key, null, out var value) && value is SolidColorBrush brush)
                    brush.Color = Color.Parse(hex);
            // İşaret kutusu ("oyun dosyalarını da sil") her temada tehlike kırmızısı kalır
        }
    }
}
