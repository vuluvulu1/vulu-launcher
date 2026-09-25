using System.Drawing.Drawing2D;

namespace AGLR_Launcher
{
    public class ModernTextBox : TextBox
    {
        private bool _hovered;

        public ModernTextBox()
        {
            BorderStyle = BorderStyle.FixedSingle;
            BackColor = Color.FromArgb(20, 18, 17);
            ForeColor = Color.FromArgb(235, 226, 214);
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            const int wmPaint = 0x000F;
            const int wmNCPaint = 0x0085;

            if (m.Msg == wmPaint || m.Msg == wmNCPaint)
                DrawBorder();
        }

        private void DrawBorder()
        {
            using var graphics = Graphics.FromHwnd(Handle);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            var borderColor = Focused
                ? Color.FromArgb(155, 97, 45)
                : _hovered
                    ? Color.FromArgb(84, 58, 38)
                    : Color.FromArgb(44, 35, 30);

            using var pen = new Pen(borderColor, 1f);
            graphics.DrawRectangle(pen, rect);
        }
    }
}
