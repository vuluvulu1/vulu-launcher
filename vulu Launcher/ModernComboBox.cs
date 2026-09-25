using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace AGLR_Launcher
{
    public class ModernComboBox : ComboBox
    {
        private readonly Color _background = Color.FromArgb(22, 19, 18);
        private readonly Color _dropdownBackground = Color.FromArgb(27, 24, 22);
        private readonly Color _border = Color.FromArgb(58, 48, 42);
        private readonly Color _borderFocus = Color.FromArgb(153, 99, 47);
        private readonly Color _itemHover = Color.FromArgb(67, 49, 34);
        private readonly Color _text = Color.FromArgb(235, 226, 214);
        private bool _hovered;

        public ModernComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = _background;
            ForeColor = _text;
            ItemHeight = 26;
            IntegralHeight = false;
            DropDownHeight = 136;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int BorderRadius { get; set; } = 4;

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

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var backgroundBrush = new SolidBrush(selected ? _itemHover : _dropdownBackground);
            using var textBrush = new SolidBrush(_text);

            e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var textRect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                GetItemText(Items[e.Index]),
                Font,
                textRect,
                _text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            const int wmPaint = 0x000F;
            const int wmNCPaint = 0x0085;

            if (m.Msg == wmPaint || m.Msg == wmNCPaint)
                DrawComboChrome();
        }

        private void DrawComboChrome()
        {
            using var graphics = Graphics.FromHwnd(Handle);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = CreateRoundPath(bounds, BorderRadius);
            using var backgroundBrush = new SolidBrush(_hovered ? Color.FromArgb(31, 26, 23) : _background);
            using var borderPen = new Pen(Focused || DroppedDown ? _borderFocus : _border, 1.2f);

            graphics.FillPath(backgroundBrush, path);
            graphics.DrawPath(borderPen, path);

            var textRect = new Rectangle(11, 0, Width - 34, Height);
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                textRect,
                _text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            DrawArrow(graphics);
        }

        private void DrawArrow(Graphics graphics)
        {
            var centerX = Width - 19;
            var centerY = Height / 2 + 1;

            using var arrowBrush = new SolidBrush(Color.FromArgb(193, 139, 79));
            Point[] arrow =
            {
                new(centerX - 4, centerY - 2),
                new(centerX + 4, centerY - 2),
                new(centerX, centerY + 3)
            };

            graphics.FillPolygon(arrowBrush, arrow);
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
