using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>Bir instance'ın oynama süresi.</summary>
    public sealed class PlayTimeEntry
    {
        public long Seconds { get; set; }
        public int Sessions { get; set; }
        public DateTime? LastPlayed { get; set; }
    }

    /// <summary>
    /// Instance başına toplam oynama süresi. %LocalAppData%\vuluLauncher\playtime.json içinde, instance kimliğine
    /// ("pack:…" / "instance:…") göre saklanır; böylece sunucu paketleri ve kişisel instance'lar aynı yerde durur.
    /// </summary>
    internal static class PlayTimeStore
    {
        private static readonly string FilePath = Path.Combine(AppPaths.AppData, "playtime.json");
        private static readonly object Gate = new();

        public static Dictionary<string, PlayTimeEntry> All()
        {
            lock (Gate) return Load();
        }

        public static PlayTimeEntry? Get(string id)
        {
            lock (Gate) return Load().GetValueOrDefault(id);
        }

        /// <summary>Süre ekler; <paramref name="newSession"/> oyunun her açılışında bir kez true verilir.</summary>
        public static void Add(string id, TimeSpan played, bool newSession)
        {
            if (played < TimeSpan.Zero) played = TimeSpan.Zero;
            lock (Gate)
            {
                var all = Load();
                if (!all.TryGetValue(id, out var entry)) all[id] = entry = new PlayTimeEntry();
                entry.Seconds += (long)played.TotalSeconds;
                if (newSession) entry.Sessions++;
                entry.LastPlayed = DateTime.UtcNow;
                Save(all);
            }
        }

        public static void Remove(string id)
        {
            lock (Gate)
            {
                var all = Load();
                if (all.Remove(id)) Save(all);
            }
        }

        private static Dictionary<string, PlayTimeEntry> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Dictionary<string, PlayTimeEntry>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { /* bozuk dosya: sıfırdan */ }
            return new();
        }

        private static void Save(Dictionary<string, PlayTimeEntry> all)
        {
            Directory.CreateDirectory(AppPaths.AppData);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(all));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }
}
