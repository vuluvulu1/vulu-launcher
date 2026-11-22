using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;

namespace VuluShared
{
    /// <summary>
    /// İngilizce seçiliyse arayüzdeki metinleri yerinde çevirir: TextBlock, Run, düğme içeriği, menü ve sekme başlığı,
    /// ipucu, kutuların boş metni ve pencere başlığı değişince değer sözlükte varsa İngilizcesi konur. Değer
    /// SetCurrentValue ile konduğu için bağlı (binding) özellikler bağlı kalır; Türkçe yeni değer gelince yine çevrilir.
    /// Böylece axaml ve sabit Türkçe metinler olduğu gibi kalır; yalnızca değişken içeren metinler kodda L.F ile yazılır.
    /// </summary>
    internal static class UiTranslator
    {
        public static void Install()
        {
            if (!L.IsEnglish) return;
            Hook<TextBlock>(TextBlock.TextProperty);
            Hook<Run>(Run.TextProperty);
            Hook<ContentControl>(ContentControl.ContentProperty);
            Hook<HeaderedContentControl>(HeaderedContentControl.HeaderProperty);
            Hook<HeaderedSelectingItemsControl>(HeaderedSelectingItemsControl.HeaderProperty);
            Hook<HeaderedItemsControl>(HeaderedItemsControl.HeaderProperty);
            Hook<TextBox>(TextBox.WatermarkProperty);
            Hook<Control>(ToolTip.TipProperty);
            Hook<Window>(Window.TitleProperty);
        }

        private static void Hook<T>(AvaloniaProperty property) where T : AvaloniaObject =>
            property.Changed.AddClassHandler<T>((target, e) =>
            {
                if (e.NewValue is string text && L.TryTranslate(text, out var english) && english != text)
                    target.SetCurrentValue(property, english);
            });
    }
}
