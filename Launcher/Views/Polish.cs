using System.Diagnostics;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Boş durum (web: .empty-state): kesik çizgili çerçeve içinde yuvarlak simge, başlık ve kısa açıklama.
    /// </summary>
    internal sealed class EmptyState : Panel
    {
        public static readonly StyledProperty<string> IconProperty = AvaloniaProperty.Register<EmptyState, string>(nameof(Icon), "");
        public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<EmptyState, string>(nameof(Title), "");
        public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<EmptyState, string>(nameof(Text), "");
        public static readonly StyledProperty<bool> DashedProperty = AvaloniaProperty.Register<EmptyState, bool>(nameof(Dashed), true);

        private readonly TextBlock _icon, _title, _text;
        private readonly Rectangle _frame;

        public EmptyState()
        {
            MaxWidth = 440;
            HorizontalAlignment = HorizontalAlignment.Center;
            _frame = new Rectangle
            {
                Stroke = Brush("LineStrong"),
                StrokeThickness = 1,
                StrokeDashArray = new AvaloniaList<double> { 4, 3 },
            };
            _icon = new TextBlock { FontSize = 22, Foreground = Brush("AccentHi"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            _text = new TextBlock { FontSize = 13, LineHeight = 19.5, Foreground = Brush("Muted"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Children.Add(_frame);
            Children.Add(new StackPanel
            {
                Margin = new Thickness(24, 28),
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = Brush("AccentSoft"), HorizontalAlignment = HorizontalAlignment.Center, Child = _icon },
                    _title,
                    _text,
                },
            });
        }

        public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public bool Dashed { get => GetValue(DashedProperty); set => SetValue(DashedProperty, value); }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IconProperty) _icon.Text = Icon;
            else if (change.Property == TitleProperty) _title.Text = Title;
            else if (change.Property == TextProperty) { _text.Text = Text; _text.IsVisible = Text.Length > 0; }
            else if (change.Property == DashedProperty) _frame.IsVisible = Dashed;
        }

        private static IBrush? Brush(string key) =>
            Application.Current?.TryFindResource(key, out var value) == true ? value as IBrush : null;
    }

    /// <summary>Kendi kendine canlanan küçük çizim: görünürken her karede yeniden çizilir.</summary>
    internal abstract class Animated : Control
    {
        protected readonly Stopwatch Clock = Stopwatch.StartNew();
        private bool _animating;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Animate();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && IsVisible) Animate();
        }

        private void Animate()
        {
            if (_animating) return;
            _animating = true;
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Frame);
        }

        private void Frame(TimeSpan _)
        {
            _animating = false;
            if (!IsEffectivelyVisible || TopLevel.GetTopLevel(this) == null) return;
            InvalidateVisual();
            Animate();
        }
    }

    /// <summary>Nabız gibi atan kırmızı canlı noktası (web: .live-dot, 1,8 sn).</summary>
    internal sealed class LiveDot : Animated
    {
        public LiveDot() { Width = 8; Height = 8; ClipToBounds = false; }

        public override void Render(DrawingContext context)
        {
            var center = new Point(4, 4);
            double t = (Clock.Elapsed.TotalMilliseconds % 1800) / 1800;
            double eased = 1 - Math.Pow(1 - t, 3);   // ease-out
            // Halka 9 piksele kadar büyürken söner
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(140 * (1 - eased)), 226, 96, 74)), null, center, 4 + 9 * eased, 4 + 9 * eased);
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#E2604A")), null, center, 4, 4);
        }
    }

    /// <summary>Radyo çalarken zıplayan üç ses çubuğu (web: .music-eq, 0,9 sn).</summary>
    internal sealed class EqBars : Animated
    {
        private static readonly IBrush Bar = new SolidColorBrush(Color.Parse("#FFC873"));

        public EqBars() { Width = 13; Height = 14; }

        public override void Render(DrawingContext context)
        {
            double ms = Clock.Elapsed.TotalMilliseconds;
            for (int i = 0; i < 3; i++)
            {
                double phase = ((ms + i * 300) % 900) / 900;
                double level = .3 + .7 * (.5 - .5 * Math.Cos(phase * 2 * Math.PI));   // %30 ↔ %100, ease-in-out
                double h = 14 * level;
                context.FillRectangle(Bar, new Rect(i * 5, 14 - h, 3, h), 1);
            }
        }
    }

    /// <summary>Yüklenirken yer tutan, üstünden ışık geçen kutu (web: .skeleton, 1,3 sn).</summary>
    internal sealed class Skeleton : Control
    {
        private static readonly Color Base = Color.Parse("#281F17");
        private static readonly Color Shine = Color.Parse("#3A2D21");
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private bool _animating;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Animate();
        }

        private void Animate()
        {
            if (_animating) return;
            _animating = true;
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Frame);
        }

        private void Frame(TimeSpan _)
        {
            _animating = false;
            if (!IsEffectivelyVisible || TopLevel.GetTopLevel(this) == null) return;
            InvalidateVisual();
            Animate();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && IsVisible) Animate();
        }

        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || h <= 0) return;
            // Işık bandı kutunun iki katı genişliğinde kayar (background-size 200%)
            double t = (_clock.Elapsed.TotalMilliseconds % 1300) / 1300;
            t = t < .5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;   // ease-in-out
            double offset = w * (1 - 2 * t);
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(offset, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(offset + 2 * w, 0, RelativeUnit.Absolute),
                SpreadMethod = GradientSpreadMethod.Repeat,
                GradientStops = { new GradientStop(Base, 0), new GradientStop(Shine, .5), new GradientStop(Base, 1) },
            };
            context.FillRectangle(brush, new Rect(Bounds.Size));
        }
    }
}
