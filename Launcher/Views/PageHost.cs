using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Sekme sayfalarının kabı. Her sayfanın görünümü ilk açılışta bir kez kurulur ve saklanır; sekme değişince
    /// yalnızca görünürlük değişir. (Önceden ContentControl her geçişte sayfayı baştan kuruyordu: 30–270 ms donma.)
    /// Yeni sayfa 0,22 sn'de saydamdan görünüre, 8 piksel aşağıdan yerine gelir (web: page-enter).
    /// </summary>
    internal sealed class PageHost : Panel
    {
        public static readonly StyledProperty<object?> PageProperty = AvaloniaProperty.Register<PageHost, object?>(nameof(Page));

        private readonly Dictionary<object, Control> _views = new();
        private Control? _current;

        public object? Page { get => GetValue(PageProperty); set => SetValue(PageProperty, value); }

        /// <summary>Şu an görünen sayfanın görünümü.</summary>
        public Control? Current => _current;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == PageProperty) Show(change.NewValue);
        }

        private void Show(object? page)
        {
            Control? next = null;
            if (page != null && !_views.TryGetValue(page, out next))
            {
                next = this.FindDataTemplate(page)?.Build(page);
                if (next != null)
                {
                    next.DataContext = page;
                    _views[page] = next;
                    Children.Add(next);
                }
            }
            if (next == _current) return;
            var previous = _current;
            _current = next;
            if (previous != null) previous.IsVisible = false;
            if (next == null) return;
            next.IsVisible = true;
            if (previous != null) _ = PageEnter.RunAsync(next);
        }

        private static readonly Avalonia.Animation.Animation PageEnter = new()
        {
            Duration = TimeSpan.FromMilliseconds(220),
            Easing = new Avalonia.Animation.Easings.QuinticEaseOut(),
            FillMode = Avalonia.Animation.FillMode.None,
            Children =
            {
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(0),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0.0), new Avalonia.Styling.Setter(TranslateTransform.YProperty, 8.0) },
                },
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(1),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, 1.0), new Avalonia.Styling.Setter(TranslateTransform.YProperty, 0.0) },
                },
            },
        };
    }
}
