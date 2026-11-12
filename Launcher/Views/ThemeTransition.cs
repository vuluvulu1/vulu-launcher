using System.Diagnostics;
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
    /// Tema geçişi: ortadan dışa doğru uzanan ışık çizgileriyle bir "olay ufku" tüneli ekranı kaplar, tam ortada tema
    /// değişir, tünel açılarak yeni temayı gösterir (~1,4 sn). Ufkun öbür ucu'na pembe-camgöbeği, Meşale'ye amber.
    /// vulu-web'deki /vulusalufuk geçişinin (vulusalGecis3d.js) hafif karşılığı; tek bir SkSL gölgelendirici.
    /// </summary>
    internal sealed class ThemeTransition : Control
    {
        public const string Shader = @"
uniform float2 size; uniform float progress; uniform float time; uniform float3 colA; uniform float3 colB;
float hash(float n) { return fract(sin(n) * 43758.5453); }
half4 main(float2 coord) {
    float2 p = (coord - size * 0.5) / size.y;
    float r = length(p);
    float a = atan(p.y, p.x);
    // Ekranı örten perde: ortada (progress 0,5) tam
    float cover = smoothstep(0.0, 0.45, progress) * (1.0 - smoothstep(0.55, 1.0, progress));
    // Işık çizgileri: açıya göre şeritler, dışa doğru akar
    float lane = floor(a * 38.0);
    float speed = 0.6 + hash(lane) * 1.4;
    float head = fract(hash(lane * 1.7) - time * speed * 0.9);
    float d = fract(r * 1.2 - time * speed * 1.6 + hash(lane * 3.1));
    float streak = smoothstep(0.75, 1.0, d) * step(0.55, hash(lane + 9.0)) * smoothstep(0.02, 0.25, r);
    // Genişleyen halka (tam ortada ekranın kenarına ulaşır) ve merkezdeki parıltı
    float ringR = progress * 1.9;
    float ring = exp(-abs(r - ringR) * 26.0) * (1.0 - smoothstep(0.7, 1.0, progress));
    float core = exp(-r * 6.0) * cover;
    float3 dark = float3(0.02, 0.0, 0.05);
    float3 col = mix(dark, colA, streak * 0.9) + colB * ring * 1.2 + mix(colA, float3(1.0), 0.5) * core;
    float alpha = clamp(cover * 1.15 + ring * 0.9 + streak * cover, 0.0, 1.0);
    return half4(half3(col * alpha), half(alpha));
}";

        private static readonly Lazy<SKRuntimeEffect?> Effect = new(() =>
        {
            try { return SKRuntimeEffect.CreateShader(Shader, out _); }
            catch { return null; }
        });

        private const double DurationMs = 1400;
        private readonly Stopwatch _clock = new();
        private (float R, float G, float B) _a, _b;
        private Action? _atMiddle;
        private bool _middleDone;
        private TaskCompletionSource? _done;
        private double _progress;

        public ThemeTransition()
        {
            IsHitTestVisible = false;
            IsVisible = false;
        }

        /// <summary>Geçişi oynatır; perde ekranı tam örttüğünde <paramref name="atMiddle"/> çağrılır (tema orada değişir).</summary>
        public Task PlayAsync(bool toUfuk, Action atMiddle)
        {
            _done?.TrySetResult();
            if (Effect.Value == null || TopLevel.GetTopLevel(this) is not { } top)
            {
                atMiddle();
                return Task.CompletedTask;
            }
            (_a, _b) = toUfuk
                ? ((1f, 0.18f, 0.95f), (0f, 0.84f, 1f))       // pembe çizgiler, camgöbeği halka
                : ((1f, 0.6f, 0.2f), (1f, 0.85f, 0.5f));      // amber kor
            _atMiddle = atMiddle;
            _middleDone = false;
            _done = new TaskCompletionSource();
            IsHitTestVisible = true;   // geçiş sürerken tıklamalar arkaya gitmesin
            IsVisible = true;
            _clock.Restart();
            top.RequestAnimationFrame(Frame);
            return _done.Task;
        }

        private void Frame(TimeSpan _)
        {
            _progress = Math.Min(1, _clock.Elapsed.TotalMilliseconds / DurationMs);
            if (!_middleDone && _progress >= 0.5)
            {
                _middleDone = true;
                try { _atMiddle?.Invoke(); } catch { }
            }
            InvalidateVisual();
            if (_progress < 1 && TopLevel.GetTopLevel(this) is { } top) { top.RequestAnimationFrame(Frame); return; }
            if (!_middleDone) { _middleDone = true; _atMiddle?.Invoke(); }
            IsVisible = false;
            IsHitTestVisible = false;
            _done?.TrySetResult();
        }

        public override void Render(DrawingContext context) =>
            context.Custom(new Draw(new Rect(Bounds.Size), _progress, _clock.Elapsed.TotalSeconds, _a, _b));

        private sealed class Draw(Rect bounds, double progress, double time, (float R, float G, float B) a, (float R, float G, float B) b) : ICustomDrawOperation
        {
            public Rect Bounds => bounds;
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }

            public void Render(ImmediateDrawingContext context)
            {
                var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (feature == null || Effect.Value == null) return;
                using var lease = feature.Lease();
                var uniforms = new SKRuntimeEffectUniforms(Effect.Value)
                {
                    ["size"] = new[] { (float)bounds.Width, (float)bounds.Height },
                    ["progress"] = (float)progress,
                    ["time"] = (float)time,
                    ["colA"] = new[] { a.R, a.G, a.B },
                    ["colB"] = new[] { b.R, b.G, b.B },
                };
                using var shader = Effect.Value.ToShader(uniforms);
                using var paint = new SKPaint { Shader = shader };
                lease.SkCanvas.DrawRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height), paint);
            }
        }
    }
}
