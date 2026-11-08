using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    /// <summary>Oyunun açılış durumu: hangi aşamada, ne kadar sürdü, genelde ne kadar sürüyor.</summary>
    internal sealed record BootStatus(string Phase, long ElapsedMs, long ExpectedMs);

    /// <summary>
    /// Oyun süreci başladıktan sonra Minecraft'ın penceresi açılana kadar geçen süre (JVM, mod yükleyici, modlar) uzun
    /// olabilir. Bu sınıf o arayı izler: oyunun çıktısındaki işaretlerden aşamayı çıkarır, pencerenin açıldığını görür ve
    /// paket başına açılış süresini hatırlar; arayüz ilerlemeyi bir önceki süreye göre gösterir.
    /// </summary>
    internal sealed class GameBoot
    {
        private static readonly string TimesFile = Path.Combine(AppPaths.AppData, "boot-times.json");
        private static readonly object Gate = new();

        // Oyunun çıktısındaki işaretler → kullanıcıya gösterilen aşama
        private static readonly (Regex Pattern, Func<Match, string> Phase)[] Markers =
        {
            (new Regex(@"Loading (\d+) mods", RegexOptions.Compiled), m => $"Modlar yükleniyor ({m.Groups[1].Value} mod)"),
            (new Regex(@"ModLauncher running|Launching target|Loading Minecraft|Forge Mod Loader version", RegexOptions.Compiled), _ => "Mod yükleyici başlıyor"),
            (new Regex(@"Datafixer|Bootstrap|Mixin", RegexOptions.Compiled), _ => "Minecraft hazırlanıyor"),
            (new Regex(@"Setting user:|Backend library: LWJGL|Created window", RegexOptions.Compiled), _ => "Oyun penceresi açılıyor"),
        };

        private readonly Process _process;
        private readonly string _key;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private volatile string _phase = "Java başlatılıyor";
        private int _done;

        public GameBoot(Process process, string key)
        {
            _process = process;
            _key = key;
            ExpectedMs = Load(key);
        }

        /// <summary>Bu paketin bir önceki açılış süresi (ms); bilinmiyorsa 0.</summary>
        public long ExpectedMs { get; }

        public BootStatus Status => new(_phase, _clock.ElapsedMilliseconds, ExpectedMs);

        /// <summary>Oyunun bir çıktı satırı: aşamayı ilerletir (geri gitmez).</summary>
        public void OnOutput(string line)
        {
            if (_done == 1) return;
            for (int i = 0; i < Markers.Length; i++)
            {
                var m = Markers[i].Pattern.Match(line);
                if (!m.Success) continue;
                string phase = Markers[i].Phase(m);
                if (Rank(phase) >= Rank(_phase)) _phase = phase;
                return;
            }
        }

        private static int Rank(string phase) =>
            phase.StartsWith("Java", StringComparison.Ordinal) ? 0
            : phase.StartsWith("Mod yükleyici", StringComparison.Ordinal) ? 1
            : phase.StartsWith("Modlar", StringComparison.Ordinal) ? 2
            : phase.StartsWith("Minecraft", StringComparison.Ordinal) ? 3 : 4;

        /// <summary>
        /// Pencere açılana (ya da oyun kapanana) kadar bekler; arada <paramref name="progress"/> ile durumu bildirir.
        /// true: pencere açıldı.
        /// </summary>
        public async Task<bool> WaitForWindowAsync(Action<BootStatus> progress, CancellationToken token = default)
        {
            var lastReport = TimeSpan.Zero;
            while (!token.IsCancellationRequested && _clock.Elapsed < TimeSpan.FromMinutes(10))
            {
                try
                {
                    _process.Refresh();
                    if (_process.HasExited) break;
                    if (_process.MainWindowHandle != IntPtr.Zero)
                    {
                        Interlocked.Exchange(ref _done, 1);
                        Save(_key, _clock.ElapsedMilliseconds);
                        return true;
                    }
                }
                catch { break; }
                if (_clock.Elapsed - lastReport >= TimeSpan.FromMilliseconds(400))
                {
                    lastReport = _clock.Elapsed;
                    progress(Status);
                }
                await Task.Delay(150, token).ContinueWith(_ => { });
            }
            Interlocked.Exchange(ref _done, 1);
            return false;
        }

        private static long Load(string key)
        {
            try
            {
                lock (Gate)
                {
                    if (!File.Exists(TimesFile)) return 0;
                    var times = JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(TimesFile));
                    return times != null && times.TryGetValue(key, out long ms) ? ms : 0;
                }
            }
            catch { return 0; }
        }

        private static void Save(string key, long ms)
        {
            try
            {
                lock (Gate)
                {
                    var times = File.Exists(TimesFile)
                        ? JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(TimesFile)) ?? new()
                        : new Dictionary<string, long>();
                    // Ani sapmalar (ilk açılıştaki önbellek ısınması gibi) beklentiyi bozmasın: önceki süreyle harmanlanır
                    times[key] = times.TryGetValue(key, out long previous) && previous > 0 ? (previous + ms * 2) / 3 : ms;
                    Directory.CreateDirectory(Path.GetDirectoryName(TimesFile)!);
                    File.WriteAllText(TimesFile, JsonSerializer.Serialize(times));
                }
            }
            catch { /* hatırlanamazsa bir dahaki ilerleme belirsiz gösterilir */ }
        }
    }
}
