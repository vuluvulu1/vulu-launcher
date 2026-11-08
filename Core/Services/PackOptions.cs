using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    /// <summary>
    /// Paketle birlikte taşınan oyun seçenekleri. options.txt kişisel ayarlar (ses, FOV) içerdiği için bütünüyle
    /// paylaşılmaz; paketin dosya zip'ine <see cref="FileName"/> olarak yalnızca şunlar konur ve kuranın
    /// options.txt'sine işlenir:
    /// - açık kaynak paketlerinin listesi,
    /// - tuş atamaları (paylaşanın çakışmasız düzeni; kuranın kendi seçtikleri korunur, bkz. <see cref="MergeKeys"/>).
    /// Shader seçimi zaten paylaşılan dosyalarda durur (Iris/Oculus: config/, OptiFine: optionsshaders.txt).
    /// </summary>
    internal static partial class PackOptions
    {
        public const string FileName = "vulu-pack.json";
        /// <summary>Paketten en son yazılan tuşlar; kullanıcının sonradan değiştirdiğini ayırt etmek için. Paylaşılmaz.</summary>
        public const string AppliedKeysFile = "vulu-pack-keys.json";
        private const string Key = "resourcePacks";
        /// <summary>Minecraft, sürümü tutmayan bir paketi yalnızca bu listedeyse açık bırakır; yoksa kapatır.</summary>
        private const string IncompatibleKey = "incompatibleResourcePacks";
        private const int MaxKeys = 1500;

        private sealed record Data(List<string>? ResourcePacks, List<string>? IncompatibleResourcePacks = null, Dictionary<string, string>? Keys = null);

        private static readonly JsonSerializerOptions WriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

        [GeneratedRegex(@"^[A-Za-z0-9_.:\-]{1,120}$")] private static partial Regex KeyName();
        [GeneratedRegex(@"^key\.[a-z0-9_.\-]{1,60}$")] private static partial Regex KeyValue();

        /// <summary>Instance'ta açık kaynak paketleri (options.txt'de yoksa ya da okunamazsa null).</summary>
        public static List<string>? ReadResourcePacks(string instanceDir)
        {
            return ReadOption(instanceDir, Key);
        }

        /// <summary>Paylaşanın sürüm uyarısını onaylayıp açık bıraktığı kaynak paketleri.</summary>
        public static List<string>? ReadIncompatiblePacks(string instanceDir) => ReadOption(instanceDir, IncompatibleKey);

        /// <summary>options.txt'deki bütün tuş atamaları ("key_" satırları): eylem → tuş. Dosya yoksa null.</summary>
        public static Dictionary<string, string>? ReadKeys(string instanceDir)
        {
            string path = Path.Combine(instanceDir, "options.txt");
            if (!File.Exists(path)) return null;
            var keys = ParseKeys(File.ReadLines(path));
            return keys.Count > 0 ? keys : null;
        }

        private static Dictionary<string, string> ParseKeys(IEnumerable<string> lines)
        {
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                if (!line.StartsWith("key_", StringComparison.Ordinal)) continue;
                int colon = line.LastIndexOf(':');
                if (colon <= 4) continue;
                keys[line[4..colon]] = line[(colon + 1)..];
            }
            return keys;
        }

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

        public static byte[] Serialize(List<string>? resourcePacks, List<string>? incompatiblePacks = null, Dictionary<string, string>? keys = null) =>
            JsonSerializer.SerializeToUtf8Bytes(new Data(resourcePacks, incompatiblePacks, keys), WriteOptions);

        /// <summary>
        /// Paketteki seçenekleri kuranın options.txt'sine yazar. Kaynak paketleri: önce paketin açtıkları (aynı sırayla),
        /// sonra kullanıcının kendi açtıkları. Tuşlar: <see cref="MergeKeys"/>. Diğer ayarlara dokunulmaz.
        /// </summary>
        public static void Apply(string instanceDir, Stream json)
        {
            Data? data;
            try { data = JsonSerializer.Deserialize<Data>(json, ReadOptions); }
            catch (JsonException) { return; }
            if (data == null || (data.ResourcePacks == null && data.Keys == null)) return;

            string path = Path.Combine(instanceDir, "options.txt");
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            if (data.ResourcePacks != null)
            {
                MergeLine(lines, Key, data.ResourcePacks);
                if (data.IncompatibleResourcePacks != null) MergeLine(lines, IncompatibleKey, data.IncompatibleResourcePacks);
            }
            if (data.Keys != null) MergeKeys(instanceDir, lines, data.Keys);
            File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        }

        /// <summary>
        /// Paketin tuşlarını işler, kullanıcının kendi seçimlerini bozmadan:
        /// - satır hiç yoksa (ilk kurulum) paketin tuşu yazılır,
        /// - satır paketten en son yazılan değerde duruyorsa (kullanıcı dokunmamış) yeni değer yazılır,
        /// - paketten daha önce hiç yazılmamışsa yalnızca o tuş başka bir eylemle çakışıyorsa düzeltilir
        ///   (modların varsayılanları çakışır; kişisel seçimler genelde çakışmaz).
        /// Yazılanlar <see cref="AppliedKeysFile"/>'a kaydedilir.
        /// </summary>
        private static void MergeKeys(string instanceDir, List<string> lines, Dictionary<string, string> incoming)
        {
            var applied = ReadApplied(instanceDir);
            var current = ParseKeys(lines);
            // F3 kombinasyonları (key.debug.*) yalnızca F3 basılıyken çalışır; çakışma sayılmaz.
            var usage = current.Where(k => !k.Key.StartsWith("key.debug.", StringComparison.Ordinal) && k.Value != "key.keyboard.unknown")
                .GroupBy(k => k.Value).ToDictionary(g => g.Key, g => g.Count());

            foreach (var (name, value) in incoming.Take(MaxKeys))
            {
                if (!KeyName().IsMatch(name) || !KeyValue().IsMatch(value)) continue;
                bool exists = current.TryGetValue(name, out var mine);
                bool write;
                if (!exists) write = true;
                else if (mine == value) write = false;
                else if (applied.TryGetValue(name, out var last)) write = last == mine;
                else write = mine != "key.keyboard.unknown" && usage.GetValueOrDefault(mine!) > 1;

                if (write)
                {
                    string line = $"key_{name}:{value}";
                    int index = lines.FindIndex(l => l.StartsWith($"key_{name}:", StringComparison.Ordinal) && l.LastIndexOf(':') == name.Length + 4);
                    if (index >= 0) lines[index] = line; else lines.Add(line);
                    if (exists && mine != "key.keyboard.unknown") usage[mine!] = usage.GetValueOrDefault(mine!) - 1;
                    current[name] = value;
                }
                if (write || mine == value) applied[name] = value;
            }

            try { File.WriteAllText(Path.Combine(instanceDir, AppliedKeysFile), JsonSerializer.Serialize(applied)); }
            catch (IOException) { }
        }

        private static Dictionary<string, string> ReadApplied(string instanceDir)
        {
            try
            {
                string path = Path.Combine(instanceDir, AppliedKeysFile);
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new()
                    : new();
            }
            catch (Exception e) when (e is IOException or JsonException) { return new(); }
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
