using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Version;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AGLR_Launcher.LauncherUpdateService;

namespace AGLR_Launcher
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            CreateCustomTitleBar();
            ApplyDarkLauncherTheme();
            this.Opacity = 0;
            this.ShowInTaskbar = false;
            this.KeyPreview = true;
            // _updateService field initializer'da zaten oluştu
        }
        private readonly LauncherUpdateService _updateService = new();
        private AdminService? _adminService; // readonly kaldır
        private readonly ManifestService _manifestService = new(string.Empty); // token gerekmez, sadece okuma

        private async Task CheckLauncherVersionAsync()
        {
            var (updateAvailable, latest) = await _updateService.CheckForUpdateAsync();

            if (updateAvailable && latest != null)
            {
                Log($"Yeni sürüm mevcut: {latest.Version}", Color.Yellow);

                // Dosya boyutunu öğren
                string sizeText = "";
                try
                {
                    using var http = new HttpClient();
                    http.DefaultRequestHeaders.Add("User-Agent", "vulu-launcher");
                    using var req = new HttpRequestMessage(HttpMethod.Head, latest.DownloadUrl);
                    using var res = await http.SendAsync(req);
                    if (res.Content.Headers.ContentLength.HasValue)
                    {
                        double mb = res.Content.Headers.ContentLength.Value / 1024.0 / 1024.0;
                        sizeText = $" ({mb:F1} MB)";

                        // Disk alanı kontrolü
                        string drive = Path.GetPathRoot(Application.StartupPath) ?? "C:\\";
                        var driveInfo = new DriveInfo(drive);
                        long gerekli = res.Content.Headers.ContentLength.Value + 50 * 1024 * 1024; // +50MB buffer
                        if (driveInfo.AvailableFreeSpace < gerekli)
                        {
                            ShowThemedConfirm("Yetersiz Disk Alanı",
                                $"Güncelleme için yeterli disk alanı yok.\n" +
                                $"Gereken: {gerekli / 1024 / 1024} MB, " +
                                $"Mevcut: {driveInfo.AvailableFreeSpace / 1024 / 1024} MB");
                            return;
                        }
                    }
                }
                catch { /* boyut alınamazsa devam et */ }

                bool onay = ShowThemedConfirm(
                    "Güncelleme Mevcut",
                    $"Launcher {latest.Version} sürümüne güncellenebilir{sizeText}.\nŞimdi güncellensin mi?");

                if (onay)
                    await StartUpdateAsync(latest.DownloadUrl);
                else
                    Log("Güncelleme ertelendi.", Color.Gray);
            }
            else
            {
                Log("Launcher güncel.", Color.Yellow);
            }
        }




        private async Task StartUpdateAsync(string downloadUrl)
        {
            string updaterPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Updater.exe");
            string launcherPath = Application.ExecutablePath;

            if (!File.Exists(updaterPath))
            {
                Log("Updater.exe bulunamadı!", Color.Red);
                return;
            }

            Log("Güncelleme başlatılıyor...", Color.Yellow);
            await Task.Delay(500);

            System.Diagnostics.Process.Start(updaterPath, $"\"{downloadUrl}\" \"{launcherPath}\"");
            Application.Exit();
        }

        private bool ShowThemedConfirm(string title, string message)
        {
            bool result = false;

            using var dialog = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(380, 170),
                BackColor = themeBack,
                ShowInTaskbar = false,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var header = new GradientTitleBar { Dock = DockStyle.Top, Height = 34 };
            header.MouseDown += (_, e) => DragWindow(dialog, e);
            header.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                ForeColor = themeText,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Location = new Point(16, 8),
                BackColor = Color.Transparent
            });

            var body = new Label
            {
                Text = message,
                ForeColor = themeText,
                BackColor = Color.Transparent,
                Location = new Point(24, 58),
                Size = new Size(332, 42)
            };

            var btnNo = CreateDialogButton("Hayır", new Size(96, 34));
            btnNo.Location = new Point(158, 118);
            btnNo.Click += (_, _) => { result = false; dialog.Close(); };

            var btnYes = CreateDialogButton("Evet", new Size(96, 34));
            btnYes.Location = new Point(260, 118);
            btnYes.BackColor = Color.FromArgb(73, 43, 25);
            btnYes.Click += (_, _) => { result = true; dialog.Close(); };

            dialog.Controls.Add(header);
            dialog.Controls.Add(body);
            dialog.Controls.Add(btnNo);
            dialog.Controls.Add(btnYes);
            dialog.AcceptButton = btnYes;
            dialog.CancelButton = btnNo;

            dialog.ShowDialog(this);
            return result;
        }

        private const int CustomTitleBarHeight = 34;
        private GradientTitleBar? customTitleBar;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private void CreateCustomTitleBar()
        {
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + CustomTitleBarHeight);
            MinimumSize = new Size(MinimumSize.Width, MinimumSize.Height + CustomTitleBarHeight);
            MaximumSize = new Size(MaximumSize.Width, MaximumSize.Height + CustomTitleBarHeight);

            foreach (Control control in Controls)
            {
                control.Top += CustomTitleBarHeight;
            }

            customTitleBar = new GradientTitleBar
            {
                Dock = DockStyle.Top,
                Height = CustomTitleBarHeight
            };
            customTitleBar.MouseDown += CustomTitleBar_MouseDown;

            var iconBox = new PictureBox
            {
                Image = Icon?.ToBitmap(),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Location = new Point(12, 9),
                Size = new Size(16, 16),
                BackColor = Color.Transparent
            };
            iconBox.MouseDown += CustomTitleBar_MouseDown;

            var titleLabel = new Label
            {
                Text = "vulu Launcher (Beta)",
                ForeColor = themeText,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(36, 8),
                BackColor = Color.Transparent
            };
            titleLabel.MouseDown += CustomTitleBar_MouseDown;

            var buttonHost = new Panel
            {
                Dock = DockStyle.Right,
                Width = 88,
                BackColor = Color.Transparent
            };
            buttonHost.Controls.Add(CreateTitleBarButton("X", 44, Close));
            buttonHost.Controls.Add(CreateTitleBarButton("-", 0, () => WindowState = FormWindowState.Minimized));

            customTitleBar.Controls.Add(iconBox);
            customTitleBar.Controls.Add(titleLabel);
            customTitleBar.Controls.Add(buttonHost);
            Controls.Add(customTitleBar);
            customTitleBar.BringToFront();
        }

        private Button CreateTitleBarButton(string text, int left, Action action)
        {
            var button = new Button
            {
                Text = text,
                ForeColor = Color.FromArgb(235, 226, 214),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Location = new Point(left, 0),
                Size = new Size(44, CustomTitleBarHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TabStop = false,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = text == "X" ? Color.FromArgb(126, 42, 32) : Color.FromArgb(64, 42, 28);
            button.FlatAppearance.MouseDownBackColor = text == "X" ? Color.FromArgb(86, 24, 18) : Color.FromArgb(38, 26, 20);
            button.Click += (_, _) => action();
            return button;
        }

        private void CustomTitleBar_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }

        private sealed class GradientTitleBar : Panel
        {
            public GradientTitleBar()
            {
                DoubleBuffered = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    ClientRectangle,
                    Color.FromArgb(8, 7, 6),
                    Color.FromArgb(55, 35, 23),
                    0f);
                e.Graphics.FillRectangle(brush, ClientRectangle);

                // Alt çizgi kaldırıldı — form arkaplanıyla birleşik görünüm için
            }
        }

        private WaveStream? audioFile;
        private WaveOutEvent? outputDevice;

        private void PlayMusicFromResources()
        {
            try
            {
                // 1. Eğer halihazırda çalan bir şey varsa temizle
                outputDevice?.Stop();
                outputDevice?.Dispose();
                audioFile?.Dispose();

                // 2. Resource'u al ve Mp3FileReader'a ver
                // UnmanagedMemoryStream doğrudan kabul edilir
                var mp3Reader = new Mp3FileReader(Properties.Resources.DistantHorizons_LauncherMenu);

                // 3. Değişkenimize atıyoruz (WaveStream her türlü Reader'ı kabul eder)
                audioFile = mp3Reader;

                outputDevice = new WaveOutEvent();
                outputDevice.Init(audioFile);
                outputDevice.Play();

                // Döngü (Loop) mantığı
                outputDevice.PlaybackStopped += (s, e) =>
                {
                    if (audioFile != null)
                    {
                        audioFile.Position = 0;
                        outputDevice?.Play();
                        outputDevice.Volume = trackBarVolume.Value / 100f;
                    }
                };
            }
            catch (Exception ex)
            {
                Log("Müzik çalarken hata: " + ex.Message, Color.Red);
            }
        }

        // Varsayılan olarak bir sürüm belirleyelim (opsiyonel)
        private string secilenSurumID= "";
        private List<ModPack> modPacks = new List<ModPack>();
        private ModPack? selectedPack;
        private string secilenSurumFolder = "";
        private string secilenPaketisim = "";

        private static readonly System.Text.RegularExpressions.Regex _usernameRegex =
            new(@"^[a-zA-Z0-9]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private readonly Color themeBack = Color.FromArgb(9, 8, 7);
        private readonly Color themePanel = Color.FromArgb(22, 19, 17);
        private readonly Color themeSurface = Color.FromArgb(26, 22, 20);
        private readonly Color themeSurfaceHover = Color.FromArgb(57, 40, 27);
        private readonly Color themeBorder = Color.FromArgb(50, 40, 34);
        private readonly Color themeText = Color.FromArgb(235, 226, 214);
        private readonly Color themeMutedText = Color.FromArgb(168, 151, 133);
        private readonly Color themeSelected = Color.FromArgb(150, 94, 43);
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Title bar ile aynı gradient (soldan sağa): koyu siyah → sıcak kahverengi
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                ClientRectangle,
                Color.FromArgb(8, 7, 6),
                Color.FromArgb(55, 35, 23),
                0f);
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        private void ApplyDarkLauncherTheme()
        {
            BackColor = Color.FromArgb(8, 7, 6); // Gradient override edildiği için fallback
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            // ── Panel'ler ──────────────────────────────────────────────
            panelSurumler.BackColor = Color.FromArgb(13, 11, 10);

            // Müzik scroller paneli: koyu yüzey + custom border paint
            pnlScroller.BackColor = Color.FromArgb(18, 15, 13);
            pnlScroller.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, pnlScroller.Width - 1, pnlScroller.Height - 1);
                using var pen = new Pen(Color.FromArgb(55, 42, 32), 1f);
                int r = 4;
                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
                path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
                path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
                path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
                path.CloseFigure();
                g.DrawPath(pen, path);
            };

            // ── TextBox / ComboBox ─────────────────────────────────────
            txtUsername.BackColor = themeSurface;
            txtUsername.ForeColor = themeText;
            txtUsername.BorderStyle = BorderStyle.FixedSingle;
            txtUsername.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

            comboBoxRam.BackColor = themeSurface;
            comboBoxRam.ForeColor = themeText;

            // ── Konsol ────────────────────────────────────────────────
            rtbConsole.BackColor = Color.FromArgb(7, 6, 5);
            rtbConsole.ForeColor = Color.FromArgb(199, 187, 171);
            rtbConsole.BorderStyle = BorderStyle.None;

            // ── TrackBar (Ses Düzeyi) ──────────────────────────────────
            // ModernTrackBar zaten temayı dahili olarak uygular,
            // sadece arka planını şeffaf yapıp konumunu garantiliyoruz.
            // TrackBar arka planı OnPaintBackground ile parent rengini alıyor
            trackBarVolume.Size = new Size(124, 26);

            // ── Label'lar ─────────────────────────────────────────────
            // label1 = "Ses Düzeyi" başlığı
            label1.ForeColor = Color.FromArgb(140, 118, 96);
            label1.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
            label1.BackColor = Color.Transparent;

            // label2 = "Şu anda çalıyor..." - vurgulu turuncu
            label2.Text = "♪  Şu anda çalıyor...";
            label2.ForeColor = Color.FromArgb(196, 130, 65);
            label2.Font = new Font("Segoe UI Semibold", 8.75F, FontStyle.Bold);
            label2.BackColor = Color.Transparent;

            // label3 = "Sürümler" başlığı
            label3.ForeColor = themeText;
            label3.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);

            // label4 = "Ram Miktarı" başlığı
            label4.ForeColor = themeMutedText;
            label4.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            // lblScrollingText = kayan şarkı adı - italik, soluk altın
            lblScrollingText.ForeColor = Color.FromArgb(205, 182, 152);
            lblScrollingText.Font = new Font("Segoe UI", 8.5F, FontStyle.Italic);
            lblScrollingText.BackColor = Color.FromArgb(18, 15, 13); // Panel rengiyle aynı (Transparent Panel içinde çalışmaz)

            // ── Oynat Butonu ───────────────────────────────────────────
            btnPlay.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            btnPlay.ForeColor = themeText;
            btnPlay.ButtonColor = Color.FromArgb(53, 35, 24);
            btnPlay.HoverColor = Color.FromArgb(94, 57, 30);
            btnPlay.PressedColor = Color.FromArgb(18, 13, 10);
            btnPlay.BorderColor = Color.FromArgb(122, 76, 39);
            btnPlay.BorderRadius = 8;

            // ── Sürüm Butonları ────────────────────────────────────────
            StyleVersionButton(btnVanilla);
            StyleVersionButton(btnAGLR);
            StyleVersionButton(btnRNG);
        }

        private void StyleVersionButton(ModernImageButton button)
        {
            button.Cursor = Cursors.Hand;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.BorderColor = themeBorder;
            button.FlatAppearance.MouseOverBackColor = themeSurfaceHover;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(8, 8, 9);
            button.BackColor = Color.FromArgb(22, 19, 17);
            button.BaseColor = themePanel;
            button.ForeColor = themeText;
            button.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            button.BorderRadius = 6;
            button.TextAlign = ContentAlignment.MiddleCenter;
        }

        private async void btnPlay_Click(object sender, EventArgs e)
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            rtbConsole.Clear();

            // 1. Kullanıcı adı kontrolü
            if (string.IsNullOrWhiteSpace(txtUsername.Text))
            {
                Log("Lütfen bir kullanıcı adı girin!", Color.Red);
                return;
            }

            // 2. Kullanıcı adı karakter kontrolü
            if (!_usernameRegex.IsMatch(txtUsername.Text))
            {
                rtbConsole.Visible = true;
                Log("Kullanıcı adınız uygun değil, sadece ingilizce karakterleri ve sayıları kullanabilirsiniz.", Color.Red);
                return;
            }

            // 2. Sürüm seçilmiş mi kontrolü (Yeni değişkenin)
            if (string.IsNullOrEmpty(secilenSurumID))
            {
                Log("Lütfen bir sürüm seçin!", Color.Red);
                return;
            }

            try
            {
                // UI Hazırlığı
                btnPlay.Visible = false;
                rtbConsole.Visible = true;
                Log($"{secilenPaketisim} hazırlanıyor...", Color.Cyan);

                // Klasör yollarını ayarla
                string core = Path.Combine(basePath, "minecraft_core");
                // Her sürümün dosyalarını ayrı klasörde tutmak için secilenSurumID kullanıyoruz
                string instance = Path.Combine(basePath, "instances", secilenSurumFolder);

                if (!Directory.Exists(instance)) Directory.CreateDirectory(instance);

                // CmlLib kurulumu
                var mcPath = new MinecraftPath(instance);
                mcPath.Assets = Path.Combine(core, "assets");
                mcPath.Library = Path.Combine(core, "libraries");
                mcPath.Versions = Path.Combine(core, "versions");

                var launcher = new CMLauncher(mcPath);

                // Eventleri bağla
                launcher.FileChanged += (ev) =>
                {
                    Log($"[{ev.FileKind}] {ev.FileName} kontrol ediliyor... ({ev.ProgressedFileCount}/{ev.TotalFileCount})", Color.White);
                };

                // Sürümleri tara
                await launcher.GetAllVersionsAsync();

                // Launch ayarları
                var launchOption = new MLaunchOption
                {
                    // RAM seçimini ComboBox'tan almaya devam ediyoruz
                    MaximumRamMb = RAM(comboBoxRam.SelectedItem?.ToString() ?? "2048"),
                    Session = MSession.GetOfflineSession(txtUsername.Text),
                    JavaPath = Path.Combine(basePath, "java", "bin", "java.exe")
                };

                // SÜREÇ BAŞLATMA (Artık değişkeninden gelen ID'yi kullanıyor)
                var process = await launcher.CreateProcessAsync(secilenSurumID, launchOption);
                Log("ARGS: " + process.StartInfo.Arguments, Color.Yellow);


                // Konsol yönlendirmeleri
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                process.OutputDataReceived += (s, ev) => { if (ev.Data != null) Log(ev.Data, Color.Gray); };
                process.ErrorDataReceived += (s, ev) => { if (ev.Data != null) Log("HATA: " + ev.Data, Color.Red); };

                // Müziği durdur (Gömülü resource'tan çaldığımız versiyon)
                //if (outputDevice != null)
                //{
                //    outputDevice.Stop();
                //    Log("Arka plan müziği durduruldu.", Color.Yellow);
                //}

                process.EnableRaisingEvents = true; // ← Bu olmadan Exited çalışmaz!
                process.Exited += (s, ev) =>
                {
                    this.Invoke(() =>
                    {
                        btnPlay.Visible = true;
                        Log("Oyun kapatıldı. İyi günler!", Color.Yellow);
                    });
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                Log("Oyun başlatıldı! İyi oyunlar.", Color.Lime);
            }
            catch (Exception ex)
            {
                Log("Hata oluştu: " + ex.Message, Color.Red);
                MessageBox.Show($"Hata: {ex.Message}");
                btnPlay.Visible = true; // Hata olursa buton geri gelsin
            }
            // NOT: Finally bloğunu sildim çünkü oyna butonuna basar basmaz butonu geri getiriyordu, 
            // logları görmeni engeller. Buton kontrolünü catch ve process.Exited içinde yapmak daha sağlıklı.
        }

        private sealed class GradientLabel : Label
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    ClientRectangle,
                    Color.FromArgb(255, 220, 140), // üst — açık altın
                    Color.FromArgb(160, 80, 20),   // alt — koyu bakır
                    System.Drawing.Drawing2D.LinearGradientMode.Vertical);

                e.Graphics.DrawString(Text, Font, brush, 0, 0);
            }
        }

        public class ModPack
        {
            public string Name { get; set; } = string.Empty;
            public string VersionId { get; set; } = string.Empty;
            public string FolderName { get; set; } = string.Empty;

            public override string ToString() => Name;
        }
        private readonly LoginService _loginService = new();
        private async void Form1_Load(object sender, EventArgs e)
        {
            using var login = new LoginForm(_loginService);
            if (login.ShowDialog() != DialogResult.OK)
            {
                Application.Exit();
                return;
            }
            txtUsername.Text = _loginService.Username; // login servisinden al
            txtUsername.ReadOnly = true;
            // txtUsername'i gizle
            txtUsername.Visible = false;

            // Hoşgeldin label'ı ekle
            var lblWelcome = new GradientLabel
            {
                Text = $"Hoşgeldin, {_loginService.Username}",
                Font = new Font("Segoe UI Black", 12F, FontStyle.Bold),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            Controls.Add(lblWelcome);
            lblWelcome.BringToFront();
            // Ortak merkez X — pictureGL'e göre
            int centerX = pictureGL.Left + pictureGL.Width / 2;

            // pictureGL zaten yerinde

            // Hoşgeldin label
            lblWelcome.Location = new Point(
                centerX - lblWelcome.PreferredWidth / 2,
                txtUsername.Location.Y);

            // Oyna butonu
            btnPlay.Location = new Point(
                centerX - btnPlay.Width / 2,
                btnPlay.Location.Y);

            // Console (rtbConsole)
            rtbConsole.Location = new Point(
                centerX - rtbConsole.Width / 2,
                rtbConsole.Location.Y);

            SplashForm splash = new SplashForm();
            splash.TopMost = true; // Kesin çözüm burası: Her zaman en üstte kalır
            splash.Show();

            // Arka plan işlemlerini başlat (Örn: Drive'dan dosya indirme, log klasörlerini oluşturma)
            await PerformLoadingTasksAsync();

            // İşlemler bittiğinde ana formu görünür yap ve görev çubuğuna geri getir
            this.Opacity = 1;
            this.ShowInTaskbar = true;

            // Ana form arkada belirdiği an, üstteki Splash formunu yavaşça kaybet
            // Kontrol kısmını arka planda yap (splash açıkken)
            var updatePlan = await BuildUpdatePlanAsync();

            // Splash kapansın, form görünsün
            splash.FadeOutAndClose();

            // Artık dialog göstermek güvenli
            await ApplyUpdatePlanAsync(updatePlan);
            // Form1_Load içinde
            btnAGLR.Tag = new ModPack { VersionId = "1.20.1-forge-47.4.10", FolderName = "AGLR" };
            btnVanilla.Tag = new ModPack { VersionId = "1.20.1", FolderName = "Vanilla" };
            btnRNG.Tag = new ModPack { VersionId = "1.20.1", FolderName = "RNG" };
            comboBoxRam.SelectedIndex = 1; // Varsayılan olarak 8GB'ı seçili yapalım
            PlayMusicFromResources();
            await CheckLauncherVersionAsync();
            if (_loginService.IsAdmin)
            {
                var btnAdmin = new RoundedButton
                {
                    Text = "⚙ Veritabanı",
                    Size = new Size(110, 22),
                    Location = new Point(ClientSize.Width - 206, 8),
                    Font = new Font("Segoe UI", 7.5F),
                    ForeColor = Color.FromArgb(194, 126, 62),
                    ButtonColor = Color.FromArgb(22, 16, 10),
                    HoverColor = Color.FromArgb(45, 30, 16),
                    PressedColor = Color.FromArgb(12, 9, 6),
                    BorderColor = Color.FromArgb(80, 55, 28),
                    BorderRadius = 6
                };
                btnAdmin.Click += (_, _) => new AdminPanel(_loginService.Token!).ShowDialog(this);
                Controls.Add(btnAdmin);
                btnAdmin.BringToFront();
            }
            panelSurumler.AutoScroll = true;
            // mevcut butonların Height'ını 80px yap (Margin ile aralık)
        }

        // Sadece kontrol eder, indirir/dialog açmaz — arka planda çalışır
        private async Task<List<(string instanceName, string instancePath, InstanceManifest manifest, bool modsGuncellemesiVar, List<ManifestFile> toUpdate, List<string> toDelete)>> BuildUpdatePlanAsync()
        {
            var plan = new List<(string, string, InstanceManifest, bool, List<ManifestFile>, List<string>)>();

            string instancesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "instances");
            if (!Directory.Exists(instancesPath)) return plan;

            foreach (var instanceName in _loginService.AllowedPacks)
            {
                string instancePath = Path.Combine(instancesPath, instanceName);
                if (!Directory.Exists(instancePath)) continue;

                try
                {
                    var manifest = await _manifestService.GetManifestAsync();
                    if (manifest?.Instances == null || !manifest.Instances.TryGetValue(instanceName, out var instanceManifest))
                        continue;

                    bool modsGuncellemesiVar = false;
                    if (!string.IsNullOrEmpty(instanceManifest.ModsVersion) &&
                        !string.IsNullOrEmpty(instanceManifest.ModsUrl))
                    {
                        string localModsVersionPath = Path.Combine(instancePath, ".modsversion");
                        string localModsVersion = File.Exists(localModsVersionPath)
                            ? File.ReadAllText(localModsVersionPath).Trim()
                            : "";
                        modsGuncellemesiVar = localModsVersion != instanceManifest.ModsVersion;
                    }

                    var (toUpdate, toDelete) = await _manifestService.GetChangesAsync(instancePath, instanceName);

                    if (modsGuncellemesiVar || toUpdate.Count > 0 || toDelete.Count > 0)
                        plan.Add((instanceName, instancePath, instanceManifest, modsGuncellemesiVar, toUpdate, toDelete));
                }
                catch (Exception ex)
                {
                    Log($"❌ {instanceName} kontrol hatası: {ex.Message}", Color.FromArgb(180, 60, 50));
                }
            }

            return plan;
        }

        // Dialog açar ve indirir — splash kapandıktan sonra çağrılır
        private async Task ApplyUpdatePlanAsync(List<(string instanceName, string instancePath, InstanceManifest manifest, bool modsGuncellemesiVar, List<ManifestFile> toUpdate, List<string> toDelete)> plan)
        {
            foreach (var (instanceName, instancePath, instanceManifest, modsGuncellemesiVar, toUpdate, toDelete) in plan)
            {
                var satirlar = new System.Text.StringBuilder();
                satirlar.AppendLine($"{instanceName} için güncelleme mevcut:");
                if (modsGuncellemesiVar)
                    satirlar.AppendLine($"  • Mods paketi → v{instanceManifest.ModsVersion}");
                if (toUpdate.Count > 0)
                {
                    double toplamMB = toUpdate.Sum(f => f.Size) / 1024.0 / 1024.0;
                    satirlar.AppendLine($"  • {toUpdate.Count} config/kubejs dosyası ({toplamMB:F1} MB)");
                }
                if (toDelete.Count > 0)
                    satirlar.AppendLine($"  • {toDelete.Count} eski dosya silinecek");
                satirlar.Append("Şimdi güncellensin mi?");

                bool onay = ShowThemedConfirm("Güncelleme Mevcut", satirlar.ToString());

                if (!onay)
                {
                    Log($"⏭ {instanceName} güncellemesi ertelendi.", Color.Gray);
                    continue;
                }

                if (modsGuncellemesiVar)
                {
                    Log($"📦 {instanceName} mods güncelleniyor ({instanceManifest.ModsVersion})...", Color.FromArgb(194, 126, 62));
                    string zipPath = Path.Combine(Path.GetTempPath(), $"{instanceName}-mods.zip");

                    await _manifestService.DownloadFileAsync(
                        instanceManifest.ModsUrl!, zipPath,
                        new Progress<int>(p => UpdateLastLog($"⬇ Mods indiriliyor... %{p}", Color.FromArgb(194, 126, 62))));

                    string modsPath = Path.Combine(instancePath, "mods");
                    if (Directory.Exists(modsPath)) Directory.Delete(modsPath, recursive: true);

                    ZipFile.ExtractToDirectory(zipPath, instancePath, overwriteFiles: true);
                    File.Delete(zipPath);
                    File.WriteAllText(Path.Combine(instancePath, ".modsversion"), instanceManifest.ModsVersion);
                    Log($"✅ {instanceName} mods güncellendi!", Color.FromArgb(80, 160, 80));
                }

                if (toDelete.Count > 0)
                {
                    Log($"🗑 {instanceName}: {toDelete.Count} dosya siliniyor...", Color.FromArgb(180, 80, 60));
                    foreach (var path in toDelete)
                    {
                        File.Delete(path);
                        Log($"✓ Silindi: {Path.GetFileName(path)}", Color.FromArgb(140, 118, 96));
                    }
                }

                if (toUpdate.Count > 0)
                {
                    Log($"📦 {instanceName}: {toUpdate.Count} dosya indiriliyor...", Color.FromArgb(194, 126, 62));
                    int current = 0;
                    foreach (var file in toUpdate)
                    {
                        current++;
                        string localPath = Path.Combine(instancePath, file.Path.Replace('/', '\\'));
                        UpdateLastLog($"⬇ İndiriliyor [{current}/{toUpdate.Count}]: {Path.GetFileName(file.Path)}", Color.FromArgb(194, 126, 62));
                        await _manifestService.DownloadFileAsync(file.Url, localPath);
                    }
                }

                Log($"✅ {instanceName} güncellendi.", Color.FromArgb(80, 160, 80));
            }
        }


        private void UpdateLastLog(string message, Color color)
        {
            if (rtbConsole.InvokeRequired)
            {
                rtbConsole.Invoke(() => UpdateLastLog(message, color));
                return;
            }

            // Son satırı bul ve sadece onu değiştir
            int lastLine = rtbConsole.Lines.Length - 1;
            if (lastLine >= 0)
            {
                int start = rtbConsole.GetFirstCharIndexFromLine(lastLine);
                int length = rtbConsole.Lines[lastLine].Length;

                rtbConsole.Select(start, length);
                rtbConsole.SelectionColor = color;
                rtbConsole.SelectedText = $"[{DateTime.Now:HH:mm:ss}] {message}";
            }

            rtbConsole.SelectionColor = rtbConsole.ForeColor;
            rtbConsole.ScrollToCaret();
        }

        private async Task PerformLoadingTasksAsync()
        {
            await Task.Run(() =>
            {
                // Gerçek işlemlerin süresini simüle etmek için
                System.Threading.Thread.Sleep(1500);
            });
        }

        private void Log(string message, Color color)
        {
            if (rtbConsole.InvokeRequired)
            {
                rtbConsole.Invoke(new Action(() => Log(message, color)));
                return;
            }

            rtbConsole.SelectionStart = rtbConsole.TextLength;
            rtbConsole.SelectionLength = 0;
            rtbConsole.SelectionColor = color;

            // Başına saat ekleyelim, profesyonel dursun
            rtbConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

            rtbConsole.SelectionColor = rtbConsole.ForeColor; // Rengi eski haline döndür
            rtbConsole.ScrollToCaret(); // Her zaman en alt satırı göster
        }

        private void trackBarVolume_Scroll(object sender, EventArgs e)
        {
            if (outputDevice != null)
            {
                // outputDevice.Volume doğrudan 0.0 ile 1.0 arası çalışır
                outputDevice.Volume = trackBarVolume.Value / 100f;
            }
        }

        private void timerScroll_Tick(object sender, EventArgs e)
        {
            // Label'ı sola doğru kaydır
            lblScrollingText.Left -= 2;

            // EĞER yazının sonu (en sağ ucu), panelin solundan tamamen çıktıysa
            // Yazıyı panelin en sağından tekrar içeri sok
            if (lblScrollingText.Right < 0)
            {
                lblScrollingText.Left = pnlScroller.Width;
            }
        }

        private void comboBoxRam_SelectedIndexChanged(object sender, EventArgs e)
        {


        }

        public int RAM(string miktar)
        {
            switch (miktar)
            {
                case "4GB":
                    return 4096;
                case "8GB":
                    return 8192;
                case "8GB (Önerilen)":
                    return 8192;
                case "12GB":
                    return 12288;
                case "16GB":
                    return 16384;
                default:
                    return 8192;
            }
        }

        private string? ShowSecurityPrompt()
        {
            using var dialog = new Form
            {
                Text = "Güvenlik Kontrolü",
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(460, 250),
                BackColor = themeBack,
                ShowInTaskbar = false,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var header = new GradientTitleBar
            {
                Dock = DockStyle.Top,
                Height = 34
            };
            header.MouseDown += (_, e) => DragWindow(dialog, e);

            var title = new Label
            {
                Text = "Güvenlik Kontrolü",
                AutoSize = true,
                ForeColor = themeText,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Location = new Point(16, 8),
                BackColor = Color.Transparent
            };
            title.MouseDown += (_, e) => DragWindow(dialog, e);

            var close = CreateDialogButton("X", new Size(38, 34));
            close.Dock = DockStyle.Right;
            close.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel;

            header.Controls.Add(title);
            header.Controls.Add(close);

            var message = new Label
            {
                Text = "Bu sürüme erişmek için çok önemli olan bir yıl dönümünden, çok önemli olan birinin doğum gününü çıkar. (Gün,Ay,Yıl)",
                ForeColor = themeText,
                BackColor = Color.Transparent,
                Location = new Point(24, 58),
                Size = new Size(408, 48)
            };

            var input = new TextBox
            {
                BackColor = Color.FromArgb(18, 15, 13),
                ForeColor = themeText,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(24, 126),
                Size = new Size(408, 28),
                Font = new Font("Segoe UI", 10F, FontStyle.Regular)
            };

            var cancel = CreateDialogButton("İptal", new Size(96, 34));
            cancel.Location = new Point(226, 184);
            cancel.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel;

            var ok = CreateDialogButton("Tamam", new Size(96, 34));
            ok.Location = new Point(332, 184);
            ok.BackColor = Color.FromArgb(73, 43, 25);
            ok.Click += (_, _) => dialog.DialogResult = DialogResult.OK;

            dialog.Controls.Add(header);
            dialog.Controls.Add(message);
            dialog.Controls.Add(input);
            dialog.Controls.Add(cancel);
            dialog.Controls.Add(ok);
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;

            return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
        }

        private void ShowThemedAlert(string title, string message)
        {
            using var dialog = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(380, 170),
                BackColor = themeBack,
                ShowInTaskbar = false,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var header = new GradientTitleBar { Dock = DockStyle.Top, Height = 34 };
            header.MouseDown += (_, e) => DragWindow(dialog, e);
            header.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                ForeColor = themeText,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Location = new Point(16, 8),
                BackColor = Color.Transparent
            });

            var body = new Label
            {
                Text = message,
                ForeColor = themeText,
                BackColor = Color.Transparent,
                Location = new Point(24, 58),
                Size = new Size(332, 42)
            };

            var ok = CreateDialogButton("Tamam", new Size(96, 34));
            ok.Location = new Point(260, 118);
            ok.BackColor = Color.FromArgb(73, 43, 25);
            ok.Click += (_, _) => dialog.DialogResult = DialogResult.OK;

            dialog.Controls.Add(header);
            dialog.Controls.Add(body);
            dialog.Controls.Add(ok);
            dialog.AcceptButton = ok;
            dialog.ShowDialog(this);
        }

        private Button CreateDialogButton(string text, Size size)
        {
            var button = new Button
            {
                Text = text,
                Size = size,
                ForeColor = themeText,
                BackColor = themeSurface,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = themeSelected;
            button.FlatAppearance.MouseOverBackColor = themeSurfaceHover;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(18, 13, 10);
            return button;
        }

        private void DragWindow(Form form, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            ReleaseCapture();
            SendMessage(form.Handle, 0xA1, 0x2, 0);
        }

        private void btnVanilla_Click(object sender, EventArgs e)
        {
            rtbConsole.Clear();
            for (int i = 0; i < comboBoxRam.Items.Count; i++)
            {
                string mevcutYazi = comboBoxRam.Items[i]?.ToString() ?? string.Empty;
                if (mevcutYazi.Contains(" (Önerilen)"))
                {
                    comboBoxRam.Items[i] = mevcutYazi.Replace(" (Önerilen)", "");
                }
            }

            int index4GB = comboBoxRam.Items.IndexOf("4GB");
            if (index4GB != -1)
            {
                comboBoxRam.Items[index4GB] = "4GB (Önerilen)";
                comboBoxRam.SelectedIndex = index4GB;
            }
            SurumSec(sender);
            pictureGL.Image = Properties.Resources.minecraftlogo;
        }

        private void SurumSec(object sender)
        {
            Button btn = sender as Button;

            if (btn != null && btn.Tag != null)
            {
                // 1. Değişkeni güncelle (Hafızaya al)
                if (btn.Tag is ModPack pack)
                {
                    secilenSurumID = pack.VersionId;      // CmlLib için
                    secilenSurumFolder = pack.FolderName; // klasör için
                }
                secilenPaketisim = btn.Text; // Görsel efektler ve log için (opsiyonel)
                // 2. Log yazdır
                Log($"Sürüm Seçildi: {btn.Text}", Color.Yellow);

                // 3. Görsel efektleri yönet
                ButonlariResetle(); // Diğerlerinin çerçevesini sil

                btn.FlatStyle = FlatStyle.Flat; // Kenarlığın görünmesi için gerekli
                btn.FlatAppearance.BorderSize = 2;
                btn.FlatAppearance.BorderColor = Color.Lime; // Seçileni parlat
            }
        }

        private void ButonlariResetle()
        {
            // "panelSolMenu" yerine kendi panelinin tam adını yazmalısın
            foreach (var ctrl in panelSurumler.Controls.OfType<Button>())
            {
                ctrl.FlatAppearance.BorderSize = 0;
                ctrl.FlatAppearance.BorderColor = themeBorder;

                if (ctrl is ModernImageButton imageButton)
                {
                    imageButton.Selected = false;
                    imageButton.Invalidate();
                }
            }
        }

        private void btnAGLR_Click(object sender, EventArgs e)
        {
            rtbConsole.Clear();
            for (int i = 0; i < comboBoxRam.Items.Count; i++)
            {
                string mevcutYazi = comboBoxRam.Items[i]?.ToString() ?? string.Empty;
                if (mevcutYazi.Contains(" (Önerilen)"))
                {
                    comboBoxRam.Items[i] = mevcutYazi.Replace(" (Önerilen)", "");
                }
            }

            int index8GB = comboBoxRam.Items.IndexOf("8GB");
            if (index8GB != -1)
            {
                comboBoxRam.Items[index8GB] = "8GB (Önerilen)";
                comboBoxRam.SelectedIndex = index8GB;
            }
            SurumSec(sender);
            pictureGL.Image = Properties.Resources.aglr;
        }

        private void btnRNG_Click(object sender, EventArgs e)
        {
            string? cevap = ShowSecurityPrompt();

            if (cevap == null)
                return; // İptal edildi

            
            const string dogruCevap = "29217";

            if (cevap.Trim() == dogruCevap)
            {
                SurumSec(sender);
                Log("Erişim Onaylandı", Color.Green);
                rtbConsole.Clear();
            }
            else
            {
                ShowThemedAlert("Erişim Reddedildi", "Yanlış cevap. Bu sürüme erişim izniniz yok.");
                Log("Erişim Reddedildi", Color.Red);
                rtbConsole.Clear();
            }
        }
    }
}
