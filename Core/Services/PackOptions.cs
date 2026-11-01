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
        /// <summary>Minecraft, sürümü tutmayan bir paketi yalnızca bu listedeyse açık bırakır; yoksa kapatır.</summary>
        private const string IncompatibleKey = "incompatibleResourcePacks";

        private sealed record Data(List<string> ResourcePacks, List<string>? IncompatibleResourcePacks = null);

        /// <summary>Instance'ta açık kaynak paketleri (options.txt'de yoksa ya da okunamazsa null).</summary>
        public static List<string>? ReadResourcePacks(string instanceDir)
        {
            return ReadOption(instanceDir, Key);
        }

        /// <summary>Paylaşanın sürüm uyarısını onaylayıp açık bıraktığı kaynak paketleri.</summary>
        public static List<string>? ReadIncompatiblePacks(string instanceDir) => ReadOption(instanceDir, IncompatibleKey);

        private static List<string>? ReadOption(string instanceDir, string key)
        {
            string path = Path.Combine(instanceDir, "options.txt");
            if (!File.Exists(path)) return null;
            foreach (var line in File.ReadLines(path))
            {
                if (!line.StartsWith(key + ":", StringComparison.Ordinal)) continue;
                try { return JsonSerializer.Deserialize<List<string>>(line[(key.Length + 1)..]); }
                catch (JsonException) { return null; }
            }
            return null;
        }

        public static byte[] Serialize(List<string> resourcePacks, List<string>? incompatiblePacks = null) =>
            JsonSerializer.SerializeToUtf8Bytes(new Data(resourcePacks, incompatiblePacks), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        /// <summary>
        /// Paketteki listeyi kuranın options.txt'sine yazar: önce paketin açık kaynak paketleri (aynı sırayla),
        /// sonra kullanıcının kendi açtıkları. Diğer ayarlara dokunulmaz; options.txt yoksa yalnızca bu satırla oluşur.
        /// </summary>
        public static void Apply(string instanceDir, Stream json)
        {
            Data? data;
            try { data = JsonSerializer.Deserialize<Data>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (JsonException) { return; }
            if (data?.ResourcePacks == null) return;

            string path = Path.Combine(instanceDir, "options.txt");
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            MergeLine(lines, Key, data.ResourcePacks);
            if (data.IncompatibleResourcePacks != null) MergeLine(lines, IncompatibleKey, data.IncompatibleResourcePacks);
            File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        }

        /// <summary>Paketin listesi öne, kullanıcının kendi eklediklerini arkaya koyar.</summary>
        private static void MergeLine(List<string> lines, string key, List<string> incoming)
        {
            var packs = incoming.Where(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 300 && !p.Any(char.IsControl)).Distinct().Take(200).ToList();
            int index = lines.FindIndex(l => l.StartsWith(key + ":", StringComparison.Ordinal));
            var own = index >= 0 ? ReadList(lines[index], key) : new List<string>();
            var merged = packs.Concat(own.Where(p => !packs.Contains(p))).ToList();
            string line = key + ":" + JsonSerializer.Serialize(merged, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            if (index >= 0) lines[index] = line; else lines.Add(line);
        }

        private static List<string> ReadList(string line, string key)
        {
            try { return JsonSerializer.Deserialize<List<string>>(line[(key.Length + 1)..]) ?? new(); }
            catch (JsonException) { return new(); }
        }
    }
}
