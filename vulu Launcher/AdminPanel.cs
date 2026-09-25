using MongoDB.Bson;
using MongoDB.Driver;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AGLR_Launcher
{
    public partial class AdminPanel : Form
    {
        private readonly Color _back = Color.FromArgb(9, 8, 7);
        private readonly Color _accent = Color.FromArgb(194, 126, 62);
        private readonly Color _text = Color.FromArgb(235, 226, 214);
        private readonly Color _muted = Color.FromArgb(140, 118, 96);
        private readonly Color _border = Color.FromArgb(50, 40, 34);
        private readonly Color _green = Color.FromArgb(80, 160, 80);
        private readonly Color _red = Color.FromArgb(180, 60, 50);

        private AdminService? _adminService;
        private ManifestService? _manifestService;
        private List<AdminUser> _users = new();

        // ── Sekmeler ──────────────────────────────────────────────────────
        private Panel _tabUsers = null!;
        private Panel _tabInstances = null!;
        private Panel _contentPanel = null!;
        private Label _lblStatus = null!;

        // Kullanıcı sekmesi
        private Panel _listPanel = null!;
        private RoundedButton _btnAdd = null!;

        // Instance sekmesi
        private Panel _instancePanel = null!;
        private Label _lblInstanceLog = null!;

        private readonly string _jwtToken;

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string? pszSubIdList);

        public AdminPanel(string jwtToken)
        {
            _jwtToken = jwtToken;
            _manifestService = new ManifestService(jwtToken);
            BuildUI();
        }

        private void BuildUI()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(600, 500);
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
                Text = "⚔  Admin Paneli",
                ForeColor = _text,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(14, 9),
                BackColor = Color.Transparent
            });
            var closeBtn = new Button
            {
                Text = "✕",
                ForeColor = _muted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F),
                Size = new Size(36, 34),
                Location = new Point(600 - 36, 0),
                TabStop = false
            };
            closeBtn.FlatAppearance.BorderSize = 0;
            closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(110, 38, 28);
            closeBtn.Click += (_, _) => Close();
            titleBar.Controls.Add(closeBtn);

            var divider = new Panel { BackColor = _border, Location = new Point(0, 34), Size = new Size(600, 1) };

            // ── Sekme butonları ───────────────────────────────────────────
            _tabUsers = MakeTabButton("👥  Kullanıcılar", 0);
            _tabInstances = MakeTabButton("📦  Instance Güncelleme", 1);
            _tabUsers.Click += (_, _) => SwitchTab(0);
            _tabInstances.Click += (_, _) => SwitchTab(1);

            var tabBar = new Panel
            {
                BackColor = Color.FromArgb(14, 11, 9),
                Location = new Point(0, 35),
                Size = new Size(600, 38)
            };
            tabBar.Controls.Add(_tabUsers);
            tabBar.Controls.Add(_tabInstances);

            // ── İçerik alanı ─────────────────────────────────────────────
            _contentPanel = new Panel
            {
                Location = new Point(0, 73),
                Size = new Size(600, 390),
                BackColor = Color.Transparent
            };

            // ── Durum etiketi ─────────────────────────────────────────────
            _lblStatus = new Label
            {
                Text = "",
                ForeColor = _accent,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                AutoSize = true,
                Location = new Point(16, 476),
                BackColor = Color.Transparent
            };

            Controls.AddRange(new Control[] { titleBar, divider, tabBar, _contentPanel, _lblStatus });
            titleBar.BringToFront();

            BuildUsersTab();
            BuildInstancesTab();
            SwitchTab(0);
        }

        private Panel MakeTabButton(string text, int index)
        {
            var p = new Panel
            {
                Size = new Size(200, 38),
                Location = new Point(index * 200, 0),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Tag = index
            };

            var lbl = new Label
            {
                Text = text,
                ForeColor = _muted,
                Font = new Font("Segoe UI Semibold", 9F),
                AutoSize = true,
                Location = new Point(16, 10),
                BackColor = Color.Transparent,
                Tag = "lbl",
                Cursor = Cursors.Hand
            };

            // Label tıklamasını panel'e ilet
            lbl.Click += (_, _) =>
            {
                int idx = (int)p.Tag!;
                SwitchTab(idx);
            };
            p.Controls.Add(lbl);
            return p;
        }
        

        private void SwitchTab(int tab)
        {
            UpdateTab(_tabUsers, tab == 0);
            UpdateTab(_tabInstances, tab == 1);

            _listPanel.Visible = tab == 0;
            _btnAdd.Visible = tab == 0;
            _instancePanel.Visible = tab == 1;
        }

        private void UpdateTab(Panel tabPanel, bool active)
        {
            tabPanel.BackColor = active
                ? Color.FromArgb(22, 17, 13)
                : Color.Transparent;

            var lbl = tabPanel.Controls.OfType<Label>().FirstOrDefault(l => l.Tag?.ToString() == "lbl");
            if (lbl != null)
                lbl.ForeColor = active ? _accent : _muted;

            // Alt çizgi
            var line = tabPanel.Controls.OfType<Panel>().FirstOrDefault();
            if (line == null && active)
            {
                tabPanel.Controls.Add(new Panel
                {
                    BackColor = _accent,
                    Location = new Point(0, 35),
                    Size = new Size(200, 2)
                });
            }
            else if (line != null)
            {
                line.Visible = active;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // KULLANICI SEKMESİ
        // ════════════════════════════════════════════════════════════════
        private void BuildUsersTab()
        {
            var header = new Panel
            {
                BackColor = Color.FromArgb(16, 13, 11),
                Location = new Point(0, 0),
                Size = new Size(600, 26)
            };
            void AddHL(string t, int x) => header.Controls.Add(new Label
            {
                Text = t,
                ForeColor = _muted,
                Font = new Font("Segoe UI", 8F),
                AutoSize = true,
                Location = new Point(x, 5),
                BackColor = Color.Transparent
            });
            AddHL("Kullanıcı Adı", 12);
            AddHL("Paketler", 180);
            AddHL("Durum", 340);
            AddHL("Admin", 410);
            AddHL("✎ / ✕", 466);

            _listPanel = new Panel
            {
                Location = new Point(0, 26),
                Size = new Size(600, 340),
                AutoScroll = true,
                BackColor = Color.Transparent
            };

            _btnAdd = new RoundedButton
            {
                Text = "+ Kullanıcı Ekle",
                Size = new Size(130, 28),
                Location = new Point(454, 40), // tabBar içinde sağ üst
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = _text,
                ButtonColor = Color.FromArgb(40, 28, 18),
                HoverColor = Color.FromArgb(80, 52, 28),
                PressedColor = Color.FromArgb(18, 13, 10),
                BorderColor = Color.FromArgb(100, 68, 38),
                BorderRadius = 6,
                Enabled = false
            };
            _btnAdd.Click += (_, _) => ShowUserDialog(null);
            Controls.Add(_btnAdd);
            _btnAdd.BringToFront();

            // _contentPanel yerine direkt ana forma ekle
            Controls.Add(_btnAdd);
            _btnAdd.BringToFront();
            _contentPanel.Controls.AddRange(new Control[] { header, _listPanel }); // _btnAdd kaldırıldı
        }

        // ════════════════════════════════════════════════════════════════
        // INSTANCE SEKMESİ
        // ════════════════════════════════════════════════════════════════
        private void BuildInstancesTab()
        {
            _instancePanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(600, 390),
                BackColor = Color.Transparent,
                Visible = false,
                AutoScroll = true  // ← ekle
            };

            // Açıklama
            _instancePanel.Controls.Add(new Label
            {
                Text = "Instance klasörünü tarayıp manifest.json oluşturur ve GitHub'a push'lar.",
                ForeColor = _muted,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                AutoSize = true,
                Location = new Point(16, 12),
                BackColor = Color.Transparent
            });

            // Instance kartları
            string[] instances = { "AGLR", "Vanilla" };
            int y = 44;

            foreach (var inst in instances)
            {
                var name = inst;
                var card = new Panel
                {
                    Location = new Point(16, y),
                    Size = new Size(568, 96), // 76 → 96
                    BackColor = Color.FromArgb(18, 14, 11)
                };

                // Sol aksanı
                card.Controls.Add(new Panel
                {
                    BackColor = _accent,
                    Location = new Point(0, 0),
                    Size = new Size(3, 76) // bu 76'yı 96 yap
                });

                card.Controls.Add(new Label
                {
                    Text = $"📦  {name}",
                    ForeColor = _text,
                    Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(14, 10),
                    BackColor = Color.Transparent
                });

                var lblFolders = new Label
                {
                    Text = $"Taranacak: {string.Join(", ", ManifestService.TrackedFolders)}",
                    ForeColor = _muted,
                    Font = new Font("Segoe UI", 7.5F),
                    AutoSize = true,
                    Location = new Point(14, 32),
                    BackColor = Color.Transparent
                };
                card.Controls.Add(lblFolders);

                var txtModsVer = new ModernTextBox
                {
                    PlaceholderText = "Mods versiyon (örn: v1.0)",
                    Location = new Point(14, 52),
                    Size = new Size(180, 22),
                    Font = new Font("Segoe UI", 8F)
                };
                card.Controls.Add(txtModsVer);

                var txtModsUrl = new ModernTextBox
                {
                    PlaceholderText = "Mods ZIP URL",
                    Location = new Point(200, 52),
                    Size = new Size(210, 22),
                    Font = new Font("Segoe UI", 8F)
                };
                card.Controls.Add(txtModsUrl);

                var btnScan = new RoundedButton
                {
                    Text = "🔍 Tara & Push'la",
                    Size = new Size(130, 30),
                    Location = new Point(424, 22),
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = _text,
                    ButtonColor = Color.FromArgb(40, 28, 18),
                    HoverColor = Color.FromArgb(80, 52, 28),
                    PressedColor = Color.FromArgb(18, 13, 10),
                    BorderColor = Color.FromArgb(100, 68, 38),
                    BorderRadius = 6
                };
                btnScan.Click += (_, _) => _ = ScanAndPushAsync(name, btnScan, txtModsVer, txtModsUrl);
                card.Controls.Add(btnScan);
                _instancePanel.Controls.Add(card); 
                y += 90;                            
            }

            // Log alanı
            _instancePanel.Controls.Add(new Label
            {
                Text = "İşlem Günlüğü",
                ForeColor = _accent,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, y + 4),
                BackColor = Color.Transparent
            });

            _lblInstanceLog = new Label
            {
                Text = "",
                ForeColor = _muted,
                Font = new Font("Consolas", 8F),
                AutoSize = false,
                Size = new Size(568, 80),
                Location = new Point(16, y + 24),
                BackColor = Color.FromArgb(10, 8, 6),
                TextAlign = ContentAlignment.TopLeft
            };
            _instancePanel.Controls.Add(_lblInstanceLog);

            _contentPanel.Controls.Add(_instancePanel);
        }

        // ── Tara ve push'la ───────────────────────────────────────────────
        private async Task ScanAndPushAsync(string instanceName, RoundedButton btn, ModernTextBox txtModsVer, ModernTextBox txtModsUrl)
        {
            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            string instancePath = Path.Combine(launcherDir, "instances", instanceName);


            if (!Directory.Exists(instancePath))
            {
                InstanceLog($"❌ Klasör bulunamadı: {instancePath}");
                return;
            }

            btn.Enabled = false;
            InstanceLog($"🔍 {instanceName} taranıyor...");

            try
            {
                // Mevcut manifest'i çek
                var currentManifest = await _manifestService!.GetManifestAsync();
                var previousFiles = new List<string>();

                if (currentManifest?.Instances != null &&
                    currentManifest.Instances.TryGetValue(instanceName, out var prev))
                {
                    previousFiles = prev.Files.Select(f => f.Path).ToList();
                }

                // Lokal dosyaları tara
                var newFiles = await Task.Run(() =>
                    _manifestService!.ScanInstance(instancePath, instanceName));

                var newFilePaths = newFiles.Select(f => f.Path).ToHashSet();

                // Önceki manifestte olup artık lokalde olmayan = silinmiş
                var deletedFiles = previousFiles
                    .Where(p => !newFilePaths.Contains(p))
                    .ToList();

                InstanceLog($"✓ {newFiles.Count} dosya tarandı." +
                            (deletedFiles.Count > 0 ? $" {deletedFiles.Count} dosya silindi." : "") +
                            " GitHub'a gönderiliyor...");

                bool ok = await _manifestService!.PushManifestAsync(
                instanceName, newFiles, deletedFiles,
                string.IsNullOrEmpty(txtModsVer.Text) ? null : txtModsVer.Text,
                string.IsNullOrEmpty(txtModsUrl.Text) ? null : txtModsUrl.Text);


                if (ok)
                    InstanceLog($"✅ manifest.json güncellendi! ({newFiles.Count} dosya" +
                                (deletedFiles.Count > 0 ? $", {deletedFiles.Count} silindi" : "") + ")");
                else
                    InstanceLog("❌ Push başarısız.");
            }
            catch (Exception ex)
            {
                InstanceLog($"❌ Hata: {ex.Message}");
            }

            btn.Enabled = true;
        }

        private void InstanceLog(string message)
        {
            if (InvokeRequired) { Invoke(() => InstanceLog(message)); return; }
            _lblInstanceLog.Text = message + "\n" + _lblInstanceLog.Text;
        }

        // ════════════════════════════════════════════════════════════════
        // ORTAK
        // ════════════════════════════════════════════════════════════════
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            System.ComponentModel.ComponentResourceManager res = new(typeof(Form1));
            Icon = (Icon)res.GetObject("$this.Icon");
            SetWindowTheme(_listPanel.Handle, "DarkMode_Explorer", null);
            _ = AskCredentialsAndConnectAsync();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(8, 7, 6), Color.FromArgb(22, 14, 9), LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        // ── Credentials ───────────────────────────────────────────────────
        private async Task AskCredentialsAndConnectAsync()
        {
            using var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(340, 240),
                BackColor = Color.FromArgb(14, 12, 10)
            };

            var tb = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(22, 16, 12) };
            tb.Controls.Add(new Label
            {
                Text = "MongoDB Bağlantısı",
                ForeColor = _text,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(14, 9),
                BackColor = Color.Transparent
            });
            tb.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(dlg.Handle, 0xA1, 0x2, 0); } };

            var txtU = new ModernTextBox { Location = new Point(20, 60), Size = new Size(300, 26) };
            txtU.PlaceholderText = "MongoDB kullanıcı adı";

            var txtP = new ModernTextBox { Location = new Point(20, 108), Size = new Size(300, 26) };
            txtP.PlaceholderText = "Şifre";
            txtP.PasswordChar = '•';

            var lblE = new Label
            {
                Text = "",
                ForeColor = _red,
                Font = new Font("Segoe UI", 8F),
                AutoSize = false,
                Size = new Size(300, 16),
                Location = new Point(20, 144),
                BackColor = Color.Transparent
            };

            var btnC = new RoundedButton
            {
                Text = "Bağlan",
                Size = new Size(300, 36),
                Location = new Point(20, 164),
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                ForeColor = _text,
                ButtonColor = Color.FromArgb(53, 35, 24),
                HoverColor = Color.FromArgb(94, 57, 30),
                PressedColor = Color.FromArgb(18, 13, 10),
                BorderColor = Color.FromArgb(122, 76, 39),
                BorderRadius = 8
            };

            btnC.Click += async (_, _) =>
            {
                if (string.IsNullOrEmpty(txtU.Text) || string.IsNullOrEmpty(txtP.Text))
                { lblE.Text = "Boş bırakılamaz."; return; }

                btnC.Enabled = false;
                lblE.ForeColor = _accent;
                lblE.Text = "Bağlanılıyor...";

                try
                {
                    var svc = new AdminService(txtU.Text.Trim(), txtP.Text);
                    await svc.TestConnectionAsync();
                    _adminService = svc;
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                }
                catch
                {
                    lblE.ForeColor = _red;
                    lblE.Text = "Bağlantı başarısız.";
                    btnC.Enabled = true;
                }
            };

            dlg.Controls.AddRange(new Control[]
            {
                tb,
                new Label { Text = "Kullanıcı Adı", ForeColor = _muted, Font = new Font("Segoe UI", 8.5F), AutoSize = true, Location = new Point(20, 42), BackColor = Color.Transparent },
                txtU,
                new Label { Text = "Şifre", ForeColor = _muted, Font = new Font("Segoe UI", 8.5F), AutoSize = true, Location = new Point(20, 90), BackColor = Color.Transparent },
                txtP, lblE, btnC
            });

            if (dlg.ShowDialog(this) != DialogResult.OK) { Close(); return; }

            _lblStatus.Text = "Yükleniyor...";
            _btnAdd.Enabled = false;
            await LoadUsersAsync();
        }

        // ── Kullanıcıları yükle ───────────────────────────────────────────
        private async Task LoadUsersAsync()
        {
            try
            {
                _users = await _adminService!.GetUsersAsync();
                RenderUsers();
                _lblStatus.Text = $"{_users.Count} kullanıcı";
            }
            catch (Exception ex)
            {
                _lblStatus.ForeColor = _red;
                _lblStatus.Text = "Hata: " + ex.Message;
            }
            _btnAdd.Enabled = true;
        }

        private void RenderUsers()
        {
            _listPanel.Controls.Clear();
            int y = 8;

            foreach (var user in _users)
            {
                var u = user;

                var card = new Panel
                {
                    Location = new Point(8, y),
                    Size = new Size(566, 80),
                    BackColor = Color.FromArgb(22, 17, 13)
                };

                card.Controls.Add(new Panel
                {
                    BackColor = user.IsActive ? _accent : Color.FromArgb(80, 60, 50),
                    Location = new Point(0, 0),
                    Size = new Size(3, 80)
                });

                card.Controls.Add(new Label
                {
                    Text = user.Username,
                    ForeColor = _text,
                    Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(14, 10),
                    BackColor = Color.Transparent
                });

                string info = $"Paketler: {string.Join(", ", user.AllowedPacks)}   |   " +
                              $"Admin: {(user.IsAdmin ? "Evet" : "Hayır")}   |   " +
                              $"Durum: {(user.IsActive ? "Aktif" : "Pasif")}";
                card.Controls.Add(new Label
                {
                    Text = info,
                    ForeColor = _muted,
                    Font = new Font("Segoe UI", 8F),
                    AutoSize = true,
                    Location = new Point(14, 34),
                    BackColor = Color.Transparent
                });

                var btnEdit = new RoundedButton
                {
                    Text = "✎  Düzenle",
                    Size = new Size(90, 26),
                    Location = new Point(360, 27),
                    Font = new Font("Segoe UI", 8F),
                    ForeColor = _text,
                    ButtonColor = Color.FromArgb(45, 33, 20),
                    HoverColor = Color.FromArgb(80, 55, 28),
                    PressedColor = Color.FromArgb(18, 13, 10),
                    BorderColor = Color.FromArgb(100, 70, 36),
                    BorderRadius = 6
                };
                btnEdit.Click += (_, _) => ShowUserDialog(u);
                card.Controls.Add(btnEdit);

                var btnDel = new RoundedButton
                {
                    Text = "✕  Sil",
                    Size = new Size(80, 26),
                    Location = new Point(460, 27),
                    Font = new Font("Segoe UI", 8F),
                    ForeColor = Color.FromArgb(210, 80, 60),
                    ButtonColor = Color.FromArgb(45, 20, 18),
                    HoverColor = Color.FromArgb(80, 30, 26),
                    PressedColor = Color.FromArgb(20, 10, 10),
                    BorderColor = Color.FromArgb(100, 45, 40),
                    BorderRadius = 6
                };
                btnDel.Click += (_, _) => _ = DeleteUserAsync(u);
                card.Controls.Add(btnDel);

                card.Controls.Add(new Panel
                {
                    BackColor = Color.FromArgb(35, 28, 22),
                    Location = new Point(0, 79),
                    Size = new Size(566, 1)
                });

                _listPanel.Controls.Add(card);
                y += 90;
            }
        }

        private async Task DeleteUserAsync(AdminUser user)
        {
            using var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(320, 150),
                BackColor = Color.FromArgb(14, 12, 10)
            };

            var tb = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(22, 16, 12) };
            tb.Controls.Add(new Label
            {
                Text = "Kullanıcı Sil",
                ForeColor = _text,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(14, 9),
                BackColor = Color.Transparent
            });
            tb.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(dlg.Handle, 0xA1, 0x2, 0); } };

            dlg.Controls.Add(new Label
            {
                Text = $"'{user.Username}' silinsin mi?",
                ForeColor = _text,
                Font = new Font("Segoe UI", 9.5F),
                AutoSize = true,
                Location = new Point(20, 52),
                BackColor = Color.Transparent
            });

            var btnHayir = new RoundedButton
            {
                Text = "Hayır",
                Size = new Size(120, 32),
                Location = new Point(20, 100),
                ForeColor = _text,
                ButtonColor = Color.FromArgb(35, 28, 22),
                HoverColor = Color.FromArgb(60, 45, 30),
                PressedColor = Color.FromArgb(18, 13, 10),
                BorderColor = Color.FromArgb(80, 60, 36),
                BorderRadius = 6
            };
            btnHayir.Click += (_, _) => { dlg.DialogResult = DialogResult.No; dlg.Close(); };

            var btnEvet = new RoundedButton
            {
                Text = "Evet",
                Size = new Size(120, 32),
                Location = new Point(180, 100),
                ForeColor = Color.FromArgb(210, 80, 60),
                ButtonColor = Color.FromArgb(45, 20, 18),
                HoverColor = Color.FromArgb(80, 30, 26),
                PressedColor = Color.FromArgb(20, 10, 10),
                BorderColor = Color.FromArgb(100, 45, 40),
                BorderRadius = 6
            };
            btnEvet.Click += (_, _) => { dlg.DialogResult = DialogResult.Yes; dlg.Close(); };

            dlg.Controls.AddRange(new Control[] { tb, btnHayir, btnEvet });

            if (dlg.ShowDialog(this) != DialogResult.Yes) return;
            await _adminService!.DeleteUserAsync(user.Id);
            await LoadUsersAsync();
        }

        private void ShowUserDialog(AdminUser? existing)
        {
            bool isEdit = existing != null;

            using var dlg = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(360, 320),
                BackColor = Color.FromArgb(14, 12, 10)
            };

            var tb = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(22, 16, 12) };
            tb.Controls.Add(new Label
            {
                Text = isEdit ? $"Düzenle — {existing!.Username}" : "Yeni Kullanıcı",
                ForeColor = _text,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(14, 9),
                BackColor = Color.Transparent
            });
            tb.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(dlg.Handle, 0xA1, 0x2, 0); } };

            Label DL(string t, int top) => new Label
            {
                Text = t,
                ForeColor = _muted,
                Font = new Font("Segoe UI", 8.5F),
                AutoSize = true,
                Location = new Point(20, top),
                BackColor = Color.Transparent
            };
            ModernTextBox DT(string val, int top) => new ModernTextBox
            {
                Text = val,
                Location = new Point(20, top),
                Size = new Size(320, 26)
            };

            var txtUser = DT(isEdit ? existing!.Username : "", 58);
            var txtPass = DT("", 106);
            txtPass.PasswordChar = '•';
            if (isEdit) txtPass.PlaceholderText = "Boş bırakırsan değişmez";

            var txtPacks = DT(isEdit ? string.Join(",", existing!.AllowedPacks) : "Vanilla", 154);
            txtPacks.PlaceholderText = "Vanilla,AGLR,R";

            var chkAdmin = new CheckBox
            {
                Text = "Admin",
                ForeColor = _text,
                BackColor = Color.Transparent,
                Location = new Point(20, 192),
                AutoSize = true,
                Checked = isEdit && existing!.IsAdmin
            };
            var chkActive = new CheckBox
            {
                Text = "Aktif",
                ForeColor = _text,
                BackColor = Color.Transparent,
                Location = new Point(100, 192),
                AutoSize = true,
                Checked = !isEdit || existing!.IsActive
            };

            var lblErr = new Label
            {
                Text = "",
                ForeColor = _red,
                Font = new Font("Segoe UI", 8F),
                AutoSize = false,
                Size = new Size(320, 16),
                Location = new Point(20, 224),
                BackColor = Color.Transparent
            };

            var btnSave = new RoundedButton
            {
                Text = isEdit ? "Kaydet" : "Ekle",
                Size = new Size(320, 36),
                Location = new Point(20, 244),
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                ForeColor = _text,
                ButtonColor = Color.FromArgb(53, 35, 24),
                HoverColor = Color.FromArgb(94, 57, 30),
                PressedColor = Color.FromArgb(18, 13, 10),
                BorderColor = Color.FromArgb(122, 76, 39),
                BorderRadius = 8
            };

            btnSave.Click += async (_, _) =>
            {
                var packs = txtPacks.Text.Split(',')
                    .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
                try
                {
                    if (isEdit)
                    {
                        var upd = Builders<AdminUser>.Update
                            .Set(u => u.Username, txtUser.Text.Trim())
                            .Set(u => u.AllowedPacks, packs)
                            .Set(u => u.IsActive, chkActive.Checked)
                            .Set(u => u.IsAdmin, chkAdmin.Checked);
                        if (!string.IsNullOrEmpty(txtPass.Text))
                            upd = upd.Set(u => u.PasswordHash, BCrypt.Net.BCrypt.HashPassword(txtPass.Text));
                        await _adminService!.UpdateUserAsync(existing!.Id, upd);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(txtUser.Text) || string.IsNullOrEmpty(txtPass.Text))
                        { lblErr.Text = "Kullanıcı adı ve şifre zorunlu."; return; }
                        await _adminService!.AddUserAsync(new AdminUser
                        {
                            Username = txtUser.Text.Trim(),
                            PasswordHash = BCrypt.Net.BCrypt.HashPassword(txtPass.Text),
                            AllowedPacks = packs,
                            IsAdmin = chkAdmin.Checked,
                            IsActive = chkActive.Checked
                        });
                    }
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                    await LoadUsersAsync();
                }
                catch (Exception ex) { lblErr.Text = "Hata: " + ex.Message; }
            };

            dlg.Controls.AddRange(new Control[]
            {
                tb,
                DL("Kullanıcı Adı", 40), txtUser,
                DL("Şifre", 88), txtPass,
                DL("Paketler", 136), txtPacks,
                chkAdmin, chkActive, lblErr, btnSave
            });

            dlg.ShowDialog(this);
        }

        private sealed class GradientPanel : Panel
        {
            private readonly Color _f, _t;
            public GradientPanel(Color f, Color t) { _f = f; _t = t; DoubleBuffered = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                using var b = new LinearGradientBrush(ClientRectangle, _f, _t, 0f);
                e.Graphics.FillRectangle(b, ClientRectangle);
            }
        }
    }
}