using System.Runtime.InteropServices;

namespace VuluLauncher
{
    /// <summary>
    /// Steam'deki gibi ekranın sağ altında beliren küçük bildirim kartı. Tarayıcı kullanmaz: düz Win32 penceresi GDI
    /// ile çizilir (birkaç yüz KB bellek). Kart odağı çalmaz, en fazla üç tanesi üst üste durur, 6 sn sonra kaybolur.
    /// Tıklanınca <see cref="Clicked"/> bildirimin etiketiyle çağrılır.
    ///
    /// Bütün pencereler tek bir iş parçacığında, ilk bildirimde açılan mesaj döngüsünde yaşar. Fontlar ve fırçalar bir
    /// kez oluşturulur; kart kapanınca penceresi yok edilir.
    /// </summary>
    internal sealed class ToastWindow : IDisposable
    {
        private const int Width = 320, Height = 78, Gap = 10, Margin = 16, MaxCards = 3;
        private const int ShowMs = 6000, FadeStepMs = 15, FadeSteps = 12;
        private const int WM_APP_SHOW = 0x8000 + 2, WM_PAINT = 0x000F, WM_TIMER = 0x0113, WM_LBUTTONUP = 0x0202,
            WM_CLOSE = 0x0010, WM_DESTROY = 0x0002, WM_MOUSEACTIVATE = 0x0021, WM_SETCURSOR = 0x0020;
        private const int TimerFadeIn = 1, TimerHide = 2, TimerFadeOut = 3;

        private readonly string _iconPath;
        private readonly object _gate = new();
        private readonly Queue<(string Title, string Body, string? Tag)> _pending = new();
        private readonly List<Card> _cards = new();
        private Thread? _thread;
        private uint _threadId;
        private bool _disposed;
        private WndProc? _proc;   // GC toplamasın
        private IntPtr _instance, _icon, _fontTitle, _fontBody, _fontLetter, _brushBack, _penBorder, _cursor;
        private double _scale = 1;

        private sealed class Card
        {
            public IntPtr Hwnd;
            public string Title = "", Body = "";
            public string? Tag;
            public int Alpha;
            public bool Closing;
            public uint Accent;
        }

        /// <summary>Karta tıklandı (etiket, ör. "dm:Ali").</summary>
        public event Action<string?>? Clicked;

        public ToastWindow(string iconPath) => _iconPath = iconPath;

