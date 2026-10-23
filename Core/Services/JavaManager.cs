using System.IO.Compression;

namespace VuluLauncher
{
    /// <summary>
    /// Oyunun istediği Java sürümünü bulur; yoksa Eclipse Temurin (Adoptium) JRE'sini indirip kurar.
    /// Kurulumlar exe'nin yanındaki java/temurin-&lt;sürüm&gt; klasörlerinde durur.
    /// </summary>
    internal sealed class JavaManager
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private readonly Action<string, string> _log;

        public JavaManager(Action<string, string> log)
        {
            _log = log;
        }

        public static string JavaRoot => Path.Combine(AppPaths.Base, "java");

        /// <summary>
        /// Minecraft'ın istediği sürümü, Adoptium'da bulunan en yakın uzun destekli sürüme eşler.
        /// Yeni Java eski sürüm için derlenmiş oyunu çalıştırır; tersi olmaz, bu yüzden yukarı yuvarlanır.
        /// </summary>
        public static int ToDistributionVersion(int required) => required switch
        {
            <= 8 => 8,
            <= 11 => 11,
            <= 17 => 17,
            <= 21 => 21,
            <= 25 => 25,
            _ => required
        };

        /// <summary>Verilen Java sürümünü çalıştıracak java.exe yolunu döner; gerekirse indirir.</summary>
        public async Task<string> EnsureAsync(int requiredMajor, CancellationToken cancellation = default)
        {
            var settings = AppSettings.Current;
            if (settings.JavaMode == "custom")
            {
                if (!File.Exists(settings.CustomJavaPath))
                    throw new FileNotFoundException($"Ayarlarda seçilen Java bulunamadı: {settings.CustomJavaPath}");
                return settings.CustomJavaPath;
            }

            int major = ToDistributionVersion(requiredMajor);
            string installDir = Path.Combine(JavaRoot, $"temurin-{major}");

            await Gate.WaitAsync(cancellation);
            try
            {
                string? existing = FindJavaExe(installDir);
                if (existing != null) return existing;

                // Bilgisayarda aynı ana sürümden bir Java kuruluysa indirmek yerine o kullanılır
                var system = Detect().FirstOrDefault(j => j.Major == major);
                if (system != null)
                {
                    _log($"Bilgisayardaki Java {major} kullanılıyor ({system.Source}).", "muted");
                    return system.Path;
                }

                _log($"Java {major} bulunamadı, Adoptium'dan indiriliyor...", "accent");
                // Oyun başlatılırken o işe eklenir; ayarlardan istenince kendi işi olur
                var parent = DownloadJob.Current;
                using var own = parent == null ? new DownloadJob($"Java {major} kuruluyor") : null;
                var job = parent ?? own!;
                job.Phase = $"Java {major} indiriliyor";
                try
                {
                    await InstallAsync(major, installDir, job.Add($"Java {major} (Eclipse Temurin)", 0), cancellation);
                }
                catch (Exception ex)
                {
                    own?.Fail(ex.Message);
                    throw;
                }
                own?.Complete($"Java {major} kuruldu");

                return FindJavaExe(installDir)
                    ?? throw new FileNotFoundException($"Java {major} kuruldu ama java.exe bulunamadı: {installDir}");
            }
            finally
            {
                Gate.Release();
            }
        }

        internal sealed record DetectedJava(int Major, string Version, string Path, string Source);

        private static List<DetectedJava>? _detected;
        private static DateTime _detectedAt;

