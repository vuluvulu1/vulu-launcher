using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;
using VuluLauncher.Views;

namespace VuluLauncher.ViewModels
{
    /// <summary>Liderlik tablosunda bir satır.</summary>
    internal sealed partial class RankRow(WorldViewModel owner, int rank, string username, string value, bool you) : ObservableObject
    {
        public int Rank { get; } = rank;
        public string Username { get; } = username;
        public string Value { get; } = value;
        public bool You { get; } = you;
        public string Medal => Rank switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", _ => Rank.ToString(CultureInfo.InvariantCulture) };
        public bool Top => Rank <= 3;
        [ObservableProperty] private Bitmap? _avatar;
        [RelayCommand] private Task Open() => owner.OpenProfileAsync(Username);
    }

    /// <summary>Bir dal: başlık ve sıralama.</summary>
    internal sealed record RankCategory(string Icon, string Name, ObservableCollection<RankRow> Rows)
    {
        public bool Empty => Rows.Count == 0;
    }

    /// <summary>Geçen haftanın birincisi (dal başına).</summary>
    internal sealed record WinnerItem(string Icon, string Category, string Username, string Value);

    /// <summary>
    /// Sosyal'deki Liderlik ve Harita bölümleri: vulu sunucusunun liderlik tablosu (haftalık, sezon, tüm zamanlar) ve canlı haritası
    /// (kendin ve arkadaşlarının yeri). Harita görünürken oyuncu konumları 10 saniyede bir yenilenir.
    /// </summary>
    internal sealed partial class WorldViewModel(MainViewModel main) : ObservableObject
    {
        private readonly CoreClient _core = CoreClient.Instance;
        private DispatcherTimer? _timer;
        private int _loadId;

