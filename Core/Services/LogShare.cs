using System.Text.Json;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    /// <summary>
    /// Oyun günlüğünü (logs/latest.log) mclo.gs'ye yükleyip paylaşılabilir bağlantı verir (Prism ve Modrinth da bunu
    /// kullanır): çökme yardımı isteyen kişi günlüğü dosya yerine bağlantı olarak atar. Yüklemeden önce Windows kullanıcı
    /// adı (dosya yollarında) ve oturum anahtarları gizlenir; IP adreslerini mclo.gs kendisi gizler. Yalnızca kullanıcı onaylayınca çağrılır.
    /// </summary>
    internal static class LogShare
    {
        private const string Endpoint = "https://api.mclo.gs/1/log";
        private const int MaxLines = 25000;              // mclo.gs sınırı
        private const int MaxBytes = 9 * 1024 * 1024;    // 10 MiB sınırının altında

        private static readonly Regex Token = new(@"(--accessToken\s+|accessToken[""']?\s*[:=]\s*[""']?|token:)[A-Za-z0-9._\-]{16,}", RegexOptions.IgnoreCase);
        private static readonly Regex Ip = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b");

        public static async Task<string> UploadAsync(string logFile)
        {
            if (!File.Exists(logFile)) throw new InvalidOperationException("Bu paketin henüz günlüğü yok. Oyunu bir kez açınca oluşur.");
            string text;
            // Oyun açıkken dosya kilitli olabilir: paylaşımlı okunur
            await using (var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                text = await reader.ReadToEndAsync();
            text = Redact(text);
            var lines = text.Split('\n');
            if (lines.Length > MaxLines) text = string.Join('\n', lines[^MaxLines..]);
            if (text.Length > MaxBytes) text = text[^MaxBytes..];
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Günlük boş.");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("vulu-launcher");
            using var response = await http.PostAsync(Endpoint, new FormUrlEncodedContent(new Dictionary<string, string> { ["content"] = text }));
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            if (!root.TryGetProperty("success", out var ok) || !ok.GetBoolean() || !root.TryGetProperty("url", out var url))
                throw new InvalidOperationException("Günlük yüklenemedi: " + (root.TryGetProperty("error", out var e) ? e.GetString() : "mclo.gs cevap vermedi."));
            return url.GetString() ?? throw new InvalidOperationException("Günlük yüklenemedi.");
        }

        /// <summary>Günlükten kişisel bilgileri çıkarır (testlerde de kullanılır).</summary>
        internal static string Redact(string text)
        {
            string user = Environment.UserName;
            if (user.Length >= 2)
                text = Regex.Replace(text, @"(?<=[\\/]Users[\\/])" + Regex.Escape(user) + @"(?=[\\/])", "<kullanıcı>", RegexOptions.IgnoreCase);
            text = Token.Replace(text, m => m.Groups[1].Value + "<gizlendi>");
            return text;
        }
    }
}
