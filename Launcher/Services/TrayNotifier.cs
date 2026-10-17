using System.Runtime.InteropServices;

namespace VuluLauncher
{
    /// <summary>
    /// Windows bildirimleri (Windows 10/11'de bildirim merkezine düşer). Ek paket gerektirmesin diye bildirim alanı
    /// simgesi üzerinden gösterilir: simge ilk bildirimde eklenir, launcher kapanınca kaldırılır.
    /// Bildirime tıklanınca <see cref="Clicked"/> o bildirimin etiketiyle çağrılır; simgeye tıklamak pencereyi öne getirir.
    /// </summary>
    internal sealed class TrayNotifier : IDisposable
    {
        private const int WM_APP_TRAY = 0x8000 + 1;
        private const int WM_CLOSE = 0x0010;
        private const int WM_LBUTTONUP = 0x0202;
        private const int NIN_SELECT = 0x0400;
        private const int NIN_BALLOONUSERCLICK = 0x0405;
        private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
        private const int NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
        private const int NIIF_USER = 0x4, NIIF_LARGE_ICON = 0x20, NIIF_RESPECT_QUIET_TIME = 0x80;

        private readonly string _iconPath;
        private readonly object _gate = new();
        private Thread? _thread;
        private IntPtr _hwnd, _icon;
        private bool _added, _disposed;
        private string? _lastTag;
        private WndProc? _proc;   // GC toplamasın

        /// <summary>Bildirime tıklandı (etiket, ör. "dm:Ali").</summary>
        public event Action<string?>? Clicked;
        /// <summary>Bildirim alanı simgesine tıklandı.</summary>
        public event Action? IconClicked;

        public TrayNotifier(string iconPath) => _iconPath = iconPath;

        /// <summary>Bildirim gösterir. Odak yardımcısı/sessiz saatler açıksa Windows kendisi susturur.</summary>
        public void Show(string title, string text, string? tag)
        {
            try
            {
                if (!EnsureWindow()) return;
                lock (_gate)
                {
                    if (_disposed) return;
                    _lastTag = tag;
                    // Gezgin yeniden başlarsa simge kaybolur: bildirim gösterilemezse simge yeniden eklenip bir kez daha denenir
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        if (!_added && !AddIcon()) return;
                        var data = NewData();
                        data.uFlags = NIF_INFO;
                        data.szInfoTitle = Clip(title, 63);
                        data.szInfo = Clip(text, 255);
                        data.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON | NIIF_RESPECT_QUIET_TIME;
                        data.hBalloonIcon = _icon;
                        if (Shell_NotifyIcon(NIM_MODIFY, ref data)) return;
                        _added = false;
                    }
                }
            }
            catch { /* bildirim gösterilemezse launcher etkilenmez */ }
        }

        private bool AddIcon()
        {
            var data = NewData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            Shell_NotifyIcon(NIM_DELETE, ref data);   // kalıntı varsa
            if (!Shell_NotifyIcon(NIM_ADD, ref data)) return false;
            data.uTimeoutOrVersion = 4;   // NOTIFYICON_VERSION_4: tıklama olayları lParam'ın alt kelimesinde
            Shell_NotifyIcon(NIM_SETVERSION, ref data);
            _added = true;
            return true;
        }

        private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

        private NOTIFYICONDATA NewData() => new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = _icon,
            szTip = "vulu Launcher",
            szInfo = "", szInfoTitle = "",
        };

        /// <summary>Simgenin mesajlarını alan görünmez pencere kendi iş parçacığında çalışır.</summary>
        private bool EnsureWindow()
        {
            lock (_gate)
            {
                if (_disposed) return false;
                if (_thread != null) return _hwnd != IntPtr.Zero;
                using var ready = new ManualResetEventSlim();
                _thread = new Thread(() => MessageLoop(ready)) { IsBackground = true, Name = "vulu tray" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                ready.Wait(TimeSpan.FromSeconds(5));
                return _hwnd != IntPtr.Zero;
            }
        }

        private void MessageLoop(ManualResetEventSlim ready)
        {
            try
            {
                _icon = LoadImage(IntPtr.Zero, _iconPath, 1 /* IMAGE_ICON */, 0, 0, 0x10 /* LR_LOADFROMFILE */ | 0x40 /* LR_DEFAULTSIZE */);
                _proc = Proc;
                var cls = new WNDCLASS { lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc), lpszClassName = "vuluLauncherTray", hInstance = GetModuleHandle(null) };
                RegisterClass(ref cls);
                _hwnd = CreateWindowEx(0, cls.lpszClassName, "vulu tray", 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
            }
            catch { _hwnd = IntPtr.Zero; }
            finally { ready.Set(); }
            if (_hwnd == IntPtr.Zero) return;

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private IntPtr Proc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_APP_TRAY)
            {
                int ev = (int)(lParam.ToInt64() & 0xFFFF);
                try
                {
                    if (ev == NIN_BALLOONUSERCLICK) Clicked?.Invoke(_lastTag);
                    else if (ev is NIN_SELECT or WM_LBUTTONUP) IconClicked?.Invoke();
                }
                catch { }
                return IntPtr.Zero;
            }
            if (msg == WM_CLOSE) { DestroyWindow(hwnd); PostQuitMessage(0); return IntPtr.Zero; }
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                try
                {
                    if (_added) { var data = NewData(); Shell_NotifyIcon(NIM_DELETE, ref data); }
                    if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }
            }
        }

        /// <summary>Pencereyi küçültülmüşse geri açıp öne getirir.</summary>
        public static void BringToFront(IntPtr window)
        {
            if (window == IntPtr.Zero) return;
            if (IsIconic(window)) ShowWindow(window, 9 /* SW_RESTORE */);
            SetForegroundWindow(window);
        }

        /// <summary>Kullanıcı şu an bu pencereye bakıyor mu (önde ve küçültülmemiş)?</summary>
        public static bool IsActive(IntPtr window) => window != IntPtr.Zero && GetForegroundWindow() == window && !IsIconic(window);

        // ── Win32 ─────────────────────────────────────────────────────────
        private delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam, lParam; public int time; public int x, y; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WNDCLASS cls);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string name, int type, int cx, int cy, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    }
}
