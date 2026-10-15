using System.Runtime.InteropServices;
using Microsoft.Win32;
using Photino.NET;

namespace VuluLauncher
{
    internal static class Program
    {
        // Arayüzün tasarlandığı boyut (%100 ölçekte). Ekran ölçeğine göre büyütülür.
        private const int BaseWidth = 1120;
        private const int BaseHeight = 680;

        private const string WebView2Key = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        private const string WebView2DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        [STAThread]
        private static void Main()
        {
            if (!WebView2Installed())
            {
                MessageBox(IntPtr.Zero,
                    "vulu Launcher için Microsoft Edge WebView2 Runtime gerekli, ancak bu bilgisayarda kurulu değil.\n\n" +
                    $"Şu adresten ücretsiz kurabilirsiniz:\n{WebView2DownloadUrl}",
                    "vulu Launcher", 0x10);
                return;
            }

            double scale = GetDpiForSystem() / 96.0;
            if (scale < 1) scale = 1;

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
                .SetSize((int)(BaseWidth * scale), (int)(BaseHeight * scale))
                .Center();

#if !DEBUG
            // Dağıtılan sürümde geliştirici araçları ve sağ tık menüsü kapalıdır.
            window.SetDevToolsEnabled(false).SetContextMenuEnabled(false);
#endif

            window.WindowCreated += (_, _) => RoundCorners(window.WindowHandle);

            var bridge = new Bridge(window);
            var app = new LauncherApp(bridge, window);
            app.RegisterCommands();

            window.RegisterWebMessageReceivedHandler((_, message) => bridge.HandleMessage(message));
            window.Load(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"));
            window.WaitForClose();

            app.Shutdown();
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
