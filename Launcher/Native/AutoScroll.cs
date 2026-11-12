using Avalonia;
using Avalonia.Controls;

namespace VuluLauncher.Native
{
    /// <summary>
    /// Sohbet listeleri için: içerik uzayınca (yeni satır) en alta kayar; kullanıcı yukarı kaydırıp eski satırları
    /// okuyorsa yerinde kalır. Kullanım: &lt;ScrollViewer native:AutoScroll.ToEnd="True"&gt;
    /// </summary>
    public static class AutoScroll
    {
        public static readonly AttachedProperty<bool> ToEndProperty =
            AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("ToEnd", typeof(AutoScroll));

        public static bool GetToEnd(ScrollViewer s) => s.GetValue(ToEndProperty);
        public static void SetToEnd(ScrollViewer s, bool value) => s.SetValue(ToEndProperty, value);

        static AutoScroll()
        {
            ToEndProperty.Changed.AddClassHandler<ScrollViewer>((s, e) =>
            {
                if (e.NewValue is true) s.PropertyChanged += OnChanged;
                else s.PropertyChanged -= OnChanged;
            });
        }

        private static void OnChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (sender is not ScrollViewer s || e.Property != ScrollViewer.ExtentProperty) return;
            var old = e.OldValue is Size o ? o : default;
            // Önceki içerikte en alttaysa (ya da ilk dolum) en alta git
            bool atBottom = s.Offset.Y + s.Viewport.Height >= old.Height - 24;
            if (atBottom) s.ScrollToEnd();
        }
    }
}
