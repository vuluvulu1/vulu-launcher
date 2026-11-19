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

            ApplyLayout();
            Themes.Changed += ApplyLayout;
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

        private void OnSectionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => ShowCards();

        private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.Search)) ShowCards();
        }

        /// <summary>Arama boşsa seçili kategorinin satırları; aramada bütün kategorilerin uyan satırları.</summary>
        private void ShowCards()
        {
            if (_kutVm == null || !Themes.IsKutuphane) return;
            var current = _kutVm.Sections.FirstOrDefault(s => s.IsCurrent) ?? _kutVm.Sections.FirstOrDefault();
            KutCards.ItemsSource = string.IsNullOrWhiteSpace(_kutVm.Search)
                ? current?.Rows.ToList()
                : _kutVm.Sections.SelectMany(s => s.Rows).ToList();
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
