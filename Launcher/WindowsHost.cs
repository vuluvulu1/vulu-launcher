using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Photino.NET;

namespace VuluLauncher
{
    /// <summary>Windows: Photino penceresi, bildirim alanı simgesi, DPAPI ve Gezgin.</summary>
    internal sealed class WindowsHost : IPlatformHost, IDisposable
    {
        private readonly Func<PhotinoWindow> _createWindow;
        private readonly string _page;
        private readonly TrayNotifier _tray = new(Path.Combine(AppContext.BaseDirectory, "logo.ico"));
        private readonly ToastWindow _toast = new(Path.Combine(AppContext.BaseDirectory, "logo.ico"));

        // Açık pencere; launcher bildirim alanındayken null (pencere ve tarayıcı tamamen kapatılır)
        private volatile PhotinoWindow? _window;
        private int _hideRequested, _exitRequested;
        private readonly AutoResetEvent _wake = new(false);

        // Pencere sürükleme: başlangıçtaki pencere ve imleç konumu
        private Point _dragWindowStart;
        private POINT _dragCursorStart;

        public WindowsHost(Func<PhotinoWindow> createWindow, string page)
        {
            _createWindow = createWindow;
            _page = page;
            _tray.IconClicked += BringToFront;
            _tray.ExitRequested += Exit;
        }

        /// <summary>Bildirime (Windows bildirimi ya da sağ alttaki kart) tıklanınca; LauncherApp oluşturulunca bağlanır.</summary>
        public event Action<string?>? NotificationClicked
        {
            add { _tray.Clicked += value; _toast.Clicked += value; }
            remove { _tray.Clicked -= value; _toast.Clicked -= value; }
        }

        /// <summary>
        /// Ana döngü (ana iş parçacığında): pencereyi açar ve kapanmasını bekler. Kapat tuşu bildirim alanına
        /// küçültüyorsa pencere ve onu çizen WebView2 süreçleri tamamen kapatılır; geriye yalnızca bu süreç (mesaj ve
        /// bildirim denetimleri) ve simge kalır. Simgeye, bildirime tıklanınca ya da launcher yeniden açılınca pencere
        /// baştan kurulur; arayüz oturumu C# tarafından alıp kaldığı yerden sürer.
        /// </summary>
        public void Run(Action<PhotinoWindow> attach)
        {
            while (true)
            {
                // Photino ileti döngüsünü yalnızca ilk pencerede çalıştırır (statik bayrak); yeniden kurulan pencerede
                // de çalışsın diye bayrak sıfırlanır
                MessageLoopStarted?.SetValue(null, false);
                var window = _createWindow();
                attach(window);
                Interlocked.Exchange(ref _hideRequested, 0);
                _window = window;
                window.Load(_page);
                window.WaitForClose();
                _window = null;

                if (Volatile.Read(ref _exitRequested) == 1 || Volatile.Read(ref _hideRequested) == 0) return;
                if (!WaitInTray()) return;
            }
        }

        /// <summary>Bildirim alanında bekler: verimlilik modu, beş dakikada bir bellek boşaltma. Açılacaksa true.</summary>
        private bool WaitInTray()
        {
            Efficiency.Enter();
            try
            {
                while (true)
                {
                    bool woke = _wake.WaitOne(TimeSpan.FromMinutes(5));
                    if (Volatile.Read(ref _exitRequested) == 1) return false;
                    if (woke) return true;
                    Efficiency.Trim();
                }
            }
            finally { Efficiency.Leave(); }
        }

        public string Platform => "windows";
        public bool CanLaunchGame => true;
        public bool CanSelfUpdate => true;
        public bool IsDesktop => true;

        public void SendToUi(string json)
        {
            // Pencere yokken (bildirim alanında) arayüze giden olaylar düşer; arayüz açılınca durumu baştan alır
            try { _window?.SendWebMessage(json); } catch { }
        }

        public bool IsForeground => _window is { } w && TrayNotifier.IsActive(w.WindowHandle);
        public bool IsWindowOpen => _window != null;

        public void BringToFront()
        {
            if (_window is { } w) TrayNotifier.BringToFront(w.WindowHandle);
            else _wake.Set();   // bildirim alanındaysa pencere yeniden kurulur
        }

        public void Minimize() => _window?.SetMinimized(true);

        public (int Width, int Height)? ScreenWorkArea => Program.WorkArea();

        /// <summary>
        /// Photino'nun "ileti döngüsü başladı" bayrağı. Bulunamazsa (Photino sürümü değişti) pencere yeniden
        /// kurulamaz: bildirim alanına küçültme kapanır, kapat tuşu launcher'ı kapatır.
        /// </summary>
        private static readonly System.Reflection.FieldInfo? MessageLoopStarted =
            typeof(PhotinoWindow).GetField("_messageLoopIsStarted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        public bool CanHideToTray => MessageLoopStarted?.FieldType == typeof(bool);

        public void HideToTray()
        {
            if (_window is not { } w) return;
            _tray.ShowIcon();
            Interlocked.Exchange(ref _hideRequested, 1);
            w.Invoke(w.Close);
        }

        /// <summary>Launcher'ı tamamen kapatır (simgenin "Çıkış"ı ya da ayarda kapat tuşu "Kapat" ise).</summary>
        private void Exit()
        {
            Interlocked.Exchange(ref _exitRequested, 1);
            if (_window is { } w) w.Invoke(w.Close);
            else _wake.Set();
        }

        public void ApplyWindowSize(string size)
        {
            if (_window is not { } w) return;
            var (width, height, zoom) = Program.WindowLayout(size);
            w.Invoke(() =>
            {
                w.SetSize(width, height);
                w.Zoom = zoom;
                w.Center();
            });
        }

        public void Close() => Exit();

        /// <summary>
        /// Masaüstünde sağ altta kart. Tam ekran oyunun üstünde de görünür (odağı çalmaz, oyunu küçültmez); Windows
        /// bildirimi tam ekranda gizlendiği için ona düşülmez.
        /// </summary>
        public void Notify(string title, string body, string? tag) => _toast.Show(title, body, tag);

        public void Notify(string title, string body, string? tag, string? imagePath) => _toast.Show(title, body, tag, imagePath);

        public void BeginWindowDrag()
        {
            if (_window is not { } w) return;
            _dragWindowStart = w.Location;
            GetCursorPos(out _dragCursorStart);
        }

        public void MoveWindowDrag()
        {
            if (_window is not { } w || !GetCursorPos(out var cursor)) return;
            w.Location = new Point(
                _dragWindowStart.X + cursor.X - _dragCursorStart.X,
                _dragWindowStart.Y + cursor.Y - _dragCursorStart.Y);
        }

        public Task<string?> PickFileAsync(string title, string filterName, string[] extensions)
        {
            var files = _window?.ShowOpenFile(title, null, false, new[] { (filterName, extensions) });
            return Task.FromResult(files is { Length: > 0 } ? files[0] : null);
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
            _wake.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    }
}
