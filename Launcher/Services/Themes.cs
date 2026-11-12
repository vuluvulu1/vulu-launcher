using Avalonia;
using Avalonia.Media;

namespace VuluLauncher
{
    /// <summary>
    /// Launcher temaları. Arayüzün renkleri App.axaml'daki fırçalardır; tema değişince yeni fırça konmaz, aynı fırçaların
    /// rengi değiştirilir. Böylece StaticResource ile bağlı her yer anında yeni rengi alır, yeniden başlatmak gerekmez.
    ///   mesale: varsayılan, amber ve koyu kahve (vulu'nun kendi rengi)
    ///   ufuk:   "Ufkun öbür ucu" (vulu-web'deki /vulusalufuk sürprizi): synthwave, mor gökyüzü, pembe ve camgöbeği neon;
    ///           sayfaların arkasında hareketli güneş ve ızgara (<see cref="Views.SynthwaveBackdrop"/>)
    /// </summary>
    internal static class Themes
    {
        public const string Mesale = "mesale";
        public const string Ufuk = "ufuk";

        public static string Current { get; private set; } = Mesale;
        public static bool IsUfuk => Current == Ufuk;
        public static event Action? Changed;

        /// <summary>İlk uygulamada Meşale renkleri buradan alınır (App.axaml'daki değerler).</summary>
        private static Dictionary<string, Color[]>? _mesale;

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
        };

        private static readonly Dictionary<string, Color> UfukSystem = new()
        {
            ["SystemAccentColor"] = C("#FF2DF1"),
            ["SystemAccentColorLight1"] = C("#FF8CF0"),
            ["SystemAccentColorDark1"] = C("#B000C8"),
            ["AccentColor"] = C("#FF2DF1"),
            ["BgColor"] = C("#07000F"),
        };
        private static Dictionary<string, Color>? _mesaleSystem;

        private static Color C(string hex) => Color.Parse(hex);

        public static string Normalize(string? name) => name == Ufuk ? Ufuk : Mesale;

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

            var palette = name == Ufuk ? UfukColors : _mesale;
            foreach (var (key, colors) in palette)
                if (res.TryGetResource(key, null, out var value)) Write(value, colors);
            // Fluent'in vurgu renkleri Color değeridir (fırça değil): yeniden konur, DynamicResource ile bağlı olanlar güncellenir
            foreach (var (key, color) in name == Ufuk ? UfukSystem : _mesaleSystem!) res[key] = color;

            bool changed = Current != name;
            Current = name;
            if (changed) Changed?.Invoke();
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
