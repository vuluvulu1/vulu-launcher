using System.Runtime.InteropServices;

namespace VuluLauncher
{
    /// <summary>
    /// Steam'deki gibi ekranın sağ altında beliren bildirim kartı: ekranın sağ kenarından kayarak gelir, sağ kenara ve
    /// görev çubuğuna yapışık durur (yalnızca sol köşeleri yuvarlak), kapanırken yine sağa kayarak gider. Gönderen bir
    /// kişiyse profil resmi (yoksa baş harfi) yuvarlak olarak görünür.
    ///
    /// Tarayıcı kullanmaz: düz Win32 penceresi GDI ile çizilir (birkaç yüz KB bellek); profil resmi Windows'un kendi
    /// resim bileşeniyle (WIC: PNG, JPEG, GIF, WebP) çözülür. Kart odağı çalmaz ve hep en üsttedir; tam ekran oyunun
    /// üstünde de görünür, oyunu küçültmez. En fazla üç kart üst üste durur, her biri 6 sn sonra gider. Tıklanınca
    /// <see cref="Clicked"/> bildirimin etiketiyle çağrılır.
    ///
    /// Bütün pencereler tek bir iş parçacığında, ilk bildirimde açılan mesaj döngüsünde yaşar. Fontlar ve fırçalar bir
    /// kez oluşturulur; kart kapanınca penceresi ve resmi yok edilir.
    /// </summary>
    internal sealed class ToastWindow : IDisposable
    {
        private const int Width = 340, Height = 80, Gap = 6, Radius = 14, AvatarSize = 44, MaxCards = 3;
        private const int ShowMs = 6000, FrameMs = 15;
        private const int WM_APP_SHOW = 0x8000 + 2, WM_PAINT = 0x000F, WM_TIMER = 0x0113, WM_LBUTTONUP = 0x0202,
            WM_CLOSE = 0x0010, WM_DESTROY = 0x0002, WM_MOUSEACTIVATE = 0x0021, WM_SETCURSOR = 0x0020;
        private const int TimerAnimate = 1, TimerHide = 2;

        private readonly string _iconPath;
        private readonly object _gate = new();
        private readonly Queue<(string Title, string Body, string? Tag, string? Image)> _pending = new();
        private readonly List<Card> _cards = new();
        private Thread? _thread;
        private uint _threadId;
        private bool _disposed;
        private WndProc? _proc;   // GC toplamasın
        private IntPtr _instance, _icon, _fontTitle, _fontBody, _fontLetter, _brushBack, _penBorder, _cursor;
        private double _scale = 1;

        private sealed class Card
        {
            public IntPtr Hwnd, Avatar;   // Avatar: önceden çarpılmış alfa ile 32 bit, yuvarlak kesilmiş DIB
            public string Title = "", Body = "";
            public string? Tag;
            public double Offset;          // ekranın sağına ne kadar taşıyor (0 = tam görünür)
            public int Y;
            public bool Closing;
            public uint Accent;
        }

        /// <summary>Karta tıklandı (etiket, ör. "dm:Ali").</summary>
        public event Action<string?>? Clicked;

        public ToastWindow(string iconPath) => _iconPath = iconPath;

        /// <param name="imagePath">Gönderenin profil resmi (yerel dosya) ya da null.</param>
        public void Show(string title, string body, string? tag, string? imagePath = null)
        {
            lock (_gate)
            {
                if (_disposed) return;
                _pending.Enqueue((title, body, tag, imagePath));
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
                _icon = LoadImage(IntPtr.Zero, _iconPath, 1 /* IMAGE_ICON */, S(AvatarSize), S(AvatarSize), 0x10 /* LR_LOADFROMFILE */);
                _fontTitle = Font(14, 600);
                _fontBody = Font(13, 400);
                _fontLetter = Font(19, 700);
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
                (string Title, string Body, string? Tag, string? Image) next;
                lock (_gate)
                {
                    if (_disposed || _pending.Count == 0) return;
                    next = _pending.Dequeue();
                }
                // En eskisi gider (en fazla üç kart)
                while (_cards.Count(c => !c.Closing) >= MaxCards) BeginClose(_cards.First(c => !c.Closing));

                int w = S(Width), h = S(Height), r = S(Radius);
                var card = new Card
                {
                    Title = next.Title, Body = next.Body, Tag = next.Tag, Accent = AccentFor(next.Tag),
                    Offset = w, Avatar = next.Image != null ? AvatarBitmap.Load(next.Image, S(AvatarSize)) : IntPtr.Zero,
                };
                card.Hwnd = CreateWindowEx(
                    0x8 /* WS_EX_TOPMOST */ | 0x80 /* WS_EX_TOOLWINDOW */ | 0x8000000 /* WS_EX_NOACTIVATE */ | 0x80000 /* WS_EX_LAYERED */,
                    "vuluLauncherToast", next.Title, unchecked((int)0x80000000) /* WS_POPUP */, 0, 0, w, h,
                    IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
                if (card.Hwnd == IntPtr.Zero) { if (card.Avatar != IntPtr.Zero) DeleteObject(card.Avatar); continue; }
                // Bölge sağdan taşırılır: sol köşeler yuvarlak, sağ kenar ekrana yapışık düz
                SetWindowRgn(card.Hwnd, CreateRoundRectRgn(0, 0, w + r + 1, h + 1, r, r), false);   // bölge artık pencereye ait
                SetLayeredWindowAttributes(card.Hwnd, 0, 250, 0x2 /* LWA_ALPHA */);
                _cards.Add(card);
                Layout();
                ShowWindow(card.Hwnd, 4 /* SW_SHOWNOACTIVATE */);
                SetTimer(card.Hwnd, TimerAnimate, FrameMs, IntPtr.Zero);
                SetTimer(card.Hwnd, TimerHide, ShowMs, IntPtr.Zero);
            }
        }

        /// <summary>Kartlar sağ altta, görev çubuğunun hemen üstünden yukarı doğru dizilir; en yenisi en altta.</summary>
        private void Layout()
        {
            var area = new RECT();
            SystemParametersInfo(0x0030 /* SPI_GETWORKAREA */, 0, ref area, 0);
            int y = area.Bottom;
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                y -= S(Height);
                _cards[i].Y = y;
                Place(_cards[i], area);
                y -= S(Gap);
            }
        }

