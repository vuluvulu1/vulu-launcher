using System.Diagnostics;
using System.Text.RegularExpressions;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installers;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ModLoaders.QuiltMC;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Version;

namespace VuluLauncher
{
    /// <summary>Başlatılacak şey: sunucu paketi ya da kullanıcı instance'ı.</summary>
    internal sealed record LaunchTarget(string Name, string Folder, string McVersion, string Loader, string? LoaderVersion);

    /// <summary>
    /// Oyunu kurar ve başlatır. Bütün instance'lar oyun dosyalarını (assets, libraries, versions)
    /// minecraft_core klasöründe paylaşır; her birinin kendi oyun klasörü (mods, saves, config) vardır.
    /// </summary>
    internal sealed class GameLauncher
    {
        private static readonly Regex UsernameRegex = new(@"^[a-zA-Z0-9_]{1,16}$", RegexOptions.Compiled);

        private readonly Action<string, string> _log;
        private readonly JavaManager _java;
        private int _running;

        public GameLauncher(Action<string, string> log, JavaManager java)
        {
            _log = log;
            _java = java;
        }

        public bool IsRunning => _running == 1;

        /// <summary>Oyun kapandığında çağrılır.</summary>
        public event Action? Exited;

        public static string CorePath => Path.Combine(AppPaths.Base, "minecraft_core");

        public static MinecraftPath CreatePath(string gameDirectory) => new(gameDirectory)
        {
            Assets = Path.Combine(CorePath, "assets"),
            Library = Path.Combine(CorePath, "libraries"),
            Versions = Path.Combine(CorePath, "versions"),
            Runtime = Path.Combine(CorePath, "runtime")
        };

        /// <summary>Java'yı launcher kendisi yönettiği için Mojang'ın Java indiricisi devre dışı bırakılır.</summary>
        public static MinecraftLauncher CreateLauncher(MinecraftPath path)
        {
            var parameters = MinecraftLauncherParameters.CreateDefault(path, Downloader.Http);
            var javaExtractor = parameters.FileExtractors?.OfType<JavaFileExtractor>().FirstOrDefault();
            if (javaExtractor != null) parameters.FileExtractors!.Remove(javaExtractor);
            return new MinecraftLauncher(parameters);
        }

        /// <summary>Sürümün istediği Java ana sürümü; eski sürümler belirtmez, onlar Java 8 ister.</summary>
        public static async Task<int> GetRequiredJavaAsync(MinecraftLauncher launcher, string versionName)
        {
            var version = await launcher.GetVersionAsync(versionName);
            string? major = version.GetInheritedProperty(v => v.JavaVersion)?.MajorVersion;
            return int.TryParse(major, out int parsed) && parsed > 0 ? parsed : 8;
        }

