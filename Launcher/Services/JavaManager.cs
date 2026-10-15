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

                _log($"Java {major} bulunamadı, Adoptium'dan indiriliyor...", "accent");
                await InstallAsync(major, installDir, cancellation);

                return FindJavaExe(installDir)
                    ?? throw new FileNotFoundException($"Java {major} kuruldu ama java.exe bulunamadı: {installDir}");
            }
            finally
            {
                Gate.Release();
            }
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

        private async Task InstallAsync(int major, string installDir, CancellationToken cancellation)
        {
            string zipPath = Path.Combine(Path.GetTempPath(), $"vulu-temurin-{major}.zip");
            string stagingDir = installDir + ".tmp";

            try
            {
                long downloaded = 0;
                var progress = new ThrottledProgress<long>(_ =>
                    _log($"Java {major} indiriliyor... {Interlocked.Read(ref downloaded) / 1024 / 1024} MB", "replace"), 200);
                var bytes = new CountingProgress(read => { Interlocked.Add(ref downloaded, read); progress.Report(read); });

                // JRE daha küçüktür; o sürüm için JRE yayınlanmadıysa JDK'ya düşülür
                try
                {
                    await Downloader.DownloadAsync(BinaryUrl(major, "jre"), zipPath, bytes, cancellation: cancellation);
                }
                catch (HttpRequestException)
                {
                    downloaded = 0;
                    await Downloader.DownloadAsync(BinaryUrl(major, "jdk"), zipPath, bytes, cancellation: cancellation);
                }

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
