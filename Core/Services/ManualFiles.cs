using System.IO.Compression;
using System.Security.Cryptography;

namespace VuluLauncher
{
    /// <summary>Elle indirilen bir dosyanın durumu: yerine kondu, sorunlu (neden) ya da henüz bulunamadı.</summary>
    internal sealed record ManualPlacement(ManualDownload File, string Status, string? Problem = null);

    /// <summary>Bozuk ya da kopya jar'lar ve yerine konmamış elle indirilecekler; oyun açılmadan önce gösterilir.</summary>
    /// <param name="Duplicates">Fazla dosya, kalan dosya ve nedeni (aynı içerik ya da aynı modun başka sürümü).</param>
    internal sealed record ModFileProblems(List<(string File, string Reason)> Broken, List<(string File, string CopyOf, string Reason)> Duplicates,
        List<ManualDownload> PendingManual)
    {
        public bool Any => Broken.Count > 0 || Duplicates.Count > 0 || PendingManual.Count > 0;
    }

    /// <summary>
    /// CurseForge'un elle indirilmesini istediği dosyalar: kullanıcının İndirilenler klasöründe bulunur, SHA-1'i doğrulanır
    /// ve instance'taki yerine kopyalanır. Yanlış sürüm, yarım kalmış (boş) indirme ve "dosya (1).jar" kopyaları ayırt edilir.
    /// Launcher dosyayı kendisi indirmez; CurseForge'un kuralı bu.
    /// </summary>
    internal static class ManualFiles
    {
        public const string BackupFolder = "mods-yedek";
        /// <summary>Aynı anda tek yerleştirme: arayüz İndirilenler'i birkaç saniyede bir tarar, iki tur aynı dosyayı kopyalamasın.</summary>
        private static readonly object PlaceGate = new();

        /// <summary>
        /// Bekleyen dosyaları İndirilenler'de arar; doğru olanları yerine koyar, instance'ın içerik listesine ekler ve
        /// bekleyenlerden çıkarır. Kullanıcı dosyayı instance klasörüne kendisi koyduysa o da doğrulanıp sayılır.
        /// </summary>
        public static List<ManualPlacement> Place(InstanceMeta instance, string? downloadsDir)
        {
            lock (PlaceGate) return PlaceLocked(instance, downloadsDir);
        }

