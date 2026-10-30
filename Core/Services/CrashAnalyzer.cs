using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    /// <summary>Çökme yardımcısının bir bulgusu.</summary>
    /// <param name="Kind">missing | mismatch | mixin | modError | java | ram-start | ram-heap | duplicate | driver | unknown</param>
    /// <param name="ModIds">Bulguyla ilgili mod kimlikleri (eksikler ya da suçlu mod).</param>
    internal sealed record CrashFinding(string Kind, string Title, string Detail, string Fix, IReadOnlyList<string> ModIds);

    internal sealed record CrashAnalysis(bool Crashed, string? ReportFile, string? Description, IReadOnlyList<CrashFinding> Findings);

    /// <summary>
    /// Oyun kapandıktan sonra çökme raporunu, logs/latest.log'u ve oyun çıktısını okuyup bilinen hata kalıplarına göre
    /// sade bir açıklama üretir. Yalnızca okur; okunan miktar sınırlıdır. Kalıplar vulu Server Panel'in çökme
    /// çözümleyicisinden gelir ve oyuncu tarafına (NeoForge yükleme ekranı, mod kodundaki hatalar, sürücüler) genişletilmiştir.
    /// </summary>
    internal static class CrashAnalyzer
    {
        private const int ReadMax = 768 * 1024;
        private const string Id = @"[a-z0-9_.\-]{1,64}";
        // Oyunun kendisi ya da yükleyici: suçlu mod sayılmaz
        private static readonly HashSet<string> NotMods = new(StringComparer.OrdinalIgnoreCase)
        {
            "minecraft", "neoforge", "forge", "fml", "java.base", "fabricloader", "mixin", "mixinextras", "loader", "javafml",
        };

        // Yığın izinde mod sayılmayan paketler (oyun, Java, yükleyici, ortak kütüphaneler)
        private static readonly string[] NotModPackages =
        {
            "net.minecraft.", "com.mojang.", "java.", "javax.", "jdk.", "sun.", "org.spongepowered.", "net.fabricmc.",
            "org.quiltmc.", "net.neoforged.", "net.minecraftforge.", "cpw.mods.", "org.lwjgl.", "io.netty.", "com.google.",
            "it.unimi.", "org.apache.", "org.slf4j.", "org.objectweb.", "com.llamalad7.", "kotlin.", "org.joml.", "oshi.",
        };

        /// <param name="classOwner">Bir sınıf adını (com.ornek.mod.Sinif) içeren modun kimliğine çevirir. Fabric çökme raporları
        /// suçlu modu yazmaz; yığın izindeki ilk mod sınıfı mod dosyalarında aranarak bulunur.</param>
        public static CrashAnalysis Analyze(GameExit exit, Func<string, string?>? classOwner = null)
        {
            var (reportName, report) = LatestReport(exit.InstanceDir, exit.StartedUtc);
            string log = ReadTail(Path.Combine(exit.InstanceDir, "logs", "latest.log"), exit.StartedUtc);
            string output = string.Join("\n", exit.Output);
            string text = report + "\n" + log + "\n" + output;

            bool loadError = Regex.IsMatch(text, @"Error loading mods|ModLoadingException|LoadingFailedException|Missing or unsupported mandatory dependencies|Incompatible mods? found|which is missing!");
            // Oyun tam açıldıktan sonra kanıtsız sıfırdan farklı çıkış çökme sayılmaz: oyuncu pencereyi kapatmış ya da süreci
            // sonlandırmıştır (bazı modlar kapanırken de çıkış kodunu bozar). Gerçek çökme rapor ya da hs_err dosyası bırakır.
            bool started = Regex.IsMatch(text, @"Stopping!|Created: \d+x\d+x\d+ minecraft:textures/atlas/");
            bool evidence = report.Length > 0 || loadError || JvmCrashed(exit.InstanceDir, exit.StartedUtc) || text.Contains("java.lang.OutOfMemoryError");
            bool crashed = evidence || (exit.ExitCode != 0 && !started);
            if (!crashed) return new CrashAnalysis(false, null, null, Array.Empty<CrashFinding>());

            var found = new List<CrashFinding>();
            void Add(string kind, string title, string detail, string fix, IEnumerable<string>? ids = null)
            {
                if (found.Any(f => f.Kind == kind)) return;
                found.Add(new CrashFinding(kind, title, detail, fix, (ids ?? Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList()));
            }

            // ── Eksik bağımlılık ──────────────────────────────────────────
            var missing = new List<(string Dep, string By)>();
            // NeoForge: "Mod zoomify requires yet_another_config_lib_v3 any" + "Currently, yet_another_config_lib_v3 is not installed"
            foreach (Match m in Regex.Matches(text, @"Mod (" + Id + @") requires (" + Id + @") [^\n]{0,80}\n?[^\n]{0,20}Currently, \2 is not installed"))
                missing.Add((m.Groups[2].Value, m.Groups[1].Value));
            // Fabric: "Mod 'Zoomify' (zoomify) 2.1 requires any version of mod 'YACL' (yet_another_config_lib_v3), which is missing!"
            foreach (Match m in Regex.Matches(text, @"Mod '([^'\n]{1,80})' \((" + Id + @")\)[^\n]{0,40}? requires ([^\n]{1,200}?), which is missing"))
            {
                var ids = Regex.Matches(m.Groups[3].Value, @"\((" + Id + @")\)").Select(x => x.Groups[1].Value).ToList();
                missing.Add((ids.Count > 0 ? ids[^1] : m.Groups[3].Value.Split(' ')[^1].Trim('\''), m.Groups[1].Value));
            }
            // Forge: "Mod ID: 'cloth_config', Requested by: 'zoomify'"
            foreach (Match m in Regex.Matches(text, @"Mod ID: '(" + Id + @")', Requested by: '(" + Id + @")'"))
                missing.Add((m.Groups[1].Value, m.Groups[2].Value));
            if (missing.Count > 0)
            {
                Add("missing", "Eksik mod",
                    string.Join("; ", missing.GroupBy(x => x.Dep).Select(g => $"{g.Key} (isteyen: {string.Join(", ", g.Select(x => x.By).Distinct())})")),
                    "Eksik modu kurup oyunu yeniden başlat.", missing.Select(x => x.Dep));
            }

            // ── Sürüm uyuşmazlığı: "Mod x requires y 1.2 or above" + "Currently, y is 1.0" ──
            var mismatch = Regex.Matches(text, @"Mod (" + Id + @") requires (" + Id + @") ([^\n]{1,60})\n?[^\n]{0,20}Currently, \2 is (?!not\b)([^\n\s]{1,40})");
            if (mismatch.Count > 0)
            {
                var m = mismatch[0];
                Add("mismatch", "Bir modun sürümü uymuyor",
                    $"{m.Groups[1].Value}, {m.Groups[2].Value} için {m.Groups[3].Value.Trim()} istiyor; kurulu olan {m.Groups[4].Value}.",
                    $"{m.Groups[2].Value} modunu uygun sürüme güncelle ya da {m.Groups[1].Value} modunun başka bir sürümünü dene.",
                    new[] { m.Groups[2].Value, m.Groups[1].Value });
            }

            // ── Java sürümü ───────────────────────────────────────────────
            if (Regex.Match(text, @"UnsupportedClassVersionError.*?class file version (\d+)\.0") is { Success: true } jv)
            {
                int need = int.Parse(jv.Groups[1].Value) - 44;
                Add("java", $"Java {need} gerekiyor", $"Bir dosya Java {need} ile derlenmiş, ama oyun daha eski bir Java ile açıldı.",
                    "Ayarlar → Oyun → Java'yı \"Otomatik\" yap.");
            }

            // ── Bellek ────────────────────────────────────────────────────
            if (text.Contains("Could not reserve enough space for object heap") || text.Contains("insufficient memory for the Java Runtime"))
                Add("ram-start", "Bilgisayarda yeterli boş bellek yok", "Java, ayrılan RAM'i Windows'tan alamadı.",
                    "Diğer programları kapat ya da Oyna'daki RAM ayarını düşür.");
            else if (Regex.IsMatch(text, @"java\.lang\.OutOfMemoryError(: Java heap space|: GC overhead)?"))
                Add("ram-heap", "Oyunun belleği doldu", "Oyun sırasında ayrılan RAM yetmedi (çok mod ya da yüksek görüş mesafesi).",
                    "Oyna'daki RAM ayarını artır ya da oyunda görüş mesafesini düşür.");

            // ── Mixin: bir modun kodu oyunla uyuşmuyor ────────────────────
            if (Regex.IsMatch(text, @"MixinApplyError|InvalidInjectionException|MixinTransformerError|Mixin apply failed|InvalidMixinException"))
            {
                var mods = Regex.Matches(text, @"Mixin \[[^\]\n]{1,200} from mod (" + Id + @")\][^\n]{0,120}(?:FAILED|failed)")
                    .Select(x => x.Groups[1].Value)
                    .Concat(Regex.Matches(text, @"Mixin apply (?:for mod|failed) (" + Id + ")").Select(x => x.Groups[1].Value))
                    .Where(x => !NotMods.Contains(x)).Distinct().ToList();
                Add("mixin", mods.Count > 0 ? $"{string.Join(", ", mods)} bu sürümle uyumsuz" : "Modlar arasında uyumsuzluk (Mixin hatası)",
                    "Mod, Minecraft'ın ya da yükleyicinin kodunu değiştiremedi. Genelde modun bu Minecraft/yükleyici sürümüne uymadığını gösterir.",
                    "Modun daha yeni (gerekirse beta) bir sürümünü kur ya da modu geçici olarak kapat.", mods);
            }

            // ── Bir modun kendi hatası (çökme raporunun ilk mod satırı) ────
            if (report.Length > 0 && !found.Any(f => f.Kind == "mixin"))
            {
                string head = report.Length > 12000 ? report[..12000] : report;
                var culprit = Regex.Matches(head, @"at (?:TRANSFORMER/|MC-BOOTSTRAP/|PLUGIN/|GAME/)?(" + Id + @")@[^/\s]+/")
                    .Select(x => x.Groups[1].Value).FirstOrDefault(x => !NotMods.Contains(x));
                var suspected = Regex.Match(report, @"Suspected Mods?: ([^\n]{1,300})");
                if (culprit == null && suspected.Success && !Regex.IsMatch(suspected.Groups[1].Value, "^(None|Unknown)", RegexOptions.IgnoreCase))
                    culprit = Regex.Match(suspected.Groups[1].Value, @"\((" + Id + @")\)") is { Success: true } sm ? sm.Groups[1].Value : null;
                culprit ??= CulpritFromStack(head, classOwner);
                if (culprit != null)
                    Add("modError", $"{culprit} modu çöktü",
                        "Çökme bu modun kodunda başladı. Genelde modun bir hatasıdır ya da başka bir modla/sürümle uyuşmazlıktır.",
                        "Modu güncelle; düzelmezse geçici olarak kapatıp dene.", new[] { culprit });
            }

            // ── Aynı mod iki kez ──────────────────────────────────────────
            if (Regex.IsMatch(text, @"[Dd]uplicate mods? found|DuplicateModsFoundException|Found a duplicate mod"))
            {
                var dups = Regex.Matches(text, @"(?:Mod ID|mod) '(" + Id + @")'").Select(x => x.Groups[1].Value).Distinct().ToList();
                Add("duplicate", "Aynı mod iki kez kurulu", dups.Count > 0 ? "Mod: " + string.Join(", ", dups) : "mods klasöründe aynı modun iki dosyası var.",
                    "İçerik'ten eski sürümü sil.", dups);
            }

            // ── Ekran kartı sürücüsü ──────────────────────────────────────
            if (Regex.IsMatch(text, @"(atio6axx|atioglxx|nvoglv|ig\d+icd)\w*\.dll", RegexOptions.IgnoreCase) && text.Contains("EXCEPTION_ACCESS_VIOLATION")
                || Regex.IsMatch(text, @"GLFW error 65542|driver does not appear to support OpenGL|Pixel format not accelerated"))
                Add("driver", "Ekran kartı sürücüsü sorunu", "Çökme ekran kartı sürücüsünün içinde oldu ya da sürücü OpenGL'i desteklemiyor.",
                    "Ekran kartı sürücünü (NVIDIA, AMD ya da Intel) güncelle. Shader kullanıyorsan geçici olarak kapat.");

            string? description = Regex.Match(report, @"^Description: ([^\n]{1,200})", RegexOptions.Multiline) is { Success: true } d ? d.Groups[1].Value.Trim() : null;
            if (found.Count == 0)
                Add("unknown", "Oyun beklenmedik şekilde kapandı",
                    description != null ? $"Çökme raporu: \"{description}\"." : $"Çıkış kodu {exit.ExitCode}; bilinen bir neden bulunamadı.",
                    "Çökme raporunu aç; son eklediğin modu kapatıp tekrar dene.");
            return new CrashAnalysis(true, reportName, description, found);
        }

        /// <summary>
        /// Fabric: yığın izinde modun kendi sınıfı (com.erikedits.justquests.client.QuestScreen) ya da oyun sınıfına eklenmiş
        /// bir mixin yöntemi (net.minecraft.X.handler$abc000$justquests$render) aranır. İlk bulunan mod suçlu sayılır.
        /// </summary>
        internal static string? CulpritFromStack(string report, Func<string, string?>? classOwner)
        {
            foreach (Match frame in Regex.Matches(report, @"^\s*at ([\w$.]{3,300})\.([\w$<>]{1,200})\(", RegexOptions.Multiline).Take(60))
            {
                string cls = frame.Groups[1].Value, method = frame.Groups[2].Value;
                if (Regex.Match(method, @"^(?:handler|redirect|wrapOperation|wrapWithCondition|modify\w*|localvar|inject)\$\w+?\$(" + Id + @")\$") is { Success: true } mixin
                    && !NotMods.Contains(mixin.Groups[1].Value))
                    return mixin.Groups[1].Value;
                if (classOwner == null || NotModPackages.Any(p => cls.StartsWith(p, StringComparison.Ordinal))) continue;
                if (classOwner(cls) is { } owner && !NotMods.Contains(owner)) return owner;
            }
            return null;
        }

        /// <summary>Java'nın kendisi çöktüyse oyun klasörüne hs_err_pid*.log yazar.</summary>
        private static bool JvmCrashed(string instanceDir, DateTime startedUtc)
        {
            try
            {
                return new DirectoryInfo(instanceDir).EnumerateFiles("hs_err_pid*.log").Any(f => f.LastWriteTimeUtc >= startedUtc.AddSeconds(-5));
            }
            catch (Exception) { return false; }
        }

        /// <summary>Bu oyun açıldıktan sonra yazılmış en yeni çökme raporu.</summary>
        private static (string? Name, string Text) LatestReport(string instanceDir, DateTime startedUtc)
        {
            string dir = Path.Combine(instanceDir, "crash-reports");
            if (!Directory.Exists(dir)) return (null, "");
            var file = new DirectoryInfo(dir).EnumerateFiles("crash-*.txt")
                .Where(f => f.LastWriteTimeUtc >= startedUtc.AddSeconds(-5))
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            return file == null ? (null, "") : (file.Name, ReadHead(file.FullName));
        }

        private static string ReadHead(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buffer = new byte[Math.Min(stream.Length, ReadMax)];
                int read = stream.Read(buffer, 0, buffer.Length);
                return Encoding.UTF8.GetString(buffer, 0, read).Replace("\r", "");
            }
            catch (Exception) { return ""; }
        }

        /// <summary>logs/latest.log'un sonu (oyun bu çalıştırmada yazdıysa).</summary>
        private static string ReadTail(string path, DateTime startedUtc)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.LastWriteTimeUtc < startedUtc.AddSeconds(-5)) return "";
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long start = Math.Max(0, stream.Length - ReadMax);
                stream.Seek(start, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd().Replace("\r", "");
            }
            catch (Exception) { return ""; }
        }

        /// <summary>Test ve eski kayıtlar için: .log.gz dosyasını açar.</summary>
        internal static string ReadGz(string path)
        {
            using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.UTF8);
            return reader.ReadToEnd().Replace("\r", "");
        }
    }
}
