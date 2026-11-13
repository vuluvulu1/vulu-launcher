using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
uniform float2 size; uniform float time; uniform float rise; uniform float bright; uniform float offsetY; uniform float par; uniform float focus; uniform float fade;
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
    // Kapanış: saydamlık burada hesaplanır (fade 1 tam görünür, 0 tamamen saydam)
    float a = clamp(fade, 0.0, 1.0);
    return half4(half3(col * a), half(a));
}";

        /// <summary>
        /// Derin Karanlık: sculk mağarası. Koyu deniz yeşili mağara, tavanda sarkıtlar, zeminde iki kaya sırtı; zemindeki
        /// sculk damarları sensörden yayılan bir dalgayla nefes alır gibi parlar, sensörden nabız halkaları çıkar, sporlar
        /// yukarı süzülür. focus 1: sensör ekranın ortasında (açılış ekranı).
        /// </summary>
        internal const string DerinShader = @"
uniform float2 size; uniform float time; uniform float rise; uniform float bright; uniform float offsetY; uniform float par; uniform float focus; uniform float fade;
float hash(float2 p) { return fract(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float noise1(float x) { float i = floor(x), f = fract(x); return mix(hash(float2(i, 0.0)), hash(float2(i + 1.0, 0.0)), f * f * (3.0 - 2.0 * f)); }
float noise2(float2 p) {
    float2 i = floor(p), f = fract(p), u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + float2(1, 0)), u.x), mix(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
}
float fbm(float2 p) { return noise2(p) * .5 + noise2(p * 2.1 + 3.0) * .27 + noise2(p * 4.3 + 7.0) * .15 + noise2(p * 9.1 + 1.0) * .08; }
half4 main(float2 local) {
    float2 coord = float2(local.x, local.y + offsetY);
    float2 uv = coord / size;
    float aspect = size.x / size.y;
    float3 cyan = float3(0.184, 0.902, 0.847);
    // Sensör: sahnede sağ altta, açılışta ortada
    float2 s = mix(float2(0.6 - par * 0.02, 0.8), float2(0.5, 0.5), focus);
    float r = length((uv - s) * float2(aspect, 1.0));
    // Mağara: sensörün çevresi koyu deniz yeşili, uzaklar kömür siyahı; duvarda kaya dokusu
    float3 col = mix(float3(0.043, 0.165, 0.173), float3(0.012, 0.03, 0.035), smoothstep(0.0, 0.8, r));
    col = mix(col, float3(0.008, 0.02, 0.024), smoothstep(0.3, 1.2, r));
    col *= 0.7 + 0.6 * fbm(uv * float2(aspect, 1.0) * 3.0 + float2(par * 0.1, 0.0));
    // Tavan: sarkıtlar
    float cx = (uv.x - par * 0.01) * 14.0;
    float stal = 0.10 + 0.05 * noise1(cx) + 0.09 * pow(noise1(cx * 2.7 + 3.0), 3.0);
    float ceiling = 1.0 - focus;
    if (uv.y < stal * ceiling) col = float3(0.006, 0.016, 0.018) + float3(0.0, 0.05, 0.05) * smoothstep(stal - 0.012, stal, uv.y);
    // Zemin: iki kaya sırtı
    float fx = (uv.x + par * 0.02) * 5.0;
    float floor1 = mix(0.80 + 0.04 * noise1(fx) - 0.03 * noise1(fx * 2.3 + 7.0), 2.0, focus);
    float floor2 = mix(0.89 + 0.03 * noise1(fx * 1.4 + 20.0), 2.0, focus);
    // Sculk damarları: dallanan çizgiler, sensöre yaklaştıkça sıklaşır; sensörden yayılan dalgayla parlar
    float2 q = uv * float2(aspect, 1.0) * 4.0;
    float v = abs(fbm(q + float2(0.0, time * 0.015)) - 0.5);
    float reach = smoothstep(1.25, 0.15, r) * mix(smoothstep(0.3, 0.62, uv.y), 1.0, focus) * rise;
    // Ana damarlar ince, dallar daha ince ve sık
    float vein = (1.0 - smoothstep(0.0, 0.0065, v)) * reach * 0.8;
    float v2 = abs(fbm(q * 2.3 + float2(5.0, time * 0.02)) - 0.5);
    vein += (1.0 - smoothstep(0.0, 0.006, v2)) * reach * smoothstep(0.9, 0.1, r) * 0.45;
    float halo = (1.0 - smoothstep(0.0, 0.05, v)) * reach;
    // Uzak duvarlarda soluk damarlar (üst yarı boş kalmasın)
    float v3 = abs(fbm(uv * float2(aspect, 1.0) * 2.2 + float2(11.0, 3.0)) - 0.5);
    col += cyan * (1.0 - smoothstep(0.0, 0.006, v3)) * 0.065 * (1.0 - focus) * rise;
    float breathe = 0.5 + 0.5 * sin(time * 1.7 - r * 7.0);
    if (uv.y > floor1) {
        col = mix(float3(0.027, 0.08, 0.09), float3(0.016, 0.05, 0.06), smoothstep(floor1, 1.0, uv.y));
        vein *= 1.3;
        col += cyan * exp(-(uv.y - floor1) * size.y * 0.25) * 0.25;
        // Sculk bloğu: zeminde nabızla parlayan küçük noktalar
        float dots = step(0.82, noise2(uv * float2(aspect, 1.0) * 70.0));
        col += cyan * dots * 0.35 * (0.4 + 0.6 * breathe) * smoothstep(1.1, 0.2, r);
    }
    if (uv.y > floor2) col = float3(0.012, 0.04, 0.047);
    col += cyan * vein * (0.35 + 0.75 * breathe);
    col += cyan * halo * 0.12 * (0.4 + 0.6 * breathe);
    // Sensörün kendisi ve nabız halkaları
    col += float3(0.3, 1.0, 0.95) * exp(-r * r * 900.0) * (0.7 + 0.3 * sin(time * 3.0)) * rise;
    for (int k = 0; k < 3; k++) {
        float ph = fract(time / 3.6 + float(k) / 3.0);
        float ring = exp(-abs(r - ph * 0.6) * 70.0) * (1.0 - ph) * (1.0 - ph);
        col += cyan * ring * 0.85 * rise;
    }
    // Sporlar: yukarı süzülen küçük ışıklar
    float2 g = uv * float2(22.0 * aspect, 22.0) + float2(0.0, time * 0.35);
    float2 cell = floor(g);
    float h = hash(cell);
    float2 jitter = float2(hash(cell + 3.1) - 0.5, hash(cell + 7.7) - 0.5) * 0.6;
    float sp = smoothstep(0.09, 0.0, length(fract(g) - 0.5 - jitter)) * step(0.93, h) * mix(smoothstep(0.3, 0.75, uv.y), 1.0, focus);
    col += float3(0.5, 0.97, 0.93) * sp * (0.45 + 0.55 * sin(time * 2.0 + h * 40.0));
    // Gren, vinyet, parlaklık (açılışta camgöbeği bir parlama)
    col += (hash(coord + fract(time) * 91.7) - 0.5) * 0.025;
    float2 qv = uv - 0.5;
    col *= 1.0 - dot(qv, qv) * 1.1;
    col = bright <= 1.0 ? col * bright : col + float3(0.45, 1.0, 0.95) * clamp(bright - 1.0, 0.0, 1.0) * 0.75;
    // Kapanış: sahne sensörden dışa yayılan bir ses dalgasıyla açılır; dalganın kenarı camgöbeği parlar
    float rr = (1.0 - clamp(fade, 0.0, 1.0)) * 1.8;
    float a = fade >= 1.0 ? 1.0 : smoothstep(rr - 0.14, rr, r);
    col += cyan * exp(-abs(r - rr) * 45.0) * (1.0 - fade) * 1.2;
    return half4(half3(col * a), half(a));
}";

        private static readonly Lazy<SKRuntimeEffect?> DerinEffect = new(() =>
        {
            try
            {
                var effect = SKRuntimeEffect.CreateShader(DerinShader, out string? errors);
                if (effect == null && errors != null) System.Diagnostics.Debug.WriteLine("Derin Karanlık gölgelendirici: " + errors);
                return effect;
            }
            catch { return null; }
        });

        private static readonly Lazy<SKRuntimeEffect?> UfukEffect = new(() =>
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
        /// <summary>Açılış ekranında sahnenin kapanış süresi (SplashView.BurnMs ile aynı).</summary>
        internal const double OpenMs = 1500;

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

        /// <summary>
        /// Elle sürülen sahne (açılış ekranı): sensör/güneş ortada, zaman çizelgesi <see cref="StartSplash"/> ve
        /// <see cref="OpenSplash"/> ile verilen anlardan çizim iş parçacığında hesaplanır.
        /// </summary>
        public bool Manual { get; set; }
        private long _splashStart, _splashOpen = -1;

        /// <summary>Açılış sahnesi baştan başlar (güneş doğar / damarlar yayılır).</summary>
        public void StartSplash()
        {
            _splashStart = Stopwatch.GetTimestamp();
            _splashOpen = -1;
            SendState();
        }

        /// <summary>Açılış sahnesi kapanmaya başlar (hızlanır ve saydamlaşır).</summary>
        public void OpenSplash()
        {
            _splashOpen = Stopwatch.GetTimestamp();
            SendState();
        }

        private readonly SynthLayer _sky, _ground;
        private readonly DispatcherTimer _watch;
        private double _parallaxTarget;
        private TopLevel? _top;
        private SceneState? _lastSent;

        /// <summary>
        /// Performans: sahne çizim iş parçacığında (compositor) çizilir; arayüz iş parçacığı meşgulken bile akar ve her
        /// karede arayüzün yeniden düzenlenmesine yol açmaz. Sahne iki katmandır: yalnızca alt katman akıcı (saniyede
        /// ~25 kare), üst katman saniyede birkaç kez güncellenir. Pencere odakta değilken ikisi de yavaşlar; gizliyken,
        /// oyun açıkken ya da hareketli arka plan kapalıyken tek kare çizilir.
        /// </summary>
        public SynthwaveBackdrop()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            _sky = new SynthLayer(this, top: true);
            _ground = new SynthLayer(this, top: false);
            LogicalChildren.Add(_sky); VisualChildren.Add(_sky);
            LogicalChildren.Add(_ground); VisualChildren.Add(_ground);
            // Pencere durumu (görünür mü, odakta mı) arada bir okunur; değişince çizim tarafına bildirilir
            _watch = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Normal, (_, _) => SendState());
        }

        /// <summary>Çizim tarafına gönderilen durum (iki katman da aynı durumu alır).</summary>
        private sealed record SceneState(bool Derin, bool Manual, long SplashStart, long SplashOpen, bool Active, bool Focused, double Parallax);

        private void SendState()
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            bool visible = IsEffectivelyVisible && window is { IsVisible: true, WindowState: not WindowState.Minimized };
            var state = new SceneState(Themes.IsDerin, Manual, _splashStart, _splashOpen,
                Active: IsEffectivelyVisible && (Manual || (visible && Animated && !GameRunning)), Focused: window?.IsActive ?? true, Parallax: _parallaxTarget);
            if (state == _lastSent) return;
            _lastSent = state;
            _sky.Send(state);
            _ground.Send(state);
        }

        public new void InvalidateVisual()
        {
            _lastSent = null;
            SendState();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _sky.Measure(availableSize);
            _ground.Measure(availableSize);
            return default;
        }

        /// <summary>Sahnenin son yerleşim boyutu (katmanlar yerleşirken Bounds henüz güncellenmemiş olur).</summary>
        private Size _full;

        protected override Size ArrangeOverride(Size finalSize)
        {
            _full = finalSize;
            double split = Math.Round(finalSize.Height * (Themes.IsDerin && !Manual ? 0.45 : Horizon));
            _sky.Arrange(new Rect(0, 0, finalSize.Width, split));
            _ground.Arrange(new Rect(0, split, finalSize.Width, finalSize.Height - split));
            return finalSize;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Themes.Changed += OnThemeChanged;
            AnimatedChanged += InvalidateVisual;
            _top = TopLevel.GetTopLevel(this);
            _top?.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            _watch.Start();
            SendState();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _watch.Stop();
            AnimatedChanged -= InvalidateVisual;
            Themes.Changed -= OnThemeChanged;
            _top?.RemoveHandler(Avalonia.Input.InputElement.PointerMovedEvent, OnPointerMoved);
            _top = null;
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty) SendState();
        }

        private void OnThemeChanged()
        {
            InvalidateArrange();
            InvalidateVisual();
        }

        private void OnPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            if (_top == null || _top.Bounds.Width < 1) return;
            double target = Math.Clamp(e.GetPosition(_top).X / _top.Bounds.Width * 2 - 1, -1, 1);
            if (Math.Abs(target - _parallaxTarget) < 0.02) return;
            _parallaxTarget = target;
            SendState();
        }

        /// <summary>Sahnenin bir parçası (gökyüzü ya da zemin); çizimi kendi compositor görseli yapar.</summary>
        private sealed class SynthLayer(SynthwaveBackdrop owner, bool top) : Control
        {
            private CompositionCustomVisual? _visual;
            private SceneState? _state;

            public void Send(SceneState state)
            {
                _state = state;
                EnsureVisual();
                _visual?.SendHandlerMessage(new LayerMessage(state, owner._full, top ? 0 : Bounds.Y, top));
            }

            /// <summary>
            /// Compositor görseli, kontrolün kendi görseli hazır olduğu ilk anda oluşturulur (ağaca eklendiği anda henüz
            /// olmayabilir; o zaman yüklenince ya da ilk boyutlanmada denenir).
            /// </summary>
            private void EnsureVisual()
            {
                if (_visual != null || !this.IsAttachedToVisualTree()) return;
                var compositor = ElementComposition.GetElementVisual(this)?.Compositor;
                if (compositor == null) return;
                _visual = compositor.CreateCustomVisual(new SceneHandler());
                ElementComposition.SetElementChildVisual(this, _visual);
                _visual.Size = new Vector(Bounds.Width, Bounds.Height);
                if (_state != null) _visual.SendHandlerMessage(new LayerMessage(_state, owner._full, top ? 0 : Bounds.Y, top));
            }

            protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
            {
                base.OnAttachedToVisualTree(e);
                RetryVisual();
            }

            /// <summary>Görsel hazır olana kadar her karede yeniden dener (yalnızca ağaçtayken).</summary>
            private void RetryVisual()
            {
                EnsureVisual();
                if (_visual == null && this.IsAttachedToVisualTree())
                    TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => RetryVisual());
            }

            protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
            {
                base.OnLoaded(e);
                RetryVisual();
            }

            protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
            {
                _visual?.SendHandlerMessage(LayerMessage.Stop);
                ElementComposition.SetElementChildVisual(this, null);
                _visual = null;
                base.OnDetachedFromVisualTree(e);
            }

            protected override void OnSizeChanged(SizeChangedEventArgs e)
            {
                base.OnSizeChanged(e);
                Resize();
            }

            protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
            {
                base.OnPropertyChanged(change);
                if (change.Property == BoundsProperty) Resize();
            }

            private void Resize()
            {
                EnsureVisual();
                if (_visual == null) return;
                _visual.Size = new Vector(Bounds.Width, Bounds.Height);
                if (_state != null) Send(_state);
            }
        }

        private sealed record LayerMessage(SceneState? State, Size Full, double OffsetY, bool Top)
        {
            public static readonly LayerMessage Stop = new(null, default, 0, false);
        }

        /// <summary>
        /// Çizim iş parçacığında çalışır: kendi saatiyle sahnenin zamanını hesaplar, gerektiğinde kare ister. Arayüz iş
        /// parçacığından yalnızca durum mesajları gelir.
        /// </summary>
        private sealed class SceneHandler : CompositionCustomVisualHandler
        {
            private static readonly long Origin = Stopwatch.GetTimestamp();
            private LayerMessage? _m;
            private double _par;
            private long _lastFrame, _lastRender;
            private bool _ticking;
            private SKSurface? _surface;
            private int _w, _h;

            private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

            public override void OnMessage(object message)
            {
                if (message is not LayerMessage m) return;
                if (m.State == null) { _m = null; _ticking = false; return; }
                _m = m;
                Invalidate();
                if (!_ticking && (m.State.Active || Math.Abs(m.State.Parallax - _par) > 0.002))
                {
                    _ticking = true;
                    _lastFrame = Stopwatch.GetTimestamp();
                    RegisterForNextAnimationFrameUpdate();
                }
            }

            public override void OnAnimationFrameUpdate()
            {
                var m = _m;
                if (m?.State is not { } s) { _ticking = false; return; }
                long now = Stopwatch.GetTimestamp();
                double dt = Math.Min(100, Ms(_lastFrame, now));
                _lastFrame = now;

                // Paralaks yumuşakça hedefe yaklaşır (kare hızından bağımsız)
                double before = _par;
                _par += (s.Parallax - _par) * (1 - Math.Exp(-dt / 120.0));
                bool parallaxMoving = Math.Abs(_par - before) > 0.0005;

                if (s.Manual)
                {
                    // Açılış: her kare; kapanış bitince durur
                    Invalidate();
                    bool done = s.SplashOpen > 0 && Ms(s.SplashOpen, now) > OpenMs + 100;
                    if (!done && s.Active) { RegisterForNextAnimationFrameUpdate(); return; }
                    _ticking = false;
                    return;
                }

                double interval = m.Top ? (s.Focused ? 125 : 500) : (s.Focused ? 40 : 125);
                if (s.Active && Ms(_lastRender, now) >= interval) { _lastRender = now; Invalidate(); }
                else if (parallaxMoving && Ms(_lastRender, now) >= 33) { _lastRender = now; Invalidate(); }

                if (s.Active || parallaxMoving) RegisterForNextAnimationFrameUpdate();
                else _ticking = false;
            }

            public override void OnRender(ImmediateDrawingContext context)
            {
                if (_m?.State is not { } s) return;
                var size = EffectiveSize;
                if (size.X < 1 || size.Y < 1) return;
                long now = Stopwatch.GetTimestamp();
                double time, rise = 1, bright = 1, fade = 1, focus = 0, par = _par;
                if (s.Manual)
                {
                    // Açılış zaman çizelgesi (SplashView ile uyumlu): karanlıktan açılır, sahne doğar; kapanışta hızlanır,
                    // Ufuk kısa bir parlama yapar, sonra sahne kendi içinde saydamlaşır
                    double t = Ms(s.SplashStart, now);
                    double open = s.SplashOpen > 0 ? Math.Min(Ms(s.SplashOpen, now), OpenMs) : -1;
                    double p = open < 0 ? 0 : open / OpenMs;
                    double T = Math.Max(0, open) / 1000;
                    time = t / 1000 + 16 * T * T * T / 6.75;
                    rise = Out(Math.Clamp((t - 150) / 1600, 0, 1));
                    double flash = s.Derin || p <= 0.3 ? 0 : Math.Sin(Math.PI * Math.Clamp((p - 0.3) / 0.6, 0, 1));
                    bright = Out(Math.Clamp(t / 900, 0, 1)) + flash * 0.35;
                    double closing = Math.Clamp((p - 0.25) / 0.75, 0, 1);
                    fade = 1 - closing * closing * (3 - 2 * closing);
                    focus = 1;
                    par = 0;
                }
                else time = Animated ? Ms(Origin, now) / 1000 : 4.0;
                Draw(context, s.Derin ? DerinEffect.Value : UfukEffect.Value, new Rect(0, 0, size.X, size.Y), _m.Full, _m.OffsetY,
                    time, rise, bright, par, fade, focus, ref _surface, ref _w, ref _h);
            }

            private static double Out(double x) => 1 - Math.Pow(1 - x, 3);
        }

        private static void Draw(ImmediateDrawingContext context, SKRuntimeEffect? effect, Rect bounds, Size full, double offsetY,
            double time, double rise, double bright, double par, double fade, double focus, ref SKSurface? surface, ref int sw, ref int sh)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature == null || bounds.Width < 1 || bounds.Height < 1 || full.Width < 1) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var rect = new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height);
            if (effect == null)
            {
                // Gölgelendirici açılamazsa sitedeki yedek gibi düz gradyan
                using var fallback = new SKPaint
                {
                    Shader = SKShader.CreateLinearGradient(new SKPoint(0, (float)-offsetY), new SKPoint(0, (float)(full.Height - offsetY)),
                        new[] { new SKColor(0, 0, 0), new SKColor(11, 0, 32), new SKColor(58, 10, 90), new SKColor(163, 32, 111), new SKColor(18, 0, 42), new SKColor(7, 0, 15) },
                        new[] { 0f, .35f, .55f, .64f, .642f, 1f }, SKShaderTileMode.Clamp),
                    Color = SKColors.White.WithAlpha((byte)(Math.Clamp(fade, 0, 1) * 255)),
                };
                canvas.DrawRect(rect, fallback);
                return;
            }
            var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["size"] = new[] { (float)full.Width, (float)full.Height },
                ["time"] = (float)(time % 3600),
                ["rise"] = (float)rise,
                ["bright"] = (float)bright,
                ["offsetY"] = (float)offsetY,
                ["par"] = (float)par,
                ["focus"] = (float)focus,
                ["fade"] = (float)fade,
            };
            using var shader = effect.ToShader(uniforms);
            using var paint = new SKPaint { Shader = shader };

            // Bir ara yüzeye çizilir, sonra yumuşatılarak büyütülür: üst katman tam, alt katman %60 çözünürlük
            var gr = lease.GrContext;
            float scale = canvas.TotalMatrix.ScaleX;
            double res = offsetY <= 0 ? 1.0 : 0.6;
            int w = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale * res)), h = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale * res));
            if (gr == null) { canvas.DrawRect(rect, paint); return; }
            if (surface == null || sw != w || sh != h)
            {
                surface?.Dispose();
                surface = SKSurface.Create(gr, false, new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
                sw = w; sh = h;
            }
            if (surface == null) { canvas.DrawRect(rect, paint); return; }
            var off = surface.Canvas;
            off.ResetMatrix();
            off.Clear(SKColors.Transparent);
            off.Scale(w / (float)bounds.Width, h / (float)bounds.Height);
            off.DrawRect(rect, paint);
            off.Flush();
            using var image = surface.Snapshot();
            canvas.DrawImage(image, rect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }
    }
}
