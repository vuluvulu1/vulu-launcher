using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VuluLauncher.ViewModels
{
    /// <param name="Page">Parlayan üst sekme ("Göster" onu açar); boşsa sekme yok.</param>
    /// <param name="Edition">Adımın eklendiği rehber sürümü: rehberi daha önce görenlere yalnızca yeni adımlar gösterilir.</param>
    /// <param name="IconData">Sekmesi olmayan adımın simgesi (SVG yolu).</param>
    internal sealed record TourStep(string Page, string Title, string Text, int Edition = 1, string? IconData = null);

    /// <summary>
    /// Tanıtım rehberi: kısa adımlar; her adımda ilgili üst sekme parlar ve "Göster" o sayfayı açar. İlk girişte bütün
    /// adımlar, rehberi daha önce görmüş olana yeni özellikler çıkınca yalnızca yeni adımlar bir kez gösterilir.
    /// Ayarlar → Launcher'dan hepsi yeniden açılabilir. Sonuç: "Göster"e basıldıysa sayfanın anahtarı.
    /// </summary>
    internal sealed partial class TourModal : ModalViewModel
    {
        /// <summary>Rehberin sürümü: yeni adım eklenince artar (UiState "tourEdition").</summary>
        public const int CurrentEdition = 2;

        private static readonly TourStep[] AllSteps =
        {
            new("home", "Paket kur", "\"Yeni paket\" ile vulu Packs'ten ekibin paketlerini, CurseForge ya da Modrinth'ten modpack'leri kurar ya da boş bir paket açarsın. Başka bir launcher'dan gelen paketi (.mrpack, CurseForge zip'i, Prism) pencereye sürükleyip bırakman yeter."),
            new("home", "Oyna", "Paketini seç ve OYNA'ya bas; belleği yanındaki seçiciden ayarlarsın. Dünya listesindeki \"Oyna\" oyunu doğrudan o dünyada açar. Bir paket açıkken başka bir paketi de açabilirsin."),
            new("home", "Paketin sayfası", "Paketin kapağına (öbür temalarda ⋯ menüsüne) tıkla: modlar, dünyalar, ekran görüntüleri, konsol ve ayarlar orada. Mod güncellemesinin değişiklik notlarını görür, beğenmezsen \"Geri al\" ile eski sürüme dönersin. Java, JVM argümanları ve pencere boyutu her paket için ayrı seçilebilir.",
                2, "M4 5h16v14H4zM4 9h16M9 9v10"),
            new("social", "Arkadaş ekle", "Sosyal'deki arama kutusundan birini bul ve \"+ Ekle\"ye bas. Arkadaşlarınla uçtan uca şifreli mesajlaşır, resim ve ekran görüntüsü paylaşırsın; oyundalarsa yanlarında \"Katıl\" çıkar."),
            new("social", "Paketini gönder", "Kendi paketini bir arkadaşına gönder: paketin ayarlarında \"Arkadaşına gönder\" de, mesajla giden karttan tek tıkla kursun. Oyun çökerse günlüğü de aynı yoldan ya da bağlantı olarak paylaşabilirsin.",
                2),
            new("servers", "Sunucuya bağlan", "Sunucular'da \"Bağlan\"a bas: doğru paket açılır (gerekirse önce kurulur) ve oyun doğrudan sunucuya girer. Sunucunun sohbetini ve haritasını launcher'dan izleyebilirsin."),
            new("", "Her şeyi hızlıca bul", "Ctrl+K ile paket, sayfa ya da kişi ararsın. Keşfet'e CurseForge ya da Modrinth bağlantısı yapıştırınca o proje doğrudan açılır.",
                2, "M11 4a7 7 0 1 0 0 14a7 7 0 0 0 0-14zM20 20l-4-4"),
        };

        private readonly IReadOnlyList<PageTab> _tabs;
        private readonly TourStep[] Steps;

        /// <param name="sinceEdition">Bu sürümden sonra eklenen adımlar gösterilir (0: hepsi).</param>
        public TourModal(IReadOnlyList<PageTab> tabs, int sinceEdition = 0)
        {
            _tabs = tabs;
            Steps = AllSteps.Where(s => s.Edition > sinceEdition).ToArray();
            if (Steps.Length == 0) Steps = AllSteps;
            Heading = sinceEdition > 0 ? "VULU'DA YENİ" : "VULU LAUNCHER'A HOŞ GELDİN";
            foreach (var _ in Steps) Dots.Add(new Choice("", ""));
            Render();
        }

        public string Heading { get; }
        [ObservableProperty] private bool _canShow;

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
            Icon = s.IconData != null ? Geometry.Parse(s.IconData) : _tabs.FirstOrDefault(t => t.Key == s.Page)?.Icon;
            CanShow = s.Page.Length > 0;
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

        [RelayCommand] private void Show() { if (Steps[Step].Page.Length > 0) Close(Steps[Step].Page); }
    }
}
