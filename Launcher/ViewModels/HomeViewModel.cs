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
        /// <summary>CurseForge ya da Modrinth'ten kurulmuş hazır modpack.</summary>
        public bool IsModpack { get; init; }
        public string? ModpackVersion { get; init; }
        /// <summary>Kütüphanede sıralama: toplam oynama süresi, son oynanma, eklenme.</summary>
        public double PlaySeconds { get; init; }
        public DateTime? LastPlayed { get; init; }
        public DateTime? CreatedAt { get; init; }

        public bool IsCustom => Kind == "custom";
        /// <summary>
        /// Görünümü (simge, logo, arka plan) yalnızca kendi oluşturduğun pakette değişir; hazır indirilen paketler
        /// (vulu'nun resmi paketleri, vulu Packs, CurseForge/Modrinth modpackleri) kendi görünümleriyle kalır.
        /// </summary>
        public bool CanEditLook => IsCustom && !IsVuluPack && !IsModpack;
        /// <summary>vulu Packs'ten kurulmuş paket (kişisel instance olarak saklanır ama ekibin paketidir).</summary>
        public bool IsVuluPack => !string.IsNullOrEmpty(SharedPackId);
        public string Initial => Name.Length > 0 ? Name[..1].ToUpper(CultureInfo.GetCultureInfo("tr-TR")) : "?";
        public string LoaderLabel => Names.Loader(Loader);
        public string Subtitle => Kind == "server" ? Name : $"{LoaderLabel} {McVersion}";

        [ObservableProperty] private Bitmap? _thumbnail;
        /// <summary>Kütüphane temasındaki kapak: paketin arka planı, kapak boyunda (küçük simge kapağa büyütülünce bulanıklaşıyordu).</summary>
        [ObservableProperty] private Bitmap? _poster;
        /// <summary>Paketin yeni revizyonu var (vulu Packs).</summary>
        [ObservableProperty] private bool _hasUpdate;
        /// <summary>Şeritteki ipucu: adı; o pakette oynayan varsa "Şu an oynayan: …" (web: pack-card title).</summary>
        [ObservableProperty] private string _tip = "";

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
            IsModpack = e.Bool("isModpack"),
            ModpackVersion = e.Text("modpackVersion"),
            PlaySeconds = e.Num("playSeconds"),
            LastPlayed = DateTime.TryParse(e.Text("lastPlayed"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lp) ? lp : null,
            CreatedAt = DateTime.TryParse(e.Text("createdAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ca) ? ca : null,
        };
    }

    /// <summary>Konsoldaki satır.</summary>
    /// <summary>
    /// Konsol satırları. Oyun saniyede yüzlerce satır yazabilir: satırlar birikip tek seferde eklenir (toplu
    /// bildirim), her satır için ayrı ekle/sil değil.
    /// </summary>
    internal sealed class LogCollection : System.Collections.ObjectModel.ObservableCollection<LogLine>
    {
        public void AppendRange(List<LogLine> lines, int max)
        {
            if (lines.Count == 0) return;
            CheckReentrancy();
            var added = lines.Count > max ? lines.GetRange(lines.Count - max, max) : lines.ToList();
            int extra = Math.Min(Items.Count, Items.Count + added.Count - max);
            // Önce baştan taşanlar, sonra sona yeniler: iki toplu bildirim (liste yalnızca değişen yeri günceller)
            if (extra > 0)
            {
                var removed = ((List<LogLine>)Items).GetRange(0, extra);
                ((List<LogLine>)Items).RemoveRange(0, extra);
                OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Remove, removed, 0));
            }
            int at = Items.Count;
            ((List<LogLine>)Items).AddRange(added);
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Add, added, at));
        }
    }

    internal sealed record LogLine(string Time, string Text, string Level)
    {
        public bool IsWarn => Level == "warn";
        public bool IsError => Level is "error" or "gameError";
    }

    /// <summary>Alt bilgi alanındaki dünya kartı.</summary>
    internal sealed partial class WorldTile : ObservableObject
    {
        public WorldTile(string name, string meta, string? icon, bool canQuickPlay = false)
        {
            Name = name;
            CanQuickPlay = canQuickPlay;
            Meta = meta;
            _ = Load(icon);
        }

        public string Name { get; }
        public string Meta { get; }
        /// <summary>Oyun doğrudan bu dünyada açılabilir (Minecraft 1.20+).</summary>
        public bool CanQuickPlay { get; }
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

        /// <summary>Kişisel instance'ı şeritte başka yere taşır (sürükle bırak); sıra bu bilgisayarda hatırlanır.</summary>
        public void MoveInstance(InstanceItem item, InstanceItem target)
        {
            if (!item.IsCustom || !target.IsCustom || item == target) return;
            int from = Instances.IndexOf(item), to = Instances.IndexOf(target);
            if (from < 0 || to < 0) return;
            Instances.Move(from, to);
        }

        public void SaveInstanceOrder() =>
            UiState.Set("instanceOrder", string.Join(",", Instances.Where(i => i.IsCustom).Select(i => i.Id)));

        /// <summary>Bu pakette oynayan arkadaşlar (en çok üç avatar ve "N kişi burada oynuyor").</summary>
        public void UpdatePlayingHere()
        {
            if (Main != null)
                foreach (var instance in Instances)
                {
                    var here = Main.Social.PlayingIn(instance.SharedPackId ?? instance.PackId, instance.Name).ToList();
                    instance.Tip = here.Count > 0 ? $"{instance.Name}\nŞu an oynayan: {string.Join(", ", here)}" : instance.Name;
                }
            PlayingHere.Clear();
            if (Main == null || Selected is not { } item) { PlayingHereText = ""; return; }
            var names = Main.Social.PlayingIn(item.SharedPackId ?? item.PackId, item.Name).ToList();
            foreach (var name in names.Take(3)) PlayingHere.Add(Main.Social.Person(name));
            PlayingHereText = names.Count > 0 ? $"{names.Count} kişi burada oynuyor" : "";
        }

        [RelayCommand]
        private Task JoinPackServer() => PackServer is { } s ? Main.Servers.JoinAsync(s) : Task.CompletedTask;

        public ObservableCollection<InstanceItem> Instances { get; } = new();
        public LogCollection Console { get; } = new();
        public ObservableCollection<int> RamOptions { get; } = new();
        public ObservableCollection<WorldTile> Worlds { get; } = new();
        public ObservableCollection<ShotTile> Shots { get; } = new();
        public ObservableCollection<Tag> ContentTags { get; } = new();

        [ObservableProperty] private InstanceItem? _selected;
        [ObservableProperty] private Bitmap? _background;
        /// <summary>Arka planın önceden bulanıklaştırılmış küçük kopyası (Meşale'de öbür sayfaların zemini).</summary>
        [ObservableProperty] private Bitmap? _backgroundBlurred;
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

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowStatusRow))]
        private string _statusText = "";
        /// <summary>Kütüphane çekmecesi: durum ve ilerleme çubuğu yalnızca söylenecek bir şey varken (boşta "Hazır %100" gösterilmez).</summary>
        public bool ShowStatusRow => StatusText.Length > 0 && StatusText != "Hazır";
        [ObservableProperty] private double _progress;
        [ObservableProperty] private bool _indeterminate;
        [ObservableProperty] private string _playLabel = "OYNA";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowBarJoin))]
        private bool _canPlay;

        /// <summary>Alt bilgi alanında açık sekme: worlds, shots, content, log.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsWorlds), nameof(IsShots), nameof(IsContent), nameof(IsLog), nameof(InfoIcon))]
        private string _infoTab = UiState.Get("infoTab") is { } t && InfoTabs.Contains(t) ? t : "worlds";
        /// <summary>
        /// Meşale ve sahneli temalar: dünyalar, ekran görüntüleri, içerik ve konsol sağdan kayan çekmecede; "Ayrıntılar" açar.
        /// Oyun açılırken konsol görünsün diye kendiliğinden açılır, oyun kapanınca (kendiliğinden açıldıysa) kapanır.
        /// </summary>
        [ObservableProperty] private bool _detailsOpen;
        private bool _detailsAutoOpened;
        /// <summary>Konsol: oyunun günlük klasörü (logs/latest.log ve çökme kayıtları).</summary>
        [RelayCommand]
        private async Task OpenLogs()
        {
            if (Selected is not { } s) return;
            try { await _core.Call("files.open", new { target = "logs", id = s.Id }); } catch (Exception ex) { AppendLog(ex.Message, "error"); }
        }

        /// <summary>
        /// Oyunun günlüğünü paylaşılabilir bağlantıya çevirir (mclo.gs): önce sorulur, bağlantı panoya kopyalanır.
        /// Kullanıcı adı ve oturum anahtarları yüklemeden önce gizlenir.
        /// </summary>
        [RelayCommand]
        private async Task ShareLog()
        {
            if (Selected is not { } s) return;
            if (await Main.ShowAsync(new LogShareModal()) is not string how) return;
            string? to = null;
            // vulu'da: önce kime gönderileceği seçilir (vazgeçilirse hiçbir şey yüklenmez)
            if (how == "vulu" && (to = await Main.Social.PickUserAsync("Günlüğü kime gönderelim?")) == null) return;
            try
            {
                var r = await _core.Call("logs.share", new { id = s.Id });
                string url = r.Text("url") ?? "";
                AppendLog("Günlük paylaşıldı: " + url, "ok");
                if (to != null)
                {
                    string text = $"{s.Name} ({s.LoaderLabel} {s.McVersion}) günlüğüm, bakabilir misin? {url}";
                    if (await Main.Social.SendToAsync(to, text, null)) Main.ShowToast("Gönderildi", $"Günlük {to} adlı kişiye gönderildi.");
                    return;
                }
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } w }
                    && Avalonia.Controls.TopLevel.GetTopLevel(w)?.Clipboard is { } clipboard)
                    await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, url);
                Main.ShowToast("Bağlantı kopyalandı", url);
            }
            catch (Exception ex) { await Main.AlertAsync("Günlük paylaşılamadı", ex.Message); }
        }

        // ── Konsolda arama ──
        // Arama yazılınca yalnızca eşleşen satırlar görünür; yeni gelen satırlar da süzülür.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ConsoleView))]
        private string _consoleQuery = "";
        private readonly System.Collections.ObjectModel.ObservableCollection<LogLine> _consoleMatches = new();
        private bool _consoleWatch;
        public System.Collections.IEnumerable ConsoleView => string.IsNullOrWhiteSpace(ConsoleQuery) ? Console : _consoleMatches;

        partial void OnConsoleQueryChanged(string value)
        {
            if (!_consoleWatch) { Console.CollectionChanged += (_, _) => { if (!string.IsNullOrWhiteSpace(ConsoleQuery)) RefilterConsole(); }; _consoleWatch = true; }
            RefilterConsole();
        }

        private void RefilterConsole()
        {
            string q = ConsoleQuery.Trim();
            _consoleMatches.Clear();
            if (q.Length == 0) return;
            foreach (var line in Console)
                if (line.Text.Contains(q, StringComparison.OrdinalIgnoreCase)) _consoleMatches.Add(line);
        }

        /// <summary>Konsoldaki bütün satırlar düz yazı olarak (panoya kopyalamak için).</summary>
        public string ConsoleText() { FlushLog(); return string.Join(Environment.NewLine, Console.Select(l => $"{l.Time}  {l.Text}")); }

        /// <summary>Boş kütüphanedeki karşılama düğmeleri: Yeni paket penceresi o kaynakla açılır.</summary>
        [RelayCommand] private Task NewPackFrom(string source) => Main.OpenNewPackAsync(source);

        [RelayCommand] private void ToggleDetails() { DetailsOpen = !DetailsOpen; _detailsAutoOpened = false; }

        /// <summary>Durdur: açılış sürerken (hazırlık ya da oyun penceresi açılana kadar) ya da oyun çalışırken.</summary>
        public bool CanStop => Launching || GameRunning;
        public string StopText => Launching || _boot != null ? "Durdur" : "Oyunu kapat";

        [RelayCommand]
        private async Task StopGame()
        {
            // Oyun tamamen açıldıysa kaydedilmemiş ilerleme gidebilir: önce sorulur
            if (!Launching && _boot == null && GameRunning &&
                !await Main.ConfirmAsync("Oyunu kapat", "Oyun hemen kapatılır; kaydedilmemiş ilerleme kaybolabilir. Normalde oyunun içinden çıkmak daha iyidir.", "Kapat", danger: true))
                return;
            _stopping = true;
            Refresh();
            try { await _core.Call("game.stop"); } catch (Exception ex) { AppendLog(ex.Message, "error"); }
        }

        /// <summary>Durdur'a basıldı, açılış daha kapanmadı: düğmede "Durduruluyor…".</summary>
        private bool _stopping;
        /// <summary>Boş sekmenin simgesi (Kütüphane çekmecesindeki boş durum kutusu).</summary>
        public string InfoIcon => InfoTab switch { "shots" => "🖼", "content" => "🧩", _ => "🗺" };
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
            _totalRamGb = init.Num("totalRamGb");
        }

        // ── Bellek önerisi ──
        // "Önerilen" yerine sayı: paketin önerdiği (bilgisayara sığacak kadar) ve bilgisayardaki toplam RAM; fazla seçilince uyarı.
        private double _totalRamGb;
        /// <summary>Windows'a da yer bırakarak oyuna verilebilecek en çok bellek (MemoryCheck ile aynı pay).</summary>
        private int SafeMaxRamGb => _totalRamGb > 0 ? Math.Max(2, (int)Math.Floor(_totalRamGb - 2.5)) : int.MaxValue;
        public int RecommendedRamFor(InstanceItem item) =>
            Math.Min(SafeMaxRamGb, Math.Max(RamOptions.FirstOrDefault(4), RamOptions.LastOrDefault(gb => gb <= item.RecommendedRamGb)));

        public string RamHint
        {
            get
            {
                if (Selected is not { } item) return "";
                int rec = RecommendedRamFor(item);
                string machine = _totalRamGb > 0 ? $"bilgisayarda {Math.Round(_totalRamGb):0} GB RAM var" : "";
                if (RamGb > SafeMaxRamGb) return $"{RamGb} GB bu bilgisayar için fazla ({machine}); oyun en çok {SafeMaxRamGb} GB ile açılır.";
                if (RamGb < rec) return $"Bu paket için önerilen {rec} GB; daha azıyla takılabilir" + (machine.Length > 0 ? $" · {machine}" : "");
                return $"Önerilen {rec} GB" + (machine.Length > 0 ? $" · {machine}" : "");
            }
        }
        public bool RamWarn => Selected is { } item && (RamGb > SafeMaxRamGb || RamGb < RecommendedRamFor(item));

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
            // VULU_UI_MANUAL=1: seçili pakette elle indirilecek iki örnek mod (uyarı ve pencere görünümü)
            if (Environment.GetEnvironmentVariable("VULU_UI_MANUAL") == "1")
            {
                _manual = JsonDocument.Parse("""[{"fileId":1,"title":"Örnek Mod","folder":"mods","fileName":"ornek-1.0.jar","url":"https://www.curseforge.com"},{"fileId":2,"title":"Başka Mod","folder":"mods","fileName":"baska-2.1.jar","url":"https://www.curseforge.com"}]""").RootElement.Clone();
                _manualFor = Selected.Id;
                ManualCount = 2;
            }
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
            // Sunucu paketleri üstte; kişisel instance'lar kullanıcının sürükleyerek verdiği sırayla (web: orderedCustoms)
            var items = list.Items().Select(InstanceItem.From).ToList();
            var order = (UiState.Get("instanceOrder") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            int Rank(InstanceItem i) { int k = order.IndexOf(i.Id); return k < 0 ? int.MaxValue : k; }
            var ordered = items.Where(i => !i.IsCustom).Concat(items.Where(i => i.IsCustom).OrderBy(Rank));
            foreach (var item in ordered)
            {
                item.HasUpdate = _packUpdates.ContainsKey(item.Id);
                item.Tip = item.Name;
                Instances.Add(item);
                _ = LoadThumbnailAsync(item);
            }
            UpdatePlayingHere();
            var select = Instances.FirstOrDefault(i => i.Id == selectId) ?? Instances.FirstOrDefault();
            if (select != null && select.Id == Selected?.Id) OnSelectedChanged(select);   // aynı paket: bilgiler yenilensin
            Selected = select;
            Refresh();
        }

        private static async Task LoadThumbnailAsync(InstanceItem item)
        {
            item.Thumbnail = await Images.LoadAsync(item.ThumbnailUrl ?? item.BackgroundUrl, 96);
            item.Poster = await Images.LoadAsync(item.BackgroundUrl ?? item.ThumbnailUrl, 420);
            // Bulanık ortam zemini önceden hazırlanır: pakete ilk tıklayınca da arka plan beklemeden değişsin
            _ = Images.LoadBlurredAsync(item.BackgroundUrl);
        }

        // ── Kütüphane: instance sayfası (kapağa tıklayınca kütüphanenin yerine açılır) ──
        /// <summary>Instance sayfası açık: solda sekmeler (İçerik, Dünyalar, Görüntüler, Konsol, Ayarlar), sağda paketin çekmecesi.</summary>
        [ObservableProperty] private bool _pageOpen;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPageContent), nameof(IsPageWorlds), nameof(IsPageShots), nameof(IsPageLog), nameof(IsPageSettings), nameof(IsPageInfo))]
        private string _pageTab = "worlds";
        public bool IsPageContent => PageTab == "content";
        public bool IsPageWorlds => PageTab == "worlds";
        public bool IsPageShots => PageTab == "shots";
        public bool IsPageLog => PageTab == "log";
        public bool IsPageSettings => PageTab == "settings";
        /// <summary>Dünyalar ya da Görüntüler: bilgi mesajı (boş, alınamadı) bu sekmelerde görünür.</summary>
        public bool IsPageInfo => PageTab is "worlds" or "shots";
        /// <summary>İçerik sekmesinin modeli (mod listesi: aç/kapa, güncelle, kaldır); sekme her açılışta tazelenir.</summary>
        [ObservableProperty] private ContentModal? _pageContent;
        private string? _pageItemId;

        [RelayCommand]
        private void OpenPage(InstanceItem item)
        {
            if (Selected != item) Selected = item;
            _pageItemId = item.Id;
            PageOpen = true;
            SelectPageTab(item.IsCustom ? "content" : "worlds");
        }

        [RelayCommand] private void ClosePage() { PageOpen = false; _pageItemId = null; PageContent = null; }

        [RelayCommand]
        private void SelectPageTab(string tab)
        {
            if (tab == "content" && Selected is not { IsCustom: true }) tab = "worlds";
            PageTab = tab;
            if (tab is "worlds" or "shots" or "log") SelectInfoTab(tab);
            if (tab == "content" && Selected is { } s && Main != null) PageContent = new ContentModal(Main, s);
        }

        /// <summary>Sayfadaki "+ İçerik ekle": pencere kapanınca liste tazelenir.</summary>
        [RelayCommand]
        private async Task PageAddContent()
        {
            if (Selected is not { IsCustom: true } s || Main == null) return;
            await Main.OpenAddContentAsync(s);
            if (PageContent != null) await PageContent.LoadAsync();
        }

        /// <summary>Kapağın üstündeki ▶: o paket seçilir ve başlatılır (sayfa açılmaz).</summary>
        [RelayCommand]
        private async Task PlayItem(InstanceItem item)
        {
            if (Selected != item) Selected = item;
            await Task.Yield();
            if (PlayCommand.CanExecute(null)) await PlayCommand.ExecuteAsync(null);
        }

        partial void OnSelectedChanged(InstanceItem? value)
        {
            // Sayfa açıkken başka bir pakete geçildiyse (ör. paket silindi) sayfa kapanır; aynı paketin yeniden yüklenmesi kapatmaz
            if (PageOpen && value?.Id != _pageItemId) ClosePage();
            Background = null;
            BackgroundBlurred = null;
            Logo = null;
            HasLogo = false;
            ModCount = Size = Updated = PlayTime = "–";
            Installed = null;
            OnPropertyChanged(nameof(SelectedIsCustom));
            OnPropertyChanged(nameof(SelectedCanEditLook));
            OnPropertyChanged(nameof(SelectedIsModpack));
            OnPropertyChanged(nameof(SelectedModpackVersion));
            GameSettingsSummary = "Genel ayarlar kullanılıyor";
            if (value != null && !_preview) _ = LoadGameSettingsSummaryAsync(value);
            OnPropertyChanged(nameof(CanShare));
            OnPropertyChanged(nameof(ShareText));
            if (value == null) { Refresh(); return; }

            UiState.Set("lastPack", value.Id);
            PackServer = null;
            _ = UpdatePackServerAsync(value);
            UpdatePlayingHere();
            // Pakete ayrıca seçilen RAM; yoksa Ayarlar'daki varsayılan; o da "Önerilen"se paketin önerdiği
            int fallback = RamOptions.Contains(AppSettings.Current.DefaultRamGb) ? AppSettings.Current.DefaultRamGb
                : RecommendedRamFor(value);
            RamGb = int.TryParse(UiState.Get("ram_" + value.Id), out int saved) && RamOptions.Contains(saved) ? saved : fallback;
            OnPropertyChanged(nameof(RamHint));
            OnPropertyChanged(nameof(RamWarn));
            PackUpdateText = _packUpdates.TryGetValue(value.Id, out var u) ? $"rev {u.Int("from")} → rev {u.Int("to")}" : null;
            _ = LoadVisualsAsync(value);
            _ = LoadVersionsAsync(value);
            if (!_preview) { _ = LoadDetailsAsync(value); _ = LoadInfoAsync(); }
            else Installed = true;
            Refresh();
        }

        public bool SelectedIsCustom => Selected?.IsCustom == true;

        /// <summary>Paketin oyun ayarları satırındaki özet: genel mi, pakete özel mi (neler).</summary>
        [ObservableProperty] private string _gameSettingsSummary = "Genel ayarlar kullanılıyor";

        private async Task LoadGameSettingsSummaryAsync(InstanceItem item)
        {
            try
            {
                var r = await _core.Call("instances.gameSettings", new { id = item.Id });
                if (Selected == item) GameSettingsSummary = GameSettingsModal.Summary(r);
            }
            catch { /* özet genel kalır */ }
        }

        /// <summary>Bu paketin kendi oyun ayarları: Java, ek JVM argümanları, oyun penceresi.</summary>
        [RelayCommand]
        private async Task GameSettings()
        {
            if (Selected is not { } item) return;
            JsonElement r;
            try { r = await _core.Call("instances.gameSettings", new { id = item.Id }); }
            catch (Exception ex) { await Main.AlertAsync("Ayarlar açılamadı", ex.Message); return; }
            if (await Main.ShowAsync(new GameSettingsModal(item.Id, item.Name, r)) is string summary && Selected == item) GameSettingsSummary = summary;
        }
        public bool SelectedCanEditLook => Selected?.CanEditLook == true;
        public bool SelectedIsModpack => Selected?.IsModpack == true;
        public string SelectedModpackVersion => Selected?.ModpackVersion ?? "";

        /// <summary>
        /// Modpack sürümünü değiştir: sürümler listelenir, seçilen sürüme geçilir (modlar o sürümünkilerle değişir; sonradan
        /// eklenenler ve dünyalar kalır, ayarlar önce yedeklenir). İlerleme indirme kutusunda görünür.
        /// </summary>
        [RelayCommand]
        private async Task SwitchModpack()
        {
            if (Selected is not { IsModpack: true } item || GameRunning || Launching || _switchingPack) return;
            var modal = new ModpackVersionModal(item.Name);
            _ = modal.LoadAsync(item.Id);
            if (await Main.ShowAsync(modal) is not string versionId) return;
            if (!await Main.ConfirmAsync("Sürümü değiştir", $"{item.Name} seçtiğin sürüme geçirilsin mi? Paketin modları değişir; dünyaların ve sonradan eklediğin modlar kalır, ayar klasörleri önce yedeklenir.", "Geçir")) return;
            _switchingPack = true;   // bu sırada oyun açılmaz
            AppendLog($"{item.Name} sürümü değiştiriliyor…", "accent");
            try
            {
                var r = await _core.Call("instances.switchModpack", new { id = item.Id, versionId });
                await ReloadInstancesAsync(item.Id);
                Main.ShowToast("Sürüm değişti", $"{item.Name} artık {r.Text("version")}.");
                if (r.Items("manual").Any()) await Main.ShowAsync(new ManualDownloadsModal(r.Get("manual"), item.Id));
            }
            catch (Exception ex) { await Main.AlertAsync("Sürüm değiştirilemedi", ex.Message); }
            finally { _switchingPack = false; }
        }

        private bool _switchingPack;
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
            OnPropertyChanged(nameof(RamHint));
            OnPropertyChanged(nameof(RamWarn));
        }

        partial void OnGameRunningChanged(bool value)
        {
            Views.SynthwaveBackdrop.GameRunning = value;   // oyun açıkken tema arka planı durur
            Refresh();
        }
        partial void OnLaunchingChanged(bool value) => Refresh();

        private async Task LoadVisualsAsync(InstanceItem item)
        {
            // Üçü birlikte yüklenir; bulanık zemin büyük resmi ve logoyu beklemeden gelir (sırayla yüklenince geç değişiyordu)
            var background = Images.LoadAsync(item.BackgroundUrl, 1920);
            var logo = Images.LoadAsync(item.LogoUrl, 720);
            var blurredTask = Images.LoadBlurredAsync(item.BackgroundUrl);
            _ = blurredTask.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && Selected == item) BackgroundBlurred = t.Result;
            }, TaskScheduler.FromCurrentSynchronizationContext());
            var bg = await background;
            if (Selected != item) return;
            Background = bg;
            var lg = await logo;
            if (Selected != item) return;
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
            if (_manualFor != item.Id) ManualCount = 0;   // başka paketin uyarısı kalmasın
            _manualFor = item.Id;
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
            _manual = d.Get("manual").ValueKind == JsonValueKind.Array ? d.Get("manual").Clone() : default;
            ManualCount = _manual.ValueKind == JsonValueKind.Array ? _manual.GetArrayLength() : 0;
            Refresh();
        }

        // ── Elle indirilecek modlar ──
        // İlk kurulumda açılan pencere kapatılsa da paket seçiliyken uyarısı durur; "Göster" pencereyi yeniden açar.
        private JsonElement _manual;
        private string? _manualFor;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasManual), nameof(ManualText))]
        private int _manualCount;
        public bool HasManual => ManualCount > 0;
        public string ManualText => ManualCount == 1 ? "1 mod elle indirilmeli" : $"{ManualCount} mod elle indirilmeli";

        [RelayCommand]
        private async Task OpenManual()
        {
            if (Selected is not { } item || _manual.ValueKind != JsonValueKind.Array) return;
            await Main.ShowAsync(new ManualDownloadsModal(_manual, item.Id));
            if (Selected == item) await LoadDetailsAsync(item);
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
                        Worlds.Add(new WorldTile(w.Text("name") ?? "", Names.Ago(w.Text("lastPlayed")) + (w.Items("backups").Count() is > 0 and var n ? $" · {n} yedek" : ""), w.Text("icon"),
                            GameLauncher.SupportsQuickPlay(CanPickVersion ? McVersion ?? instance.McVersion : instance.McVersion)));
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
        private Task NewInstance() => Main.OpenNewPackAsync();

        [RelayCommand]
        private async Task EditInstance()
        {
            if (Selected is not { IsCustom: true } s) return;
            if (await Main.ShowAsync(new InstanceEditorModal(s)) is string id) await ReloadInstancesAsync(id);
        }

        [RelayCommand]
        private async Task Appearance()
        {
            if (Selected is { CanEditLook: true } s) await Main.ShowAsync(new AppearanceModal(this, s));
        }

        [RelayCommand]
        private async Task AddContent()
        {
            if (Selected is { IsCustom: true } s) await Main.OpenAddContentAsync(s);
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
                await _core.Call("shortcuts.create", new { kind = "play", id = s.Id, name = s.Name, iconDataUrl = ShortcutIcon.ToDataUrl(s.Thumbnail ?? s.Poster) });
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
            await Main.Packs.UpdateByIdAsync(u.Text("packId") ?? "");
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
            if (_boot is { } boot && GameRunning)
            {
                // Oyun süreci başladı, penceresi henüz açılmadı: aşama, geçen süre ve önceki açılışa göre ilerleme
                long seconds = boot.ElapsedMs / 1000;
                StatusText = $"{boot.Phase}… {seconds} sn" + (boot.ExpectedMs > 0 ? $" / genelde {Math.Max(1, (boot.ExpectedMs + 500) / 1000)} sn" : "");
                if (boot.ExpectedMs > 0) Progress = Math.Min(95, boot.ElapsedMs * 100.0 / boot.ExpectedMs);
                else Indeterminate = true;
            }
            else if (Launching)
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

            if (!Launching && !GameRunning) _stopping = false;
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(StopText));
            PlayLabel = _stopping ? "Durduruluyor…" : Launching ? "Başlatılıyor…" : _boot != null && GameRunning ? "Açılıyor…" : GameRunning ? "Oyun çalışıyor" : !CanLaunchGame ? "Telefonda oyun yakında" : "OYNA";
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

        /// <summary>Dünya listesindeki "Oyna": oyun açılınca menüye uğramadan doğrudan o dünyaya girer.</summary>
        [RelayCommand]
        private Task PlayWorld(WorldTile world) => CanPlay ? PlayAsync(false, false, GameLauncher.WorldPrefix + world.Name) : Task.CompletedTask;

        /// <summary>Seçili paketi açar; join verilirse oyun doğrudan o sunucuya bağlanır.</summary>
        public async Task PlayAsync(bool ignoreMissing, bool ignoreFiles, string join)
        {
            if (Selected is not { } instance || GameRunning || Launching) return;
            if (_switchingPack) { AppendLog("Paketin sürümü değişiyor; bitince oynayabilirsin.", "warn"); return; }
            _lastJoin = join;
            if (!ignoreMissing && !ignoreFiles) { Console.Clear(); _lastLineWasProgress = false; }
            _activityText = "";
            Launching = true;
            // Açılış sırasında konsol görünsün (seçilen sekme hatırlanmaya devam eder)
            InfoTab = "log";
            if (!DetailsOpen) { DetailsOpen = true; _detailsAutoOpened = true; }
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
            // Oyunun kendi çıktısı biriktirilir, kısa aralıklarla topluca eklenir (launcher arkadayken daha seyrek)
            if (level is "game" or "gameError" && !replace)
            {
                _pendingLog.Add(line);
                if (line.IsError && !IsLog) LogAlert = true;
                if (_logFlush == null)
                {
                    bool front = Views.MainWindow.Current is { IsActive: true, WindowState: not Avalonia.Controls.WindowState.Minimized };
                    _logFlush = Avalonia.Threading.DispatcherTimer.RunOnce(FlushLog, TimeSpan.FromMilliseconds(front ? 120 : 1000), Avalonia.Threading.DispatcherPriority.Background);
                }
                return;
            }
            FlushLog();
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

        private readonly List<LogLine> _pendingLog = new();
        private IDisposable? _logFlush;

        private void FlushLog()
        {
            _logFlush?.Dispose();
            _logFlush = null;
            if (_pendingLog.Count == 0) return;
            Console.AppendRange(_pendingLog, MaxLogLines);
            _pendingLog.Clear();
            _lastLineWasProgress = false;
        }

        [RelayCommand]
        private void ClearConsole()
        {
            _pendingLog.Clear();
            Console.Clear();
            _lastLineWasProgress = false;
        }

        /// <summary>Oyunun açılış durumu (süreç başladı, pencere açılmadı); null: açılış bitti ya da yok.</summary>
        private sealed record BootInfo(string Phase, long ElapsedMs, long ExpectedMs);
        private BootInfo? _boot;

        public void OnCoreEvent(string name, JsonElement data)
        {
            switch (name)
            {
                case "gameBoot":
                    _boot = new BootInfo(data.Text("phase") ?? "Oyun açılıyor", (long)data.Num("elapsedMs"), (long)data.Num("expectedMs"));
                    if (GameRunning) Refresh();
                    break;
                case "gameReady":
                    _boot = null;
                    Progress = 100;
                    Refresh();
                    break;
                case "log":
                    foreach (var line in data.Items())
                        AppendLog(line.Text("text") ?? "", line.Text("level") ?? "info", line.Bool("replace"), line.Text("time"));
                    break;
                case "gameExited":
                    _boot = null;
                    GameRunning = false;
                    _infoCache.Clear();
                    InfoTab = UiState.Get("infoTab") is { } t && InfoTabs.Contains(t) ? t : "worlds";
                    if (_detailsAutoOpened && !LogAlert) DetailsOpen = false;
                    _detailsAutoOpened = false;
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