        private void Place(Card card, RECT? workArea = null)
        {
            var area = workArea ?? WorkArea();
            int x = area.Right - S(Width) + (int)Math.Round(card.Offset);
            SetWindowPos(card.Hwnd, new IntPtr(-1) /* HWND_TOPMOST */, x, card.Y, 0, 0, 0x1 /* SWP_NOSIZE */ | 0x10 /* SWP_NOACTIVATE */);
        }

        private static RECT WorkArea()
        {
            var area = new RECT();
            SystemParametersInfo(0x0030, 0, ref area, 0);
            return area;
        }

        /// <summary>Oyuna giriş yeşil, diğerleri launcher'ın vurgu rengi (turuncu).</summary>
        private static uint AccentFor(string? tag) =>
            tag != null && tag.StartsWith("profile:", StringComparison.Ordinal) ? Rgb(0x7c, 0xc4, 0x7f) : Rgb(0xc2, 0x7e, 0x3e);

        /// <summary>Etiketten kişi adı (dm:, friend:, profile:, invite:kişi:sunucu); resmi yoksa kartta baş harfi görünür.</summary>
        private static string? PersonOf(string? tag)
        {
            if (tag == null) return null;
            if (IsInvite(tag)) return tag.Split(':')[1];
            foreach (var prefix in new[] { "dm:", "friend:", "profile:" })
                if (tag.StartsWith(prefix, StringComparison.Ordinal) && tag.Length > prefix.Length) return tag[prefix.Length..];
            return null;
        }

        /// <summary>Sunucu daveti ("invite:kişi:sunucu"): kartta "Katıl" düğmesi çizilir.</summary>
        private static bool IsInvite(string? tag) => tag != null && tag.StartsWith("invite:", StringComparison.Ordinal) && tag.Split(':').Length == 3;

        /// <summary>"Katıl" düğmesinin kart içindeki yeri.</summary>
        private RECT JoinButton() => new() { Left = S(Width) - S(76), Top = (S(Height) - S(30)) / 2, Right = S(Width) - S(14), Bottom = (S(Height) + S(30)) / 2 };

        private void BeginClose(Card card)
        {
            if (card.Closing) return;
            card.Closing = true;
            KillTimer(card.Hwnd, TimerHide);
            SetTimer(card.Hwnd, TimerAnimate, FrameMs, IntPtr.Zero);
        }

