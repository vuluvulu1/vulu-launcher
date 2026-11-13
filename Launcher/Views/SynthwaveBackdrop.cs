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
    /// izleyiciye doğru akan neon ızgara. Pencere gizliyken ya da tema kapalıyken hiç çizmez.
    /// </summary>
    internal sealed class SynthwaveBackdrop : Control
    {
        internal const string Shader = @"
uniform float2 size; uniform float time; uniform float rise; uniform float bright; uniform float offsetY; uniform float par;
float hash(float2 p) { return fract(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float noise1(float x) { float i = floor(x), f = fract(x); return mix(hash(float2(i, 0.0)), hash(float2(i + 1.0, 0.0)), f * f * (3.0 - 2.0 * f)); }
half4 main(float2 local) {
    float2 coord = float2(local.x, local.y + offsetY);
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
        if (s > 0.9955 && t < 0.75) col += float3(0.9, 0.85, 1.0) * (0.35 + 0.65 * (0.5 + 0.5 * sin(time * 2.0 + s * 80.0))) * (1.0 - t / 0.75) * (s > 0.9985 ? 1.0 : 0.55);
        // Güneş: altına doğru kalınlaşan, aşağı kayan boşluklar; çevresinde pembe hale
        float2 c = float2(0.5 - par * 0.012, hz - 0.17 + (1.0 - rise) * 0.4);
        float2 d = (uv - c) * float2(aspect, 1.0);
        float r = length(d), R = 0.20;
        float glow = exp(-max(r - R, 0.0) * 7.0) * 0.75;
        col += float3(1.0, 0.18, 0.75) * glow * 0.6;
        if (r < R) {
            float v = (uv.y - (c.y - R)) / (2.0 * R);          // 0 üst, 1 alt
            float3 sun = mix(float3(1.0, 0.85, 0.48), float3(1.0, 0.24, 0.78), smoothstep(0.1, 0.95, v));
            float band = fract(v * 9.0 - time * 0.06);
            float gap = smoothstep(0.45, 1.0, v) * 0.55;       // üstte boşluk yok
            if (band < gap) sun = col;
            col = mix(col, sun, smoothstep(R, R - 0.004, r));
        }
        // Uzak dağlar: daha yüksek, sise karışmış açık mor; güneşin altını örter, kenarında soluk camgöbeği
        float x2 = (uv.x - par * 0.018) * 3.2;
        float m2 = noise1(x2 + 20.0) * 0.58 + noise1(x2 * 2.4 + 31.0) * 0.3 + noise1(x2 * 6.3 + 44.0) * 0.12;
        float mh2 = hz - (0.05 + m2 * 0.15) * (1.0 - (1.0 - abs(uv.x - 0.5) * 2.0) * 0.55);
        if (uv.y > mh2) {
            col = mix(col, float3(0.17, 0.03, 0.30), 0.88);
            col += float3(0.0, 0.84, 1.0) * exp(-(uv.y - mh2) * size.y * 0.5) * 0.35;
        }
        // Dağlar: ufkun hemen üstünde koyu mor siluet, kenarında pembe çizgi
        float x = (uv.x - par * 0.03) * 7.0;
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
        float wx = (uv.x - 0.5 + par * 0.06) * aspect * z * 4.0;
        float wz = z * 2.0 + time * 1.6;
        // Çizgiye uzaklık (0: çizginin üstü) ve bir pikselin dünya ölçüsündeki boyu (SkSL'de fwidth yok, elle)
        float dx = 0.5 - abs(fract(wx) - 0.5), dz = 0.5 - abs(fract(wz) - 0.5);
        float px = aspect * z * 4.0 / size.x, pz = 0.36 / (y * y) / size.y;
        float vx = (1.0 - smoothstep(0.0, px * 2.4, dx)) * clamp(0.6 / (px * 30.0), 0.0, 1.0);
        float vz = (1.0 - smoothstep(0.0, pz * 2.4, dz)) * clamp(0.6 / (pz * 30.0), 0.0, 1.0);
        float gx = exp(-dx / max(px * 7.0, 0.0001)) * clamp(0.6 / (px * 30.0), 0.0, 1.0);
        float gz = exp(-dz / max(pz * 7.0, 0.0001)) * clamp(0.6 / (pz * 30.0), 0.0, 1.0);
        float fade = smoothstep(0.0, 0.2, y);             // ufukta silikleşir
        col = mix(float3(0.07, 0.0, 0.16), float3(0.027, 0.0, 0.06), smoothstep(0.0, 0.35, y));
        col += float3(1.0, 0.18, 0.95) * vz * (0.15 + 0.95 * fade);
        col += float3(0.0, 0.84, 1.0) * vx * (0.3 + 0.8 * fade);
        col += float3(1.0, 0.18, 0.95) * gz * 0.22 * fade + float3(0.0, 0.84, 1.0) * gx * 0.18 * fade;
        // Ufuk çizgisinin parıltısı
        col += float3(1.0, 0.25, 0.8) * exp(-y * size.y * 0.05) * 0.5;
    }
    // Ufuk sisi: çizginin iki yanında pembe bir pus
    col += float3(0.9, 0.2, 0.7) * exp(-abs(uv.y - hz) * 22.0) * 0.18;
    // Film greni
    col += (hash(coord + fract(time) * 91.7) - 0.5) * 0.03;
    // Vinyet
    float2 q = uv - 0.5;
    col *= 1.0 - dot(q, q) * 0.9;
    // Parlaklık: 0 karanlık, 1 normal, 1'in üstü beyaza doğru parlama
    col = bright <= 1.0 ? col * bright : col + float3(1.0, 0.35, 0.9) * clamp(bright - 1.0, 0.0, 1.0) * 0.8;
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

        /// <summary>Ufuk çizgisi (gölgelendiricideki hz ile aynı).</summary>
        internal const double Horizon = 0.64;

        /// <summary>Hareketli arka plan açık mı (Ayarlar); kapalıyken sahne tek kare çizilip durur.</summary>
        public static bool Animated { get; set; } = true;
        public static event Action? AnimatedChanged;
        /// <summary>Oyun çalışırken sahne durur: ekran kartı oyuna kalsın (HomeViewModel.GameRunning).</summary>
        public static bool GameRunning { get; set; }
        public static void SetAnimated(bool value)
        {
            Animated = value;
            AnimatedChanged?.Invoke();
        }

        private readonly SynthLayer _sky, _ground;
        private readonly DispatcherTimer _skyTimer, _groundTimer;
        private readonly DateTime _start = DateTime.UtcNow;

        /// <summary>
        /// Elle sürülen sahne (açılış animasyonu): zamanlayıcılar çalışmaz; <see cref="Time"/>, <see cref="Rise"/> ve
        /// <see cref="Brightness"/> verilip InvalidateVisual çağrılır.
        /// </summary>
        public bool Manual { get; set; }
        /// <summary>Izgaranın ve yıldızların zamanı (sn); hızlandırmak için daha hızlı artırılır.</summary>
        public double Time { get; set; }
        /// <summary>Güneşin doğuşu: 0 ufkun altında, 1 yerinde.</summary>
        public double Rise { get; set; } = 1;
        /// <summary>0 karanlık, 1 normal, 2 beyaz parlama.</summary>
        public double Brightness { get; set; } = 1;

        internal double CurrentTime => Manual ? Time : Animated ? (DateTime.UtcNow - _start).TotalSeconds : 4.0;
        /// <summary>Fareye göre paralaks (-1 sol, 1 sağ); yumuşakça hedefe yaklaşır.</summary>
        internal double Parallax { get; private set; }
        private double _parallaxTarget;
        private TopLevel? _top;
        internal double CurrentRise => Manual ? Rise : 1;
        internal double CurrentBrightness => Manual ? Brightness : 1;

        /// <summary>
        /// Performans: sahne iki katmandır. Yalnızca ufkun altındaki ızgara akıcı güncellenir (pencerenin ~üçte biri);
        /// gökyüzü, güneş ve yıldızlar saniyede birkaç kez. Bir katman yenilenince üstündeki arayüz de o bölgede yeniden
        /// çizildiği için asıl kazanç buradan gelir. Pencere odakta değilken ikisi de yavaşlar; gizliyken durur.
        /// </summary>
        public SynthwaveBackdrop()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            _sky = new SynthLayer(this, top: true);
            _ground = new SynthLayer(this, top: false);
            LogicalChildren.Add(_sky); VisualChildren.Add(_sky);
            LogicalChildren.Add(_ground); VisualChildren.Add(_ground);
            _skyTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(125), DispatcherPriority.Background, (_, _) => Tick(_sky));
            _groundTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, (_, _) => Tick(_ground));
        }

        private void Tick(SynthLayer layer)
        {
            if (Manual || !Animated || GameRunning || !IsEffectivelyVisible) return;
            if (TopLevel.GetTopLevel(this) is not Window { IsVisible: true, WindowState: not WindowState.Minimized } window) return;
            // Odakta değilken yavaş: ızgara saniyede 8, gökyüzü 2 kare
            var want = TimeSpan.FromMilliseconds(layer == _ground ? (window.IsActive ? 40 : 125) : (window.IsActive ? 125 : 500));
            var timer = layer == _ground ? _groundTimer : _skyTimer;
            if (timer.Interval != want) timer.Interval = want;
            if (layer == _ground)
            {
                double before = Parallax;
                Parallax += (_parallaxTarget - Parallax) * 0.12;
                // Paralaks değiştikçe gökyüzü de (güneş, dağlar) birlikte kayar
                if (Math.Abs(Parallax - before) > 0.002) _sky.InvalidateVisual();
            }
            layer.InvalidateVisual();
        }

        public new void InvalidateVisual()
        {
            _sky.InvalidateVisual();
            _ground.InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _sky.Measure(availableSize);
            _ground.Measure(availableSize);
            return default;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double split = Math.Round(finalSize.Height * Horizon);
            _sky.Arrange(new Rect(0, 0, finalSize.Width, split));
            _ground.Arrange(new Rect(0, split, finalSize.Width, finalSize.Height - split));
            return finalSize;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _skyTimer.Start();
            _groundTimer.Start();
            AnimatedChanged += InvalidateVisual;
            _top = TopLevel.GetTopLevel(this);
            _top?.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _skyTimer.Stop();
            _groundTimer.Stop();
            AnimatedChanged -= InvalidateVisual;
            _top?.RemoveHandler(Avalonia.Input.InputElement.PointerMovedEvent, OnPointerMoved);
            _top = null;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            if (_top == null || _top.Bounds.Width < 1) return;
            _parallaxTarget = Math.Clamp(e.GetPosition(_top).X / _top.Bounds.Width * 2 - 1, -1, 1);
        }

        /// <summary>Sahnenin bir parçası (gökyüzü ya da zemin).</summary>
        private sealed class SynthLayer(SynthwaveBackdrop owner, bool top) : Control
        {
            /// <summary>Çizim iş parçacığının kullandığı ara yüzey (yarı çözünürlük); yalnızca orada dokunulur.</summary>
            private readonly Holder _holder = new();

            public override void Render(DrawingContext context)
            {
                var full = owner.Bounds.Size;
                context.Custom(new Draw(new Rect(Bounds.Size), full, top ? 0 : Bounds.Y, owner.CurrentTime,
                    owner.CurrentRise, owner.CurrentBrightness, owner.Manual ? 0 : owner.Parallax, _holder));
            }
        }

        private sealed class Holder
        {
            public SKSurface? Surface;
            public int W, H;
        }

        private sealed class Draw(Rect bounds, Size full, double offsetY, double time, double rise, double bright, double par, Holder holder) : ICustomDrawOperation
        {
            public Rect Bounds => bounds;
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }

            public void Render(ImmediateDrawingContext context)
            {
                var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (feature == null || bounds.Width < 1 || bounds.Height < 1) return;
                using var lease = feature.Lease();
                var canvas = lease.SkCanvas;
                var rect = new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height);
                if (Effect.Value == null)
                {
                    // Gölgelendirici açılamazsa sitedeki yedek gibi düz gradyan
                    using var fallback = new SKPaint
                    {
                        Shader = SKShader.CreateLinearGradient(new SKPoint(0, (float)-offsetY), new SKPoint(0, (float)(full.Height - offsetY)),
                            new[] { new SKColor(0, 0, 0), new SKColor(11, 0, 32), new SKColor(58, 10, 90), new SKColor(163, 32, 111), new SKColor(18, 0, 42), new SKColor(7, 0, 15) },
                            new[] { 0f, .35f, .55f, .64f, .642f, 1f }, SKShaderTileMode.Clamp),
                    };
                    canvas.DrawRect(rect, fallback);
                    return;
                }
                var uniforms = new SKRuntimeEffectUniforms(Effect.Value)
                {
                    ["size"] = new[] { (float)full.Width, (float)full.Height },
                    ["time"] = (float)(time % 3600),
                    ["rise"] = (float)rise,
                    ["bright"] = (float)bright,
                    ["offsetY"] = (float)offsetY,
                    ["par"] = (float)par,
                };
                using var shader = Effect.Value.ToShader(uniforms);
                using var paint = new SKPaint { Shader = shader };

                // Bir ara yüzeye çizilir, sonra yumuşatılarak büyütülür
                var gr = lease.GrContext;
                float scale = canvas.TotalMatrix.ScaleX;
                // Gökyüzü seyrek güncellenir: tam çözünürlük (dağ kenarları basamaklanmasın); zemin akıcı: %60
                double res = offsetY <= 0 ? 1.0 : 0.6;
                int w = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale * res)), h = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale * res));
                if (gr == null) { canvas.DrawRect(rect, paint); return; }
                if (holder.Surface == null || holder.W != w || holder.H != h)
                {
                    holder.Surface?.Dispose();
                    holder.Surface = SKSurface.Create(gr, false, new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
                    holder.W = w; holder.H = h;
                }
                if (holder.Surface == null) { canvas.DrawRect(rect, paint); return; }
                var off = holder.Surface.Canvas;
                off.ResetMatrix();
                off.Scale(w / (float)bounds.Width, h / (float)bounds.Height);
                off.DrawRect(rect, paint);
                off.Flush();
                using var image = holder.Surface.Snapshot();
                canvas.DrawImage(image, rect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            }
        }
    }
}