        public void Show(string title, string body, string? tag)
        {
            lock (_gate)
            {
                if (_disposed) return;
                _pending.Enqueue((title, body, tag));
                if (_thread == null)
                {
                    using var ready = new ManualResetEventSlim();
                    _thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "vulu toast" };
                    _thread.SetApartmentState(ApartmentState.STA);
                    _thread.Start();
                    ready.Wait(TimeSpan.FromSeconds(5));
                }
                if (_threadId != 0) PostThreadMessage(_threadId, WM_APP_SHOW, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private void Loop(ManualResetEventSlim ready)
        {
            try
            {
                _threadId = GetCurrentThreadId();
                _instance = GetModuleHandle(null);
                _scale = Math.Max(1, GetDpiForSystem() / 96.0);
                _proc = Proc;
                var cls = new WNDCLASS
                {
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc), lpszClassName = "vuluLauncherToast",
                    hInstance = _instance, hCursor = _cursor = LoadCursor(IntPtr.Zero, 32649 /* IDC_HAND */),
                };
                RegisterClass(ref cls);
                _icon = LoadImage(IntPtr.Zero, _iconPath, 1 /* IMAGE_ICON */, S(40), S(40), 0x10 /* LR_LOADFROMFILE */);
                _fontTitle = Font(14, 600);
                _fontBody = Font(13, 400);
                _fontLetter = Font(18, 700);
                _brushBack = CreateSolidBrush(Rgb(0x18, 0x15, 0x13));
                _penBorder = CreatePen(0, 1, Rgb(0x3a, 0x34, 0x2f));
                // Mesaj kuyruğu oluşsun diye bir kez PeekMessage
                PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            }
            finally { ready.Set(); }

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.hwnd == IntPtr.Zero && msg.message == WM_APP_SHOW) { ShowPending(); continue; }
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private int S(int value) => (int)Math.Round(value * _scale);

        private IntPtr Font(int px, int weight) =>
            CreateFont(-S(px), 0, 0, 0, weight, 0, 0, 0, 1 /* DEFAULT_CHARSET */, 0, 0, 5 /* CLEARTYPE_QUALITY */, 0, "Segoe UI");

        private static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

        private void ShowPending()
        {
            while (true)
            {
                (string Title, string Body, string? Tag) next;
                lock (_gate)
                {
                    if (_disposed || _pending.Count == 0) return;
                    next = _pending.Dequeue();
                }
                // En eskisi gider (en fazla üç kart)
                while (_cards.Count(c => !c.Closing) >= MaxCards) BeginClose(_cards.First(c => !c.Closing));

                var card = new Card { Title = next.Title, Body = next.Body, Tag = next.Tag, Accent = AccentFor(next.Tag) };
                card.Hwnd = CreateWindowEx(
                    0x8 /* WS_EX_TOPMOST */ | 0x80 /* WS_EX_TOOLWINDOW */ | 0x8000000 /* WS_EX_NOACTIVATE */ | 0x80000 /* WS_EX_LAYERED */,
                    "vuluLauncherToast", next.Title, unchecked((int)0x80000000) /* WS_POPUP */, 0, 0, S(Width), S(Height),
                    IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
                if (card.Hwnd == IntPtr.Zero) continue;
                IntPtr region = CreateRoundRectRgn(0, 0, S(Width) + 1, S(Height) + 1, S(12), S(12));
                SetWindowRgn(card.Hwnd, region, false);   // bölge artık pencereye ait, silinmez
                SetLayeredWindowAttributes(card.Hwnd, 0, 0, 0x2 /* LWA_ALPHA */);
                _cards.Add(card);
                Layout();
                ShowWindow(card.Hwnd, 4 /* SW_SHOWNOACTIVATE */);
                SetTimer(card.Hwnd, TimerFadeIn, FadeStepMs, IntPtr.Zero);
                SetTimer(card.Hwnd, TimerHide, ShowMs, IntPtr.Zero);
            }
        }

        /// <summary>Kartlar sağ altta, en yenisi en altta olacak şekilde dizilir.</summary>
        private void Layout()
        {
            var area = new RECT();
            SystemParametersInfo(0x0030 /* SPI_GETWORKAREA */, 0, ref area, 0);
            int x = area.Right - S(Width) - S(Margin), y = area.Bottom - S(Margin);
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                y -= S(Height);
                SetWindowPos(_cards[i].Hwnd, new IntPtr(-1) /* HWND_TOPMOST */, x, y, 0, 0, 0x1 /* SWP_NOSIZE */ | 0x10 /* SWP_NOACTIVATE */);
                y -= S(Gap);
            }
        }

        /// <summary>Oyuna giriş yeşil, diğerleri launcher'ın vurgu rengi (turuncu).</summary>
        private static uint AccentFor(string? tag) =>
            tag != null && tag.StartsWith("profile:", StringComparison.Ordinal) ? Rgb(0x7c, 0xc4, 0x7f) : Rgb(0xc2, 0x7e, 0x3e);

        /// <summary>Etiketten kişi adı (dm:, friend:, profile:); varsa kartta onun baş harfi görünür.</summary>
        private static string? PersonOf(string? tag)
        {
            if (tag == null) return null;
            foreach (var prefix in new[] { "dm:", "friend:", "profile:" })
                if (tag.StartsWith(prefix, StringComparison.Ordinal) && tag.Length > prefix.Length) return tag[prefix.Length..];
            return null;
        }

        private void BeginClose(Card card)
        {
            if (card.Closing) return;
            card.Closing = true;
            KillTimer(card.Hwnd, TimerFadeIn);
            KillTimer(card.Hwnd, TimerHide);
            SetTimer(card.Hwnd, TimerFadeOut, FadeStepMs, IntPtr.Zero);
        }

        private IntPtr Proc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            var card = _cards.FirstOrDefault(c => c.Hwnd == hwnd);
            switch (msg)
            {
                case WM_MOUSEACTIVATE:
                    return new IntPtr(3);   // MA_NOACTIVATE: tıklanınca da odağı (oyunu) çalmaz
                case WM_SETCURSOR:
                    SetCursor(_cursor);
                    return new IntPtr(1);
                case WM_PAINT when card != null:
                    Paint(hwnd, card);
                    return IntPtr.Zero;
                case WM_TIMER when card != null:
                    int id = (int)wParam;
                    if (id == TimerHide) BeginClose(card);
                    else if (id == TimerFadeIn)
                    {
                        card.Alpha = Math.Min(245, card.Alpha + 245 / FadeSteps + 1);
                        SetLayeredWindowAttributes(hwnd, 0, (byte)card.Alpha, 0x2);
                        if (card.Alpha >= 245) KillTimer(hwnd, TimerFadeIn);
                    }
                    else if (id == TimerFadeOut)
                    {
                        card.Alpha -= 245 / FadeSteps + 1;
                        if (card.Alpha <= 0) DestroyWindow(hwnd);
                        else SetLayeredWindowAttributes(hwnd, 0, (byte)card.Alpha, 0x2);
                    }
                    return IntPtr.Zero;
                case WM_LBUTTONUP when card != null && !card.Closing:
                    try { Clicked?.Invoke(card.Tag); } catch { }
                    BeginClose(card);
                    return IntPtr.Zero;
                case WM_DESTROY when card != null:
                    KillTimer(hwnd, TimerFadeIn); KillTimer(hwnd, TimerHide); KillTimer(hwnd, TimerFadeOut);
                    _cards.Remove(card);
                    Layout();
                    return IntPtr.Zero;
                case WM_CLOSE:
                    if (card != null) BeginClose(card); else DestroyWindow(hwnd);
                    return IntPtr.Zero;
            }
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private void Paint(IntPtr hwnd, Card card)
        {
            IntPtr dc = BeginPaint(hwnd, out var ps);
            try
            {
                int w = S(Width), h = S(Height);
                var all = new RECT { Left = 0, Top = 0, Right = w, Bottom = h };
                FillRect(dc, ref all, _brushBack);

                // İnce çerçeve ve soldaki renkli şerit
                IntPtr oldPen = SelectObject(dc, _penBorder);
                IntPtr oldBrush = SelectObject(dc, GetStockObject(5 /* NULL_BRUSH */));
                RoundRect(dc, 0, 0, w, h, S(12), S(12));
                IntPtr accent = CreateSolidBrush(card.Accent);
                var stripe = new RECT { Left = 0, Top = 0, Right = S(4), Bottom = h };
                FillRect(dc, ref stripe, accent);

                // Kişinin baş harfi (yuvarlak) ya da launcher simgesi
                int size = S(40), x = S(16), y = (h - size) / 2;
                string? person = PersonOf(card.Tag);
                if (person != null)
                {
                    SelectObject(dc, accent);
                    SelectObject(dc, GetStockObject(8 /* NULL_PEN */));
                    Ellipse(dc, x, y, x + size + 1, y + size + 1);
                    SetBkMode(dc, 1 /* TRANSPARENT */);
                    SetTextColor(dc, Rgb(0x16, 0x0e, 0x07));
                    SelectObject(dc, _fontLetter);
                    var letter = new RECT { Left = x, Top = y, Right = x + size, Bottom = y + size };
                    DrawText(dc, char.ToUpperInvariant(person[0]).ToString(), -1, ref letter, 0x1 | 0x4 | 0x20 /* CENTER|VCENTER|SINGLELINE */);
                }
                else if (_icon != IntPtr.Zero) DrawIconEx(dc, x, y, _icon, size, size, 0, IntPtr.Zero, 0x3 /* DI_NORMAL */);

                SelectObject(dc, oldPen);
                SelectObject(dc, oldBrush);
                DeleteObject(accent);

                // Başlık ve metin (sığmazsa "…")
                SetBkMode(dc, 1);
                int textLeft = x + size + S(12), right = w - S(14);
                IntPtr oldFont = SelectObject(dc, _fontTitle);
                SetTextColor(dc, Rgb(0xec, 0xe6, 0xdd));
                var title = new RECT { Left = textLeft, Top = S(16), Right = right, Bottom = S(36) };
                DrawText(dc, card.Title, -1, ref title, 0x20 | 0x8000 /* SINGLELINE|END_ELLIPSIS */ | 0x800 /* NOPREFIX */);
                SelectObject(dc, _fontBody);
                SetTextColor(dc, card.Accent == AccentFor("profile:") ? card.Accent : Rgb(0xa0, 0x96, 0x8c));
                var body = new RECT { Left = textLeft, Top = S(38), Right = right, Bottom = S(60) };
                DrawText(dc, card.Body, -1, ref body, 0x20 | 0x8000 | 0x800);
                SelectObject(dc, oldFont);
            }
            finally { EndPaint(hwnd, ref ps); }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _pending.Clear();
                if (_threadId != 0) PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
            }
            // Pencereler iş parçacığıyla birlikte yok olur; GDI nesneleri süreç kapanırken bırakılır
        }

