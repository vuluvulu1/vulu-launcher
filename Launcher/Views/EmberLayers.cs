using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Meşale açılışının karanlık örtüsü. Açılış başlayana kadar düz koyu; sonra yazının ortasından dışa doğru gürültülü
    /// bir alev cephesiyle yanarak açılır. Cephe web arayüzündeki WebGL gölgelendiricisinin SkSL karşılığıyla çizilir.
    /// </summary>
    internal sealed class EmberSheet : Control
    {
        private const string Shader = @"
uniform float2 size; uniform float2 word; uniform float front; uniform float time;
float hash(float2 p) { return fract(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float noise(float2 p) {
    float2 i = floor(p), f = fract(p), u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + float2(1, 0)), u.x), mix(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
}
float fbm(float2 p) { return noise(p) * .5 + noise(p * 2.1 + 3.0) * .27 + noise(p * 4.3 + 7.0) * .15 + noise(p * 9.1 + 1.0) * .08; }
half4 main(float2 coord) {
    float2 p = (coord - word) / size.y;
    float e = length(p * float2(.55, 1.25)) + (fbm(p * 3.4 + float2(0.0, time * .25)) - .5) * .42 - front;
    float3 hot = mix(float3(1.0, .93, .68), float3(1.0, .46, .1), smoothstep(0.0, .022, e));
    hot = mix(hot, float3(.34, .08, .02), smoothstep(.022, .07, e));
    float3 sheet = mix(hot, float3(.031, .027, .024), smoothstep(.05, .17, e));
    float a = smoothstep(-.004, .004, e);
    float3 color = sheet * a + float3(1.0, .42, .08) * .34 * exp(min(e, 0.0) * 24.0) * (1.0 - a);
    return half4(half3(color), half(a));
}";

        private static readonly Lazy<SKRuntimeEffect?> Effect = new(() =>
        {
            try { return SKRuntimeEffect.CreateShader(Shader, out _); }
            catch { return null; }
        });

        /// <summary>Yazının ortası (bu denetime göre).</summary>
        public Point Word { get; set; }
        /// <summary>Cephenin yarıçapı (pencere yüksekliği cinsinden); null ise açılış başlamadı, örtü düz koyu.</summary>
        public double? Front { get; set; }
        public double Time { get; set; }

        public override void Render(DrawingContext context) =>
            context.Custom(new Draw(new Rect(Bounds.Size), Word, Front, Time));

        private sealed class Draw(Rect bounds, Point word, double? front, double time) : ICustomDrawOperation
        {
            public Rect Bounds => bounds;
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }

            public void Render(ImmediateDrawingContext context)
            {
                var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (feature == null) return;
                using var lease = feature.Lease();
                var canvas = lease.SkCanvas;
                var rect = new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height);
                var dark = new SKColor(8, 7, 6);
                if (front == null || Effect.Value == null)
                {
                    // Gölgelendirici yoksa karanlık ortadan dışa çekilir (web'deki yedek davranış)
                    using var plain = new SKPaint { Color = dark };
                    if (front == null) { canvas.DrawRect(rect, plain); return; }
                    float r = (float)Math.Max(0, front.Value * bounds.Height * 1.6);
                    using var hole = new SKPath { FillType = SKPathFillType.EvenOdd };
                    hole.AddRect(rect);
                    hole.AddCircle((float)word.X, (float)word.Y, r);
                    canvas.DrawPath(hole, plain);
                    return;
                }
                var uniforms = new SKRuntimeEffectUniforms(Effect.Value)
                {
                    ["size"] = new[] { (float)bounds.Width, (float)bounds.Height },
                    ["word"] = new[] { (float)word.X, (float)word.Y },
                    ["front"] = (float)front.Value,
                    ["time"] = (float)time,
                };
                using var shader = Effect.Value.ToShader(uniforms);
                using var paint = new SKPaint { Shader = shader };
                canvas.DrawRect(rect, paint);
            }
        }
    }

    /// <summary>Kıvılcımlar: ışığı topladıkça parlayan küçük korlar (web: m-sparks).</summary>
    internal sealed class EmberSparks : Control
    {
        private sealed class Spark
        {
            public double X, Y, Vx, Vy, Life, Left, Size;
        }

        private readonly List<Spark> _sparks = new();
        private readonly Random _random = new();

        public EmberSparks() => IsHitTestVisible = false;

        public void Clear() => _sparks.Clear();

        public void Add(double x, double y, double spread, double lift)
        {
            if (_sparks.Count > 280) return;
            double life = 500 + _random.NextDouble() * 900;
            _sparks.Add(new Spark
            {
                X = x, Y = y, Vx = (_random.NextDouble() - .5) * spread, Vy = -lift * (.4 + _random.NextDouble()),
                Life = life, Left = life, Size = .7 + _random.NextDouble() * 1.3,
            });
        }

        public double Next() => _random.NextDouble();

        /// <param name="dt">Geçen süre (ms).</param>
        public void Step(double dt)
        {
            _sparks.RemoveAll(s => (s.Left -= dt) <= 0);
            foreach (var s in _sparks)
            {
                s.X += (s.Vx + Math.Sin(s.Left / 90) * .025) * dt;
                s.Y += s.Vy * dt;
            }
            InvalidateVisual();
        }

        public override void Render(DrawingContext context) =>
            context.Custom(new Draw(new Rect(Bounds.Size), _sparks.Select(s => (s.X, s.Y, s.Size, s.Left / s.Life)).ToArray()));

        private sealed class Draw(Rect bounds, (double X, double Y, double Size, double Heat)[] sparks) : ICustomDrawOperation
        {
            public Rect Bounds => bounds;
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }

            public void Render(ImmediateDrawingContext context)
            {
                var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (feature == null || sparks.Length == 0) return;
                using var lease = feature.Lease();
                var canvas = lease.SkCanvas;
                using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
                foreach (var (x, y, size, heat) in sparks)
                {
                    paint.Color = new SKColor(255, (byte)(90 + heat * 130), (byte)(heat * 70), (byte)(Math.Min(1, heat * 1.6) * 255));
                    canvas.DrawCircle((float)x, (float)y, (float)size, paint);
                }
            }
        }
    }
}
