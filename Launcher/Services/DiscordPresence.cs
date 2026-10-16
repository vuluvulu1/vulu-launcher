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

        private const string LogoUrl = "https://raw.githubusercontent.com/vuluvulu1/vulu-launcher/main/Launcher/wwwroot/assets/logo.png";
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(20);

        private readonly object _gate = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly SemaphoreSlim _changed = new(0, 1);
        private JsonObject? _activity;           // gösterilmek istenen durum; null = boş
        private bool _enabled;
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

        /// <summary>Launcher'da geziniyor.</summary>
        public void ShowIdle() => Set(new JsonObject
        {
            ["details"] = "Launcher'da",
            ["assets"] = Assets(null),
        });

        /// <summary>Oyun açık: instance adı, sürüm ve süre.</summary>
        public void ShowPlaying(string instanceName, string mcVersion, string loader)
        {
            string loaderName = loader switch
            {
                "fabric" => "Fabric", "forge" => "Forge", "neoforge" => "NeoForge", "quilt" => "Quilt", _ => "Vanilla",
            };
            Set(new JsonObject
            {
                ["details"] = Trim($"{instanceName} oynuyor"),
                ["state"] = Trim($"Minecraft {mcVersion} · {loaderName}"),
                ["timestamps"] = new JsonObject { ["start"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                ["assets"] = Assets($"Minecraft {mcVersion}"),
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
                        try
                        {
                            while (!stop.IsCancellationRequested && !reader.IsCompleted)
                            {
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
