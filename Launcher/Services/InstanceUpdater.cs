using System.IO.Compression;

namespace VuluLauncher
{
    /// <summary>Bir instance için yapılması gereken güncellemeler.</summary>
    internal sealed record InstanceUpdatePlan(
        string InstanceName,
        string InstancePath,
        InstanceManifest Manifest,
        bool ModsUpdate,
        List<ManifestFile> ToUpdate,
        List<string> ToDelete);

    /// <summary>
    /// Kurulu instance'ları manifest ile karşılaştırır ve onaylanan güncellemeleri uygular.
    /// </summary>
    internal sealed class InstanceUpdater
    {
        private readonly ManifestService _manifest = new(string.Empty); // sadece okuma, token gerekmez
        private readonly Action<string, string> _log;

        public InstanceUpdater(Action<string, string> log)
        {
            _log = log;
        }

        /// <summary>Sadece kontrol eder; hiçbir şey indirmez. Hata fırlatmaz, sorunları loga yazar.</summary>
        public async Task<List<InstanceUpdatePlan>> BuildPlanAsync(IEnumerable<string> allowedPacks)
        {
            var plan = new List<InstanceUpdatePlan>();
            if (!Directory.Exists(AppPaths.Instances)) return plan;

            ManifestRoot? manifest;
            try
            {
                manifest = await _manifest.GetManifestAsync();
            }
            catch (Exception ex)
            {
                _log($"Paket güncellemeleri kontrol edilemedi: {ex.Message}", "error");
                return plan;
            }

            if (manifest?.Instances == null) return plan;

            foreach (var instanceName in allowedPacks)
            {
                // Sunucudan gelen isim klasör adı olarak kullanılıyor; yol içeremez
                if (string.IsNullOrWhiteSpace(instanceName) ||
                    instanceName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    instanceName.Trim('.').Length == 0)
                    continue;

                string instancePath = Path.Combine(AppPaths.Instances, instanceName);
                if (!Directory.Exists(instancePath)) continue;
                if (!manifest.Instances.TryGetValue(instanceName, out var instanceManifest)) continue;

                try
                {
                    bool modsUpdate = false;
                    if (!string.IsNullOrEmpty(instanceManifest.ModsVersion) &&
                        !string.IsNullOrEmpty(instanceManifest.ModsUrl))
                    {
                        string versionFile = Path.Combine(instancePath, ".modsversion");
                        string localVersion = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "";
                        modsUpdate = localVersion != instanceManifest.ModsVersion;
                    }

                    var (toUpdate, toDelete) = await _manifest.GetChangesAsync(instancePath, instanceManifest);

                    if (modsUpdate || toUpdate.Count > 0 || toDelete.Count > 0)
                        plan.Add(new InstanceUpdatePlan(instanceName, instancePath, instanceManifest, modsUpdate, toUpdate, toDelete));
                }
                catch (Exception ex)
                {
                    _log($"{instanceName} kontrol hatası: {ex.Message}", "error");
                }
            }

            return plan;
        }

        public async Task ApplyAsync(InstanceUpdatePlan item)
        {
            if (item.ModsUpdate)
            {
                _log($"{item.InstanceName} mods güncelleniyor ({item.Manifest.ModsVersion})...", "accent");
                await UpdateModsAsync(item);
                _log($"{item.InstanceName} mods güncellendi!", "ok");
            }

            if (item.ToDelete.Count > 0)
            {
                _log($"{item.InstanceName}: {item.ToDelete.Count} dosya siliniyor...", "warn");
                foreach (var path in item.ToDelete)
                {
                    File.Delete(path);
                    _log($"Silindi: {Path.GetFileName(path)}", "muted");
                }
            }

            if (item.ToUpdate.Count > 0)
            {
                _log($"{item.InstanceName}: {item.ToUpdate.Count} dosya indiriliyor...", "accent");
                int current = 0;
                foreach (var file in item.ToUpdate)
                {
                    current++;
                    string? localPath = ManifestService.ResolveTrackedPath(item.InstancePath, file.Path);
                    if (localPath == null) continue;

                    _log($"İndiriliyor [{current}/{item.ToUpdate.Count}]: {Path.GetFileName(file.Path)}", "replace");
                    await _manifest.DownloadFileAsync(file.Url, localPath, expectedSha256: file.Hash);
                }
            }
        }

        /// <summary>
        /// Mods zip'ini indirir ve geçici klasöre açar; ancak her şey hazır olduktan sonra
        /// eski "mods" klasörünün yerine koyar. Hata olursa eski modlar geri gelir.
        /// </summary>
        private async Task UpdateModsAsync(InstanceUpdatePlan item)
        {
            string zipPath = Path.Combine(Path.GetTempPath(), $"{item.InstanceName}-mods.zip");
            string stagingPath = Path.Combine(item.InstancePath, ".mods_staging");
            string modsPath = Path.Combine(item.InstancePath, "mods");
            string backupPath = Path.Combine(item.InstancePath, "mods.bak");

            try
            {
                int lastPercent = -1;
                var progress = new SyncProgress(p =>
                {
                    if (p == lastPercent) return;
                    lastPercent = p;
                    _log($"Mods indiriliyor... %{p}", "replace");
                });

                await _manifest.DownloadFileAsync(item.Manifest.ModsUrl!, zipPath, progress);
                _log("Mods paketi açılıyor...", "replace");

                await Task.Run(() =>
                {
                    // Önceki güncelleme yarıda kaldıysa eski modları geri getir
                    if (Directory.Exists(backupPath) && !Directory.Exists(modsPath))
                        Directory.Move(backupPath, modsPath);

                    if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true);
                    ZipFile.ExtractToDirectory(zipPath, stagingPath);

                    if (!Directory.Exists(Path.Combine(stagingPath, "mods")))
                        throw new InvalidDataException("Mods paketinin içinde 'mods' klasörü yok.");

                    if (Directory.Exists(backupPath)) Directory.Delete(backupPath, recursive: true);
                    if (Directory.Exists(modsPath)) Directory.Move(modsPath, backupPath);

                    try
                    {
                        MoveDirectoryContents(stagingPath, item.InstancePath);
                    }
                    catch
                    {
                        if (Directory.Exists(backupPath))
                        {
                            if (Directory.Exists(modsPath)) Directory.Delete(modsPath, recursive: true);
                            Directory.Move(backupPath, modsPath);
                        }
                        throw;
                    }

                    if (Directory.Exists(backupPath)) Directory.Delete(backupPath, recursive: true);
                });

                File.WriteAllText(Path.Combine(item.InstancePath, ".modsversion"), item.Manifest.ModsVersion);
            }
            finally
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true);
            }
        }

        private static void MoveDirectoryContents(string sourceDir, string targetDir)
        {
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(targetDir, Path.GetRelativePath(sourceDir, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(file, destination, overwrite: true);
            }
        }

        /// <summary>İlerlemeyi çağıran thread'de, sırasını bozmadan bildirir.</summary>
        private sealed class SyncProgress : IProgress<int>
        {
            private readonly Action<int> _report;
            public SyncProgress(Action<int> report) => _report = report;
            public void Report(int value) => _report(value);
        }
    }
}