        private static List<ManualPlacement> PlaceLocked(InstanceMeta instance, string? downloadsDir)
        {
            string instanceDir = InstanceStore.PathOf(instance.Folder);
            var candidates = downloadsDir != null && Directory.Exists(downloadsDir)
                ? new DirectoryInfo(downloadsDir).EnumerateFiles().Where(f => f.Extension is ".jar" or ".zip").ToList()
                : new List<FileInfo>();

            var results = new List<ManualPlacement>();
            foreach (var file in instance.PendingManual.ToList())
            {
                string relative = $"{file.Folder}/{file.FileName}";
                string? target = Downloader.ResolveInside(instanceDir, relative);
                if (target == null) { results.Add(new(file, "problem", "Geçersiz dosya adı.")); continue; }

                string? problem = null;
                bool placed = Matches(new FileInfo(target), file);
                if (!placed)
                {
                    // Aynı boyuttakiler hash'le doğrulanır; adı tutup içeriği tutmayanlar neden uymadığıyla bildirilir
                    FileInfo? source = null;
                    try { source = candidates.FirstOrDefault(c => c.Length == file.Length && Matches(c, file)); }
                    catch (IOException) { /* tarayıcı dosyayı hâlâ yazıyor: bir sonraki turda */ }
                    if (source != null)
                    {
                        // Önce yanına kopyalanır, sonra tek hamlede yerine konur; yarım dosya oluşmaz
                        string part = target + ".vulu-part";
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                            File.Copy(source.FullName, part, overwrite: true);
                            File.Move(part, target, overwrite: true);
                            placed = true;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            try { File.Delete(part); } catch { }
                            // Kilitli bir dosyanın üstüne taşıma Windows'ta "erişim engellendi" de verebilir
                            problem = File.Exists(target) || ex is IOException
                                ? "Dosya şu an kullanımda (oyun ya da başka bir program açık olabilir); kapanınca kendiliğinden yerine konur."
                                : "Dosya yerine konamadı: klasöre yazma izni yok.";
                        }
                    }
                    else if (problem != null) { }
                    else if (candidates.FirstOrDefault(c => SameName(c.Name, file.FileName)) is { } named)
                        problem = named.Length == 0 ? $"İndirilenler'deki {named.Name} boş: indirme yarım kalmış, yeniden indir."
                            : $"İndirilenler'deki {named.Name} beklenen dosya değil; bozuk ya da başka bir sürüm.";
                    else if (File.Exists(target))
                        problem = new FileInfo(target).Length == 0 ? "Instance'taki dosya boş: indirme yarım kalmış, yeniden indir."
                            : "Instance'taki dosya beklenen sürüm değil; sayfadaki dosyayı indir.";
                    else if (StemMatch(candidates, file) is { } other)
                        problem = $"İndirilenler'de {other.Name} var ama beklenen {file.FileName}; başka bir sürüm indirilmiş.";
                }

                if (placed)
                {
                    instance.PendingManual.Remove(file);
                    instance.Content.RemoveAll(c => c.Source == CurseForgeService.Source && c.ProjectId == file.ProjectId);
                    instance.Content.Add(new InstalledContent
                    {
                        Source = CurseForgeService.Source,
                        ProjectId = file.ProjectId ?? "",
                        VersionId = file.FileId ?? "",
                        Title = file.Title,
                        VersionNumber = Path.GetFileNameWithoutExtension(file.FileName),
                        Type = file.Type,
                        File = relative,
                        IconUrl = file.IconUrl,
                    });
                    results.Add(new(file, "placed"));
                }
                else results.Add(new(file, problem != null ? "problem" : "waiting", problem));
            }
            if (results.Any(r => r.Status == "placed")) InstanceStore.Save(instance);
            return results;
        }

