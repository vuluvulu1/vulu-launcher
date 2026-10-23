using System.Runtime.InteropServices;

namespace VuluLauncher
{
    /// <summary>
    /// Mod önbelleği: CurseForge ve Modrinth dosyaları hash'leriyle değişmez olduğu için bir kez indirilen dosya başka
    /// instance'lara yeniden indirilmeden konur. Önbellek dosyaları instance'lardakilere sabit bağlantıdır (hard link):
    /// aynı veri diskte iki kez yer kaplamaz. Yalnızca Windows'ta ve aynı sürücüde; olmazsa normal indirilir.
    /// Önbellek 5 GB'ı geçince en eski kullanılanlar silinir (instance'lardaki dosyalar yerinde kalır).
    /// </summary>
    internal static class ModCache
    {
        private const long MaxBytes = 5L * 1024 * 1024 * 1024;
        private static readonly string[] Hosts = { "cdn.modrinth.com", "edge.forgecdn.net", "mediafilez.forgecdn.net" };
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
                if (Interlocked.Exchange(ref _pruning, 1) == 0) _ = Task.Run(Prune);
            }
            catch { }
        }

        private static bool HashMatches(string path, string key)
        {
            using var stream = File.OpenRead(path);
            byte[] hash = key.Length == 40 ? System.Security.Cryptography.SHA1.HashData(stream) : System.Security.Cryptography.SHA512.HashData(stream);
            return string.Equals(Convert.ToHexString(hash), key, StringComparison.OrdinalIgnoreCase);
        }

        private static void Prune()
        {
            try
            {
                var files = new DirectoryInfo(Root).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                long total = 0;
                foreach (var file in files)
                {
                    total += file.Length;
                    if (total > MaxBytes) file.Delete();
                }
            }
            catch { }
            finally { Interlocked.Exchange(ref _pruning, 0); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
    }
}
