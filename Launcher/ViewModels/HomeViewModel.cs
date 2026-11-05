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
        public int RecommendedRamGb { get; init; }
        public string? LogoUrl { get; init; }
        public string? BackgroundUrl { get; init; }
        public string? ThumbnailUrl { get; init; }

        public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
        public string LoaderLabel => Loader switch
        {
            "vanilla" => "Vanilla",
            "fabric" => "Fabric",
            "forge" => "Forge",
            "neoforge" => "NeoForge",
            "quilt" => "Quilt",
            _ => Loader,
        };
        public string Subtitle => Kind == "server" ? Name : $"{LoaderLabel} {McVersion}";

        [ObservableProperty] private Bitmap? _thumbnail;

        public static InstanceItem From(JsonElement e) => new()
        {
            Id = e.Str("id") ?? "",
            Kind = e.Str("kind") ?? "custom",
            Name = e.Str("name") ?? "",
            Description = e.Str("description") ?? "",
            McVersion = e.Str("mcVersion") ?? "",
            Loader = e.Str("loader") ?? "",
            RecommendedRamGb = e.Int("recommendedRamGb", 4),
            LogoUrl = e.Str("logo"),
            BackgroundUrl = e.Str("background"),
            // Kişisel instance'ın küçük resmi yoksa Modrinth/CurseForge simgesi kullanılır
            ThumbnailUrl = e.Str("thumbnail") ?? e.Str("iconUrl"),
        };
    }

    /// <summary>Konsoldaki satır.</summary>
    internal sealed record LogLine(string Time, string Text, string Level)
    {
        public bool IsWarn => Level == "warn";
        public bool IsError => Level is "error" or "gameError";
    }

    /// <summary>
    /// Oyna sayfası: paket seçimi, seçili paketin arka planı ve bilgileri, alt çubuktaki RAM ve OYNA düğmesi, konsol.
    /// </summary>
    internal sealed partial class HomeViewModel : ObservableObject
    {
        private const int MaxLogLines = 600;
        private readonly CoreClient _core = CoreClient.Instance;
        private bool _lastLineWasProgress;
        private int _detailsSeq;
        private bool _preview;   // geliştirme önizlemesi: Core'a sorulmaz

        public ObservableCollection<InstanceItem> Instances { get; } = new();
        public ObservableCollection<LogLine> Console { get; } = new();
        public ObservableCollection<int> RamOptions { get; } = new();

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
        [ObservableProperty] private bool _gameRunning;
        [ObservableProperty] private bool _launching;
        [ObservableProperty] private bool _canLaunchGame = true;

        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private double _progress;
        [ObservableProperty] private bool _indeterminate;
        [ObservableProperty] private string _playLabel = "OYNA";
        [ObservableProperty] private bool _canPlay;

        private string _activityText = "";

        public void ApplyInit(JsonElement init)
        {
            CanLaunchGame = init.Get("canLaunchGame").ValueKind != JsonValueKind.False;
            RamOptions.Clear();
            foreach (var gb in init.Items("ramOptionsGb")) RamOptions.Add(gb.GetInt32());
        }

        /// <summary>Girişten sonra: paketler listelenir, son seçilen paket seçilir.</summary>
        public async Task LoadAsync()
        {
            Console.Clear();
            _lastLineWasProgress = false;
            await ReloadInstancesAsync(UiState.Get("lastPack"));
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

        private async Task ReloadInstancesAsync(string? selectId)
        {
            JsonElement list;
            try { list = await _core.Call("instances.list"); }
            catch (Exception ex) { AppendLog(ex.Message, "error"); return; }

            Instances.Clear();
            foreach (var e in list.Items())
            {
                var item = InstanceItem.From(e);
                Instances.Add(item);
                _ = LoadThumbnailAsync(item);
            }
            Selected = Instances.FirstOrDefault(i => i.Id == selectId) ?? Instances.FirstOrDefault();
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
            if (value == null) { Refresh(); return; }

            UiState.Set("lastPack", value.Id);
            // Pakete ayrıca seçilen RAM; yoksa Ayarlar'daki varsayılan; o da "Önerilen"se paketin önerdiği
            int fallback = RamOptions.Contains(AppSettings.Current.DefaultRamGb) ? AppSettings.Current.DefaultRamGb
                : Math.Max(RamOptions.FirstOrDefault(4), RamOptions.LastOrDefault(gb => gb <= value.RecommendedRamGb));
            RamGb = int.TryParse(UiState.Get("ram_" + value.Id), out int saved) && RamOptions.Contains(saved) ? saved : fallback;
            _ = LoadVisualsAsync(value);
            if (!_preview) _ = LoadDetailsAsync(value);
            else Installed = true;
            Refresh();
        }

        partial void OnRamGbChanged(int value)
        {
            if (Selected != null) UiState.Set("ram_" + Selected.Id, value.ToString(CultureInfo.InvariantCulture));
        }

        partial void OnGameRunningChanged(bool value) => Refresh();
        partial void OnLaunchingChanged(bool value) => Refresh();

        private async Task LoadVisualsAsync(InstanceItem item)
        {
            var background = Images.LoadAsync(item.BackgroundUrl ?? "assets/launchervanillabg.jpg", 1920);
            var logo = Images.LoadAsync(item.LogoUrl, 720);
            var bg = await background;
            var lg = await logo;
            if (Selected != item) return;
            Background = bg;
            Logo = lg;
            HasLogo = lg != null;
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
            Updated = d.Str("updatedAt") is { } u && DateTime.TryParse(u, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
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
        private async Task Play() => await PlayAsync(false, false);

        private async Task PlayAsync(bool ignoreMissing, bool ignoreFiles)
        {
            if (Selected is not { } instance || GameRunning || Launching) return;
            if (!ignoreMissing && !ignoreFiles) { Console.Clear(); _lastLineWasProgress = false; }
            _activityText = "";
            Launching = true;
            JsonElement result = default;
            try
            {
                result = await _core.Call("game.play", new
                {
                    instanceId = instance.Id,
                    ramGb = RamGb,
                    mcVersion = "",
                    ignoreMissing,
                    ignoreFiles,
                    join = "",
                });
                GameRunning = result.Bool("started");
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message, "error");
            }
            Launching = false;

            // Sorunlu mod dosyaları ve eksik bağımlılıklar için pencereler henüz native'e taşınmadı:
            // ne olduğu konsola yazılır, kullanıcı yine de açmayı seçebilir (bir sonraki adımda pencere gelecek)
            if (result.Get("files").ValueKind == JsonValueKind.Object)
                AppendLog("Mod dosyalarında sorun bulundu (boş, bozuk ya da kopya dosya). Ayrıntı penceresi yakında; şimdilik oyun bu denetim atlanarak açılabilir.", "warn");
            if (result.Items("missing").Any())
                AppendLog("Eksik bağımlılıklar: " + string.Join(", ", result.Items("missing").Select(m => m.Str("name") ?? m.Str("id") ?? "?")), "warn");
            if (!result.Bool("started") && result.Str("error") is { Length: > 0 } error)
                AppendLog(error, "error");
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

            if (level != "game" && level != "gameError" && Launching)
            {
                _activityText = text;
                Refresh();
            }
        }

        public void OnCoreEvent(string name, JsonElement data)
        {
            switch (name)
            {
                case "log":
                    foreach (var line in data.Items())
                        AppendLog(line.Str("text") ?? "", line.Str("level") ?? "info", line.Bool("replace"), line.Str("time"));
                    break;
                case "gameExited":
                    GameRunning = false;
                    if (Selected is { } s) _ = LoadDetailsAsync(s);
                    break;
                case "gameCrashed":
                    AppendLog("Oyun çöktü. " + (data.Str("summary") ?? data.Str("title") ?? ""), "error");
                    break;
                case "logosChanged":
                    _ = ReloadInstancesAsync(Selected?.Id);
                    break;
            }
        }
    }
}
