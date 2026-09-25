using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace AGLR_Launcher
{
    public class RoundedButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
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
        public int BorderRadius { get; set; } = 8;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color ButtonColor { get; set; } = Color.FromArgb(38, 29, 24);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color HoverColor { get; set; } = Color.FromArgb(71, 49, 31);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color PressedColor { get; set; } = Color.FromArgb(18, 14, 12);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Color.FromArgb(101, 68, 39);

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

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = CreateRoundPath(bounds, BorderRadius))
            {
                Region = new Region(path);

                var fillColor = _pressed ? PressedColor : _hovered ? HoverColor : ButtonColor;
                using var fillBrush = new SolidBrush(fillColor);
                using var borderPen = new Pen(BorderColor, _hovered ? 1.6f : 1.1f);

                pevent.Graphics.FillPath(fillBrush, path);
                pevent.Graphics.DrawPath(borderPen, path);
            }

            var textColor = Enabled ? ForeColor : Color.FromArgb(115, 115, 118);
            TextRenderer.DrawText(
                pevent.Graphics,
                Text,
                Font,
                ClientRectangle,
                textColor,
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