        /// <summary>
        /// Gerekli Java'yı, oyun dosyalarını ve mod yükleyiciyi kurup oyunu başlatır.
        /// Başlatılamazsa nedenini açıklayan bir hata fırlatır.
        /// </summary>
        /// <param name="skins">Verilirse oyun vulu skin sistemiyle (authlib-injector) açılır; null ise eskisi gibi çevrimdışı.</param>
        public async Task LaunchAsync(LaunchTarget target, string username, int ramGb, SkinSession? skins = null)
        {
            if (!UsernameRegex.IsMatch(username))
                throw new InvalidOperationException(
                    "Kullanıcı adınız uygun değil; sadece ingilizce harf, sayı ve alt çizgi kullanılabilir.");

            if (Interlocked.Exchange(ref _running, 1) == 1)
                throw new InvalidOperationException("Oyun zaten çalışıyor.");

            // Her şey kuruluysa iş hızla biter ve panelde görünmez; ilk açılışta indirilenler panelde izlenir
            using var job = new DownloadJob($"{target.Name} hazırlanıyor") { Phase = "Oyun dosyaları denetleniyor" };
            try
            {
                var settings = AppSettings.Current;
                _log($"{target.Name} hazırlanıyor...", "accent");

                string instanceDir = Path.Combine(AppPaths.Instances, target.Folder);
                Directory.CreateDirectory(instanceDir);

                var launcher = CreateLauncher(CreatePath(instanceDir));

                int javaMajor = await GetRequiredJavaAsync(launcher, target.McVersion);
                string javaPath = await _java.EnsureAsync(javaMajor);

                string versionName = await EnsureLoaderAsync(launcher, target, javaPath);
                job.Phase = "Oyun dosyaları indiriliyor";

                var option = new MLaunchOption
                {
                    MaximumRamMb = ramGb * 1024,
                    // Skin sisteminde kimlik çevrimdışı kimliğin aynısıdır; token LAN/sunucu doğrulaması için kullanılır
                    Session = skins == null
                        ? MSession.CreateOfflineSession(username)
                        : new MSession(username, skins.AccessToken, skins.ProfileId) { UserType = "mojang" },
                    JavaPath = javaPath,
                    FullScreen = settings.Fullscreen
                };
                if (settings.GameWidth > 0 && settings.GameHeight > 0)
                {
                    option.ScreenWidth = settings.GameWidth;
                    option.ScreenHeight = settings.GameHeight;
                }
                if (!string.IsNullOrWhiteSpace(settings.JvmArgs))
                {
                    option.ExtraJvmArguments = MLaunchOption.DefaultExtraJvmArguments
                        .Append(MArgument.FromCommandLine(settings.JvmArgs));
                }

                var process = await launcher.InstallAndBuildProcessAsync(
                    versionName, option, FileProgress(), ByteProgress(), CancellationToken.None);

                if (skins != null) AddSkinAgent(process.StartInfo, skins);

                // Launcher oyun açılınca kapanacaksa çıktı yönlendirilmez; okuyan kalmayacağı için
                bool closeAfterStart = settings.OnGameStart == "close";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = !closeAfterStart;
                process.StartInfo.RedirectStandardError = !closeAfterStart;

                process.OutputDataReceived += (_, ev) => { if (ev.Data != null) _log(ev.Data, "game"); };
                process.ErrorDataReceived += (_, ev) => { if (ev.Data != null) _log(ev.Data, "gameError"); };

                process.EnableRaisingEvents = true;
                process.Exited += (_, _) =>
                {
                    Interlocked.Exchange(ref _running, 0);
                    _log("Oyun kapatıldı. İyi günler!", "warn");
                    Exited?.Invoke();
                };

                process.Start();
                if (!closeAfterStart)
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }

                _log("Oyun başlatıldı! İyi oyunlar.", "ok");
                job.Complete("Oyun başlatıldı");
            }
            catch (Exception ex)
            {
                job.Fail(ex.Message);
                Interlocked.Exchange(ref _running, 0);
                throw;
            }
        }

        /// <summary>
        /// authlib-injector'ı Java ajanı olarak ekler: oyun skinleri ve oturum doğrulamayı vulu-api'den alır.
        /// JVM seçenekleri ana sınıftan önce gelmeli; komutun en başına eklenir (yolda boşluk olabileceği için tırnaklı).
        /// </summary>
        private static void AddSkinAgent(ProcessStartInfo info, SkinSession skins)
        {
            string agent = $"-javaagent:{skins.AgentJar}={skins.ApiRoot}";
            string prefetched = "-Dauthlibinjector.yggdrasil.prefetched=" +
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(skins.Metadata));

