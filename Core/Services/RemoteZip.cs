using System.Net.Http.Headers;
using System.Text;
using VuluShared;

namespace VuluLauncher
{
    /// <summary>
    /// Uzaktaki bir zip'ten tek bir dosyayı, zip'in tamamını indirmeden okur: HTTP Range ile önce sondaki dizin,
    /// sonra yalnızca istenen dosya alınır (<see cref="RangeZip"/>). Modpack'lerin manifest'ini (mod listesi) okumak
    /// için; yüzlerce MB'lık paketlerde bile birkaç istek ve birkaç KB tutar.
    /// </summary>
    internal static class RemoteZip
    {
        private const int MaxEntryBytes = 4 * 1024 * 1024;

        /// <summary>Zip'teki dosyanın metni; sunucu Range desteklemiyorsa ya da dosya yoksa null.</summary>
        public static async Task<string?> ReadTextAsync(string url, string entryName, CancellationToken cancellation = default)
        {
            long length;
            using (var head = new HttpRequestMessage(HttpMethod.Get, url))
            {
                head.Headers.Range = new RangeHeaderValue(0, 0);
                using var response = await Downloader.Http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cancellation);
                if (response.StatusCode != System.Net.HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.Length is not { } total)
                    return null;
                length = total;
            }

            var zip = await RangeZip.OpenAsync((offset, count, token) => RangeZip.ReadHttpRangeAsync(Downloader.Http, url, offset, count, length, token), length, cancellation);
            if (!zip.Entries.TryGetValue(RangeZip.Normalize(entryName), out var entry) || entry.Size > MaxEntryBytes) return null;
            using var data = new MemoryStream();
            await zip.ExtractToAsync(entry, data, cancellation);
            return Encoding.UTF8.GetString(data.ToArray());
        }
    }
}
