using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VuluLauncher.ViewModels
{
    internal sealed record TourStep(string Page, string Title, string Text);

    /// <summary>
    /// İlk açılış rehberi: dört kısa adım; her adımda ilgili üst sekme parlar ve "Göster" o sayfayı açar.
    /// Bir kez gösterilir; Ayarlar → Launcher'dan yeniden açılabilir. Sonuç: "Göster"e basıldıysa sayfanın anahtarı.
    /// </summary>
    internal sealed partial class TourModal : ModalViewModel
    {
        private static readonly TourStep[] Steps =
        {
            new("discover", "Paket kur", "Keşfet'teki vulu Packs, ekibin hazırladığı mod paketleri. \"Kur\"a bas; modlar, ayarlar ve görev kitapları kendiliğinden iner. Kendi modlarını istersen Oyna'daki \"+ Yeni\" ile instance oluştur."),
            new("home", "Oyna", "Soldan paketi seç, sağ alttaki OYNA'ya bas. RAM düğmesiyle belleği ayarlarsın. Alttaki sekmelerde dünyaların, ekran görüntülerin ve modların durur."),
            new("social", "Arkadaş ekle", "Sosyal'de soldaki kutudan birini ara ve \"+ Ekle\"ye bas. Arkadaşlarınla uçtan uca şifreli mesajlaşır, ekran görüntüsü paylaşırsın; oyundalarsa yanlarında \"Katıl\" çıkar."),
            new("servers", "Sunucuya bağlan", "Sunucular'da \"Bağlan\"a bas: doğru paket açılır (gerekirse önce kurulur) ve oyun doğrudan sunucuya girer. Ctrl+K ile her şeyi hızlıca arayabilirsin."),
        };

        private readonly IReadOnlyList<PageTab> _tabs;

        public TourModal(IReadOnlyList<PageTab> tabs)
        {
            _tabs = tabs;
            foreach (var _ in Steps) Dots.Add(new Choice("", ""));
            Render();
        }

        public override double Width => 460;
        public ObservableCollection<Choice> Dots { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBack), nameof(NextText))]
        private int _step;
        [ObservableProperty] private string _title = "";
        [ObservableProperty] private string _text = "";
        [ObservableProperty] private Geometry? _icon;

        public bool HasBack => Step > 0;
        public string NextText => Step == Steps.Length - 1 ? "Başla" : "İleri";

        private void Render()
        {
            var s = Steps[Step];
            Title = s.Title;
            Text = s.Text;
            Icon = _tabs.FirstOrDefault(t => t.Key == s.Page)?.Icon;
            for (int i = 0; i < Dots.Count; i++) Dots[i].IsSelected = i == Step;
            Glow(s.Page);
        }

        /// <summary>Adımın sekmesi parlar; rehber kapanınca hiçbiri.</summary>
        public void Glow(string? page)
        {
            foreach (var tab in _tabs) tab.IsGlowing = tab.Key == page;
        }

        [RelayCommand]
        private void Next()
        {
            if (Step < Steps.Length - 1) { Step++; Render(); }
            else Close(null);
        }

        [RelayCommand]
        private void Back()
        {
            if (Step == 0) return;
            Step--;
            Render();
        }

        [RelayCommand] private void Show() => Close(Steps[Step].Page);
    }
}
