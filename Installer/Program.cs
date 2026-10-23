using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using Photino.NET;
using VuluShared;

namespace Installer
{
    /// <summary>
    /// vulu Launcher kurulum programı. En son sürümü vulu sunucusundan indirip kurar, kısayolları ve
    /// "Program ekle/kaldır" kaydını oluşturur. Yönetici izni gerekmez.
    ///
    /// Argümanlar (otomasyon/test için):
    ///   --silent              arayüz açmadan kurar
    ///   --dir &lt;klasör&gt;        kurulum klasörü (varsayılan: önceki kurulum ya da LocalAppData\Programs)
    ///   --no-desktop          masaüstü kısayolu oluşturma
    ///   --no-startmenu        Başlat menüsü kısayolu oluşturma
    /// </summary>
    internal static class Program
    {
        private const int BaseWidth = 760;
        private const int BaseHeight = 500;
        private const string WebView2Key = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        private const string WebView2Bootstrapper = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private static PhotinoWindow _window = null!;
        private static LatestRelease? _latest;
        private static string? _installedDir;
        private static bool _installing;

        private static Point _dragWindowStart;
        private static POINT _dragCursorStart;

        [STAThread]
        private static int Main(string[] args)
        {
            string? dirArg = ArgValue(args, "--dir");
            if (args.Contains("--silent")) return RunSilent(dirArg, !args.Contains("--no-desktop"), !args.Contains("--no-startmenu"));

            if (!WebView2Installed() && !InstallWebView2()) return 1;

            string ui = ExtractUi();
            double scale = Math.Max(1, GetDpiForSystem() / 96.0);

            _window = new PhotinoWindow()
                .SetTitle("vulu Launcher Kurulumu")
                .SetTemporaryFilesPath(Path.Combine(Path.GetTempPath(), "vulu-setup-browser"))
                .SetChromeless(true)
                .SetResizable(false)
                .SetUseOsDefaultSize(false)
                .SetUseOsDefaultLocation(false)
                .SetSize((int)(BaseWidth * scale), (int)(BaseHeight * scale))
                .SetContextMenuEnabled(false)
                .Center();
#if !DEBUG
            _window.SetDevToolsEnabled(false);
#endif
            _window.WindowCreated += (_, _) =>
            {
                RoundCorners(_window.WindowHandle);
                WindowIcon.Apply(_window.WindowHandle, Path.Combine(ui, "logo.ico"));
            };
            // Dosyalar yerleştirilirken kapatılırsa kurulum yarım kalır
            _window.WindowClosing += (_, _) => _installing;

            _installedDir = dirArg ?? InstallInfo.InstalledDirectory();
            _window.RegisterWebMessageReceivedHandler((_, message) => HandleMessage(message));
            _window.Load(Path.Combine(ui, "setup.html"));
            _window.WaitForClose();

            try { Directory.Delete(ui, recursive: true); } catch { }
            return 0;
        }

