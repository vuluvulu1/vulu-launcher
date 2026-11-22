using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Resmi değişince yenisi eskisinin üstüne yavaşça beliren resim (Kütüphane'nin ortam arka planı). Kaynak bir an boşalırsa
    /// (yeni paketin resmi yüklenirken) eski resim yerinde kalır; gerçekten resim yoksa kısa bir beklemeden sonra söner.
    /// </summary>
    internal sealed class CrossfadeImage : Panel
    {
        public static readonly StyledProperty<IImage?> SourceProperty = AvaloniaProperty.Register<CrossfadeImage, IImage?>(nameof(Source));
        public static readonly StyledProperty<Stretch> StretchProperty = AvaloniaProperty.Register<CrossfadeImage, Stretch>(nameof(Stretch), Stretch.UniformToFill);

        public IImage? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
        public Stretch Stretch { get => GetValue(StretchProperty); set => SetValue(StretchProperty, value); }

        private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(280);
        private readonly Image _back = NewLayer(), _front = NewLayer();
        private IDisposable? _clear;

        public CrossfadeImage()
        {
            Children.Add(_back);
            Children.Add(_front);
            _back.Opacity = 0;
            _front.Opacity = 0;
            _back.Stretch = _front.Stretch = Stretch;   // varsayılan değer değişiklik olarak gelmez
        }

        private static Image NewLayer() => new()
        {
            Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = Fade, Easing = new CubicEaseInOut() } },
        };

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == StretchProperty) { _back.Stretch = Stretch; _front.Stretch = Stretch; }
            else if (change.Property == SourceProperty) Show(Source);
        }

        private void Show(IImage? image)
        {
            _clear?.Dispose();
            if (image == null)
            {
                // Yeni resim genelde hemen gelir: o arada eski resim kalır; gelmezse söner
                _clear = DispatcherTimer.RunOnce(() => { _front.Opacity = 0; _back.Opacity = 0; }, TimeSpan.FromMilliseconds(600));
                return;
            }
            if (ReferenceEquals(_front.Source, image) && _front.Opacity > 0) return;
            // Görünen resim arkaya geçer (anında, aynı görüntü), yenisi önde 0'dan belirir
            _back.Transitions = null;
            _back.Source = _front.Source;
            _back.Opacity = _front.Source != null ? _front.Opacity : 0;
            _back.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = Fade, Easing = new CubicEaseInOut() } };
            _front.Transitions = null;
            _front.Opacity = 0;
            _front.Source = image;
            _front.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = Fade, Easing = new CubicEaseInOut() } };
            _front.Opacity = 1;   // geçiş hemen başlar (bir sonraki kareyi beklemeden)
            // Eski resim yenisi belirdikten sonra söner (arada koyu bir boşluk görünmesin)
            var shown = image;
            DispatcherTimer.RunOnce(() => { if (ReferenceEquals(_front.Source, shown)) _back.Opacity = 0; }, Fade);
        }
    }
}
