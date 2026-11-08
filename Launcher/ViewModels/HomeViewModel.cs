using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>Sol şeritteki paket ya da kişisel instance.</summary>
    internal sealed partial class InstanceItem : ObservableObject
    {
        public required string Id { get; init; }
        public required string Kind { get; init; }
        public required string Name { get; init; }
        public string Description { get; init; } = "";
        public string McVersion { get; init; } = "";
        public string Loader { get; init; } = "";
        public string? LoaderVersion { get; init; }
        public int RecommendedRamGb { get; init; }
        public string? LogoUrl { get; init; }
        public string? BackgroundUrl { get; init; }
        public string? ThumbnailUrl { get; init; }
        public string? CustomIconUrl { get; init; }
        public bool HasCustomLogo { get; init; }
        public bool HasCustomBackground { get; init; }
        public bool BackgroundAsIcon { get; init; } = true;
        public string? SharedPackId { get; init; }
        public string? PackId { get; init; }

        public bool IsCustom => Kind == "custom";
        public string Initial => Name.Length > 0 ? Name[..1].ToUpper(CultureInfo.GetCultureInfo("tr-TR")) : "?";
        public string LoaderLabel => Names.Loader(Loader);
        public string Subtitle => Kind == "server" ? Name : $"{LoaderLabel} {McVersion}";

        [ObservableProperty] private Bitmap? _thumbnail;
        /// <summary>Paketin yeni revizyonu var (vulu Packs).</summary>
        [ObservableProperty] private bool _hasUpdate;

        public static InstanceItem From(JsonElement e) => new()
        {
            Id = e.Text("id") ?? "",
            Kind = e.Text("kind") ?? "custom",
            Name = e.Text("name") ?? "",
            Description = e.Text("description") ?? "",
            McVersion = e.Text("mcVersion") ?? "",
            Loader = e.Text("loader") ?? "",
            LoaderVersion = e.Text("loaderVersion"),
            RecommendedRamGb = e.Int("recommendedRamGb", 4),
            LogoUrl = e.Text("logo"),
            BackgroundUrl = e.Text("background"),
            // Kişisel instance'ın küçük resmi yoksa Modrinth/CurseForge simgesi kullanılır
            ThumbnailUrl = e.Text("thumbnail") ?? e.Text("iconUrl"),
            CustomIconUrl = e.Text("customIcon"),
            HasCustomLogo = e.Bool("hasCustomLogo"),
            HasCustomBackground = e.Bool("hasCustomBackground"),
            BackgroundAsIcon = e.Get("backgroundAsIcon").ValueKind != JsonValueKind.False,
            SharedPackId = e.Text("sharedPackId"),
            PackId = e.Text("packId"),
        };
    }

    /// <summary>Konsoldaki satır.</summary>
    internal sealed record LogLine(string Time, string Text, string Level)
    {
        public bool IsWarn => Level == "warn";
        public bool IsError => Level is "error" or "gameError";
    }

    /// <summary>Alt bilgi alanındaki dünya kartı.</summary>
    internal sealed partial class WorldTile : ObservableObject
    {
        public WorldTile(string name, string meta, string? icon)
        {
            Name = name;
            Meta = meta;
            _ = Load(icon);
        }

        public string Name { get; }
        public string Meta { get; }
        [ObservableProperty] private Bitmap? _icon;
        private async Task Load(string? url) => Icon = await Images.LoadAsync(url, 64);
    }

    /// <summary>Alt bilgi alanındaki ekran görüntüsü küçük resmi.</summary>
    internal sealed partial class ShotTile : ObservableObject
    {
        public ShotTile(string? url) => _ = Load(url);
        [ObservableProperty] private Bitmap? _image;
        private async Task Load(string? url) => Image = await Images.LoadAsync(url, 320);
    }

    /// <summary>
    /// Oyna sayfası: paket seçimi, seçili paketin arka planı ve bilgileri, işlemleri, dünyalar/ekran görüntüleri/içerik/konsol
    /// sekmeleri, alt çubuktaki RAM ve OYNA düğmesi.
    /// </summary>
    internal sealed partial class HomeViewModel : ObservableObject
    {
        private const int MaxLogLines = 600;
        private static readonly string[] InfoTabs = { "worlds", "shots", "content", "log" };
        private readonly CoreClient _core = CoreClient.Instance;
        private readonly Dictionary<string, JsonElement> _infoCache = new();
        private Dictionary<string, JsonElement> _packUpdates = new();
        private bool _lastLineWasProgress;
        private int _detailsSeq, _infoSeq;
        private bool _preview;   // geliştirme önizlemesi: Core'a sorulmaz
        private string _lastJoin = "";

        /// <summary>Kutular ve sayfa geçişleri için; MainViewModel kurulurken bağlanır.</summary>
        public MainViewModel Main { get; set; } = null!;

        // ── Paketin sunucusu ve orada oynayanlar (web: renderPackServer, m-heads, m-join) ──
        /// <summary>Seçili paketin bağlı olduğu vulu sunucusu; yoksa null.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPackServer), nameof(ShowBarJoin))]
        private ServerCard? _packServer;
        public bool HasPackServer => PackServer != null;
        /// <summary>Sunucu açıksa alt çubukta "Sunucuya bağlan" ana düğme olur, OYNA küçülür.</summary>
        public bool ShowBarJoin => PackServer is { IsOnline: true } && CanPlay;
        public ObservableCollection<PersonItem> PlayingHere { get; } = new();
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPlayingHere))]
        private string _playingHereText = "";
        public bool HasPlayingHere => PlayingHereText.Length > 0;

        partial void OnPackServerChanged(ServerCard? oldValue, ServerCard? newValue)
        {
            if (oldValue != null) oldValue.PropertyChanged -= OnPackServerStateChanged;
            if (newValue != null) newValue.PropertyChanged += OnPackServerStateChanged;
        }

        private void OnPackServerStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ServerCard.State)) OnPropertyChanged(nameof(ShowBarJoin));
        }

        private async Task UpdatePackServerAsync(InstanceItem item)
        {
            if (Main == null) return;
            if (Main.Servers.Servers.Count == 0)
            {
                try { await Main.Servers.LoadAsync(); } catch { /* sunucu listesi alınamadı */ }
                // Liste o sırada başka yerden yükleniyorduysa bitmesi beklenir
                for (int i = 0; i < 10 && Main.Servers.Servers.Count == 0 && Main.Servers.FirstLoad; i++) await Task.Delay(300);
            }
            if (Selected != item) return;
            var server = Main.Servers.Servers.FirstOrDefault(s => s.InstanceId == item.Id || (item.SharedPackId != null && s.PackId == item.SharedPackId));
            PackServer = server;
            if (server != null) _ = server.PingAsync();
        }

        /// <summary>Bu pakette oynayan arkadaşlar (en çok üç avatar ve "N kişi burada oynuyor").</summary>
        public void UpdatePlayingHere()
        {
            PlayingHere.Clear();
            if (Main == null || Selected is not { } item) { PlayingHereText = ""; return; }
            var names = Main.Social.PlayingIn(item.SharedPackId ?? item.PackId, item.Name).ToList();
            foreach (var name in names.Take(3)) PlayingHere.Add(Main.Social.Person(name));
            PlayingHereText = names.Count > 0 ? $"{names.Count} kişi burada oynuyor" : "";
        }

        [RelayCommand]
        private Task JoinPackServer() => PackServer is { } s ? Main.Servers.JoinAsync(s) : Task.CompletedTask;

        public ObservableCollection<InstanceItem> Instances { get; } = new();
        public ObservableCollection<LogLine> Console { get; } = new();
        public ObservableCollection<int> RamOptions { get; } = new();
        public ObservableCollection<WorldTile> Worlds { get; } = new();
        public ObservableCollection<ShotTile> Shots { get; } = new();
        public ObservableCollection<Tag> ContentTags { get; } = new();

        [ObservableProperty] private InstanceItem? _selected;
        [ObservableProperty] private Bitmap? _background;
        [ObservableProperty] private Bitmap? _logo;
        [ObservableProperty] private bool _hasLogo;

        [ObservableProperty] private string _modCount = "–";
        [ObservableProperty] private string _size = "–";
        [ObservableProperty] private string _updated = "–";
        [ObservableProperty] private string _playTime = "–";
        [ObservableProperty] private bool? _installed;

        [ObservableProperty] private int _ramGb = 4;
        public ObservableCollection<string> McVersions { get; } = new();
        [ObservableProperty] private string? _mcVersion;
        private List<string>? _allMcVersions;
        private bool _settingVersion;
        /// <summary>Sunucunun Vanilla paketinde sürüm seçilebilir; modlu paketlerin sürümü sabittir.</summary>
        public bool CanPickVersion => Selected is { Kind: "server", Loader: "vanilla" };

        partial void OnMcVersionChanged(string? value)
        {
            if (!_settingVersion && Selected != null && value != null) UiState.Set("mcver_" + Selected.Id, value);
        }

        private async Task LoadVersionsAsync(InstanceItem item)
        {
            OnPropertyChanged(nameof(CanPickVersion));
            if (!CanPickVersion) return;
            string current = UiState.Get("mcver_" + item.Id) ?? item.McVersion;
            _settingVersion = true;
            McVersions.Clear();
            McVersions.Add(current);
            McVersion = current;
            _settingVersion = false;
            if (_allMcVersions == null)
            {
                try { _allMcVersions = (await _core.Call("game.versions")).Items().Select(v => v.GetString() ?? "").ToList(); }
                catch { return; }   // liste alınamazsa yalnızca mevcut sürüm seçili kalır
            }
            if (Selected != item) return;
            _settingVersion = true;
            McVersions.Clear();
            if (!_allMcVersions.Contains(current)) McVersions.Add(current);
            foreach (var v in _allMcVersions) McVersions.Add(v);
            McVersion = current;
            _settingVersion = false;
        }
        [ObservableProperty] private bool _gameRunning;
        [ObservableProperty] private bool _launching;
        [ObservableProperty] private bool _canLaunchGame = true;

        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private double _progress;
        [ObservableProperty] private bool _indeterminate;
        [ObservableProperty] private string _playLabel = "OYNA";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowBarJoin))]
        private bool _canPlay;

        /// <summary>Alt bilgi alanında açık sekme: worlds, shots, content, log.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsWorlds), nameof(IsShots), nameof(IsContent), nameof(IsLog))]
        private string _infoTab = UiState.Get("infoTab") is { } t && InfoTabs.Contains(t) ? t : "worlds";
        public bool IsWorlds => InfoTab == "worlds";
        public bool IsShots => InfoTab == "shots";
        public bool IsContent => InfoTab == "content";
        public bool IsLog => InfoTab == "log";
        [ObservableProperty] private string? _infoMessage;
        [ObservableProperty] private string _shotsMore = "";
        [ObservableProperty] private string _contentSummary = "";
        /// <summary>Konsol sekmesi kapalıyken gelen hata: sekmede kırmızı nokta.</summary>
        [ObservableProperty] private bool _logAlert;

        /// <summary>Seçili paketin vulu Packs güncellemesi ("rev 3 → rev 4").</summary>
        [ObservableProperty] private string? _packUpdateText;
        public bool HasPackUpdate => PackUpdateText != null;
        partial void OnPackUpdateTextChanged(string? value) => OnPropertyChanged(nameof(HasPackUpdate));

        private string _activityText = "";

        /// <summary>Oyun çıktısı dışındaki son log satırı: diğer sayfalarda süren işlem (kurulum, indirme) bununla görünür.</summary>
        [ObservableProperty] private string _lastActivity = "";
        /// <summary>Oyna sayfasının üstündeki duyuru (kapatılan duyuru, yenisi gelene kadar gösterilmez).</summary>
        [ObservableProperty] private string? _announcement;
        [ObservableProperty] private string? _announcementMeta;
        public bool HasAnnouncement => Announcement != null;
        partial void OnAnnouncementChanged(string? value) => OnPropertyChanged(nameof(HasAnnouncement));
        private string? _announcementId;
        private Avalonia.Threading.DispatcherTimer? _announcementTimer;

        public async Task RefreshAnnouncementAsync()
        {
            _announcementTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMinutes(10), Avalonia.Threading.DispatcherPriority.Background,
                async (_, _) => await RefreshAnnouncementAsync());
            _announcementTimer.Start();
            JsonElement a;
            try { a = (await _core.Call("announcement.get")).Get("announcement"); } catch { return; }
            if (a.ValueKind != JsonValueKind.Object || UiState.Get("announcementDismissed") == a.Text("id")) { Announcement = null; return; }
            _announcementId = a.Text("id");
            AnnouncementMeta = string.Join(" · ", new[] { a.Text("author"), Names.Ago(a.Text("createdAt")) }.Where(x => !string.IsNullOrEmpty(x)));
            Announcement = a.Text("text");
        }

        [RelayCommand]
        private void DismissAnnouncement()
        {
            UiState.Set("announcementDismissed", _announcementId);
            Announcement = null;
        }

        /// <summary>Güncellemesi olan paket sayısı (Keşfet sekmesindeki rozet).</summary>
        [ObservableProperty] private int _packUpdateCount;

        public void ApplyInit(JsonElement init)
        {
            CanLaunchGame = init.Get("canLaunchGame").ValueKind != JsonValueKind.False;
            RamOptions.Clear();
            foreach (var gb in init.Items("ramOptionsGb")) RamOptions.Add(gb.GetInt32());
        }

        /// <summary>Girişten sonra: paketler listelenir, son seçilen paket seçilir; bekleyen çökme özeti gösterilir.</summary>
        public async Task LoadAsync()
        {
            Console.Clear();
            _lastLineWasProgress = false;
            await ReloadInstancesAsync(UiState.Get("lastPack"));
            _ = RefreshPackUpdatesAsync();
            _ = RefreshAnnouncementAsync();
            // Launcher simge durumundayken oyun çöktüyse özeti burada bekliyor
            try
            {
                var pending = await _core.Call("crash.takePending");
                if (pending.ValueKind == JsonValueKind.Object) await ShowCrashAsync(pending);
            }
            catch { /* yoksa ya da alınamadıysa sorun değil */ }
        }

