using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace VuluLauncher
{
    /// <summary>Arayüzden bağlanabilen tema durumu (ör. IsVisible="{Binding IsKutuphane, Source={x:Static local:ThemeState.Instance}}").</summary>
    internal sealed partial class ThemeState : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public static ThemeState Instance { get; } = new();
        /// <summary>Kütüphane: simge rayı, kapak ızgarası; başlık çubuğunda sekme yok.</summary>
        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isKutuphane;
    }

    /// <summary>
    /// Launcher temaları. İkisi de vulu'nun renklerini (App.axaml'daki fırçalar) kullanır; ayrılan düzenleridir:
    ///   kutuphane: varsayılan; solda simge rayı, Oyna'da paketler kapak ızgarasında, seçilenin ayrıntısı ve OYNA sağdaki
    ///              çekmecede; başlık çubuğunda sekme yok (Views/LibraryView, Views/KutuphaneStyles.axaml)
    ///   mesale:    üstte sekmeler, Oyna'da paketin dünyası tam ekran, solda paket şeridi, altta oyun çubuğu (Views/HomeView)
    /// Temaya özgü stiller pencere tema adıyla aynı sınıfı taşıdığında geçerli olur; ortak yuvarlak görünüm "soft" sınıfıyla.
    /// </summary>
    internal static class Themes
    {
        public const string Mesale = "mesale";
        public const string Kutuphane = "kutuphane";
        private static readonly string[] All = { Mesale, Kutuphane };

        public static string Current { get; private set; } = Kutuphane;
        public static bool IsKutuphane => Current == Kutuphane;
        public static event Action? Changed;

        /// <summary>Köşe yuvarlaklığı: iki temada da yumuşak (Fluent denetimleri: açılır listeler, menüler, ipuçları).</summary>
        private static (double Control, double Overlay) Corners(string name) => (10, 14);

        public static string Normalize(string? name) => All.Contains(name) ? name! : Kutuphane;

        public static void Apply(string? name)
        {
            var app = Application.Current;
            if (app == null) return;
            name = Normalize(name);
            var res = app.Resources;
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
            ThemeState.Instance.IsKutuphane = name == Kutuphane;
            if (changed) Changed?.Invoke();
        }

        private static Styles? _appStyles;

        /// <summary>Temaların stil dosyaları; her biri yalnızca pencere kendi sınıfını taşırken geçerlidir.</summary>
        private static Styles ThemeStyles() => new() { new Views.SoftStyles(), new Views.KutuphaneStyles() };

        /// <summary>
        /// Kendi stilleri olan sayfalar (Oyna) için: sayfanın stilleri uygulamanınkinden önce geldiği için temaya özel
        /// stiller sayfanın kendi listesinin sonuna da eklenir (pencerenin tema sınıfıyla açılıp kapanırlar).
        /// </summary>
        public static void Attach(StyledElement element) => element.Styles.Add(ThemeStyles());

        /// <summary>Pencere açılınca: tema sınıfı verilir (sonradan açılan pencereler için).</summary>
        public static void Mark(Window window) => MarkWith(window, Current);

        private static void MarkWith(Window window, string name)
        {
            foreach (var theme in All) window.Classes.Set(theme, theme == name);
            window.Classes.Set("soft", true);   // ortak yuvarlak görünüm (Views/SoftStyles.axaml)
        }
    }
}