            if (info.ArgumentList.Count > 0)
            {
                info.ArgumentList.Insert(0, prefetched);
                info.ArgumentList.Insert(0, agent);
            }
            else
            {
                info.Arguments = $"\"{agent}\" {prefetched} {info.Arguments}";
            }
        }

        /// <summary>
        /// Mod yükleyiciyi (gerekiyorsa) kurar ve başlatılacak sürüm adını döner.
        /// Zaten kuruluysa yeniden kurmaz.
        /// </summary>
        public async Task<string> EnsureLoaderAsync(MinecraftLauncher launcher, LaunchTarget target, string javaPath)
        {
            string mc = target.McVersion;
            string loaderVersion = target.LoaderVersion ?? "";

            switch (target.Loader)
            {
                case "vanilla":
                    return mc;

                case "fabric":
                {
                    string name = FabricInstaller.GetVersionName(mc, loaderVersion);
                    if (File.Exists(launcher.MinecraftPath.GetVersionJsonPath(name))) return name;

                    _log($"Fabric {loaderVersion} kuruluyor...", "accent");
                    return await new FabricInstaller(Downloader.Http).Install(mc, loaderVersion, launcher.MinecraftPath);
                }

                case "quilt":
                {
                    string name = QuiltInstaller.GetVersionName(mc, loaderVersion);
                    if (File.Exists(launcher.MinecraftPath.GetVersionJsonPath(name))) return name;

                    _log($"Quilt {loaderVersion} kuruluyor...", "accent");
                    return await new QuiltInstaller(Downloader.Http).Install(mc, loaderVersion, launcher.MinecraftPath);
                }

                case "forge":
                {
                    _log($"Forge {loaderVersion} kontrol ediliyor...", "accent");
                    // Aynı adla kurulmuş eski NeoForge profili Forge sanılmasın
                    bool reuse = !LegacyNeoForge.ProfileIsNeoForge(launcher, mc, loaderVersion);
                    return await new ForgeInstaller(launcher, Downloader.Http).Install(mc, loaderVersion, InstallOptions(javaPath, reuse));
                }

                case "neoforge" when LegacyNeoForge.IsLegacy(mc):
                {
                    _log($"NeoForge {loaderVersion} kontrol ediliyor...", "accent");
                    bool reuse = LegacyNeoForge.ProfileIsNeoForge(launcher, mc, loaderVersion);
                    return await new ForgeInstaller(launcher, Downloader.Http)
                        .Install(LegacyNeoForge.CreateVersion(mc, loaderVersion), InstallOptions(javaPath, reuse));
                }

                case "neoforge":
                {
                    _log($"NeoForge {loaderVersion} kontrol ediliyor...", "accent");
                    return await new NeoForgeInstaller(launcher, Downloader.Http).Install(mc, loaderVersion, InstallOptions(javaPath));
                }

                default:
                    throw new InvalidOperationException($"Bilinmeyen mod yükleyici: {target.Loader}");
            }
        }

        private ForgeInstallOptions InstallOptions(string javaPath, bool skipIfInstalled = true) => new()
        {
            JavaPath = javaPath,
            SkipIfAlreadyInstalled = skipIfInstalled,
            FileProgress = FileProgress(),
            InstallerOutput = new ThrottledProgress<string>(line => _log(line, "replace"), 100)
        };

        private IProgress<InstallerProgressChangedEventArgs> FileProgress()
        {
            // İlerleme başka thread'den gelir; iş, akıştan şimdi alınır
            var job = DownloadJob.Current;
            var log = new ThrottledProgress<InstallerProgressChangedEventArgs>(e =>
                _log($"Dosyalar kontrol ediliyor... {e.ProgressedTasks}/{e.TotalTasks}  {e.Name}", "replace"), 100);
            return new ThrottledProgress<InstallerProgressChangedEventArgs>(e =>
            {
                job?.Aggregate(e.ProgressedTasks, e.TotalTasks, e.Name);
                log.Report(e);
            }, 0);
        }

        private IProgress<ByteProgress>? ByteProgress()
        {
            var job = DownloadJob.Current;
            return job == null ? null : new ThrottledProgress<ByteProgress>(p => job.AggregateBytes(p.ProgressedBytes, p.TotalBytes), 0);
        }
    }

    /// <summary>Instance oluşturma ekranı için Minecraft ve mod yükleyici sürümlerini listeler.</summary>
    internal static class LoaderCatalog
    {
        private static List<string>? _minecraftVersions;

        /// <summary>Beta/alfa sürümleri ("-beta" gibi ek taşıyanlar) listenin sonuna alır; sıra aksi halde korunur.</summary>
        private static List<string> StableFirst(IEnumerable<string> versions) =>
            versions.Distinct().OrderBy(v => v.Contains('-') ? 1 : 0).ToList();

        private static MinecraftLauncher CoreLauncher() =>
            GameLauncher.CreateLauncher(GameLauncher.CreatePath(GameLauncher.CorePath));

        /// <summary>Yayınlanmış (release) Minecraft sürümleri, yeniden eskiye.</summary>
        public static async Task<List<string>> MinecraftVersionsAsync()
        {
            if (_minecraftVersions != null) return _minecraftVersions;

            var versions = await CoreLauncher().GetAllVersionsAsync();
            _minecraftVersions = versions
                .Where(v => v.Type == "release")
                .OrderByDescending(v => v.ReleaseTime)
                .Select(v => v.Name)
                .ToList();
            return _minecraftVersions;
        }

        /// <summary>Verilen Minecraft sürümü için mod yükleyici sürümleri; ilk sıradaki önerilendir.</summary>
        public static async Task<List<string>> LoaderVersionsAsync(string loader, string mcVersion)
        {
            switch (loader)
            {
                case "vanilla":
                    return new List<string>();

                case "fabric":
                {
                    var loaders = await new FabricInstaller(Downloader.Http).GetLoaders(mcVersion);
                    return loaders.OrderByDescending(l => l.Stable).Select(l => l.Version!).Where(v => v != null).Distinct().ToList();
                }

                case "quilt":
                {
                    var loaders = await new QuiltInstaller(Downloader.Http).GetLoaders(mcVersion);
                    return StableFirst(loaders.Select(l => l.Version!).Where(v => v != null));
                }

                case "forge":
                {
                    var versions = (await new ForgeInstaller(CoreLauncher(), Downloader.Http).GetForgeVersions(mcVersion)).ToList();
                    return versions
                        .OrderByDescending(v => v.IsRecommendedVersion)
                        .ThenByDescending(v => v.IsLatestVersion)
                        .Select(v => v.ForgeVersionName!)
                        .Where(v => v != null)
                        .Distinct()
                        .ToList();
                }

                case "neoforge" when LegacyNeoForge.IsLegacy(mcVersion):
                    return await LegacyNeoForge.VersionsAsync(mcVersion);

                case "neoforge":
                {
                    var versions = await new NeoForgeInstaller(CoreLauncher(), Downloader.Http).GetNeoForgeVersions(mcVersion);
                    return StableFirst(versions.Select(v => v.NeoForgeVersionName));
                }

                default:
                    throw new InvalidOperationException($"Bilinmeyen mod yükleyici: {loader}");
            }
        }
    }

    /// <summary>
    /// NeoForge'un 1.20.1 sürümleri, sonrakilerden farklı olarak Forge'un bir kolu olarak "net/neoforged/forge"
    /// altında ("1.20.1-47.1.106" gibi) yayınlandı. NeoForge kütüphanesi bunları bulamadığı için listeyi
    /// buradan alır, kurulumu da Forge kurucusuyla (kurucu dosyası NeoForge'un Maven'ından) yaparız.
    /// Kurulan profil Forge ile aynı adı taşır ("1.20.1-forge-47.1.106"); hangisine ait olduğu içinden okunur.
    /// </summary>
    internal static class LegacyNeoForge
    {
        private const string Maven = "https://maven.neoforged.net/releases/net/neoforged/forge";
        private static readonly Dictionary<string, List<string>> Cache = new();

        public static bool IsLegacy(string mcVersion) => mcVersion == "1.20.1";

        public static async Task<List<string>> VersionsAsync(string mcVersion)
        {
            lock (Cache) { if (Cache.TryGetValue(mcVersion, out var cached)) return cached; }

            string xml = await Downloader.Http.GetStringAsync($"{Maven}/maven-metadata.xml");
            string prefix = mcVersion + "-";
            var versions = Regex.Matches(xml, @"<version>([^<]+)</version>")
                .Select(m => m.Groups[1].Value)
                .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
                .Select(v => v[prefix.Length..])
                .Where(v => Version.TryParse(v, out _))
                .OrderByDescending(v => Version.Parse(v))
                .ToList();

            lock (Cache) Cache[mcVersion] = versions;
            return versions;
        }

        public static CmlLib.Core.Installer.Forge.Versions.ForgeVersion CreateVersion(string mcVersion, string loaderVersion)
        {
            if (!Version.TryParse(loaderVersion, out _))
                throw new InvalidOperationException($"Geçersiz NeoForge sürümü: {loaderVersion}");

            string full = $"{mcVersion}-{loaderVersion}";
            return new CmlLib.Core.Installer.Forge.Versions.ForgeVersion(mcVersion, loaderVersion)
            {
                Files = new[]
                {
                    new ForgeVersionFile { Type = "installer", DirectUrl = $"{Maven}/{full}/forge-{full}-installer.jar" }
                }
            };
        }

        /// <summary>Bu adla kurulu profil NeoForge'a mı ait? (Kurulu değilse false.)</summary>
        public static bool ProfileIsNeoForge(MinecraftLauncher launcher, string mcVersion, string loaderVersion)
        {
            string path = launcher.MinecraftPath.GetVersionJsonPath($"{mcVersion}-forge-{loaderVersion}");
            try { return File.Exists(path) && File.ReadAllText(path).Contains("net.neoforged", StringComparison.Ordinal); }
            catch { return false; }
        }
    }
}
