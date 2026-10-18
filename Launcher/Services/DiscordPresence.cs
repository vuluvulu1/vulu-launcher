using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VuluLauncher
{
    /// <summary>
    /// Discord'da "vulu Launcher oynuyor" durumu (Rich Presence). Discord istemcisinin yerel bağlantı noktasıyla
    /// (\\.\pipe\discord-ipc-N) konuşur; ek paket gerekmez. Discord kapalıysa sessizce bekler, açılınca bağlanır.
    /// Launcher kapanınca bağlantı kopar ve Discord durumu kendiliğinden kaldırır.
    /// </summary>
    internal sealed class DiscordPresence : IDisposable
    {
        /// <summary>Discord Developer Portal'daki "vulu Launcher" uygulamasının kimliği (gizli değildir).</summary>
        public const string ApplicationId = "1560770125177684048";

        private const string LogoUrl = "https://raw.githubusercontent.com/vuluvulu1/vulu-launcher/main/docs/logo-512.png";
        private const string DownloadUrl = "https://github.com/vuluvulu1/vulu-launcher/releases/latest";
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(20);

        private readonly object _gate = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly SemaphoreSlim _changed = new(0, 1);
        private JsonObject? _activity;           // gösterilmek istenen durum; null = boş
        private JsonObject? _browsing;           // launcher'daki son sayfa; oyun kapanınca buna dönülür
        private bool _playing;
        private bool _enabled;
        private readonly long _sessionStart = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Discord durum güncellemesini sınırlar (~20 sn'de 5); hızlı sayfa geçişleri birleştirilir
        private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(4);
        private Task? _loop;

        public void SetEnabled(bool enabled)
        {
            lock (_gate)
            {
                if (_enabled == enabled) return;
                _enabled = enabled;
                if (enabled && _loop == null && ApplicationId.Length > 0)
                    _loop = Task.Run(() => RunAsync(_stop.Token));
            }
            Signal();
        }

        /// <summary>Oyun kapandı: launcher'daki son sayfanın durumuna dönülür.</summary>
        public void ShowIdle()
        {
            JsonObject activity;
            lock (_gate)
            {
                _playing = false;
                activity = _browsing ?? Browsing("Menüde", null);
            }
            Set(activity);
        }

        /// <summary>
        /// Launcher'da hangi sayfada olunduğu (ve Oyna'da seçili instance). Oyun açıkken saklanır, oyun kapanınca gösterilir.
        /// </summary>
        public void ShowPage(string page, string? instanceName, string? mcVersion, string? loader)
        {
            var (details, state) = page switch
            {
                "home" => ("Ana menüde", instanceName == null ? null : Summary(instanceName, mcVersion, loader)),
                "packs" => ("Keşfet'te", "vulu Packs'e göz atıyor"),
                "modrinth" => ("Keşfet'te", "Modrinth'te mod arıyor"),
                "servers" => ("Sunuculara bakıyor", (string?)null),
                "shots" => ("Sosyal'de", "Ekran görüntülerine bakıyor"),
                "messages" or "friends" => ("Sosyal'de", null),
                "settings" => ("Ayarlarda", null),
                "profile" => ("Profillere bakıyor", null),
                "admin" => ("Yönetim panelinde", null),
                _ => ("Menüde", null),
            };
            var activity = Browsing(details, state);
            bool show;
            lock (_gate)
            {
                _browsing = activity;
                show = !_playing;
            }
            if (show) Set(activity.DeepClone().AsObject());
        }

        private JsonObject Browsing(string details, string? state)
        {
            var activity = new JsonObject
            {
                ["details"] = Trim(details),
                ["timestamps"] = new JsonObject { ["start"] = _sessionStart },   // launcher ne zamandır açık
                ["assets"] = Assets(null),
                ["buttons"] = Buttons(),
            };
            if (state != null) activity["state"] = Trim(state);
            return activity;
        }

        /// <summary>Durumun altındaki düğme (başkaları görür; Discord kendi durumunda göstermez).</summary>
        private static JsonArray Buttons() => new() { new JsonObject { ["label"] = "vulu Launcher'ı indir", ["url"] = DownloadUrl } };

        /// <summary>"Vanilla · 1.20.1" ya da "AGLR SMP · NeoForge 26.3" (adı tekrar etmeden).</summary>
        private static string Summary(string name, string? mcVersion, string? loader) =>
            string.IsNullOrEmpty(loader) || loader == "vanilla"
                ? (string.IsNullOrEmpty(mcVersion) ? name : $"{name} · {mcVersion}")
                : $"{name} · {Describe(mcVersion, loader)}";

        private static string LoaderName(string? loader) => loader switch
        {
            "fabric" => "Fabric", "forge" => "Forge", "neoforge" => "NeoForge", "quilt" => "Quilt", _ => "Vanilla",
        };

        /// <summary>"NeoForge 26.3"; modsuz oyunda "Minecraft 1.20.1".</summary>
        private static string Describe(string? mcVersion, string? loader)
        {
            string loaderName = string.IsNullOrEmpty(loader) || loader == "vanilla" ? "Minecraft" : LoaderName(loader);
            return string.IsNullOrEmpty(mcVersion) ? loaderName : $"{loaderName} {mcVersion}";
        }

        /// <summary>
        /// Oyun açık: instance adı, sürüm ve süre. vulu Packs'ten kurulan paketlerde büyük resim paketin logosu (yoksa kapağı) olur,
        /// köşede vulu logosu kalır.
        /// </summary>
        /// <param name="serverName">vulu listesindeki sunucunun adı; listede olmayan sunucunun adresi gösterilmez.</param>
        public void ShowPlaying(string instanceName, string mcVersion, string loader, string? packImageUrl = null, string? serverName = null, bool onServer = false)
        {
            lock (_gate) _playing = true;
            string where = serverName != null ? $"{serverName} sunucusunda" : onServer ? "Bir sunucuda" : "";
            Set(new JsonObject
            {
                ["details"] = Trim(instanceName),
                ["state"] = Trim(where.Length > 0 ? $"{where} · {Describe(mcVersion, loader)}" : Describe(mcVersion, loader)),
                ["buttons"] = Buttons(),
                ["timestamps"] = new JsonObject { ["start"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                ["assets"] = packImageUrl == null ? Assets($"Minecraft {mcVersion}") : new JsonObject
                {
                    ["large_image"] = packImageUrl,
                    ["large_text"] = Trim(instanceName),
                    ["small_image"] = LogoUrl,
                    ["small_text"] = "vulu Launcher",
                },
            });
        }

        private static JsonObject Assets(string? text) => new()
        {
            ["large_image"] = LogoUrl,
            ["large_text"] = text ?? "vulu Launcher",
        };

        // Discord alanları 2-128 karakter ister
        private static string Trim(string s) => s.Length > 128 ? s[..127] + "…" : s.PadRight(2);

        private void Set(JsonObject activity)
        {
            lock (_gate) _activity = activity;
            Signal();
        }

        private void Signal()
        {
            try { _changed.Release(); } catch (SemaphoreFullException) { }
        }

        // ── Bağlantı döngüsü ──────────────────────────────────────────────
        private async Task RunAsync(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                bool enabled;
                lock (_gate) enabled = _enabled;
                if (!enabled)
                {
                    await WaitAsync(Timeout.InfiniteTimeSpan, stop);
                    continue;
                }

                NamedPipeClientStream? pipe = await ConnectAsync(stop);
                if (pipe == null)
                {
                    await WaitAsync(RetryDelay, stop);
                    continue;
                }

                try
                {
                    using (pipe)
                    {
                        await WriteAsync(pipe, 0, new JsonObject { ["v"] = 1, ["client_id"] = ApplicationId }, stop);
                        var (op, _) = await ReadAsync(pipe, stop);    // READY
                        if (op != 1) throw new IOException("Discord el sıkışmayı reddetti.");

                        // Okuma döngüsü: ping'lere cevap verir, bağlantı koparsa biter
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop);
                        var reader = Task.Run(() => ReadLoopAsync(pipe, linked.Token));
                        var lastSent = DateTime.MinValue;
                        try
                        {
                            while (!stop.IsCancellationRequested && !reader.IsCompleted)
                            {
                                var wait = lastSent + MinInterval - DateTime.UtcNow;
                                if (wait > TimeSpan.Zero) await Task.Delay(wait, linked.Token);   // bu sürede gelen değişiklikler birleşir
                                lastSent = DateTime.UtcNow;
                                JsonObject? activity;
                                lock (_gate) { enabled = _enabled; activity = _activity; }
                                await SendActivityAsync(pipe, enabled ? activity : null, stop);
                                if (!enabled) break;
                                await Task.WhenAny(reader, WaitAsync(Timeout.InfiniteTimeSpan, linked.Token));   // bağlantı koparsa bekleyen de iptal olur
                            }
                        }
                        finally
                        {
                            linked.Cancel();
                        }
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                catch (Exception) { /* Discord kapandı ya da bağlantı koptu: biraz sonra yeniden denenir */ }

                await WaitAsync(RetryDelay, stop);
            }
        }

        private static async Task<NamedPipeClientStream?> ConnectAsync(CancellationToken stop)
        {
            for (int i = 0; i < 10; i++)
            {
                var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await pipe.ConnectAsync(300, stop);
                    return pipe;
                }
                catch (Exception) when (!stop.IsCancellationRequested)
                {
                    pipe.Dispose();
                }
            }
            return null;
        }

        private async Task WaitAsync(TimeSpan delay, CancellationToken stop)
        {
            try { await _changed.WaitAsync(delay, stop); }
            catch (OperationCanceledException) { }
        }

        private static async Task ReadLoopAsync(Stream pipe, CancellationToken stop)
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var (op, payload) = await ReadAsync(pipe, stop);
                    if (op == 3) await WriteRawAsync(pipe, 4, payload, stop);   // ping → pong
                    else if (op == 2) return;                                      // close
                }
            }
            catch (Exception) { }
        }

        private static Task SendActivityAsync(Stream pipe, JsonObject? activity, CancellationToken stop)
        {
            var args = new JsonObject { ["pid"] = Environment.ProcessId };
            if (activity != null) args["activity"] = activity.DeepClone();
            return WriteAsync(pipe, 1, new JsonObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = args,
                ["nonce"] = Guid.NewGuid().ToString("N"),
            }, stop);
        }

        // ── Çerçeve: opcode (int32 LE) + uzunluk (int32 LE) + JSON ─────────
        private static readonly SemaphoreSlim WriteGate = new(1, 1);

        private static Task WriteAsync(Stream pipe, int op, JsonNode body, CancellationToken stop) =>
            WriteRawAsync(pipe, op, Encoding.UTF8.GetBytes(body.ToJsonString()), stop);

        private static async Task WriteRawAsync(Stream pipe, int op, byte[] payload, CancellationToken stop)
        {
            var frame = new byte[8 + payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame, op);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), payload.Length);
            payload.CopyTo(frame, 8);
            await WriteGate.WaitAsync(stop);
            try
            {
                await pipe.WriteAsync(frame, stop);
                await pipe.FlushAsync(stop);
            }
            finally { WriteGate.Release(); }
        }

        private static async Task<(int Op, byte[] Payload)> ReadAsync(Stream pipe, CancellationToken stop)
        {
            var header = new byte[8];
            await pipe.ReadExactlyAsync(header, stop);
            int op = BinaryPrimitives.ReadInt32LittleEndian(header);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
            if (length is < 0 or > 1024 * 1024) throw new IOException("Discord'dan geçersiz çerçeve.");
            var payload = new byte[length];
            await pipe.ReadExactlyAsync(payload, stop);
            return (op, payload);
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _loop?.Wait(1000); } catch { }
        }
    }
}
