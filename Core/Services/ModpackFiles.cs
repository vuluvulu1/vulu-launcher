using System.IO.Compression;

namespace VuluLauncher
{
    /// <summary>
    /// Modpack sürüm değiştirmede paketin getirdiği dosyalar: hangileri paketin (mods, resourcepacks, shaderpacks),
    /// eskisinin yenisinde olmayanlarının kaldırılması ve değişimden önce ayar klasörlerinin yedeği.
    /// Sonradan kendi eklenen içerik paketin listesinde olmadığı için dokunulmaz; dünyalara hiç dokunulmaz.
    /// </summary>
    internal static class ModpackFiles
    {
        private static readonly string[] ContentDirs = ["mods/", "resourcepacks/", "shaderpacks/"];
        private static readonly string[] ConfigItems = ["config", "defaultconfigs", "kubejs", "options.txt"];
        public const string BackupFolder = "surum-yedekleri";

        public static string Normalize(string relative) => relative.Replace('\\', '/').TrimStart('/');

        public static bool IsContent(string relative)
        {
            string r = Normalize(relative);
            return ContentDirs.Any(d => r.StartsWith(d, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Overrides içindeki içerik dosyaları (mods/ vb.), instance klasörüne göre yollarıyla.</summary>
        public static IEnumerable<string> OverrideContent(ZipArchive zip, string prefix) =>
            zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && !e.FullName.EndsWith('/'))
                .Select(e => Normalize(e.FullName[prefix.Length..])).Where(IsContent);

        /// <summary>Eski paketin dosyalarından yenisinde olmayanlar silinir (aynı yoldakiler yeniden indirilir ya da yerinde kalır).</summary>
        public static int RemoveOld(string instanceDir, IEnumerable<string> oldFiles, IEnumerable<string> newFiles)
        {
            var keep = newFiles.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int removed = 0;
            foreach (var rel in oldFiles.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (keep.Contains(rel) || !IsContent(rel)) continue;
                if (Downloader.ResolveInside(instanceDir, rel) is { } path && File.Exists(path))
                {
                    try { Downloader.MakeWritable(path); File.Delete(path); removed++; } catch (IOException) { }
                }
            }
            return removed;
        }

        /// <summary>Ayar klasörlerinin (config, defaultconfigs, kubejs, options.txt) yedeği: surum-yedekleri/&lt;sürüm&gt;-&lt;tarih&gt;.zip.</summary>
        public static string? BackupConfigs(string instanceDir, string? version)
        {
            var items = ConfigItems.Select(i => (Name: i, Path: Path.Combine(instanceDir, i))).Where(i => File.Exists(i.Path) || Directory.Exists(i.Path)).ToList();
            if (items.Count == 0) return null;
            string safe = new string((version ?? "eski").Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());
            if (safe.Length > 40) safe = safe[..40];
            string dir = Path.Combine(instanceDir, BackupFolder);
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, $"{safe}-{DateTime.Now:yyyyMMdd-HHmm}.zip");
            using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
            foreach (var (name, path) in items)
            {
                if (File.Exists(path)) { zip.CreateEntryFromFile(path, name, CompressionLevel.Fastest); continue; }
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    zip.CreateEntryFromFile(file, Normalize(Path.Combine(name, Path.GetRelativePath(path, file))), CompressionLevel.Fastest);
            }
            return target;
        }
    }
}
