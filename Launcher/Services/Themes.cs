using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace VuluLauncher
{
    /// <summary>Arayüzden bağlanabilen tema durumu (ör. IsVisible="{Binding HasScene, Source={x:Static local:ThemeState.Instance}}").</summary>
    internal sealed partial class ThemeState : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public static ThemeState Instance { get; } = new();
        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isUfuk;
        /// <summary>Temanın hareketli bir arka plan sahnesi var mı (Ufkun öbür ucu, Derin Karanlık).</summary>
        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _hasScene;
    }

    /// <summary>
    /// Launcher temaları. Arayüzün renkleri App.axaml'daki fırçalardır; tema değişince yeni fırça konmaz, aynı fırçaların
    /// rengi değiştirilir. Böylece StaticResource ile bağlı her yer anında yeni rengi alır, yeniden başlatmak gerekmez.
    ///   mesale: varsayılan, amber ve koyu kahve (vulu'nun kendi rengi)
    ///   ufuk:   "Ufkun öbür ucu" (vulu-web'deki /vulusalufuk sürprizi): synthwave, mor gökyüzü, pembe ve camgöbeği neon
    ///   derin:  "Derin Karanlık": Minecraft'ın sculk mağarası; neredeyse siyah zemin, biyolüminesans camgöbeği damarlar
    /// Sahnesi olan temalarda sayfaların arkasında <see cref="Views.SynthwaveBackdrop"/> çizer (sahne temaya göre seçilir).
    /// Fırça olmayan değerler (BoxShadow ışıltıları) her temanın stil dosyasındadır; pencere tema adıyla aynı sınıfı
    /// taşıdığında geçerli olurlar (Views/UfukStyles.axaml, Views/DerinStyles.axaml).
    /// </summary>
    internal static class Themes
    {
        public const string Mesale = "mesale";
        public const string Ufuk = "ufuk";
        public const string Derin = "derin";
        private static readonly string[] All = { Mesale, Ufuk, Derin };

        public static string Current { get; private set; } = Mesale;
        public static bool IsUfuk => Current == Ufuk;
        public static bool IsDerin => Current == Derin;
        /// <summary>Hareketli arka plan sahnesi olan tema.</summary>
        public static bool HasScene => Current != Mesale;
        public static event Action? Changed;

        /// <summary>İlk uygulamada Meşale renkleri buradan alınır (App.axaml'daki değerler).</summary>
        private static Dictionary<string, Color[]>? _mesale;
        private static Dictionary<string, Color>? _mesaleSystem;

        private static readonly Dictionary<string, Color[]> UfukColors = new()
        {
            ["Bg"] = [C("#07000F")],
            ["Panel"] = [C("#F0100022")],
            ["SidePanel"] = [C("#8C080016")],
            ["Surface"] = [C("#E61A0533")],
            ["Raised"] = [C("#F02A0A4A")],
            ["Card"] = [C("#C4140030")],
            ["Glass"] = [C("#E0100024")],
            ["Hover"] = [C("#1FFF2DF1")],
            ["Line"] = [C("#38FF2DF1")],
            ["LineStrong"] = [C("#6600D7FF")],
            ["ButtonBg"] = [C("#C7140030")],
            ["ButtonLine"] = [C("#8000D7FF")],
            ["Text"] = [C("#F1E8FF")],
            ["Muted"] = [C("#D2C3EE")],
            ["Dim"] = [C("#A391C4")],
            ["Accent"] = [C("#FF2DF1")],
            ["AccentHi"] = [C("#9FF4FF")],
            ["AccentSoft"] = [C("#33FF2DF1")],
            ["AccentInk"] = [C("#FFFFFF")],
            ["PrimaryFill"] = [C("#FF2DF1"), C("#5800FF")],
            ["PrimaryFillHover"] = [C("#FF5CF5"), C("#7A2BFF")],
            ["Ok"] = [C("#9FF4FF")],
            ["Warn"] = [C("#FFD77A")],
            ["Error"] = [C("#FF5A7A")],
            ["TitleBarBg"] = [C("#E60A001A")],
            ["StagePlain"] = [C("#00000000"), C("#00000000")],
            ["StageGlow"] = [C("#00000000"), C("#00000000")],
            ["BarBg"] = [C("#F00C001E"), C("#07000F")],
            ["ChipBg"] = [C("#A0100024")],
            ["HeroBg"] = [C("#E6140030")],
            ["HeroHoverBg"] = [C("#F52A0A4A")],
            ["HeroLine"] = [C("#8000D7FF")],
            ["TileBg"] = [C("#A0100024")],
            ["TileLine"] = [C("#8000D7FF")],
            ["SoftFill"] = [C("#24FF2DF1")],
            ["SoftFillHover"] = [C("#38FF2DF1")],
            ["LoginBg"] = [C("#00000000"), C("#00000000"), C("#55000000")],
            ["BannerFallback"] = [C("#5800FF"), C("#FF2DF1")],
            ["TabGlow"] = [C("#4D00D7FF"), C("#0000D7FF")],
            ["ToggleSwitchFillOn"] = [C("#FF2DF1")],
            ["ToggleSwitchFillOnPointerOver"] = [C("#FF8CF0")],
            ["ToggleSwitchFillOnPressed"] = [C("#B000C8")],
            ["ToggleSwitchKnobFillOn"] = [C("#FFFFFF")],
            ["ToggleSwitchKnobFillOnPointerOver"] = [C("#FFFFFF")],
            ["ToggleSwitchKnobFillOnPressed"] = [C("#FFFFFF")],
            ["ToggleSwitchStrokeOff"] = [C("#8000D7FF")],
            ["ToggleSwitchKnobFillOff"] = [C("#D2C3EE")],
        };

        /// <summary>Derin Karanlık: kömür siyahı ve koyu deniz yeşili yüzeyler, sculk camgöbeği (#2FE6D8) vurgu.</summary>
        private static readonly Dictionary<string, Color[]> DerinColors = new()
        {
            ["Bg"] = [C("#030809")],
            ["Panel"] = [C("#F2061416")],
            ["SidePanel"] = [C("#99020A0C")],
            ["Surface"] = [C("#E6081C1F")],
            ["Raised"] = [C("#F00C2629")],
            ["Card"] = [C("#CC051315")],
            ["Glass"] = [C("#E6061416")],
            ["Hover"] = [C("#1A2FE6D8")],
            ["Line"] = [C("#2E2FE6D8")],
            ["LineStrong"] = [C("#5C2FE6D8")],
            ["ButtonBg"] = [C("#D1061618")],
            ["ButtonLine"] = [C("#662FE6D8")],
            ["Text"] = [C("#DCF6F2")],
            ["Muted"] = [C("#A9CBC7")],
            ["Dim"] = [C("#7AA19D")],
            ["Accent"] = [C("#2FE6D8")],
            ["AccentHi"] = [C("#8FF7EE")],
            ["AccentSoft"] = [C("#262FE6D8")],
            ["AccentInk"] = [C("#021F1D")],
            ["PrimaryFill"] = [C("#45F0E2"), C("#1FBFB3")],
            ["PrimaryFillHover"] = [C("#7AF5EB"), C("#2FD6CA")],
            ["Ok"] = [C("#8FF7EE")],
            ["Warn"] = [C("#F2D27A")],
            ["Error"] = [C("#FF6B6B")],
            ["TitleBarBg"] = [C("#E6030A0C")],
            ["StagePlain"] = [C("#00000000"), C("#00000000")],
            ["StageGlow"] = [C("#00000000"), C("#00000000")],
            ["BarBg"] = [C("#F0030C0E"), C("#020607")],
            ["ChipBg"] = [C("#A6041012")],
            ["HeroBg"] = [C("#E6061618")],
            ["HeroHoverBg"] = [C("#F50C2A2C")],
            ["HeroLine"] = [C("#662FE6D8")],
            ["TileBg"] = [C("#A6041012")],
            ["TileLine"] = [C("#662FE6D8")],
            ["SoftFill"] = [C("#1F2FE6D8")],
            ["SoftFillHover"] = [C("#332FE6D8")],
            ["LoginBg"] = [C("#00000000"), C("#00000000"), C("#66000000")],
            ["BannerFallback"] = [C("#0C3A3C"), C("#031012")],
            ["TabGlow"] = [C("#3D2FE6D8"), C("#002FE6D8")],
            ["ToggleSwitchFillOn"] = [C("#2FE6D8")],
            ["ToggleSwitchFillOnPointerOver"] = [C("#8FF7EE")],
            ["ToggleSwitchFillOnPressed"] = [C("#1FA89F")],
            ["ToggleSwitchKnobFillOn"] = [C("#021F1D")],
            ["ToggleSwitchKnobFillOnPointerOver"] = [C("#021F1D")],
            ["ToggleSwitchKnobFillOnPressed"] = [C("#021F1D")],
            ["ToggleSwitchStrokeOff"] = [C("#662FE6D8")],
            ["ToggleSwitchKnobFillOff"] = [C("#A9CBC7")],
        };

        private static readonly Dictionary<string, Color> UfukSystem = new()
        {
            ["SystemAccentColor"] = C("#FF2DF1"),
            ["SystemAccentColorLight1"] = C("#FF8CF0"),
            ["SystemAccentColorDark1"] = C("#B000C8"),
            ["AccentColor"] = C("#FF2DF1"),
            ["BgColor"] = C("#07000F"),
        };

        private static readonly Dictionary<string, Color> DerinSystem = new()
        {
            ["SystemAccentColor"] = C("#2FE6D8"),
            ["SystemAccentColorLight1"] = C("#8FF7EE"),
            ["SystemAccentColorDark1"] = C("#1FA89F"),
            ["AccentColor"] = C("#2FE6D8"),
            ["BgColor"] = C("#030809"),
        };

        /// <summary>Köşe yuvarlaklığı: Meşale köşeli, Ufuk yumuşak, Derin Karanlık hafif (taş gibi).</summary>
        private static (double Control, double Overlay) Corners(string name) => name switch
        {
            Ufuk => (8, 12),
            Derin => (6, 8),
            _ => (0, 0),
        };

        private static Color C(string hex) => Color.Parse(hex);

        public static string Normalize(string? name) => All.Contains(name) ? name! : Mesale;

        public static void Apply(string? name)
        {
            var app = Application.Current;
            if (app == null) return;
            name = Normalize(name);
            var res = app.Resources;

            // Meşale'nin kendi renkleri bir kez saklanır (geri dönmek için)
            if (_mesale == null)
            {
                _mesale = new();
                foreach (var key in UfukColors.Keys)
                    if (res.TryGetResource(key, null, out var value) && Read(value) is { } colors) _mesale[key] = colors;
                _mesaleSystem = new();
                foreach (var key in UfukSystem.Keys)
                    if (res.TryGetResource(key, null, out var value) && value is Color c) _mesaleSystem[key] = c;
            }

            var (palette, system) = name switch
            {
                Ufuk => (UfukColors, UfukSystem),
                Derin => (DerinColors, DerinSystem),
                _ => (_mesale, _mesaleSystem!),
            };
            foreach (var (key, colors) in palette)
                if (res.TryGetResource(key, null, out var value)) Write(value, colors);
            // Fluent'in vurgu renkleri Color değeridir (fırça değil): yeniden konur, DynamicResource ile bağlı olanlar güncellenir
            foreach (var (key, color) in system) res[key] = color;
            var (control, overlay) = Corners(name);
            res["ControlCornerRadius"] = new CornerRadius(control);
            res["OverlayCornerRadius"] = new CornerRadius(overlay);

            if (_appStyles == null)
            {
                _appStyles = ThemeStyles();
                app.Styles.Add(_appStyles);
            }
            if (app.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                foreach (var window in desktop.Windows) MarkWith(window, name);

            bool changed = Current != name;
            Current = name;
            ThemeState.Instance.IsUfuk = name == Ufuk;
            ThemeState.Instance.HasScene = name != Mesale;
            if (changed) Changed?.Invoke();
        }

        private static Styles? _appStyles;

        /// <summary>Bütün temaların stil dosyaları; her biri yalnızca pencere kendi sınıfını taşırken geçerlidir.</summary>
        private static Styles ThemeStyles() => new() { new Views.UfukStyles(), new Views.DerinStyles() };

        /// <summary>
        /// Kendi stilleri olan sayfalar (Oyna) için: sayfanın stilleri uygulamanınkinden önce geldiği için temaya özel
        /// stiller sayfanın kendi listesinin sonuna da eklenir (pencerenin tema sınıfıyla açılıp kapanırlar).
        /// </summary>
        public static void Attach(StyledElement element) => element.Styles.Add(ThemeStyles());

        /// <summary>Pencere açılınca: tema sınıfı verilir (sonradan açılan pencereler için).</summary>
        public static void Mark(Window window) => MarkWith(window, Current);

        private static void MarkWith(Window window, string name)
        {
            foreach (var theme in All) if (theme != Mesale) window.Classes.Set(theme, theme == name);
        }

        private static Color[]? Read(object? brush) => brush switch
        {
            SolidColorBrush s => [s.Color],
            GradientBrush g => g.GradientStops.Select(x => x.Color).ToArray(),
            _ => null,
        };

        private static void Write(object? brush, Color[] colors)
        {
            switch (brush)
            {
                case SolidColorBrush s when colors.Length > 0: s.Color = colors[0]; break;
                case GradientBrush g:
                    for (int i = 0; i < g.GradientStops.Count && i < colors.Length; i++) g.GradientStops[i].Color = colors[i];
                    break;
            }
        }
    }
}
