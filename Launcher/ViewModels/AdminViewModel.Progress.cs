using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>Düzenlenen görev (başarım): ad, açıklama, simge, saydığı istatistik ve hedef.</summary>
    internal sealed partial class QuestEdit(SeasonEdit season) : ObservableObject
    {
        [ObservableProperty] private string _key = "";
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _desc = "";
        [ObservableProperty] private string _icon = "★";
        [ObservableProperty] private Option? _stat;
        [ObservableProperty] private decimal? _target = 1;
        public ObservableCollection<Option> Stats => season.Owner.QuestStats;
        [RelayCommand] private void Remove() => season.Quests.Remove(this);
        [RelayCommand] private void MoveUp() { int i = season.Quests.IndexOf(this); if (i > 0) season.Quests.Move(i, i - 1); }
    }

    /// <summary>Düzenlenen sezon: ad, tarihler, sezon pelerini ve görevleri.</summary>
    internal sealed partial class SeasonEdit(AdminViewModel owner) : ObservableObject
    {
        public AdminViewModel Owner { get; } = owner;
        public ObservableCollection<QuestEdit> Quests { get; } = new();
        public ObservableCollection<Option> Capes => Owner.CapeOptions;
        [ObservableProperty] private string _key = "";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Title))]
        private string _name = "";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Title))]
        private DateTime? _start;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Title))]
        private DateTime? _end;
        [ObservableProperty] private Option? _cape;
        [ObservableProperty] private decimal? _capeLevel = 10;
        [ObservableProperty] private bool _isOpen = true;

        public string Title
        {
            get
            {
                var tr = L.Culture;
                string range = Start is { } s && End is { } e ? $"{s.ToString("d MMM yyyy", tr)} – {e.ToString("d MMM yyyy", tr)}" : "tarih yok";
                string state = Start is { } a && End is { } b ? (DateTime.Today < a ? "yaklaşan" : DateTime.Today < b ? "sürüyor" : "bitti") : "";
                return $"{(Name.Length > 0 ? Name : "Yeni sezon")} · {range}{(state.Length > 0 ? " · " + state : "")}";
            }
        }

        [RelayCommand]
        private void AddQuest() => Quests.Add(new QuestEdit(this) { Name = "Yeni görev", Icon = "★", Stat = Owner.QuestStats.FirstOrDefault(), Target = 10 });
        [RelayCommand] private void Toggle() => IsOpen = !IsOpen;
        [RelayCommand] private Task Remove() => Owner.RemoveSeasonAsync(this);
    }

    /// <summary>Haftalık liderlik dalı: istatistiği sabit; adı, rozet adı, simgesi ve açık/kapalı düzenlenir.</summary>
    internal sealed partial class WeeklyEdit(string key) : ObservableObject
    {
        public string Key { get; } = key;
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _badgeName = "";
        [ObservableProperty] private string _icon = "★";
        [ObservableProperty] private bool _enabled = true;
    }

    /// <summary>Admin paneli › İlerleme: sezonlar, görevler, seviye eğrisi ve XP katsayıları, haftalık liderlik.</summary>
    internal sealed partial class AdminViewModel
    {
        public ObservableCollection<SeasonEdit> Seasons { get; } = new();
        public ObservableCollection<WeeklyEdit> Weekly { get; } = new();
        public ObservableCollection<Option> QuestStats { get; } = new();
        public ObservableCollection<Option> CapeOptions { get; } = new();

        public bool IsProgress => Tab == "progress";
        partial void OnTabChanged(string value) { OnPropertyChanged(nameof(IsProgress)); OnPropertyChanged(nameof(IsCapes)); }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LevelPreview))]
        private decimal? _levelBase = 50;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LevelPreview))]
        private decimal? _xpMinute = 1;
        [ObservableProperty] private decimal? _xpDiamond = 15;
        [ObservableProperty] private decimal? _xpAdvancement = 25;
        [ObservableProperty] private decimal? _xpKm = 4;
        [ObservableProperty] private decimal? _xpMobs10 = 1;
        [ObservableProperty] private bool _weeklyEnabled = true;
        // Oyunda seviye öneki (tab listesi, sohbet, isim)
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LevelTagPreview))]
        private bool _levelTagEnabled = true;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LevelTagPreview))]
        private string _levelTagFormat = "[Sv {level}] ";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LevelTagBrush))]
        private Option? _levelTagColor;
        public ObservableCollection<Option> LevelTagColors { get; } = new(new[]
        {
            new Option("gold", "Altın"), new Option("yellow", "Sarı"), new Option("aqua", "Camgöbeği"), new Option("green", "Yeşil"),
            new Option("light_purple", "Pembe"), new Option("red", "Kırmızı"), new Option("white", "Beyaz"), new Option("gray", "Gri"),
            new Option("dark_aqua", "Koyu camgöbeği"), new Option("dark_purple", "Mor"),
        });
        /// <summary>Oyundaki görünüşün örneği (tab listesi / sohbet).</summary>
        public string LevelTagPreview => LevelTagEnabled ? $"{LevelTagFormat.Replace("{level}", "12")}vuluvulu" : "Kapalı: oyunda önek görünmez.";
        /// <summary>Minecraft renginin yaklaşık karşılığı (önizleme).</summary>
        public Avalonia.Media.IBrush LevelTagBrush => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse((LevelTagColor?.Key ?? "gold") switch
        {
            "yellow" => "#FFFF55", "aqua" => "#55FFFF", "green" => "#55FF55", "light_purple" => "#FF55FF", "red" => "#FF5555",
            "white" => "#FFFFFF", "gray" => "#AAAAAA", "dark_aqua" => "#00AAAA", "dark_purple" => "#AA00AA", _ => "#FFAA00",
        }));
        [ObservableProperty] private Option? _weeklyCape;
        [ObservableProperty] private string? _progressNote;
        [ObservableProperty] private bool _progressOk;
        [ObservableProperty] private bool _progressSaving;

        private static readonly Option NoCape = new("", "Pelerin yok");

        /// <summary>Seviye eğrisinin okunur özeti: kaç XP ve yalnızca oynayarak kaç saat.</summary>
        public string LevelPreview
        {
            get
            {
                double b = (double)(LevelBase ?? 50), perMin = Math.Max(0.0001, (double)(XpMinute ?? 1));
                string Line(int l) { double xp = b * l * (l + 1); return L.F("Seviye {0}: {1:N0} XP (yalnızca oynayarak ~{2:0.#} saat)", l, xp, xp / perMin / 60); }
                return string.Join("\n", new[] { 1, 5, 10, 20, 50 }.Select(Line));
            }
        }

        private async Task LoadProgressConfigAsync()
        {
            var r = await _core.Call("admin.progressConfig");
            QuestStats.Clear();
            foreach (var s in r.Items("stats")) QuestStats.Add(new Option(s.Text("key") ?? "", s.Text("label") ?? ""));
            CapeOptions.Clear();
            CapeOptions.Add(NoCape);
            foreach (var c in r.Items("capes")) CapeOptions.Add(new Option(c.Text("key") ?? "", c.Text("name") ?? ""));

            var config = r.Get("config");
            LevelBase = (decimal)config.Num("levelBase", 50);
            var tag = config.Get("levelTag");
            LevelTagEnabled = tag.Get("enabled").ValueKind != JsonValueKind.False;
            LevelTagFormat = tag.Text("format") ?? "[Sv {level}] ";
            LevelTagColor = LevelTagColors.FirstOrDefault(c => c.Key == tag.Text("color")) ?? LevelTagColors[0];
            var xp = config.Get("xp");
            XpMinute = (decimal)xp.Num("minute", 1);
            XpDiamond = (decimal)xp.Num("diamond", 15);
            XpAdvancement = (decimal)xp.Num("advancement", 25);
            XpKm = (decimal)xp.Num("km", 4);
            XpMobs10 = (decimal)xp.Num("mobs10", 1);

            var weekly = config.Get("weekly");
            WeeklyEnabled = weekly.Get("enabled").ValueKind != JsonValueKind.False;
            WeeklyCape = CapeOptions.FirstOrDefault(c => c.Key == (weekly.Text("cape") ?? "")) ?? NoCape;
            Weekly.Clear();
            foreach (var w in weekly.Items("categories"))
                Weekly.Add(new WeeklyEdit(w.Text("key") ?? "") { Name = w.Text("name") ?? "", BadgeName = w.Text("badgeName") ?? "", Icon = w.Text("icon") ?? "★", Enabled = w.Get("enabled").ValueKind != JsonValueKind.False });

            Seasons.Clear();
            foreach (var s in config.Items("seasons"))
            {
                var season = new SeasonEdit(this)
                {
                    Key = s.Text("key") ?? "", Name = s.Text("name") ?? "",
                    Start = LocalDate(s.Text("startsAt")), End = LocalDate(s.Text("endsAt")),
                    Cape = CapeOptions.FirstOrDefault(c => c.Key == (s.Text("cape") ?? "")) ?? NoCape,
                    CapeLevel = (decimal)s.Num("capeLevel"),
                    IsOpen = false,
                };
                foreach (var q in s.Items("quests"))
                    season.Quests.Add(new QuestEdit(season)
                    {
                        Key = q.Text("key") ?? "", Name = q.Text("name") ?? "", Desc = q.Text("desc") ?? "", Icon = q.Text("icon") ?? "★",
                        Stat = QuestStats.FirstOrDefault(x => x.Key == q.Text("stat")), Target = (decimal)q.Num("target"),
                    });
                Seasons.Add(season);
            }
            // Süren (yoksa en son) sezon açık gelir
            var open = Seasons.FirstOrDefault(s => s.Start <= DateTime.Today && DateTime.Today < s.End) ?? Seasons.LastOrDefault();
            if (open != null) open.IsOpen = true;
            ProgressNote = null;
        }

        /// <summary>Sunucunun tarihi (UTC) → Türkiye takvim günü.</summary>
        private static DateTime? LocalDate(string? iso) =>
            DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToOffset(TimeSpan.FromHours(3)).Date : null;

        /// <summary>Takvim günü → o günün 00:00'ı, Türkiye saatiyle.</summary>
        private static string? IsoDate(DateTime? day) =>
            day is { } d ? new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.FromHours(3)).ToString("o", CultureInfo.InvariantCulture) : null;

        [RelayCommand]
        private void AddSeason()
        {
            var last = Seasons.Where(s => s.End != null).OrderBy(s => s.End).LastOrDefault();
            DateTime start = last?.End ?? DateTime.Today;
            int n = Seasons.Count + 1;
            while (Seasons.Any(s => s.Key == $"s{n}")) n++;
            foreach (var s in Seasons) s.IsOpen = false;
            var season = new SeasonEdit(this) { Key = $"s{n}", Name = $"Sezon {n}", Start = start, End = start.AddDays(90), Cape = NoCape, CapeLevel = 10 };
            Seasons.Add(season);
        }

        public async Task RemoveSeasonAsync(SeasonEdit season)
        {
            if (!await main.ConfirmAsync("Sezonu sil", L.F("\"{0}\" ve görevleri silinsin mi? Kazanılmış rozetler ve pelerinler oyuncularda kalır. Değişiklik \"Kaydet\"e basınca geçerli olur.", season.Name), "Sil", danger: true)) return;
            Seasons.Remove(season);
        }

        /// <summary>Görev anahtarı boşsa addan üretilir (ör. "Elmas avcısı" → elmas-avcisi).</summary>
        private static string Slug(string text)
        {
            var map = new Dictionary<char, string> { ['ç'] = "c", ['ğ'] = "g", ['ı'] = "i", ['İ'] = "i", ['ö'] = "o", ['ş'] = "s", ['ü'] = "u", ['â'] = "a", ['î'] = "i", ['û'] = "u" };
            var sb = new StringBuilder();
            foreach (char ch in text.ToLower(L.Culture))
            {
                if (map.TryGetValue(ch, out var r)) sb.Append(r);
                else if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
                else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }
            string s = sb.ToString().Trim('-');
            return s.Length > 32 ? s[..32].Trim('-') : s;
        }

        [RelayCommand]
        private async Task SaveProgress()
        {
            ProgressOk = false;
            ProgressSaving = true;
            ProgressNote = "Kaydediliyor…";
            try
            {
                foreach (var s in Seasons)
                    foreach (var q in s.Quests)
                        if (q.Key.Trim().Length == 0) q.Key = Slug(q.Name) is { Length: > 0 } k ? k : "gorev-" + (s.Quests.IndexOf(q) + 1);
                var config = new
                {
                    levelBase = LevelBase ?? 50,
                    levelTag = new { enabled = LevelTagEnabled, format = LevelTagFormat, color = LevelTagColor?.Key ?? "gold" },
                    xp = new { minute = XpMinute ?? 0, diamond = XpDiamond ?? 0, advancement = XpAdvancement ?? 0, km = XpKm ?? 0, mobs10 = XpMobs10 ?? 0 },
                    weekly = new
                    {
                        enabled = WeeklyEnabled,
                        cape = WeeklyCape?.Key ?? "",
                        categories = Weekly.Select(w => new { key = w.Key, name = w.Name, badgeName = w.BadgeName, icon = w.Icon, enabled = w.Enabled }),
                    },
                    seasons = Seasons.Select(s => new
                    {
                        key = s.Key.Trim().ToLowerInvariant(), name = s.Name.Trim(), startsAt = IsoDate(s.Start), endsAt = IsoDate(s.End),
                        cape = s.Cape?.Key ?? "", capeLevel = s.CapeLevel ?? 0,
                        quests = s.Quests.Select(q => new { key = q.Key.Trim().ToLowerInvariant(), name = q.Name.Trim(), desc = q.Desc.Trim(), icon = q.Icon.Trim(), stat = q.Stat?.Key ?? "", target = q.Target ?? 0 }),
                    }),
                };
                await _core.Call("admin.saveProgressConfig", new { config });
                ProgressOk = true;
                ProgressNote = "Kaydedildi ✓ Oyuncuların ilerlemesi bir sonraki turda (en çok 5 dk) yeni ayarla hesaplanır.";
            }
            catch (Exception ex) { ProgressNote = ex.Message; }
            finally { ProgressSaving = false; }
        }

        [RelayCommand] private Task ReloadProgress() => LoadProgressConfigAsync();
    }
}
