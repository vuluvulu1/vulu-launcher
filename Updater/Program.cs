using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using Photino.NET;

namespace Updater
{
    /// <summary>
    /// Discord tarzı güncelleyici. Kısayol launcher'ı açar; launcher (Release derlemede) önce bunu çalıştırır.
    /// Bu küçük pencere sürümü denetler, gerekiyorsa günceller ve launcher'ı "--from-updater" ile açar.
    ///
    /// Argümanlar:
    ///   --wait-pid &lt;pid&gt;   kurulumdan önce kapanması beklenecek süreç (launcher'dan başlatılınca)
    /// Eski sürümün konumsal argümanları (indirme adresi, exe yolu, sha256) yok sayılır.
    /// </summary>
    internal static class Program
    {
        private const int BaseWidth = 340;
        private const int BaseHeight = 400;

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private static PhotinoWindow _window = null!;
        private static UpdateEngine _engine = null!;
        private static CancellationTokenSource _cancel = new();
        private static bool _started;

        // Pencere sürükleme: başlangıçtaki pencere ve imleç konumu
        private static Point _dragWindowStart;
        private static POINT _dragCursorStart;

        [STAThread]
        private static void Main(string[] args)
        {
            string installDir = AppContext.BaseDirectory;
            var waitPids = ParseWaitPids(args);

            if (!WebView2Installed())
            {
                // Arayüz açılamıyor; güncellemeden launcher'a geçilir (launcher kendi uyarısını gösterir)
                TryLaunch(installDir, waitPids);
                return;
            }

            double scale = Math.Max(1, GetDpiForSystem() / 96.0);

            _window = new PhotinoWindow()
                .SetTitle("vulu Launcher Güncelleyici")
                .SetIconFile(Path.Combine(installDir, "vuluLauncher-logo-prototip.ico"))
                .SetTemporaryFilesPath(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vuluLauncher", "updater-browser"))
                .SetChromeless(true)
                .SetResizable(false)
                .SetUseOsDefaultSize(false)
                .SetUseOsDefaultLocation(false)
                .SetSize((int)(BaseWidth * scale), (int)(BaseHeight * scale))
                .Center();

#if !DEBUG
            _window.SetDevToolsEnabled(false).SetContextMenuEnabled(false);
#endif

            _window.WindowCreated += (_, _) =>
            {
                RoundCorners(_window.WindowHandle);
                VuluShared.WindowIcon.Apply(_window.WindowHandle, Path.Combine(installDir, "vuluLauncher-logo-prototip.ico"));
            };
            // Dosyalar yer değiştirirken kapatılırsa kurulum yarım kalır; o sırada kapatma engellenir
            _window.WindowClosing += (_, _) => _engine.IsApplying;

            _engine = new UpdateEngine(installDir, waitPids, Send);
            _window.RegisterWebMessageReceivedHandler((_, message) => HandleMessage(message));
            _window.LoadRawString(BuildPage());
            _window.WaitForClose();

            _cancel.Cancel();
        }

        // ── Arayüzden gelen komutlar ──────────────────────────────────────
        private static void HandleMessage(string raw)
        {
            string cmd;
            try { cmd = JsonDocument.Parse(raw).RootElement.GetProperty("cmd").GetString() ?? ""; }
            catch { return; }

            switch (cmd)
            {
                case "ready":
                    // Sayfa yüklenmeden gönderilen mesajlar kaybolur; akış sayfa hazır olunca başlar
                    if (_started) return;
                    _started = true;
                    _ = RunAsync();
                    break;
                case "retry":
                    _cancel = new CancellationTokenSource();
                    _ = RunAsync();
                    break;
                case "launch":
                    LaunchAndClose();
                    break;
                case "minimize":
                    _window.SetMinimized(true);
                    break;
                case "close":
                    if (!_engine.IsApplying) _window.Close();
                    break;
                case "dragStart":
                    _dragWindowStart = _window.Location;
                    GetCursorPos(out _dragCursorStart);
                    break;
                case "dragMove":
                    if (!GetCursorPos(out var cursor)) return;
                    _window.Location = new Point(
                        _dragWindowStart.X + cursor.X - _dragCursorStart.X,
                        _dragWindowStart.Y + cursor.Y - _dragCursorStart.Y);
                    break;
            }
        }

        private static async Task RunAsync()
        {
            try
            {
                if (await _engine.RunAsync(_cancel.Token)) LaunchAndClose();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Send(new UpdateStatus("error", "Güncelleme başarısız", ex.Message));
            }
        }

        private static void LaunchAndClose()
        {
            try
            {
                _engine.LaunchLauncher();
                _window.Close();
            }
            catch (Exception ex)
            {
                Send(new UpdateStatus("error", "Launcher açılamadı", ex.Message));
            }
        }

        private static void Send(UpdateStatus status)
        {
            try { _window.SendWebMessage(JsonSerializer.Serialize(status, Json)); }
            catch { /* pencere kapanırken gelen geç mesajlar yok sayılır */ }
        }

        // ── Sayfa ─────────────────────────────────────────────────────────
        /// <summary>Gömülü HTML'e font ve logoyu yerleştirir; sayfa diskteki hiçbir dosyaya bağlı değildir.</summary>
        private static string BuildPage()
        {
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            return ReadResourceText("updater.html")
                .Replace("__FONT__", "data:font/ttf;base64," + Convert.ToBase64String(ReadResource("IBMPlexMono.ttf")))
                .Replace("__SANS__", "data:font/ttf;base64," + Convert.ToBase64String(ReadResource("IBMPlexSans.ttf")))
                .Replace("__LOGO__", "data:image/png;base64," + Convert.ToBase64String(ReadResource("logo.png")))
                .Replace("__VERSION__", VuluShared.Releases.Clean(version));
        }

        private static byte[] ReadResource(string name)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                ?? throw new FileNotFoundException(name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static string ReadResourceText(string name) => System.Text.Encoding.UTF8.GetString(ReadResource(name));

        // ── Yardımcılar ───────────────────────────────────────────────────
        private static List<int> ParseWaitPids(string[] args)
        {
            var pids = new List<int>();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--wait-pid" && int.TryParse(args[i + 1], out int pid)) pids.Add(pid);
            return pids;
        }

        private static void TryLaunch(string installDir, List<int> waitPids)
        {
            try { new UpdateEngine(installDir, waitPids, _ => { }).LaunchLauncher(); }
            catch { }
        }

        private const string WebView2Key = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

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

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
