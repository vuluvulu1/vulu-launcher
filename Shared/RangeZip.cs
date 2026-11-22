using System.IO.Compression;
using System.Text;

namespace VuluShared
{
    /// <summary>
    /// Sunucudaki zip'in içinden yalnızca istenen dosyaları indirir (HTTP Range): önce zip'in sonundaki içindekiler
    /// listesi, sonra her dosyanın kendi bayt aralığı okunur. Güncellemede yalnızca değişen dosyalar iner.
    /// Okuma işlevi dışarıdan verilir (testte bellekteki zip, Updater'da HTTP). ZIP64 desteklenmez: launcher'ın zip'i
    /// 4 GB'ın ve 65535 dosyanın çok altında; böyle bir zip gelirse çağıran tüm zip'i indirmeye döner.
    /// Dosyaların doğruluğunu bu sınıf değil, çağıran imzalı manifest'teki SHA-256 ile denetler.
    /// </summary>
    public sealed class RangeZip
    {
        /// <summary>Verilen konumdan verilen kadar baytı okur (sonda daha az dönebilir).</summary>
        public delegate Task<byte[]> RangeReader(long offset, int count, CancellationToken token);

        public sealed record Entry(string Name, int Method, long CompressedSize, long Size, long LocalHeaderOffset);

        private readonly RangeReader _read;
        private readonly long _length;
        public IReadOnlyDictionary<string, Entry> Entries { get; }

        private RangeZip(RangeReader read, long length, Dictionary<string, Entry> entries)
        {
            _read = read;
            _length = length;
            Entries = entries;
        }

        public static string Normalize(string name) => name.Replace('\\', '/').TrimStart('/');

        public static async Task<RangeZip> OpenAsync(RangeReader read, long length, CancellationToken token)
        {
            // Sondaki kayıt (EOCD) en fazla 65535 baytlık yorumla birlikte sondadır
            int tailSize = (int)Math.Min(length, 22 + 65535);
            byte[] tail = await read(length - tailSize, tailSize, token);
            int eocd = -1;
            for (int i = tail.Length - 22; i >= 0; i--)
                if (U32(tail, i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0) throw new InvalidDataException("Zip'in içindekiler listesi bulunamadı.");

            long cdSize = U32(tail, eocd + 12), cdOffset = U32(tail, eocd + 16);
            int count = U16(tail, eocd + 10);
            if (cdOffset == 0xFFFFFFFF || cdSize == 0xFFFFFFFF || count == 0xFFFF) throw new NotSupportedException("ZIP64 desteklenmiyor.");
            if (cdOffset + cdSize > length || cdSize > 64 * 1024 * 1024) throw new InvalidDataException("Zip'in içindekiler listesi geçersiz.");

            byte[] cd = await read(cdOffset, (int)cdSize, token);
            if (cd.Length < cdSize) throw new InvalidDataException("Zip'in içindekiler listesi eksik okundu.");
            var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            int p = 0;
            for (int n = 0; n < count; n++)
            {
                if (p + 46 > cd.Length || U32(cd, p) != 0x02014b50) throw new InvalidDataException("Zip'in içindekiler listesi bozuk.");
                int flags = U16(cd, p + 8), method = U16(cd, p + 10);
                long compressed = U32(cd, p + 20), size = U32(cd, p + 24);
                int nameLen = U16(cd, p + 28), extraLen = U16(cd, p + 30), commentLen = U16(cd, p + 32);
                long local = U32(cd, p + 42);
                if (p + 46 + nameLen > cd.Length) throw new InvalidDataException("Zip'in içindekiler listesi bozuk.");
                var encoding = (flags & 0x800) != 0 ? Encoding.UTF8 : Encoding.Latin1;
                string name = Normalize(encoding.GetString(cd, p + 46, nameLen));
                if (compressed == 0xFFFFFFFF || size == 0xFFFFFFFF || local == 0xFFFFFFFF) throw new NotSupportedException("ZIP64 desteklenmiyor.");
                if (!name.EndsWith('/')) entries[name] = new Entry(name, method, compressed, size, local);
                p += 46 + nameLen + extraLen + commentLen;
            }
            return new RangeZip(read, length, entries);
        }

        /// <summary>Dosyayı indirip açar ve hedefe yazar.</summary>
        public async Task ExtractAsync(Entry entry, string targetPath, CancellationToken token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await using var file = File.Create(targetPath);
            await ExtractToAsync(entry, file, token);
        }

        /// <summary>Dosyayı indirip açar ve akışa yazar.</summary>
        public async Task ExtractToAsync(Entry entry, Stream output, CancellationToken token)
        {
            if (entry.Method is not (0 or 8)) throw new NotSupportedException($"Desteklenmeyen sıkıştırma: {entry.Method}");
            // Yerel başlık + ad + veri; yerel ek alan merkezdekinden farklı olabildiği için biraz pay bırakılır
            int guess = (int)Math.Min(_length - entry.LocalHeaderOffset, 30 + 1024 + entry.Name.Length * 4L + entry.CompressedSize);
            byte[] block = await _read(entry.LocalHeaderOffset, guess, token);
            if (block.Length < 30 || U32(block, 0) != 0x04034b50) throw new InvalidDataException($"Zip'te {entry.Name} okunamadı.");
            int start = 30 + U16(block, 26) + U16(block, 28);
            long end = start + entry.CompressedSize;
            if (end > block.Length)
            {
                var rest = await _read(entry.LocalHeaderOffset + block.Length, (int)(end - block.Length), token);
                block = block.Concat(rest).ToArray();
                if (end > block.Length) throw new InvalidDataException($"Zip'te {entry.Name} eksik okundu.");
            }

            long before = output.CanSeek ? output.Position : 0;
            using var data = new MemoryStream(block, start, (int)entry.CompressedSize, writable: false);
            if (entry.Method == 0) await data.CopyToAsync(output, token);
            else
            {
                await using var inflate = new DeflateStream(data, CompressionMode.Decompress);
                await inflate.CopyToAsync(output, token);
            }
            if (output.CanSeek && output.Position - before != entry.Size) throw new InvalidDataException($"{entry.Name} beklenen boyutta değil.");
        }

        /// <summary>HTTP ile bir bayt aralığı; sunucu aralık yerine bütün dosyayı gönderirse (Range yok) hata.</summary>
        public static async Task<byte[]> ReadHttpRangeAsync(HttpClient http, string url, long offset, int count, long size, CancellationToken token)
        {
            if (count <= 0) return Array.Empty<byte>();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + count - 1);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                throw new NotSupportedException("Sunucu parça parça indirmeyi desteklemiyor.");
            if (response.Content.Headers.ContentRange is { HasLength: true } range && range.Length != size)
                throw new InvalidDataException("Sunucudaki zip beklenen boyutta değil.");
            return await response.Content.ReadAsByteArrayAsync(token);
        }

        private static uint U32(byte[] b, int i) => BitConverter.ToUInt32(b, i);
        private static int U16(byte[] b, int i) => BitConverter.ToUInt16(b, i);
    }
}
