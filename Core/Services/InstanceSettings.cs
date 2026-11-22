using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Bir paketin kendi oyun ayarları; boş alan "genel ayarı kullan" demektir. vulu paketleri de kendi paketleri de
    /// aynı yerde, paket kimliğine göre saklanır (%LocalAppData%\vuluLauncher\instance-settings.json).
    /// </summary>
    public sealed class InstanceSettings
    {
        /// <summary>Bu pakette kullanılacak java.exe; boşsa genel Java ayarı.</summary>
        public string? JavaPath { get; set; }
        /// <summary>Genel argümanlardan sonra eklenen JVM argümanları.</summary>
        public string? JvmArgs { get; set; }
        /// <summary>Oyun penceresi: null = genel ayar; Width/Height 0 = oyunun kendi boyutu.</summary>
        public int? GameWidth { get; set; }
        public int? GameHeight { get; set; }
        public bool? Fullscreen { get; set; }

        public bool IsEmpty => string.IsNullOrWhiteSpace(JavaPath) && string.IsNullOrWhiteSpace(JvmArgs) && GameWidth == null && GameHeight == null && Fullscreen == null;
    }

    /// <summary>Genel ayarlarla paketin ayarlarının birleşimi: oyun açılırken kullanılan değerler.</summary>
    internal sealed record EffectiveGameSettings(string? JavaPath, string JvmArgs, int GameWidth, int GameHeight, bool Fullscreen)
    {
        public static EffectiveGameSettings Of(AppSettings global, InstanceSettings? own)
        {
            string? java = !string.IsNullOrWhiteSpace(own?.JavaPath) ? own!.JavaPath!.Trim().Trim('"')
                : global.JavaMode == "custom" ? global.CustomJavaPath.Trim().Trim('"') : null;
            string jvm = string.Join(" ", new[] { global.JvmArgs, own?.JvmArgs }.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!.Trim()));
            return new EffectiveGameSettings(java, jvm,
                own?.GameWidth ?? global.GameWidth, own?.GameHeight ?? global.GameHeight, own?.Fullscreen ?? global.Fullscreen);
        }
    }

    internal static class InstanceSettingsStore
    {
        private static string FilePath => Path.Combine(AppPaths.AppData, "instance-settings.json");
        private static readonly object Gate = new();

        public static InstanceSettings? Get(string id)
        {
            lock (Gate) return Load().GetValueOrDefault(id);
        }

        public static void Set(string id, InstanceSettings? settings)
        {
            lock (Gate)
            {
                var all = Load();
                if (settings == null || settings.IsEmpty) all.Remove(id);
                else all[id] = settings;
                Save(all);
            }
        }

        public static void Remove(string id) => Set(id, null);

        private static Dictionary<string, InstanceSettings> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Dictionary<string, InstanceSettings>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { /* bozuk dosya: sıfırdan */ }
            return new();
        }

        private static void Save(Dictionary<string, InstanceSettings> all)
        {
            Directory.CreateDirectory(AppPaths.AppData);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(all));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }
}
