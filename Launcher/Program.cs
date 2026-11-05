using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using VuluLauncher.Native;

namespace VuluLauncher
{
    internal static class Program
    {
        // Arayüzün tasarlandığı alan (DIP). Pencere ekrana göre bunun katı olur; küçük ekranda sığdırılır.
        private const int BaseWidth = 1320;
        private const int BaseHeight = 820;

        [STAThread]
        private static void Main(string[] args)
        {
            // Masaüstü kısayolu: "--play <paket>" / "--join <sunucu>"
            var action = StartupAction.Parse(args);
            // Bilgisayar açılışı (başlangıç kaydı): pencere açılmadan bildirim alanında başlar
            bool startInTray = args.Contains("--tray");
#if DEBUG
            // Yalnızca geliştirme derlemesinde: testler gerçek kullanıcı verisine (kayıtlı şifre, ayarlar) dokunmasın
            if (Environment.GetEnvironmentVariable("VULU_APP_DATA") is { Length: > 0 } testData)
                AppPaths.Initialize(testData, AppContext.BaseDirectory);
#endif

            // Launcher zaten açıksa yeni kopya açılmaz; istek açık olana gider ve o öne gelir
            using var single = new SingleInstance();
            if (!single.TryAcquire())
            {
                // Zaten açıksa açılış kaydı bir şey yapmaz (açık launcher öne gelmesin)
                if (!startInTray) SingleInstance.Forward(action);
                return;
            }

#if !DEBUG
            // Discord tarzı açılış: önce Updater sürümü denetler, sonra launcher'ı "--from-updater" ile açar.
            // Updater yoksa ya da açılamazsa launcher normal şekilde devam eder. Kısayoldan açılışta beklemeden
            // doğrudan açılır (güncelleme varsa launcher içinde sorulur).
            if (action == null && !startInTray && !args.Contains("--from-updater"))
            {
                single.Release();   // Updater'ın açacağı launcher kilidi alabilsin
                if (StartUpdater()) return;
                if (!single.TryAcquire()) { SingleInstance.Forward(null); return; }
            }
#endif

            // Önbellek temizliği ve eski güncelleme dosyaları
            Storage.CleanUpAtStartup();

            // Avalonia: ana iş parçacığı arayüz iş parçacığı olur; pencere kapatılsa da (bildirim alanı) uygulama sürer
            var lifetime = new ClassicDesktopStyleApplicationLifetime { Args = args, ShutdownMode = ShutdownMode.OnExplicitShutdown };
            AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().SetupWithLifetime(lifetime);

            using var host = new WindowsHost(lifetime);
            var bridge = new Bridge(host.SendToUi);
            CoreClient.Instance.Attach(bridge);
            var app = new LauncherApp(bridge, host);
            host.NotificationClicked += app.OnNotificationClicked;
            app.RegisterCommands();
            app.SetStartupAction(action);
            single.Listen(app.OnRemoteCommand);
            app.ApplyStartWithSystem();
            if (startInTray) app.StartInBackground();

            host.Start(startInTray);
            lifetime.Start(args);

            app.Shutdown();
        }

        /// <summary>Pencerenin boyutu (DIP) ve arayüz ölçeği; ayarlara ve ekrana göre.</summary>
        internal static (double Width, double Height, double Scale) WindowLayoutDip(string setting)
        {
            var (width, height, zoom) = WindowLayout(setting);
            double dpi = Math.Max(1, GetDpiForSystem() / 96.0);
            return (width / dpi, height / dpi, zoom / 100.0);
        }

        /// <summary>
        /// Pencere boyutu ve arayüz yakınlaştırması. Otomatikte ("") büyük ekranlarda pencere ekranın yaklaşık %72'sine
        /// büyür ve arayüz onunla birlikte yakınlaşır (küçük görünmesin). Hazır boyut seçildiyse ("1600x900") pencere tam
        /// o boyutta olur, arayüz pencereye göre ölçeklenir (LoL istemcisindeki gibi). Her durumda pencere masaüstünün
        /// kullanılabilir alanına sığdırılır.
        /// </summary>
        internal static (int Width, int Height, int Zoom) WindowLayout(string setting)
        {
            var (width, height, zoom) = BaseLayout(setting);
            // Ayarlardaki "yazı ve arayüz boyutu" pencereyi değil, içindeki her şeyi büyütür
            return (width, height, (int)Math.Round(Math.Clamp(zoom * AppSettings.Current.UiScale / 100.0, 60, 300)));
        }

        private static (int Width, int Height, int Zoom) BaseLayout(string setting)
        {
            double dpi = Math.Max(1, GetDpiForSystem() / 96.0);
            if (AppSettings.ParseWindowSize(setting) is { } size)
            {
                var (w, h) = FitToWorkArea(size.Width, size.Height);
                double z = Math.Min(w / (BaseWidth * dpi), h / (BaseHeight * dpi));
                return (w, h, (int)Math.Round(100 * Math.Clamp(z, 0.6, 2.5)));
            }
            double k = WorkArea() is { } area
                ? Math.Min(0.72 * area.Width / (BaseWidth * dpi), 0.80 * area.Height / (BaseHeight * dpi))
                : 1;
            k = Math.Clamp(Math.Round(k * 20) / 20, 1, 1.6);   // %5 adımlarla, %100–160
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
            if (WorkArea() is not { } area) return (width, height);
            return (Math.Min(width, (int)(area.Width * 0.96)), Math.Min(height, (int)(area.Height * 0.96)));
        }

        /// <summary>Masaüstünün görev çubuğu dışındaki alanı (piksel).</summary>
        internal static (int Width, int Height)? WorkArea()
        {
            var area = new RECT();
            return SystemParametersInfo(0x0030 /* SPI_GETWORKAREA */, 0, ref area, 0)
                ? (area.Right - area.Left, area.Bottom - area.Top) : null;
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

        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    }
}
