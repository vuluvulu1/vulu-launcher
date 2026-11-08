using System.Text;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    /// <summary>
    /// Minecraft'ın günlük ayarı (sürüm dosyasındaki log4j yapılandırması) çıktıyı launcher'lar için XML olaylarıyla verir.
    /// Bu ayar eski sürümlerde Log4Shell korumasını da içerdiği için kaldırılmaz; çıktı burada okunup
    /// "[16:26:27] [main/INFO]: mesaj" biçiminde düz satırlara çevrilir (latest.log'daki gibi). XML olmayan satırlar
    /// olduğu gibi geçer. Her akış (stdout, stderr) kendi okuyucusunu kullanır.
    /// </summary>
    internal sealed class Log4jXml
    {
        private static readonly Regex Attr = new(@"(\w+)=""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex CData = new(@"<log4j:(Message|Throwable)>\s*<!\[CDATA\[(.*?)\]\]>\s*</log4j:\1>", RegexOptions.Compiled | RegexOptions.Singleline);

        private readonly Action<string, bool> _emit;
        private StringBuilder? _event;

        /// <param name="emit">Düz satır ve hata seviyesinde (WARN/ERROR/FATAL ya da istisna) mi.</param>
        public Log4jXml(Action<string, bool> emit) => _emit = emit;

        public void Feed(string line)
        {
            if (_event == null)
            {
                if (!line.TrimStart().StartsWith("<log4j:Event", StringComparison.Ordinal))
                {
                    _emit(line, false);
                    return;
                }
                _event = new StringBuilder();
            }
            _event.Append(line).Append('\n');
            if (!line.Contains("</log4j:Event>", StringComparison.Ordinal))
            {
                // Bozuk ya da bitmeyen olay sonsuza dek bellek tutmasın
                if (_event.Length > 256 * 1024) Flush(raw: true);
                return;
            }
            Flush(raw: false);
        }

        private void Flush(bool raw)
        {
            string xml = _event!.ToString();
            _event = null;
            if (raw)
            {
                foreach (var l in xml.Split('\n', StringSplitOptions.RemoveEmptyEntries)) _emit(l, false);
                return;
            }

            int headerEnd = xml.IndexOf('>');
            var attrs = Attr.Matches(headerEnd > 0 ? xml[..headerEnd] : xml).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            string level = attrs.GetValueOrDefault("level", "INFO");
            string thread = System.Net.WebUtility.HtmlDecode(attrs.GetValueOrDefault("thread", "main"));
            string time = long.TryParse(attrs.GetValueOrDefault("timestamp"), out long ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm:ss")
                : DateTime.Now.ToString("HH:mm:ss");
            bool error = level is "WARN" or "ERROR" or "FATAL";

            string message = "", throwable = "";
            foreach (Match m in CData.Matches(xml))
            {
                if (m.Groups[1].Value == "Message") message = m.Groups[2].Value;
                else throwable = m.Groups[2].Value;
            }
            var lines = message.Replace("\r", "").TrimEnd('\n').Split('\n');
            _emit($"[{time}] [{thread}/{level}]: {lines[0]}", error);
            foreach (var l in lines.Skip(1)) _emit(l, error);
            if (throwable.Length > 0)
                foreach (var l in throwable.Replace("\r", "").TrimEnd('\n').Split('\n')) _emit(l, true);
        }
    }
}
