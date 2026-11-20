using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    /// <summary>Kütüphane sayfalarının küçük çeviricileri.</summary>
    internal static class KutBrushes
    {
        /// <summary>Resim yüklenince görünür (Opacity 0 → 1; geçişle yumuşakça belirir).</summary>
        public static readonly IValueConverter Shown = new FuncValueConverter<object?, double>(o => o == null ? 0 : 1);

        /// <summary>
        /// Resmi olmayan paketin kapağı: adından türeyen (her açılışta aynı) iki tonlu degrade; harf bunun üstünde durur.
        /// </summary>
        public static readonly IValueConverter CoverFor = new FuncValueConverter<string?, IBrush>(name =>
        {
            (string A, string B)[] pairs =
            {
                ("#5A3A1C", "#2A1A0E"), ("#2F4A44", "#14201D"), ("#4A2F4A", "#1E1420"), ("#3A4A2A", "#182012"),
                ("#5A2F26", "#24130F"), ("#2E3A55", "#121826"), ("#55471F", "#231D0C"), ("#3F3550", "#1A1522"),
            };
            int h = 17;
            foreach (char c in name ?? "") h = h * 31 + c;
            var (a, b) = pairs[(h & 0x7fffffff) % pairs.Length];
            return new LinearGradientBrush
            {
                StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative), EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(a), 0), new GradientStop(Color.Parse(b), 1) },
            };
        });

        /// <summary>true → 1, false → 0 (ör. Kütüphane rayı giriş ekranında görünmesin).</summary>
        public static readonly IValueConverter OneIfTrue = new FuncValueConverter<bool, double>(b => b ? 1 : 0);

        /// <summary>false ise 2 sütun kaplar (ör. sürüm seçicisi yokken bellek seçicisi yanındaki boş yeri de alır).</summary>
        public static readonly IValueConverter SpanUnless = new FuncValueConverter<bool, int>(b => b ? 1 : 2);

        public static readonly IValueConverter ErrorOrDim = new FuncValueConverter<bool, IBrush>(error =>
            error ? (Avalonia.Application.Current?.FindResource("Error") as IBrush ?? Brushes.OrangeRed)
                  : (Avalonia.Application.Current?.FindResource("Dim") as IBrush ?? Brushes.Gray));
    }

    /// <summary>Yeni paket › vulu Packs (PackGridView.axaml).</summary>
    internal sealed partial class PackGridView : UserControl
    {
        public PackGridView() => InitializeComponent();

        /// <summary>Kartın boş yerine tıklanınca ayrıntılar açılır (düğmeler kendi işini yapar).</summary>
        private void OnCardTapped(object? sender, TappedEventArgs e)
        {
            for (var v = e.Source as Avalonia.Visual; v != null && v != sender; v = v.GetVisualParent())
                if (v is Button) return;
            if (sender is Control { DataContext: PackCard card }) card.DetailsCommand.Execute(null);
        }
    }
}
