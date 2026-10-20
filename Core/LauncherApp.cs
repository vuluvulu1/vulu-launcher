using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Arayüzün çağırdığı komutların tamamı. Arayüz yalnızca görüntüler ve sorar;
    /// giriş, izin kontrolü, güncelleme ve oyun başlatma kararları burada verilir.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private readonly Bridge _bridge;
        private readonly IPlatformHost _host;

        private LoginService _login = new();
        private readonly ApiClient _api;
        private readonly LauncherUpdateService _launcherUpdates = new();
        private readonly JavaManager _java;
        private readonly ModrinthService _modrinth;
        private readonly GameLauncher _game;
        private readonly DiscordPresence _discord = new();

        private VuluShared.ReleaseManifest? _latestLauncher;

        public LauncherApp(Bridge bridge, IPlatformHost host)
        {
            _bridge = bridge;
            _host = host;
            CredentialManager.Protect = host.Protect;
            CredentialManager.Unprotect = host.Unprotect;
            LocalFiles.Url = host.FileUrl;
            _api = new ApiClient(() => _login.Token);
            _java = new JavaManager(Log);
            _modrinth = new ModrinthService(Log);
            _game = new GameLauncher(Log, _java);
            _game.Exited += () => { StopPlayTime(); _bridge.Emit("gameExited"); _discord.ShowIdle(); PresenceIdle(); _ = ReportCrashIfAnyAsync(); };
            // Arayüz hangi sayfada olunduğunu (ve Oyna'da seçili instance'ı) bildirir
            _bridge.On("discord.page", args =>
            {
                string page = args.Str("page");
                string? name = string.IsNullOrWhiteSpace(args.Str("instance")) ? null : args.Str("instance").Trim();
                _discord.ShowPage(page, name, args.Str("mcVersion"), args.Str("loader"));
                return Task.FromResult<object?>(null);
            });
            _discord.ShowIdle();
            _discord.SetEnabled(_host.IsDesktop && AppSettings.Current.DiscordPresence);
            DownloadJob.Emit = snapshot => _bridge.Emit("download", snapshot);
        }

        public void RegisterCommands()
        {
            // ── Pencere ───────────────────────────────────────────────────
            _bridge.OnInline("window.dragStart", _ => _host.BeginWindowDrag());
            _bridge.OnInline("window.dragMove", _ => _host.MoveWindowDrag());
            _bridge.OnInline("window.minimize", _ => _host.Minimize());
            _bridge.OnInline("window.close", _ => _host.Close());

            // ── Uygulama ──────────────────────────────────────────────────
            _bridge.On("app.init", _ => Task.FromResult<object?>(Init()));
            _bridge.On("app.wake", async _ => await WakeServerAsync());

            _bridge.On("auth.login", async args => await LoginAsync(args));

            _bridge.On("launcher.check", async _ => await CheckLauncherVersionAsync());
            _bridge.On("launcher.update", _ => Task.FromResult<object?>(StartLauncherUpdate()));

            _bridge.On("game.play", async args => await PlayAsync(args.Str("instanceId"), args.Int("ramGb"), args.Str("mcVersion"), args.Bool("ignoreMissing"), args.Str("join")));
            _bridge.On("mods.installMissing", async args => await InstallMissingAsync(args.Str("instanceId"), args.StrList("projectIds")));
            _bridge.On("game.versions", async _ => { RequireLogin(); return await LoaderCatalog.MinecraftVersionsAsync(); });

            RegisterInstanceCommands();
            RegisterPackCommands();
            RegisterHealthCommands();
            RegisterCommunityCommands();
            RegisterWorldCommands();
            RegisterMessageCommands();
            RegisterLibraryCommands();
            RegisterSocialCommands();

            // vulu-api ücretsiz planda boşta kalınca uyuyor; açılışta uyandırılır ki giriş yapılana kadar hazır olsun
            _ = Task.Run(async () => { try { using var _ = await Downloader.Http.GetAsync(ApiConfig.BaseUrl + "/ping"); } catch { } });
            RegisterProfileCommands();

            // ── Admin ─────────────────────────────────────────────────────
            RegisterAdminCommands();
        }

        public void Shutdown()
        {
            StopPlayTime();
            _bridge.Shutdown();
        }

        /// <summary>Bildirime tıklanınca launcher öne gelir ve ilgili sayfa açılır.</summary>
        public void OnNotificationClicked(string? tag)
        {
            _host.BringToFront();
            if (tag != null) _bridge.Emit("notificationClicked", new { tag });
        }

        /// <summary>Bildirimler yalnızca launcher kullanıcının önünde değilken gösterilir.</summary>
        private void NotifyIfAway(string title, string body, string tag)
        {
            if (!_host.IsForeground) _host.Notify(title, body, tag);
        }

        private void Log(string text, string level) =>
            _bridge?.Log(text, level == "replace" ? "accent" : level, replaceLast: level == "replace");

        // ════════════════════════════════════════════════════════════════
        // UYGULAMA / GİRİŞ
        // ════════════════════════════════════════════════════════════════
        private object Init()
        {
            var (username, password) = CredentialManager.Load();
            return new
            {
                version = LauncherUpdateService.LocalVersion,
                platform = _host.Platform,
                canLaunchGame = _host.CanLaunchGame,
                isDesktop = _host.IsDesktop,
                apiBaseUrl = ApiConfig.BaseUrl,
                savedUsername = username,
                // Kayıtlı şifre arayüze gönderilmez; arayüz sadece var olduğunu bilir
                hasSavedPassword = !string.IsNullOrEmpty(password),
                ramOptionsGb = PackCatalog.RamOptionsGb,
                settings = AppSettings.Current
            };
        }

        /// <summary>Render'daki sunucu uykudaysa uyandırır; giriş ekranı açılır açılmaz çağrılır.</summary>
        private static async Task<object?> WakeServerAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                using var response = await http.GetAsync($"{ApiConfig.BaseUrl}/ping");
                return new { reachable = true };
            }
            catch
            {
                return new { reachable = false };
            }
        }

        private async Task<object?> LoginAsync(JsonElement args)
        {
            string username = args.Str("username").Trim();
            string password = args.Str("password");
            bool remember = args.Bool("remember");

            // Arayüz kayıtlı şifreyi görmez; "kayıtlı şifreyi kullan" derse burada yüklenir
            if (args.Bool("useSaved"))
            {
                var saved = CredentialManager.Load();
                if (string.Equals(saved.Username, username, StringComparison.Ordinal))
                    password = saved.Password;
            }

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return new { success = false, error = "Kullanıcı adı ve şifre boş bırakılamaz." };

            var result = await _login.LoginAsync(username, password);
            if (!result.Success)
                return new { success = false, error = result.ErrorMessage ?? "Giriş başarısız." };

            if (remember) CredentialManager.Save(username, password);
            else CredentialManager.Clear();
            _ = RepairPackLogosAsync();
            StartPresence();
            _ = SetUpMessagesAsync(_login.Username, password);
            StartPackUpdateChecks();
            StartShotEventPolling();
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => SyncPlaytimeAsync());

            return new
            {
                success = true,
                error = (string?)null,
                username = _login.Username,
                isAdmin = _login.IsAdmin
            };
        }

        private void RequireLogin()
        {
            if (string.IsNullOrEmpty(_login.Token))
                throw new InvalidOperationException("Önce giriş yapmalısınız.");
        }

        // ════════════════════════════════════════════════════════════════
        // LAUNCHER GÜNCELLEMESİ
        // ════════════════════════════════════════════════════════════════
        private async Task<object?> CheckLauncherVersionAsync()
        {
            // Android sürümü kendini güncellemez: yeni APK sitede yayınlandıysa indirmesi önerilir
            if (!_host.CanSelfUpdate) return await CheckAndroidUpdateAsync();

            var (status, latest) = await _launcherUpdates.CheckForUpdateAsync();

            if (status == UpdateCheckStatus.CheckFailed)
            {
                Log("Launcher sürümü kontrol edilemedi (sunucuya ulaşılamadı).", "muted");
                return new { status = "failed" };
            }

            if (status == UpdateCheckStatus.UpToDate || latest == null)
            {
                Log("Launcher güncel.", "muted");
                return new { status = "upToDate" };
            }

            _latestLauncher = latest;
            string version = VuluShared.Releases.Clean(latest.Version);
            Log($"Yeni launcher sürümü mevcut: v{version}", "warn");

            // Zip ve açılmış hali için yer gerekir; boyut bilinmiyorsa güncelleme yine de önerilir
            double? sizeMb = latest.Size > 0 ? Math.Round(latest.Size / 1024.0 / 1024.0, 1) : null;
            bool diskOk = true;
            long neededMb = 0, freeMb = 0;
            try
            {
                if (latest.Size > 0)
                {
                    var drive = new DriveInfo(Path.GetPathRoot(AppPaths.Base) ?? "C:\\");
                    long needed = latest.Size * 3 + 50L * 1024 * 1024;
                    neededMb = needed / 1024 / 1024;
                    freeMb = drive.AvailableFreeSpace / 1024 / 1024;
                    diskOk = drive.AvailableFreeSpace >= needed;
                }
            }
            catch { }

            return new { status = "available", version, notes = latest.Notes, sizeMb, diskOk, neededMb, freeMb };
        }

        private async Task<object?> CheckAndroidUpdateAsync()
        {
            AndroidUpdateFeed.Manifest? latest;
            try { latest = await AndroidUpdateFeed.CheckAsync(); }
            catch
            {
                Log("Launcher sürümü kontrol edilemedi (sunucuya ulaşılamadı).", "muted");
                return new { status = "failed" };
            }
            if (latest == null) return new { status = "upToDate" };

            _androidUpdateAvailable = true;
            string version = VuluShared.Releases.Clean(latest.Version);
            Log($"Yeni launcher sürümü mevcut: v{version}", "warn");
            double? sizeMb = latest.Size > 0 ? Math.Round(latest.Size / 1024.0 / 1024.0, 1) : null;
            return new { status = "available", version, notes = latest.Notes, sizeMb, diskOk = true, neededMb = 0, freeMb = 0, external = true };
        }

        private bool _androidUpdateAvailable;

        /// <summary>Updater'ı açar ve launcher'ı kapatır; Updater kapanmayı bekleyip kurar ve launcher'ı yeniden açar.</summary>
        private object StartLauncherUpdate()
        {
            if (!_host.CanSelfUpdate)
            {
                if (!_androidUpdateAvailable) throw new InvalidOperationException("Bekleyen launcher güncellemesi yok.");
                _host.OpenUrl(AndroidUpdateFeed.ApkUrl);   // APK tarayıcıda iner, Android kurar
                return new { started = true };
            }

            if (_latestLauncher == null)
                throw new InvalidOperationException("Bekleyen launcher güncellemesi yok.");

            _host.StartLauncherUpdate();
            return new { started = true };
        }

        // ════════════════════════════════════════════════════════════════
        // OYUN
        // ════════════════════════════════════════════════════════════════
        /// <summary>Instance vulu Packs'ten geldiyse paketin logosu (kare zeminde; yoksa kapağı); Discord durumunda gösterilir.</summary>
        private async Task<string?> PackImageUrlAsync(string instanceId)
        {
            if (!instanceId.StartsWith(InstancePrefix, StringComparison.Ordinal)) return null;
            string? packId = InstanceStore.Get(instanceId[InstancePrefix.Length..])?.SharedPackId;
            if (packId == null) return null;
            try
            {
                var lookup = Packs.GetAsync(packId);
                if (await Task.WhenAny(lookup, Task.Delay(TimeSpan.FromSeconds(6))) != lookup) return null;
                var pack = await lookup;
                if (pack == null) return null;
                return (pack.HasLogo ? $"{ApiConfig.BaseUrl}/packs/{Uri.EscapeDataString(pack.Id)}/logo/square?v={pack.Revision}" : null)
                    ?? PackStore.CoverUrl(pack);
            }
            catch { return null; }   // paket bilgisi alınamazsa vulu logosu kalır
        }

        /// <summary>
        /// Oyun açılmadan önce mods klasörü taranır; zorunlu bir bağımlılık eksikse oyun açılmaz, liste arayüze döner
        /// (kendi instance'larında Modrinth karşılığıyla birlikte). Tarama hata verirse oyun engellenmez.
        /// </summary>
        private async Task<object?> MissingDependenciesAsync(string instanceId, LaunchTarget target)
        {
            if (target.Loader == "vanilla") return null;
            try
            {
                var scan = await Task.Run(() => ModScanner.Scan(Path.Combine(AppPaths.Instances, target.Folder), target.Loader));
                if (scan.Missing.Count == 0) return null;

                var meta = instanceId.StartsWith(InstancePrefix, StringComparison.Ordinal) ? InstanceStore.Get(instanceId[InstancePrefix.Length..]) : null;
                Dictionary<string, MrProject?> projects = new();
                if (meta != null)
                {
                    var lookup = _modrinth.ResolveModIdsAsync(meta, scan, scan.Missing);
                    if (await Task.WhenAny(lookup, Task.Delay(TimeSpan.FromSeconds(12))) == lookup) projects = await lookup;
                }
                Log($"Eksik bağımlılık: {string.Join(", ", scan.Missing.Select(m => m.Id))}", "warn");
                return scan.Missing.Select(m =>
                {
                    projects.TryGetValue(m.Id, out var p);
                    return new { id = m.Id, requiredBy = m.RequiredBy, projectId = p?.Id, title = p?.Title, iconUrl = p?.IconUrl };
                }).ToList();
            }
            catch (Exception) { return null; }
        }

        private async Task<object?> InstallMissingAsync(string instanceId, List<string> projectIds)
        {
            var meta = RequireCustomInstance(instanceId);
            if (_game.IsRunning) throw new InvalidOperationException("Oyun çalışırken mod kurulamaz.");
            var installed = new List<string>();
            foreach (var id in projectIds.Where(i => System.Text.RegularExpressions.Regex.IsMatch(i, "^[A-Za-z0-9]{6,16}$")).Distinct())
                installed.AddRange(await _modrinth.InstallAsync(meta, id));
            Log($"Eksik bağımlılıklar kuruldu: {string.Join(", ", installed.Distinct())}", "ok");
            return new { installed };
        }

        private async Task<object?> PlayAsync(string instanceId, int ramGb, string mcVersion, bool ignoreMissing = false, string? join = null)
        {
            RequireLogin();
            if (!_host.CanLaunchGame)
                return new { started = false, error = "Bu cihazda oyun başlatma henüz desteklenmiyor." };

            try
            {
                var (target, recommendedRamGb) = ResolveTarget(instanceId);
                if (!ignoreMissing && await MissingDependenciesAsync(instanceId, target) is { } missing)
                    return new { started = false, error = (string?)null, missing };

                // Sunucunun Vanilla paketinde kullanıcı Minecraft sürümünü seçebilir; modlu paketlerin sürümü sabittir
                if (!string.IsNullOrWhiteSpace(mcVersion) && mcVersion != target.McVersion &&
                    target.Loader == "vanilla" && instanceId.StartsWith(PackPrefix, StringComparison.Ordinal))
                {
                    if (!(await LoaderCatalog.MinecraftVersionsAsync()).Contains(mcVersion))
                        throw new InvalidOperationException($"Bilinmeyen Minecraft sürümü: {mcVersion}");
                    target = target with { McVersion = mcVersion };
                }

                if (!PackCatalog.RamOptionsGb.Contains(ramGb))
                    ramGb = AppSettings.Current.DefaultRamGb > 0 ? AppSettings.Current.DefaultRamGb : recommendedRamGb;

                var skins = await PrepareSkinsAsync();
                if (string.IsNullOrWhiteSpace(join)) join = null;
                else if (!System.Text.RegularExpressions.Regex.IsMatch(join, @"^[A-Za-z0-9](?:[A-Za-z0-9.\-]{0,118}[A-Za-z0-9])?(?::\d{1,5})?$"))
                    throw new InvalidOperationException("Sunucu adresi geçersiz.");

                await _game.LaunchAsync(target, _login.Username, ramGb, skins, join);
                StartPlayTime(instanceId);
                if (!_game.IsRunning) StopPlayTime();   // oyun hemen kapandıysa kapanış olayı sayaçtan önce gelmiş olabilir
                string? packId = instanceId.StartsWith(InstancePrefix, StringComparison.Ordinal) ? InstanceStore.Get(instanceId[InstancePrefix.Length..])?.SharedPackId : null;
                PresencePlaying(target, packId, join);
                _discord.ShowPlaying(target.Name, target.McVersion, target.Loader, await PackImageUrlAsync(instanceId), await ServerNameAsync(join), join != null);

                // Ayarlardaki "oyun açılınca" davranışı
                switch (AppSettings.Current.OnGameStart)
                {
                    case "minimize":
                        _host.Minimize();
                        break;
                    case "close":
                        _ = Task.Delay(1500).ContinueWith(_ => _host.Close());
                        break;
                }

                return new { started = true, error = (string?)null };
            }
            catch (Exception ex)
            {
                Log(ex.Message, "error");
                return new { started = false, error = ex.Message };
            }
        }

        /// <summary>
        /// Skin sistemi için oyun oturumu, API bilgisi ve authlib-injector hazırlanır. Biri olmazsa oyun eskisi gibi
        /// çevrimdışı ve varsayılan skinlerle açılır; skin sistemi oyunun açılmasını hiçbir zaman engellemez.
        /// </summary>
        private async Task<SkinSession?> PrepareSkinsAsync()
        {
            try
            {
                // Adres sunucunun cevabından değil, launcher'ın bildiği API adresinden kurulur
                string apiRoot = ApiConfig.BaseUrl.TrimEnd('/') + "/yggdrasil";
                var session = await _api.PostAsync<JsonElement>("/users/me/game-session", new { });
                string accessToken = session.GetProperty("accessToken").GetString() ?? "";
                string profileId = session.GetProperty("profileId").GetString() ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(profileId, "^[0-9a-f]{32}$") || accessToken.Length == 0)
                    throw new InvalidDataException("Sunucu geçersiz bir oyun oturumu döndürdü.");

                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                string metadata = await Downloader.Http.GetStringAsync(apiRoot, cancel.Token);

                string? agent = await AuthlibInjector.EnsureAsync(Log);
                if (agent == null) throw new InvalidOperationException("authlib-injector indirilemedi.");

                Log("Skinler etkin.", "muted");
                return new SkinSession(accessToken, profileId, apiRoot, metadata, agent);
            }
            catch (Exception ex)
            {
                Log("Skin sistemi şu an kullanılamıyor; oyun varsayılan skinlerle açılıyor. (" + ex.Message + ")", "warn");
                return null;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // ADMİN
        // ════════════════════════════════════════════════════════════════
        private void RequireAdmin()
        {
            RequireLogin();
            if (!_login.IsAdmin)
                throw new InvalidOperationException("Bu işlem için admin yetkisi gerekli.");
        }
    }
}
