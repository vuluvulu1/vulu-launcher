using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using VuluShared;

namespace Installer
{
    /// <summary>Arayüze gönderilen anlık durum.</summary>
    internal sealed record InstallStatus(
        string Step,              // download | verify | install | shortcuts | done | error
        string Title,
        string Detail = "",
        double? Progress = null,  // 0..1; null = belirsiz
        string? Version = null);

    internal sealed record InstallOptions(string Directory, bool DesktopShortcut, bool StartMenuShortcut);

    /// <summary>
    /// En son sürümü GitHub Releases'tan indirir, doğrular ve kurar. Dosyalar önce geçici klasörde hazırlanır,
    /// her biri release.json'daki hash ile karşılaştırılır; ancak hepsi sağlamsa kurulum klasörüne konur.
    /// Klasörde önceden kalan oyun dosyalarına (instances, java, minecraft_core) dokunulmaz.
    /// </summary>
    internal sealed class InstallEngine
    {
        private readonly Action<InstallStatus> _report;

        public InstallEngine(Action<InstallStatus> report)
        {
            _report = report;
        }

        public async Task<string> RunAsync(InstallOptions options, LatestRelease latest, CancellationToken token)
        {
            var manifest = latest.Manifest;
            string version = Releases.Clean(manifest.Version);
            string dir = ValidateDirectory(options.Directory);

            if (LauncherRunningFrom(dir))
                throw new InvalidOperationException("vulu Launcher şu an bu klasörden açık. Kapatıp tekrar dene.");

            EnsureDiskSpace(dir, manifest);

            string work = Path.Combine(Path.GetTempPath(), "vulu-setup-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(work);
            try
            {
                string zip = await DownloadAsync(latest, version, Path.Combine(work, "app.zip"), token);

                _report(new InstallStatus("verify", "Dosyalar doğrulanıyor…", "", null, version));
                string staging = Path.Combine(work, "app");
                await Task.Run(() => ExtractAndVerify(zip, staging, manifest), token);

                _report(new InstallStatus("install", "Kuruluyor…", dir, null, version));
                await Task.Run(() => Place(staging, dir, manifest), token);

                _report(new InstallStatus("shortcuts", "Kısayollar oluşturuluyor…", "", null, version));
                string launcher = Path.Combine(dir, Releases.LauncherExeName);
                if (options.StartMenuShortcut) InstallInfo.CreateShortcut(InstallInfo.StartMenuShortcut, launcher, InstallInfo.AppName);
                if (options.DesktopShortcut) InstallInfo.CreateShortcut(InstallInfo.DesktopShortcut, launcher, InstallInfo.AppName);
                InstallInfo.WriteUninstallEntry(dir, version);

                _report(new InstallStatus("done", "Kurulum tamamlandı", dir, 1, version));
                return dir;
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch { /* geçici klasör, Windows temizler */ }
            }
        }

        // ── Klasör ve alan ────────────────────────────────────────────────
        public static string ValidateDirectory(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) throw new InvalidOperationException("Kurulum klasörü seçilmedi.");
            string full = Path.GetFullPath(dir.Trim().Trim('"'));
            if (string.Equals(full.TrimEnd('\\'), Path.GetPathRoot(full)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Diskin köküne kurulamaz; bir klasör seç (ör. D:\\Oyunlar\\vulu Launcher).");

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (full.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Windows klasörüne kurulamaz.");
            return full;
        }

        /// <summary>Seçilen klasörün sürücüsündeki boş alan (MB); okunamazsa null.</summary>
        public static long? FreeSpaceMb(string dir)
        {
            try
            {
                string? root = Path.GetPathRoot(Path.GetFullPath(dir));
                return root == null ? null : new DriveInfo(root).AvailableFreeSpace / 1024 / 1024;
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureDiskSpace(string dir, ReleaseManifest manifest)
        {
            long installed = manifest.Files.Sum(f => f.Size);
            long needed = installed + manifest.Size + 200L * 1024 * 1024; // zip + açılmış hali + pay
            long? free = FreeSpaceMb(dir);
            if (free != null && free.Value * 1024 * 1024 < needed)
                throw new InvalidOperationException($"Yeterli boş alan yok. Gereken: {needed / 1024 / 1024} MB, boş: {free} MB.");
        }

        // ── İndirme ───────────────────────────────────────────────────────
        private async Task<string> DownloadAsync(LatestRelease latest, string version, string target, CancellationToken token)
        {
            var manifest = latest.Manifest;
            _report(new InstallStatus("download", $"v{version} indiriliyor…", "", 0, version));

            using (var http = Releases.CreateHttpClient(TimeSpan.FromMinutes(30)))
            using (var response = await http.GetAsync(latest.ZipUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? manifest.Size;

                await using var stream = await response.Content.ReadAsStreamAsync(token);
                await using var file = File.Create(target);
                var buffer = new byte[81920];
                long done = 0, lastReport = 0;
                var clock = Stopwatch.StartNew();
                int read;
                while ((read = await stream.ReadAsync(buffer, token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    done += read;
                    if (clock.ElapsedMilliseconds - lastReport < 120 && done != total) continue;
                    lastReport = clock.ElapsedMilliseconds;

                    double seconds = Math.Max(clock.Elapsed.TotalSeconds, 0.1);
                    string amount = total > 0 ? $"{Mb(done)} / {Mb(total)} MB" : $"{Mb(done)} MB";
                    _report(new InstallStatus("download", $"v{version} indiriliyor…",
                        $"{amount} · {done / seconds / 1024 / 1024:F1} MB/sn", total > 0 ? (double)done / total : null, version));
                }
            }

            if (!string.Equals(Releases.Sha256Of(target), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("İndirilen dosya doğrulanamadı (hash uyuşmuyor). Tekrar dene.");
            return target;
        }

        private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("F1");

        private static void ExtractAndVerify(string zipPath, string staging, ReleaseManifest manifest)
        {
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(zipPath, staging);
            foreach (var file in manifest.Files)
            {
                string path = Releases.ResolveInside(staging, file.Path)
                    ?? throw new InvalidDataException($"Geçersiz dosya yolu: {file.Path}");
                if (!File.Exists(path) || !string.Equals(Releases.Sha256Of(path), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Dosya doğrulanamadı: {file.Path}");
            }
        }

        // ── Yerleştirme ───────────────────────────────────────────────────
        /// <summary>Doğrulanmış dosyaları kurulum klasörüne kopyalar (varsa üzerine) ve install.json'u yazar.</summary>
        private static void Place(string staging, string dir, ReleaseManifest manifest)
        {
            Directory.CreateDirectory(dir);
            foreach (var file in manifest.Files)
            {
                string source = Releases.ResolveInside(staging, file.Path)!;
                string target = Releases.ResolveInside(dir, file.Path)
                    ?? throw new InvalidDataException($"Geçersiz dosya yolu: {file.Path}");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }

            var install = new ReleaseManifest
            {
                Version = Releases.Clean(manifest.Version),
                Notes = manifest.Notes,
                PublishedAt = manifest.PublishedAt,
                Files = manifest.Files
            };
            File.WriteAllText(Path.Combine(dir, Releases.InstallFileName), JsonSerializer.Serialize(install, Releases.Json));
        }

        public static bool LauncherRunningFrom(string dir)
        {
            string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            foreach (string name in new[] { Path.GetFileNameWithoutExtension(Releases.LauncherExeName), Path.GetFileNameWithoutExtension(Releases.UpdaterExeName) })
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (process.MainModule?.FileName?.StartsWith(root, StringComparison.OrdinalIgnoreCase) == true) return true;
                    }
                    catch { /* erişilemeyen süreç başka bir kuruluma aittir */ }
                    finally { process.Dispose(); }
                }
            }
            return false;
        }

        public static void Launch(string dir)
        {
            string exe = Path.Combine(dir, Releases.LauncherExeName);
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = dir });
        }
    }
}
