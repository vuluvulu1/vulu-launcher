using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AGLR_Launcher
{
    public partial class SplashForm : Form
    {
        private System.Windows.Forms.Timer fadeOutTimer;

        public SplashForm()
        {
            InitializeComponent();
            this.Opacity = 1; // Başlangıçta tam görünür olduğundan emin olalım
        }

        // Ana form işlemleri bitirdiğinde direkt kapatmak yerine bu metodu çağıracak
        public void FadeOutAndClose()
        {
            fadeOutTimer = new System.Windows.Forms.Timer();

            // 5 saniyede kapanması için bir matematik:
            // Her 50 milisaniyede bir (Interval) saydamlığı %1 (0.01) azaltırsak
            // 100 adımda form tamamen şeffaf olur. (50ms x 100 adım = 5000ms = 5 saniye)
            fadeOutTimer.Interval = 20;
            fadeOutTimer.Tick += FadeOutTimer_Tick;
            fadeOutTimer.Start();
        }

        private void FadeOutTimer_Tick(object sender, EventArgs e)
        {
            if (this.Opacity > 0)
            {
                // Saydamlığı azalt (yavaşça yok olma efekti)
                this.Opacity -= 0.01;
            }
            else
            {
                // Form tamamen görünmez (Opacity = 0) olduğunda Timer'ı durdur ve formu kapat
                fadeOutTimer.Stop();
                this.Close();
            }
        }
    }
}
