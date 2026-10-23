using System.IO.Compression;
using System.Net.Http.Headers;

namespace VuluLauncher
{
    /// <summary>
    /// Uzaktaki bir zip'ten tek bir dosyayı, zip'in tamamını indirmeden okur: HTTP Range ile önce sondaki dizin,
    /// sonra yalnızca istenen dosya alınır. Modpack'lerin manifest'ini (mod listesi) okumak için; yüzlerce MB'lık
    /// paketlerde bile birkaç istek ve birkaç KB tutar.
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

            await using var stream = new RangeStream(url, length);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = zip.GetEntry(entryName);
            if (entry == null || entry.Length > MaxEntryBytes) return null;
            using var reader = new StreamReader(entry.Open());
            return await reader.ReadToEndAsync(cancellation);
        }

        /// <summary>Konumlanabilen, salt okunur akış: okunan her parça Range isteğiyle (en az 64 KB) alınır.</summary>
        private sealed class RangeStream(string url, long length) : Stream
        {
            private const int Block = 64 * 1024;
            private long _position;
            private long _bufferStart = -1;
            private byte[] _buffer = Array.Empty<byte>();
            private int _requests;

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => length;
            public override long Position { get => _position; set => _position = value; }

            public override int Read(byte[] buffer, int offset, int count) =>
                ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

            public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellation = default)
            {
                if (_position >= length || destination.Length == 0) return 0;
                if (_bufferStart < 0 || _position < _bufferStart || _position >= _bufferStart + _buffer.Length)
                {
                    if (++_requests > 40) throw new IOException("Uzaktaki zip okunurken çok fazla istek gerekti.");
                    long end = Math.Min(length - 1, _position + Math.Max(Block, destination.Length) - 1);
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Range = new RangeHeaderValue(_position, end);
                    using var response = await Downloader.Http.SendAsync(request, cancellation);
                    if (response.StatusCode != System.Net.HttpStatusCode.PartialContent) throw new IOException("Sunucu parça okumayı desteklemiyor.");
                    _buffer = await response.Content.ReadAsByteArrayAsync(cancellation);
                    _bufferStart = _position;
                    if (_buffer.Length == 0) return 0;
                }
                int available = (int)Math.Min(destination.Length, _bufferStart + _buffer.Length - _position);
                _buffer.AsSpan((int)(_position - _bufferStart), available).CopyTo(destination.Span);
                _position += available;
                return available;
            }

            public override long Seek(long offset, SeekOrigin origin) => _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => length + offset,
            };

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
