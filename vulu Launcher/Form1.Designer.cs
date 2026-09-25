namespace AGLR_Launcher
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            txtUsername = new ModernTextBox();
            pictureGL = new PictureBox();
            rtbConsole = new ModernRichTextBox();
            trackBarVolume = new ModernTrackBar();
            label1 = new Label();
            label2 = new Label();
            lblScrollingText = new Label();
            timerScroll = new System.Windows.Forms.Timer(components);
            pnlScroller = new Panel();
            comboBoxRam = new ModernComboBox();
            label3 = new Label();
            label4 = new Label();
            btnPlay = new RoundedButton();
            btnVanilla = new ModernImageButton();
            panelSurumler = new Panel();
            btnRNG = new ModernImageButton();
            btnAGLR = new ModernImageButton();
            ((System.ComponentModel.ISupportInitialize)pictureGL).BeginInit();
            ((System.ComponentModel.ISupportInitialize)trackBarVolume).BeginInit();
            pnlScroller.SuspendLayout();
            panelSurumler.SuspendLayout();
            SuspendLayout();
            // 
            // txtUsername
            // 
            txtUsername.BackColor = Color.White;
            txtUsername.BorderStyle = BorderStyle.FixedSingle;
            txtUsername.Font = new Font("Segoe UI", 9F);
            txtUsername.ForeColor = Color.Black;
            txtUsername.Location = new Point(232, 148);
            txtUsername.MaxLength = 16;
            txtUsername.Name = "txtUsername";
            txtUsername.PlaceholderText = "Kullanıcı Adı";
            txtUsername.Size = new Size(236, 23);
            txtUsername.TabIndex = 2;
            // 
            // pictureGL
            // 
            pictureGL.BackColor = Color.Transparent;
            pictureGL.BackgroundImageLayout = ImageLayout.None;
            pictureGL.Image = Properties.Resources.aglr;
            pictureGL.Location = new Point(214, 18);
            pictureGL.Name = "pictureGL";
            pictureGL.Size = new Size(300, 120);
            pictureGL.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureGL.TabIndex = 3;
            pictureGL.TabStop = false;
            // 
            // rtbConsole
            // 
            rtbConsole.BackColor = Color.Black;
            rtbConsole.BorderStyle = BorderStyle.None;
            rtbConsole.Font = new Font("Consolas", 8.25F);
            rtbConsole.ForeColor = Color.Silver;
            rtbConsole.Location = new Point(202, 248);
            rtbConsole.Name = "rtbConsole";
            rtbConsole.ReadOnly = true;
            rtbConsole.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtbConsole.Size = new Size(334, 58);
            rtbConsole.TabIndex = 4;
            rtbConsole.Text = "";
            // 
            // trackBarVolume
            // 
            trackBarVolume.Location = new Point(438, 340);
            trackBarVolume.Maximum = 100;
            trackBarVolume.Name = "trackBarVolume";
            trackBarVolume.Size = new Size(124, 26);
            trackBarVolume.TabIndex = 5;
            trackBarVolume.Value = 30;
            trackBarVolume.Scroll += trackBarVolume_Scroll;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.BackColor = Color.Transparent;
            label1.Font = new Font("Segoe UI", 8F);
            label1.ForeColor = Color.FromArgb(140, 118, 96);
            label1.Location = new Point(469, 328);
            label1.Name = "label1";
            label1.Size = new Size(61, 13);
            label1.TabIndex = 6;
            label1.Text = "Ses Düzeyi";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.BackColor = Color.Transparent;
            label2.Font = new Font("Segoe UI Semibold", 8.75F, FontStyle.Bold);
            label2.ForeColor = Color.FromArgb(196, 130, 65);
            label2.Location = new Point(238, 320);
            label2.Name = "label2";
            label2.Size = new Size(109, 15);
            label2.TabIndex = 7;
            label2.Text = "♪  Şu anda çalıyor...";
            // 
            // lblScrollingText
            // 
            lblScrollingText.AutoSize = true;
            lblScrollingText.BackColor = Color.Transparent;
            lblScrollingText.Font = new Font("Segoe UI", 8.5F, FontStyle.Italic);
            lblScrollingText.ForeColor = Color.FromArgb(205, 182, 152);
            lblScrollingText.Location = new Point(0, 6);
            lblScrollingText.Name = "lblScrollingText";
            lblScrollingText.Size = new Size(261, 15);
            lblScrollingText.TabIndex = 8;
            lblScrollingText.Text = "Distant Horizons / Jeremy Soule, The Elder Scrolls";
            // 
            // timerScroll
            // 
            timerScroll.Enabled = true;
            timerScroll.Interval = 60;
            timerScroll.Tick += timerScroll_Tick;
            // 
            // pnlScroller
            // 
            pnlScroller.BackColor = Color.FromArgb(18, 15, 13);
            pnlScroller.BackgroundImageLayout = ImageLayout.None;
            pnlScroller.Controls.Add(lblScrollingText);
            pnlScroller.Location = new Point(236, 340);
            pnlScroller.Name = "pnlScroller";
            pnlScroller.Size = new Size(188, 26);
            pnlScroller.TabIndex = 9;
            // 
            // comboBoxRam
            // 
            comboBoxRam.BackColor = Color.White;
            comboBoxRam.DrawMode = DrawMode.OwnerDrawFixed;
            comboBoxRam.DropDownHeight = 136;
            comboBoxRam.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxRam.FlatStyle = FlatStyle.Flat;
            comboBoxRam.Font = new Font("Segoe UI", 9F);
            comboBoxRam.ForeColor = Color.Black;
            comboBoxRam.FormattingEnabled = true;
            comboBoxRam.IntegralHeight = false;
            comboBoxRam.ItemHeight = 26;
            comboBoxRam.Items.AddRange(new object[] { "4GB", "8GB", "12GB", "16GB" });
            comboBoxRam.Location = new Point(18, 325);
            comboBoxRam.Name = "comboBoxRam";
            comboBoxRam.Size = new Size(136, 32);
            comboBoxRam.TabIndex = 10;
            comboBoxRam.SelectedIndexChanged += comboBoxRam_SelectedIndexChanged;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Font = new Font("Segoe UI", 10F);
            label3.ForeColor = Color.White;
            label3.Location = new Point(18, 16);
            label3.Name = "label3";
            label3.Size = new Size(64, 19);
            label3.TabIndex = 11;
            label3.Text = "Sürümler";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Font = new Font("Segoe UI", 9F);
            label4.ForeColor = Color.FromArgb(168, 151, 133);
            label4.Location = new Point(20, 304);
            label4.Name = "label4";
            label4.Size = new Size(71, 15);
            label4.TabIndex = 11;
            label4.Text = "Ram Miktarı";
            // 
            // btnPlay
            // 
            btnPlay.BackColor = Color.White;
            btnPlay.BackgroundImageLayout = ImageLayout.Stretch;
            btnPlay.FlatAppearance.BorderSize = 0;
            btnPlay.FlatStyle = FlatStyle.Flat;
            btnPlay.ForeColor = Color.Black;
            btnPlay.Location = new Point(258, 188);
            btnPlay.Name = "btnPlay";
            btnPlay.Size = new Size(184, 46);
            btnPlay.TabIndex = 12;
            btnPlay.Text = "Oyna";
            btnPlay.UseVisualStyleBackColor = false;
            btnPlay.Click += btnPlay_Click;
            // 
            // btnVanilla
            // 
            btnVanilla.BackColor = Color.FromArgb(18, 18, 20);
            btnVanilla.BackgroundImage = Properties.Resources.launchervanillabg;
            btnVanilla.BackgroundImageLayout = ImageLayout.Stretch;
            btnVanilla.FlatAppearance.BorderColor = Color.DimGray;
            btnVanilla.FlatStyle = FlatStyle.Flat;
            btnVanilla.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnVanilla.ForeColor = Color.White;
            btnVanilla.Location = new Point(14, 47);
            btnVanilla.Name = "btnVanilla";
            btnVanilla.Size = new Size(140, 66);
            btnVanilla.TabIndex = 13;
            btnVanilla.Tag = "Vanilla";
            btnVanilla.Text = "Vanilla";
            btnVanilla.UseVisualStyleBackColor = true;
            btnVanilla.Click += btnVanilla_Click;
            // 
            // panelSurumler
            // 
            panelSurumler.BackColor = Color.FromArgb(13, 11, 10);
            panelSurumler.Controls.Add(btnRNG);
            panelSurumler.Controls.Add(btnAGLR);
            panelSurumler.Controls.Add(btnVanilla);
            panelSurumler.Controls.Add(label3);
            panelSurumler.Location = new Point(0, -1);
            panelSurumler.Name = "panelSurumler";
            panelSurumler.Size = new Size(168, 286);
            panelSurumler.TabIndex = 14;
            // 
            // btnRNG
            // 
            btnRNG.BackColor = Color.FromArgb(22, 19, 17);
            btnRNG.BackgroundImageLayout = ImageLayout.Stretch;
            btnRNG.FlatAppearance.BorderColor = Color.DimGray;
            btnRNG.FlatStyle = FlatStyle.Flat;
            btnRNG.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnRNG.ForeColor = Color.White;
            btnRNG.Location = new Point(14, 205);
            btnRNG.Name = "btnRNG";
            btnRNG.Size = new Size(140, 66);
            btnRNG.TabIndex = 13;
            btnRNG.Tag = "R";
            btnRNG.Text = "R";
            btnRNG.UseVisualStyleBackColor = false;
            btnRNG.Click += btnRNG_Click;
            // 
            // btnAGLR
            // 
            btnAGLR.BackColor = Color.FromArgb(18, 18, 20);
            btnAGLR.BackgroundImage = Properties.Resources.mainmenu;
            btnAGLR.BackgroundImageLayout = ImageLayout.Stretch;
            btnAGLR.FlatAppearance.BorderColor = Color.DimGray;
            btnAGLR.FlatStyle = FlatStyle.Flat;
            btnAGLR.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnAGLR.ForeColor = Color.White;
            btnAGLR.Location = new Point(14, 126);
            btnAGLR.Name = "btnAGLR";
            btnAGLR.Size = new Size(140, 66);
            btnAGLR.TabIndex = 13;
            btnAGLR.Tag = "AGLR";
            btnAGLR.Text = "AGLR";
            btnAGLR.UseVisualStyleBackColor = true;
            btnAGLR.Click += btnAGLR_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(18, 18, 18);
            BackgroundImageLayout = ImageLayout.Stretch;
            ClientSize = new Size(584, 332);
            Controls.Add(panelSurumler);
            Controls.Add(btnPlay);
            Controls.Add(label4);
            Controls.Add(comboBoxRam);
            Controls.Add(pnlScroller);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(trackBarVolume);
            Controls.Add(rtbConsole);
            Controls.Add(pictureGL);
            Controls.Add(txtUsername);
            DoubleBuffered = true;
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximumSize = new Size(600, 371);
            MinimumSize = new Size(600, 371);
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "vulu Launcher";
            Load += Form1_Load;
            ((System.ComponentModel.ISupportInitialize)pictureGL).EndInit();
            ((System.ComponentModel.ISupportInitialize)trackBarVolume).EndInit();
            pnlScroller.ResumeLayout(false);
            pnlScroller.PerformLayout();
            panelSurumler.ResumeLayout(false);
            panelSurumler.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private ModernTextBox txtUsername;
        private PictureBox pictureGL;
        private ModernRichTextBox rtbConsole;
        private ModernTrackBar trackBarVolume;
        private Label label1;
        private Label label2;
        private Label lblScrollingText;
        private System.Windows.Forms.Timer timerScroll;
        private Panel pnlScroller;
        private ModernComboBox comboBoxRam;
        private Label label3;
        private Label label4;
        private RoundedButton btnPlay;
        private ModernImageButton btnVanilla;
        private Panel panelSurumler;
        private ModernImageButton btnRNG;
        private ModernImageButton btnAGLR;
    }
}
