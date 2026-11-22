using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    /// <summary>Tanıtım turunun gösterebileceği düğmeler: v:Tour.Target="search" gibi. Sekmeler "tab:anahtar" ile kendiliğinden bulunur.</summary>
    public static class Tour
    {
        public static readonly AttachedProperty<string?> TargetProperty =
            AvaloniaProperty.RegisterAttached<Control, string?>("Target", typeof(Tour));
        public static string? GetTarget(Control c) => c.GetValue(TargetProperty);
        public static void SetTarget(Control c, string? value) => c.SetValue(TargetProperty, value);
    }

    /// <summary>Karartma: gösterilen alanın dışını örter; alanın çevresinde nabız gibi atan amber halka.</summary>
    public sealed class TourSpot : Control
    {
        private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(0xC4, 0x05, 0x04, 0x03));
        public Rect? Hole { get; set; }
        public double Pulse { get; set; }

        public override void Render(DrawingContext ctx)
        {
            var b = new Rect(Bounds.Size);
            if (Hole is not { } h) { ctx.FillRectangle(Dim, b); return; }
            double r = Math.Min(Math.Min(h.Width, h.Height) / 2, 16);
            // Alanın dışı: çok kalın çizgiyle çizilen yuvarlak dikdörtgen (iç kenarı alanın köşeleriyle aynı yuvarlaklıkta)
            double t = (b.Width + b.Height) * 2;
            ctx.DrawRectangle(null, new Pen(Dim, t), h.Inflate(t / 2), r + t / 2, r + t / 2);
            // Halka ve dışa doğru sönen ışık
            for (int i = 3; i >= 1; i--)
            {
                double grow = i * 3 + Pulse * 4;
                byte a = (byte)(0x38 / i * (1 - Pulse * 0.5));
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(a, 0xF2, 0xA3, 0x3A)), 3), h.Inflate(grow), r + grow, r + grow);
            }
            ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0xF2, 0xF2, 0xA3, 0x3A)), 2), h, r, r);
        }
    }

    public partial class TourOverlay : UserControl
    {
        private readonly DispatcherTimer _timer;
        private TourGuide? _guide;
        private Control? _target;
        private string? _targetKey;
        private Rect? _hole;
        private Point _card;
        private bool _cardPlaced;
        private double _phase;

        public TourOverlay()
        {
            InitializeComponent();
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
            DataContextChanged += (_, _) => Attach(DataContext as TourGuide);
        }

        private void Attach(TourGuide? guide)
        {
            if (_guide != null) _guide.PropertyChanged -= OnGuideChanged;
            _guide = guide;
            _target = null;
            _targetKey = null;
            if (guide == null)
            {
                Classes.Set("shown", false);
                _timer.Stop();
                DispatcherTimer.RunOnce(() => { if (_guide == null) IsVisible = false; }, TimeSpan.FromMilliseconds(260));
                return;
            }
            guide.PropertyChanged += OnGuideChanged;
            _hole = null;
            _cardPlaced = false;
            IsVisible = true;
            Classes.Set("shown", true);
            _timer.Start();
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
        }

        private void OnGuideChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TourGuide.Target)) { _target = null; _targetKey = null; }
        }

        /// <summary>Hedef düğme: görünür olan ilk eşleşme ("a|b" sırayla denenir). Kaydırılan listedeyse görünür yere kaydırılır.</summary>
        private Control? Find(string keys)
        {
            if (this.GetVisualParent() is not Visual root) return null;
            var all = root.GetVisualDescendants().OfType<Control>().Where(c => c != this && !this.IsVisualAncestorOf(c)).ToList();
            foreach (var key in keys.Split('|'))
            {
                var found = all.FirstOrDefault(c => Matches(c, key) && c.IsEffectivelyVisible && c.Bounds.Width > 1 && c.Bounds.Height > 1 && OnScreen(c));
                if (found != null) return found;
            }
            return null;
        }

        private static bool Matches(Control c, string key) =>
            key.StartsWith("tab:")
                ? c is Button btn && (btn.Classes.Contains("tab") || btn.Classes.Contains("rail-tab")) && btn.DataContext is PageTab tab && tab.Key == key[4..]
                : Tour.GetTarget(c) == key;

        private bool OnScreen(Control c)
        {
            // Saydam (ör. açılırken solmuş) üst öğeler ve yerleşimin dışında kalanlar sayılmaz
            for (Visual? v = c; v != null && v != this.GetVisualParent(); v = v.GetVisualParent())
                if (v.Opacity < 0.05) return false;
            if (c.TranslatePoint(new Point(0, 0), this) is not { } p) return false;
            return p.X + c.Bounds.Width > 0 && p.Y + c.Bounds.Height > 0 && p.X < Bounds.Width && p.Y < Bounds.Height;
        }

        private void Tick()
        {
            if (_guide == null) return;
            _phase += 0.016 / 1.6;
            var spot = this.FindControl<TourSpot>("Spot")!;
            var card = this.FindControl<Border>("Card")!;

            // Hedef: sayfa değişince düğme sonradan yerleşebilir; bulunana kadar her karede yeniden aranır
            if (_guide.Target is { } key)
            {
                if (_target == null || _targetKey != key || !_target.IsEffectivelyVisible || !_target.IsAttachedToVisualTree())
                {
                    _target = Find(key);
                    _targetKey = key;
                    if (_target != null && _target.TranslatePoint(new Point(0, 0), this) is { } p0 && p0.Y + _target.Bounds.Height > Bounds.Height)
                        _target.BringIntoView();
                }
            }
            else _target = null;

            Rect? want = null;
            if (_target != null && _target.TranslatePoint(new Point(0, 0), this) is { } p)
                want = new Rect(p, _target.Bounds.Size).Inflate(6);

            // Alan yeni hedefe yumuşakça kayar (ilk adımda doğrudan yerinde belirir)
            if (want is { } w)
                _hole = _hole is { } h ? Lerp(h, w, 0.22) : w;
            else _hole = null;
            spot.Hole = _hole;
            spot.Pulse = (Math.Sin(_phase * Math.PI * 2) + 1) / 2;
            spot.InvalidateVisual();

            // Kart: hedef soldaki raydaysa sağında, üstteyse altında, değilse üstünde; hedef yoksa ortada
            var size = card.Bounds.Size;
            if (size.Width < 1) return;
            const double gap = 18, edge = 16;
            Point target;
            if (want is not { } t) target = new Point((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2);
            else if (t.Right < 140) target = new Point(t.Right + gap, t.Center.Y - size.Height / 2);
            else if (t.Bottom + gap + size.Height < Bounds.Height - edge && t.Center.Y < Bounds.Height / 2) target = new Point(t.Center.X - size.Width / 2, t.Bottom + gap);
            else if (t.Top - gap - size.Height > edge) target = new Point(t.Center.X - size.Width / 2, t.Top - gap - size.Height);
            else if (t.Left - gap - size.Width > edge) target = new Point(t.Left - gap - size.Width, t.Center.Y - size.Height / 2);
            else target = new Point(t.Right + gap, t.Center.Y - size.Height / 2);
            target = new Point(Math.Clamp(target.X, edge, Math.Max(edge, Bounds.Width - size.Width - edge)),
                               Math.Clamp(target.Y, 56, Math.Max(56, Bounds.Height - size.Height - edge)));
            _card = _cardPlaced ? new Point(_card.X + (target.X - _card.X) * 0.22, _card.Y + (target.Y - _card.Y) * 0.22) : target;
            _cardPlaced = true;
            Canvas.SetLeft(card, Math.Round(_card.X));
            Canvas.SetTop(card, Math.Round(_card.Y));
        }

        private static Rect Lerp(Rect a, Rect b, double k) => new(
            a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k, a.Width + (b.Width - a.Width) * k, a.Height + (b.Height - a.Height) * k);
    }
}
