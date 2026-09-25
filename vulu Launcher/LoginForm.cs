using Microsoft.VisualBasic.Logging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AGLR_Launcher
{
    public class LoginForm : Form
    {
        // ── Tema ──────────────────────────────────────────────────────────
        private readonly Color _back = Color.FromArgb(9, 8, 7);
        private readonly Color _surface = Color.FromArgb(22, 19, 17);
        private readonly Color _border = Color.FromArgb(50, 40, 34);
        private readonly Color _accent = Color.FromArgb(150, 94, 43);
        private readonly Color _accentHot = Color.FromArgb(194, 126, 62);
        private readonly Color _text = Color.FromArgb(235, 226, 214);
        private readonly Color _muted = Color.FromArgb(140, 118, 96);

        // ── Kontroller ────────────────────────────────────────────────────
        private ModernTextBox _txtUsername = null!;
        private ModernTextBox _txtPassword = null!;
        private RoundedButton _btnLogin = null!;
        private Label _lblError = null!;
        private Label _lblStatus = null!;
        private ToggleSwitch _chkRemember = null!;

        private readonly LoginService _loginService;

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        public LoginForm(LoginService loginService)
        {
            _loginService = loginService;
            BuildUI();
        }

        private Label _logoLbl = null!;
        private Label _subLbl = null!;

        private sealed class ToggleSwitch : Control
        {
            private bool _checked;
            private bool _hovered;

            private readonly Color _accent = Color.FromArgb(194, 126, 62);
            private readonly Color _accentDark = Color.FromArgb(112, 68, 34);
            private readonly Color _off = Color.FromArgb(38, 30, 24);
            private readonly Color _border = Color.FromArgb(70, 52, 36);
            private readonly Color _text = Color.FromArgb(140, 118, 96);

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public bool Checked
            {
                get => _checked;
                set { _checked = value; Invalidate(); }
            }

            public ToggleSwitch()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint, true);
                Cursor = Cursors.Hand;
                Size = new Size(140, 22);
            }

            protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnClick(EventArgs e) { _checked = !_checked; Invalidate(); base.OnClick(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                // ── Track (zemin) ─────────────────────────────────────────────
                var track = new Rectangle(0, (Height - 14) / 2, 32, 14);

                using (var trackBrush = new SolidBrush(_checked
                    ? (_hovered ? _accent : _accentDark)
                    : (_hovered ? Color.FromArgb(55, 42, 30) : _off)))
                using (var trackPath = RoundRect(track, 7))
                    e.Graphics.FillPath(trackBrush, trackPath);

                using (var borderPen = new Pen(_checked ? _accent : _border, 1f))
                using (var trackPath = RoundRect(track, 7))
                    e.Graphics.DrawPath(borderPen, trackPath);

                // ── Knob ──────────────────────────────────────────────────────
                int knobX = _checked ? track.Right - 13 : track.X + 1;
                var knob = new Rectangle(knobX, track.Y + 1, 12, 12);
                var knobColor = _checked ? Color.FromArgb(235, 226, 214) : Color.FromArgb(120, 95, 70);

                using var knobBrush = new SolidBrush(knobColor);
                e.Graphics.FillEllipse(knobBrush, knob);

                // ── Yazı ──────────────────────────────────────────────────────
                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    new Rectangle(40, 0, Width - 40, Height),
                    _text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            }

            private static GraphicsPath RoundRect(Rectangle r, int radius)
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


        private void BuildUI()
        {
            // ── Form ayarları ─────────────────────────────────────────────
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(360, 346); // 320 → 346
            BackColor = _back;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9F);


            // ── Title bar ─────────────────────────────────────────────────
            var titleBar = new GradientPanel(_back, Color.FromArgb(38, 26, 18))
            {
                Dock = DockStyle.Top,
                Height = 36
            };
            titleBar.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            };

            var icon = new Label
            {
                Text = "⚔",
                ForeColor = Color.FromArgb(194, 126, 62),
                Font = new Font("Segoe UI", 11F),
                AutoSize = true,
                Location = new Point(14, 7),
                BackColor = Color.Transparent
            };

            var titleLbl = new Label
            {
                Text = "vulu Launcher — Giriş",
                ForeColor = _text,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(46, 9),
                BackColor = Color.Transparent
            };

            var closeBtn = new Button
            {
                Text = "✕",
                ForeColor = _muted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F),
                Size = new Size(36, 36),
                Location = new Point(ClientSize.Width - 36, 0),
                TabStop = false
            };
            closeBtn.FlatAppearance.BorderSize = 0;
            closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(110, 38, 28);
            closeBtn.FlatAppearance.MouseDownBackColor = Color.FromArgb(80, 20, 14);
            closeBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

            titleBar.Controls.AddRange(new Control[] { icon, titleLbl, closeBtn });



            // ── Logo / başlık alanı ───────────────────────────────────────
            _logoLbl = new Label
            {
                Text = "VULU",
                ForeColor = Color.FromArgb(194, 126, 62),
                Font = new Font("Segoe UI Black", 28F, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(280, 44),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(40, 54)
            };
            _logoLbl.Location = new Point((ClientSize.Width - _logoLbl.PreferredWidth) / 2, 54);

            _subLbl = new Label
            {
                Text = "Launcher",
                ForeColor = _muted,
                Font = new Font("Segoe UI", 9F, FontStyle.Italic),
                AutoSize = false,
                Size = new Size(280, 18),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(40, 100)
            };
            _subLbl.Location = new Point((ClientSize.Width - _subLbl.PreferredWidth) / 2, 92);

            // ── Giriş alanları ────────────────────────────────────────────
            var lblUser = MakeFieldLabel("Kullanıcı Adı", 130);
            _txtUsername = MakeTextBox(152);
            _txtUsername.PlaceholderText = "kullanici_adi";

            var lblPass = MakeFieldLabel("Şifre", 196);
            _txtPassword = MakeTextBox(218);
            _txtPassword.PlaceholderText = "••••••••";
            _txtPassword.PasswordChar = '•';
            _txtPassword.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) TryLogin(); };

            // ── Hata etiketi ─────────────────────────────────────────────
            _lblError = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(210, 80, 60),
                Font = new Font("Segoe UI", 8.5F),
                AutoSize = false,
                Size = new Size(280, 16),
                Location = new Point(40, 272), // 254 → 272
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };

            // ── Giriş butonu ─────────────────────────────────────────────
            _btnLogin = new RoundedButton
            {
                Text = "Giriş Yap",
                Size = new Size(280, 40),
    Location = new Point(40, 294), // 274 → 294
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = _text,
                ButtonColor = Color.FromArgb(53, 35, 24),
                HoverColor = Color.FromArgb(94, 57, 30),
                PressedColor = Color.FromArgb(18, 13, 10),
                BorderColor = Color.FromArgb(122, 76, 39),
                BorderRadius = 8
            };

            _btnLogin.Click += (_, _) => TryLogin();

            // ── CheckBox Beni Hatırla ─────────────────────────────────────────────
            _chkRemember = new ToggleSwitch
            {
                Text = "Beni hatırla",
                Location = new Point(40, 248),
                Font = new Font("Segoe UI", 8.5F)
            };

            // ── Durum etiketi (yükleniyor vb.) ───────────────────────────
            _lblStatus = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(194, 126, 62),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                AutoSize = false,
                Size = new Size(280, 14),
                Location = new Point(40, 275), // 257 → 275
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };

            // ── Alt çizgi ─────────────────────────────────────────────────
            var divider = new Panel
            {
                BackColor = Color.FromArgb(38, 30, 24),
                Location = new Point(0, 36),
                Size = new Size(ClientSize.Width, 1)
            };

            // ── Hepsini ekle ─────────────────────────────────────────────
            Controls.AddRange(new Control[]
            {
                titleBar, divider,
                _logoLbl, _subLbl,
                lblUser, _txtUsername,
                lblPass, _txtPassword,
                _chkRemember,
                _lblError, _lblStatus,
                _btnLogin
            });

            titleBar.BringToFront();
            AcceptButton = _btnLogin;

            _txtUsername.KeyPress += (_, e) =>
            {
                // Sadece a-z, A-Z, 0-9, _ ve backspace
                if (!char.IsLetterOrDigit(e.KeyChar) && e.KeyChar != '_' && e.KeyChar != '\b')
                    e.Handled = true;

                // Türkçe ve özel karakter engelle
                if (e.KeyChar > 127)
                    e.Handled = true;
            };

            _txtUsername.MaxLength = 16;
        }



        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            System.ComponentModel.ComponentResourceManager resources = new(typeof(Form1));
            Icon = (Icon)resources.GetObject("$this.Icon");

            _logoLbl.Left = (ClientSize.Width - _logoLbl.Width) / 2;
            _subLbl.Left = (ClientSize.Width - _subLbl.Width) / 2;

            // Kayıtlı bilgileri yükle
            var (username, password) = CredentialManager.Load();
            if (!string.IsNullOrEmpty(username))
            {
                _txtUsername.Text = username;
                _txtPassword.Text = password;
                _chkRemember.Checked = true;
            }

            // Render'ı arka planda uyandır, kullanıcı görmeden hazır olsun
            _ = WakeUpServerAsync();
        }

        private async Task WakeUpServerAsync()
        {
            try
            {
                _lblStatus.Text = "Sunucuya bağlanılıyor...";
                _lblStatus.Visible = true;

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                await http.GetAsync("https://vulu-api.onrender.com/ping");

                _lblStatus.Visible = false;
            }
            catch
            {
                _lblStatus.Text = "Sunucuya ulaşılamadı.";
                _lblStatus.ForeColor = Color.FromArgb(210, 80, 60);
                _lblStatus.Visible = true;
            }
        }

        // ── Login işlemi ─────────────────────────────────────────────────
        private async void TryLogin()
        {
            string username = _txtUsername.Text.Trim();
            string password = _txtPassword.Text;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowError("Kullanıcı adı ve şifre boş bırakılamaz.");
                return;
            }

            SetLoading(true);

            var result = await _loginService.LoginAsync(username, password);

            SetLoading(false);

            if (result.Success)
            {
                if (_chkRemember.Checked)
                    CredentialManager.Save(_txtUsername.Text.Trim(), _txtPassword.Text);
                else
                    CredentialManager.Clear();

                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                ShowError(result.ErrorMessage ?? "Giriş başarısız.");
            }
        }

        private void ShowError(string message)
        {
            _lblError.Text = message;
            _lblError.Visible = true;
            _lblStatus.Visible = false;
        }

        private void SetLoading(bool loading)
        {
            _btnLogin.Enabled = !loading;
            _lblError.Visible = false;
            _lblStatus.Text = loading ? "Sunucuya bağlanılıyor, lütfen bekleyin..." : "";
            _lblStatus.Visible = loading;
        }

        // ── Arka plan gradient ────────────────────────────────────────────
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new LinearGradientBrush(
                ClientRectangle,
                Color.FromArgb(8, 7, 6),
                Color.FromArgb(38, 26, 18),
                LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        // ── Yardımcı factory metodlar ─────────────────────────────────────
        private Label MakeFieldLabel(string text, int top)
        {
            return new Label
            {
                Text = text,
                ForeColor = _muted,
                Font = new Font("Segoe UI", 8.5F),
                AutoSize = true,
                Location = new Point(40, top),
                BackColor = Color.Transparent
            };
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LoginForm));
            SuspendLayout();
            // 
            // LoginForm
            // 
            ClientSize = new Size(284, 261);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "LoginForm";
            ResumeLayout(false);

        }

        private ModernTextBox MakeTextBox(int top)
        {
            return new ModernTextBox
            {
                Location = new Point(40, top),
                Size = new Size(280, 26),
                BackColor = Color.FromArgb(20, 18, 17),
                ForeColor = Color.FromArgb(235, 226, 214),
                Font = new Font("Segoe UI", 9.5F)
            };
        }

        // ── Gradient panel (title bar için) ───────────────────────────────
        private sealed class GradientPanel : Panel
        {
            private readonly Color _from, _to;
            public GradientPanel(Color from, Color to) { _from = from; _to = to; DoubleBuffered = true; }

            protected override void OnPaint(PaintEventArgs e)
            {
                using var brush = new LinearGradientBrush(ClientRectangle, _from, _to, 0f);
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }
    }
}
