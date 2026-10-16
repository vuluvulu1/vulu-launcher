using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using MongoDB.Driver;
using Photino.NET;

namespace VuluLauncher
{
    /// <summary>
    /// Arayüzün çağırdığı komutların tamamı. Arayüz yalnızca görüntüler ve sorar;
    /// giriş, izin kontrolü, güncelleme ve oyun başlatma kararları burada verilir.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private readonly Bridge _bridge;
        private readonly PhotinoWindow _window;

        private LoginService _login = new();
        private readonly ApiClient _api;
        private readonly LauncherUpdateService _launcherUpdates = new();
        private readonly JavaManager _java;
        private readonly ModrinthService _modrinth;
        private readonly GameLauncher _game;

        private VuluShared.ReleaseManifest? _latestLauncher;
        private AdminService? _admin;

        // Pencere sürükleme: başlangıçtaki pencere ve imleç konumu
        private Point _dragWindowStart;
        private POINT _dragCursorStart;

        public LauncherApp(Bridge bridge, PhotinoWindow window)
        {
            _bridge = bridge;
            _window = window;
            _api = new ApiClient(() => _login.Token);
            _java = new JavaManager(Log);
            _modrinth = new ModrinthService(Log);
            _game = new GameLauncher(Log, _java);
            _game.Exited += () => _bridge.Emit("gameExited");
            DownloadJob.Emit = snapshot => _bridge.Emit("download", snapshot);
        }

        public void RegisterCommands()
        {
            // ── Pencere ───────────────────────────────────────────────────
            _bridge.OnInline("window.dragStart", _ =>
            {
                _dragWindowStart = _window.Location;
                GetCursorPos(out _dragCursorStart);
            });
            _bridge.OnInline("window.dragMove", _ =>
            {
                if (!GetCursorPos(out var cursor)) return;
                _window.Location = new Point(
                    _dragWindowStart.X + cursor.X - _dragCursorStart.X,
                    _dragWindowStart.Y + cursor.Y - _dragCursorStart.Y);
            });
            _bridge.OnInline("window.minimize", _ => _window.SetMinimized(true));
            _bridge.OnInline("window.close", _ => _window.Close());

            // ── Uygulama ──────────────────────────────────────────────────
            _bridge.On("app.init", _ => Task.FromResult<object?>(Init()));
            _bridge.On("app.wake", async _ => await WakeServerAsync());

            _bridge.On("auth.login", async args => await LoginAsync(args));

            _bridge.On("launcher.check", async _ => await CheckLauncherVersionAsync());
            _bridge.On("launcher.update", _ => Task.FromResult<object?>(StartLauncherUpdate()));

            _bridge.On("game.play", async args => await PlayAsync(args.Str("instanceId"), args.Int("ramGb"), args.Str("mcVersion")));
            _bridge.On("game.versions", async _ => { RequireLogin(); return await LoaderCatalog.MinecraftVersionsAsync(); });

            RegisterInstanceCommands();
            RegisterPackCommands();
            RegisterProfileCommands();

            // ── Admin ─────────────────────────────────────────────────────
            _bridge.On("admin.connect", async args => await AdminConnectAsync(args.Str("username"), args.Str("password")));
            _bridge.On("admin.users", async _ => await AdminUsersAsync());
            _bridge.On("admin.saveUser", async args => await AdminSaveUserAsync(args));
            _bridge.On("admin.deleteUser", async args => await AdminDeleteUserAsync(args.Str("id")));
        }

        public void Shutdown()
        {
            _bridge.Shutdown();
        }

        private void Log(string text, string level) =>
            _bridge.Log(text, level == "replace" ? "accent" : level, replaceLast: level == "replace");

