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

        /// <summary>
        /// Meşale'ye dönüş: ekranın o anki görüntüsü (mor tema) ortadan dışa doğru yanarak açılır, altından Meşale çıkar.
        /// Alev cephesi Meşale açılışındakiyle aynı (EmberSheet); yanmamış yerlerde eski görüntü durur.
        /// </summary>
        public const string BurnShader = @"
uniform shader img; uniform float2 size; uniform float2 imgSize; uniform float front; uniform float time;
float hash(float2 p) { return fract(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float noise(float2 p) {
    float2 i = floor(p), f = fract(p), u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + float2(1, 0)), u.x), mix(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
}
float fbm(float2 p) { return noise(p) * .5 + noise(p * 2.1 + 3.0) * .27 + noise(p * 4.3 + 7.0) * .15 + noise(p * 9.1 + 1.0) * .08; }
half4 main(float2 coord) {
    float2 p = (coord - size * 0.5) / size.y;
    float e = length(p * float2(.55, 1.25)) + (fbm(p * 3.4 + float2(0.0, time * .25)) - .5) * .42 - front;
    float3 old = img.eval(coord * imgSize / size).rgb;
    float3 hot = mix(float3(1.0, .93, .68), float3(1.0, .46, .1), smoothstep(0.0, .022, e));
    hot = mix(hot, float3(.34, .08, .02), smoothstep(.022, .07, e));
    float3 sheet = mix(hot, old, smoothstep(.05, .17, e));
    float a = smoothstep(-.004, .004, e);
    float3 color = sheet * a + float3(1.0, .42, .08) * .34 * exp(min(e, 0.0) * 24.0) * (1.0 - a);
    return half4(half3(color), half(a));
}";

        private static readonly Lazy<SKRuntimeEffect?> Burn = new(() =>
        {
            try { return SKRuntimeEffect.CreateShader(BurnShader, out _); }
            catch { return null; }
        });

        private const double BurnMs = 1500;
        private SKImage? _snapshot;
        private bool _burning;

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

        /// <summary>
        /// Geçişi oynatır. Ufkun öbür ucu'na: ışık tüneli, perde ekranı tam örttüğünde <paramref name="atMiddle"/> çağrılır.
        /// Meşale'ye: ekranın görüntüsü alınır, tema hemen değişir, eski görüntü yanarak açılır.
        /// </summary>
        public Task PlayAsync(bool toUfuk, Action atMiddle)
        {
            _done?.TrySetResult();
            if (!toUfuk && Burn.Value != null && TakeSnapshot() is { } shot)
            {
                _snapshot?.Dispose();
                _snapshot = shot;
                _burning = true;
                atMiddle();                // altta Meşale; üstte eski görüntü yanacak
                _atMiddle = null;
                _middleDone = true;
                _done = new TaskCompletionSource();
                IsHitTestVisible = true;
                IsVisible = true;
                _clock.Restart();
                TopLevel.GetTopLevel(this)!.RequestAnimationFrame(Frame);
                return _done.Task;
            }
            _burning = false;
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

        /// <summary>Pencerenin şu anki görüntüsü (yanma geçişi için), Skia resmi olarak.</summary>
        private SKImage? TakeSnapshot()
        {
            try
            {
                if (TopLevel.GetTopLevel(this) is not { } top) return null;
                double scale = top.RenderScaling;
                var pixels = new PixelSize(Math.Max(1, (int)(top.ClientSize.Width * scale)), Math.Max(1, (int)(top.ClientSize.Height * scale)));
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
                bitmap.Render(top);
                using var stream = new MemoryStream();
                bitmap.Save(stream);
                return SKImage.FromEncodedData(stream.ToArray());
            }
            catch { return null; }
        }

        private void Frame(TimeSpan _)
        {
            _progress = Math.Min(1, _clock.Elapsed.TotalMilliseconds / (_burning ? BurnMs : DurationMs));
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
            // Görüntü burada silinmez: çizim iş parçacığı son kareyi hâlâ çiziyor olabilir; bir sonraki geçişte değiştirilir
            _burning = false;
            InvalidateVisual();
            _done?.TrySetResult();
        }

        /// <summary>Alev cephesinin yarıçapı: yavaş tutuşur, hızlanarak yayılır (Meşale açılışıyla aynı).</summary>
        private double Front(double progress)
        {
            double w = Math.Max(1, Bounds.Width), h = Math.Max(1, Bounds.Height);
            double end = Math.Sqrt(Math.Pow(w / h * .42, 2) + .85 * .85) + .4;
            return -.22 + Math.Pow(progress, 1.7) * (end + .22);
        }

        public override void Render(DrawingContext context)
        {
            if (_burning && _snapshot != null)
                context.Custom(new BurnDraw(new Rect(Bounds.Size), _snapshot, Front(_progress), _clock.Elapsed.TotalSeconds));
            else
                context.Custom(new Draw(new Rect(Bounds.Size), _progress, _clock.Elapsed.TotalSeconds, _a, _b));
        }

        private sealed class BurnDraw(Rect bounds, SKImage image, double front, double time) : ICustomDrawOperation
        {
            public Rect Bounds => bounds;
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }

            public void Render(ImmediateDrawingContext context)
            {
                var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (feature == null || Burn.Value == null) return;
                using var lease = feature.Lease();
                var uniforms = new SKRuntimeEffectUniforms(Burn.Value)
                {
                    ["size"] = new[] { (float)bounds.Width, (float)bounds.Height },
                    ["imgSize"] = new[] { (float)image.Width, (float)image.Height },
                    ["front"] = (float)front,
                    ["time"] = (float)time,
                };
                using var imageShader = image.ToShader();
                var children = new SKRuntimeEffectChildren(Burn.Value) { ["img"] = imageShader };
                using var shader = Burn.Value.ToShader(uniforms, children);
                using var paint = new SKPaint { Shader = shader };
                lease.SkCanvas.DrawRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height), paint);
            }
        }

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
