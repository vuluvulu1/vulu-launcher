using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace AGLR_Launcher
{
    public class ModernTrackBar : Control, ISupportInitialize
    {
        private bool _dragging;
        private bool _hovered;
        private int _value = 30;
        private int _maximum = 100;

        public ModernTrackBar()
        {
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            Size = new Size(124, 26);
            // Transparent BackColor yerine OnPaintBackground override edildi
            // SetStyle burada işe yaramıyor çünkü handle henüz oluşmadı
        }

        // Arka planı parent ile aynı renge boyayarak "transparent" efekti veriyoruz
        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            if (Parent != null)
            {
                // Parent'ın gradient arka planını bu kontrolün konumuna göre çiz
                var offset = new System.Drawing.Drawing2D.Matrix();
                offset.Translate(-Left, -Top);
                pevent.Graphics.Transform = offset;
                using (var e2 = new PaintEventArgs(pevent.Graphics, new Rectangle(Left, Top, Parent.Width, Parent.Height)))
                    InvokePaintBackground(Parent, e2);
                pevent.Graphics.ResetTransform();
            }
            else
            {
                base.OnPaintBackground(pevent);
            }
        }

        public event EventHandler? Scroll;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public int Maximum
        {
            get => _maximum;
            set
            {
                _maximum = Math.Max(1, value);
                Value = Math.Min(Value, _maximum);
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public int Value
        {
            get => _value;
            set
            {
                _value = Math.Max(0, Math.Min(Maximum, value));
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public TickStyle TickStyle { get; set; } = TickStyle.None;

        public void BeginInit()
        {
        }

        public void EndInit()
        {
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
            _dragging = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _dragging = true;
            SetValueFromMouse(e.X);
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
                SetValueFromMouse(e.X);

            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var trackRect = new Rectangle(8, Height / 2 - 3, Width - 16, 6);
            var fillWidth = (int)(trackRect.Width * (Value / (float)Maximum));
            var fillRect = new Rectangle(trackRect.X, trackRect.Y, fillWidth, trackRect.Height);

            using (var trackPath = CreateRoundPath(trackRect, 3))
            using (var trackBrush = new SolidBrush(Color.FromArgb(42, 32, 25)))
            {
                e.Graphics.FillPath(trackBrush, trackPath);
            }

            if (fillRect.Width > 0)
            {
                using var fillPath = CreateRoundPath(fillRect, 3);
                using var fillBrush = new LinearGradientBrush(
                    fillRect,
                    Color.FromArgb(112, 68, 34),
                    Color.FromArgb(194, 126, 62),
                    LinearGradientMode.Horizontal);
                e.Graphics.FillPath(fillBrush, fillPath);
            }

            int knobX = trackRect.X + fillWidth;
            var knobRect = new Rectangle(knobX - 6, Height / 2 - 6, 12, 12);
            var knobColor = _dragging || _hovered
                ? Color.FromArgb(218, 156, 82)
                : Color.FromArgb(169, 105, 51);

            using var knobBrush = new SolidBrush(knobColor);
            using var knobPen = new Pen(Color.FromArgb(40, 26, 18), 1f);
            e.Graphics.FillEllipse(knobBrush, knobRect);
            e.Graphics.DrawEllipse(knobPen, knobRect);
        }

        private void SetValueFromMouse(int x)
        {
            var trackStart = 8;
            var trackWidth = Math.Max(1, Width - 16);
            var ratio = Math.Max(0, Math.Min(1, (x - trackStart) / (float)trackWidth));
            Value = (int)Math.Round(Maximum * ratio);
            Scroll?.Invoke(this, EventArgs.Empty);
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
