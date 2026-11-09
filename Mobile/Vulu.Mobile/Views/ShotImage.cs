using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VuluMobile.ViewModels;

namespace VuluMobile.Views
{
    /// <summary>
    /// Akıştaki önizleme: resim gelmeden önce de doğru yüksekliği kaplar (liste zıplamaz), görünür olunca indirir.
    /// </summary>
    internal sealed class ShotImage : Border
    {
        public static readonly StyledProperty<ShotItem?> ShotProperty = AvaloniaProperty.Register<ShotImage, ShotItem?>(nameof(Shot));

        private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
        private bool _attached;

        public ShotImage()
        {
            Background = new SolidColorBrush(Color.Parse("#1B1510"));
            ClipToBounds = true;
            Child = _image;
        }

        public ShotItem? Shot { get => GetValue(ShotProperty); set => SetValue(ShotProperty, value); }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != ShotProperty) return;
            if (change.OldValue is ShotItem old) old.PropertyChanged -= OnShotChanged;
            if (change.NewValue is ShotItem shot)
            {
                shot.PropertyChanged += OnShotChanged;
                _image.Source = shot.Thumb;
                if (_attached) shot.LoadThumb();
            }
            InvalidateMeasure();
        }

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            Shot?.LoadThumb();
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            _attached = false;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnShotChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ShotItem.Thumb)) _image.Source = Shot?.Thumb;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? 360 : availableSize.Width;
            double height = width * (Shot?.Ratio ?? 0.5625);
            base.MeasureOverride(new Size(width, height));
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Child?.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
