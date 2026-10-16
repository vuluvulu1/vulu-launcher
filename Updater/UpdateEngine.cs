using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using VuluShared;

namespace Updater
{
    /// <summary>Arayüze gönderilen anlık durum.</summary>
    internal sealed record UpdateStatus(
        string State,               // checking | downloading | verifying | installing | launching | error | confirm | uninstalled
        string Title,
        string Detail = "",
        double? Progress = null,    // 0..1; null = belirsiz
        string? Version = null,
        string? Notes = null);

    /// <summary>
    /// Discord tarzı akış: sürümü denetle → gerekiyorsa indir, doğrula, kur → launcher'ı aç.
    /// Yalnızca release'teki dosyalara dokunur; oyun dosyaları (instances, java, minecraft_core) hiç ellenmez.
    /// </summary>
    internal sealed class UpdateEngine
    {
        private readonly string _installDir;
        private readonly string _workDir;
        private readonly Action<UpdateStatus> _report;
        private readonly IReadOnlyList<int> _waitPids;

        /// <summary>Dosyalar yer değiştirirken pencere kapatılmamalı.</summary>
        public bool IsApplying { get; private set; }

        public UpdateEngine(string installDir, IReadOnlyList<int> waitPids, Action<UpdateStatus> report)
        {
            _installDir = installDir;
            _workDir = Path.Combine(installDir, ".update");
            _waitPids = waitPids;
            _report = report;
        }

        /// <summary>Tüm akış. true: launcher açılmalı; false: hata gösterildi, kullanıcı karar verecek.</summary>
        public async Task<bool> RunAsync(CancellationToken token)
        {
            CleanupLeftovers();
            _report(new UpdateStatus("checking", "Güncellemeler denetleniyor…"));

            var installed = LoadInstalled();

            LatestRelease latest;
            using (var http = Releases.CreateHttpClient(TimeSpan.FromSeconds(15)))
            {
                try
                {
                    latest = await Releases.GetLatestAsync(http, token);
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    // Discord gibi: denetlenemezse eldeki sürümle devam edilir
                    _report(new UpdateStatus("launching", "Güncelleme sunucusuna ulaşılamadı", "Mevcut sürümle açılıyor…"));
                    await Task.Delay(1400, token);
                    return true;
                }
            }

            var manifest = latest.Manifest;
            if (!Releases.IsNewer(manifest.Version, installed.Version))
            {
                _report(new UpdateStatus("launching", "Güncel", $"v{Releases.Clean(installed.Version)}"));
                return true;
            }

            string version = Releases.Clean(manifest.Version);
            string notes = manifest.Notes;

            string zipPath = await DownloadAsync(latest, version, notes, token);

            _report(new UpdateStatus("verifying", "Doğrulanıyor…", "", null, version, notes));
            string staging = Path.Combine(_workDir, "staging");
            await Task.Run(() => Extract(zipPath, staging, manifest), token);

            _report(new UpdateStatus("installing", "Launcher'ın kapanması bekleniyor…", "", null, version, notes));
            await WaitForLauncherExitAsync(token);

            _report(new UpdateStatus("installing", "Kuruluyor…", "", null, version, notes));
            IsApplying = true;
            try
            {
                await Task.Run(() => Apply(staging, manifest, installed));
            }
            finally
            {
                IsApplying = false;
            }

            TryDelete(Path.Combine(_workDir, "download"));
            TryDelete(staging);

            _report(new UpdateStatus("launching", $"v{version} kuruldu", "Launcher açılıyor…", 1, version, notes));
            await Task.Delay(900, token);
            return true;
        }

        public void LaunchLauncher()
        {
            string exe = Path.Combine(_installDir, Releases.LauncherExeName);
            if (!File.Exists(exe))
                throw new FileNotFoundException($"{Releases.LauncherExeName} bulunamadı.");

            var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = _installDir };
            start.ArgumentList.Add("--from-updater");
            Process.Start(start);
        }

        // ── Kurulu sürüm ──────────────────────────────────────────────────
        private ReleaseManifest LoadInstalled()
        {
            string installFile = Path.Combine(_installDir, Releases.InstallFileName);
            try
            {
                if (File.Exists(installFile))
                {
                    var info = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(installFile), Releases.Json);
                    if (info != null && !string.IsNullOrWhiteSpace(info.Version)) return info;
                }
            }
            catch { /* bozuksa exe sürümüne düşülür */ }

