using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>Bir Minecraft sunucusunun anlık durumu (oyundaki sunucu listesinde görünen bilgiler).</summary>
    internal sealed record ServerStatus(bool Online, int Players, int MaxPlayers, string? Version, string? Motd, int PingMs, IReadOnlyList<string> Sample);

    /// <summary>
    /// Minecraft'ın sunucu listesi protokolü (Server List Ping): el sıkışma + durum isteği, JSON cevap.
    /// Oyuna giriş yapmaz; sunucu bunu oyundaki sunucu listesinin yenilenmesi gibi görür. Süre ve boyut sınırlıdır.
    /// SRV kaydı çözülmez; port verilmezse 25565 kullanılır.
    /// </summary>
    internal static class ServerPing
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);
        private const int MaxResponse = 512 * 1024;

        public static (string Host, int Port) Split(string address)
        {
            int colon = address.LastIndexOf(':');
            if (colon > 0 && int.TryParse(address[(colon + 1)..], out int port) && port is > 0 and < 65536)
                return (address[..colon], port);
            return (address, 25565);
        }

        public static async Task<ServerStatus> PingAsync(string address)
        {
            var (host, port) = Split(address);
            using var cancel = new CancellationTokenSource(Timeout);
            try
            {
                using var client = new TcpClient { NoDelay = true };
                await client.ConnectAsync(host, port, cancel.Token);
                var stream = client.GetStream();

                // El sıkışma: protokol -1 (yalnızca durum), adres, port, sonraki durum 1 (status)
                var hostBytes = Encoding.UTF8.GetBytes(host);
                using var handshake = new MemoryStream();
                WriteVarInt(handshake, 0x00);
                WriteVarInt(handshake, -1);
                WriteVarInt(handshake, hostBytes.Length);
                handshake.Write(hostBytes);
                handshake.WriteByte((byte)(port >> 8));
                handshake.WriteByte((byte)port);
                WriteVarInt(handshake, 1);
                await SendPacketAsync(stream, handshake.ToArray(), cancel.Token);

                var watch = Stopwatch.StartNew();
                await SendPacketAsync(stream, new byte[] { 0x00 }, cancel.Token);   // durum isteği

                int length = await ReadVarIntAsync(stream, cancel.Token);
                if (length is <= 0 or > MaxResponse) throw new IOException("Geçersiz cevap.");
                var body = new byte[length];
                await stream.ReadExactlyAsync(body, cancel.Token);
                int ping = (int)watch.ElapsedMilliseconds;

                int offset = 0;
                if (ReadVarInt(body, ref offset) != 0x00) throw new IOException("Beklenmeyen paket.");
                int jsonLength = ReadVarInt(body, ref offset);
                string json = Encoding.UTF8.GetString(body, offset, Math.Min(jsonLength, body.Length - offset));
                return Parse(json, ping);
            }
            catch (Exception)
            {
                return new ServerStatus(false, 0, 0, null, null, 0, Array.Empty<string>());
            }
        }

        private static ServerStatus Parse(string json, int ping)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int online = 0, max = 0;
            var sample = new List<string>();
            if (root.TryGetProperty("players", out var players))
            {
                if (players.TryGetProperty("online", out var o) && o.TryGetInt32(out int on)) online = on;
                if (players.TryGetProperty("max", out var m) && m.TryGetInt32(out int mx)) max = mx;
                if (players.TryGetProperty("sample", out var s) && s.ValueKind == JsonValueKind.Array)
                    foreach (var p in s.EnumerateArray().Take(12))
                        if (p.TryGetProperty("name", out var n) && n.GetString() is { } name) sample.Add(name);
            }
            string? version = root.TryGetProperty("version", out var v) && v.TryGetProperty("name", out var vn) && vn.GetString() is { } raw
                ? System.Text.RegularExpressions.Regex.Replace(raw, "§.", "").Trim() : null;
            string? motd = root.TryGetProperty("description", out var d) ? PlainText(d).Trim() : null;
            return new ServerStatus(true, online, max, version, string.IsNullOrEmpty(motd) ? null : motd, ping, sample);
        }

        /// <summary>Metin bileşenini (string ya da {text, extra[]}) düz metne çevirir; § renk kodları atılır.</summary>
        private static string PlainText(JsonElement e)
        {
            var sb = new StringBuilder();
            void Walk(JsonElement x)
            {
                if (x.ValueKind == JsonValueKind.String) sb.Append(x.GetString());
                else if (x.ValueKind == JsonValueKind.Object)
                {
                    if (x.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String) sb.Append(t.GetString());
                    if (x.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
                        foreach (var item in extra.EnumerateArray()) Walk(item);
                }
                else if (x.ValueKind == JsonValueKind.Array)
                    foreach (var item in x.EnumerateArray()) Walk(item);
            }
            Walk(e);
            string text = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "§.", "");
            return text.Length > 200 ? text[..200] : text;
        }

        // ── VarInt ve paket ───────────────────────────────────────────────
        private static async Task SendPacketAsync(NetworkStream stream, byte[] payload, CancellationToken cancel)
        {
            using var packet = new MemoryStream();
            WriteVarInt(packet, payload.Length);
            packet.Write(payload);
            await stream.WriteAsync(packet.ToArray(), cancel);
        }

        private static void WriteVarInt(Stream s, int value)
        {
            uint v = (uint)value;
            do
            {
                byte b = (byte)(v & 0x7F);
                v >>= 7;
                if (v != 0) b |= 0x80;
                s.WriteByte(b);
            } while (v != 0);
        }

        private static int ReadVarInt(byte[] data, ref int offset)
        {
            int value = 0, shift = 0;
            while (offset < data.Length && shift < 35)
            {
                byte b = data[offset++];
                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
                shift += 7;
            }
            throw new IOException("VarInt okunamadı.");
        }

        private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken cancel)
        {
            int value = 0, shift = 0;
            var one = new byte[1];
            while (shift < 35)
            {
                await stream.ReadExactlyAsync(one, cancel);
                value |= (one[0] & 0x7F) << shift;
                if ((one[0] & 0x80) == 0) return value;
                shift += 7;
            }
            throw new IOException("VarInt okunamadı.");
        }
    }
}