#if DEBUG
        /// <summary>Geliştirme önizlemesi: sunucuya bağlanmadan örnek paketler.</summary>
        public void LoadPreview()
        {
            _preview = true;
            foreach (var gb in new[] { 2, 3, 4, 6, 8, 10, 12 }) RamOptions.Add(gb);
            Instances.Add(new InstanceItem { Id = "p:vanilla", Kind = "custom", Name = "Vanilla", Description = "Modsuz, sade Minecraft.", McVersion = "1.21.4", Loader = "vanilla", RecommendedRamGb = 4, LogoUrl = "assets/minecraftlogo.png", BackgroundUrl = "assets/launchervanillabg.jpg" });
            Instances.Add(new InstanceItem { Id = "p:fabric", Kind = "custom", Name = "Fabric 1.21", Description = "Kişisel instance", McVersion = "1.21.1", Loader = "fabric", RecommendedRamGb = 8 });
            foreach (var item in Instances) _ = LoadThumbnailAsync(item);
            Selected = Instances[0];
            Installed = true;
            ModCount = "0"; Size = "612 MB"; Updated = "Bugün"; PlayTime = "12 sa";
            AppendLog("Launcher güncel.");
            AppendLog("Önizleme modu: sunucuya bağlanılmadı.", "warn");
            Refresh();
        }
