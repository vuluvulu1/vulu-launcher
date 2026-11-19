using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    /// <summary>Kütüphane sayfalarının küçük çeviricileri.</summary>
    internal static class KutBrushes
    {
        public static readonly IValueConverter ErrorOrDim = new FuncValueConverter<bool, IBrush>(error =>
            error ? (Avalonia.Application.Current?.FindResource("Error") as IBrush ?? Brushes.OrangeRed)
                  : (Avalonia.Application.Current?.FindResource("Dim") as IBrush ?? Brushes.Gray));
    }

    /// <summary>
    /// Kütüphane temasının Keşfet sayfası (KutDiscoverView.axaml). vulu paketlerinin ilki öne çıkan büyük kartta, kalanı
    /// ızgarada gösterilir (aynı listeden, görünümde ayrılır).
    /// </summary>
    internal sealed partial class KutDiscoverView : UserControl
    {
        private readonly ObservableCollection<PackCard> _rest = new();
        private PacksViewModel? _packs;

        public KutDiscoverView()
        {
            InitializeComponent();
            Featured.DataContext = null;
            Rest.ItemsSource = _rest;
            DataContextChanged += (_, _) => Bind((DataContext as DiscoverViewModel)?.Packs);
        }

        private void Bind(PacksViewModel? packs)
        {
            if (_packs == packs) return;
            if (_packs != null) _packs.Cards.CollectionChanged -= OnCardsChanged;
            _packs = packs;
            if (packs != null) packs.Cards.CollectionChanged += OnCardsChanged;
            Rebuild();
        }

        private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

        private void Rebuild()
        {
            _rest.Clear();
            var cards = _packs?.Cards.ToList() ?? new List<PackCard>();
            Featured.DataContext = cards.FirstOrDefault();
            Featured.IsVisible = cards.Count > 0;
            foreach (var c in cards.Skip(1)) _rest.Add(c);
            RestTitle.IsVisible = _rest.Count > 0;
        }

        /// <summary>Kartın boş yerine tıklanınca ayrıntılar açılır (düğmeler kendi işini yapar).</summary>
        private void OnCardTapped(object? sender, TappedEventArgs e)
        {
            for (var v = e.Source as Avalonia.Visual; v != null && v != sender; v = v.GetVisualParent())
                if (v is Button) return;
            if (sender is Control { DataContext: PackCard card }) card.DetailsCommand.Execute(null);
        }
    }
}
