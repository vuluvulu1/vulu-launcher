using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    internal sealed partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            Themes.Attach(this);   // Ufkun öbür ucu: sayfanın kendi stillerinin üstüne

            // Metin kutusundan çıkınca kaydedilir (Enter de kaydeder; satırın kendi KeyBinding'i)
            AddHandler(LostFocusEvent, (_, e) =>
            {
                if (e.Source is not TextBox box) return;
                switch (box.DataContext)
                {
                    case TextRow row: row.CommitCommand.Execute(null); break;
                    case GameSizeRow size when box.Classes.Contains("commit-size"): size.SaveCustomCommand.Execute(null); break;
                }
            }, RoutingStrategies.Bubble, handledEventsToo: true);

            // Kaydırıcı bırakılınca kaydedilir (sürüklerken her adımda değil)
            AddHandler(PointerReleasedEvent, (_, e) =>
            {
                if (e.Source is Visual v && FindSlider(v) is { DataContext: SliderRow row }) _ = row.CommitAsync();
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

            Scroll.ScrollChanged += (_, _) => MarkCurrentSection();

            KutPolish.AttachSearch(this, KutSearch);
            KutScroll.SizeChanged += (_, e) => FitCards(e.NewSize.Width);
            ApplyLayout();
            // Tema aboneliği yalnızca sayfa ağaçtayken (uzun süre açılmayan sayfa PageHost'tan atılınca bellekte kalmasın)
            AttachedToVisualTree += (_, _) => { Themes.Changed -= ApplyLayout; Themes.Changed += ApplyLayout; ApplyLayout(); };
            DetachedFromVisualTree += (_, _) => Themes.Changed -= ApplyLayout;
            DataContextChanged += (_, _) => BindKut(DataContext as SettingsViewModel);
            AttachedToVisualTree += (_, _) => FillAccount();
        }

        // ── Kütüphane yerleşimi: kategori hapları + seçili kategorinin satırları kart kart + hesap çekmecesi ──
        private SettingsViewModel? _kutVm;

        private void ApplyLayout()
        {
            bool k = Themes.IsKutuphane;
            KutRoot.IsVisible = k;
            ClassicRoot.IsVisible = !k;
            if (k) { FillAccount(); ShowCards(); }
        }

        private void BindKut(SettingsViewModel? vm)
        {
            if (_kutVm == vm) return;
            if (_kutVm != null) { _kutVm.Sections.CollectionChanged -= OnSectionsChanged; _kutVm.PropertyChanged -= OnVmChanged; }
            _kutVm = vm;
            if (vm != null) { vm.Sections.CollectionChanged += OnSectionsChanged; vm.PropertyChanged += OnVmChanged; }
            ShowCards();
        }

        private void OnSectionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            _shownKey = null;   // bölümler yeniden kuruldu; hepsi gelince bir kez gösterilir
            if (_cardsQueued) return;
            _cardsQueued = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { _cardsQueued = false; ShowCards(); }, Avalonia.Threading.DispatcherPriority.Background);
        }

        private bool _cardsQueued;

        private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.Search)) ShowCards();
        }

        /// <summary>Arama boşsa seçili kategorinin satırları; aramada bütün kategorilerin uyan satırları.</summary>
        /// <remarks>
        /// Aramada bütün satırlar bir kez verilir; her tuşta kartlar yeniden kurulmaz, uymayanları satırın IsShown'ı gizler.
        /// </remarks>
        private object? _shownKey;

        private void ShowCards()
        {
            if (_kutVm == null || !Themes.IsKutuphane) return;
            bool searching = !string.IsNullOrWhiteSpace(_kutVm.Search);
            var current = _kutVm.Sections.FirstOrDefault(s => s.IsCurrent) ?? _kutVm.Sections.FirstOrDefault();
            object? key = searching ? "search" : current;
            KutCards.Classes.Set("searching", searching);
            if (Equals(key, _shownKey) && KutCards.ItemsSource != null) return;
            _shownKey = key;
            if (searching)
                foreach (var s in _kutVm.Sections)
                    foreach (var row in s.Rows) row.Category = s.Title.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
            KutCards.ItemsSource = searching ? _kutVm.Sections.SelectMany(s => s.Rows).ToList() : current?.Rows.ToList();
            KutScroll.Offset = default;
        }

        /// <summary>Ayar kartları satırı boşluksuz doldurur (en az 410 px, Java listesi sığsın).</summary>
        private double _fitWidth = -1;
        private void FitCards(double width)
        {
            const double gap = 14, min = 410, max = 640;
            if (width <= 0 || Math.Abs(width - _fitWidth) < 1) return;
            _fitWidth = width;
            double room = width - 1;   // her öğe sağ boşluğuyla (gap) birlikte yer kaplar
            int cols = Math.Max(1, (int)(room / (min + gap)));
            Resources["CardW"] = Math.Min(max, Math.Floor(room / cols - gap));
        }

        private void OnCategoryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: SettingSection section } || _kutVm == null) return;
            foreach (var s in _kutVm.Sections) s.IsCurrent = s == section;
            if (!string.IsNullOrEmpty(_kutVm.Search)) _kutVm.Search = "";
            ShowCards();
        }

        private MainViewModel? Main => TopLevel.GetTopLevel(this)?.DataContext as MainViewModel;

        private void FillAccount()
        {
            if (Main is not { } m) return;
            AccName.Text = m.Username;
            AccInitial.Text = m.UserInitial;
            AccAvatar.Source = m.MyAvatar;
            AccVersion.Text = m.Version;
        }

        private void OnProfileClick(object? sender, RoutedEventArgs e) => Main?.OpenMyProfileCommand.Execute(null);

        private void OnLogoutClick(object? sender, RoutedEventArgs e)
        {
            if (Main is { } m) _ = m.LogoutAsync();
        }

        private static Slider? FindSlider(Visual? visual)
        {
            for (var v = visual; v != null; v = v.GetVisualParent())
                if (v is Slider slider) return slider;
            return null;
        }

        /// <summary>Bölüm menüsünden tıklanan bölüme kaydırır.</summary>
        private void OnNavClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: SettingSection section }) return;
            if (SectionList.ContainerFromItem(section) is not Control container) return;
            if (container.TranslatePoint(new Point(0, 0), Body) is not { } top) return;
            // Tıklanan bölüm seçili kalır (son bölümlere inince sayfa en alta dayanır, üstteki bölüm başka olur)
            _navTarget = section;
            Scroll.Offset = new Vector(0, Math.Max(0, top.Y));
            if (DataContext is SettingsViewModel vm)
                foreach (var s in vm.Sections) s.IsCurrent = s == section;
        }

        // Menüden gidilen bölüm; kullanıcı bir sonraki kaydırışında yeniden hesaplanır
        private SettingSection? _navTarget;

        /// <summary>Kaydırırken üstte görünen bölüm menüde seçili olur.</summary>
        private void MarkCurrentSection()
        {
            if (DataContext is not SettingsViewModel vm) return;
            if (_navTarget != null) { _navTarget = null; return; }
            SettingSection? current = null;
            foreach (var section in vm.Sections)
            {
                if (!section.IsVisible || SectionList.ContainerFromItem(section) is not Control container) continue;
                if (container.TranslatePoint(new Point(0, 0), Body) is not { } top) continue;
                if (current == null || top.Y <= Scroll.Offset.Y + 60) current = section;
            }
            // En alta inildiyse son bölüm seçili olur (kısa son bölüm hiç üste çıkamaz)
            if (Scroll.Offset.Y >= Scroll.Extent.Height - Scroll.Viewport.Height - 2)
                current = vm.Sections.LastOrDefault(s => s.IsVisible) ?? current;
            foreach (var section in vm.Sections) section.IsCurrent = section == current;
        }
    }
}
