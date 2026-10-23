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
        private readonly PhotinoWindow _window;
        private readonly TrayNotifier _tray = new(Path.Combine(AppContext.BaseDirectory, "logo.ico"));

        // Pencere sürükleme: başlangıçtaki pencere ve imleç konumu
        private Point _dragWindowStart;
        private POINT _dragCursorStart;

        public WindowsHost(PhotinoWindow window)
        {
            _window = window;
            _tray.IconClicked += BringToFront;
        }

        /// <summary>Bildirime tıklanınca; LauncherApp oluşturulunca bağlanır.</summary>
        public event Action<string?>? NotificationClicked
        {
            add => _tray.Clicked += value;
            remove => _tray.Clicked -= value;
        }

        public string Platform => "windows";
        public bool CanLaunchGame => true;
        public bool CanSelfUpdate => true;
        public bool IsDesktop => true;

        public void SendToUi(string json) => _window.SendWebMessage(json);

        public bool IsForeground => TrayNotifier.IsActive(_window.WindowHandle);
        public void BringToFront() => TrayNotifier.BringToFront(_window.WindowHandle);
        public void Minimize() => _window.SetMinimized(true);

        public void ApplyWindowScale(int scale)
        {
            var (width, height, zoom) = Program.WindowLayout(scale);
            _window.Invoke(() =>
            {
                _window.SetSize(width, height);
                _window.Zoom = zoom;
                _window.Center();
            });
        }
        public void Close() => _window.Close();

        public void BeginWindowDrag()
        {
            _dragWindowStart = _window.Location;
            GetCursorPos(out _dragCursorStart);
        }

        public void MoveWindowDrag()
        {
            if (!GetCursorPos(out var cursor)) return;
            _window.Location = new Point(
                _dragWindowStart.X + cursor.X - _dragCursorStart.X,
                _dragWindowStart.Y + cursor.Y - _dragCursorStart.Y);
        }

        public void Notify(string title, string body, string? tag) => _tray.Show(title, body, tag);

        public Task<string?> PickFileAsync(string title, string filterName, string[] extensions)
        {
            var files = _window.ShowOpenFile(title, null, false, new[] { (filterName, extensions) });
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
            _window.Close();
        }

        // Windows'u Updater günceller (StartLauncherUpdate); paket kurdurma yalnızca Android'de
        public Task<PackageInstallResult> InstallPackageAsync(string path) => throw new NotSupportedException();
        public void OpenInstallPermissionSettings() { }
        public bool IsMeteredConnection => false;
        public IGameRunner? GameRunner => null;   // oyun java.exe süreci olarak açılır (GameLauncher)

        public void Dispose() => _tray.Dispose();

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    }
}
