using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VuluMobile.Services;

namespace VuluMobile.Views
{
    /// <summary>Yuvarlak profil fotoğrafı; fotoğraf yoksa ya da yüklenirken adın baş harfi.</summary>
    internal sealed class Avatar : Border
    {
        public static readonly StyledProperty<string?> UsernameProperty =
            AvaloniaProperty.Register<Avatar, string?>(nameof(Username));
        public static readonly StyledProperty<double> SizeProperty =
            AvaloniaProperty.Register<Avatar, double>(nameof(Size), 40);

        private static readonly IBrush[] Tints =
            new[] { "#7A4A1E", "#3E5A6E", "#5B3E6E", "#3E6E52", "#6E3E3E", "#6E623E" }.Select(c => (IBrush)new SolidColorBrush(Color.Parse(c))).ToArray();

        private readonly TextBlock _letter = new()
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
        };
        private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
        private int _load;

        public Avatar()
        {
            ClipToBounds = true;
            Child = new Grid { Children = { _letter, _image } };
            Apply();
        }

        public string? Username { get => GetValue(UsernameProperty); set => SetValue(UsernameProperty, value); }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Images.AvatarChanged += OnAvatarChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            Images.AvatarChanged -= OnAvatarChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnAvatarChanged(string username)
        {
            if (string.Equals(username, Username, StringComparison.OrdinalIgnoreCase)) Apply();
        }
        public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == UsernameProperty || change.Property == SizeProperty) Apply();
        }

        private async void Apply()
        {
            Width = Height = Size;
            CornerRadius = new CornerRadius(Size / 2);
            _letter.FontSize = Size * 0.42;
            string name = Username ?? "";
            _letter.Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "";
            // Her açılışta aynı renk: .NET'in GetHashCode'u süreç başına değişir, harflerden basit bir toplam kullanılır
            int sum = 0;
            foreach (char ch in name.ToLowerInvariant()) sum = sum * 31 + ch;
            Background = Tints[(int)((uint)sum % Tints.Length)];
            _image.Source = null;
            if (name.Length == 0) return;
            int load = ++_load;
            try
            {
                var bitmap = await Images.AvatarAsync(name);
                if (load == _load) _image.Source = bitmap;
            }
            catch { /* baş harf kalır */ }
        }
    }
}
