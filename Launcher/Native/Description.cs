using System.Net;
using System.Text.RegularExpressions;

namespace VuluLauncher.Native
{
    /// <summary>Açıklamanın bir bloğu: başlık, paragraf, madde, kod ya da resim.</summary>
    internal sealed record DescriptionBlock(string Kind, string Text, int Level = 0, string? ImageUrl = null)
    {
        public bool IsHeading => Kind == "h";
        public bool IsParagraph => Kind == "p";
        public bool IsItem => Kind == "li";
        public bool IsCode => Kind == "code";
        public bool IsImage => Kind == "img";
        public double HeadingSize => Level <= 1 ? 20 : Level == 2 ? 17 : 15;
    }

    /// <summary>
    /// Proje açıklamaları (CurseForge'da HTML, Modrinth'te markdown) başkalarının yazdığı içeriktir: düz metin bloklarına
    /// çevrilir. Betik, stil ve gömülü içerik hiç geçmez; resimler yalnızca https adresinden, bağlantılar yalnızca metin olarak.
    /// </summary>
    internal static class Description
    {
        private static readonly Regex Tag = new("<[^>]+>", RegexOptions.Compiled);

        public static List<DescriptionBlock> FromHtml(string html)
        {
            // Tehlikeli ve görünmeyen içerik tamamen atılır
            html = Regex.Replace(html, @"<(script|style|iframe|object|embed|form|svg|math|video|audio|noscript|template)\b[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase);
            // Bloklar satır sonlarına, resimler ayrı satırlara çevrilir; sonra markdown gibi okunur
            html = Regex.Replace(html, @"<img\b[^>]*\bsrc\s*=\s*[""']([^""']+)[""'][^>]*>", m => $"\n![]({m.Groups[1].Value})\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<h([1-6])\b[^>]*>", m => "\n" + new string('#', int.Parse(m.Groups[1].Value)) + " ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<li\b[^>]*>", "\n- ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<(br|/p|/div|/h[1-6]|/li|/tr|/blockquote|hr)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<p\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            return FromMarkdown(WebUtility.HtmlDecode(Tag.Replace(html, "")));
        }

        public static List<DescriptionBlock> FromMarkdown(string md)
        {
            var blocks = new List<DescriptionBlock>();
            var paragraph = new List<string>();
            List<string>? code = null;
            void Flush()
            {
                if (paragraph.Count == 0) return;
                string text = Inline(string.Join(" ", paragraph));
                if (text.Length > 0) blocks.Add(new DescriptionBlock("p", text));
                paragraph.Clear();
            }

            foreach (var raw in (md ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (code != null)
                {
                    if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { blocks.Add(new DescriptionBlock("code", string.Join("\n", code))); code = null; }
                    else code.Add(line);
                    continue;
                }
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { Flush(); code = new(); continue; }
                // Satırın içindeki resimler kendi bloğu olur
                var images = Regex.Matches(line, @"!\[[^\]]*\]\((\S+?)(?:\s+""[^""]*"")?\)");
                if (images.Count > 0)
                {
                    Flush();
                    foreach (Match m in images)
                        if (m.Groups[1].Value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) blocks.Add(new DescriptionBlock("img", "", ImageUrl: m.Groups[1].Value));
                    line = Regex.Replace(line, @"\[?!\[[^\]]*\]\(\S+?\)\]?(\(\S+?\))?", "").Trim();
                    if (line.Length == 0) continue;
                }
                line = Tag.Replace(line, "").Trim();   // markdown içindeki HTML etiketleri
                Match h, li;
                if ((h = Regex.Match(line, @"^(#{1,6})\s+(.*)$")).Success) { Flush(); blocks.Add(new DescriptionBlock("h", Inline(h.Groups[2].Value), h.Groups[1].Length)); }
                else if (Regex.IsMatch(line, @"^([-*_])(\s*\1){2,}$")) Flush();
                else if ((li = Regex.Match(line, @"^([-*+]|\d+[.)])\s+(.*)$")).Success) { Flush(); blocks.Add(new DescriptionBlock("li", "•  " + Inline(li.Groups[2].Value))); }
                else if (line.Length == 0) Flush();
                else paragraph.Add(line.TrimStart('>', ' '));
            }
            if (code != null) blocks.Add(new DescriptionBlock("code", string.Join("\n", code)));
            Flush();
            return blocks;
        }

        /// <summary>Satır içi biçim işaretleri atılır; bağlantının yalnızca metni kalır.</summary>
        private static string Inline(string s)
        {
            s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]*\)", "$1");
            s = Regex.Replace(s, @"\[([^\]]+)\]\[[^\]]*\]", "$1");
            s = Regex.Replace(s, @"(\*\*|__|~~|`)", "");
            s = Regex.Replace(s, @"(^|\s)[*_]([^*_]+)[*_](?=\s|$|[.,!?])", "$1$2");
            return WebUtility.HtmlDecode(Regex.Replace(s, @"\s+", " ")).Trim();
        }
    }
}
