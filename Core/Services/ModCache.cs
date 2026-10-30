using System.Runtime.InteropServices;

namespace VuluLauncher
{
    /// <summary>
    /// Mod önbelleği: CurseForge ve Modrinth dosyaları hash'leriyle değişmez olduğu için bir kez indirilen dosya başka
    /// instance'lara yeniden indirilmeden konur. Önbellek dosyaları instance'lardakilere sabit bağlantıdır (hard link):
    /// aynı veri diskte iki kez yer kaplamaz. Yalnızca Windows'ta ve aynı sürücüde; olmazsa normal indirilir.
    /// Bir instance'ta hâlâ duran dosya önbellekte ek yer kaplamaz; yalnızca hiçbir instance'ın kullanmadığı (silinmiş ya
    /// da güncellenmiş paketlerden kalan) "yetim" dosyalar yer tutar. Yetimler 14 gün sonra, toplamları 500 MB'ı geçerse
    /// daha önce silinir.
    /// </summary>
    internal static class ModCache
    {
        private const long MaxOrphanBytes = 500L * 1024 * 1024;
        private static readonly TimeSpan OrphanLifetime = TimeSpan.FromDays(14);
        // Yetim olduğu fark edilen dosya bu öznitelikle işaretlenir ve o andan itibaren süresi işler
        private const FileAttributes OrphanMark = FileAttributes.NotContentIndexed;
        // vulu API: paket aynasındaki dosyalar da CDN'dekilerin aynısıdır
        private static readonly string[] Hosts = { "cdn.modrinth.com", "edge.forgecdn.net", "mediafilez.forgecdn.net", new Uri(ApiConfig.BaseUrl).Host };
        private static int _pruning;

        private static string Root => Path.Combine(AppPaths.Base, "cache", "mods");

        /// <summary>Bu indirme önbelleğe uygun mu: mod dağıtım sunucusundan ve hash'i biliniyor.</summary>
        public static string? KeyFor(Uri uri, string? sha1, string? sha512)
        {
            if (!OperatingSystem.IsWindows() || !Hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) return null;
            string? hash = !string.IsNullOrEmpty(sha1) ? sha1 : sha512;
            return hash != null && hash.All(Uri.IsHexDigit) && hash.Length is 40 or 128 ? hash.ToLowerInvariant() : null;
        }

        /// <summary>Önbellekteki dosyayı hedefe bağlar; yoksa ya da bağlanamazsa false.</summary>
        public static bool TryPlace(string key, string path)
        {
            try
            {
                string cached = Path.Combine(Root, key);
                if (!File.Exists(cached) || new FileInfo(cached).Length == 0) return false;
                // Bozulmuş önbellek dosyası her yere yayılmasın: hash tutmuyorsa silinir, dosya yeniden iner
                if (!HashMatches(cached, key)) { File.Delete(cached); return false; }
                if (File.Exists(path)) File.Delete(path);
                if (!CreateHardLink(path, cached, IntPtr.Zero)) return false;
                var info = new FileInfo(cached);
                if (info.Attributes.HasFlag(OrphanMark)) info.Attributes &= ~OrphanMark;   // yeniden kullanılıyor
                File.SetLastWriteTimeUtc(cached, DateTime.UtcNow);   // en son kullanılan; temizlikte en son silinir
                return true;
            }
            catch { return false; }
        }

        /// <summary>İndirilen (hash'i doğrulanmış) dosyayı önbelleğe bağlar.</summary>
        public static void Remember(string key, string path)
        {
            try
            {
                Directory.CreateDirectory(Root);
                string cached = Path.Combine(Root, key);
                if (File.Exists(cached)) return;
                if (!CreateHardLink(cached, path, IntPtr.Zero)) return;   // başka sürücü: önbelleğe alınmaz
                PruneInBackground();
            }
            catch { }
        }

        /// <summary>Süresi dolan yetimleri arka planda siler (açılışta ve her yeni dosyada).</summary>
        public static void PruneInBackground()
        {
            if (OperatingSystem.IsWindows() && Interlocked.Exchange(ref _pruning, 1) == 0) _ = Task.Run(() => Prune(clearAll: false));
        }

        /// <summary>Hiçbir instance'ın kullanmadığı bütün önbellek dosyalarını siler; silinen bayt sayısı.</summary>
        public static long ClearOrphans() => OperatingSystem.IsWindows() ? Prune(clearAll: true) : 0;

        /// <summary>Önbellekte instance'larla paylaşılan (ek yer tutmayan) ve yetim (yer tutan) dosyaların boyutu.</summary>
        public static (long Shared, long Orphan) Measure()
        {
            long shared = 0, orphan = 0;
            if (!OperatingSystem.IsWindows() || !Directory.Exists(Root)) return (0, 0);
            foreach (var file in new DirectoryInfo(Root).EnumerateFiles())
            {
                try { if (LinkCount(file.FullName) > 1) shared += file.Length; else orphan += file.Length; }
                catch { }
            }
            return (shared, orphan);
        }

        private static bool HashMatches(string path, string key)
        {
            using var stream = File.OpenRead(path);
            byte[] hash = key.Length == 40 ? System.Security.Cryptography.SHA1.HashData(stream) : System.Security.Cryptography.SHA512.HashData(stream);
            return string.Equals(Convert.ToHexString(hash), key, StringComparison.OrdinalIgnoreCase);
        }

        private static long Prune(bool clearAll)
        {
            long freed = 0;
            try
            {
                if (!Directory.Exists(Root)) return 0;
                long kept = 0;
                var now = DateTime.UtcNow;
                foreach (var file in new DirectoryInfo(Root).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc))
                {
                    try
                    {
                        if (LinkCount(file.FullName) > 1) continue;   // bir instance'ta duruyor: ek yer kaplamıyor
                        bool expired = file.Attributes.HasFlag(OrphanMark) && now - file.LastWriteTimeUtc > OrphanLifetime;
                        if (clearAll || expired || kept + file.Length > MaxOrphanBytes)
                        {
                            long size = file.Length;
                            file.Delete();
                            freed += size;
                            continue;
                        }
                        kept += file.Length;
                        if (!file.Attributes.HasFlag(OrphanMark))
                        {
                            file.Attributes |= OrphanMark;
                            file.LastWriteTimeUtc = now;   // yetim kalma süresi şimdi başlar
                        }
                    }
                    catch { }
                }
            }
            catch { }
            finally { if (!clearAll) Interlocked.Exchange(ref _pruning, 0); }
            return freed;
        }

        /// <summary>Dosyanın kaç sabit bağlantısı var: 1 ise yalnızca önbellekte duruyor.</summary>
        private static uint LinkCount(string path)
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(handle, out var info) ? info.NumberOfLinks : 2;   // bilinmiyorsa dokunma
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct FileInformation
        {
            public uint Attributes;
            public long CreationTime, LastAccessTime, LastWriteTime;
            public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, out FileInformation info);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
    }
}
