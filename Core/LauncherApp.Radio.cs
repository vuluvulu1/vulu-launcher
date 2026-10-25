using System.Text.Json;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    internal sealed partial class LauncherApp
    {
        // ════════════════════════════════════════════════════════════════
        // RADYO
        // ════════════════════════════════════════════════════════════════
        // İnternet radyosu türleri (RadioService) ve adminlerin yüklediği "vulu Radyo" şarkıları. Şarkılar bir kez
        // indirilip önbellekten çalınır: tekrar dinlemede veri harcanmaz, oturum anahtarı ses adresine girmez.

        private const string VuluGenre = "vulu";
        private static readonly Regex SongId = new("^[0-9a-f]{24}$", RegexOptions.Compiled);
        private static readonly long SongCacheBytes = 500L * 1024 * 1024;
        private static string SongCache => Path.Combine(AppPaths.AppData, "cache", "radio");

        private readonly Dictionary<string, string> _songTypes = new();          // şarkı kimliği → içerik türü
        private readonly Dictionary<string, Task<string>> _songDownloads = new(); // aynı şarkı iki kez inmesin

        private void RegisterRadioCommands()
        {
            _bridge.On("radio.genres", _ => Task.FromResult<object?>(
                new[] { new { id = VuluGenre, label = "vulu Radyo" } }
                    .Concat(RadioService.Genres.Select(g => new { id = g.Key, label = g.Value.Label }))));
            _bridge.On("radio.stations", async args =>
            {
                string genre = args.Str("genre");
                if (genre != VuluGenre) return (object?)await _radio.StationsAsync(genre);
                RequireLogin();
                return (await SongsAsync()).Select(s => new
                {
                    id = s.GetProperty("id").GetString(), name = s.GetProperty("title").GetString(),
                    url = (string?)null, country = s.GetProperty("artist").GetString(), kind = "song",
                });
            });
            _bridge.On("radio.songFile", async args => (object?)new { url = LocalFiles.Url(await SongFileAsync(args.Str("id"))) });

            // Admin paneli → Radyo
            _bridge.On("radio.adminList", async _ => { RequireAdmin(); return (object?)await _api.GetAsync<JsonElement>("/radio/songs"); });
            _bridge.On("radio.upload", async _ => await UploadSongAsync());
            _bridge.On("radio.updateSong", async args =>
            {
                RequireAdmin();
                string id = RequireSongId(args.Str("id"));
                return (object?)await _api.PatchAsync<JsonElement>($"/radio/songs/{id}", new { title = args.Str("title"), artist = args.Str("artist") });
            });
            _bridge.On("radio.deleteSong", async args =>
            {
                RequireAdmin();
                string id = RequireSongId(args.Str("id"));
                var result = await _api.DeleteAsync<JsonElement>($"/radio/songs/{id}");
                foreach (var file in Directory.Exists(SongCache) ? Directory.GetFiles(SongCache, id + ".*") : Array.Empty<string>())
                    try { File.Delete(file); } catch { }
                return (object?)result;
            });
        }

        private static string RequireSongId(string id) =>
            SongId.IsMatch(id) ? id : throw new InvalidOperationException("Geçersiz şarkı.");

        private async Task<List<JsonElement>> SongsAsync()
        {
            var list = await _api.GetAsync<JsonElement>("/radio/songs");
            var songs = list.GetProperty("songs").EnumerateArray().ToList();
            lock (_songTypes)
                foreach (var s in songs) _songTypes[s.GetProperty("id").GetString()!] = s.GetProperty("contentType").GetString() ?? "audio/mpeg";
            return songs;
        }

        /// <summary>Şarkının önbellekteki dosyası; yoksa indirilir. Aynı anda gelen istekler tek indirmeyi bekler.</summary>
        private async Task<string> SongFileAsync(string id)
        {
            RequireLogin();
            RequireSongId(id);
            string type;
            lock (_songTypes) type = _songTypes.GetValueOrDefault(id, "audio/mpeg");
            string ext = type switch { "audio/ogg" => ".ogg", "audio/mp4" => ".m4a", _ => ".mp3" };
            string path = Path.Combine(SongCache, id + ext);
            if (File.Exists(path))
            {
                try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); } catch { }
                return path;
            }

            Task<string> download;
            lock (_songDownloads)
            {
                if (!_songDownloads.TryGetValue(id, out download!))
                {
                    download = DownloadSongAsync(id, path);
                    _songDownloads[id] = download;
                }
            }
            try { return await download; }
            finally { lock (_songDownloads) _songDownloads.Remove(id); }
        }

        private async Task<string> DownloadSongAsync(string id, string path)
        {
            await _api.DownloadAsync($"/radio/songs/{id}/file", path);
            PruneSongCache(keep: path);
            return path;
        }

        /// <summary>Önbellek 500 MB'ı geçerse en uzun süredir çalınmayan şarkılar silinir.</summary>
        private static void PruneSongCache(string keep)
        {
            try
            {
                var files = new DirectoryInfo(SongCache).GetFiles().Where(f => !f.Name.EndsWith(".part")).OrderBy(f => f.LastAccessTimeUtc).ToList();
                long total = files.Sum(f => f.Length);
                foreach (var file in files)
                {
                    if (total <= SongCacheBytes) break;
                    if (string.Equals(file.FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    total -= file.Length;
                    try { file.Delete(); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Admin bir ses dosyası seçer ve yükler. Dosya adı "Sanatçı - Şarkı" biçimindeyse ikiye ayrılır; değilse adın
        /// tamamı şarkı adı olur (sonra panelden düzenlenebilir).
        /// </summary>
        private async Task<object?> UploadSongAsync()
        {
            RequireAdmin();
            string? file = await _host.PickFileAsync("Şarkı seç", "Ses dosyaları", new[] { "mp3", "ogg", "m4a" });
            if (file == null) return new { uploaded = false };
            if (new FileInfo(file).Length > 30L * 1024 * 1024) throw new InvalidOperationException("Şarkı en fazla 30 MB olabilir.");

            string name = Path.GetFileNameWithoutExtension(file).Replace('_', ' ').Trim();
            string title = name, artist = "";
            int dash = name.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0) { artist = name[..dash].Trim(); title = name[(dash + 3)..].Trim(); }

            var song = await _api.UploadFileAsync<JsonElement>(
                $"/radio/songs?title={Uri.EscapeDataString(title)}&artist={Uri.EscapeDataString(artist)}", file);
            return new { uploaded = true, song };
        }
    }
}