            // install.json yoksa (eski kurulum) sürüm exe'den okunur; dosya listesi bilinmez
            string exe = Path.Combine(_installDir, Releases.LauncherExeName);
            string version = File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "0" : "0";
            return new ReleaseManifest { Version = Releases.Clean(version) };
        }

        // ── İndirme ───────────────────────────────────────────────────────
        private async Task<string> DownloadAsync(LatestRelease latest, string version, string notes, CancellationToken token)
        {
            var manifest = latest.Manifest;
            string downloadDir = Path.Combine(_workDir, "download");
            Directory.CreateDirectory(downloadDir);
            string zipPath = Path.Combine(downloadDir, Path.GetFileName(manifest.Asset));

            // Önceki denemede tamamı inmişse tekrar indirilmez
            if (File.Exists(zipPath) && Releases.Sha256Of(zipPath) == manifest.Sha256.ToLowerInvariant())
                return zipPath;

            string partPath = zipPath + ".part";
            _report(new UpdateStatus("downloading", $"v{version} indiriliyor…", "", 0, version, notes));

            using (var http = Releases.CreateHttpClient(TimeSpan.FromMinutes(30)))
            using (var response = await http.GetAsync(latest.ZipUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? manifest.Size;

                await using var stream = await response.Content.ReadAsStreamAsync(token);
                await using var file = File.Create(partPath);

                var buffer = new byte[81920];
                long done = 0, lastReported = 0;
                var clock = Stopwatch.StartNew();
                int read;
                while ((read = await stream.ReadAsync(buffer, token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    done += read;

                    if (clock.ElapsedMilliseconds - lastReported < 120 && done != total) continue;
                    lastReported = clock.ElapsedMilliseconds;

                    double seconds = Math.Max(clock.Elapsed.TotalSeconds, 0.1);
                    string speed = $"{done / seconds / 1024 / 1024:F1} MB/sn";
                    string amount = total > 0 ? $"{Mb(done)} / {Mb(total)} MB" : $"{Mb(done)} MB";
                    _report(new UpdateStatus("downloading", $"v{version} indiriliyor…", $"{amount} · {speed}",
                        total > 0 ? (double)done / total : null, version, notes));
                }
            }

            _report(new UpdateStatus("verifying", "Doğrulanıyor…", "", null, version, notes));
            string actual = await Task.Run(() => Releases.Sha256Of(partPath), token);
            if (!string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partPath);
                throw new InvalidDataException("İndirilen dosya doğrulanamadı (hash uyuşmuyor). Tekrar dene.");
            }

            File.Move(partPath, zipPath, overwrite: true);
            return zipPath;
        }

        private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("F1");

        // ── Açma ve dosya doğrulama ───────────────────────────────────────
        private static void Extract(string zipPath, string staging, ReleaseManifest manifest)
        {
            TryDelete(staging);
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(zipPath, staging);

            foreach (var file in manifest.Files)
            {
                string path = Releases.ResolveInside(staging, file.Path)
                    ?? throw new InvalidDataException($"Geçersiz dosya yolu: {file.Path}");
                if (!File.Exists(path))
                    throw new InvalidDataException($"Pakette eksik dosya: {file.Path}");
                if (!string.Equals(Releases.Sha256Of(path), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Dosya doğrulanamadı: {file.Path}");
            }
        }

        // ── Launcher'ın kapanmasını bekleme ───────────────────────────────
        private async Task WaitForLauncherExitAsync(CancellationToken token)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (true)
            {
                var running = RunningLauncherProcesses();
                if (running.Count == 0) return;

                if (DateTime.UtcNow > deadline)
                    throw new InvalidOperationException("Launcher hâlâ açık. Kapatıp tekrar dene.");

                foreach (var process in running) process.Dispose();
                await Task.Delay(400, token);
            }
        }

        private List<Process> RunningLauncherProcesses()
        {
            var list = new List<Process>();
            string root = Path.GetFullPath(_installDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (int pid in _waitPids)
            {
                try { var p = Process.GetProcessById(pid); if (!p.HasExited) list.Add(p); }
                catch { /* zaten kapanmış */ }
            }

            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Releases.LauncherExeName)))
            {
                try
                {
                    string? file = p.MainModule?.FileName;
                    if (file != null && file.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { list.Add(p); continue; }
                }
                catch { /* erişilemeyen süreç başka bir kuruluma aittir */ }
                p.Dispose();
            }
            return list;
        }

        // ── Kurulum ───────────────────────────────────────────────────────
        /// <summary>
        /// Değişen dosyaları yerine koyar. Eski dosya silinmez, ".update/old" altına taşınır: Windows çalışan
        /// exe/dll'lerin üzerine yazmaya izin vermez ama yeniden adlandırmaya izin verir. Bu sayede Updater
        /// kendisini de güncelleyebilir. Bir adım başarısız olursa yapılan her şey geri alınır.
        /// </summary>
        private void Apply(string staging, ReleaseManifest manifest, ReleaseManifest installed)
        {
            string oldDir = Path.Combine(_workDir, "old", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            var undo = new Stack<(string Target, string? Backup)>();

            try
            {
                var newPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in manifest.Files)
                {
                    if (IsProtected(file.Path)) continue;
                    newPaths.Add(Normalize(file.Path));

                    string target = Releases.ResolveInside(_installDir, file.Path)!;
                    string source = Releases.ResolveInside(staging, file.Path)!;

                    if (File.Exists(target) &&
                        string.Equals(Releases.Sha256Of(target), file.Sha256, StringComparison.OrdinalIgnoreCase))
                        continue; // değişmemiş

                    string? backup = null;
                    if (File.Exists(target))
                    {
                        backup = Path.Combine(oldDir, Normalize(file.Path));
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        File.Move(target, backup);
                    }
                    undo.Push((target, backup));

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(source, target);
                }

                // Eski sürümde olup yenisinde olmayan dosyalar kaldırılır
                foreach (var file in installed.Files)
                {
                    if (IsProtected(file.Path) || newPaths.Contains(Normalize(file.Path))) continue;
                    string? target = Releases.ResolveInside(_installDir, file.Path);
                    if (target == null || !File.Exists(target)) continue;

                    string backup = Path.Combine(oldDir, Normalize(file.Path));
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Move(target, backup);
                    undo.Push((target, backup));
                }

                var install = new ReleaseManifest
                {
                    Version = Releases.Clean(manifest.Version),
                    Notes = manifest.Notes,
                    PublishedAt = manifest.PublishedAt,
                    Files = manifest.Files
                };
                File.WriteAllText(Path.Combine(_installDir, Releases.InstallFileName),
                    JsonSerializer.Serialize(install, Releases.Json));
                UpdateRegisteredVersion(install.Version);
            }
            catch
            {
                while (undo.Count > 0)
                {
                    var (target, backup) = undo.Pop();
                    try
                    {
                        if (backup == null) { if (File.Exists(target)) File.Delete(target); }
                        else File.Move(backup, target, overwrite: true);
                    }
                    catch { /* elden gelen yapıldı */ }
                }
                throw;
            }
        }

        /// <summary>"Program ekle/kaldır"daki sürüm, yalnızca kayıtlı kurulum bu klasörse güncellenir.</summary>
        private void UpdateRegisteredVersion(string version)
        {
            try
            {
                string? registered = InstallInfo.InstalledDirectory();
                if (registered != null && string.Equals(Path.GetFullPath(registered).TrimEnd(Path.DirectorySeparatorChar),
                        Path.GetFullPath(_installDir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    InstallInfo.UpdateRegisteredVersion(version);
            }
            catch { /* kayıt yazılamazsa güncelleme yine geçerlidir */ }
        }

        private static string Normalize(string relativePath) =>
            relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

        /// <summary>Release'te geçse bile updater'ın kendi çalışma dosyalarına dokunulmaz.</summary>
        private static bool IsProtected(string relativePath)
        {
            string path = Normalize(relativePath);
            return path.Equals(Releases.InstallFileName, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(".update", StringComparison.OrdinalIgnoreCase);
        }

        // ── Temizlik ──────────────────────────────────────────────────────
        /// <summary>Önceki güncellemelerden kalan eski dosyalar; o sırada kilitli olanlar şimdi silinebilir.</summary>
        private void CleanupLeftovers()
        {
            TryDelete(Path.Combine(_workDir, "old"));
            TryDelete(Path.Combine(_workDir, "staging"));
        }

        private static void TryDelete(string directory)
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
                catch { /* hâlâ kullanımda; bir sonraki açılışta silinir */ }
            }
            try { Directory.Delete(directory, recursive: true); }
            catch { /* boş olmayan klasörler kalır */ }
        }
    }
}
