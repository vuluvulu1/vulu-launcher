using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace AGLR_Launcher
{
    public class ModernImageButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        public ModernImageButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.FromArgb(18, 18, 20);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int BorderRadius { get; set; } = 6;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BaseColor { get; set; } = Color.FromArgb(25, 22, 20);

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(1, 1, Width - 3, Height - 3);

            using var path = CreateRoundPath(bounds, BorderRadius);
            Region = new Region(path);

            using (var fill = new SolidBrush(BaseColor))
                pevent.Graphics.FillPath(fill, path);

            if (BackgroundImage != null)
            {
                using var imageBrush = new TextureBrush(BackgroundImage, WrapMode.Clamp);
                imageBrush.TranslateTransform(bounds.X, bounds.Y);
                imageBrush.ScaleTransform(
                    (float)bounds.Width / BackgroundImage.Width,
                    (float)bounds.Height / BackgroundImage.Height);
                pevent.Graphics.FillPath(imageBrush, path);
            }

            int overlayAlpha = _pressed ? 172 : _hovered ? 88 : 124;
            using (var overlay = new SolidBrush(Color.FromArgb(overlayAlpha, 8, 6, 5)))
                pevent.Graphics.FillPath(overlay, path);

            using (var topGlow = new LinearGradientBrush(
                bounds,
                Color.FromArgb(_hovered ? 86 : 38, 194, 126, 62),
                Color.FromArgb(0, 194, 126, 62),
                LinearGradientMode.Vertical))
            {
                pevent.Graphics.FillPath(topGlow, path);
            }

            var borderColor = Selected
                ? Color.FromArgb(150, 94, 43)
                : _hovered
                    ? Color.FromArgb(92, 62, 38)
                    : Color.FromArgb(43, 35, 30);

            using (var borderPen = new Pen(borderColor, Selected ? 1.6f : 1f))
                pevent.Graphics.DrawPath(borderPen, path);

            if (Selected)
            {
                using var accent = new SolidBrush(Color.FromArgb(210, 179, 112, 54));
                pevent.Graphics.FillRectangle(accent, bounds.X + 10, bounds.Bottom - 6, bounds.Width - 20, 2);
            }

            TextRenderer.DrawText(
                pevent.Graphics,
                Text,
                Font,
                bounds,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static GraphicsPath CreateRoundPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}
