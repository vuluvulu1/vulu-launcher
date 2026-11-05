using System.Text.Json;

namespace VuluLauncher.Native
{
    /// <summary>
    /// Arayüzün hatırladığı küçük seçimler (seçili paket, paket başına RAM, açık sekme). Web arayüzünde localStorage'daydı;
    /// launcher veri klasöründe ui-state.json olarak tutulur.
    /// </summary>
    internal static class UiState
    {
        private static readonly string FilePath = Path.Combine(AppPaths.AppData, "ui-state.json");
        private static readonly object Gate = new();
        private static Dictionary<string, string>? _values;

        public static string? Get(string key)
        {
            lock (Gate) return Values().TryGetValue(key, out var v) ? v : null;
        }

        public static void Set(string key, string? value)
        {
            lock (Gate)
            {
                var values = Values();
                if (value == null) values.Remove(key); else values[key] = value;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(values));
                }
                catch (Exception ex) { LogFile.Write($"[ui] ui-state.json yazılamadı: {ex.Message}"); }
            }
        }

        private static Dictionary<string, string> Values()
        {
            if (_values != null) return _values;
            try
            {
                if (File.Exists(FilePath))
                    _values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
            }
            catch { /* bozuksa baştan başlanır */ }
            return _values ??= new();
        }
    }
}
