using System.IO.Compression;

namespace VuluLauncher
{
    /// <summary>
    /// Dışarıdan gelen zip'leri (modpack, vulu paketi, arkadaşın gönderdiği paket) açmadan önce denetler: içindekilerin
    /// açılmış toplam boyutu ve dosya sayısı sınırı aşarsa açılmaz. Küçük bir zip'in açılınca diski doldurmasını
    /// ("zip bombası") önler. Yol denetimi ayrıca Downloader.ResolveInside ile yapılır.
    /// </summary>
    internal static class ZipGuard
    {
        public const long MaxBytes = 4L * 1024 * 1024 * 1024;
        public const int MaxEntries = 100_000;

        public static void Check(ZipArchive zip, long maxBytes = MaxBytes)
        {
            if (zip.Entries.Count > MaxEntries)
                throw new InvalidDataException(L.T("Paketin içinde çok fazla dosya var; bozuk ya da zararlı olabilir, açılmadı."));
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (total > maxBytes)
                    throw new InvalidDataException(L.T("Paket açılınca çok büyük (4 GB'tan fazla); bozuk ya da zararlı olabilir, açılmadı."));
            }
        }
    }
}
