using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace VuluLauncher.Views
{
    /// <summary>
    /// "Ufkun öbür ucu" temasının hareketli arka planı: vulu-web'deki /vulusalufuk sahnesinin (three.js) tek bir SkSL
    /// gölgelendiriciyle çizilen hafif karşılığı. Mor gökyüzü ve yıldızlar, çizgili batan güneş, dağ silueti ve
    /// izleyiciye doğru akan neon ızgara. Saniyede 30 kare; pencere gizliyken ya da tema kapalıyken hiç çizmez.
    /// </summary>
    internal sealed class SynthwaveBackdrop : Control
    {
        internal const string Shader = @"
uniform float2 size; uniform float time;
float hash(float2 p) { return fract(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float noise1(float x) { float i = floor(x), f = fract(x); return mix(hash(float2(i, 0.0)), hash(float2(i + 1.0, 0.0)), f * f * (3.0 - 2.0 * f)); }
half4 main(float2 coord) {
    float2 uv = coord / size;
    float aspect = size.x / size.y;
    float hz = 0.64;                       // ufuk çizgisi
    float3 col;
    if (uv.y < hz) {
        // Gökyüzü: tepede siyah, ufka doğru mor ve pembe (ufuk.css .uf-arka)
        float t = uv.y / hz;
        col = mix(float3(0.0, 0.0, 0.0), float3(0.043, 0.0, 0.125), smoothstep(0.0, 0.45, t));
        col = mix(col, float3(0.227, 0.039, 0.353), smoothstep(0.45, 0.85, t));
        col = mix(col, float3(0.64, 0.125, 0.435), smoothstep(0.85, 1.0, t));
        // Yıldızlar (yukarıda, yavaşça parlayıp söner)
        float2 g = floor(coord / 3.0);
        float s = hash(g);
        if (s > 0.9975 && t < 0.7) col += float3(0.9, 0.85, 1.0) * (0.5 + 0.5 * sin(time * 2.0 + s * 80.0)) * (1.0 - t / 0.7);
        // Güneş: altına doğru kalınlaşan, aşağı kayan boşluklar; çevresinde pembe hale
        float2 c = float2(0.5, hz - 0.17);
        float2 d = (uv - c) * float2(aspect, 1.0);
        float r = length(d), R = 0.20;
        float glow = exp(-max(r - R, 0.0) * 9.0) * 0.55;
        col += float3(1.0, 0.18, 0.75) * glow * 0.6;
        if (r < R) {
            float v = (uv.y - (c.y - R)) / (2.0 * R);          // 0 üst, 1 alt
            float3 sun = mix(float3(1.0, 0.85, 0.48), float3(1.0, 0.24, 0.78), smoothstep(0.1, 0.95, v));
            float band = fract(v * 9.0 - time * 0.25);
            float gap = smoothstep(0.45, 1.0, v) * 0.55;       // üstte boşluk yok
            if (band < gap) sun = col;
            col = mix(col, sun, smoothstep(R, R - 0.004, r));
        }
        // Dağlar: ufkun hemen üstünde koyu mor siluet, kenarında pembe çizgi
        float x = uv.x * 7.0;
        float m = (noise1(x) * 0.6 + noise1(x * 2.3 + 5.0) * 0.3 + noise1(x * 5.1 + 9.0) * 0.1);
        float edge = 1.0 - abs(uv.x - 0.5) * 2.0;              // ortada (güneşin önü) daha alçak
        float mh = hz - (0.03 + m * 0.12) * (1.0 - edge * 0.65);
        if (uv.y > mh) {
            col = mix(float3(0.07, 0.0, 0.16), float3(0.03, 0.0, 0.07), (uv.y - mh) / (hz - mh + 0.0001));
            col += float3(1.0, 0.18, 0.95) * exp(-(uv.y - mh) * size.y * 0.35) * 0.8;
        }
    } else {
        // Zemin: izleyiciye doğru akan perspektif ızgara (yatay pembe, dikey camgöbeği)
        float y = uv.y - hz;
        float z = 0.18 / max(y, 0.0008);
        float wx = (uv.x - 0.5) * aspect * z * 4.0;
        float wz = z * 2.0 + time * 1.6;
        // Çizgiye uzaklık (0: çizginin üstü) ve bir pikselin dünya ölçüsündeki boyu (SkSL'de fwidth yok, elle)
        float dx = 0.5 - abs(fract(wx) - 0.5), dz = 0.5 - abs(fract(wz) - 0.5);
        float px = aspect * z * 4.0 / size.x, pz = 0.36 / (y * y) / size.y;
        float vx = (1.0 - smoothstep(0.0, px * 2.4, dx)) * clamp(0.6 / (px * 30.0), 0.0, 1.0);
        float vz = (1.0 - smoothstep(0.0, pz * 2.4, dz)) * clamp(0.6 / (pz * 30.0), 0.0, 1.0);
        float fade = smoothstep(0.0, 0.2, y);             // ufukta silikleşir
        col = mix(float3(0.07, 0.0, 0.16), float3(0.027, 0.0, 0.06), smoothstep(0.0, 0.35, y));
        col += float3(1.0, 0.18, 0.95) * vz * (0.15 + 0.95 * fade);
        col += float3(0.0, 0.84, 1.0) * vx * (0.3 + 0.8 * fade);
        // Ufuk çizgisinin parıltısı
        col += float3(1.0, 0.25, 0.8) * exp(-y * size.y * 0.05) * 0.5;
    }
    // Vinyet
    float2 q = uv - 0.5;
    col *= 1.0 - dot(q, q) * 0.9;
    return half4(half3(col), 1.0);
}";

        private static readonly Lazy<SKRuntimeEffect?> Effect = new(() =>
        {
            try
            {
                var effect = SKRuntimeEffect.CreateShader(Shader, out string? errors);
                if (effect == null && errors != null) System.Diagnostics.Debug.WriteLine("Synthwave gölgelendirici: " + errors);
                return effect;
            }
            catch { return null; }
        });

        private readonly DispatcherTimer _timer;
        private readonly DateTime _start = DateTime.UtcNow;

        public SynthwaveBackdrop()
        {
            IsHitTestVisible = false;
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
            {
                if (IsEffectivelyVisible && TopLevel.GetTopLevel(this) is Window { IsVisible: true, WindowState: not WindowState.Minimized })
                    InvalidateVisual();
            });
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _timer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _timer.Stop();
            base.OnDetachedFromVisualTree(e);
        }

        public override void Render(DrawingContext context) =>
            context.Custom(new Draw(new Rect(Bounds.Size), (DateTime.UtcNow - _start).TotalSeconds));

        private sealed class Draw(Rect bounds, double time) : ICustomDrawOperation
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
                if (Effect.Value == null)
                {
                    // Gölgelendirici açılamazsa sitedeki yedek gibi düz gradyan
                    using var fallback = new SKPaint
                    {
                        Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, rect.Height),
                            new[] { new SKColor(0, 0, 0), new SKColor(11, 0, 32), new SKColor(58, 10, 90), new SKColor(163, 32, 111), new SKColor(18, 0, 42), new SKColor(7, 0, 15) },
                            new[] { 0f, .35f, .55f, .64f, .642f, 1f }, SKShaderTileMode.Clamp),
                    };
                    canvas.DrawRect(rect, fallback);
                    return;
                }
                var uniforms = new SKRuntimeEffectUniforms(Effect.Value)
                {
                    ["size"] = new[] { (float)bounds.Width, (float)bounds.Height },
                    ["time"] = (float)(time % 3600),
                };
                using var shader = Effect.Value.ToShader(uniforms);
                using var paint = new SKPaint { Shader = shader };
                canvas.DrawRect(rect, paint);
            }
        }
    }
}
