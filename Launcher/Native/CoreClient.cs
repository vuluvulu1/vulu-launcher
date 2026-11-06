using System.Text.Json;
using Avalonia.Threading;

namespace VuluLauncher.Native
{
    /// <summary>
    /// Native arayüzün Core'a açılan kapısı. Web arayüzünün kullandığı köprü komutlarının aynısı süreç içinde çağrılır;
    /// Core'un gönderdiği olaylar (log, oyun kapandı, bildirim…) arayüz iş parçacığında <see cref="Event"/> ile gelir.
    /// </summary>
    internal sealed class CoreClient
    {
        private Bridge? _bridge;

        public static CoreClient Instance { get; } = new();

        /// <summary>Core olayı: adı ve verisi (veri yoksa Undefined).</summary>
        public event Action<string, JsonElement>? Event;

        public void Attach(Bridge bridge) => _bridge = bridge;

        /// <summary>Komutu çalıştırır; hata verirse istisna iletisi kullanıcıya gösterilecek metindir.</summary>
        public Task<JsonElement> Call(string command, object? args = null) =>
            (_bridge ?? throw new InvalidOperationException("Core bağlı değil.")).InvokeAsync(command, args);

        /// <summary>Sonucu beklenmeyen komut (pencere işlemleri vb.); hata günlüğe yazılır.</summary>
        public void Send(string command, object? args = null)
        {
            _ = Run();
            async Task Run()
            {
                try { await Call(command, args); }
                catch (Exception ex) { LogFile.Write($"[ui] {command}: {ex.Message}"); }
            }
        }

        /// <summary>Core'dan gelen mesaj (IPlatformHost.SendToUi). Yalnızca olaylar işlenir.</summary>
        public void Receive(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("event", out var name) || name.GetString() is not { } eventName) return;
                var data = doc.RootElement.TryGetProperty("data", out var d) ? d.Clone() : default;
                Dispatcher.UIThread.Post(() => Event?.Invoke(eventName, data));
            }
            catch (Exception ex)
            {
                LogFile.Write($"[ui] olay okunamadı: {ex.Message}");
            }
        }
    }

    /// <summary>Komut sonuçlarını okumak için kısa yardımcılar (alan yoksa varsayılan döner).</summary>
    internal static class Json
    {
        public static JsonElement Get(this JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

        public static string? Text(this JsonElement e, string name) =>
            e.Get(name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

        public static int Int(this JsonElement e, string name, int fallback = 0) =>
            e.Get(name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out int n) ? n : fallback;

        public static double Num(this JsonElement e, string name, double fallback = 0) =>
            e.Get(name) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : fallback;

        public static bool Bool(this JsonElement e, string name) => e.Get(name).ValueKind == JsonValueKind.True;

        public static IEnumerable<JsonElement> Items(this JsonElement e) =>
            e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : Enumerable.Empty<JsonElement>();

        public static IEnumerable<JsonElement> Items(this JsonElement e, string name) => e.Get(name).Items();
    }
}
