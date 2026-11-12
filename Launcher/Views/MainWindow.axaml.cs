using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VuluLauncher.ViewModels;
using Avalonia.VisualTree;

namespace VuluLauncher.Views
{
    internal sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Themes.Mark(this);
            // Tema geçişi animasyonu (Ayarlar › Tema)
            DataContextChanged += (_, _) =>
            {
                if (DataContext is MainViewModel m) m.PlayThemeTransition = (toUfuk, atMiddle) => Transition.PlayAsync(toUfuk, atMiddle);
            };
            // Başlık çubuğundan sürükleme (düğmelerin üstünde değilse)
            TitleBar.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !IsOnButton(e.Source as Visual))
                    BeginMoveDrag(e);
            };
            // Sekme değişince yeni sayfa belirir: 0,18 sn'de saydamdan görünüre, 6 piksel aşağıdan yerine (web: page-enter)
            PageHost.PropertyChanged += (_, e) =>
            {
                if (e.Property == ContentControl.ContentProperty && e.OldValue != null && e.NewValue != null && e.OldValue != e.NewValue)
                    _ = PageEnter.RunAsync(PageHost);
            };
            // İndirme paneli: üstüne gelindiyse tamamlanınca kapanmaz
            DownloadsPanel.PointerEntered += (_, _) => { if (DataContext is MainViewModel m) m.Downloads.Hovered = true; };
            DownloadsPanel.PointerExited += (_, _) => { if (DataContext is MainViewModel m) m.Downloads.Hovered = false; };
            // Esc en üstteki kutuyu kapatır; Ctrl+K hızlı aramayı açar
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control) && DataContext is MainViewModel main)
                {
                    main.OpenPaletteCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Escape && DataContext is MainViewModel { Modals.Count: > 0 } vm)
                {
                    vm.Modals[^1].DismissCommand.Execute(null);
                    e.Handled = true;
                }
            };
        }

        private void OnNotificationsOpened(object? sender, EventArgs e)
        {
            if (DataContext is MainViewModel vm) vm.Notifications.IsOpen = true;
        }

        /// <summary>Bildirim listesi kapanınca hepsi okundu sayılır.</summary>
        private void OnNotificationsClosed(object? sender, EventArgs e)
        {
            if (DataContext is MainViewModel vm) vm.Notifications.IsOpen = false;
        }

        private static readonly Avalonia.Animation.Animation PageEnter = new()
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            FillMode = Avalonia.Animation.FillMode.None,
            Children =
            {
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(0),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0.0), new Avalonia.Styling.Setter(TranslateTransform.YProperty, 6.0) },
                },
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(1),
                    Setters = { new Avalonia.Styling.Setter(OpacityProperty, 1.0), new Avalonia.Styling.Setter(TranslateTransform.YProperty, 0.0) },
                },
            },
        };

        private static bool IsOnButton(Visual? visual)
        {
            for (var v = visual; v != null; v = v.GetVisualParent())
                if (v is Button) return true;
            return false;
        }

        /// <summary>Pencere boyutu (DIP) ve arayüz ölçeği (Ayarlar → launcher boyutu, yazı ve arayüz boyutu).</summary>
        public void ApplyLayout(double width, double height, double scale)
        {
            Width = width;
            Height = height;
            Scaler.LayoutTransform = new ScaleTransform(scale, scale);
        }
    }
}
