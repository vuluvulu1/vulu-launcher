using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>Etiket (Minecraft 1.21.1, Fabric, 12 mod…).</summary>
    internal sealed record Tag(string Text, bool Accent = false);

    /// <summary>Yan yana seçeneklerden biri (kaynak, tür).</summary>
    internal sealed partial class Choice(string key, string label) : ObservableObject
    {
        public string Key { get; } = key;
        public string Label { get; } = label;
        [ObservableProperty] private bool _isSelected;
    }

    /// <summary>Açılır listedeki seçenek.</summary>
    internal sealed record Option(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    internal static class Names
    {
        public static readonly Dictionary<string, string> Loaders = new()
        {
            ["fabric"] = "Fabric", ["forge"] = "Forge", ["neoforge"] = "NeoForge", ["quilt"] = "Quilt", ["vanilla"] = "Vanilla",
        };

        public static string Loader(string? key) => key != null && Loaders.TryGetValue(key, out var name) ? name : key ?? "";

        /// <summary>Kategori adları İngilizce gelir; bilinenler çevrilir.</summary>
        public static readonly Dictionary<string, string> Categories = new()
        {
            ["adventure"] = "Macera", ["cursed"] = "Tuhaf", ["decoration"] = "Dekorasyon", ["economy"] = "Ekonomi", ["equipment"] = "Ekipman",
            ["food"] = "Yemek", ["game-mechanics"] = "Oyun mekanikleri", ["library"] = "Kütüphane", ["magic"] = "Büyü", ["management"] = "Yönetim",
            ["minigame"] = "Mini oyun", ["mobs"] = "Yaratıklar", ["optimization"] = "Optimizasyon", ["social"] = "Sosyal", ["storage"] = "Depolama",
            ["technology"] = "Teknoloji", ["transportation"] = "Ulaşım", ["utility"] = "Araçlar", ["worldgen"] = "Dünya üretimi",
            ["challenging"] = "Zorlu", ["combat"] = "Savaş", ["kitchen-sink"] = "Her şeyden biraz", ["lightweight"] = "Hafif",
            ["multiplayer"] = "Çok oyunculu", ["quests"] = "Görevler", ["modded"] = "Modlu", ["realistic"] = "Gerçekçi", ["simplistic"] = "Sade",
            ["themed"] = "Temalı", ["tweaks"] = "İnce ayar", ["vanilla-like"] = "Vanilla benzeri", ["audio"] = "Ses", ["blocks"] = "Bloklar",
            ["core-shaders"] = "Çekirdek shader", ["entities"] = "Varlıklar", ["environment"] = "Ortam", ["fonts"] = "Yazı tipi", ["gui"] = "Arayüz",
            ["items"] = "Eşyalar", ["locale"] = "Dil", ["models"] = "Modeller", ["cartoon"] = "Çizgi film", ["fantasy"] = "Fantastik",
            ["semi-realistic"] = "Yarı gerçekçi", ["atmosphere"] = "Atmosfer", ["bloom"] = "Parlama", ["colored-lighting"] = "Renkli ışık",
            ["foliage"] = "Bitki örtüsü", ["path-tracing"] = "Path tracing", ["pbr"] = "PBR", ["reflections"] = "Yansımalar", ["shadows"] = "Gölgeler",
            ["high"] = "Yüksek", ["medium"] = "Orta", ["low"] = "Düşük", ["potato"] = "Çok düşük", ["screenshot"] = "Ekran görüntüsü",
        };

        public static string Category(string name) =>
            Categories.TryGetValue(name, out var tr) ? tr : name.Length > 0 ? char.ToUpper(name[0], CultureInfo.InvariantCulture) + name[1..] : name;

        public static string Count(double n) => n >= 1e6 ? (n / 1e6).ToString("0.#", CultureInfo.GetCultureInfo("tr-TR")) + " Mn"
            : n >= 1e3 ? (n / 1e3).ToString("0.#", CultureInfo.GetCultureInfo("tr-TR")) + " B" : Math.Round(n).ToString(CultureInfo.InvariantCulture);

        public static string Ago(string? iso)
        {
            if (iso == null || !DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)) return "";
            int days = (int)(DateTime.UtcNow - at.ToUniversalTime()).TotalDays;
            return days < 1 ? "bugün" : days < 30 ? $"{days} gün önce" : days < 365 ? $"{days / 30} ay önce" : $"{days / 365} yıl önce";
        }

        /// <summary>Desteklenen sürümler: "1.21.1" ya da "1.20.1 – 1.21.4".</summary>
        public static string McRange(IReadOnlyList<string> versions) =>
            versions.Count == 0 ? "" : versions.Count == 1 ? versions[0] : $"{versions[0]} – {versions[^1]}";
    }

    /// <summary>
    /// Keşfet: vulu Packs (ekibin paylaştığı paketler), CurseForge ve Modrinth. Son açılan kaynak hatırlanır.
    /// </summary>
    internal sealed partial class DiscoverViewModel : ObservableObject
    {
        public DiscoverViewModel(MainViewModel main)
        {
            Main = main;
            Packs = new PacksViewModel(main);
            Content = new ContentSearchViewModel(main);
            foreach (var (key, label) in new[] { ("packs", "vulu Packs"), ("curseforge", "CurseForge"), ("modrinth", "Modrinth") })
                Sources.Add(new Choice(key, label));
            _source = UiState.Get("discoverSource") is "curseforge" or "modrinth" ? UiState.Get("discoverSource")! : "packs";
            foreach (var s in Sources) s.IsSelected = s.Key == _source;
        }

        public MainViewModel Main { get; }
        public PacksViewModel Packs { get; }
        public ContentSearchViewModel Content { get; }
        public ObservableCollection<Choice> Sources { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPacks))]
        private string _source;
        public bool IsPacks => Source == "packs";

        /// <summary>Sekme açıldı: seçili kaynak (yeniden) yüklenir.</summary>
        public Task OpenAsync() => IsPacks ? Packs.LoadAsync() : Content.OpenAsync(Source);

        [RelayCommand]
        private Task SelectSource(Choice choice)
        {
            SetSource(choice.Key);
            return OpenAsync();
        }

        /// <summary>Kaynağı seçer (yüklemeden; sekme açılınca yüklenir).</summary>
        public void SetSource(string key)
        {
            Source = key;
            foreach (var s in Sources) s.IsSelected = s.Key == key;
            UiState.Set("discoverSource", key);
        }
    }

    // ── vulu Packs ───────────────────────────────────────────────────────

    /// <summary>Paketin bir revizyonundaki değişiklikler.</summary>
    internal sealed record ChangeEntry(string Head, string Meta, string? Note, IReadOnlyList<string> Added, IReadOnlyList<string> Updated,
        IReadOnlyList<string> Removed, bool FilesChanged, string? Empty)
    {
        public bool HasNote => !string.IsNullOrEmpty(Note);
        public bool HasEmpty => Empty != null;

        public static ChangeEntry From(JsonElement c)
        {
            static List<string> Cap(IEnumerable<string> items)
            {
                var list = items.ToList();
                return list.Count > 40 ? list.Take(40).Append($"…ve {list.Count - 40} tane daha").ToList() : list;
            }
            int revision = c.Int("revision");
            var added = Cap(c.Items("added").Select(a => "+  " + a.GetString()));
            var updated = Cap(c.Items("updated").Select(u => $"↑  {u.Str("title")}  {u.Str("from")} → {u.Str("to")}"));
            var removed = Cap(c.Items("removed").Select(r => "−  " + r.GetString()));
            string? note = c.Str("note");
            bool files = c.Bool("filesChanged");
            bool empty = string.IsNullOrEmpty(note) && added.Count == 0 && updated.Count == 0 && removed.Count == 0 && !files;
            string date = c.Str("at") is { } at && DateTime.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
                ? d.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR")) : "";
            return new ChangeEntry($"rev {revision}", string.Join(" · ", new[] { date, c.Str("by") }.Where(s => !string.IsNullOrEmpty(s))),
                note, added, updated, removed, files, empty ? (revision == 1 ? "İlk sürüm." : "İçerik değişmedi.") : null);
        }
    }

    /// <summary>Keşfet'teki paket kartı.</summary>
    internal sealed partial class PackCard : ObservableObject
    {
        private readonly PacksViewModel _owner;

        public PackCard(PacksViewModel owner, JsonElement p)
        {
            _owner = owner;
            Id = p.Str("id") ?? "";
            Name = p.Str("name") ?? "";
            Description = p.Str("description") ?? "";
            Revision = p.Int("revision");
            var installed = p.Get("installed");
            InstalledId = installed.ValueKind == JsonValueKind.Object ? installed.Str("instanceId") : null;
            InstalledRevision = installed.Int("revision");
            string loader = p.Str("loader") == "vanilla" ? "Vanilla" : $"{Names.Loader(p.Str("loader"))} {p.Str("loaderVersion")}".Trim();
            Meta = string.Join(" · ", new[] { p.Str("author"), Names.Ago(p.Str("updatedAt")) }.Where(s => !string.IsNullOrEmpty(s)));
            DetailMeta = string.Join(" · ", new[] { p.Str("author") is { Length: > 0 } a ? "Paylaşan: " + a : null, Names.Ago(p.Str("updatedAt")), $"rev {Revision}" }
                .Where(s => !string.IsNullOrEmpty(s)));

            int mods = p.Int("modCount");
            Tags.Add(new Tag("Minecraft " + p.Str("mcVersion")));
            Tags.Add(new Tag(loader));
            if (mods > 0) Tags.Add(new Tag(mods + " mod"));

            // Ayrıntıdaki bütün etiketler: içerik türleri, önerilen RAM, boyut
            foreach (var t in Tags) AllTags.Add(t);
            var includes = p.Items("includes").Select(i => (i.GetString() ?? "").ToLowerInvariant()).ToHashSet();
            if (p.Int("resourcePackCount") is > 0 and var rp) AllTags.Add(new Tag(rp + " kaynak paketi"));
            else if (includes.Contains("resourcepacks")) AllTags.Add(new Tag("Kaynak paketleri"));
            if (p.Int("shaderCount") is > 0 and var sh) AllTags.Add(new Tag(sh + " shader"));
            else if (includes.Contains("shaderpacks")) AllTags.Add(new Tag("Shader"));
            if (includes.Contains("kubejs")) AllTags.Add(new Tag("KubeJS"));
            if (includes.Contains("config") || includes.Contains("defaultconfigs")) AllTags.Add(new Tag("Config"));
            if (p.Int("recommendedRamGb") is > 0 and var ram) AllTags.Add(new Tag(ram + " GB RAM"));
            AllTags.Add(new Tag(HomeViewModel.FormatSize(p.Num("sizeMb"))));

            Changelog = p.Items("changelog").Select(ChangeEntry.From).ToList();
            _ = LoadCoverAsync(p.Str("coverUrl"));
        }

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public bool HasDescription => Description.Length > 0;
        public string Meta { get; }
        public string DetailMeta { get; }
        public int Revision { get; }
        public string? InstalledId { get; }
        public int InstalledRevision { get; }
        public bool IsInstalled => InstalledId != null;
        public bool IsOutdated => IsInstalled && InstalledRevision < Revision;
        public List<Tag> Tags { get; } = new();
        public List<Tag> AllTags { get; } = new();
        public List<ChangeEntry> Changelog { get; }
        public bool IsAdmin => _owner.IsAdmin;

        /// <summary>Ana düğme: Kur / Güncelle / Kurulu · Aç.</summary>
        public string ActionText => Busy ? (IsOutdated ? "Güncelleniyor…" : "Kuruluyor…") : !IsInstalled ? "Kur" : IsOutdated ? "Güncelle" : "Kurulu · Aç";
        public bool ActionIsPrimary => !IsInstalled || IsOutdated;

        [ObservableProperty] private Bitmap? _cover;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActionText))]
        private bool _busy;

        private async Task LoadCoverAsync(string? url) => Cover = await Images.LoadAsync(url, 640);

        [RelayCommand] private Task Act() => _owner.ActAsync(this);
        [RelayCommand] private Task Details() => _owner.ShowDetailsAsync(this);
    }

    internal sealed partial class PacksViewModel(MainViewModel main) : ObservableObject
    {
        private readonly CoreClient _core = CoreClient.Instance;
        private bool _loading;

        public ObservableCollection<PackCard> Cards { get; } = new();
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private bool _statusIsError;
        [ObservableProperty] private bool _isEmpty;
        [ObservableProperty] private bool _isAdmin;

        [RelayCommand]
        public async Task LoadAsync()
        {
            if (_loading) return;
            _loading = true;
            Status = "Yükleniyor…";
            StatusIsError = false;
            try
            {
                var result = await _core.Call("packs.list");
                IsAdmin = result.Bool("isAdmin");
                Cards.Clear();
                foreach (var p in result.Items("packs")) Cards.Add(new PackCard(this, p));
                IsEmpty = Cards.Count == 0;
                Status = Cards.Count > 0 ? $"{Cards.Count} paket" : "";
            }
            catch (Exception ex)
            {
                Status = "Paketler alınamadı: " + ex.Message;
                StatusIsError = true;
            }
            finally { _loading = false; }
        }

        /// <summary>Oyna sayfasından "Paketi güncelle": paket listesi alınıp değişikliklerle birlikte güncellenir.</summary>
        public async Task UpdateByIdAsync(string packId)
        {
            await LoadAsync();
            if (Cards.FirstOrDefault(c => c.Id == packId) is { IsOutdated: true } card) await ActAsync(card, stayOnPage: false);
        }

        public Task ActAsync(PackCard card) => ActAsync(card, stayOnPage: true);

        private async Task ActAsync(PackCard card, bool stayOnPage)
        {
            if (card.IsInstalled && !card.IsOutdated) { await main.ShowHomeAsync(card.InstalledId); return; }
            if (card.IsOutdated)
            {
                var changes = card.Changelog.Where(c => int.Parse(c.Head[4..], CultureInfo.InvariantCulture) > card.InstalledRevision).ToList();
                if (await main.ShowAsync(new PackUpdateModal(card, changes)) is not true) return;
            }
            card.Busy = true;
            try
            {
                var result = await _core.Call(card.IsOutdated ? "packs.update" : "packs.install", new { id = card.Id });
                await main.ShowHomeAsync(result.Str("id"));
            }
            catch (Exception ex)
            {
                await main.AlertAsync(card.IsOutdated ? "Güncellenemedi" : "Kurulamadı", ex.Message);
            }
            finally { card.Busy = false; }
            if (stayOnPage) await LoadAsync();
        }

        public async Task ShowDetailsAsync(PackCard card)
        {
            switch (await main.ShowAsync(new PackDetailsModal(card)))
            {
                case "act": await ActAsync(card); break;
                case "delete": await DeleteAsync(card); break;
            }
        }

        private async Task DeleteAsync(PackCard card)
        {
            if (!await main.ConfirmAsync("Paketi kaldır",
                    $"'{card.Name}' vulu Packs'ten kaldırılsın mı?\nKuranların instance'ları silinmez ama artık güncelleme alamazlar.", "Kaldır", danger: true))
                return;
            try { await _core.Call("packs.delete", new { id = card.Id }); }
            catch (Exception ex) { await main.AlertAsync("Kaldırılamadı", ex.Message); }
            await LoadAsync();
        }
    }

    // ── CurseForge / Modrinth ────────────────────────────────────────────

    /// <summary>Arama sonucundaki proje.</summary>
    internal sealed partial class ContentHit : ObservableObject
    {
        private readonly ContentSearchViewModel _owner;

        public ContentHit(ContentSearchViewModel owner, JsonElement h, bool curseForge)
        {
            _owner = owner;
            Id = h.Str("id") ?? "";
            Slug = h.Str("slug") ?? "";
            Type = h.Str("type") ?? "mod";
            Title = h.Str("title") ?? "";
            Author = h.Str("author") ?? "";
            Description = h.Str("description") ?? "";
            PageUrl = h.Str("pageUrl");
            ManualOnly = h.Bool("manualOnly");
            GameVersions = h.Items("gameVersions").Select(v => v.GetString() ?? "").ToList();
            Loaders = h.Items("loaders").Select(v => v.GetString() ?? "").ToList();
            Installed = h.Bool("installed");

            var meta = new List<string> { "↓ " + Names.Count(h.Num("downloads")) };
            if (h.Num("follows") > 0) meta.Add((curseForge ? "👍 " : "♥ ") + Names.Count(h.Num("follows")));
            if (Names.Ago(h.Str("updated")) is { Length: > 0 } ago) meta.Add("Güncelleme: " + ago);
            Meta = string.Join("   ", meta);

            bool isPack = Type == "modpack";
            string mc = Names.McRange(isPack ? GameVersions.TakeLast(1).ToList() : GameVersions);
            if (mc.Length > 0) Tags.Add(new Tag(mc, true));
            foreach (var c in h.Items("categories")) Tags.Add(new Tag(Names.Category(c.GetString() ?? "")));
            if (Type == "mod" || isPack) foreach (var l in Loaders) Tags.Add(new Tag(Names.Loader(l)));

            _buttonText = isPack ? "Instance olarak kur" : Installed ? "Güncelle" : "Kur";
            _isPrimary = !Installed || isPack;
            _ = LoadIconAsync(h.Str("iconUrl"));
        }

        public string Id { get; }
        public string Slug { get; }
        public string Type { get; }
        public string Title { get; }
        public string Author { get; }
        public string Description { get; }
        public string? PageUrl { get; }
        public bool ManualOnly { get; }
        public List<string> GameVersions { get; }
        public List<string> Loaders { get; }
        public bool Installed { get; }
        public string Meta { get; }
        public List<Tag> Tags { get; } = new();

        [ObservableProperty] private Bitmap? _icon;
        [ObservableProperty] private string _buttonText;
        [ObservableProperty] private bool _isPrimary;
        [ObservableProperty] private bool _canInstall = true;

        private async Task LoadIconAsync(string? url) => Icon = await Images.LoadAsync(url, 128);

        /// <summary>Instance'ta zaten var (paketle ya da elle gelmiş): yeniden kurulursa iki kopya olurdu.</summary>
        public void MarkPresent()
        {
            if (Installed || Type == "modpack") return;
            ButtonText = "Kurulu ✓";
            IsPrimary = false;
            CanInstall = false;
        }

        [RelayCommand] private Task Install() => _owner.InstallAsync(this, "");
        [RelayCommand] private Task PickVersion() => _owner.PickVersionAsync(this);
        [RelayCommand] private Task OpenPage() => _owner.OpenPageAsync(this);
    }

    /// <summary>Filtre grubundaki kategori.</summary>
    internal sealed partial class CategoryChip(string name) : ObservableObject
    {
        public string Name { get; } = name;
        public string Label { get; } = Names.Category(name);
        [ObservableProperty] private bool _isSelected;
    }

    internal sealed record CategoryGroup(string Title, List<CategoryChip> Chips);

    internal sealed partial class ContentSearchViewModel : ObservableObject
    {
        private static readonly (string Key, string Label)[] TypeList =
            { ("mod", "Modlar"), ("modpack", "Modpackler"), ("resourcepack", "Kaynak paketleri"), ("shader", "Shaderlar") };
        private static readonly Dictionary<string, string> Headers = new()
        {
            ["categories"] = "Kategoriler", ["features"] = "Özellikler", ["resolutions"] = "Çözünürlük", ["performance impact"] = "Performans etkisi",
        };

        private readonly MainViewModel _main;
        private readonly CoreClient _core = CoreClient.Instance;
        // Filtre seçenekleri kaynak ve tür başına bir kez alınır; seçili kategoriler de kaynak ve tür başına tutulur
        private readonly Dictionary<string, JsonElement> _filterData = new();
        private readonly Dictionary<string, HashSet<string>> _selected = new();
        private readonly Dictionary<string, Task<JsonElement>> _installedIndex = new();
        private DispatcherTimer? _debounce;
        private int _requestId, _offset, _total;
        private bool _suppress, _targetPicked;

        public ContentSearchViewModel(MainViewModel main)
        {
            _main = main;
            foreach (var (key, label) in TypeList) Types.Add(new Choice(key, label));
            Types[0].IsSelected = true;
            foreach (var (key, label) in new[] { ("", "Otomatik"), ("relevance", "İlgi düzeyi"), ("downloads", "İndirme sayısı"), ("follows", "Takipçi sayısı"), ("newest", "En yeni"), ("updated", "Son güncellenen") })
                Sorts.Add(new Option(key, label));
            _sort = Sorts[0];
        }

        public ObservableCollection<Choice> Types { get; } = new();
        public ObservableCollection<Option> Sorts { get; } = new();
        public ObservableCollection<Option> Targets { get; } = new();
        public ObservableCollection<Option> Versions { get; } = new();
        public ObservableCollection<Option> LoaderOptions { get; } = new();
        public ObservableCollection<CategoryGroup> Groups { get; } = new();
        public ObservableCollection<ContentHit> Hits { get; } = new();

        [ObservableProperty] private string _source = "curseforge";
        [ObservableProperty] private string _type = "mod";
        [ObservableProperty] private string _query = "";
        [ObservableProperty] private Option _sort;
        [ObservableProperty] private Option? _target;
        [ObservableProperty] private Option? _version;
        [ObservableProperty] private Option? _loader;
        [ObservableProperty] private bool _openSource;
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private bool _hasMore;
        [ObservableProperty] private bool _filtersLoading;
        [ObservableProperty] private string _clearText = "Temizle";
        [ObservableProperty] private bool _canClear;

        public bool IsCurseForge => Source == "curseforge";
        public bool IsPack => Type == "modpack";
        public string SourceLabel => IsCurseForge ? "CurseForge" : "Modrinth";
        public string SearchHint => $"{SourceLabel}'da ara…";
        /// <summary>Instance seçilmediyse (ya da modpack aranıyorsa) sürüm ve yükleyici filtresi gösterilir.</summary>
        public bool FreeBrowse => IsPack || string.IsNullOrEmpty(Target?.Key);
        public bool ShowTarget => !IsPack;
        public bool ShowLoader => FreeBrowse && (IsPack || Type == "mod");

        private string FilterKey => Source + ":" + Type;
        private HashSet<string> SelectedCategories => _selected.TryGetValue(FilterKey, out var set) ? set : _selected[FilterKey] = new();
        private string Command(string name) => (IsCurseForge ? "cf." : "modrinth.") + name;
        private IEnumerable<InstanceItem> Customs => _main.Home.Instances.Where(i => i.Kind == "custom");

        partial void OnQueryChanged(string value)
        {
            _debounce?.Stop();
            _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(350), DispatcherPriority.Normal, (_, _) => { _debounce?.Stop(); _ = SearchAsync(false); });
            _debounce.Start();
        }

        partial void OnSortChanged(Option value) { if (!_suppress) _ = SearchAsync(false); }
        partial void OnVersionChanged(Option? value) { if (!_suppress) _ = SearchAsync(false); }
        partial void OnLoaderChanged(Option? value) { if (!_suppress) _ = SearchAsync(false); }
        partial void OnOpenSourceChanged(bool value) { if (!_suppress) { UpdateClear(); _ = SearchAsync(false); } }

        private string? _presetTarget;

        /// <summary>Oyna'daki "Mod ekle": Keşfet bu instance hedef seçili açılır, modlar listelenir.</summary>
        public void PresetTarget(string instanceId)
        {
            _presetTarget = instanceId;
            _targetPicked = true;
            if (Type == "modpack") SelectTypeCommand.Execute(Types[0]);
        }

        partial void OnTargetChanged(Option? value)
        {
            if (_suppress) return;
            _targetPicked = true;
            NotifyLayout();
            _ = SearchAsync(false);
        }

        private void NotifyLayout()
        {
            OnPropertyChanged(nameof(FreeBrowse));
            OnPropertyChanged(nameof(ShowTarget));
            OnPropertyChanged(nameof(ShowLoader));
            OnPropertyChanged(nameof(IsPack));
        }

        /// <summary>Keşfet'te CurseForge ya da Modrinth seçildi.</summary>
        public async Task OpenAsync(string source)
        {
            Source = source;
            OnPropertyChanged(nameof(IsCurseForge));
            OnPropertyChanged(nameof(SearchHint));
            Sorts[3] = new Option("follows", IsCurseForge ? "Beğeni sayısı" : "Takipçi sayısı");

            _suppress = true;
            // Hedef seçenekleri: kişisel instance'lar. Hiç seçilmediyse Oyna'da seçili kişisel instance önerilir.
            string? keep = _presetTarget ?? Target?.Key;
            _presetTarget = null;
            if (!_targetPicked && _main.Home.Selected is { Kind: "custom" } selected) keep = selected.Id;
            Targets.Clear();
            Targets.Add(new Option("", Customs.Any() ? "Sonra seçerim (hepsini göster)" : "Henüz instance yok"));
            foreach (var i in Customs) Targets.Add(new Option(i.Id, $"{i.Name} ({i.Subtitle})"));
            Target = Targets.FirstOrDefault(t => t.Key == keep) ?? Targets[0];
            Sort = Sorts.FirstOrDefault(s => s.Key == Sort.Key) ?? Sorts[0];
            _suppress = false;
            NotifyLayout();

            RenderFilters();
            _ = SearchAsync(false);
            await LoadFiltersAsync();
        }

        [RelayCommand]
        private async Task SelectType(Choice choice)
        {
            foreach (var t in Types) t.IsSelected = t == choice;
            Type = choice.Key;
            NotifyLayout();
            RenderFilters();
            _ = SearchAsync(false);
            await LoadFiltersAsync();
        }

        private async Task LoadFiltersAsync()
        {
            string key = FilterKey;
            if (_filterData.ContainsKey(key)) return;
            FiltersLoading = true;
            try
            {
                _filterData[key] = await _core.Call(Command("filters"), new { type = Type });
                if (key == FilterKey) RenderFilters();
            }
            catch { /* filtreler olmadan da arama çalışır */ }
            finally { FiltersLoading = false; }
        }

        private void RenderFilters()
        {
            Groups.Clear();
            if (!_filterData.TryGetValue(FilterKey, out var data)) { UpdateClear(); return; }

            _suppress = true;
            string? version = Version?.Key, loader = Loader?.Key;
            Versions.Clear();
            Versions.Add(new Option("", "Tüm sürümler"));
            foreach (var v in data.Items("minecraftVersions")) Versions.Add(new Option(v.GetString() ?? "", v.GetString() ?? ""));
            LoaderOptions.Clear();
            LoaderOptions.Add(new Option("", "Tümü"));
            foreach (var l in data.Items("loaders")) LoaderOptions.Add(new Option(l.GetString() ?? "", Names.Loader(l.GetString())));
            Version = Versions.FirstOrDefault(v => v.Key == version) ?? Versions[0];
            Loader = LoaderOptions.FirstOrDefault(l => l.Key == loader) ?? LoaderOptions[0];
            _suppress = false;

            var selected = SelectedCategories;
            foreach (var group in data.Items("categories"))
            {
                string header = group.Str("header") ?? "";
                var chips = group.Items("names").Select(n => new CategoryChip(n.GetString() ?? "") { IsSelected = selected.Contains(n.GetString() ?? "") }).ToList();
                Groups.Add(new CategoryGroup(Headers.TryGetValue(header, out var h) ? h : header, chips));
            }
            UpdateClear();
        }

        [RelayCommand]
        private void ToggleCategory(CategoryChip chip)
        {
            var selected = SelectedCategories;
            chip.IsSelected = !chip.IsSelected;
            if (chip.IsSelected) selected.Add(chip.Name); else selected.Remove(chip.Name);
            UpdateClear();
            _ = SearchAsync(false);
        }

        [RelayCommand]
        private void ClearFilters()
        {
            SelectedCategories.Clear();
            foreach (var chip in Groups.SelectMany(g => g.Chips)) chip.IsSelected = false;
            _suppress = true;
            OpenSource = false;
            _suppress = false;
            UpdateClear();
            _ = SearchAsync(false);
        }

        private void UpdateClear()
        {
            int active = SelectedCategories.Count + (OpenSource ? 1 : 0);
            CanClear = active > 0;
            ClearText = active > 0 ? $"Temizle ({active})" : "Temizle";
        }

        [RelayCommand] private Task More() => SearchAsync(true);

        private async Task SearchAsync(bool append)
        {
            int requestId = ++_requestId;
            if (!append) { _offset = 0; Status = "Aranıyor..."; }
            HasMore = false;
            string targetId = IsPack ? "" : Target?.Key ?? "";

            JsonElement result;
            try
            {
                result = await _core.Call(Command("search"), new
                {
                    query = Query.Trim(),
                    type = Type,
                    instanceId = targetId,
                    mcVersion = FreeBrowse ? Version?.Key ?? "" : "",
                    loader = ShowLoader ? Loader?.Key ?? "" : "",
                    sort = Sort.Key,
                    categories = SelectedCategories.ToList(),
                    openSource = !IsCurseForge && OpenSource,
                    offset = _offset,
                });
            }
            catch (Exception ex)
            {
                if (requestId == _requestId) Status = "Arama başarısız: " + ex.Message;
                return;
            }
            if (requestId != _requestId) return;   // daha yeni bir arama başladı

            _total = result.Int("total");
            if (!append) Hits.Clear();
            int added = 0;
            foreach (var h in result.Items("hits")) { Hits.Add(new ContentHit(this, h, IsCurseForge)); added++; }
            _offset += added;
            HasMore = _offset < _total && added > 0;

            // Paketin getirdiği ya da elle konan modlar da "kurulu" görünsün (dosya hash'leriyle bulunur)
            if (!IsPack && targetId.Length > 0) _ = MarkInstalledAsync(targetId);

            string count = _total.ToString("N0", CultureInfo.GetCultureInfo("tr-TR")) + " sonuç";
            var target = Customs.FirstOrDefault(i => i.Id == targetId);
            Status = _total == 0 ? "Sonuç bulunamadı. Filtreleri gevşetmeyi dene."
                : !IsPack && target != null
                    ? $"{count} · {target.Name} ile uyumlu olanlar (Minecraft {target.McVersion}{(target.Loader != "vanilla" && Type == "mod" ? ", " + Names.Loader(target.Loader) : "")})"
                    : !IsPack ? $"{count} · \"Kur\"a basınca nereye kurulacağını seçersin" : count;
        }

        private async Task MarkInstalledAsync(string instanceId)
        {
            try
            {
                if (!_installedIndex.TryGetValue(instanceId, out var task))
                    _installedIndex[instanceId] = task = _core.Call("content.installedIndex", new { instanceId });
                var index = await task;
                var present = index.Items(IsCurseForge ? "curseforge" : "modrinth").Select(i => i.GetString()).ToHashSet();
                foreach (var hit in Hits.Where(h => present.Contains(h.Id))) hit.MarkPresent();
            }
            catch { _installedIndex.Remove(instanceId); }
        }

        /// <summary>Kurulum; versionId boşsa en yeni uyumlu sürüm. Instance seçilmediyse nereye kurulacağı sorulur.</summary>
        public async Task InstallAsync(ContentHit hit, string versionId)
        {
            string original = hit.ButtonText;
            hit.CanInstall = false;
            try
            {
                if (hit.Type == "modpack")
                {
                    hit.ButtonText = "Kuruluyor…";
                    var pack = await _core.Call(Command("installModpack"), new { projectId = hit.Id, versionId });
                    if (!pack.Bool("success")) throw new InvalidOperationException(pack.Str("error") ?? "Kurulamadı.");
                    hit.ButtonText = original;
                    await _main.ShowHomeAsync(pack.Str("id"));
                    if (pack.Items("manual").Any()) await ShowManualAsync(pack.Get("manual"), pack.Str("id"));
                    return;
                }

                string instanceId = Target?.Key ?? "";
                bool newTarget = instanceId.Length == 0;
                if (newTarget)
                {
                    if (await _main.ShowAsync(new TargetPickerModal(hit, Customs.ToList())) is not string picked)
                    {
                        hit.ButtonText = original;
                        return;
                    }
                    instanceId = picked;
                }
                hit.ButtonText = "Kuruluyor…";

                var result = await _core.Call(Command("install"), new { instanceId, projectId = hit.Id, versionId });
                if (!result.Bool("success")) throw new InvalidOperationException(result.Str("error") ?? "Kurulamadı.");
                var installed = result.Items("installed").Select(i => i.GetString()).ToList();
                hit.ButtonText = installed.Count > 0 ? "Kuruldu ✓" : original;
                hit.IsPrimary = false;
                _installedIndex.Remove(instanceId);
                if (result.Items("manual").Any()) await ShowManualAsync(result.Get("manual"), instanceId);
                // Hedef yeni seçildiyse sonuçlar artık o instance'a göre süzülür
                if (newTarget && Targets.FirstOrDefault(t => t.Key == instanceId) is { } option) Target = option;
                else if (installed.Count > 1) Status = "Kuruldu: " + string.Join(", ", installed);
            }
            catch (Exception ex)
            {
                hit.ButtonText = original;
                await _main.AlertAsync("Kurulamadı", ex.Message);
            }
            finally { hit.CanInstall = hit.ButtonText != "Kurulu ✓"; }
        }

        public async Task PickVersionAsync(ContentHit hit)
        {
            var modal = new VersionPickerModal(hit);
            _ = modal.LoadAsync(Command("versions"), hit.Type == "modpack" ? "" : Target?.Key ?? "");
            if (await _main.ShowAsync(modal) is string versionId) await InstallAsync(hit, versionId);
        }

        public async Task OpenPageAsync(ContentHit hit)
        {
            try
            {
                if (IsCurseForge) await _core.Call("cf.openPage", new { url = hit.PageUrl });
                else await _core.Call("modrinth.openPage", new { type = hit.Type, slug = hit.Slug });
            }
            catch { /* tarayıcı açılamadıysa yapacak bir şey yok */ }
        }

        private Task ShowManualAsync(JsonElement manual, string? instanceId) =>
            _main.ShowAsync(new ManualDownloadsModal(manual, instanceId));
    }
}
