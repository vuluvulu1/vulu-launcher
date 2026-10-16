using System.Diagnostics;
using System.Text;
using System.Text.Json;
using VuluShared;

namespace Updater
{
    /// <summary>
    /// Kaldırma ("Program ekle/kaldır" → Updater.exe --uninstall).
    ///
    /// Güvenlik: yalnızca install.json'da listelenen kurulum dosyaları silinir. Oyun klasörleri (instances, java,
    /// minecraft_core) ve kullanıcı ayarları ancak kullanıcı isterse silinir. Kurulum klasörü hiçbir zaman toptan
    /// silinmez; yalnızca boşalan klasörler kaldırılır, kullanıcının oraya koyduğu başka dosyalar yerinde kalır.
    /// Kurulum dosyaları Updater kapandıktan sonra küçük bir betikle silinir: Updater bu klasördeki çalışma
    /// zamanıyla çalışır ve henüz yüklemediği bir dll'i silmek, sonraki adımları (ör. kayıt silme) çökertir.
    /// </summary>
    internal static class Uninstaller
    {
        private static readonly string[] GameFolders = { "instances", "java", "minecraft_core" };

        public static string UserDataFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vuluLauncher");

        public static async Task RunAsync(string installDir, bool deleteData, Action<UpdateStatus> report)
        {
            string dir = Path.GetFullPath(installDir).TrimEnd('\\');
            string installFile = Path.Combine(dir, Releases.InstallFileName);
            if (!File.Exists(installFile))
                throw new InvalidOperationException("Bu klasörde vulu Launcher kurulumu bulunamadı.");

            if (LauncherRunning(dir))
                throw new InvalidOperationException("vulu Launcher açık. Kapatıp tekrar dene.");

            report(new UpdateStatus("installing", "Kaldırılıyor…"));
            var pending = new List<string>();

            await Task.Run(() =>
            {
                var manifest = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(installFile), Releases.Json) ?? new();
                var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in manifest.Files)
                {
                    string? path = Releases.ResolveInside(dir, file.Path);
                    if (path == null || !File.Exists(path)) continue;
                    for (string? d = Path.GetDirectoryName(path); d != null && d.Length > dir.Length; d = Path.GetDirectoryName(d))
                        directories.Add(d);
                    pending.Add(path);
                }

                string updateDir = Path.Combine(dir, ".update");
                if (Directory.Exists(updateDir))
                {
                    pending.AddRange(Directory.EnumerateFiles(updateDir, "*", SearchOption.AllDirectories));
                    foreach (var d in Directory.EnumerateDirectories(updateDir, "*", SearchOption.AllDirectories)) directories.Add(d);
                }
                pending.Add(installFile);

                // Önce kayıt ve kısayollar: kurulum dosyalarına dokunulmadan yapılır
                InstallInfo.RemoveShortcuts();
                InstallInfo.RemoveUninstallEntry();

                if (deleteData)
                {
                    foreach (var folder in GameFolders) TryDeleteTree(Path.Combine(dir, folder), pending);
                    TryDeleteTree(UserDataFolder, pending);
                }

                ScheduleCleanup(pending, directories, dir, deleteData);
            });

            report(new UpdateStatus("uninstalled", "vulu Launcher kaldırıldı",
                deleteData ? "Oyun dosyaları ve ayarlar da silindi." : "Dünyaların ve oyun dosyaların klasörde duruyor: " + dir));
        }

        private static void TryDelete(string path, List<string> pending)
        {
            try { if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); } }
            catch { pending.Add(path); }
        }

        private static void TryDeleteTree(string dir, List<string> pending)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) TryDelete(file, pending);
            foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                TryRemoveEmpty(sub);
            TryRemoveEmpty(dir);
        }

        private static void TryRemoveEmpty(string dir)
        {
            try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
            catch { }
        }

        /// <summary>
        /// Updater kapanınca kalan (kilitli) dosyaları silen ve boşalan klasörleri kaldıran gizli bir betik başlatır.
        /// Betik yalnızca listelenen dosyaları siler; klasörleri "rd" ile (boşsa) kaldırır, asla toptan silmez.
        /// </summary>
        private static void ScheduleCleanup(List<string> pending, HashSet<string> directories, string dir, bool deleteData)
        {
            var dirs = directories.ToList();
            if (deleteData)
            {
                dirs.Add(UserDataFolder);
                foreach (var folder in GameFolders) dirs.Add(Path.Combine(dir, folder));
                // Kilitli kalan kullanıcı verisi (ör. bu pencerenin tarayıcı önbelleği) için alt klasörler
                if (Directory.Exists(UserDataFolder))
                    dirs.AddRange(Directory.EnumerateDirectories(UserDataFolder, "*", SearchOption.AllDirectories));
            }
            dirs.Add(Path.Combine(dir, ".update"));

            var script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("chcp 65001 >nul"); // betik UTF-8; Türkçe karakterli yollar için
            script.AppendLine(":wait");
            script.AppendLine($"tasklist /FI \"PID eq {Environment.ProcessId}\" 2>nul | find \"{Environment.ProcessId}\" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)");
            foreach (var file in pending) script.AppendLine($"del /f /q \"{file}\" 2>nul");
            foreach (var d in dirs.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(d => d.Length))
                script.AppendLine($"rd \"{d}\" 2>nul");
            script.AppendLine($"rd \"{dir}\" 2>nul");
            script.AppendLine("del \"%~f0\"");

            string path = Path.Combine(Path.GetTempPath(), $"vulu-uninstall-{Guid.NewGuid():N}.cmd");
            File.WriteAllText(path, script.ToString(), new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        private static bool LauncherRunning(string dir)
        {
            string root = dir + "\\";
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Releases.LauncherExeName)))
            {
                try { if (process.MainModule?.FileName?.StartsWith(root, StringComparison.OrdinalIgnoreCase) == true) return true; }
                catch { }
                finally { process.Dispose(); }
            }
            return false;
        }
    }
}
