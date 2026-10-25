using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace VuluLauncher
{
    /// <summary>
    /// Launcher'daki radyo: istasyonlar açık ve ücretsiz radio-browser.info dizininden türe göre alınır. Ses doğrudan
    /// istasyonun kendi yayın adresinden çalınır (launcher dosya indirmez, saklamaz). Liste birkaç saat bellekte tutulur.
    /// </summary>
    internal sealed class RadioService
    {
        /// <summary>Arayüzdeki türler: anahtar → (başlık, radio-browser araması).</summary>
        public static readonly IReadOnlyDictionary<string, (string Label, string Query)> Genres = new Dictionary<string, (string, string)>
        {
            ["lofi"] = ("Lo-fi", "tag=lofi"),
            ["chill"] = ("Chill", "tag=chillout"),
            ["game"] = ("Oyun müzikleri", "tag=video%20game%20music"),
            ["turkpop"] = ("Türkçe pop", "tag=pop&countrycode=TR"),
            ["rock"] = ("Rock", "tag=rock"),
            ["electronic"] = ("Elektronik", "tag=electronic"),
        };

        /// <summary>
        /// Türün başına sabitlenen istasyonlar (dizinde yoksa ya da sırası düşükse de görünsün diye). HLS (.m3u8)
        /// yayınları arayüzde hls.js ile çalınır.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, RadioStation[]> Pinned = new Dictionary<string, RadioStation[]>
        {
            ["turkpop"] = new[]
            {
                new RadioStation("pin-kralpop", "Kral Pop", "https://ssldyg.radyotvonline.com/smil/smil:kralpop.smil/playlist.m3u8", "Türkiye", null, 128),
                new RadioStation("pin-joyfm", "Joy FM", "https://playerservices.streamtheworld.com/api/livestream-redirect/JOY_FM128AAC.aac", "Türkiye", null, 128),
                new RadioStation("pin-palnostalji", "Pal Nostalji", "https://moondigitaledge.radyotvonline.net/palnostalji/playlist.m3u8", "Türkiye", null, 128),
            },
        };

        // Dizinin aynaları; biri çalışmazsa sıradaki denenir
        private static readonly string[] Mirrors = { "https://de1.api.radio-browser.info", "https://nl1.api.radio-browser.info", "https://at1.api.radio-browser.info", "https://fi1.api.radio-browser.info" };
        private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);

        private readonly Dictionary<string, (DateTime At, List<RadioStation> Stations)> _cache = new();
        private readonly object _gate = new();

        public sealed record RadioStation(string Id, string Name, string Url, string Country, string? Favicon, int Bitrate);

        public async Task<List<RadioStation>> StationsAsync(string genre)
        {
            if (!Genres.TryGetValue(genre, out var info)) throw new InvalidOperationException("Bilinmeyen tür.");
            lock (_gate)
                if (_cache.TryGetValue(genre, out var hit) && DateTime.UtcNow - hit.At < CacheFor) return hit.Stations;

            Exception? last = null;
            foreach (var mirror in Mirrors)
            {
                try
                {
                    string url = $"{mirror}/json/stations/search?{info.Query}&hidebroken=true&order=clickcount&reverse=true&limit=60";
                    var found = await Downloader.Http.GetFromJsonAsync<List<RbStation>>(url) ?? new();
                    var pinned = Pinned.GetValueOrDefault(genre, Array.Empty<RadioStation>());
                    var stations = found
                        .Where(s => !pinned.Any(p => SameName(p.Name, s.Name)))   // sabitlenenlerin kopyaları listede tekrar çıkmasın
                        .Where(s => !string.IsNullOrWhiteSpace(s.UrlResolved) && s.LastCheckOk == 1
                            && s.Codec is "MP3" or "AAC" or "AAC+" or "OGG"
                            && Uri.TryCreate(s.UrlResolved, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
                        // Aynı istasyonun kopyaları (aynı ad) bir kez; https olanlar önde
                        .GroupBy(s => s.Name.Trim().ToLowerInvariant())
                        .Select(g => g.OrderByDescending(s => s.UrlResolved!.StartsWith("https:", StringComparison.Ordinal)).First())
                        .Take(30)
                        .Select(s => new RadioStation(s.StationUuid, s.Name.Trim(), s.UrlResolved!, s.Country ?? "",
                            s.Favicon is { Length: > 0 } f && f.StartsWith("https://", StringComparison.Ordinal) ? f : null, s.Bitrate))
                        .ToList();
                    stations.InsertRange(0, pinned);
                    lock (_gate) _cache[genre] = (DateTime.UtcNow, stations);
                    return stations;
                }
                catch (Exception e) { last = e; }
            }
            // Dizine ulaşılamasa da sabitlenen istasyonlar çalınabilir
            if (Pinned.TryGetValue(genre, out var only)) return only.ToList();
            throw new InvalidOperationException("Radyo listesi alınamadı: " + (last?.Message ?? "bağlantı yok"));
        }

        /// <summary>"PAL Nostalji" ile "Pal Nostalji" aynı istasyon sayılır (büyük/küçük harf ve boşluklar önemsiz).</summary>
        private static bool SameName(string a, string b) =>
            string.Equals(string.Concat(a.Where(char.IsLetterOrDigit)), string.Concat(b.Where(char.IsLetterOrDigit)), StringComparison.OrdinalIgnoreCase);

        private sealed class RbStation
        {
            [JsonPropertyName("stationuuid")] public string StationUuid { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("url_resolved")] public string? UrlResolved { get; set; }
            [JsonPropertyName("country")] public string? Country { get; set; }
            [JsonPropertyName("favicon")] public string? Favicon { get; set; }
            [JsonPropertyName("codec")] public string? Codec { get; set; }
            [JsonPropertyName("bitrate")] public int Bitrate { get; set; }
            [JsonPropertyName("lastcheckok")] public int LastCheckOk { get; set; }
        }
    }
}
