using System.Reflection;
using Avalonia;

namespace Updater
{
    /// <summary>
    /// Discord tarzı güncelleyici. Kısayol launcher'ı açar; launcher (Release derlemede) önce bunu çalıştırır.
    /// Bu küçük pencere sürümü denetler, gerekiyorsa günceller ve launcher'ı "--from-updater" ile açar.
    /// Arayüz launcher'la aynı kütüphaneyle (Avalonia) kendisi çizilir; WebView2 gerekmez.
    ///
    /// Argümanlar:
    ///   --wait-pid &lt;pid&gt;   kurulumdan önce kapanması beklenecek süreç (launcher'dan başlatılınca)
    ///   --uninstall         "Program ekle/kaldır"dan çağrılır: güncelleme yerine kaldırma onayı gösterilir
    ///   --silent            --uninstall ile: soru sormadan kaldırır (--delete-data oyun dosyalarını da siler)
    /// Eski sürümün konumsal argümanları (indirme adresi, exe yolu, sha256) yok sayılır.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            string installDir = AppContext.BaseDirectory;
            bool uninstallMode = args.Contains(VuluShared.InstallInfo.UninstallSwitch, StringComparer.OrdinalIgnoreCase);

            if (uninstallMode && args.Contains("--silent", StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    Uninstaller.RunAsync(installDir, args.Contains("--delete-data", StringComparer.OrdinalIgnoreCase), _ => { })
                        .GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    // Sessiz kaldırmada hata görünmez; nedeni geçici klasöre yazılır
                    try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "vulu-uninstall.log"), ex.ToString()); } catch { }
                    Environment.ExitCode = 1;
                }
                return;
            }

            string version = VuluShared.Releases.Clean(Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "");
            App.Model = new UpdaterViewModel(installDir, ParseWaitPids(args), uninstallMode, version);

            try
            {
                AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
            }
            catch when (!uninstallMode)
            {
                // Arayüz açılamadıysa (ekran kartı sürücüsü vb.) güncellemeden launcher'a geçilir
                TryLaunch(installDir, ParseWaitPids(args));
            }
        }

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
    }
}
