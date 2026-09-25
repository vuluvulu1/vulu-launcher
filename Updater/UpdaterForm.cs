using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace Updater
{
    public class UpdaterForm : Form
    {
        // ── Tema ─────────────────────────────────────────────────────────
        private readonly Color _back = Color.FromArgb(9, 8, 7);
        private readonly Color _accent = Color.FromArgb(194, 126, 62);
        private readonly Color _text = Color.FromArgb(235, 226, 214);
        private readonly Color _muted = Color.FromArgb(140, 118, 96);
        private readonly Color _border = Color.FromArgb(50, 40, 34);

        // ── Kontroller ────────────────────────────────────────────────────
        private Label _lblStatus = null!;
        private Label _lblPercent = null!;
        private Label _lblSize = null!;
        private CustomProgressBar _progress = null!;

        private readonly string _downloadUrl;
        private readonly string _launcherPath;

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        public UpdaterForm(string downloadUrl, string launcherPath)
        {
            _downloadUrl = downloadUrl;
            _launcherPath = launcherPath;
            BuildUI();
        }

        private void BuildUI()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(420, 175);
            BackColor = _back;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9F);

            // ── Title bar ─────────────────────────────────────────────────
            var titleBar = new GradientPanel(Color.FromArgb(8, 7, 6), Color.FromArgb(38, 26, 18))
            {
                Dock = DockStyle.Top,
                Height = 34
            };
            titleBar.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            };
            titleBar.Controls.Add(new Label
            {
                Text = "vulu Launcher — Güncelleniyor",
                ForeColor = _text,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(14, 9),
                BackColor = Color.Transparent
            });

            var divider = new Panel
            {
                BackColor = _border,
                Location = new Point(0, 34),
                Size = new Size(420, 1)
            };

            // ── Durum ─────────────────────────────────────────────────────
            _lblStatus = new Label
            {
                Text = "Güncelleme hazırlanıyor...",
                ForeColor = _accent,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 50),
                BackColor = Color.Transparent
            };

            // ── Progress bar ──────────────────────────────────────────────
            _progress = new CustomProgressBar
            {
                Location = new Point(24, 82),
                Size = new Size(372, 12)
            };

            // ── Yüzde ─────────────────────────────────────────────────────
            _lblPercent = new Label
            {
                Text = "%0",
                ForeColor = _text,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 104),
                BackColor = Color.Transparent
            };

            // ── İndirilen / Toplam ────────────────────────────────────────
            _lblSize = new Label
            {
                Text = "",
                ForeColor = _muted,
                Font = new Font("Segoe UI", 8.5F),
                AutoSize = true,
                Location = new Point(300, 106),
                BackColor = Color.Transparent
            };

            // ── Alt bilgi ─────────────────────────────────────────────────
            var lblInfo = new Label
            {
                Text = "Tamamlanınca launcher otomatik açılacak.",
                ForeColor = _muted,
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                AutoSize = true,
                Location = new Point(24, 148),
                BackColor = Color.Transparent
            };

            Controls.AddRange(new Control[]
            {
                titleBar, divider,
                _lblStatus, _progress,
                _lblPercent, _lblSize,
                lblInfo
            });
            titleBar.BringToFront();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _ = StartDownloadAsync();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new LinearGradientBrush(
                ClientRectangle,
                Color.FromArgb(8, 7, 6),
                Color.FromArgb(28, 18, 12),
                LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        // ── İndirme ve uygulama ───────────────────────────────────────────
        private async Task StartDownloadAsync()
        {
            try
            {
                _lblStatus.Text = "İndiriliyor...";
                string zipPath = Path.Combine(Path.GetTempPath(), "vulu_update.exe"); // .zip → .exe

                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "vulu-launcher");

                using var response = await http.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var file = File.Create(zipPath);

                byte[] buffer = new byte[81920];
                long downloaded = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read));
                    downloaded += read;

                    if (totalBytes > 0)
                    {
                        int percent = (int)(downloaded * 100 / totalBytes);
                        double dlMB = downloaded / 1024.0 / 1024.0;
                        double totMB = totalBytes.Value / 1024.0 / 1024.0;

                        Invoke(() =>
                        {
                            _progress.Value = percent;
                            _lblPercent.Text = $"%{percent}";
                            _lblSize.Text = $"{dlMB:F1} MB / {totMB:F1} MB";
                        });
                    }
                }

                // stream ve file kapandıktan sonra işlem yap
                file.Close();

                _lblStatus.Text = "Dosyalar yerleştiriliyor...";
                _progress.Value = 100;
                _lblPercent.Text = "%100";

                await Task.Run(() => ApplyUpdate(zipPath)); // ← burada çağrılıyor

                _lblStatus.Text = "Tamamlandı! Launcher açılıyor...";
                await Task.Delay(1200);

                if (!string.IsNullOrEmpty(_launcherPath) && File.Exists(_launcherPath))
                    System.Diagnostics.Process.Start(_launcherPath);

                Application.Exit();
            }
            catch (Exception ex)
            {
                _lblStatus.ForeColor = Color.FromArgb(210, 80, 60);
                _lblStatus.Text = "Hata: " + ex.Message;
            }
        }

        private void InitializeComponent()
        {

        }

        private void ApplyUpdate(string exePath)
        {
            string targetPath = _launcherPath;
            string backup = _launcherPath + ".bak";

            if (File.Exists(targetPath))
                File.Move(targetPath, backup, overwrite: true);

            try
            {
                File.Copy(exePath, targetPath, overwrite: true);
                if (File.Exists(backup)) File.Delete(backup);
                if (File.Exists(exePath)) File.Delete(exePath);
            }
            catch
            {
                if (File.Exists(backup) && !File.Exists(targetPath))
                    File.Move(backup, targetPath);
                throw;
            }
        }

        // ── Özel Progress Bar ─────────────────────────────────────────────
        private sealed class CustomProgressBar : Control
        {
            private int _value;

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public int Maximum { get; set; } = 100;

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public int Value
            {
                get => _value;
                set { _value = Math.Max(0, Math.Min(Maximum, value)); Invalidate(); }
            }

            public CustomProgressBar()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, Width - 1, Height - 1);

                // Arka plan
                using (var bg = new SolidBrush(Color.FromArgb(32, 26, 20)))
                using (var bgPath = Round(r, 5))
                    e.Graphics.FillPath(bg, bgPath);

                // Dolu kısım
                if (Value > 0)
                {
                    int fw = Math.Max(10, (int)((Width - 2) * (Value / (float)Maximum)));
                    var fr = new Rectangle(1, 1, fw, Height - 2);
                    using var fillBrush = new LinearGradientBrush(fr,
                        Color.FromArgb(112, 68, 34),
                        Color.FromArgb(194, 126, 62),
                        LinearGradientMode.Horizontal);
                    using var fillPath = Round(fr, 4);
                    e.Graphics.FillPath(fillBrush, fillPath);
                }

                // Kenar
                using (var pen = new Pen(Color.FromArgb(60, 45, 30), 1f))
                using (var borderPath = Round(r, 5))
                    e.Graphics.DrawPath(pen, borderPath);
            }

            private static GraphicsPath Round(Rectangle r, int radius)
            {
                var path = new GraphicsPath();
                int d = radius * 2;
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        // ── Gradient panel ────────────────────────────────────────────────
        private sealed class GradientPanel : Panel
        {
            private readonly Color _from, _to;
            public GradientPanel(Color from, Color to)
            {
                _from = from; _to = to; DoubleBuffered = true;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                using var brush = new LinearGradientBrush(ClientRectangle, _from, _to, 0f);
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }
    }
}