#endif

        public async Task ReloadInstancesAsync(string? selectId)
        {
            JsonElement list;
            try { list = await _core.Call("instances.list"); }
            catch (Exception ex) { AppendLog(ex.Message, "error"); return; }

            Instances.Clear();
            _infoCache.Clear();
            foreach (var e in list.Items())
            {
                var item = InstanceItem.From(e);
                item.HasUpdate = _packUpdates.ContainsKey(item.Id);
                Instances.Add(item);
                _ = LoadThumbnailAsync(item);
            }
            var select = Instances.FirstOrDefault(i => i.Id == selectId) ?? Instances.FirstOrDefault();
            if (select != null && select.Id == Selected?.Id) OnSelectedChanged(select);   // aynı paket: bilgiler yenilensin
            Selected = select;
            Refresh();
        }

        private static async Task LoadThumbnailAsync(InstanceItem item) =>
            item.Thumbnail = await Images.LoadAsync(item.ThumbnailUrl ?? item.BackgroundUrl, 96);

        partial void OnSelectedChanged(InstanceItem? value)
        {
            Background = null;
            Logo = null;
            HasLogo = false;
            ModCount = Size = Updated = PlayTime = "–";
            Installed = null;
            OnPropertyChanged(nameof(SelectedIsCustom));
            OnPropertyChanged(nameof(CanShare));
            OnPropertyChanged(nameof(ShareText));
            if (value == null) { Refresh(); return; }

            UiState.Set("lastPack", value.Id);
            PackServer = null;
            _ = UpdatePackServerAsync(value);
            UpdatePlayingHere();
            // Pakete ayrıca seçilen RAM; yoksa Ayarlar'daki varsayılan; o da "Önerilen"se paketin önerdiği
            int fallback = RamOptions.Contains(AppSettings.Current.DefaultRamGb) ? AppSettings.Current.DefaultRamGb
                : Math.Max(RamOptions.FirstOrDefault(4), RamOptions.LastOrDefault(gb => gb <= value.RecommendedRamGb));
            RamGb = int.TryParse(UiState.Get("ram_" + value.Id), out int saved) && RamOptions.Contains(saved) ? saved : fallback;
            PackUpdateText = _packUpdates.TryGetValue(value.Id, out var u) ? $"rev {u.Int("from")} → rev {u.Int("to")}" : null;
            _ = LoadVisualsAsync(value);
            _ = LoadVersionsAsync(value);
            if (!_preview) { _ = LoadDetailsAsync(value); _ = LoadInfoAsync(); }
            else Installed = true;
            Refresh();
        }

        public bool SelectedIsCustom => Selected?.IsCustom == true;
        /// <summary>Admin kendi instance'ını vulu Packs'te paylaşabilir (ya da paylaşımını güncelleyebilir).</summary>
        public bool CanShare => SelectedIsCustom && Main?.IsAdmin == true;
        public string ShareText => Selected?.SharedPackId != null ? "Paylaşımı güncelle" : "vulu Packs'te paylaş";

        [RelayCommand]
        private async Task Share()
        {
            if (Selected is not { IsCustom: true } s) return;
            JsonElement info;
            try { info = await _core.Call("packs.shareInfo", new { instanceId = s.Id }); }
            catch (Exception ex) { await Main.AlertAsync("Paylaşılamıyor", ex.Message); return; }
            if (await Main.ShowAsync(new ShareModal(Main, s, info)) is not true) return;
            await ReloadInstancesAsync(s.Id);
            AppendLog($"{s.Name} paylaşıldı. vulu Packs sekmesinde görünüyor.", "ok");
        }

        partial void OnRamGbChanged(int value)
        {
            if (Selected != null) UiState.Set("ram_" + Selected.Id, value.ToString(CultureInfo.InvariantCulture));
        }

        partial void OnGameRunningChanged(bool value) => Refresh();
        partial void OnLaunchingChanged(bool value) => Refresh();

        private async Task LoadVisualsAsync(InstanceItem item)
        {
            var background = Images.LoadAsync(item.BackgroundUrl, 1920);
            var logo = Images.LoadAsync(item.LogoUrl, 720);
            var bg = await background;
            var lg = await logo;
            if (Selected != item) return;
            Background = bg;
            Logo = lg;
            HasLogo = lg != null;
        }

        /// <summary>İçerik değişince (mod kuruldu, kapatıldı): istatistikler ve alt bilgi yenilenir.</summary>
        public void RefreshDetails()
        {
            if (Selected is not { } s || _preview) return;
            foreach (var key in _infoCache.Keys.Where(k => k.StartsWith(s.Id + "|", StringComparison.Ordinal)).ToList()) _infoCache.Remove(key);
            _ = LoadDetailsAsync(s);
            _ = LoadInfoAsync();
        }

        private async Task LoadDetailsAsync(InstanceItem item)
        {
            int seq = ++_detailsSeq;
            JsonElement d;
            try { d = await _core.Call("instances.details", new { id = item.Id }); }
            catch (Exception ex) { AppendLog(ex.Message, "error"); return; }
            if (seq != _detailsSeq) return;

            Installed = d.Bool("installed");
            ModCount = d.Int("modCount").ToString(CultureInfo.InvariantCulture);
            Size = FormatSize(d.Num("sizeMb"));
            Updated = d.Text("updatedAt") is { } u && DateTime.TryParse(u, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? FormatAgo(at) : "–";
            PlayTime = FormatPlayTime(d.Get("playTime").Num("seconds"));
            Refresh();
        }

        public static string FormatSize(double mb) =>
            mb >= 1024 ? (mb / 1024).ToString("0.#", CultureInfo.GetCultureInfo("tr-TR")) + " GB" : Math.Round(mb).ToString(CultureInfo.InvariantCulture) + " MB";

        public static string FormatAgo(DateTime at)
        {
            var days = (DateTime.Now.Date - at.ToLocalTime().Date).Days;
            return days <= 0 ? "Bugün" : days == 1 ? "Dün" : days < 30 ? $"{days} gün önce" : at.ToLocalTime().ToString("d MMM yyyy", CultureInfo.GetCultureInfo("tr-TR"));
        }

        public static string FormatPlayTime(double seconds)
        {
            if (seconds < 60) return "0 dk";
            if (seconds < 3600) return $"{(int)(seconds / 60)} dk";
            return $"{(int)(seconds / 3600)} sa";
        }

        // ── Alt bilgi alanı ──────────────────────────────────────────────
        [RelayCommand]
        private void SelectInfoTab(string tab)
        {
            InfoTab = tab;
            UiState.Set("infoTab", tab);
            if (tab == "log") LogAlert = false;
        }

        partial void OnInfoTabChanged(string value) => _ = LoadInfoAsync();

        /// <summary>Seçili sekmenin verisi; paket başına önbellekte tutulur, oyun kapanınca yenilenir.</summary>
        private async Task LoadInfoAsync()
        {
            if (Selected is not { } instance || _preview) return;
            string tab = InfoTab == "content" && !instance.IsCustom ? "worlds" : InfoTab;
            if (tab != InfoTab) { InfoTab = tab; return; }
            if (tab == "log") return;
            string key = instance.Id + "|" + tab;
            int seq = ++_infoSeq;
            InfoMessage = null;
            if (!_infoCache.TryGetValue(key, out var data))
            {
                try
                {
                    data = await _core.Call(tab == "worlds" ? "worlds.list" : tab == "shots" ? "screenshots.list" : "instances.content", new { id = instance.Id });
                    _infoCache[key] = data;
                }
                catch (Exception ex)
                {
                    if (seq != _infoSeq) return;
                    Worlds.Clear(); Shots.Clear(); ContentTags.Clear();
                    InfoMessage = Installed == false || ex.Message.Contains("kurul", StringComparison.OrdinalIgnoreCase)
                        ? "Paket kurulunca dünyaları ve ekran görüntüleri burada görünür." : ex.Message;
                    return;
                }
            }
            if (seq != _infoSeq || Selected != instance) return;

            switch (tab)
            {
                case "worlds":
                    Worlds.Clear();
                    foreach (var w in data.Items("worlds").OrderByDescending(w => w.Text("lastPlayed")).Take(8))
                        Worlds.Add(new WorldTile(w.Text("name") ?? "", Names.Ago(w.Text("lastPlayed")) + (w.Items("backups").Count() is > 0 and var n ? $" · {n} yedek" : ""), w.Text("icon")));
                    if (Worlds.Count == 0) InfoMessage = "Henüz dünya yok. Oyunu açıp bir dünya oluşturunca burada görünür.";
                    break;
                case "shots":
                    Shots.Clear();
                    foreach (var s in data.Items("shots").Take(10)) Shots.Add(new ShotTile(s.Text("url")));
                    ShotsMore = $"Tümü ({data.Int("total")}) ›";
                    if (Shots.Count == 0) InfoMessage = "Henüz ekran görüntüsü yok. Oyunda F2 tuşuna basınca burada görünür.";
                    break;
                case "content":
                    ContentTags.Clear();
                    var items = data.Items("items").ToList();
                    int Count(string type) => items.Count(i => i.Text("type") == type);
                    ContentSummary = string.Join(" · ", new[] { (Count("mod"), "mod"), (Count("resourcepack"), "kaynak paketi"), (Count("shader"), "shader") }
                        .Where(x => x.Item1 > 0).Select(x => $"{x.Item1} {x.Item2}"));
                    var mods = items.Where(i => i.Text("type") == "mod").ToList();
                    foreach (var m in mods.Take(18)) ContentTags.Add(new Tag(m.Text("title") ?? ""));
                    if (mods.Count > 18) ContentTags.Add(new Tag($"+{mods.Count - 18}"));
                    if (items.Count == 0) InfoMessage = "Bu instance'ta henüz mod yok.";
                    break;
            }
        }

        // ── Paket işlemleri ──────────────────────────────────────────────
        [RelayCommand]
        private async Task NewInstance()
        {
            if (await Main.ShowAsync(new InstanceEditorModal(null)) is string id) await ReloadInstancesAsync(id);
        }

        [RelayCommand]
        private async Task EditInstance()
        {
            if (Selected is not { IsCustom: true } s) return;
            if (await Main.ShowAsync(new InstanceEditorModal(s)) is string id) await ReloadInstancesAsync(id);
        }

        [RelayCommand]
        private async Task Appearance()
        {
            if (Selected is { } s) await Main.ShowAsync(new AppearanceModal(this, s));
        }

        [RelayCommand]
        private async Task AddContent()
        {
            if (Selected is { IsCustom: true } s) await Main.OpenDiscoverForAsync(s);
        }

        [RelayCommand]
        private async Task ManageContent()
        {
            if (Selected is { IsCustom: true } s) await Main.ShowAsync(new ContentModal(Main, s));
            RefreshDetails();
        }

        [RelayCommand]
        private async Task OpenWorlds()
        {
            if (Selected is { } s) await Main.ShowAsync(new WorldsModal(Main, s));
            RefreshDetails();
        }

        [RelayCommand]
        private async Task OpenShots()
        {
            if (Selected is { } s) await Main.ShowAsync(new ScreenshotsModal(Main, s));
            RefreshDetails();
        }

        [RelayCommand]
        private async Task Verify()
        {
            if (Selected is not { } s) return;
            if (GameRunning || Launching) { AppendLog("Şu an başka bir işlem sürüyor.", "warn"); return; }
            JsonElement result;
            try { result = await _core.Call("instances.verify", new { id = s.Id }); }
            catch (Exception ex) { await Main.AlertAsync("Doğrulanamadı", ex.Message); return; }
            RefreshDetails();
            await Main.ShowAsync(new VerifyModal(s.Name, result));
        }

        [RelayCommand]
        private async Task OpenFolder()
        {
            if (Selected is not { } s) return;
            try { await _core.Call("files.open", new { target = "instance", id = s.Id }); } catch (Exception ex) { AppendLog(ex.Message, "error"); }
        }

        [RelayCommand]
        private async Task CreateShortcut()
        {
            if (Selected is not { } s) return;
            try
            {
                await _core.Call("shortcuts.create", new { kind = "play", id = s.Id, name = s.Name, iconDataUrl = "" });
                await Main.AlertAsync("Kısayol eklendi", $"Masaüstündeki \"{s.Name}\" kısayolu launcher'ı açıp {s.Name} paketini doğrudan başlatır.");
            }
            catch (Exception ex) { await Main.AlertAsync("Kısayol oluşturulamadı", ex.Message); }
        }

        [RelayCommand]
        private async Task Duplicate()
        {
            if (Selected is not { IsCustom: true } s) return;
            if (await Main.ShowAsync(new PromptModal("Instance'ı çoğalt", "Kopyanın adı", $"{s.Name} (kopya)", "Çoğalt")) is not string name) return;
            try
            {
                var result = await _core.Call("instances.duplicate", new { id = s.Id, name });
                await ReloadInstancesAsync(result.Text("id"));
            }
            catch (Exception ex) { await Main.AlertAsync("Çoğaltılamadı", ex.Message); }
        }

        [RelayCommand]
        private async Task ExportMrpack()
        {
            if (Selected is not { IsCustom: true } s) return;
            try
            {
                var result = await _core.Call("instances.exportMrpack", new { id = s.Id });
                AppendLog("Kaydedildi: " + result.Text("path"), "ok");
            }
            catch (Exception ex) { await Main.AlertAsync("Dışa aktarılamadı", ex.Message); }
        }

        [RelayCommand]
        private async Task DeleteInstance()
        {
            if (Selected is not { IsCustom: true } s) return;
            if (!await Main.ConfirmAsync("Instance'ı sil", $"'{s.Name}' ve içindeki her şey (dünyalar, modlar, ayarlar) silinecek.\nBu işlem geri alınamaz.", "Sil", danger: true)) return;
            try
            {
                await _core.Call("instances.delete", new { id = s.Id });
                await ReloadInstancesAsync(null);
            }
            catch (Exception ex) { await Main.AlertAsync("Silinemedi", ex.Message); }
        }

        /// <summary>Seçili paketin vulu Packs güncellemesi: değişiklikler gösterilip güncellenir.</summary>
        [RelayCommand]
        private async Task UpdatePack()
        {
            if (Selected is not { } s || !_packUpdates.TryGetValue(s.Id, out var u)) return;
            await Main.Discover.Packs.UpdateByIdAsync(u.Text("packId") ?? "");
            await RefreshPackUpdatesAsync();
        }

        public async Task RefreshPackUpdatesAsync()
        {
            try { ApplyPackUpdates(await _core.Call("packs.updates")); } catch { /* sonra yeniden denenir */ }
        }

        private void ApplyPackUpdates(JsonElement result)
        {
            _packUpdates = result.Items("updates").ToDictionary(u => u.Text("instanceId") ?? "", u => u.Clone());
            PackUpdateCount = _packUpdates.Count;
            foreach (var i in Instances) i.HasUpdate = _packUpdates.ContainsKey(i.Id);
            PackUpdateText = Selected != null && _packUpdates.TryGetValue(Selected.Id, out var u) ? $"rev {u.Int("from")} → rev {u.Int("to")}" : null;
        }

        // ── Oyna ─────────────────────────────────────────────────────────
        /// <summary>Alt çubuktaki durum metni, ilerleme ve OYNA düğmesi.</summary>
        private void Refresh()
        {
            Indeterminate = false;
            if (Launching)
            {
                StatusText = _activityText.Length > 0 ? _activityText : "Başlatılıyor…";
                var pct = ProgressOf(StatusText);
                Progress = pct ?? 0;
                Indeterminate = pct == null;
            }
            else if (GameRunning) { StatusText = "Oyun çalışıyor"; Progress = 100; }
            else if (!CanLaunchGame) { StatusText = "Oyun şimdilik yalnızca bilgisayarda açılıyor"; Progress = 0; }
            else if (Selected == null) { StatusText = "Başlamak için bir paket seç"; Progress = 0; }
            else if (Installed == null) { StatusText = "Paket bilgileri alınıyor…"; Progress = 0; Indeterminate = true; }
            else if (Installed == false) { StatusText = "İlk açılışta oyun dosyaları indirilecek"; Progress = 0; }
            else { StatusText = "Hazır"; Progress = 100; }

            PlayLabel = Launching ? "Başlatılıyor…" : GameRunning ? "Oyun çalışıyor" : !CanLaunchGame ? "Telefonda oyun yakında" : "OYNA";
            CanPlay = !Launching && !GameRunning && CanLaunchGame && Selected != null;
            PlayCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Log satırındaki ilerleme: "%45", "45%" ya da "12/120". Bulunamazsa null.</summary>
        private static double? ProgressOf(string text)
        {
            var m = Regex.Match(text, @"%\s?(\d{1,3})|(\d{1,3})\s?%");
            if (m.Success) return Math.Min(100, int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, CultureInfo.InvariantCulture));
            m = Regex.Match(text, @"(\d+)\s?/\s?(\d+)");
            if (m.Success && double.TryParse(m.Groups[2].Value, out double total) && total > 0)
                return Math.Min(100, Math.Round(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 100 / total));
            return null;
        }

        [RelayCommand(CanExecute = nameof(CanPlay))]
        private Task Play() => PlayAsync(false, false, "");

        /// <summary>Seçili paketi açar; join verilirse oyun doğrudan o sunucuya bağlanır.</summary>
        public async Task PlayAsync(bool ignoreMissing, bool ignoreFiles, string join)
        {
            if (Selected is not { } instance || GameRunning || Launching) return;
            _lastJoin = join;
            if (!ignoreMissing && !ignoreFiles) { Console.Clear(); _lastLineWasProgress = false; }
            _activityText = "";
            Launching = true;
            // Açılış sırasında konsol görünsün (seçilen sekme hatırlanmaya devam eder)
            InfoTab = "log";
            JsonElement result = default;
            try
            {
                result = await _core.Call("game.play", new { instanceId = instance.Id, ramGb = RamGb, mcVersion = CanPickVersion ? McVersion ?? "" : "", ignoreMissing, ignoreFiles, join });
                GameRunning = result.Bool("started");
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message, "error");
            }
            Launching = false;

            // Boş/bozuk ya da kopya jar'lar, yerine konmamış elle indirilecekler
            if (result.Get("files").ValueKind == JsonValueKind.Object)
            {
                var choice = await Main.ShowAsync(new FileProblemsModal(Main, result.Get("files").Clone(), instance));
                RefreshDetails();
                if (choice is "retry") await PlayAsync(ignoreMissing, false, join);
                else if (choice is "ignore") await PlayAsync(ignoreMissing, true, join);
                return;
            }

            // Eksik bağımlılık: kullanıcıya sor, kurulursa yeniden dene
            if (result.Items("missing").Any())
            {
                var missing = result.Get("missing").Clone();
                var choice = await Main.ShowAsync(new MissingDepsModal(missing, instance));
                if (choice is "install")
                {
                    try
                    {
                        // Hepsi tek seferde; kurulanların da eksiği çıkarsa launcher onları ayrıca sormadan kurar
                        await _core.Call("mods.installMissing", new
                        {
                            instanceId = instance.Id,
                            items = missing.Items().Where(m => m.Text("projectId") != null).Select(m => new { source = m.Text("source") ?? "modrinth", projectId = m.Text("projectId") }).ToList(),
                        });
                        RefreshDetails();
                    }
                    catch (Exception ex) { AppendLog(ex.Message, "error"); return; }
                    await PlayAsync(false, ignoreFiles, join);
                }
                else if (choice is "ignore") await PlayAsync(true, ignoreFiles, join);
                return;
            }

            if (!result.Bool("started") && result.Text("error") is { Length: > 0 } error)
                AppendLog(error, "error");
        }

        public async Task ShowCrashAsync(JsonElement crash)
        {
            var modal = new CrashModal(Main, crash.Clone());
            var choice = await Main.ShowAsync(modal);
            if (modal.Changed) await ReloadInstancesAsync(Selected?.Id);
            if (choice is "replay")
            {
                Main.SelectedTab = Main.Tabs[0];
                if (Instances.FirstOrDefault(i => i.Id == modal.InstanceId) is { } target) Selected = target;
                await PlayAsync(false, false, _lastJoin);
            }
        }

        public void AppendLog(string text, string level = "info", bool replace = false, string? time = null)
        {
            var line = new LogLine(time ?? DateTime.Now.ToString("HH:mm:ss"), text, level);
            // İlerleme satırları art arda gelirse aynı satır güncellenir
            if (replace && _lastLineWasProgress && Console.Count > 0) Console[^1] = line;
            else
            {
                Console.Add(line);
                while (Console.Count > MaxLogLines) Console.RemoveAt(0);
            }
            _lastLineWasProgress = replace;

            if (line.IsError && !IsLog) LogAlert = true;
            if (level != "game" && level != "gameError") LastActivity = text;
            if (level != "game" && level != "gameError" && Launching)
            {
                _activityText = text;
                Refresh();
            }
        }

        [RelayCommand]
        private void ClearConsole()
        {
            Console.Clear();
            _lastLineWasProgress = false;
        }

        public void OnCoreEvent(string name, JsonElement data)
        {
            switch (name)
            {
                case "log":
                    foreach (var line in data.Items())
                        AppendLog(line.Text("text") ?? "", line.Text("level") ?? "info", line.Bool("replace"), line.Text("time"));
                    break;
                case "gameExited":
                    GameRunning = false;
                    _infoCache.Clear();
                    InfoTab = UiState.Get("infoTab") is { } t && InfoTabs.Contains(t) ? t : "worlds";
                    RefreshDetails();
                    break;
                case "gameCrashed":
                    _ = ShowCrashAsync(data);
                    break;
                case "logosChanged":
                    _ = ReloadInstancesAsync(Selected?.Id);
                    break;
                case "packUpdates":
                    ApplyPackUpdates(data);
                    break;
            }
        }
    }
}
