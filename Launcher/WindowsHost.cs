using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using VuluLauncher.Native;
using VuluLauncher.ViewModels;
using VuluLauncher.Views;

namespace VuluLauncher
{
    /// <summary>Windows: Avalonia penceresi, bildirim alanı simgesi, DPAPI ve Gezgin.</summary>
    internal sealed class WindowsHost : IPlatformHost, IDisposable
    {
        private readonly ClassicDesktopStyleApplicationLifetime _lifetime;
        private readonly TrayNotifier _tray = new(Path.Combine(AppContext.BaseDirectory, "logo.ico"));
        private readonly ToastWindow _toast = new(Path.Combine(AppContext.BaseDirectory, "logo.ico"));

        private MainWindow? _window;
        private MainViewModel? _viewModel;
        // Pencere durumu başka iş parçacıklarından da okunur (Core'un arka plan denetimleri)
        private volatile bool _visible, _active;
        private IntPtr _hwnd;
        private int _exitRequested;
        // Bildirim alanındayken beş dakikada bir bellek boşaltılır
        private DispatcherTimer? _trimTimer;

        public WindowsHost(ClassicDesktopStyleApplicationLifetime lifetime)
        {
            _lifetime = lifetime;
            _tray.IconClicked += BringToFront;
            _tray.ExitRequested += Exit;
        }

        /// <summary>
        /// Pencere görünür oldu (true) ya da bildirim alanına gizlendi (false). Discord durumu buna uyar: launcher
        /// simge durumundayken (bilgisayar açılışında da) Discord'da görünmez.
        /// </summary>
        public event Action<bool>? WindowVisibilityChanged;

        /// <summary>Bildirime (Windows bildirimi ya da sağ alttaki kart) tıklanınca; LauncherApp oluşturulunca bağlanır.</summary>
        public event Action<string?>? NotificationClicked
        {
            add { _tray.Clicked += value; _toast.Clicked += value; }
            remove { _tray.Clicked -= value; _toast.Clicked -= value; }
        }

        /// <summary>
        /// Pencereyi kurar (arayüz iş parçacığında, ileti döngüsünden önce). Bilgisayar açılışında pencere gösterilmez:
        /// bildirim alanında beklenir, simgeye tıklanınca açılır.
        /// </summary>
        public void Start(bool startInTray)
        {
            _viewModel = new MainViewModel();
            _window = new MainWindow { DataContext = _viewModel };
            ApplyLayout(AppSettings.Current.WindowSize);
            _window.Opened += (_, _) =>
            {
                _hwnd = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                RoundCorners(_hwnd);
            };
            _window.Activated += (_, _) => _active = true;
            _window.Deactivated += (_, _) => _active = false;
            // Alt+F4 ya da görev çubuğundan kapatma, kapat düğmesiyle aynı yoldan gider (ayara göre bildirim alanı)
            _window.Closing += (_, e) =>
            {
                if (Volatile.Read(ref _exitRequested) == 1) return;
                e.Cancel = true;
                CoreClient.Instance.Send("window.close");
            };
            _ = _viewModel.StartAsync();

            if (startInTray)
            {
                _tray.ShowIcon();
                // Sağ altta kısa bir kart: launcher'ın arka planda çalıştığı belli olsun; tıklanınca pencere açılır
                _toast.Show("vulu Launcher çalışıyor", "Simge durumunda başladı; mesajlar ve bildirimler gelecek. Açmak için tıkla.", null);
                EnterTray();
            }
            else
            {
                _window.Show();
                _visible = true;
                WindowVisibilityChanged?.Invoke(true);
            }
        }

        private void EnterTray()
        {
            WindowVisibilityChanged?.Invoke(false);
            Efficiency.Enter();
            _trimTimer ??= new DispatcherTimer(TimeSpan.FromMinutes(5), DispatcherPriority.Background, (_, _) => Efficiency.Trim());
            _trimTimer.Start();
        }

        private void LeaveTray()
        {
            WindowVisibilityChanged?.Invoke(true);
            _trimTimer?.Stop();
            Efficiency.Leave();
        }

        public string Platform => "windows";
        public bool CanLaunchGame => true;
        public bool CanSelfUpdate => true;
        public bool IsDesktop => true;

        public void SendToUi(string json) => CoreClient.Instance.Receive(json);

        public bool IsForeground => _visible && _active;
        public bool IsWindowOpen => _visible;

        public void BringToFront() => Dispatcher.UIThread.Post(() =>
        {
            if (_window == null) return;
            if (!_visible) { LeaveTray(); _window.Show(); _visible = true; }
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
            if (_hwnd != IntPtr.Zero) TrayNotifier.BringToFront(_hwnd);
        });

        public void Minimize() => Dispatcher.UIThread.Post(() => { if (_window != null) _window.WindowState = WindowState.Minimized; });

        public (int Width, int Height)? ScreenWorkArea => Program.WorkArea();

        public bool CanHideToTray => true;

        /// <summary>Pencere gizlenir, bildirim alanında simge kalır; simgeye ya da bildirime tıklanınca geri gelir.</summary>
        public void HideToTray() => Dispatcher.UIThread.Post(() =>
        {
            if (_window == null || !_visible) return;
            _tray.ShowIcon();
            _window.Hide();
            _visible = false;
            _active = false;
            EnterTray();
        });

        /// <summary>Launcher'ı tamamen kapatır (simgenin "Çıkış"ı ya da ayarda kapat tuşu "Kapat" ise).</summary>
        private void Exit()
        {
            Interlocked.Exchange(ref _exitRequested, 1);
            Dispatcher.UIThread.Post(() =>
            {
                _window?.Close();
                _lifetime.Shutdown();
            });
        }

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunValue = "vulu Launcher";

