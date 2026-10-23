using System.Runtime.InteropServices;
using Microsoft.Win32;
using Photino.NET;

namespace VuluLauncher
{
    internal static class Program
    {
        // Arayüzün tasarlandığı boyut (%100 ölçekte). Ekran ölçeğine göre büyütülür.
        private const int BaseWidth = 1240;
        private const int BaseHeight = 760;

        private const string WebView2Key = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        private const string WebView2DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        [STAThread]
        private static void Main(string[] args)
        {
            // Masaüstü kısayolu: "--play <paket>" / "--join <sunucu>"
            var action = StartupAction.Parse(args);

            // Launcher zaten açıksa yeni kopya açılmaz; istek açık olana gider ve o öne gelir
            using var single = new SingleInstance();
            if (!single.TryAcquire())
            {
                SingleInstance.Forward(action);
                return;
            }

#if !DEBUG
            // Discord tarzı açılış: önce Updater sürümü denetler, sonra launcher'ı "--from-updater" ile açar.
            // Updater yoksa ya da açılamazsa launcher normal şekilde devam eder. Kısayoldan açılışta beklemeden
            // doğrudan açılır (güncelleme varsa launcher içinde sorulur).
            if (action == null && !args.Contains("--from-updater"))
            {
                single.Release();   // Updater'ın açacağı launcher kilidi alabilsin
                if (StartUpdater()) return;
                if (!single.TryAcquire()) { SingleInstance.Forward(null); return; }
            }
#endif

            if (!WebView2Installed())
            {
                MessageBox(IntPtr.Zero,
                    "vulu Launcher için Microsoft Edge WebView2 Runtime gerekli, ancak bu bilgisayarda kurulu değil.\n\n" +
                    $"Şu adresten ücretsiz kurabilirsiniz:\n{WebView2DownloadUrl}",
                    "vulu Launcher", 0x10);
                return;
            }

            var (width, height, zoom) = WindowLayout(AppSettings.Current.WindowScale);

            var window = new PhotinoWindow()
                .SetTitle("vulu Launcher")
                .SetIconFile(Path.Combine(AppContext.BaseDirectory, "logo.ico"))
                // Tarayıcı önbelleği varsayılan olarak bütün Photino uygulamalarının paylaştığı klasöre yazılır;
                // launcher'ın kendi klasörüne alınır.
                .SetTemporaryFilesPath(AppPaths.BrowserData)
                .SetChromeless(true)
                .SetResizable(false)
                .SetUseOsDefaultSize(false)
                .SetUseOsDefaultLocation(false)
                .SetSize(width, height)
                .SetZoom(zoom)
                .Center();

            // Sağ tık menüsü hiçbir sürümde açılmaz (tarayıcı menüsü launcher'a ait değil)
            window.SetContextMenuEnabled(false);
#if !DEBUG
            // Dağıtılan sürümde geliştirici araçları da kapalıdır; Debug'da F12 ile açılır.
            window.SetDevToolsEnabled(false);
#endif

            window.WindowCreated += (_, _) =>
            {
                RoundCorners(window.WindowHandle);
                VuluShared.WindowIcon.Apply(window.WindowHandle, Path.Combine(AppContext.BaseDirectory, "logo.ico"));
            };

            using var host = new WindowsHost(window);
            var bridge = new Bridge(host.SendToUi);
            var app = new LauncherApp(bridge, host);
            host.NotificationClicked += app.OnNotificationClicked;
            app.RegisterCommands();
            app.SetStartupAction(action);
            single.Listen(app.OnRemoteCommand);

            window.RegisterWebMessageReceivedHandler((_, message) => bridge.HandleMessage(message));
            window.Load(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"));
            window.WaitForClose();

            app.Shutdown();
        }

        /// <summary>
        /// Pencere boyutu ve arayüz yakınlaştırması. Otomatikte (0) büyük ekranlarda pencere ekranın yaklaşık
        /// %72'sine büyür ve arayüz onunla birlikte yakınlaşır (küçük görünmesin); seçilen yüzde (125, 150...) bunu sabitler.
        /// Her durumda pencere masaüstünün kullanılabilir alanına sığdırılır.
        /// </summary>
        internal static (int Width, int Height, int Zoom) WindowLayout(int setting)
        {
            double dpi = Math.Max(1, GetDpiForSystem() / 96.0);
            double k = setting / 100.0;
            if (setting == 0)
            {
                var area = new RECT();
                k = SystemParametersInfo(0x0030 /* SPI_GETWORKAREA */, 0, ref area, 0)
                    ? Math.Min(0.72 * (area.Right - area.Left) / (BaseWidth * dpi), 0.80 * (area.Bottom - area.Top) / (BaseHeight * dpi))
                    : 1;
                k = Math.Clamp(Math.Round(k * 20) / 20, 1, 1.6);   // %5 adımlarla, %100–160
            }
            var (width, height) = FitToWorkArea((int)(BaseWidth * dpi * k), (int)(BaseHeight * dpi * k));
            // Pencere ekrana sığdırılmak için küçüldüyse yakınlaştırma da o oranda azalır
            double fit = Math.Min(width / (BaseWidth * dpi * k), height / (BaseHeight * dpi * k));
            return (width, height, (int)Math.Round(100 * k * Math.Min(1, fit)));
        }

        /// <summary>
        /// Küçük ekranlarda (ör. 1366×768 dizüstü) pencere görev çubuğunun altına taşmasın: masaüstünün
        /// kullanılabilir alanına sığacak kadar küçültülür. Arayüz esnek olduğu için daha küçük boyutta da çalışır.
        /// </summary>
        private static (int Width, int Height) FitToWorkArea(int width, int height)
        {
            var area = new RECT();
            if (!SystemParametersInfo(0x0030 /* SPI_GETWORKAREA */, 0, ref area, 0)) return (width, height);

            int maxWidth = (int)((area.Right - area.Left) * 0.96);
            int maxHeight = (int)((area.Bottom - area.Top) * 0.96);
            return (Math.Min(width, maxWidth), Math.Min(height, maxHeight));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint param, ref RECT value, uint winIni);

        private static bool StartUpdater()
        {
            string updater = Path.Combine(AppContext.BaseDirectory, VuluShared.Releases.UpdaterExeName);
            if (!File.Exists(updater)) return false;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(updater)
                {
                    UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool WebView2Installed() =>
            HasVersion(Registry.LocalMachine, $@"SOFTWARE\WOW6432Node\{WebView2Key}")
            || HasVersion(Registry.LocalMachine, $@"SOFTWARE\{WebView2Key}")
            || HasVersion(Registry.CurrentUser, $@"Software\{WebView2Key}");

        private static bool HasVersion(RegistryKey root, string path)
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue("pv") is string version && version.Length > 0 && version != "0.0.0.0";
        }

        /// <summary>Windows 11'de çerçevesiz pencerenin köşelerini yuvarlatır; eski sürümlerde etkisizdir.</summary>
        private static void RoundCorners(IntPtr hwnd)
        {
            try
            {
                int preference = 2; // DWMWCP_ROUND
                _ = DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
            }
            catch { }
        }

        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
