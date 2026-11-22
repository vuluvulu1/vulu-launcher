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
    /// Tema geçişi: ekranın o anki görüntüsü alınır, tema değişir, eski görüntü temaya özgü bir efektle kaybolur
    /// (Meşale'ye yanarak, Kütüphane'ye sayfa çevirerek). Görüntü alınamazsa yedek olarak ortadan dışa uzanan ışık
    /// çizgileriyle bir tünel ekranı örter, tam ortada tema değişir. Hepsi tek bir SkSL gölgelendiricisi.
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

        /// <summary>
        /// Kütüphane'ye geçiş: eski görüntü 110 px'lik dikey şeritlere bölünür; şeritler sağdan sola sırayla, sol kenarları
        /// üstünde kitap sayfası gibi dönüp kapanır, arkalarından yeni tema görünür. Dönen şerit kararır, kenarında amber çizgi.
        /// front: 0..1 ilerleme.
        /// </summary>
        public const string PagesShader = @"
uniform shader img; uniform float2 size; uniform float2 imgSize; uniform float front; uniform float time;
half4 main(float2 coord) {
    float cell = 110.0;
    float n = floor(size.x / cell) + 1.0;
    float id = floor(coord.x / cell);
    float lx = (coord.x - id * cell) / cell;
    float order = (n - 1.0 - id) / max(n - 1.0, 1.0);
    float t = clamp((front - order * 0.6) / 0.4, 0.0, 1.0);
    t = t * t * (3.0 - 2.0 * t);
    float w = cos(t * 1.5707963);
    if (w < 0.002 || lx > w) { return half4(0.0); }
    // Dönen sayfa hafifçe eğilir: üst ve alt kenarlar içe çekilir (perspektif)
    float squeeze = 0.06 * t * (lx / w);
    float y = (coord.y / size.y - 0.5) / (1.0 - squeeze) + 0.5;
    if (y < 0.0 || y > 1.0) { return half4(0.0); }
    float sx = id * cell + (lx / w) * cell;
    half4 c = img.eval(float2(sx, y * size.y) * imgSize / size);
    float shade = 1.0 - 0.65 * t * (0.4 + 0.6 * lx / w);
    float edge = smoothstep(w - 2.5 / cell, w, lx) * step(0.001, t);
    half3 amber = half3(0.949, 0.639, 0.227);
    return half4(mix(c.rgb * half(shade), amber * c.a, half(edge * 0.8)), c.a);
}";

        private static readonly Lazy<SKRuntimeEffect?> Pages = new(() =>
        {
            try { return SKRuntimeEffect.CreateShader(PagesShader, out _); }
            catch { return null; }
        });

        private const double BurnMs = 1500;
        private const double PagesMs = 1150;
        private SKImage? _snapshot;
        /// <summary>Eski görüntünün üstünden ilerleyen geçiş (yanma ya da sayfa çevirme) sürüyor.</summary>
        private bool _burning;
        private SKRuntimeEffect? _sheetEffect;
        private double _sheetMs = BurnMs;

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
        /// Geçişi oynatır; her temanın kendi geçişi vardır:
        ///   Meşale: ekranın görüntüsü alınır, tema hemen değişir, eski görüntü yanarak açılır.
        ///   Kütüphane: aynı düzen, eski görüntü dikey şeritler halinde kitap sayfası gibi çevrilir.
        /// Görüntü alınamazsa ışık tüneli: perde ekranı tam örttüğünde <paramref name="atMiddle"/> çağrılır.
        /// </summary>
        public Task PlayAsync(string theme, Action atMiddle)
        {
            // Önceki geçiş temasını henüz uygulamadıysa önce o uygulanır (hiçbir seçim kaybolmasın)
            if (!_middleDone && _atMiddle is { } pending)
            {
                _atMiddle = null;
                _middleDone = true;
                try { pending(); } catch { }
            }
            _done?.TrySetResult();
            var sheet = theme switch { Themes.Mesale => Burn.Value, Themes.Kutuphane => Pages.Value, _ => null };
            if (sheet != null && TakeSnapshot() is { } shot)
            {
                Retire(_snapshot);
                _snapshot = shot;
                _sheetEffect = sheet;
                _sheetMs = theme == Themes.Kutuphane ? PagesMs : BurnMs;
                _burning = true;
                // Tema hemen değil, eski görüntü ekrana geldikten sonra uygulanır: uygulama arayüzü bir süre meşgul eder,
                // bu sırada ekranda donuk bir arayüz yerine eski görüntü durur; efekt tema uygulanınca başlar
                _atMiddle = atMiddle;
                _middleDone = false;
                _waitFrames = 2;
                _progress = 0;
                _done = new TaskCompletionSource();
                IsHitTestVisible = true;
                IsVisible = true;
                _clock.Restart();
                StartLoop();
                return _done.Task;
            }
            _burning = false;
            if (Effect.Value == null || TopLevel.GetTopLevel(this) is not { } top)
            {
                atMiddle();
                return Task.CompletedTask;
            }
            // Görüntü alınamadı: amber ışık tüneli
            (_a, _b) = ((1f, 0.6f, 0.2f), (1f, 0.85f, 0.5f));
            _atMiddle = atMiddle;
            _middleDone = false;
            _done = new TaskCompletionSource();
            IsHitTestVisible = true;   // geçiş sürerken tıklamalar arkaya gitmesin
            IsVisible = true;
            _clock.Restart();
            StartLoop();
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
                // Pikseller doğrudan kopyalanır (PNG'ye çevirip geri okumak yüz milisaniyeler sürüyordu)
                int stride = pixels.Width * 4;
                var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(stride * pixels.Height);
                try
                {
                    bitmap.CopyPixels(new PixelRect(pixels), buffer, stride * pixels.Height, stride);
                    var info = new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    return SKImage.FromPixelCopy(info, buffer, stride);
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
            }
            catch { return null; }
        }

        // Tek kare döngüsü: geçiş sürerken yenisi başlarsa ikinci bir döngü açılmaz (her geçiş bir döngü daha ekliyordu)
        private bool _looping;

        private void StartLoop()
        {
            if (_looping || TopLevel.GetTopLevel(this) is not { } top) return;
            _looping = true;
            top.RequestAnimationFrame(Frame);
        }

        private void Continue()
        {
            if (TopLevel.GetTopLevel(this) is { } top) top.RequestAnimationFrame(Frame);
            else _looping = false;
        }

        // Eski görüntü hemen silinmez: çizim iş parçacığı onu son karede hâlâ çiziyor olabilir (silinmiş belleği okumak
        // launcher'ı iz bırakmadan düşürüyordu). Bir süre bekletilip öyle silinir.
        private readonly List<(SKImage Image, long At)> _retired = new();

        private void Retire(SKImage? image)
        {
            if (image != null) _retired.Add((image, Environment.TickCount64));
        }

        private void DisposeRetired(bool all = false)
        {
            long now = Environment.TickCount64;
            for (int i = _retired.Count - 1; i >= 0; i--)
                if (all || now - _retired[i].At > 1000) { _retired[i].Image.Dispose(); _retired.RemoveAt(i); }
        }

        /// <summary>Görüntü geçişinde temayı uygulamadan önce beklenen kare sayısı (görüntü ekrana gelsin).</summary>
        private int _waitFrames;

        private void Frame(TimeSpan _)
        {
            if (_burning && _waitFrames > 0)
            {
                if (--_waitFrames == 0)
                {
                    _middleDone = true;
                    try { _atMiddle?.Invoke(); } catch { }
                    _atMiddle = null;
                    _clock.Restart();       // efekt tema uygulandıktan sonra baştan
                }
                InvalidateVisual();
                if (TopLevel.GetTopLevel(this) != null) Continue();
                else { _looping = false; _waitFrames = 0; _middleDone = true; _atMiddle?.Invoke(); _atMiddle = null; }
                return;
            }
            _progress = Math.Min(1, _clock.Elapsed.TotalMilliseconds / (_burning ? _sheetMs : DurationMs));
            if (!_middleDone && _progress >= 0.5)
            {
                _middleDone = true;
                try { _atMiddle?.Invoke(); } catch { }
            }
            InvalidateVisual();
            DisposeRetired();
            if (_progress < 1 && TopLevel.GetTopLevel(this) != null) { Continue(); return; }
            _looping = false;
            // Son görüntüler de geçiş bitince bir süre sonra bırakılır (yeni geçiş gelmezse bellekte kalmasın)
            Avalonia.Threading.DispatcherTimer.RunOnce(() => { if (!_looping) DisposeRetired(); }, TimeSpan.FromMilliseconds(1500));
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
            if (_burning && _snapshot != null && _sheetEffect != null)
                context.Custom(new BurnDraw(_sheetEffect, new Rect(Bounds.Size), _snapshot,
                    _sheetEffect == Pages.Value ? _progress : Front(_progress), _clock.Elapsed.TotalSeconds));
            else
                context.Custom(new Draw(new Rect(Bounds.Size), _progress, _clock.Elapsed.TotalSeconds, _a, _b));
        }

        private sealed class BurnDraw(SKRuntimeEffect effect, Rect bounds, SKImage image, double front, double time) : ICustomDrawOperation
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
                var uniforms = new SKRuntimeEffectUniforms(effect)
                {
                    ["size"] = new[] { (float)bounds.Width, (float)bounds.Height },
                    ["imgSize"] = new[] { (float)image.Width, (float)image.Height },
                    ["front"] = (float)front,
                    ["time"] = (float)time,
                };
                using var imageShader = image.ToShader();
                var children = new SKRuntimeEffectChildren(effect) { ["img"] = imageShader };
                using var shader = effect.ToShader(uniforms, children);
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
