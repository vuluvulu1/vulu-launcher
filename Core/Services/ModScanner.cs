using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    /// <summary>
    /// Bir mod jar'ının tanımı: verdiği kimlikler (gömülü jar'larınkiler dahil), zorunlu bağımlılıkları ve jar'ın asıl
    /// kimlikleri (gömülü kütüphaneler hariç; aynı modun iki sürümünü ayırt etmek için).
    /// </summary>
    internal sealed record ModInfo(string File, string Name, IReadOnlyList<string> Ids, IReadOnlyList<string> Requires, bool ClientOnly,
        IReadOnlyList<string> PrimaryIds);

    /// <summary>Eksik bir bağımlılık ve onu isteyen modların adları.</summary>
    internal sealed record MissingDependency(string Id, IReadOnlyList<string> RequiredBy);

    internal sealed record ModScan(IReadOnlyList<ModInfo> Mods, IReadOnlyList<MissingDependency> Missing)
    {
        /// <summary>Kimliği verilen modun dosyası ve adı (çökme raporundaki "fancymenu" → FancyMenu, mods/fancymenu.jar).</summary>
        public ModInfo? Find(string id) => Mods.FirstOrDefault(m => m.Ids.Contains(id, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// mods klasöründeki jar'ları okur (fabric.mod.json, quilt.mod.json, META-INF/neoforge.mods.toml, META-INF/mods.toml)
    /// ve kurulu olmayan zorunlu bağımlılıkları bulur. Jar içindeki gömülü jar'ların (jar-in-jar) verdiği kimlikler de sayılır.
    /// Yalnızca okur; dosya ve iç içe derinlik sınırlıdır. Sonuç dosya boyutu/tarihine göre önbellekte tutulur.
    /// </summary>
    internal static class ModScanner
    {
        // Yükleyicinin ya da oyunun kendisi; bağımlılık olarak kurulmaları gerekmez
        private static readonly HashSet<string> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
        {
            "minecraft", "java", "fabricloader", "fabric-loader", "quilt_loader", "quilt_base", "forge", "neoforge",
            "javafml", "lowcodefml", "fml", "mixinextras", "mixin",
        };
        private const int MetaMax = 256 * 1024, NestedMax = 32 * 1024 * 1024, MaxDepth = 2;
        private static readonly Dictionary<string, (long Size, DateTime Time, ModInfo Info)> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new();

        /// <param name="loader">Instance'ın yükleyicisi: birden çok yükleyiciye uyan jar'larda hangi tanımın okunacağını belirler.</param>
        public static ModScan Scan(string instanceDir, string loader)
        {
            string modsDir = Path.Combine(instanceDir, "mods");
            var mods = new List<ModInfo>();
            if (Directory.Exists(modsDir))
            {
                foreach (var path in Directory.EnumerateFiles(modsDir, "*.jar"))
                {
                    var info = Read(path, loader);
                    if (info != null) mods.Add(info);
                }
            }

            var provided = new HashSet<string>(mods.SelectMany(m => m.Ids), StringComparer.OrdinalIgnoreCase);
            if (provided.Contains("fabric-api")) provided.Add("fabric");   // Fabric API'nin eski kimliği
            var missing = mods
                .SelectMany(m => m.Requires.Select(dep => (Dep: dep, By: m.Name)))
                .Where(x => !BuiltIn.Contains(x.Dep) && !provided.Contains(x.Dep))
                .GroupBy(x => x.Dep, StringComparer.OrdinalIgnoreCase)
                .Select(g => new MissingDependency(g.Key, g.Select(x => x.By).Distinct().ToList()))
                .OrderBy(m => m.Id)
                .ToList();
            return new ModScan(mods, missing);
        }

        private static ModInfo? Read(string path, string loader)
        {
            var file = new FileInfo(path);
            string key = loader + "|" + path;
            lock (Gate)
                if (Cache.TryGetValue(key, out var hit) && hit.Size == file.Length && hit.Time == file.LastWriteTimeUtc)
                    return hit.Info;

            ModInfo? info = null;
            try
            {
                using var zip = ZipFile.OpenRead(path);
                info = ReadZip(zip, Path.GetFileName(path), loader, 0);
            }
            catch (Exception) { /* bozuk ya da mod olmayan jar: atlanır */ }

            if (info != null)
                lock (Gate) Cache[key] = (file.Length, file.LastWriteTimeUtc, info);
            return info;
        }

        // Yükleyiciye göre okunacak tanım dosyalarının sırası (ilk bulunan kullanılır)
        private static readonly string[] FabricFirst = { "fabric.mod.json", "quilt.mod.json", "META-INF/neoforge.mods.toml", "META-INF/mods.toml" };
        private static readonly string[] QuiltFirst = { "quilt.mod.json", "fabric.mod.json", "META-INF/neoforge.mods.toml", "META-INF/mods.toml" };
        private static readonly string[] NeoForgeFirst = { "META-INF/neoforge.mods.toml", "META-INF/mods.toml", "fabric.mod.json", "quilt.mod.json" };
        private static readonly string[] ForgeFirst = { "META-INF/mods.toml", "META-INF/neoforge.mods.toml", "fabric.mod.json", "quilt.mod.json" };

        private static ModInfo? ReadZip(ZipArchive zip, string label, string loader, int depth)
        {
            var ids = new List<string>();
            var requires = new List<string>();
            string? name = null;
            bool clientOnly = false;

            var order = loader switch { "neoforge" => NeoForgeFirst, "forge" => ForgeFirst, "quilt" => QuiltFirst, _ => FabricFirst };
            bool parsed = false;
            foreach (var meta in order)
            {
                if (Text(zip, meta) is not { } text) continue;
                if (meta == "fabric.mod.json") ParseFabric(text, ids, requires, ref name, ref clientOnly);
                else if (meta == "quilt.mod.json") ParseQuilt(text, ids, requires, ref name, ref clientOnly);
                else ParseToml(text, ids, requires, ref name);
                parsed = true;
                break;
            }
            // Dil sağlayıcıları (Kotlin for Forge, Scalable Cat's Force): mod tanımı yok, adı jar'ın modül adı.
            // Modlar onlara normal bir mod gibi bağımlı olur ("kotori_scala").
            if (LanguageProviderId(zip) is { } provider) { ids.Add(provider); parsed = true; }
            var own = ids.ToList();

            // Gömülü jar'lar: verdikleri kimlikler bu jar'ın kimlikleri sayılır (bağımlılıkları sayılmaz)
            if (depth < MaxDepth)
            {
                foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                             && (e.FullName.StartsWith("META-INF/jars/", StringComparison.Ordinal) || e.FullName.StartsWith("META-INF/jarjar/", StringComparison.Ordinal))
                             && e.Length is > 0 and < NestedMax).Take(200))
                {
                    try
                    {
                        using var ms = new MemoryStream();
                        using (var s = entry.Open()) s.CopyTo(ms);
                        ms.Position = 0;
                        using var nested = new ZipArchive(ms, ZipArchiveMode.Read);
                        if (ReadZip(nested, label, loader, depth + 1) is { } inner) ids.AddRange(inner.Ids);
                    }
                    catch (Exception) { }
                }
            }

            if (ids.Count == 0) return depth == 0 ? null : new ModInfo(label, label, ids, requires, clientOnly, ids);
            // Kendi tanımı olmayan kap jar'ın (ör. kotlinforforge-all) asıl kimlikleri içindekilerdir
            var primary = (parsed ? own : ids).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new ModInfo(label, name ?? ids[0], ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                requires.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), clientOnly, primary);
        }

        private static readonly string[] LanguageServices =
        {
            "META-INF/services/net.neoforged.neoforgespi.language.IModLanguageLoader",
            "META-INF/services/net.minecraftforge.forgespi.language.IModLanguageProvider",
        };

        private static string? LanguageProviderId(ZipArchive zip)
        {
            if (!LanguageServices.Any(s => zip.GetEntry(s) != null) || zip.GetEntry("META-INF/MANIFEST.MF") is not { } entry) return null;
            // Manifest'in yalnızca ana bölümü okunur (kütüphaneli jar'larda her dosyanın özetiyle MB'larca olabiliyor)
            using var reader = new StreamReader(entry.Open());
            for (int i = 0; i < 200 && reader.ReadLine() is { Length: > 0 } line; i++)
            {
                var match = Regex.Match(line, @"^Automatic-Module-Name:\s*([A-Za-z0-9_.\-]+)\s*$");
                if (match.Success) return match.Groups[1].Value;
            }
            return null;
        }

        private static string? Text(ZipArchive zip, string entryName)
        {
            var entry = zip.GetEntry(entryName);
            if (entry == null || entry.Length > MetaMax) return null;
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }

        // ── Fabric / Quilt ────────────────────────────────────────────────
        private static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        private static void ParseFabric(string json, List<string> ids, List<string> requires, ref string? name, ref bool clientOnly)
        {
            using var doc = JsonDocument.Parse(json, Lenient);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) ids.Add(id.GetString()!);
            if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString();
            if (root.TryGetProperty("provides", out var provides) && provides.ValueKind == JsonValueKind.Array)
                ids.AddRange(provides.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!));
            if (root.TryGetProperty("depends", out var depends) && depends.ValueKind == JsonValueKind.Object)
                requires.AddRange(depends.EnumerateObject().Select(p => p.Name));
            clientOnly = root.TryGetProperty("environment", out var env) && env.ValueKind == JsonValueKind.String && env.GetString() == "client";
        }

        private static void ParseQuilt(string json, List<string> ids, List<string> requires, ref string? name, ref bool clientOnly)
        {
            using var doc = JsonDocument.Parse(json, Lenient);
            if (!doc.RootElement.TryGetProperty("quilt_loader", out var ql)) return;
            if (ql.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) ids.Add(id.GetString()!);
            if (ql.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString();
            if (ql.TryGetProperty("provides", out var provides) && provides.ValueKind == JsonValueKind.Array)
                foreach (var p in provides.EnumerateArray())
                    if (p.ValueKind == JsonValueKind.String) ids.Add(p.GetString()!);
                    else if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("id", out var pid)) ids.Add(pid.GetString()!);
            if (ql.TryGetProperty("depends", out var depends) && depends.ValueKind == JsonValueKind.Array)
                foreach (var d in depends.EnumerateArray())
                    if (d.ValueKind == JsonValueKind.String) requires.Add(d.GetString()!);
                    else if (d.ValueKind == JsonValueKind.Object && d.TryGetProperty("id", out var did)
                             && !(d.TryGetProperty("optional", out var opt) && opt.ValueKind == JsonValueKind.True))
                        requires.Add(did.GetString()!);
            clientOnly = doc.RootElement.TryGetProperty("minecraft", out var mc) && mc.TryGetProperty("environment", out var env) && env.GetString() == "client";
        }

        // ── Forge / NeoForge (mods.toml) ──────────────────────────────────
        private static readonly Regex Section = new(@"^\[\[\s*(mods|dependencies\.[^\]\s]+)\s*\]\]$", RegexOptions.Compiled);
        private static readonly Regex InlineArray = new(@"^(mods|dependencies\.[A-Za-z0-9_.\-""']+)\s*=\s*\[", RegexOptions.Compiled);
        private static readonly Regex KeyValue = new(@"^([A-Za-z]+)\s*=\s*(.+)$", RegexOptions.Compiled);
        private static readonly Regex InlinePair = new(@"([A-Za-z]+)\s*=\s*(""[^""]*""|'[^']*'|[^,}\s]+)", RegexOptions.Compiled);

        /// <summary>
        /// Yeterli bir TOML okuyucu. İki yazımı da tanır:
        ///   [[mods]] / [[dependencies.x]] tabloları ve  mods = [{ ... }] / dependencies.x = [{ ... }] satır içi dizileri.
        /// Okunanlar: modId/displayName, bağımlılıklarda modId + mandatory/type/side ve dil sağlayıcı (modLoader).
        /// İstemcide yalnızca sunucuya özel (side=SERVER) bağımlılıklar sayılmaz.
        /// </summary>
        private static void ParseToml(string toml, List<string> ids, List<string> requires, ref string? name)
        {
            string? displayName = null;
            void Take(string section, Dictionary<string, string> block)
            {
                if (section == "mods" && block.TryGetValue("modId", out var mid))
                {
                    ids.Add(mid);
                    if (displayName == null && block.TryGetValue("displayName", out var dn)) displayName = dn;
                }
                else if (section.StartsWith("dependencies.", StringComparison.Ordinal) && block.TryGetValue("modId", out var dep))
                {
                    bool required = block.TryGetValue("type", out var type)
                        ? type.Equals("required", StringComparison.OrdinalIgnoreCase)
                        : block.TryGetValue("mandatory", out var mandatory) && mandatory.Equals("true", StringComparison.OrdinalIgnoreCase);
                    bool serverOnly = block.TryGetValue("side", out var side) && side.Equals("SERVER", StringComparison.OrdinalIgnoreCase);
                    if (required && !serverOnly) requires.Add(dep);
                }
            }

            string? section = null;
            Dictionary<string, string>? block = null;
            void Flush() { if (section != null && block != null) Take(section, block); block = null; }

            var lines = toml.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripComment(lines[i]).Trim();
                if (line.Length == 0) continue;

                // Satır içi dizi: köşeli parantez kapanana kadar (birden çok satıra yayılabilir) okunur
                var inline = InlineArray.Match(line);
                if (inline.Success)
                {
                    Flush();
                    string key = inline.Groups[1].Value.Replace("\"", "").Replace("'", "");
                    var text = new System.Text.StringBuilder(line[inline.Length..]);
                    while (Depth(text.ToString()) > 0 && i + 1 < lines.Length) text.Append(' ').Append(StripComment(lines[++i]));
                    foreach (Match obj in Regex.Matches(text.ToString(), @"\{([^{}]*)\}"))
                        Take(key, InlinePair.Matches(obj.Groups[1].Value).ToDictionary(m => m.Groups[1].Value, m => Unquote(m.Groups[2].Value)));
                    section = null;
                    continue;
                }

                var s = Section.Match(line);
                if (s.Success)
                {
                    Flush();
                    section = s.Groups[1].Value;
                    block = new Dictionary<string, string>(StringComparer.Ordinal);
                    continue;
                }
                if (line.StartsWith('[')) { Flush(); section = null; continue; }

                var kv = KeyValue.Match(line);
                if (!kv.Success) continue;
                string value = Unquote(kv.Groups[2].Value.Trim());
                if (block != null) block[kv.Groups[1].Value] = value;
                // Dil sağlayıcısı (ör. modLoader="klf" → KotlinLangForge) da bir bağımlılıktır
                else if (section == null && kv.Groups[1].Value == "modLoader") requires.Add(value);
            }
            Flush();
            name ??= displayName;
        }

        /// <summary>Tırnak dışındaki # yorumunu atar.</summary>
        private static string StripComment(string line)
        {
            char quote = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c is '"' or '\'') quote = c;
                else if (c == '#') return line[..i];
            }
            return line;
        }

        private static int Depth(string text)
        {
            int depth = 1;   // açılış köşeli parantezi okundu
            char quote = '\0';
            foreach (char c in text)
            {
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c is '"' or '\'') quote = c;
                else if (c == '[') depth++;
                else if (c == ']') depth--;
            }
            return depth;
        }

        private static string Unquote(string value) =>
            value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0] ? value[1..^1] : value.Trim();
    }
}