        // ── Tam ekran oyun kontrolü ──────────────────────────────────────
        /// <summary>Öndeki pencere bulunduğu ekranı tamamen kaplıyorsa (tam ekran oyun) true.</summary>
        public static bool FullscreenAppInFront()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == GetDesktopWindow() || fg == GetShellWindow()) return false;
            if (!GetWindowRect(fg, out var r)) return false;
            IntPtr monitor = MonitorFromWindow(fg, 2 /* MONITOR_DEFAULTTONEAREST */);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) return false;
            return r.Left <= info.rcMonitor.Left && r.Top <= info.rcMonitor.Top
                && r.Right >= info.rcMonitor.Right && r.Bottom >= info.rcMonitor.Bottom;
        }

        // ── Win32 ─────────────────────────────────────────────────────────
        private delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam, lParam; public int time; public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct PAINTSTRUCT { public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore, fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }

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

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WNDCLASS cls);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
        [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr hwnd, int id, int ms, IntPtr proc);
        [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hwnd, int id);
        [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref RECT rect, IntPtr brush);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawText(IntPtr dc, string text, int count, ref RECT rect, uint format);
        [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string name, int type, int cx, int cy, int flags);
        [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, int id);
        [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, ref RECT value, uint winIni);
        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int style, int width, uint color);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFont(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int obj);
        [DllImport("gdi32.dll")] private static extern bool RoundRect(IntPtr dc, int l, int t, int r, int b, int w, int h);
        [DllImport("gdi32.dll")] private static extern bool Ellipse(IntPtr dc, int l, int t, int r, int b);
        [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
    }
}
