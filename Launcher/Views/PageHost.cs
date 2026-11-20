using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Sekme sayfalarının kabı. Her sayfanın görünümü ilk açılışta bir kez kurulur ve saklanır; sekme değişince
    /// yalnızca görünürlük değişir. (Önceden ContentControl her geçişte sayfayı baştan kuruyordu: 30–270 ms donma.)
    /// 5 dakika açılmayan sayfanın görünümü atılır (Oyna hariç), açılınca yeniden kurulur.
    /// Yeni sayfa 0,22 sn'de saydamdan görünüre, 8 piksel aşağıdan yerine gelir (web: page-enter).
    /// </summary>
    internal sealed class PageHost : Panel
    {
        public static readonly StyledProperty<object?> PageProperty = AvaloniaProperty.Register<PageHost, object?>(nameof(Page));

        private readonly Dictionary<object, Control> _views = new();
        private readonly Dictionary<object, long> _hiddenAt = new();
        private Avalonia.Threading.DispatcherTimer? _sweep;

        /// <summary>
        /// Bu kadar süre açılmayan sayfanın görünümü atılır (bellek); tekrar açılınca yeniden kurulur. Oyna sayfası hep
        /// kalır (ana sayfa, en sık dönülen yer).
        /// </summary>
        private static readonly TimeSpan DropAfter =
#if DEBUG
            Environment.GetEnvironmentVariable("VULU_PAGE_DROP_SEC") is { } sec && int.TryParse(sec, out int n) ? TimeSpan.FromSeconds(n) :
#endif
            TimeSpan.FromMinutes(5);
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
            if (previous != null)
            {
                previous.IsVisible = false;
                if (previous.DataContext is { } prevPage) _hiddenAt[prevPage] = Environment.TickCount64;
            }
            if (page != null) _hiddenAt.Remove(page);
            _sweep ??= new Avalonia.Threading.DispatcherTimer(DropAfter < TimeSpan.FromMinutes(1) ? DropAfter : TimeSpan.FromMinutes(1), Avalonia.Threading.DispatcherPriority.Background, (_, _) => Sweep());
            if (!_sweep.IsEnabled) _sweep.Start();
            if (next == null) return;
            next.IsVisible = true;
            if (previous != null) _ = PageEnter.RunAsync(next);
        }

        private void Sweep()
        {
            long now = Environment.TickCount64;
            foreach (var (page, at) in _hiddenAt.ToList())
            {
                if (now - at < DropAfter.TotalMilliseconds || page is ViewModels.HomeViewModel || !_views.TryGetValue(page, out var view) || view == _current) continue;
                _hiddenAt.Remove(page);
                _views.Remove(page);
                Children.Remove(view);
                view.DataContext = null;   // görünümün modele taktığı abonelikler bırakılır
            }
            if (_hiddenAt.Count == 0) _sweep?.Stop();
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
