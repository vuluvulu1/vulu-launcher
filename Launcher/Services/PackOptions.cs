using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Paketle birlikte taşınan oyun seçenekleri. options.txt kişisel ayarlar (tuşlar, ses, FOV) içerdiği için
    /// paylaşılmaz; yalnızca açık kaynak paketlerinin listesi paketin dosya zip'ine <see cref="FileName"/> olarak
    /// konur ve kuranın options.txt'sine işlenir. Shader seçimi zaten paylaşılan dosyalarda durur
    /// (Iris/Oculus: config/, OptiFine: optionsshaders.txt).
    /// </summary>
    internal static class PackOptions
    {
        public const string FileName = "vulu-pack.json";
        private const string Key = "resourcePacks";

        private sealed record Data(List<string> ResourcePacks);

        /// <summary>Instance'ta açık kaynak paketleri (options.txt'de yoksa ya da okunamazsa null).</summary>
        public static List<string>? ReadResourcePacks(string instanceDir)
        {
            string path = Path.Combine(instanceDir, "options.txt");
            if (!File.Exists(path)) return null;
            foreach (var line in File.ReadLines(path))
            {
                if (!line.StartsWith(Key + ":", StringComparison.Ordinal)) continue;
                try { return JsonSerializer.Deserialize<List<string>>(line[(Key.Length + 1)..]); }
                catch (JsonException) { return null; }
            }
            return null;
        }

        public static byte[] Serialize(List<string> resourcePacks) =>
            JsonSerializer.SerializeToUtf8Bytes(new Data(resourcePacks), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        /// <summary>
        /// Paketteki listeyi kuranın options.txt'sine yazar: önce paketin açık kaynak paketleri (aynı sırayla),
        /// sonra kullanıcının kendi açtıkları. Diğer ayarlara dokunulmaz; options.txt yoksa yalnızca bu satırla oluşur.
        /// </summary>
        public static void Apply(string instanceDir, Stream json)
        {
            List<string>? packs;
            try { packs = JsonSerializer.Deserialize<Data>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.ResourcePacks; }
            catch (JsonException) { return; }
            if (packs == null) return;
            packs = packs.Where(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 300 && !p.Any(char.IsControl)).Distinct().Take(200).ToList();

            string path = Path.Combine(instanceDir, "options.txt");
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            int index = lines.FindIndex(l => l.StartsWith(Key + ":", StringComparison.Ordinal));

            var own = index >= 0 ? ReadList(lines[index]) : new List<string>();
            var merged = packs.Concat(own.Where(p => !packs.Contains(p))).ToList();
            string line = Key + ":" + JsonSerializer.Serialize(merged, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

            if (index >= 0) lines[index] = line; else lines.Add(line);
            File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        }

        private static List<string> ReadList(string line)
        {
            try { return JsonSerializer.Deserialize<List<string>>(line[(Key.Length + 1)..]) ?? new(); }
            catch (JsonException) { return new(); }
        }
    }
}
