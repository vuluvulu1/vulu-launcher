using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

namespace VuluLauncher.Views
{
    /// <summary>Kütüphane sayfalarının ortak küçük dokunuşları.</summary>
    internal static class KutPolish
    {
        private static readonly Animation SwapIn = new()
        {
            Duration = TimeSpan.FromMilliseconds(240),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Backward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0), new Setter(TranslateTransform.XProperty, 12.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0), new Setter(TranslateTransform.XProperty, 0.0) } },
            },
        };

        /// <summary>Çekmecenin içeriği başka pakete/sunucuya geçince hafifçe sağdan süzülerek gelir (görünmüyorsa boşuna oynatılmaz).</summary>
        public static void Swap(Control target)
        {
            if (!target.IsEffectivelyVisible) return;
            _ = SwapIn.RunAsync(target);
        }

        /// <summary>
        /// Sayfanın arama kutusu: sayfa görünürken Ctrl+F kutuya odaklanır, kutuda Esc yazılanı siler
        /// (boşsa odağı bırakır).
        /// </summary>
        public static void AttachSearch(Control page, TextBox box)
        {
            void OnKey(object? sender, KeyEventArgs e)
            {
                if (e.Handled || !page.IsEffectivelyVisible) return;
                if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
                {
                    box.Focus();
                    box.SelectAll();
                    e.Handled = true;
                }
            }
            TopLevel? top = null;
            page.AttachedToVisualTree += (_, _) =>
            {
                top = TopLevel.GetTopLevel(page);
                top?.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
            };
            page.DetachedFromVisualTree += (_, _) => top?.RemoveHandler(InputElement.KeyDownEvent, OnKey);
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                if (!string.IsNullOrEmpty(box.Text)) box.Text = "";
                else TopLevel.GetTopLevel(box)?.Focus();
                e.Handled = true;
            };
        }

        /// <summary>Süzgeç hapının yazısı: ad ve yanında kaç öğe olduğu ("Kişisel  3").</summary>
        public static object ChipText(string name, int count) => new TextBlock
        {
            Inlines = new Avalonia.Controls.Documents.InlineCollection
            {
                new Avalonia.Controls.Documents.Run(name),
                new Avalonia.Controls.Documents.Run("  " + count) { FontFamily = Application.Current?.FindResource("Mono") as FontFamily ?? FontFamily.Default, FontSize = 11.5, FontWeight = FontWeight.Normal },
            },
        };
    }
}
