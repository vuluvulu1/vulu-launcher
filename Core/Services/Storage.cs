namespace VuluLauncher
{
    /// <summary>
    /// Launcher'ın diskte kapladığı yer: açılışta kendiliğinden yapılan temizlik ve Ayarlar'daki Depolama bölümü.
    /// Yalnızca yeniden üretilebilen dosyalara (önbellekler, çökme dökümleri, eski güncelleme yedekleri) dokunulur;
    /// instance'lar, dünyalar ve ayarlar silinmez.
    /// </summary>
    internal static class Storage
    {
        /// <summary>Arayüzün tarayıcı önbelleği bu boyutla sınırlanır (WebView2 varsayılanı sınırsıza yakındır).</summary>
        public const long BrowserCacheBytes = 50L * 1024 * 1024;

        private static string WebView => Path.Combine(AppPaths.BrowserData, "EBWebView");
        private static string UpdaterWebView => Path.Combine(AppPaths.AppData, "updater-browser", "EBWebView");
        private static string ShotCache => Path.Combine(AppPaths.AppData, "shot-cache");
        // WebView2 çalışırken önbellek dosyaları kilitlidir; silme isteği bir sonraki açılışa bırakılır
        private static string ClearBrowserFlag => Path.Combine(AppPaths.AppData, "clear-browser-cache");

        // Tarayıcının yeniden üretebildiği klasörler (oturum, çerez ve yerel depolama bunların dışında kalır)
        private static readonly string[] BrowserCaches = { "Cache", "Code Cache", "GPUCache", "DawnGraphiteCache", "DawnWebGPUCache" };

        /// <summary>Tarayıcıya verilecek ek argüman: disk önbelleği sınırı.</summary>
        public static string BrowserArguments => $"--disk-cache-size={BrowserCacheBytes}";

        /// <summary>Pencere (WebView2) açılmadan önce: bekleyen tarayıcı temizliği, sonra arka planda kalan temizlik.</summary>
        public static void CleanUpAtStartup()
        {
            if (File.Exists(ClearBrowserFlag))
            {
                ClearBrowserCaches();
                try { File.Delete(ClearBrowserFlag); } catch { }
            }
            _ = Task.Run(() =>
            {
                ModCache.PruneInBackground();
                // WebView2 çökme dökümleri hiçbir yere gönderilmez, yalnızca birikir
                DeleteContents(Path.Combine(WebView, "Crashpad", "reports"));
                DeleteContents(Path.Combine(UpdaterWebView, "Crashpad", "reports"));
                // Updater değiştirdiği dosyaları buraya taşır; launcher açıldığına göre güncelleme bitmiştir
                DeleteContents(Path.Combine(AppPaths.Base, ".update", "old"));
            });
        }

        /// <summary>Ayarlar'da gösterilen boyutlar (bayt).</summary>
        public static object Usage()
        {
            var (shared, orphan) = ModCache.Measure();
            long browser = SizeOf(WebView) + SizeOf(UpdaterWebView);
            long shots = SizeOf(ShotCache);
            return new
            {
                modCache = orphan,
                modCacheShared = shared,
                browser,
                shots,
                cache = orphan + browser + shots,
                game = SizeOf(AppPaths.GameCore),
                java = SizeOf(JavaManager.JavaRoot),
                instances = SizeOf(AppPaths.Instances),
                browserPending = File.Exists(ClearBrowserFlag)
            };
        }

        /// <summary>Önbellekleri temizler. Tarayıcı önbelleği launcher bir sonraki açılışında silinir.</summary>
        public static object Clear()
        {
            long freed = ModCache.ClearOrphans();
            freed += DeleteContents(ShotCache);
            freed += DeleteContents(Path.Combine(WebView, "Crashpad", "reports"));
            freed += DeleteContents(Path.Combine(UpdaterWebView, "Crashpad", "reports"));
            // Güncelleyicinin tarayıcısı şu an çalışmıyor; hemen silinebilir
            foreach (string name in BrowserCaches) freed += DeleteContents(Path.Combine(UpdaterWebView, "Default", name));
            try
            {
                Directory.CreateDirectory(AppPaths.AppData);
                File.WriteAllText(ClearBrowserFlag, "");
            }
            catch { }
            return new { freed, browserPending = true };
        }

        private static void ClearBrowserCaches()
        {
            foreach (string name in BrowserCaches) DeleteContents(Path.Combine(WebView, "Default", name));
        }

        private static long SizeOf(string directory)
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(directory)) return 0;
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", options)) total += file.Length;
            }
            catch { }
            return total;
        }

        /// <summary>Klasörün içini siler (kilitli dosyalar atlanır); silinen bayt sayısı.</summary>
        private static long DeleteContents(string directory)
        {
            long freed = 0;
            if (!Directory.Exists(directory)) return 0;
            try
            {
                foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { long size = file.Length; file.Delete(); freed += size; }
                    catch { }
                }
                foreach (var sub in Directory.GetDirectories(directory))
                    try { Directory.Delete(sub, recursive: true); } catch { }
            }
            catch { }
            return freed;
        }
    }
}
