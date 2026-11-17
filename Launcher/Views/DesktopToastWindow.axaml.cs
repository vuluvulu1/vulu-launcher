using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Masaüstü bildirimleri: ekranın sağ altında, görev çubuğunun hemen üstünde, bütün pencerelerin (kenarlıksız tam
    /// ekran oyunların da) üstünde. Kart odağı çalmaz: tıklamak oyunu ya da açık uygulamayı arkaya atmaz.
    /// Launcher içindeki bildirim kartıyla aynı görünüm ve tema (Views/ToastCard). Kart kalmayınca pencere gizlenir.
    /// </summary>
    internal sealed partial class DesktopToastWindow : Window
    {
        private readonly ToastStack _stack;

        public DesktopToastWindow() : this(new ToastStack(_ => { })) { }   // XAML önizleyicisi için

        public DesktopToastWindow(ToastStack stack)
        {
            _stack = stack;
            DataContext = stack;
            InitializeComponent();
            Themes.Mark(this);
            stack.Emptied += () => { if (_stack.Items.Count == 0) Hide(); };
            // Kart eklenip çıktıkça pencere boyu değişir; alt kenar hep görev çubuğunun üstünde kalsın
            SizeChanged += (_, _) => Place();
            Opened += (_, _) =>
            {
                if (TryGetPlatformHandle()?.Handle is { } hwnd && OperatingSystem.IsWindows())
                {
                    long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
                    SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
                }
                Place();
            };
        }

        /// <summary>Kartı gösterir (arayüz iş parçacığında).</summary>
        public void Add(string title, string body, string? tag, string? avatar)
        {
            _stack.Show(title, body, tag, avatar);
            if (!IsVisible) Show();
            else Place();
            // Tam ekran bir pencere sonradan öne geçtiyse yeniden en üste
            if (TryGetPlatformHandle()?.Handle is { } hwnd && OperatingSystem.IsWindows())
                SetWindowPos(hwnd, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x1 | 0x2 | 0x10 /* NOSIZE|NOMOVE|NOACTIVATE */);
#if DEBUG
            // Geliştirmede: VULU_UI_SHOT varsa bu pencere de (yalnızca kendisi) PNG'ye çizilir
            if (Environment.GetEnvironmentVariable("VULU_UI_SHOT") is { Length: > 0 } dir)
                Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                {
                    try
                    {
                        using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
                        rtb.Render(this);
                        rtb.Save(System.IO.Path.Combine(dir, $"desktop-toast-{_stack.Items.Count}.png"));
                        var own = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                        LogFile.Write($"[bildirim] konum {Position}, boyut {Bounds.Width}x{Bounds.Height}, saydamlık {ActualTransparencyLevel}, " +
                                      $"öndeki pencere bildirim mi: {GetForegroundWindow() == own}, exstyle 0x{GetWindowLongPtr(own, GWL_EXSTYLE).ToInt64():X}");
                    }
                    catch (Exception ex) { LogFile.Write("[ui] bildirim görüntüsü alınamadı: " + ex.Message); }
                }, TimeSpan.FromMilliseconds(900));
#endif
        }

        private void Place()
        {
            var screen = Screens.Primary ?? Screens.ScreenFromWindow(this);
            if (screen == null) return;
            var area = screen.WorkingArea;
            double scale = screen.Scaling;
            int w = (int)Math.Ceiling(Bounds.Width * scale), h = (int)Math.Ceiling(Bounds.Height * scale);
            if (w <= 0 || h <= 0) return;
            var target = new PixelPoint(area.Right - w, area.Bottom - h);
            if (Position != target) Position = target;
        }

        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