        /// <summary>Kullanıcının başlangıç programlarına (HKCU\...\Run) "--tray" ile eklenir ya da çıkarılır.</summary>
        public void SetStartWithSystem(bool enabled)
        {
            // Geliştirme derlemesi başlangıç kaydını değiştirmez (kayıt dağıtılan launcher'ı göstermeli)
#if !DEBUG
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                if (enabled && Environment.ProcessPath is { } exe)
                    key.SetValue(RunValue, $"\"{exe}\" --tray");
                else if (key.GetValue(RunValue) != null)
                    key.DeleteValue(RunValue, throwOnMissingValue: false);
            }
            catch { /* kayıt yazılamazsa launcher yine çalışır */ }
#endif
        }

        public void ApplyWindowSize(string size) => Dispatcher.UIThread.Post(() => ApplyLayout(size));

        private void ApplyLayout(string size)
        {
            if (_window == null) return;
            var (width, height, scale) = Program.WindowLayoutDip(size);
            _window.ApplyLayout(width, height, scale);
            if (_visible && _window.Screens.ScreenFromWindow(_window) is { } screen)
            {
                var area = screen.WorkingArea;
                double dpi = screen.Scaling;
                _window.Position = new PixelPoint(area.X + (int)((area.Width - width * dpi) / 2), area.Y + (int)((area.Height - height * dpi) / 2));
            }
        }

        public void Close() => Exit();

        /// <summary>
        /// Masaüstünde sağ altta kart. Tam ekran oyunun üstünde de görünür (odağı çalmaz, oyunu küçültmez); Windows
        /// bildirimi tam ekranda gizlendiği için ona düşülmez.
        /// </summary>
        public void Notify(string title, string body, string? tag) => _toast.Show(title, body, tag);

        public void Notify(string title, string body, string? tag, string? imagePath) => _toast.Show(title, body, tag, imagePath);

        // Pencere sürükleme native başlık çubuğunda yapılır (MainWindow)
        public void BeginWindowDrag() { }
        public void MoveWindowDrag() { }

        public async Task<string?> PickFileAsync(string title, string filterName, string[] extensions)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (_window == null) return null;
                var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType(filterName) { Patterns = extensions.Select(e => "*." + e.TrimStart('.', '*')).ToArray() } },
                });
                return files.Count > 0 ? files[0].TryGetLocalPath() : null;
            });
        }

        public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        public string FileUrl(string path) => new Uri(path).AbsoluteUri;
        public void OpenFile(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        public void ShowInFolder(string path, bool selectFile)
        {
            var start = new ProcessStartInfo("explorer.exe");
            if (selectFile) start.ArgumentList.Add("/select,");
            start.ArgumentList.Add(path);
            Process.Start(start);
        }

        public bool CopyFileToClipboard(string path) => FileClipboard.SetFiles(new[] { path });
        public bool MoveToTrash(string path) => FileClipboard.Recycle(path);

        public string PicturesDirectory => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        /// <summary>Kullanıcının İndirilenler klasörü; başka bir yere taşınmışsa da doğru yeri verir.</summary>
        public string DownloadsDirectory
        {
            get
            {
                try
                {
                    if (SHGetKnownFolderPath(FolderDownloads, 0, IntPtr.Zero, out var pointer) == 0)
                    {
                        try { if (Marshal.PtrToStringUni(pointer) is { Length: > 0 } path) return path; }
                        finally { Marshal.FreeCoTaskMem(pointer); }
                    }
                }
                catch { }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }

        private static readonly Guid FolderDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        public byte[] Protect(byte[] data) => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);

        public string CreateShortcut(string name, string arguments, string description, string? iconPath)
        {
            string target = Environment.ProcessPath ?? Path.Combine(AppPaths.Base, VuluShared.Releases.LauncherExeName);
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name + ".lnk");
            VuluShared.InstallInfo.CreateShortcut(path, target, description, arguments, iconPath);
            return path;
        }

        /// <summary>Updater'ı açar ve launcher'ı kapatır; Updater kapanmayı bekleyip kurar ve launcher'ı yeniden açar.</summary>
        public void StartLauncherUpdate()
        {
            string updaterPath = Path.Combine(AppPaths.Base, VuluShared.Releases.UpdaterExeName);
            if (!File.Exists(updaterPath))
                throw new FileNotFoundException("Updater.exe bulunamadı.");

            var startInfo = new ProcessStartInfo(updaterPath) { UseShellExecute = false, WorkingDirectory = AppPaths.Base };
            startInfo.ArgumentList.Add("--wait-pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());

            Process.Start(startInfo);
            Exit();
        }

        // Windows'u Updater günceller (StartLauncherUpdate); paket kurdurma yalnızca Android'de
        public Task<PackageInstallResult> InstallPackageAsync(string path) => throw new NotSupportedException();
        public void OpenInstallPermissionSettings() { }
        public bool IsMeteredConnection => false;
        public IGameRunner? GameRunner => null;   // oyun java.exe süreci olarak açılır (GameLauncher)

        public void Dispose()
        {
            _tray.Dispose();
            _toast.Dispose();
        }

        /// <summary>Windows 11'de çerçevesiz pencerenin köşelerini yuvarlatır; eski sürümlerde etkisizdir.</summary>
        private static void RoundCorners(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int preference = 2; // DWMWCP_ROUND
                _ = DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
            }
            catch { }
        }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