        // ── Arayüzden gelen komutlar ──────────────────────────────────────
        private static void HandleMessage(string raw)
        {
            JsonElement msg;
            try { msg = JsonDocument.Parse(raw).RootElement.Clone(); }
            catch { return; }
            string cmd = msg.TryGetProperty("cmd", out var c) ? c.GetString() ?? "" : "";

            switch (cmd)
            {
                case "ready":
                    string dir = _installedDir ?? InstallInfo.DefaultDirectory;
                    Send(new { type = "init", dir, reinstall = _installedDir != null, freeMb = InstallEngine.FreeSpaceMb(dir), warning = InstallEngine.PathWarning(dir) });
                    _ = LoadReleaseAsync();
                    break;
                case "pickFolder":
                    var picked = _window.ShowOpenFolder("Kurulum klasörünü seç", null, false);
                    if (picked is { Length: > 0 })
                    {
                        // Seçilen klasörün içine "vuluLauncher" alt klasörü açılır (oyun dosyaları da burada durur)
                        string chosen = picked[0];
                        string last = Path.GetFileName(chosen.TrimEnd('\\'));
                        if (!last.Equals(InstallInfo.FolderName, StringComparison.OrdinalIgnoreCase) && !last.Equals(InstallInfo.AppName, StringComparison.OrdinalIgnoreCase))
                            chosen = Path.Combine(chosen, InstallInfo.FolderName);
                        Send(new { type = "dir", dir = chosen, freeMb = InstallEngine.FreeSpaceMb(chosen), warning = InstallEngine.PathWarning(chosen) });
                    }
                    break;
                case "checkDir":
                    string asked = msg.TryGetProperty("dir", out var d) ? d.GetString() ?? "" : "";
                    try { Send(new { type = "dir", dir = InstallEngine.ValidateDirectory(asked), freeMb = InstallEngine.FreeSpaceMb(asked), warning = InstallEngine.PathWarning(asked) }); }
                    catch (Exception ex) { Send(new { type = "dir", dir = asked, error = ex.Message }); }
                    break;
                case "reloadRelease":
                    _ = LoadReleaseAsync();
                    break;
                case "install":
                    if (_installing) return;
                    var options = new InstallOptions(
                        msg.GetProperty("dir").GetString() ?? "",
                        msg.TryGetProperty("desktop", out var dk) && dk.GetBoolean(),
                        msg.TryGetProperty("startMenu", out var sm) && sm.GetBoolean());
                    _ = InstallAsync(options);
                    break;
                case "launch":
                    try
                    {
                        InstallEngine.Launch(msg.GetProperty("dir").GetString() ?? "");
                        _window.Close();
                    }
                    catch (Exception ex) { Send(new { type = "error", message = "Launcher açılamadı: " + ex.Message }); }
                    break;
                case "close":
                    if (!_installing) _window.Close();
                    break;
                case "minimize":
                    _window.SetMinimized(true);
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

        private static async Task LoadReleaseAsync()
        {
            try
            {
                using var http = Releases.CreateHttpClient(TimeSpan.FromSeconds(30));
                _latest = await Releases.GetLatestAsync(http);
                var m = _latest.Manifest;
                Send(new
                {
                    type = "release",
                    version = Releases.Clean(m.Version),
                    notes = m.Notes,
                    downloadMb = Math.Round(m.Size / 1024.0 / 1024.0, 1),
                    installMb = Math.Round(m.Files.Sum(f => f.Size) / 1024.0 / 1024.0)
                });
            }
            catch (ReleaseSignatureException ex)
            {
                Send(new { type = "release-error", message = ex.Message });
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Send(new { type = "release-error", message = "Sunucuda henüz yayınlanmış bir vulu Launcher sürümü yok." });
            }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
            {
                Send(new { type = "release-error", message = "Sunucu şu an çok fazla istek aldı. Birkaç dakika sonra tekrar dene." });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Send(new { type = "release-error", message = "Sunucuya ulaşılamadı. İnternet bağlantını kontrol edip tekrar dene." });
            }
            catch (Exception ex)
            {
                Send(new { type = "release-error", message = "Sürüm bilgisi okunamadı: " + ex.Message });
            }
        }

        private static async Task InstallAsync(InstallOptions options)
        {
            _installing = true;
            try
            {
                if (_latest == null)
                {
                    using var http = Releases.CreateHttpClient(TimeSpan.FromSeconds(30));
                    _latest = await Releases.GetLatestAsync(http);
                }
                var engine = new InstallEngine(status => Send(new { type = "status", status.Step, status.Title, status.Detail, status.Progress, status.Version }));
                string dir = await engine.RunAsync(options, _latest, CancellationToken.None);
                Send(new { type = "installed", dir });
            }
            catch (Exception ex)
            {
                Send(new { type = "error", message = ex.Message });
            }
            finally
            {
                _installing = false;
            }
        }

        private static void Send(object payload)
        {
            try { _window.SendWebMessage(JsonSerializer.Serialize(payload, Json)); }
            catch { /* pencere kapanırken gelen geç mesajlar yok sayılır */ }
        }

        // ── Sessiz kurulum ────────────────────────────────────────────────
        private static int RunSilent(string? dir, bool desktop, bool startMenu)
        {
            try
            {
                using var http = Releases.CreateHttpClient(TimeSpan.FromSeconds(30));
                var latest = Releases.GetLatestAsync(http).GetAwaiter().GetResult();
                var engine = new InstallEngine(_ => { });
                engine.RunAsync(new InstallOptions(dir ?? InstallInfo.InstalledDirectory() ?? InstallInfo.DefaultDirectory, desktop, startMenu),
                    latest, CancellationToken.None).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        // ── Arayüz dosyaları ──────────────────────────────────────────────
        /// <summary>Gömülü arayüzü geçici klasöre açar (tek dosyalık exe'de diskte başka dosya yok).</summary>
        private static string ExtractUi()
        {
            string dir = Path.Combine(Path.GetTempPath(), "vulu-setup-ui-" + Environment.ProcessId);
            Directory.CreateDirectory(dir);
            var assembly = Assembly.GetExecutingAssembly();
            foreach (string name in assembly.GetManifestResourceNames())
            {
                string file = name.StartsWith("ui/", StringComparison.Ordinal) ? name[3..] : name;
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var output = File.Create(Path.Combine(dir, file));
                stream.CopyTo(output);
            }
            return dir;
        }

        // ── WebView2 ──────────────────────────────────────────────────────
        private static bool WebView2Installed() =>
            HasVersion(Registry.LocalMachine, $@"SOFTWARE\WOW6432Node\{WebView2Key}")
            || HasVersion(Registry.LocalMachine, $@"SOFTWARE\{WebView2Key}")
            || HasVersion(Registry.CurrentUser, $@"Software\{WebView2Key}");

        private static bool HasVersion(RegistryKey root, string path)
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue("pv") is string version && version.Length > 0 && version != "0.0.0.0";
        }

        /// <summary>WebView2 yoksa izin isteyip Microsoft'un resmi kurucusunu indirir ve çalıştırır.</summary>
        private static bool InstallWebView2()
        {
            const uint YesNo = 0x4, Question = 0x20, Error = 0x10;
            int answer = MessageBox(IntPtr.Zero,
                "vulu Launcher için Microsoft Edge WebView2 Runtime gerekli, ancak bu bilgisayarda kurulu değil.\n\n" +
                "Şimdi Microsoft'tan indirilip kurulsun mu? (Ücretsiz, yaklaşık 2 MB indirme)",
                "vulu Launcher Kurulumu", YesNo | Question);
            if (answer != 6) return false; // IDYES

            try
            {
                string setup = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebview2Setup.exe");
                using (var http = Releases.CreateHttpClient(TimeSpan.FromMinutes(5)))
                using (var stream = http.GetStreamAsync(WebView2Bootstrapper).GetAwaiter().GetResult())
                using (var file = File.Create(setup))
                    stream.CopyTo(file);

                using var process = Process.Start(new ProcessStartInfo(setup, "/silent /install") { UseShellExecute = true })!;
                process.WaitForExit();
                if (WebView2Installed()) return true;
            }
            catch { }

            MessageBox(IntPtr.Zero, "WebView2 kurulamadı. Şu adresten elle kurup tekrar dene:\n" + WebView2Bootstrapper,
                "vulu Launcher Kurulumu", Error);
            return false;
        }

        private static string? ArgValue(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static void RoundCorners(IntPtr hwnd)
        {
            try
            {
                int preference = 2; // DWMWCP_ROUND
                _ = DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