        public ObservableCollection<RankCategory> Categories { get; } = new();
        public ObservableCollection<WinnerItem> Winners { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBoard), nameof(IsMap), nameof(Title), nameof(Description))]
        private string _view = "board";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsWeek), nameof(IsSeason), nameof(IsAll))]
        private string _period = "week";
        [ObservableProperty] private string _subtitle = "";
        [ObservableProperty] private string? _error;
        [ObservableProperty] private bool _loading;
        [ObservableProperty] private IReadOnlyList<MapMarker> _markers = [];
        [ObservableProperty] private string _mapStatus = "";
        [ObservableProperty] private string _hover = "";
        [ObservableProperty] private bool _mapEnabled = true;
        [ObservableProperty] private string _mapUpdated = "";

        public string Title => IsMap ? "Harita" : "Liderlik";
        public string Description => IsMap ? "vulu sunucusunun canlı haritası: sen ve arkadaşların nerede." : "vulu sunucusunda en çok oynayanlar, elmas bulanlar, ölenler ve gezenler.";
        public bool IsBoard => View == "board";
        public bool IsMap => View == "map";
        public bool IsWeek => Period == "week";
        public bool IsSeason => Period == "season";
        public bool IsAll => Period == "all";
        public bool HasWinners => Winners.Count > 0;

        /// <summary>Görünüm (WorldView) haritayı bağlayınca verilir; karoları yenilemek ve ortalamak için.</summary>
        public WorldMapControl? Map { get; set; }

        public async Task OpenAsync()
        {
            if (IsBoard) await LoadBoardAsync();
            else await OpenMapAsync();
        }

        /// <summary>Sekmeden çıkılınca harita yenilemesi durur.</summary>
        public void Leave() { _timer?.Stop(); }

        partial void OnPeriodChanged(string value) => _ = LoadBoardAsync();

        [RelayCommand] private void SetPeriod(string period) => Period = period;
        [RelayCommand] private Task Reload() => OpenAsync();

        public Task OpenProfileAsync(string username) => main.OpenProfileAsync(username);

        private async Task LoadBoardAsync()
        {
            _timer?.Stop();
            int id = ++_loadId;
            Loading = true;
            Error = null;
            JsonElement data;
            try { data = await _core.Call("progress.leaderboard", new { period = Period }); }
            catch (Exception ex) { if (id == _loadId) { Error = "Liderlik tablosu alınamadı: " + ex.Message; Loading = false; } return; }
            if (id != _loadId) return;

            var tr = CultureInfo.GetCultureInfo("tr-TR");
            Categories.Clear();
            foreach (var c in data.Items("categories"))
            {
                var rows = new ObservableCollection<RankRow>();
                foreach (var r in c.Items("rows"))
                {
                    string name = r.Text("username") ?? "";
                    var row = new RankRow(this, r.Int("rank"), name, r.Text("display") ?? "", name == main.Username);
                    rows.Add(row);
                    _ = LoadAvatarAsync(row, (long)r.Num("avatarVersion"));
                }
                Categories.Add(new RankCategory(c.Text("icon") ?? "", c.Text("name") ?? "", rows));
            }

            Winners.Clear();
            var last = data.Get("lastWeek");
            if (Period == "week" && last.ValueKind == JsonValueKind.Object)
                foreach (var w in last.Items("winners"))
                    if (w.Get("winner") is { ValueKind: JsonValueKind.Object } winner)
                        Winners.Add(new WinnerItem(w.Text("icon") ?? "", w.Text("name") ?? "", winner.Text("username") ?? "", winner.Text("display") ?? ""));
            OnPropertyChanged(nameof(HasWinners));

            string Date(string? iso, string format) =>
                DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime().ToString(format, tr) : "";
            Subtitle = Period switch
            {
                "week" => $"Bu hafta · {Date(data.Text("weekEnd"), "d MMMM dddd HH:mm")}'de sıfırlanır. Her dalın birincisi rozet ve \"Haftanın Şampiyonu\" pelerinini alır.",
                "season" => data.Get("season") is { ValueKind: JsonValueKind.Object } s ? $"{s.Text("name")} · {Date(s.Text("endsAt"), "d MMMM yyyy")} tarihinde biter." : "Şu an süren bir sezon yok.",
                _ => "vulu sunucusunda bugüne kadar.",
            };
            if (data.Text("updatedAt") is { } updated) Subtitle += $" Son güncelleme: {Names.Ago(updated)}.";
            Loading = false;
        }

        private static async Task LoadAvatarAsync(RankRow row, long version) =>
            row.Avatar = await Images.LoadAsync(Avatars.Url(row.Username, version), 64);

        // ── Harita ───────────────────────────────────────────────────────
        private bool _centered;

        private async Task OpenMapAsync()
        {
            Error = null;
            try
            {
                var info = await _core.Call("map.info");
                MapEnabled = info.Bool("enabled");
                var ow = info.Get("dims").Get("overworld");
                MapUpdated = ow.Text("updatedAt") is { } at ? "Harita güncellendi: " + Names.Ago(at) : "";
            }
            catch (Exception ex) { Error = "Harita alınamadı: " + ex.Message; return; }
            if (!MapEnabled) { MapStatus = "Harita henüz hazır değil; sunucu ilk çizimi bitirince burada görünür."; return; }
            Map?.Refresh();
            await RefreshPlayersAsync();
            _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Background, async (_, _) =>
            {
                if (IsMap && main.SelectedTab?.Page == main.Social && main.Social.View == "map") await RefreshPlayersAsync(); else _timer?.Stop();
            });
            _timer.Start();
        }

        private async Task RefreshPlayersAsync()
        {
            try
            {
                var data = await _core.Call("map.players");
                var list = data.Items("players").Where(p => (p.Text("dim") ?? "overworld") == "overworld")
                    .Select(p => new MapMarker(p.Text("name") ?? "", p.Num("x"), p.Num("z"), p.Bool("you"))).ToList();
                Markers = list;
                int online = data.Int("online");
                int elsewhere = data.Items("players").Count() - list.Count;
                MapStatus = online == 0 ? "Sunucuda şu an kimse yok."
                    : $"Sunucuda {online} kişi · haritada sen ve arkadaşların görünür" + (elsewhere > 0 ? $" ({elsewhere} kişi Nether/End'de)" : "") + ".";
                if (!_centered && list.FirstOrDefault(m => m.You) is { } me) { Map?.CenterOn(me.X, me.Z, 1); _centered = true; }
            }
            catch (Exception ex) { MapStatus = "Oyuncular alınamadı: " + ex.Message; }
        }

        [RelayCommand]
        private void CenterOnMe()
        {
            if (Markers.FirstOrDefault(m => m.You) is { } me) Map?.CenterOn(me.X, me.Z, 1);
            else Map?.CenterOn(0, 0, 0.5);
        }

        [RelayCommand]
        private void CenterOn(MapMarker marker) => Map?.CenterOn(marker.X, marker.Z, 1);

        /// <summary>Karo: Core'dan (oturumla) base64 PNG.</summary>
        public async Task<Bitmap?> LoadTileAsync(int zoom, int x, int z)
        {
            var r = await _core.Call("map.tile", new { dim = "overworld", zoom, x, z });
            if (r.ValueKind != JsonValueKind.Object || r.Text("data") is not { } b64) return null;
            using var stream = new MemoryStream(Convert.FromBase64String(b64));
            return new Bitmap(stream);
        }
    }
}
