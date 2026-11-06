using Avalonia;
using VuluShared;

namespace Installer
{
    /// <summary>
    /// vulu Launcher kurulum programı. En son sürümü vulu sunucusundan indirip kurar, kısayolları ve
    /// "Program ekle/kaldır" kaydını oluşturur. Yönetici izni gerekmez. Arayüz launcher'la aynı kütüphaneyle
    /// (Avalonia) kendisi çizilir; WebView2 gerekmez.
    ///
    /// Argümanlar (otomasyon/test için):
    ///   --silent              arayüz açmadan kurar
    ///   --dir &lt;klasör&gt;        kurulum klasörü (varsayılan: önceki kurulum ya da LocalAppData\Programs)
    ///   --no-desktop          masaüstü kısayolu oluşturma
    ///   --no-startmenu        Başlat menüsü kısayolu oluşturma
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string? dirArg = ArgValue(args, "--dir");
            if (args.Contains("--silent")) return RunSilent(dirArg, !args.Contains("--no-desktop"), !args.Contains("--no-startmenu"));

            App.Model = new SetupViewModel(dirArg ?? InstallInfo.InstalledDirectory());
            AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
            return 0;
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

        private static string? ArgValue(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
