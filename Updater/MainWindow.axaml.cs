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
#if DEBUG
                // Yalnızca geliştirmede: VULU_UPDATER_SHOTS=<klasör> ile güncelleme yapılmaz; örnek durumlar sırayla
                // gösterilip pencerenin kendisi (görsel ağacı, ekran değil) PNG'ye çizilir, sonra pencere kapanır
                if (Environment.GetEnvironmentVariable("VULU_UPDATER_SHOTS") is { Length: > 0 } shotDir)
                {
                    _ = ShootSamplesAsync(shotDir);
                    return;
                }
#endif
                model.Start();
            };
            Closing += (_, e) => { if (!model.CanClose) e.Cancel = true; };
            Closed += (_, _) => model.Cancel();
        }

#if DEBUG
        private async Task ShootSamplesAsync(string dir)
        {
            const string notes = "- **Pelerinler** ve dolap\n- Liderlik tablosu\n- Sezonlar ve haftalık görevler\n"
                + "- Sosyal'de canlı harita\n- Profilde 3D skin görüntüleyici\n- Admin › İlerleme düzenleyicisi\n- Küçük düzeltmeler";
            string theme = UpdaterTheme.ReadThemeName();
            var samples = new (string Name, UpdateStatus Status)[]
            {
                ("1-checking", new("checking", "Güncellemeler denetleniyor…")),
                ("2-downloading", new("downloading", "v0.27.0 indiriliyor…", "18.4 / 42.0 MB · 6.1 MB/s", 0.44, "0.27.0", notes)),
                ("3-verifying", new("verifying", "Doğrulanıyor…", "", null, "0.27.0", notes)),
                ("4-installing", new("installing", "Kuruluyor…", "", null, "0.27.0", notes)),
                ("5-installed", new("launching", "v0.27.0 kuruldu", "Launcher açılıyor…", 1, "0.27.0", notes)),
                ("6-uptodate", new("launching", "Güncel", "v0.26.0")),
                ("7-offline", new("launching", "Güncelleme sunucusuna ulaşılamadı", "Mevcut sürümle açılıyor…")),
                ("8-error", new("error", "Güncelleme başarısız", "İndirilen dosyanın imzası doğrulanamadı. Güvenlik için kurulmadı; bağlantını denetleyip tekrar dene.")),
                ("9-confirm", new("confirm", "vulu Launcher kaldırılsın mı?", @"D:\vulu Launcher")),
                ("10-uninstalled", new("uninstalled", "vulu Launcher kaldırıldı", @"Dünyaların ve oyun dosyaların klasörde duruyor: D:\vulu Launcher")),
            };
            Directory.CreateDirectory(dir);
            foreach (var (name, status) in samples)
            {
                // Not taşıyan örneklerden sonra notsuz olanlar notları göstermesin diye her örnek temiz modelle başlar
                var sample = new UpdaterViewModel(SampleInstallDir, [], status.State is "confirm" or "uninstalled", "0.9.0");
                DataContext = sample;
                sample.ShowSample(status);
                await Task.Delay(700);
                var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
                using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
                rtb.Render(this);
                rtb.Save(Path.Combine(dir, $"{theme}-{name}.png"));
            }
            Environment.Exit(0);
        }

        private static string SampleInstallDir => Path.Combine(Path.GetTempPath(), "vulu-updater-sample");
#endif

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
