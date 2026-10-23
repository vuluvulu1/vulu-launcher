using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Dosyaların SHA-1'i (Modrinth) ve CurseForge parmak izi (MurmurHash2, boşluk baytları atılarak). Sonuçlar dosya
    /// yolu, boyutu ve değişme zamanına göre diskte saklanır: yüzlerce modluk paketlerde yalnızca ilk sefer hesaplanır.
    /// </summary>
    internal static class FileHashes
    {
        internal sealed record Entry(long Size, long Time, string Sha1, uint Fingerprint);

        private static readonly string CachePath = Path.Combine(AppPaths.AppData, "cache", "filehashes.json");
        private static readonly ConcurrentDictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static int _loaded, _dirty;

        public static Entry Of(string path)
        {
            Load();
            var info = new FileInfo(path);
            long time = info.LastWriteTimeUtc.Ticks;
            if (Cache.TryGetValue(path, out var hit) && hit.Size == info.Length && hit.Time == time) return hit;

            string sha1;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                sha1 = Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
            var entry = new Entry(info.Length, time, sha1, CurseForgeFingerprint(path));
            Cache[path] = entry;
            Interlocked.Exchange(ref _dirty, 1);
            return entry;
        }

        /// <summary>CurseForge'un parmak izi: dosyadan 9, 10, 13 ve 32 baytları atılır, kalan MurmurHash2 (tohum 1) ile özetlenir.</summary>
        public static uint CurseForgeFingerprint(string path)
        {
            static bool Skip(byte b) => b is 9 or 10 or 13 or 32;
            byte[] buffer = new byte[1 << 16];

            uint length = 0;
            using (var stream = File.OpenRead(path))
                for (int read; (read = stream.Read(buffer, 0, buffer.Length)) > 0;)
                    for (int i = 0; i < read; i++) if (!Skip(buffer[i])) length++;

            const uint m = 0x5bd1e995;
            uint h = 1 ^ length, word = 0;
            int shift = 0;
            using (var stream = File.OpenRead(path))
                for (int read; (read = stream.Read(buffer, 0, buffer.Length)) > 0;)
                    for (int i = 0; i < read; i++)
                    {
                        byte b = buffer[i];
                        if (Skip(b)) continue;
                        word |= (uint)b << shift;
                        shift += 8;
                        if (shift != 32) continue;
                        uint k = word * m;
                        k ^= k >> 24;
                        k *= m;
                        h = h * m ^ k;
                        word = 0;
                        shift = 0;
                    }
            if (shift > 0)
            {
                h ^= word;
                h *= m;
            }
            h ^= h >> 13;
            h *= m;
            h ^= h >> 15;
            return h;
        }

        private static void Load()
        {
            if (Interlocked.Exchange(ref _loaded, 1) == 1) return;
            try
            {
                if (!File.Exists(CachePath)) return;
                var saved = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(CachePath));
                if (saved != null) foreach (var (key, value) in saved) Cache[key] = value;
            }
            catch { /* bozuk önbellek: yeniden hesaplanır */ }
        }

        /// <summary>Yeni hesaplananları diske yazar (çok sayıda dosya hesaplandıktan sonra bir kez çağrılır).</summary>
        public static void Save()
        {
            if (Interlocked.Exchange(ref _dirty, 0) == 0) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                // Silinmiş dosyaların kayıtları atılır
                var alive = Cache.Where(p => File.Exists(p.Key)).Take(20000).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                File.WriteAllText(CachePath, JsonSerializer.Serialize(alive));
            }
            catch { }
        }
    }
}