        /// <summary>
        /// Bilgisayarda kurulu Java'lar: JAVA_HOME, Program Files altındaki bilinen dağıtımlar ve resmi Minecraft
        /// launcher'ının Java'ları. Sürüm, Java klasöründeki "release" dosyasından okunur. Sonuç bir dakika saklanır.
        /// </summary>
        public static List<DetectedJava> Detect()
        {
            if (_detected != null && DateTime.UtcNow - _detectedAt < TimeSpan.FromMinutes(1)) return _detected;
            var found = new List<DetectedJava>();
            if (!OperatingSystem.IsWindows()) return _detected = found;

            var homes = new List<(string Home, string Source)>();
            if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } javaHome) homes.Add((javaHome, "JAVA_HOME"));
            var vendors = new[] { "Java", "Eclipse Adoptium", "Eclipse Foundation", "Microsoft", "Zulu", "BellSoft", "Amazon Corretto", "Semeru", "OpenJDK" };
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") }.Where(r => r.Length > 0).Distinct())
                foreach (var vendor in vendors)
                {
                    string dir = Path.Combine(root, vendor);
                    if (!Directory.Exists(dir)) continue;
                    try { foreach (var home in Directory.GetDirectories(dir)) homes.Add((home, vendor)); } catch { }
                }
            // Resmi Minecraft launcher'ının indirdiği Java'lar
            string mojang = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "runtime");
            if (Directory.Exists(mojang))
                try
                {
                    foreach (var runtime in Directory.GetDirectories(mojang))
                        foreach (var platform in Directory.GetDirectories(runtime))
                            foreach (var home in Directory.GetDirectories(platform)) homes.Add((home, "Minecraft Launcher"));
                }
                catch { }

            foreach (var (home, source) in homes)
            {
                string exe = Path.Combine(home, "bin", "java.exe");
                if (!File.Exists(exe) || ReadVersion(home) is not { } version) continue;
                int major = ParseMajor(version);
                if (major > 0 && !found.Any(f => f.Path.Equals(exe, StringComparison.OrdinalIgnoreCase)))
                    found.Add(new DetectedJava(major, version, exe, source));
            }
            _detectedAt = DateTime.UtcNow;
            return _detected = found.OrderByDescending(f => f.Major).ToList();
        }

        /// <summary>Java klasöründeki "release" dosyasından JAVA_VERSION (ör. "21.0.4" ya da eski "1.8.0_402").</summary>
        private static string? ReadVersion(string home)
        {
            try
            {
                string release = Path.Combine(home, "release");
                if (!File.Exists(release)) return null;
                foreach (var line in File.ReadLines(release))
                    if (line.StartsWith("JAVA_VERSION=", StringComparison.Ordinal)) return line["JAVA_VERSION=".Length..].Trim().Trim('"');
            }
            catch { }
            return null;
        }

        private static int ParseMajor(string version)
        {
            var parts = version.Split('.', '_', '-', '+');
            if (!int.TryParse(parts[0], out int first)) return 0;
            return first == 1 && parts.Length > 1 && int.TryParse(parts[1], out int legacy) ? legacy : first;
        }

        /// <summary>Kurulu Temurin sürümlerini listeler (ayarlar ekranı için).</summary>
        public static IEnumerable<int> InstalledVersions()
        {
            if (!Directory.Exists(JavaRoot)) yield break;

            foreach (var dir in Directory.GetDirectories(JavaRoot, "temurin-*"))
            {
                if (int.TryParse(Path.GetFileName(dir)["temurin-".Length..], out int major) && FindJavaExe(dir) != null)
                    yield return major;
            }
        }

        private async Task InstallAsync(int major, string installDir, DownloadItem item, CancellationToken cancellation)
        {
            string zipPath = Path.Combine(Path.GetTempPath(), $"vulu-temurin-{major}.zip");
            string stagingDir = installDir + ".tmp";

            try
            {
                long downloaded = 0;
                var progress = new ThrottledProgress<long>(_ =>
                    _log($"Java {major} indiriliyor... {Interlocked.Read(ref downloaded) / 1024 / 1024} MB", "replace"), 200);
                var bytes = new CountingProgress(read => { Interlocked.Add(ref downloaded, read); progress.Report(read); item.Report(read); });
                item.Start();

                // JRE daha küçüktür; o sürüm için JRE yayınlanmadıysa JDK'ya düşülür
                try
                {
                    await Downloader.DownloadAsync(BinaryUrl(major, "jre"), zipPath, bytes, cancellation: cancellation, contentLength: item.SetSize);
                }
                catch (HttpRequestException)
                {
                    downloaded = 0;
                    item.Reset();
                    await Downloader.DownloadAsync(BinaryUrl(major, "jdk"), zipPath, bytes, cancellation: cancellation, contentLength: item.SetSize);
                }
                item.Complete();

                _log($"Java {major} kuruluyor...", "replace");
                await Task.Run(() =>
                {
                    if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
                    ZipFile.ExtractToDirectory(zipPath, stagingDir);

                    if (Directory.Exists(installDir)) Directory.Delete(installDir, recursive: true);
                    Directory.CreateDirectory(Path.GetDirectoryName(installDir)!);
                    Directory.Move(stagingDir, installDir);
                }, cancellation);

                _log($"Java {major} kuruldu.", "ok");
            }
            finally
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
            }
        }

        private static string BinaryUrl(int major, string imageType) =>
            $"https://api.adoptium.net/v3/binary/latest/{major}/ga/windows/x64/{imageType}/hotspot/normal/eclipse";

        /// <summary>Zip içinde "jdk-17.0.x+y-jre/bin/java.exe" gibi tek bir üst klasör bulunur.</summary>
        private static string? FindJavaExe(string installDir)
        {
            if (!Directory.Exists(installDir)) return null;

            string direct = Path.Combine(installDir, "bin", "java.exe");
            if (File.Exists(direct)) return direct;

            foreach (var sub in Directory.GetDirectories(installDir))
            {
                string candidate = Path.Combine(sub, "bin", "java.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private sealed class CountingProgress : IProgress<long>
        {
            private readonly Action<long> _report;
            public CountingProgress(Action<long> report) => _report = report;
            public void Report(long value) => _report(value);
        }
    }
}
