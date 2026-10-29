using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>Bir Minecraft sunucusunun anlık durumu (oyundaki sunucu listesinde görünen bilgiler).</summary>
    internal sealed record ServerStatus(bool Online, int Players, int MaxPlayers, string? Version, string? Motd, int PingMs, IReadOnlyList<string> Sample)
    {
        /// <summary>MOTD'nin renkli parçaları (oyundaki sunucu listesindeki gibi çizmek için).</summary>
        public IReadOnlyList<MotdPart> MotdParts { get; init; } = Array.Empty<MotdPart>();
        /// <summary>Sunucu simgesi: 64x64 PNG, "data:image/png;base64,..." (yoksa null).</summary>
        public string? Favicon { get; init; }
    }

    /// <summary>MOTD'nin aynı biçimdeki bir parçası. Renk "#RRGGBB" ya da null (varsayılan gri).</summary>
    internal sealed record MotdPart(string Text, string? Color, bool Bold, bool Italic, bool Underlined, bool Strikethrough);

    /// <summary>
    /// Minecraft'ın sunucu listesi protokolü (Server List Ping): el sıkışma + durum isteği, JSON cevap.
    /// Oyuna giriş yapmaz; sunucu bunu oyundaki sunucu listesinin yenilenmesi gibi görür. Süre ve boyut sınırlıdır.
    /// SRV kaydı çözülmez; port verilmezse 25565 kullanılır.
    /// </summary>
    internal static class ServerPing
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);
        private const int MaxResponse = 512 * 1024, MaxFavicon = 64 * 1024, MaxMotdParts = 400, MaxMotdChars = 300;

        // Oyunun adlı renkleri; sırası § kodlarıyla (0-9, a-f) aynı
        private static readonly (string Name, string Hex)[] NamedColors =
        {
            ("black", "#000000"), ("dark_blue", "#0000AA"), ("dark_green", "#00AA00"), ("dark_aqua", "#00AAAA"),
            ("dark_red", "#AA0000"), ("dark_purple", "#AA00AA"), ("gold", "#FFAA00"), ("gray", "#AAAAAA"),
            ("dark_gray", "#555555"), ("blue", "#5555FF"), ("green", "#55FF55"), ("aqua", "#55FFFF"),
            ("red", "#FF5555"), ("light_purple", "#FF55FF"), ("yellow", "#FFFF55"), ("white", "#FFFFFF"),
        };

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
            string? favicon = root.TryGetProperty("favicon", out var f) && f.ValueKind == JsonValueKind.String && f.GetString() is { } icon
                && icon.StartsWith("data:image/png;base64,", StringComparison.Ordinal) && icon.Length <= MaxFavicon
                ? icon.Replace("\n", "") : null;
            return new ServerStatus(true, online, max, version, string.IsNullOrEmpty(motd) ? null : motd, ping, sample)
            {
                MotdParts = root.TryGetProperty("description", out var rich) ? RichText(rich) : Array.Empty<MotdPart>(),
                Favicon = favicon,
            };
        }

        private readonly record struct Style(string? Color, bool Bold, bool Italic, bool Underlined, bool Strikethrough);

        /// <summary>
        /// Metin bileşenini renkli parçalara çevirir: biçim (color, bold…) alt bileşenlere geçer, metnin içindeki § kodları da
        /// işlenir. MiniMOTD gibi eklentilerin gradyanları harf başına renkli parçalar olarak gelir; bitişik aynı biçimler birleşir.
        /// </summary>
        internal static IReadOnlyList<MotdPart> RichText(JsonElement root)
        {
            var parts = new List<MotdPart>();
            int chars = 0;
            void Emit(string text, Style st)
            {
                if (text.Length == 0 || chars >= MaxMotdChars || parts.Count >= MaxMotdParts) return;
                if (chars + text.Length > MaxMotdChars) text = text[..(MaxMotdChars - chars)];
                chars += text.Length;
                if (parts.Count > 0 && parts[^1] is var last && last.Color == st.Color && last.Bold == st.Bold && last.Italic == st.Italic
                    && last.Underlined == st.Underlined && last.Strikethrough == st.Strikethrough)
                    parts[^1] = last with { Text = last.Text + text };
                else parts.Add(new MotdPart(text, st.Color, st.Bold, st.Italic, st.Underlined, st.Strikethrough));
            }
            void Legacy(string text, Style st)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] != '§' || i + 1 >= text.Length) { sb.Append(text[i]); continue; }
                    Emit(sb.ToString(), st); sb.Clear();
                    char code = char.ToLowerInvariant(text[++i]);
                    int index = "0123456789abcdef".IndexOf(code);
                    if (index >= 0) st = new Style(NamedColors[index].Hex, false, false, false, false);
                    else st = code switch
                    {
                        'l' => st with { Bold = true }, 'o' => st with { Italic = true }, 'n' => st with { Underlined = true },
                        'm' => st with { Strikethrough = true }, 'r' => default, _ => st,
                    };
                }
                Emit(sb.ToString(), st);
            }
            void Walk(JsonElement x, Style st, int depth)
            {
                if (depth > 16) return;
                if (x.ValueKind == JsonValueKind.String) { Legacy(x.GetString() ?? "", st); return; }
                if (x.ValueKind == JsonValueKind.Array) { foreach (var item in x.EnumerateArray()) Walk(item, st, depth + 1); return; }
                if (x.ValueKind != JsonValueKind.Object) return;
                if (x.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String) st = st with { Color = ColorHex(c.GetString()!) ?? st.Color };
                bool Flag(string name, bool current) => x.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.True : current;
                st = st with
                {
                    Bold = Flag("bold", st.Bold), Italic = Flag("italic", st.Italic),
                    Underlined = Flag("underlined", st.Underlined), Strikethrough = Flag("strikethrough", st.Strikethrough),
                };
                if (x.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String) Legacy(t.GetString() ?? "", st);
                if (x.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
                    foreach (var item in extra.EnumerateArray()) Walk(item, st, depth + 1);
            }
            Walk(root, default, 0);
            return parts;
        }

        private static string? ColorHex(string color)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$")) return color.ToUpperInvariant();
            foreach (var (name, hex) in NamedColors) if (name == color) return hex;
            return null;
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
