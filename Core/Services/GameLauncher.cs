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
    /// <summary>Kapanan oyun hakkında çökme yardımcısının ihtiyaç duyduğu bilgiler.</summary>
    internal sealed record GameExit(LaunchTarget Target, string InstanceDir, int ExitCode, DateTime StartedUtc, IReadOnlyList<string> Output);

    internal sealed class GameLauncher
    {
        private static readonly Regex UsernameRegex = new(@"^[a-zA-Z0-9_]{1,16}$", RegexOptions.Compiled);

        private readonly Action<string, string> _log;
        private readonly JavaManager _java;
        private readonly IGameRunner? _runner;
        private int _running;

        /// <param name="runner">Oyunu platformun motoru açıyorsa (Android) o; null ise oyun java.exe süreci olarak açılır.</param>
        public GameLauncher(Action<string, string> log, JavaManager java, IGameRunner? runner = null)
        {
            _log = log;
            _java = java;
            _runner = runner;
        }

        public bool IsRunning => _running == 1;
        private volatile bool _downloadingGameFiles;   // CmlLib bayt bildirdiyse dosya inmekte demektir

        /// <summary>Oyun kapandığında çağrılır.</summary>
        public event Action? Exited;

        /// <summary>Son kapanan oyunun bilgileri (çıkış kodu, oyun çıktısının son satırları).</summary>
        public GameExit? LastExit { get; private set; }

        private const int OutputKeep = 600;
        private readonly Queue<string> _output = new();

        public static string CorePath => AppPaths.GameCore;

        public static MinecraftPath CreatePath(string gameDirectory) => new(gameDirectory)
        {
            Assets = Path.Combine(CorePath, "assets"),
            Library = Path.Combine(CorePath, "libraries"),
            Versions = Path.Combine(CorePath, "versions"),
            Runtime = Path.Combine(CorePath, "runtime")
        };

        /// <summary>
        /// Java'yı launcher kendisi yönettiği için Mojang'ın Java indiricisi devre dışı bırakılır.
        /// CmlLib varsayılan olarak her açılışta binlerce dosyanın hash'ini hesaplar; oyun açılırken yalnızca dosyanın
        /// var olup olmadığı ve boyutu denetlenir, hash'ler "Dosyaları doğrula"da (verifyHashes) denetlenir.
        /// </summary>
        public static MinecraftLauncher CreateLauncher(MinecraftPath path, bool verifyHashes = false)
        {
            var parameters = MinecraftLauncherParameters.CreateDefault(path, Downloader.Http);
            var javaExtractor = parameters.FileExtractors?.OfType<JavaFileExtractor>().FirstOrDefault();
            if (javaExtractor != null) parameters.FileExtractors!.Remove(javaExtractor);
            if (parameters.GameInstaller is CmlLib.Core.Installers.GameInstallerBase installer)
            {
                installer.CheckFileSize = true;
                installer.CheckFileChecksum = verifyHashes;
            }
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
        /// <param name="serverAddress">Verilirse oyun açılınca doğrudan bu sunucuya bağlanır (host ya da host:port).</param>
        public async Task LaunchAsync(LaunchTarget target, string username, int ramGb, SkinSession? skins = null, string? serverAddress = null)
        {
            if (!UsernameRegex.IsMatch(username))
                throw new InvalidOperationException(
                    "Kullanıcı adınız uygun değil; sadece ingilizce harf, sayı ve alt çizgi kullanılabilir.");

            if (Interlocked.Exchange(ref _running, 1) == 1)
                throw new InvalidOperationException("Oyun zaten çalışıyor.");

            // Her şey kuruluysa iş hızla biter ve panelde görünmez; ilk açılışta indirilenler panelde izlenir
            using var job = new DownloadJob($"{target.Name} hazırlanıyor") { Phase = "Hazırlanıyor" };
            try
            {
                var settings = AppSettings.Current;
                _log($"{target.Name} hazırlanıyor...", "accent");

                string instanceDir = Path.Combine(AppPaths.Instances, target.Folder);
                Directory.CreateDirectory(instanceDir);

                var launcher = CreateLauncher(CreatePath(instanceDir));

                if (_runner != null)
                {
                    await LaunchWithRunnerAsync(launcher, target, instanceDir, username, ramGb, skins, job);
                    return;
                }

                job.Phase = "Java hazırlanıyor";
                int javaMajor = await GetRequiredJavaAsync(launcher, target.McVersion);
                string javaPath = await _java.EnsureAsync(javaMajor);

                job.Phase = target.Loader == "vanilla" ? "Minecraft hazırlanıyor" : "Mod yükleyici hazırlanıyor";
                string versionName = await EnsureLoaderAsync(launcher, target, javaPath);
                // Bir dosya inmeye başlarsa ByteProgress bunu "indiriliyor" yapar
                job.Phase = "Oyun dosyaları kontrol ediliyor";

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
                if (!string.IsNullOrEmpty(serverAddress))
                {
                    var (host, port) = ServerPing.Split(serverAddress);
                    option.ServerIp = host;
                    option.ServerPort = port;
                }
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

                lock (_output) _output.Clear();
                void Keep(string line)
                {
                    lock (_output)
                    {
                        _output.Enqueue(line);
                        while (_output.Count > OutputKeep) _output.Dequeue();
                    }
                }
                process.OutputDataReceived += (_, ev) => { if (ev.Data != null) { Keep(ev.Data); _log(ev.Data, "game"); } };
                process.ErrorDataReceived += (_, ev) => { if (ev.Data != null) { Keep(ev.Data); _log(ev.Data, "gameError"); } };

                var startedUtc = DateTime.UtcNow;
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) =>
                {
                    int code;
                    try { code = process.ExitCode; } catch { code = 0; }
                    lock (_output) LastExit = new GameExit(target, instanceDir, code, startedUtc, _output.ToList());
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
        /// Oyunu açmadan oyun dosyalarını denetler: Java, mod yükleyici, Minecraft'ın kendisi, kütüphaneler ve
        /// kaynaklar. Eksik ya da hash'i tutmayan dosya yeniden indirilir ("Dosyaları doğrula").
        /// </summary>
        public async Task VerifyGameAsync(LaunchTarget target)
        {
            if (IsRunning) throw new InvalidOperationException("Oyun açıkken dosyalar doğrulanamaz.");
            string instanceDir = Path.Combine(AppPaths.Instances, target.Folder);
            Directory.CreateDirectory(instanceDir);
            var launcher = CreateLauncher(CreatePath(instanceDir), verifyHashes: true);

            // Android'de Java'yı oyun motoru seçer; Forge ve NeoForge orada henüz kurulamıyor
            string javaPath = "";
            if (_runner == null) javaPath = await _java.EnsureAsync(await GetRequiredJavaAsync(launcher, target.McVersion));
            else if (target.Loader is "forge" or "neoforge")
                throw new InvalidOperationException("Forge ve NeoForge paketlerinin oyun dosyaları telefonda denetlenemiyor.");

            string versionName = await EnsureLoaderAsync(launcher, target, javaPath);
            await launcher.InstallAsync(versionName, FileProgress(), ByteProgress(), CancellationToken.None);
        }

        /// <summary>
        /// authlib-injector'ı Java ajanı olarak ekler: oyun skinleri ve oturum doğrulamayı vulu-api'den alır.
        /// JVM seçenekleri ana sınıftan önce gelmeli; komutun en başına eklenir (yolda boşluk olabileceği için tırnaklı).
        /// </summary>
        /// <summary>
        /// Android: oyun dosyaları ve mod yükleyici burada (CmlLib) kurulur, oyunu platformun motoru (Amethyst) açar.
        /// Java'yı da motor kendisi seçer ve gerekirse indirir. Forge ve NeoForge kurulumu Java çalıştırdığı için henüz yok.
        /// Oyun ayrı bir süreçte çalışır; kapanışı buradan izlenmez.
        /// </summary>
        private async Task LaunchWithRunnerAsync(MinecraftLauncher launcher, LaunchTarget target, string instanceDir,
            string username, int ramGb, SkinSession? skins, DownloadJob job)
        {
            if (target.Loader is "forge" or "neoforge")
                throw new InvalidOperationException("Forge ve NeoForge paketleri telefonda henüz açılamıyor; Vanilla, Fabric ve Quilt açılabilir.");
            try
            {
                string versionName = await EnsureLoaderAsync(launcher, target, javaPath: "");
                job.Phase = "Oyun dosyaları indiriliyor";
                await launcher.InstallAsync(versionName, FileProgress(), ByteProgress(), CancellationToken.None);

                var javaArgs = new List<string>();
                if (skins != null)
                {
                    javaArgs.Add($"-javaagent:{skins.AgentJar}={skins.ApiRoot}");
                    javaArgs.Add("-Dauthlibinjector.yggdrasil.prefetched=" +
                        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(skins.Metadata)));
                }
                if (!string.IsNullOrWhiteSpace(AppSettings.Current.JvmArgs)) javaArgs.Add(AppSettings.Current.JvmArgs.Trim());

                // Skin sisteminde kimlik çevrimdışı kimliğin aynısıdır (MSession.CreateOfflineSession ile aynı UUID)
                var session = skins == null ? MSession.CreateOfflineSession(username) : new MSession(username, skins.AccessToken, skins.ProfileId);
                string uuid = Guid.TryParse(session.UUID, out var parsed) ? parsed.ToString("D") : session.UUID ?? "";

                job.Phase = "Oyun açılıyor";
                _log("Oyun açılıyor; ilk açılışta Java indirilebilir, biraz sürebilir.", "accent");
                await _runner!.LaunchAsync(new GameRun(instanceDir, versionName, string.Join(' ', javaArgs), ramGb * 1024,
                    username, session.AccessToken ?? "0", uuid));
                _log("Oyun başlatıldı! İyi oyunlar.", "ok");
                job.Complete("Oyun başlatıldı");
            }
            finally
            {
                // Oyun ayrı süreçte; launcher yeniden "oynanabilir" duruma döner
                Interlocked.Exchange(ref _running, 0);
            }
        }

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
            // Forge/NeoForge kurulumu "processor" programlarını Java ile pencere gizlemeden çalıştırır;
            // java.exe her seferinde boş bir konsol penceresi açardı. javaw.exe aynı Java'dır ama pencere açmaz.
            JavaPath = WindowlessJava(javaPath),
            SkipIfAlreadyInstalled = skipIfInstalled,
            FileProgress = FileProgress(),
            InstallerOutput = new ThrottledProgress<string>(line => _log(line, "replace"), 100)
        };

        /// <summary>java.exe'nin yanında javaw.exe varsa onu döner (konsol penceresi açmayan sürüm); yoksa verileni.</summary>
        private static string WindowlessJava(string javaPath)
        {
            if (!string.Equals(Path.GetFileName(javaPath), "java.exe", StringComparison.OrdinalIgnoreCase)) return javaPath;
            string javaw = Path.Combine(Path.GetDirectoryName(javaPath)!, "javaw.exe");
            return File.Exists(javaw) ? javaw : javaPath;
        }

        private IProgress<InstallerProgressChangedEventArgs> FileProgress()
        {
            // İlerleme başka thread'den gelir; iş, akıştan şimdi alınır
            var job = DownloadJob.Current;
            // Bir dosya gerçekten indiriliyorsa "indiriliyor", yalnızca denetleniyorsa "kontrol ediliyor" yazar
            var log = new ThrottledProgress<InstallerProgressChangedEventArgs>(e =>
                _log($"{(_downloadingGameFiles ? "Oyun dosyaları indiriliyor" : "Dosyalar kontrol ediliyor")}... {e.ProgressedTasks}/{e.TotalTasks}  {e.Name}", "replace"), 100);
            return new ThrottledProgress<InstallerProgressChangedEventArgs>(e =>
            {
                job?.Aggregate(e.ProgressedTasks, e.TotalTasks, e.Name);
                log.Report(e);
            }, 0);
        }

        private IProgress<ByteProgress>? ByteProgress()
        {
            var job = DownloadJob.Current;
            _downloadingGameFiles = false;
            return new ThrottledProgress<ByteProgress>(p =>
            {
                if (p.TotalBytes > 0 && !_downloadingGameFiles)
                {
                    _downloadingGameFiles = true;
                    if (job != null) job.Phase = "Oyun dosyaları indiriliyor";
                }
                job?.AggregateBytes(p.ProgressedBytes, p.TotalBytes);
            }, 0);
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
