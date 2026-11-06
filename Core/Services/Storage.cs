namespace VuluLauncher
{
    /// <summary>
    /// Launcher'ın diskte kapladığı yer: açılışta kendiliğinden yapılan temizlik ve Ayarlar'daki Depolama bölümü.
    /// Yalnızca yeniden üretilebilen dosyalara (önbellekler, çökme dökümleri, eski güncelleme yedekleri) dokunulur;
    /// instance'lar, dünyalar ve ayarlar silinmez.
    /// </summary>
    internal static class Storage
    {
        private static string ShotCache => Path.Combine(AppPaths.AppData, "shot-cache");

        /// <summary>
        /// Eski web arayüzünün (WebView2) ve eski güncelleyicinin tarayıcı verileri. Arayüz native olduktan sonra
        /// kullanılmıyor; açılışta bütünüyle silinir.
        /// </summary>
        private static readonly string[] OldBrowserData =
        {
            AppPaths.BrowserData,
            Path.Combine(AppPaths.AppData, "updater-browser"),
        };

        /// <summary>Açılışta arka planda: eski tarayıcı verileri, eski güncelleme yedekleri ve bayat önbellek.</summary>
        public static void CleanUpAtStartup()
        {
            _ = Task.Run(() =>
            {
                ModCache.PruneInBackground();
                RemoveOldBrowserData();
                // Updater değiştirdiği dosyaları buraya taşır; launcher açıldığına göre güncelleme bitmiştir
                DeleteContents(Path.Combine(AppPaths.Base, ".update", "old"));
            });
        }

        /// <summary>Ayarlar'da gösterilen boyutlar (bayt).</summary>
        public static object Usage()
        {
            var (shared, orphan) = ModCache.Measure();
            long browser = OldBrowserData.Sum(SizeOf);
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
                browserPending = false
            };
        }

        /// <summary>Önbellekleri temizler.</summary>
        public static object Clear()
        {
            long freed = ModCache.ClearOrphans();
            freed += DeleteContents(ShotCache);
            freed += RemoveOldBrowserData();
            return new { freed, browserPending = false };
        }

        private static long RemoveOldBrowserData()
        {
            long freed = 0;
            foreach (string directory in OldBrowserData)
            {
                freed += DeleteContents(directory);
                try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); } catch { }
            }
            return freed;
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
