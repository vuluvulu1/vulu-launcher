using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VuluLauncher.ViewModels
{
    /// <param name="Target">Işıkla gösterilen düğme (Views.Tour.Target ya da "tab:anahtar"); boşsa kart ortada durur.</param>
    /// <param name="Page">Adımdan önce açılan sayfa (ör. Oyna'daki düğmeler için "home").</param>
    /// <param name="Edition">Adımın eklendiği rehber sürümü: rehberi daha önce görenlere yalnızca yeni adımlar gösterilir.</param>
    internal sealed record TourStep(string? Target, string Title, string Text, string? Page = null, int Edition = 3);

    /// <summary>
    /// Tanıtım turu: ekran kararır, her adımda launcher'daki gerçek düğme ışıkla çevrelenir ve yanında kısa bir açıklama
    /// kartı çıkar (Views.TourOverlay). İlk girişte bütün adımlar, rehberi daha önce görmüş olana yeni adımlar çıkınca
    /// yalnızca onlar bir kez gösterilir. Ayarlar → Launcher'dan hepsi yeniden açılabilir.
    /// </summary>
    internal sealed partial class TourGuide : ObservableObject
    {
        /// <summary>Rehberin sürümü: yeni adım eklenince artar (UiState "tourEdition"). 3: düğmeleri gösteren tur.</summary>
        public const int CurrentEdition = 3;

        private static readonly TourStep[] AllSteps =
        {
            new(null, "vulu Launcher'a hoş geldin", "Kısa bir turla launcher'ı gezelim: her adımda ilgili düğme ışıkla gösterilir. İstediğin an Esc ile çıkabilirsin."),
            new("new-pack", "Paket kur", "Buradan vulu Packs'ten ekibin paketlerini, CurseForge ya da Modrinth'ten modpack'leri kurar ya da boş bir paket açarsın. Başka bir launcher'dan gelen paketi pencereye sürüklemen de yeter.", "home"),
            new("play|poster", "Oyna", "Paketini seç ve OYNA'ya bas. Paketin modları, dünyaları, ayarları ve belleği hemen yanında; bir paket açıkken başka bir paketi de açabilirsin.", "home"),
            new("tab:servers", "Sunucular", "Sunucuya \"Bağlan\" deyince doğru paket açılır (gerekirse önce kurulur) ve oyun doğrudan sunucuya girer. Sohbeti ve haritayı buradan izlersin."),
            new("tab:social", "Sosyal", "Arkadaş ekle, uçtan uca şifreli mesajlaş, ekran görüntülerini paylaş. Arkadaşın oyundaysa yanında \"Katıl\" çıkar; paketini de mesajla gönderebilirsin."),
            new("search", "Her şeyi hızlıca bul", "Ctrl+K ile paket, sayfa ya da kişi ararsın. CurseForge ya da Modrinth bağlantısı yapıştırınca o proje doğrudan açılır."),
            new("notifications", "Bildirimler", "Arkadaşlık istekleri, paket güncellemeleri, beğeni ve yorumlar burada toplanır."),
            new("radio", "Radyo", "Launcher kendiliğinden müzik çalmaz; istersen buradan bir istasyon aç. Açılışta çalmasını Ayarlar › Ses ve görünüm'den seçebilirsin."),
            new("profile", "Profilin", "Resmini, kapağını ve pelerinini buradan değiştirir, oynama süreni ve rozetlerini görürsün."),
            new("settings", "Ayarlar", "Tema, dil, bellek ve bildirimler burada. Bu turu da Ayarlar › Launcher'dan yeniden açabilirsin."),
        };

        private readonly TourStep[] _steps;
        private readonly Action<string> _openPage;
        private readonly TaskCompletionSource _done = new();

        /// <param name="sinceEdition">Bu sürümden sonra eklenen adımlar gösterilir (0: hepsi).</param>
        public TourGuide(Action<string> openPage, int sinceEdition = 0)
        {
            _openPage = openPage;
            _steps = AllSteps.Where(s => s.Edition > sinceEdition).ToArray();
            if (_steps.Length == 0) _steps = AllSteps;
            IsWhatsNew = _steps.Length < AllSteps.Length;
            foreach (var _ in _steps) Dots.Add(new Choice("", ""));
            Render();
        }

        public bool IsWhatsNew { get; }
        public Task Completion => _done.Task;
        public ObservableCollection<Choice> Dots { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBack), nameof(NextText), nameof(Counter))]
        private int _step;
        [ObservableProperty] private string _title = "";
        [ObservableProperty] private string _text = "";
        /// <summary>Işıkla gösterilecek düğme; değişince katman yeni hedefe kayar.</summary>
        [ObservableProperty] private string? _target;

        public bool HasBack => Step > 0;
        public string NextText => Step == _steps.Length - 1 ? "Başla" : "İleri";
        public string Counter => $"{Step + 1} / {_steps.Length}";

        private void Render()
        {
            var s = _steps[Step];
            if (s.Page != null) _openPage(s.Page);
            Title = s.Title;
            Text = s.Text;
            Target = s.Target;
            for (int i = 0; i < Dots.Count; i++) Dots[i].IsSelected = i == Step;
        }

        [RelayCommand]
        private void Next()
        {
            if (Step < _steps.Length - 1) { Step++; Render(); }
            else Finish();
        }

        [RelayCommand]
        private void Back()
        {
            if (Step == 0) return;
            Step--;
            Render();
        }

        [RelayCommand] public void Finish() => _done.TrySetResult();
    }
}
