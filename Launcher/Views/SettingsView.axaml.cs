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
