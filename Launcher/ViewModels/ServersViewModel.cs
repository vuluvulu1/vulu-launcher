using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>Bir kişinin çevrimiçi durumu (Sunucular'daki arkadaş listesi, Sosyal).</summary>
    internal sealed partial class PersonItem : ObservableObject
    {
        public PersonItem(string username, long avatarVersion)
        {
            Username = username;
            _ = LoadAvatar(avatarVersion);
        }

        public string Username { get; }
        public string Initial => Username.Length > 0 ? Username[..1].ToUpperInvariant() : "?";
        [ObservableProperty] private Bitmap? _avatar;
        /// <summary>"playing", "online" ya da null (çevrimdışı).</summary>
        [ObservableProperty] private string? _state;
        [ObservableProperty] private string? _game;
        [ObservableProperty] private string? _rest;
        public bool IsPlaying => State == "playing";
        public bool IsOnline => State != null;
        public bool HasGame => !string.IsNullOrEmpty(Game);
        partial void OnStateChanged(string? value) { OnPropertyChanged(nameof(IsPlaying)); OnPropertyChanged(nameof(IsOnline)); }
        partial void OnGameChanged(string? value) => OnPropertyChanged(nameof(HasGame));

        private async Task LoadAvatar(long version) => Avatar = await Images.LoadAsync(Avatars.Url(Username, version), 96);
    }

    /// <summary>Sunucular sayfasındaki kart: oyundaki sunucu listesi gibi simge, ad, oyuncu sayısı, renkli MOTD.</summary>
    internal sealed partial class ServerCard : ObservableObject
    {
        private readonly ServersViewModel _owner;

        public ServerCard(ServersViewModel owner, JsonElement s, string packLabel)
        {
            _owner = owner;
            Id = s.Str("id") ?? "";
            Name = s.Str("name") ?? "";
            Address = s.Str("address") ?? "";
            Description = s.Str("description") ?? "";
            PackId = s.Str("packId");
            McVersion = s.Str("mcVersion") ?? "";
            InstanceId = s.Str("instanceId");
            PackLabel = packLabel;
            _motdFallback = Description.Length > 0 ? Description : "Durum denetleniyor…";
        }

        public string Id { get; }
        public string Name { get; }
        public string Address { get; }
        public string Description { get; }
        public string? PackId { get; }
        public string McVersion { get; }
        public string? InstanceId { get; }
        public string PackLabel { get; }
        public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
        public string JoinText => InstanceId != null ? "Bağlan" : "Paketi kur ve bağlan";
        public bool IsAdmin => _owner.IsAdmin;

        [ObservableProperty] private Bitmap? _favicon;
        /// <summary>"checking", "online", "offline".</summary>
        [ObservableProperty] private string _state = "checking";
        [ObservableProperty] private string _players = "";
        [ObservableProperty] private string _sample = "";
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private IReadOnlyList<RichPart>? _motd;
        [ObservableProperty] private string _motdFallback;
        /// <summary>Sunucunun kendi MOTD'si göründüğünde admin açıklaması altta ayrıca durur.</summary>
        [ObservableProperty] private bool _showDescription;
        [ObservableProperty] private string? _friendsHere;
        public bool IsOnline => State == "online";
        public bool IsOffline => State == "offline";
        public bool HasMotd => Motd is { Count: > 0 };
        public bool HasFriendsHere => FriendsHere != null;
        partial void OnStateChanged(string value) { OnPropertyChanged(nameof(IsOnline)); OnPropertyChanged(nameof(IsOffline)); }
        partial void OnMotdChanged(IReadOnlyList<RichPart>? value) => OnPropertyChanged(nameof(HasMotd));
        partial void OnFriendsHereChanged(string? value) => OnPropertyChanged(nameof(HasFriendsHere));

        public async Task PingAsync()
        {
            JsonElement st;
            try { st = await CoreClient.Instance.Call("community.ping", new { address = Address }); }
            catch { st = default; }
            bool online = st.Bool("online");
            State = online ? "online" : "offline";
            Players = online ? $"{st.Int("players")}/{st.Int("maxPlayers")}" : "";
            Sample = string.Join(", ", st.Items("sample").Select(x => x.GetString()));
            Status = online ? string.Join(" · ", new[] { st.Str("version"), $"{st.Int("ping")} ms" }.Where(x => !string.IsNullOrEmpty(x))) : "Kapalı ya da ulaşılamıyor";
            if (Favicon == null && st.Str("favicon") is { } fav) Favicon = await Images.LoadAsync(fav, 64);
            if (online && st.Items("motdParts").Any())
            {
                Motd = RichPart.FromMotd(st.Get("motdParts"));
                ShowDescription = Description.Length > 0;
            }
            else if (!online)
            {
                Motd = null;
                MotdFallback = Description.Length > 0 ? Description : "Sunucu şu an kapalı.";
            }
        }

        [RelayCommand] private Task Join() => _owner.JoinAsync(this);
        [RelayCommand] private Task CopyAddress() => _owner.CopyAsync(Address);
        [RelayCommand] private Task Shortcut() => _owner.ShortcutAsync(this);
        [RelayCommand] private Task Edit() => _owner.EditAsync(this);
        [RelayCommand] private Task Invite() => _owner.InviteAsync(this);
        [RelayCommand] private Task Delete() => _owner.DeleteAsync(this);
    }

    /// <summary>
    /// Sunucular: vulu sunucuları (canlı durum, "Bağlan" doğru paketi açıp oyunu sunucuya sokar) ve çevrimiçi arkadaşlar.
    /// Sayfa açıkken durumlar 30 saniyede bir yenilenir.
    /// </summary>
    internal sealed partial class ServersViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private readonly CoreClient _core = CoreClient.Instance;
        private readonly DispatcherTimer _timer;
        private bool _loading;
        private JsonElement _presence;

        public ServersViewModel(MainViewModel main)
        {
            _main = main;
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, async (_, _) =>
            {
                if (_main.SelectedTab?.Page != this) { _timer!.Stop(); return; }
                await RefreshPresenceAsync();
                foreach (var card in Servers) _ = card.PingAsync();
            });
        }

        public ObservableCollection<ServerCard> Servers { get; } = new();
        public ObservableCollection<PersonItem> Online { get; } = new();
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private bool _isEmpty;
        [ObservableProperty] private string _onlineCount = "";
        [ObservableProperty] private bool _nobodyOnline;
        public bool IsAdmin => _main.IsAdmin;

        [RelayCommand]
        public async Task LoadAsync()
        {
            if (_loading) return;
            _loading = true;
            Status = "";
            OnPropertyChanged(nameof(IsAdmin));
            try
            {
                var serversTask = _core.Call("community.servers");
                var packsTask = _core.Call("packs.list");
                await RefreshPresenceAsync();
                var servers = await serversTask;
                JsonElement packs = default;
                try { packs = await packsTask; } catch { /* paket adları yerine "vulu paketi" yazar */ }

                Servers.Clear();
                foreach (var s in servers.Items())
                {
                    string? packId = s.Str("packId");
                    string label = packId != null
                        ? packs.Items("packs").FirstOrDefault(p => p.Str("id") == packId).Str("name") ?? "vulu paketi"
                        : "Vanilla" + (s.Str("mcVersion") is { Length: > 0 } v ? " " + v : "");
                    Servers.Add(new ServerCard(this, s, label));
                }
                IsEmpty = Servers.Count == 0;
                MarkFriendsHere();
                foreach (var card in Servers) _ = card.PingAsync();
                _timer.Start();
            }
            catch (Exception ex) { Status = "Sunucular alınamadı: " + ex.Message; }
            finally { _loading = false; }
        }

        private async Task RefreshPresenceAsync()
        {
            try { _presence = await _core.Call("community.presence"); }
            catch { return; /* sunucuya ulaşılamazsa eski liste kalır */ }
            var members = await _main.MembersAsync();
            var others = _presence.Items().Where(p => p.Str("username") != _main.Username)
                .OrderBy(p => p.Str("state") == "playing" ? 0 : 1).ThenBy(p => p.Str("username"), StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true)).ToList();
            Online.Clear();
            foreach (var p in others)
            {
                string name = p.Str("username") ?? "";
                var person = new PersonItem(name, members.TryGetValue(name, out long v) ? v : 0) { State = p.Str("state") };
                (person.Game, person.Rest) = Describe(p);
                Online.Add(person);
            }
            OnlineCount = Online.Count > 0 ? $"{Online.Count} çevrimiçi" : "";
            NobodyOnline = Online.Count == 0;
            MarkFriendsHere();
        }

        /// <summary>Oyundaysa ("AGLR SMP", "sunucuda · 1 sa 12 dk"); değilse (null, "Çevrimiçi").</summary>
        public (string? Game, string Detail) Describe(JsonElement p)
        {
            if (p.Str("state") != "playing") return (null, "Çevrimiçi");
            string game = p.Str("instance") ?? "Minecraft";
            string? address = p.Str("server");
            var server = address != null ? Servers.FirstOrDefault(s => s.Address == address) : null;
            string where = server != null && server.Name != game ? server.Name : address != null ? "sunucuda" : "";
            string played = "";
            if (p.Str("since") is { } since && DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at))
            {
                int minutes = (int)(DateTime.UtcNow - at.ToUniversalTime()).TotalMinutes;
                played = minutes < 1 ? "" : minutes < 60 ? $"{minutes} dk" : $"{minutes / 60} sa {minutes % 60} dk";
            }
            return (game, string.Join(" · ", new[] { where, played }.Where(x => x.Length > 0)));
        }

        private void MarkFriendsHere()
        {
            foreach (var card in Servers)
            {
                var here = _presence.Items().Where(p => p.Str("state") == "playing" && p.Str("server") == card.Address).Select(p => p.Str("username")).ToList();
                card.FriendsHere = here.Count > 0 ? $"{here.Count} arkadaş burada" : null;
            }
        }

        /// <summary>Bağlan: doğru instance seçilir (gerekirse paket kurulur) ve oyun doğrudan sunucuya girer.</summary>
        public async Task JoinAsync(ServerCard s)
        {
            string? instanceId = s.InstanceId;
            if (instanceId == null && s.PackId != null)
            {
                try { instanceId = (await _core.Call("packs.install", new { id = s.PackId })).Str("id"); }
                catch (Exception ex) { await _main.AlertAsync("Paket kurulamadı", ex.Message); return; }
            }
            await _main.ShowHomeAsync(instanceId);
            if (_main.Home.Selected?.Id != instanceId) { await _main.AlertAsync("Bağlanılamıyor", "Bu sunucunun instance'ı bulunamadı."); return; }
            await _main.Home.PlayAsync(false, false, s.Address);
        }

        public async Task CopyAsync(string text)
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } w }
                && w.Clipboard is { } clipboard)
            {
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
                _main.ShowToast("Kopyalandı", text);
            }
        }

        public async Task ShortcutAsync(ServerCard s)
        {
            try
            {
                await _core.Call("shortcuts.create", new { kind = "join", id = s.Id, name = s.Name, iconDataUrl = "" });
                await _main.AlertAsync("Kısayol eklendi", $"Masaüstündeki \"{s.Name}\" kısayolu launcher'ı açıp doğrudan {s.Name} sunucusuna bağlanır.");
            }
            catch (Exception ex) { await _main.AlertAsync("Kısayol oluşturulamadı", ex.Message); }
        }

        [RelayCommand] private Task Add() => EditAsync(null);

        /// <summary>"Davet et": sunucu kartı mesajla bir arkadaşa gönderilir.</summary>
        public Task InviteAsync(ServerCard s) =>
            _main.Social.ShareCardAsync(new { type = "server", id = s.Id, name = s.Name, address = s.Address }, $"{s.Name} davetini gönder");

        public async Task EditAsync(ServerCard? s)
        {
            var modal = new ServerEditorModal(s);
            _ = modal.LoadPacksAsync();
            if (await _main.ShowAsync(modal) is true) await LoadAsync();
        }

        public async Task DeleteAsync(ServerCard s)
        {
            if (!await _main.ConfirmAsync("Sunucuyu sil", $"'{s.Name}' listeden kaldırılsın mı? Sunucunun kendisine dokunulmaz.", "Sil", danger: true)) return;
            try { await _core.Call("servers.delete", new { id = s.Id }); }
            catch (Exception ex) { await _main.AlertAsync("Silinemedi", ex.Message); }
            await LoadAsync();
        }
    }

    /// <summary>Admin: sunucu ekle / düzenle. Paket seçilirse oyuncular o paketin instance'ıyla bağlanır; yoksa Vanilla.</summary>
    internal sealed partial class ServerEditorModal(ServerCard? server) : ModalViewModel
    {
        public string Title => server != null ? "Sunucuyu düzenle" : "Sunucu ekle";
        public ObservableCollection<Option> Packs { get; } = new() { new Option("", "Vanilla (paket yok)") };
        [ObservableProperty] private string _name = server?.Name ?? "";
        [ObservableProperty] private string _address = server?.Address ?? "";
        [ObservableProperty] private string _description = server?.Description ?? "";
        [ObservableProperty] private string _mcVersion = server?.McVersion ?? "";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVanilla))]
        private Option? _pack;
        [ObservableProperty] private string? _error;
        public bool IsVanilla => string.IsNullOrEmpty(Pack?.Key);

        public async Task LoadPacksAsync()
        {
            try
            {
                foreach (var p in (await CoreClient.Instance.Call("packs.list")).Items("packs"))
                    Packs.Add(new Option(p.Str("id") ?? "", $"{p.Str("name")} · {p.Str("mcVersion")}"));
            }
            catch { /* paketsiz (Vanilla) seçilebilir */ }
            Pack = Packs.FirstOrDefault(p => p.Key == (server?.PackId ?? "")) ?? Packs[0];
        }

        [RelayCommand]
        private async Task Save()
        {
            Error = null;
            try
            {
                await CoreClient.Instance.Call("servers.save", new { id = server?.Id ?? "", name = Name, address = Address, description = Description, packId = Pack?.Key ?? "", mcVersion = McVersion });
                Close(true);
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }
}
