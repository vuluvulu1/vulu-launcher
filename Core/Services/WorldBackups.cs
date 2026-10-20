using System.IO.Compression;

namespace VuluLauncher
{
    /// <summary>
    /// Dünya yedekleri: saves/&lt;dünya&gt; klasörünü zip'ler ve geri yükler. Yedek klasörü oyun klasörünün dışındadır.
    /// Geri yükleme önce yeni bir klasöre açar, sonra eskisinin yerine koyar; yarıda kalırsa dünya bozulmaz.
    /// </summary>
    internal static class WorldBackups
    {
        public const int AutoKept = 5;

        /// <summary>Dünyayı yedekler; dosya adını döner.</summary>
        /// <param name="kind">"elle" | "otomatik" | "geri-yukleme-oncesi" (otomatiklerden yalnızca son <see cref="AutoKept"/> tanesi kalır).</param>
        public static string Backup(string worldDir, string backupDir, string kind)
        {
            if (!Directory.Exists(worldDir)) throw new DirectoryNotFoundException("Dünya bulunamadı.");
            string world = Path.GetFileName(worldDir.TrimEnd('\\', '/'));
            Directory.CreateDirectory(backupDir);
            string name = $"{world}-{kind}-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            string target = Path.Combine(backupDir, name);
            for (int n = 2; File.Exists(target); n++) target = Path.Combine(backupDir, name = $"{world}-{kind}-{DateTime.Now:yyyyMMdd-HHmmss}-{n}.zip");
            string part = target + ".part";
            try
            {
                using (var zip = ZipFile.Open(part, ZipArchiveMode.Create))
                    foreach (var file in Directory.EnumerateFiles(worldDir, "*", SearchOption.AllDirectories))
                    {
                        if (Path.GetFileName(file) == "session.lock") continue;
                        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var entry = zip.CreateEntry(Path.GetRelativePath(worldDir, file).Replace('\\', '/'), CompressionLevel.Fastest).Open();
                        input.CopyTo(entry);
                    }
                File.Move(part, target);
            }
            finally { if (File.Exists(part)) File.Delete(part); }

            if (kind == "otomatik")
                foreach (var old in new DirectoryInfo(backupDir).EnumerateFiles("*-otomatik-*.zip").OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name).Skip(AutoKept))
                    old.Delete();
            return name;
        }

        /// <summary>Yedeği geri yükler. Dünya şu an varsa önce onun da yedeği alınır (geri yükleme de geri alınabilsin).</summary>
        public static void Restore(string zipPath, string worldDir, string backupDir)
        {
            if (!File.Exists(zipPath)) throw new FileNotFoundException("Yedek bulunamadı.");
            if (Directory.Exists(worldDir)) Backup(worldDir, backupDir, "geri-yukleme-oncesi");

            string saves = Path.GetDirectoryName(worldDir.TrimEnd('\\', '/'))!;
            Directory.CreateDirectory(saves);
            string staging = Path.Combine(saves, $".restore-{Guid.NewGuid():N}");
            try
            {
                using (var zip = ZipFile.OpenRead(zipPath))
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.FullName.EndsWith('/')) continue;
                        string? path = Downloader.ResolveInside(staging, entry.FullName);
                        if (path == null) continue;                       // klasör dışına çıkan yol atlanır
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        entry.ExtractToFile(path, overwrite: true);
                    }
                if (!File.Exists(Path.Combine(staging, "level.dat"))) throw new InvalidDataException("Yedekte level.dat yok; bu bir dünya yedeği değil.");
                if (Directory.Exists(worldDir)) Directory.Delete(worldDir, recursive: true);
                Directory.Move(staging, worldDir);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
        }
    }
}
