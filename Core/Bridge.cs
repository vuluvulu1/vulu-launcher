using System.Collections.Concurrent;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Arayüz (JS) ile C# arasındaki mesaj katmanı.
    ///   JS → C#  : { id, cmd, args }
    ///   C# → JS  : { id, ok, result | error }   (komut cevabı)
    ///              { event, data }              (C#'ın kendiliğinden gönderdiği olay)
    /// </summary>
    internal sealed class Bridge
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly Action<string> _send;
        private readonly Dictionary<string, Func<JsonElement, Task<object?>>> _asyncHandlers = new();
        private readonly Dictionary<string, Action<JsonElement>> _inlineHandlers = new();

        // Oyun çıktısı saniyede yüzlerce satır üretebilir; satırlar biriktirilip toplu gönderilir.
        private readonly ConcurrentQueue<LogLine> _logQueue = new();
        private readonly System.Threading.Timer _logTimer;

        public Bridge(Action<string> send)
        {
            _send = send;
            _logTimer = new System.Threading.Timer(_ => FlushLogs(), null, 80, 80);
        }

        /// <summary>Arka planda çalışan, sonucu JS'e Promise cevabı olarak dönen komut.</summary>
        public void On(string command, Func<JsonElement, Task<object?>> handler) => _asyncHandlers[command] = handler;

        /// <summary>Mesajın geldiği (UI) thread'de hemen çalışan, cevap dönmeyen komut. Pencere işlemleri için.</summary>
        public void OnInline(string command, Action<JsonElement> handler) => _inlineHandlers[command] = handler;

        public void Emit(string eventName, object? data = null) =>
            Send(new { @event = eventName, data });

        public void Log(string text, string level = "info", bool replaceLast = false) =>
            _logQueue.Enqueue(new LogLine(DateTime.Now.ToString("HH:mm:ss"), text, level, replaceLast));

        public void HandleMessage(string raw)
        {
            string? id = null;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                string command = root.GetProperty("cmd").GetString() ?? "";
                var args = root.TryGetProperty("args", out var argsElement) ? argsElement.Clone() : default;

                if (_inlineHandlers.TryGetValue(command, out var inline))
                {
                    inline(args);
                    return;
                }

                if (!_asyncHandlers.TryGetValue(command, out var handler))
                {
                    Respond(id, false, null, $"Bilinmeyen komut: {command}");
                    return;
                }

                string? requestId = id;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Respond(requestId, true, await handler(args), null);
                    }
                    catch (Exception ex)
                    {
                        Respond(requestId, false, null, ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                Respond(id, false, null, ex.Message);
            }
        }

        public void Shutdown()
        {
            _logTimer.Dispose();
        }

        private void Respond(string? id, bool ok, object? result, string? error)
        {
            if (id == null) return;
            Send(new { id, ok, result, error });
        }

        private void FlushLogs()
        {
            if (_logQueue.IsEmpty) return;

            var lines = new List<LogLine>();
            while (lines.Count < 400 && _logQueue.TryDequeue(out var line))
                lines.Add(line);

            Emit("log", lines);
        }

        private void Send(object payload)
        {
            try
            {
                _send(JsonSerializer.Serialize(payload, Json));
            }
            catch
            {
                // Pencere kapanırken gelen geç mesajlar yok sayılır
            }
        }

        private sealed record LogLine(string Time, string Text, string Level, bool Replace);
    }

    internal static class JsonArgs
    {
        public static string Str(this JsonElement args, string name, string fallback = "")
        {
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? fallback;
            return fallback;
        }

        public static bool Bool(this JsonElement args, string name)
        {
            return args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) &&
                   value.ValueKind == JsonValueKind.True;
        }

        public static int Int(this JsonElement args, string name, int fallback = 0)
        {
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;
            return fallback;
        }

        public static List<string> StrList(this JsonElement args, string name)
        {
            var list = new List<string>();
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                        list.Add(item.GetString()!.Trim());
            }
            return list;
        }
    }
}
