using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Updater
{
    internal sealed partial class MainWindow : Window
    {
        private readonly UpdaterViewModel _model;

        public MainWindow() : this(App.Model!) { }

        public MainWindow(UpdaterViewModel model)
        {
            _model = model;
            DataContext = model;
            InitializeComponent();
            Title = model.UninstallMode ? "vulu Launcher Kaldır" : "vulu Launcher Güncelleyici";

            string icon = Path.Combine(AppContext.BaseDirectory, "vuluLauncher-logo-prototip.ico");
            if (File.Exists(icon)) Icon = new WindowIcon(icon);

            model.CloseWindow = Close;
            model.MinimizeWindow = () => WindowState = WindowState.Minimized;

            // Çerçevesiz pencere: düğmeler ve kutular dışında her yerden sürüklenir
            PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !IsInteractive(e.Source as Visual)) BeginMoveDrag(e);
            };
            Opened += (_, _) =>
            {
                if (TryGetPlatformHandle()?.Handle is { } hwnd)
                {
                    RoundCorners(hwnd);
                }
                model.Start();
            };
            Closing += (_, e) => { if (!model.CanClose) e.Cancel = true; };
            Closed += (_, _) => model.Cancel();
        }

        private static bool IsInteractive(Visual? visual)
        {
            for (var v = visual; v != null; v = v.GetVisualParent())
                if (v is Button or CheckBox or ScrollViewer) return true;
            return false;
        }

        /// <summary>Windows 11'de çerçevesiz pencerenin köşelerini yuvarlatır; eski sürümlerde etkisizdir.</summary>
        private static void RoundCorners(IntPtr hwnd)
        {
            try
            {
                int preference = 2; // DWMWCP_ROUND
                _ = DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
            }
            catch { }
        }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