        /// <summary>
        /// Bir kare: gelirken yavaşlayarak yerine oturur (ease-out), giderken hızlanarak sağa çıkar (ease-in).
        /// </summary>
        private void Animate(Card card)
        {
            double w = S(Width);
            if (!card.Closing)
            {
                card.Offset -= Math.Max(1, card.Offset * 0.22);
                if (card.Offset <= 0.5) { card.Offset = 0; KillTimer(card.Hwnd, TimerAnimate); }
            }
            else
            {
                card.Offset += Math.Max(4, card.Offset * 0.28);
                if (card.Offset >= w) { DestroyWindow(card.Hwnd); return; }
            }
            Place(card);
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
                    if ((int)wParam == TimerHide) BeginClose(card);
                    else Animate(card);
                    return IntPtr.Zero;
                case WM_LBUTTONUP when card != null && !card.Closing:
                    // Davette yalnızca "Katıl" düğmesi katılır; kartın geri kalanı konuşmayı açar
                    string? clicked = card.Tag;
                    if (IsInvite(clicked))
                    {
                        int x = (short)(lParam.ToInt64() & 0xFFFF), y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        var button = JoinButton();
                        bool onButton = x >= button.Left && x <= button.Right && y >= button.Top && y <= button.Bottom;
                        if (!onButton) clicked = "dm:" + PersonOf(clicked);
                    }
                    try { Clicked?.Invoke(clicked); } catch { }
                    BeginClose(card);
                    return IntPtr.Zero;
                case WM_DESTROY when card != null:
                    KillTimer(hwnd, TimerAnimate); KillTimer(hwnd, TimerHide);
                    if (card.Avatar != IntPtr.Zero) { DeleteObject(card.Avatar); card.Avatar = IntPtr.Zero; }
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
                int w = S(Width), h = S(Height), r = S(Radius);
                var all = new RECT { Left = 0, Top = 0, Right = w, Bottom = h };
                FillRect(dc, ref all, _brushBack);

                // İnce çerçeve (sağ kenarı ekranın dışında kalır) ve soldaki renkli şerit
                IntPtr oldPen = SelectObject(dc, _penBorder);
                IntPtr oldBrush = SelectObject(dc, GetStockObject(5 /* NULL_BRUSH */));
                RoundRect(dc, 0, 0, w + r, h, r, r);
                IntPtr accent = CreateSolidBrush(card.Accent);
                var stripe = new RECT { Left = 0, Top = 0, Right = S(4), Bottom = h };
                FillRect(dc, ref stripe, accent);

                // Profil resmi; yoksa kişinin baş harfi (yuvarlak) ya da launcher simgesi
                int size = S(AvatarSize), x = S(18), y = (h - size) / 2;
                string? person = PersonOf(card.Tag);
                if (card.Avatar != IntPtr.Zero)
                {
                    IntPtr mem = CreateCompatibleDC(dc);
                    IntPtr old = SelectObject(mem, card.Avatar);
                    AlphaBlend(dc, x, y, size, size, mem, 0, 0, size, size, 0x01FF0000 /* AC_SRC_OVER, 255, AC_SRC_ALPHA */);
                    SelectObject(mem, old);
                    DeleteDC(mem);
                }
                else if (person != null)
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

                // Başlık ve metin (sığmazsa "…"); davette sağda "Katıl" düğmesi
                SetBkMode(dc, 1);
                int textLeft = x + size + S(14), right = w - S(16);
                if (IsInvite(card.Tag))
                {
                    var button = JoinButton();
                    IntPtr fill = CreateSolidBrush(card.Accent);
                    IntPtr prevBrush = SelectObject(dc, fill), prevPen = SelectObject(dc, GetStockObject(8 /* NULL_PEN */));
                    RoundRect(dc, button.Left, button.Top, button.Right + 1, button.Bottom + 1, S(8), S(8));
                    SelectObject(dc, prevBrush); SelectObject(dc, prevPen);
                    DeleteObject(fill);
                    IntPtr prevFont = SelectObject(dc, _fontTitle);
                    SetTextColor(dc, Rgb(0x16, 0x0e, 0x07));
                    DrawText(dc, "Katıl", -1, ref button, 0x1 | 0x4 | 0x20);
                    SelectObject(dc, prevFont);
                    right = button.Left - S(10);
                }
                IntPtr oldFont = SelectObject(dc, _fontTitle);
                SetTextColor(dc, Rgb(0xec, 0xe6, 0xdd));
                var title = new RECT { Left = textLeft, Top = S(17), Right = right, Bottom = S(38) };
                DrawText(dc, card.Title, -1, ref title, 0x20 | 0x8000 /* SINGLELINE|END_ELLIPSIS */ | 0x800 /* NOPREFIX */);
                SelectObject(dc, _fontBody);
                SetTextColor(dc, card.Accent == AccentFor("profile:") ? card.Accent : Rgb(0xa0, 0x96, 0x8c));
                var body = new RECT { Left = textLeft, Top = S(40), Right = right, Bottom = S(62) };
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

        // ── Win32 ─────────────────────────────────────────────────────────
        private delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam, lParam; public int time; public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct PAINTSTRUCT { public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore, fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved; }

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
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int style, int width, uint color);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFont(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int obj);
        [DllImport("gdi32.dll")] private static extern bool RoundRect(IntPtr dc, int l, int t, int r, int b, int w, int h);
        [DllImport("gdi32.dll")] private static extern bool Ellipse(IntPtr dc, int l, int t, int r, int b);
        [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
        [DllImport("msimg32.dll")] private static extern bool AlphaBlend(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint blend);
    }
}