        // ════════════════════════════════════════════════════════════════
        // UYGULAMA / GİRİŞ
        // ════════════════════════════════════════════════════════════════
        private object Init()
        {
            var (username, password) = CredentialManager.Load();
            return new
            {
                version = LauncherUpdateService.LocalVersion,
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

        /// <summary>Updater'ı açar ve launcher'ı kapatır; Updater kapanmayı bekleyip kurar ve launcher'ı yeniden açar.</summary>
        private object StartLauncherUpdate()
        {
            if (_latestLauncher == null)
                throw new InvalidOperationException("Bekleyen launcher güncellemesi yok.");

            string updaterPath = Path.Combine(AppPaths.Base, VuluShared.Releases.UpdaterExeName);
            if (!File.Exists(updaterPath))
                throw new FileNotFoundException("Updater.exe bulunamadı.");

            var startInfo = new ProcessStartInfo(updaterPath) { UseShellExecute = false, WorkingDirectory = AppPaths.Base };
            startInfo.ArgumentList.Add("--wait-pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());

            Process.Start(startInfo);
            _window.Close();
            return new { started = true };
        }

        // ════════════════════════════════════════════════════════════════
        // OYUN
        // ════════════════════════════════════════════════════════════════
        private async Task<object?> PlayAsync(string instanceId, int ramGb, string mcVersion)
        {
            RequireLogin();

            try
            {
                var (target, recommendedRamGb) = ResolveTarget(instanceId);

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

                await _game.LaunchAsync(target, _login.Username, ramGb);

                // Ayarlardaki "oyun açılınca" davranışı
                switch (AppSettings.Current.OnGameStart)
                {
                    case "minimize":
                        _window.SetMinimized(true);
                        break;
                    case "close":
                        _ = Task.Delay(1500).ContinueWith(_ => _window.Close());
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

        // ════════════════════════════════════════════════════════════════
        // ADMİN
        // ════════════════════════════════════════════════════════════════
        private void RequireAdmin()
        {
            RequireLogin();
            if (!_login.IsAdmin)
                throw new InvalidOperationException("Bu işlem için admin yetkisi gerekli.");
        }

        private AdminService RequireAdminConnection()
        {
            RequireAdmin();
            return _admin ?? throw new InvalidOperationException("Önce veritabanına bağlanın.");
        }

        private async Task<object?> AdminConnectAsync(string username, string password)
        {
            RequireAdmin();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                return new { success = false, error = "Boş bırakılamaz." };

            try
            {
                var service = new AdminService(username.Trim(), password);
                await service.TestConnectionAsync();
                // Eski paket izinleri veritabanından temizlenir (bir kez iş yapar, sonra eşleşen kayıt kalmaz)
                try { await service.RemoveLegacyFieldsAsync(); } catch { /* yazma izni yoksa kayıtlar olduğu gibi kalır */ }
                _admin = service;
                return new { success = true, error = (string?)null };
            }
            catch
            {
                return new { success = false, error = "Bağlantı başarısız." };
            }
        }

        private async Task<object?> AdminUsersAsync()
        {
            var users = await RequireAdminConnection().GetUsersAsync();
            // Şifre hash'leri arayüze gönderilmez
            return users.Select(u => new
            {
                id = u.Id,
                username = u.Username,
                isActive = u.IsActive,
                isAdmin = u.IsAdmin
            });
        }

        private async Task<object?> AdminSaveUserAsync(JsonElement args)
        {
            var admin = RequireAdminConnection();

            string id = args.Str("id");
            string username = args.Str("username").Trim();
            string password = args.Str("password");
            bool isAdmin = args.Bool("isAdmin");
            bool isActive = args.Bool("isActive");

            if (string.IsNullOrEmpty(username))
                return new { success = false, error = "Kullanıcı adı zorunlu." };

            if (string.IsNullOrEmpty(id))
            {
                if (string.IsNullOrEmpty(password))
                    return new { success = false, error = "Yeni kullanıcı için şifre zorunlu." };

                await admin.AddUserAsync(new AdminUser
                {
                    Username = username,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                    IsAdmin = isAdmin,
                    IsActive = isActive
                });
            }
            else
            {
                var update = Builders<AdminUser>.Update
                    .Set(u => u.Username, username)
                    .Set(u => u.IsActive, isActive)
                    .Set(u => u.IsAdmin, isAdmin);

                // Şifre boş bırakıldıysa değişmez
                if (!string.IsNullOrEmpty(password))
                    update = update.Set(u => u.PasswordHash, BCrypt.Net.BCrypt.HashPassword(password));

                await admin.UpdateUserAsync(id, update);
            }

            return new { success = true, error = (string?)null };
        }

        private async Task<object?> AdminDeleteUserAsync(string id)
        {
            await RequireAdminConnection().DeleteUserAsync(id);
            return new { success = true };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    }
}
