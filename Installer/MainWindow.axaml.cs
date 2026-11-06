using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace Installer
{
    internal sealed partial class MainWindow : Window
    {
        private static readonly Easing RiseEase = new CubicEaseOut();
        private readonly Stopwatch _clock = new();

        public MainWindow() : this(App.Model!) { }

        public MainWindow(SetupViewModel model)
        {
            DataContext = model;
            InitializeComponent();

            model.CloseWindow = Close;
            model.MinimizeWindow = () => WindowState = WindowState.Minimized;
            model.PickFolder = async () =>
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Kurulum klasörünü seç" });
                return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            };

            // Elle yazılan klasör alandan çıkınca ya da Enter'da denetlenir
            DirBox.LostFocus += (_, _) => model.CheckDirectory();
            DirBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) model.CheckDirectory(); };
            model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SetupViewModel.IsDone) && model.IsDone) PlayButton.Focus();
            };

            // Çerçevesiz pencere: düğme ve kutular dışında her yerden sürüklenir
            PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !IsInteractive(e.Source as Visual)) BeginMoveDrag(e);
            };
            Opened += (_, _) =>
            {
                if (TryGetPlatformHandle()?.Handle is { } hwnd) RoundCorners(hwnd);
                // Başlık yazılır, kart ve alt yazı yükselerek belirir
                TitleArt.Play(250);
                _clock.Restart();
                RequestAnimationFrame(Frame);
                _ = model.ReloadReleaseAsync();
            };
            Closing += (_, e) => { if (model.Installing) e.Cancel = true; };
        }

        private void Frame(TimeSpan _)
        {
            double t = _clock.Elapsed.TotalMilliseconds;
            Rise(Card, (t - 1100) / 500);
            Rise(Subtitle, (t - 1300) / 500);
            if (t < 1900) RequestAnimationFrame(Frame);
        }

        private static void Rise(Visual control, double p)
        {
            double e = RiseEase.Ease(Math.Clamp(p, 0, 1));
            control.Opacity = e;
            control.RenderTransform = new TranslateTransform(0, 5 * (1 - e));
        }

        private static bool IsInteractive(Visual? visual)
        {
            for (var v = visual; v != null; v = v.GetVisualParent())
                if (v is Button or CheckBox or TextBox || (v is Control { Name: "Card" })) return true;
            return false;
        }

        /// <summary>Windows 11'de çerçevesiz pencerenin köşelerini yuvarlatır; eski sürümlerde etkisizdir.</summary>
        private static void RoundCorners(IntPtr hwnd)
        {
            try
            {
                int preference = 2; // DWMWCP_ROUND
                _ = DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
            }
            catch { }
        }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
