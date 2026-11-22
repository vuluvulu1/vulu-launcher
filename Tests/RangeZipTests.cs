using System.IO.Compression;
using VuluShared;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>Launcher güncellemesinde zip'in içinden yalnızca istenen dosyaları okuma (HTTP Range yerine bellekteki zip).</summary>
    public class RangeZipTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "vulu-remotezip-" + Guid.NewGuid().ToString("N"));

        public RangeZipTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private static byte[] BuildZip(Dictionary<string, byte[]> files, CompressionLevel level)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                foreach (var (name, data) in files)
                    using (var s = zip.CreateEntry(name, level).Open()) s.Write(data);
            return ms.ToArray();
        }

        [Theory]
        [InlineData(CompressionLevel.Optimal)]
        [InlineData(CompressionLevel.NoCompression)]
        public async Task ReadsOnlyRequestedFilesByRange(CompressionLevel level)
        {
            var random = new Random(7);
            var files = new Dictionary<string, byte[]>
            {
                ["vulu Launcher.dll"] = Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray(),
                ["runtimes/win-x64/native/av_libglesv2.dll"] = new byte[50_000].Select(_ => (byte)random.Next(256)).ToArray(),
                ["empty.txt"] = Array.Empty<byte>(),
            };
            byte[] zipBytes = BuildZip(files, level);
            int reads = 0;
            long readBytes = 0;
            RangeZip.RangeReader read = (offset, count, _) =>
            {
                reads++;
                int n = (int)Math.Min(count, zipBytes.Length - offset);
                readBytes += n;
                return Task.FromResult(zipBytes.AsSpan((int)offset, n).ToArray());
            };

            var zip = await RangeZip.OpenAsync(read, zipBytes.Length, CancellationToken.None);
            Assert.Equal(3, zip.Entries.Count);

            readBytes = 0;
            string target = Path.Combine(_dir, "runtimes", "native.dll");
            await zip.ExtractAsync(zip.Entries["runtimes/win-x64/native/av_libglesv2.dll"], target, CancellationToken.None);
            Assert.Equal(files["runtimes/win-x64/native/av_libglesv2.dll"], File.ReadAllBytes(target));
            var native = zip.Entries["runtimes/win-x64/native/av_libglesv2.dll"];
            Assert.True(readBytes <= native.CompressedSize + 30 + 1024 + native.Name.Length * 4, "yalnızca o dosyanın aralığı okunmalı");

            string empty = Path.Combine(_dir, "empty.txt");
            await zip.ExtractAsync(zip.Entries["empty.txt"], empty, CancellationToken.None);
            Assert.Empty(File.ReadAllBytes(empty));
        }

        [Fact]
        public async Task RejectsNonZip()
        {
            byte[] junk = new byte[1000];
            RangeZip.RangeReader read = (offset, count, _) => Task.FromResult(junk.AsSpan((int)offset, count).ToArray());
            await Assert.ThrowsAsync<InvalidDataException>(() => RangeZip.OpenAsync(read, junk.Length, CancellationToken.None));
        }
    }
}
