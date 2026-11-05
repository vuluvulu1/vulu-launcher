using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace VuluLauncher.Native
{
    /// <summary>Renkli metnin bir parçası (sunucu MOTD'si: renk "#RRGGBB" ya da null, biçimler).</summary>
    internal sealed record RichPart(string Text, string? Color, bool Bold, bool Italic, bool Underline, bool Strike)
    {
        /// <summary>Core'un MOTD parçaları: { t, c, b, i, u, s }.</summary>
        public static List<RichPart> FromMotd(JsonElement parts) => parts.Items().Select(p =>
            new RichPart(p.Str("t") ?? "", p.Str("c"), p.Bool("b"), p.Bool("i"), p.Bool("u"), p.Bool("s"))).ToList();
    }

    /// <summary>
    /// TextBlock'a parçalı (renkli, kalın, altı çizili) metin verir: <c>native:RichText.Parts="{Binding Motd}"</c>.
    /// Renk verilmeyen parça TextBlock'un kendi rengiyle yazılır.
    /// </summary>
    internal sealed class RichText : AvaloniaObject
    {
        public static readonly AttachedProperty<IReadOnlyList<RichPart>?> PartsProperty =
            AvaloniaProperty.RegisterAttached<RichText, TextBlock, IReadOnlyList<RichPart>?>("Parts");

        static RichText()
        {
            PartsProperty.Changed.AddClassHandler<TextBlock>((block, e) => Apply(block, e.NewValue as IReadOnlyList<RichPart>));
        }

        public static void SetParts(TextBlock element, IReadOnlyList<RichPart>? value) => element.SetValue(PartsProperty, value);
        public static IReadOnlyList<RichPart>? GetParts(TextBlock element) => element.GetValue(PartsProperty);

        private static void Apply(TextBlock block, IReadOnlyList<RichPart>? parts)
        {
            block.Inlines ??= new InlineCollection();
            block.Inlines.Clear();
            if (parts == null) return;
            foreach (var p in parts)
            {
                var run = new Run(p.Text);
                if (p.Color != null && Color.TryParse(p.Color, out var color)) run.Foreground = new SolidColorBrush(color);
                if (p.Bold) run.FontWeight = FontWeight.Bold;
                if (p.Italic) run.FontStyle = FontStyle.Italic;
                if (p.Underline || p.Strike)
                {
                    var decorations = new TextDecorationCollection();
                    if (p.Underline) decorations.AddRange(TextDecorations.Underline);
                    if (p.Strike) decorations.AddRange(TextDecorations.Strikethrough);
                    run.TextDecorations = decorations;
                }
                block.Inlines.Add(run);
            }
        }
    }

    /// <summary>Profil resimleri: sunucudaki adres (sürüm değişince yenisi alınır).</summary>
    internal static class Avatars
    {
        public static string ApiBase { get; set; } = "";
        public static string? Url(string username, long version) =>
            version > 0 && ApiBase.Length > 0 ? $"{ApiBase}/users/{Uri.EscapeDataString(username)}/avatar?v={version}" : null;
    }
}
