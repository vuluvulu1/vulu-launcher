using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    internal sealed record RadioStation(string Id, string Name, string Url, string Kind, string? Country);

    internal sealed partial class StationRow(RadioViewModel owner, RadioStation station, int index, bool current, bool favorite) : ObservableObject
    {
        public RadioStation Station { get; } = station;
        public bool IsCurrent { get; } = current;
        public string Star => favorite ? "★" : "☆";
        [RelayCommand] private Task Play() => owner.PlayAtAsync(index, fromMenu: true);
        [RelayCommand] private void ToggleFavorite() => owner.ToggleFavorite(Station);
    }

    internal sealed record FavoriteRow(RadioViewModel Owner, string Genre, string Name, string GenreLabel)
    {
        public IAsyncRelayCommand Play => new AsyncRelayCommand(() => Owner.PlayFavoriteAsync(this));
    }

    /// <summary>
    /// Oyna sayfasındaki radyo: istasyonlar türe göre (radio-browser dizini; vulu Radyo'da ekibin yüklediği şarkılar).
    /// Çalmayan istasyon atlanır, art arda dört başarısızlıkta durulur. Tür, istasyon ve favoriler hatırlanır.
    /// </summary>
    internal sealed partial class RadioViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private readonly CoreClient _core = CoreClient.Instance;
        private readonly RadioPlayer _player = new();
        private List<(string Id, string Label)> _genres = new();
        private List<RadioStation> _stations = new();
        private Task? _loading;
        private int _index, _failures, _playToken;
        private IDisposable? _saveVolume;

        public RadioViewModel(MainViewModel main)
        {
            _main = main;
            _genre = UiState.Get("radioGenre") ?? "";
            _volume = AppSettings.Current.MusicVolume;
            _player.Volume = _volume;
            _player.StateChanged += Refresh;
            _player.Ended += () => { if (Current?.Kind == "song") _ = PlayAtAsync(_index + 1); };
            _player.Failed += OnFailed;
        }

        public ObservableCollection<Choice> Genres { get; } = new();
        public ObservableCollection<StationRow> Stations { get; } = new();
        public ObservableCollection<FavoriteRow> Favorites { get; } = new();

        [ObservableProperty] private string _genre;
        [ObservableProperty] private bool _playing;
        [ObservableProperty] private bool _muted;
        [ObservableProperty] private double _volume;
        [ObservableProperty] private string _title = "Radyo";
        [ObservableProperty] private string _subtitle = "Çalmak için ▶";
        [ObservableProperty] private bool _live;
        [ObservableProperty] private string _search = "";
        [ObservableProperty] private string? _menuNote;
        public bool HasFavorites => Favorites.Count > 0;
        public bool ShowSearch => _stations.Count > 8;
        private RadioStation? Current => _index < _stations.Count ? _stations[_index] : null;
        private string GenreLabel(string id) => _genres.FirstOrDefault(g => g.Id == id).Label ?? id;

        partial void OnMutedChanged(bool value) => _player.Muted = value;
        partial void OnSearchChanged(string value) => RenderStations();

        /// <summary>Ses düzeyi kaydırılınca hemen uygulanır; ayarlara kısa bir beklemeyle yazılır.</summary>
        partial void OnVolumeChanged(double value)
        {
            _player.Volume = value;
            _saveVolume?.Dispose();
            _saveVolume = DispatcherTimer.RunOnce(() => _ = SaveAsync(("musicVolume", (int)Math.Round(value))), TimeSpan.FromMilliseconds(400));
        }

        /// <summary>Girişten sonra: ayarlarda radyo açıksa çalmaya başlar.</summary>
        public void StartIfEnabled()
        {
            Volume = AppSettings.Current.MusicVolume;
            if (AppSettings.Current.MusicEnabled) _ = StartAsync();
        }

        public void Stop()
        {
            _player.Stop();
            Refresh();
        }

        private async Task SaveAsync((string Key, object Value) change)
        {
            try
            {
                var settings = JsonSerializer.SerializeToNode(AppSettings.Current, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
                settings[change.Key] = JsonSerializer.SerializeToNode(change.Value);
                await _core.Call("settings.save", settings);
            }
            catch { /* ayar kaydedilemezse çalma yine sürer */ }
        }

        /// <summary>İstasyon listesi yoksa getirir; ilk açılışta vulu Radyo'da şarkı varsa o, yoksa Lo-fi.</summary>
        private Task EnsureAsync()
        {
            if (_stations.Count > 0) return Task.CompletedTask;
            return _loading ??= Load().ContinueWith(_ => _loading = null, TaskScheduler.FromCurrentSynchronizationContext());

            async Task Load()
            {
                if (_genres.Count == 0)
                    _genres = (await _core.Call("radio.genres")).Items().Select(g => (g.Str("id") ?? "", g.Str("label") ?? "")).ToList();
                if (_genres.Any(g => g.Id == Genre)) { await LoadStationsAsync(Genre); return; }
                try { await LoadStationsAsync("vulu"); } catch { _stations = new(); }
                if (_stations.Count == 0) await LoadStationsAsync("lofi");
            }
        }

        private async Task LoadStationsAsync(string genre)
        {
            Genre = genre;
            UiState.Set("radioGenre", genre);
            _stations = (await _core.Call("radio.stations", new { genre })).Items()
                .Select(s => new RadioStation(s.Str("id") ?? "", s.Str("name") ?? "", s.Str("url") ?? "", s.Str("kind") ?? "station", s.Str("country"))).ToList();
            string? saved = UiState.Get("radioStation");
            _index = Math.Max(0, _stations.FindIndex(s => s.Id == saved));
        }

        private async Task StartAsync()
        {
            try
            {
                await EnsureAsync();
                await PlayAtAsync(_index);
            }
            catch
            {
                Title = "Radyo";
                Subtitle = "İstasyonlar alınamadı";
            }
        }

        public async Task PlayAtAsync(int index, bool fromMenu = false)
        {
            if (_stations.Count == 0) return;
            _index = (index % _stations.Count + _stations.Count) % _stations.Count;
            var station = _stations[_index];
            int token = ++_playToken;
            UiState.Set("radioStation", station.Id);
            if (fromMenu) _ = SaveAsync(("musicEnabled", true));
            Refresh();
            string url;
            try
            {
                // vulu Radyo şarkıları bir kez indirilip yerel dosyadan çalınır; istasyonlar doğrudan yayından
                url = station.Kind == "song" ? (await _core.Call("radio.songFile", new { id = station.Id })).Str("url") ?? "" : station.Url;
            }
            catch { if (token == _playToken) OnFailed(); return; }
            if (token != _playToken) return;   // bu arada başka bir istasyon seçildi
            try { _player.Play(url); }
            catch { OnFailed(); return; }
            // Sıradaki şarkı arkada hazırlanır: geçişte bekleme olmasın
            var next = _stations[(_index + 1) % _stations.Count];
            if (next.Kind == "song" && next != station) _ = _core.Call("radio.songFile", new { id = next.Id });
        }

        private void OnFailed()
        {
            if (++_failures >= 4)
            {
                _failures = 0;
                _player.Stop();
                Refresh();
                Subtitle = "İstasyon çalınamadı";
                return;
            }
            _ = PlayAtAsync(_index + 1);
        }

        private void Refresh()
        {
            Playing = _player.IsPlaying;
            if (Playing) _failures = 0;
            var station = Current;
            Title = station?.Name ?? "Radyo";
            Live = Playing && station != null && station.Kind != "song";
            Subtitle = station == null ? "Çalmak için ▶" : string.Join(" · ", new[] { GenreLabel(Genre), station.Country }.Where(x => !string.IsNullOrEmpty(x)));
        }

        [RelayCommand]
        private async Task Toggle()
        {
            bool enable = !Playing;
            if (enable) await StartAsync(); else Stop();
            _ = SaveAsync(("musicEnabled", enable));
        }

        [RelayCommand] private async Task Next() { await EnsureAsync(); await PlayAtAsync(_index + 1); }
        [RelayCommand] private async Task Prev() { await EnsureAsync(); await PlayAtAsync(_index - 1); }
        [RelayCommand] private void ToggleMute() => Muted = !Muted;

        // ── İstasyon menüsü ──────────────────────────────────────────────
        /// <summary>Menü açıldı: türler, favoriler ve o türün istasyonları.</summary>
        public async Task OpenMenuAsync()
        {
            MenuNote = "Yükleniyor…";
            try { await EnsureAsync(); }
            catch { MenuNote = "İstasyonlar alınamadı."; return; }
            RenderMenu();
        }

        private void RenderMenu()
        {
            Genres.Clear();
            foreach (var g in _genres) Genres.Add(new Choice(g.Id, g.Label) { IsSelected = g.Id == Genre });
            Favorites.Clear();
            foreach (var f in LoadFavorites()) Favorites.Add(new FavoriteRow(this, f.Genre, f.Name, GenreLabel(f.Genre)));
            OnPropertyChanged(nameof(HasFavorites));
            OnPropertyChanged(nameof(ShowSearch));
            RenderStations();
        }

        private void RenderStations()
        {
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            string q = Search.Trim().ToLower(tr);
            var favorites = LoadFavorites();
            Stations.Clear();
            for (int i = 0; i < _stations.Count; i++)
            {
                var s = _stations[i];
                if (q.Length > 0 && !s.Name.ToLower(tr).Contains(q)) continue;
                Stations.Add(new StationRow(this, s, i, i == _index, favorites.Any(f => f.Genre == Genre && f.Name == s.Name)));
            }
            MenuNote = Stations.Count > 0 ? null : q.Length > 0 ? "Bu adla istasyon yok." : Genre == "vulu" ? "vulu Radyo'da henüz şarkı yok." : "Bu türde istasyon bulunamadı.";
        }

        [RelayCommand]
        private async Task PickGenre(Choice genre)
        {
            MenuNote = "Yükleniyor…";
            try
            {
                await LoadStationsAsync(genre.Key);
                _index = 0;
                Search = "";
                RenderMenu();
                await PlayAtAsync(0, fromMenu: true);
            }
            catch { RenderMenu(); }
        }

        // Favoriler: tür ve ad olarak bu bilgisayarda saklanır; menünün en üstünde durur
        private static List<(string Genre, string Name)> LoadFavorites()
        {
            try
            {
                return JsonSerializer.Deserialize<List<JsonElement>>(UiState.Get("radioFavs") ?? "[]")!
                    .Select(f => (f.Str("genre") ?? "", f.Str("name") ?? "")).ToList();
            }
            catch { return new(); }
        }

        public void ToggleFavorite(RadioStation station)
        {
            var favs = LoadFavorites();
            int i = favs.FindIndex(f => f.Genre == Genre && f.Name == station.Name);
            if (i >= 0) favs.RemoveAt(i); else favs.Insert(0, (Genre, station.Name));
            UiState.Set("radioFavs", JsonSerializer.Serialize(favs.Take(12).Select(f => new { genre = f.Genre, name = f.Name })));
            RenderMenu();
        }

        public async Task PlayFavoriteAsync(FavoriteRow fav)
        {
            if (Genre != fav.Genre || _stations.Count == 0) await LoadStationsAsync(fav.Genre);
            int index = _stations.FindIndex(s => s.Name == fav.Name);
            if (index >= 0) await PlayAtAsync(index, fromMenu: true);
            RenderMenu();
        }
    }
}
