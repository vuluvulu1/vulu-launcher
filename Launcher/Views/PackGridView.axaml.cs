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