        /// <summary>
        /// mods klasöründeki boş/açılmayan jar'lar, aynı dosyanın kopyaları ("x (1).jar"), aynı modun iki sürümü ve henüz
        /// yerine konmamış elle indirilecekler. Sorun yoksa null.
        /// </summary>
        public static ModFileProblems? Check(string instanceDir, InstanceMeta? instance, string loader)
        {
            var broken = new List<(string, string)>();
            var duplicates = new List<(string, string, string)>();
            string modsDir = Path.Combine(instanceDir, "mods");
            if (Directory.Exists(modsDir))
            {
                var jars = new DirectoryInfo(modsDir).EnumerateFiles("*.jar").ToList();
                var healthy = new List<FileInfo>();
                foreach (var jar in jars)
                {
                    if (jar.Length == 0) { broken.Add(($"mods/{jar.Name}", "Dosya boş: indirme yarım kalmış.")); continue; }
                    try { using var zip = ZipFile.OpenRead(jar.FullName); healthy.Add(jar); }
                    catch (Exception) { broken.Add(($"mods/{jar.Name}", "Jar dosyası açılmıyor: bozuk ya da yarım inmiş.")); }
                }
                // Önce boyuta, sonra içeriğe göre: aynı içerikli dosyalardan en kısa (ya da "(1)" içermeyen) adlı olan kalır
                foreach (var group in healthy.GroupBy(j => j.Length).Where(g => g.Count() > 1))
                    foreach (var same in group.GroupBy(Sha1Of).Where(g => g.Key != null && g.Count() > 1))
                    {
                        var keep = same.OrderBy(j => CopySuffix.IsMatch(Path.GetFileNameWithoutExtension(j.Name))).ThenBy(j => j.Name.Length).First();
                        foreach (var copy in same.Where(j => j != keep)) duplicates.Add(($"mods/{copy.Name}", keep.Name, "copy"));
                    }

                // Aynı modun iki farklı dosyası (ör. iki sürüm): launcher'ın kurduğu kalır, yoksa en son değiştirilen
                if (loader != "vanilla")
                {
                    var flagged = duplicates.Select(d => d.Item1).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var recorded = instance?.Content.Select(c => c.File).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
                    var mods = ModScanner.Scan(instanceDir, loader).Mods.Where(m => !flagged.Contains($"mods/{m.File}")).ToList();
                    foreach (var group in mods.SelectMany(m => m.PrimaryIds.Select(id => (Id: id, Mod: m)))
                                 .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                                 .Where(g => g.Select(x => x.Mod.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
                    {
                        var files = group.Select(x => x.Mod.File).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        string keep = files.OrderByDescending(f => recorded.Contains($"mods/{f}"))
                            .ThenByDescending(f => File.GetLastWriteTimeUtc(Path.Combine(modsDir, f))).First();
                        foreach (var extra in files.Where(f => f != keep && flagged.Add($"mods/{f}")))
                            duplicates.Add(($"mods/{extra}", keep, "version"));
                    }
                }
            }

            var pending = instance?.PendingManual.Where(m =>
            {
                string? target = Downloader.ResolveInside(instanceDir, $"{m.Folder}/{m.FileName}");
                return target == null || !Matches(new FileInfo(target), m);
            }).ToList() ?? new();

            var problems = new ModFileProblems(broken, duplicates, pending);
            return problems.Any ? problems : null;
        }

        /// <summary>Sorunlu dosyaları silmeden instance'taki mods-yedek klasörüne taşır.</summary>
        public static List<string> MoveToBackup(string instanceDir, IEnumerable<string> files)
        {
            var moved = new List<string>();
            string backup = Path.Combine(instanceDir, BackupFolder);
            foreach (var relative in files.Distinct())
            {
                // Yalnızca mods klasöründeki jar'lar
                if (!relative.StartsWith("mods/", StringComparison.Ordinal) || relative.IndexOf('/', 5) >= 0) continue;
                string? path = Downloader.ResolveInside(instanceDir, relative);
                if (path == null || !File.Exists(path)) continue;
                Directory.CreateDirectory(backup);
                string target = Path.Combine(backup, Path.GetFileName(path));
                for (int i = 2; File.Exists(target); i++)
                    target = Path.Combine(backup, $"{Path.GetFileNameWithoutExtension(path)}.{i}{Path.GetExtension(path)}");
                File.Move(path, target);
                moved.Add(relative);
            }
            return moved;
        }

        private static readonly System.Text.RegularExpressions.Regex CopySuffix = new(@" \(\d+\)$");

        private static bool Matches(FileInfo file, ManualDownload expected)
        {
            if (!file.Exists || file.Length == 0) return false;
            if (expected.Length > 0 && file.Length != expected.Length) return false;
            if (expected.Sha1 is not { Length: > 0 } sha1) return expected.Length > 0;
            return string.Equals(Sha1Of(file), sha1, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"x.jar" ile tarayıcının verdiği "x (1).jar" aynı ad sayılır.</summary>
        private static bool SameName(string name, string expected) =>
            string.Equals(name, expected, StringComparison.OrdinalIgnoreCase)
            || (Path.GetExtension(name).Equals(Path.GetExtension(expected), StringComparison.OrdinalIgnoreCase)
                && CopySuffix.Replace(Path.GetFileNameWithoutExtension(name), "").Equals(Path.GetFileNameWithoutExtension(expected), StringComparison.OrdinalIgnoreCase));

        /// <summary>Adının ilk parçası (sürüm numarasından önceki) aynı olan başka bir dosya: büyük ihtimalle başka bir sürüm.</summary>
        private static FileInfo? StemMatch(List<FileInfo> candidates, ManualDownload expected)
        {
            string stem = Stem(expected.FileName);
            return stem.Length < 4 ? null : candidates.FirstOrDefault(c => Stem(c.Name).Equals(stem, StringComparison.OrdinalIgnoreCase));
        }

        private static string Stem(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            int cut = name.IndexOfAny("0123456789".ToCharArray());
            return (cut > 0 ? name[..cut] : name).TrimEnd('-', '_', '.', ' ', 'v', 'V');
        }

        private static string? Sha1Of(FileInfo file)
        {
            try
            {
                using var stream = file.OpenRead();
                return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
            }
            catch (IOException) { return null; }
        }
    }
}
