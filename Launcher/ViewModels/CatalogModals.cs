using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>Yeni paket penceresinin solundaki kaynak satırı.</summary>
    internal sealed partial class SourceItem(string key, string label, string hint, string icon) : ObservableObject
    {
        public string Key { get; } = key;
        public string Label { get; } = label;
        public string Hint { get; } = hint;
        public Geometry Icon { get; } = StreamGeometry.Parse(icon);
        [ObservableProperty] private bool _isSelected;
    }

    /// <summary>
    /// Yeni paket: eskiden Keşfet sekmesinde duran her şey burada. Solda kaynak seçilir; sağda o kaynağın içeriği:
    ///   Boş instance: ad, Minecraft sürümü, yükleyici (eski "Yeni instance" kutusu)
    ///   vulu Packs:   ekibin paylaştığı paketler
    ///   CurseForge / Modrinth: modpack arama ve süzgeçler; kurulan modpack yeni instance olur
    /// Sonuç: boş instance oluşturulduysa kimliği. Paket kurulunca MainViewModel.ShowHomeAsync pencereyi kapatır.
    /// </summary>
    internal sealed partial class NewPackModal : ModalViewModel
    {
        public NewPackModal(MainViewModel main, string? source)
        {
            Main = main;
            Packs = main.Packs;
            Search = new ContentSearchViewModel(main, null);
            // Arama kutusuna yapıştırılan bağlantı başka kaynağınsa (CurseForge/Modrinth) o kaynağa geçilir
            Search.SourceRequested = key => { if (Sources.FirstOrDefault(s => s.Key == key) is { } item) Select(item); };
            Blank = new InstanceEditorModal(null);
            _ = ForwardBlankAsync();
            Sources.Add(new SourceItem("packs", "vulu Packs", "Ekibin hazırladığı paketler", "M4 7.5L12 3l8 4.5v9L12 21l-8-4.5zM4 7.5l8 4.5l8-4.5M12 12v9"));
            Sources.Add(new SourceItem("curseforge", "CurseForge", "Modpack ara", "M11 4.5a6.5 6.5 0 1 0 0 13a6.5 6.5 0 0 0 0-13zM16 16l4.5 4.5"));
            Sources.Add(new SourceItem("modrinth", "Modrinth", "Modpack ara", "M11 4.5a6.5 6.5 0 1 0 0 13a6.5 6.5 0 0 0 0-13zM16 16l4.5 4.5"));
            Sources.Add(new SourceItem("blank", "Boş instance", "Sürümü ve yükleyiciyi sen seç", "M12 5v14M5 12h14"));
            // Bilgisayardaki bir modpack dosyası (pencereye sürükleyip bırakmak da olur)
            Sources.Add(new SourceItem("import", "Dosyadan içe aktar", "Modpack dosyası (.mrpack, .zip)", "M12 4v11M7 10l5 5 5-5M5 19h14"));
            string start = source ?? UiState.Get("newPackSource") ?? "packs";
            if (start == "import") start = "packs";
            Select(Sources.FirstOrDefault(s => s.Key == start) ?? Sources[0]);
        }

        public MainViewModel Main { get; }
        public PacksViewModel Packs { get; }
        public ContentSearchViewModel Search { get; }
        public InstanceEditorModal Blank { get; }
        public ObservableCollection<SourceItem> Sources { get; } = new();
        public override double Width => 1200;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBlank), nameof(IsPacks), nameof(IsSearch))]
        private string _source = "packs";
        public bool IsBlank => Source == "blank";
        public bool IsPacks => Source == "packs";
        public bool IsSearch => Source is "curseforge" or "modrinth";

        [RelayCommand]
        private void Select(SourceItem item)
        {
            // İçe aktarma bir sekme değil: pencere kapanır, dosya seçtirilir, paket kütüphanede belirir
            if (item.Key == "import") { Close(); _ = Main.ImportFileAsync(); return; }
            foreach (var s in Sources) s.IsSelected = s == item;
            Source = item.Key;
            UiState.Set("newPackSource", item.Key);
            if (IsPacks) _ = Packs.LoadAsync();
            else if (IsSearch && (Search.Source != item.Key || !_searchOpened)) { _searchOpened = true; _ = Search.OpenAsync(item.Key); }
        }

        private bool _searchOpened;

        /// <summary>Boş instance formu kendi kutusu gibi kapanır (Oluştur ya da Vazgeç); sonucu bu pencerenin sonucudur.</summary>
        private async Task ForwardBlankAsync() => Close(await Blank.Result);
    }

    /// <summary>
    /// İçerik ekle: seçili kişisel instance'a CurseForge ya da Modrinth'ten mod, kaynak paketi ya da shader. Sonuçlar o
    /// instance'ın sürümü ve yükleyicisiyle süzülür; kurulanlar kartta "Kuruldu" olur, pencere açık kalır.
    /// </summary>
    internal sealed partial class AddContentModal : ModalViewModel
    {
        public AddContentModal(MainViewModel main, InstanceItem target)
        {
            Target = target;
            Search = new ContentSearchViewModel(main, target);
            Search.SourceRequested = key => { if (Sources.FirstOrDefault(s => s.Key == key) is { } choice) Select(choice); };
            Sources.Add(new Choice("curseforge", "CurseForge"));
            Sources.Add(new Choice("modrinth", "Modrinth"));
            string start = UiState.Get("addContentSource") == "modrinth" ? "modrinth" : "curseforge";
            Select(Sources.First(s => s.Key == start));
        }

        public InstanceItem Target { get; }
        public ContentSearchViewModel Search { get; }
        public ObservableCollection<Choice> Sources { get; } = new();
        public override double Width => 1200;
        public string Title => L.F("İçerik ekle: {0}", Target.Name);
        public string Subtitle => L.F("Minecraft {0}{1} ile uyumlu olanlar", Target.McVersion, (Target.Loader != "vanilla" ? " · " + Names.Loader(Target.Loader) : ""));

        [RelayCommand]
        private void Select(Choice choice)
        {
            foreach (var s in Sources) s.IsSelected = s == choice;
            UiState.Set("addContentSource", choice.Key);
            _ = Search.OpenAsync(choice.Key);
        }
    }
}
