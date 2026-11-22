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
        /// <summary>Adın altındaki satır (listelerde: oyunda / biyografi / son görülme).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSub))]
        private string _sub = "";
        public bool HasSub => Sub.Length > 0;
        [ObservableProperty] private bool _isAdmin;
        [ObservableProperty] private bool _isYou;
        public bool IsPlaying => State == "playing";
        public bool IsOnline => State != null;
        public bool HasGame => !string.IsNullOrEmpty(Game);
        partial void OnStateChanged(string? value) { OnPropertyChanged(nameof(IsPlaying)); OnPropertyChanged(nameof(IsOnline)); }
        partial void OnGameChanged(string? value) => OnPropertyChanged(nameof(HasGame));

        private async Task LoadAvatar(long version) => Avatar = await Images.LoadAsync(Avatars.Url(Username, version), 96);
    }

    internal static class ChatColors
    {
        private static readonly Avalonia.Media.IBrush Normal = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F3ECE2"));
        private static readonly Avalonia.Media.IBrush Dim = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#A1958A"));
        /// <summary>Giriş/çıkış satırları soluk, mesajlar normal.</summary>
        public static readonly Avalonia.Data.Converters.IValueConverter Text =
            new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(e => e ? Dim : Normal);
    }

    /// <summary>Oyun içi sohbetin bir satırı.</summary>
    internal sealed class ChatLine
    {
        public ChatLine(JsonElement m)
        {
            Seq = m.Int("seq");
            string kind = m.Text("kind") ?? "chat";
            Name = m.Text("name") ?? "";
            Time = DateTime.TryParse(m.Text("at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at.ToLocalTime().ToString("HH:mm") : "";
            (Tag, Text, IsEvent) = kind switch
            {
                "join" => ("", L.F("{0} oyuna girdi", Name), true),
                "leave" => ("", L.F("{0} oyundan çıktı", Name), true),
                "server" => ("[Sunucu] ", m.Text("text") ?? "", false),
                "launcher" => ("[Launcher] ", m.Text("text") ?? "", false),
                _ => ("", m.Text("text") ?? "", false),
            };
            if (IsEvent) Name = "";
        }

        public int Seq { get; }
        public string Time { get; }
        public string Tag { get; }
        public string Name { get; }
        public string Text { get; }
        public bool IsEvent { get; }
        public bool IsMessage => !IsEvent;
        public bool HasTag => Tag.Length > 0;
        public bool HasName => Name.Length > 0;
        /// <summary>"ad: " (giriş/çıkış satırlarında boş).</summary>
        public string NamePart => HasName ? Name + ": " : "";
    }

    /// <summary>Sunucular sayfasındaki kart: oyundaki sunucu listesi gibi simge, ad, oyuncu sayısı, renkli MOTD.</summary>
    internal sealed partial class ServerCard : ObservableObject
    {
        private readonly ServersViewModel _owner;

        public ServerCard(ServersViewModel owner, JsonElement s, string packLabel, string? bannerUrl = null)
        {
            _owner = owner;
            if (bannerUrl != null) _ = LoadBannerAsync(bannerUrl);
            Id = s.Text("id") ?? "";
            Name = s.Text("name") ?? "";
            Address = s.Text("address") ?? "";
            Description = s.Text("description") ?? "";
            PackId = s.Text("packId");
            McVersion = s.Text("mcVersion") ?? "";
            InstanceId = s.Text("instanceId");
            HasChat = s.Bool("chat");
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
        /// <summary>Kartın afişi: sunucunun paketinin kapak resmi (net, olduğu gibi).</summary>
        [ObservableProperty] private Bitmap? _banner;
        private async Task LoadBannerAsync(string url) => Banner = await Images.LoadAsync(url, 760);
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
        /// <summary>Doluluk (0..1): Kütüphane temasındaki kartın çubuğu.</summary>
        [ObservableProperty] private double _fill;
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
            Fill = online && st.Int("maxPlayers") > 0 ? Math.Clamp(st.Int("players") / (double)st.Int("maxPlayers"), 0, 1) : 0;
            Sample = string.Join(", ", st.Items("sample").Select(x => x.GetString()));
            Status = online ? string.Join(" · ", new[] { st.Text("version"), $"{st.Int("ping")} ms" }.Where(x => !string.IsNullOrEmpty(x))) : "Kapalı ya da ulaşılamıyor";
            if (Favicon == null && st.Text("favicon") is { } fav) Favicon = await Images.LoadAsync(fav, 64);
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
            _owner.UpdateTab();
        }

        // ── Oyun içi sohbet ───────────────────────────────────────────────
        /// <summary>Bu sunucunun sohbeti launcher'a bağlı (vulu'nun kendi sunucusu).</summary>
        public bool HasChat { get; }
        public ObservableCollection<ChatLine> Chat { get; } = new();
        [ObservableProperty] private bool _chatOpen;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendChatCommand))]
        private string _chatDraft = "";
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendChatCommand))]
        private bool _chatSending;
        [ObservableProperty] private string? _chatStatus;
        [ObservableProperty] private bool _chatEmpty;
        /// <summary>Kayıtlı geçmişte daha eski satırlar var ("Daha eski mesajlar").</summary>
        [ObservableProperty] private bool _chatHasOlder;
        [ObservableProperty] private bool _chatLoadingOlder;
        public string ChatButton => ChatOpen ? "Sohbeti gizle" : "Oyun sohbeti";
        private int _chatSeq;
        private DispatcherTimer? _chatTimer;
        private bool _chatPolling;
        /// <summary>Yeni satır geldi: görünüm en alta kayar.</summary>
        public event Action? ChatAdded;

        partial void OnChatOpenChanged(bool value)
        {
            OnPropertyChanged(nameof(ChatButton));
            if (value)
            {
                _chatTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, async (_, _) =>
                {
                    // Sayfa görünmezken yoklanmaz (sohbet açık kalır, dönülünce kaldığı yerden sürer)
                    if (!_owner.IsVisible) return;
                    await PollChatAsync();
                });
                _chatTimer.Start();
                _ = PollChatAsync();
            }
            else _chatTimer?.Stop();
        }

        [RelayCommand] private void ToggleChat() => ChatOpen = !ChatOpen;

        private async Task PollChatAsync()
        {
            if (_chatPolling) return;
            _chatPolling = true;
            try
            {
                bool first = _chatSeq == 0;
                var r = await CoreClient.Instance.Call("servers.chat", new { id = Id, after = _chatSeq });
                if (first) ChatHasOlder = r.Bool("more");
                bool added = false;
                foreach (var m in r.Items("messages"))
                {
                    var line = new ChatLine(m);
                    if (line.Seq <= _chatSeq && _chatSeq > 0) continue;
                    Chat.Add(line);
                    added = true;
                }
                int seq = r.Int("seq");
                // Sunucu (API) yeniden başladıysa sayaç küçülür: baştan alınır
                if (seq < _chatSeq) { Chat.Clear(); _chatSeq = 0; }
                else _chatSeq = seq;
                while (Chat.Count > 400) { Chat.RemoveAt(0); ChatHasOlder = true; }
                ChatEmpty = Chat.Count == 0;
                ChatStatus = null;
                if (added) ChatAdded?.Invoke();
            }
            catch (Exception ex) { ChatStatus = ex.Message; }
            finally { _chatPolling = false; }
        }

        /// <summary>Listenin başına daha eski 50 satır (kayıtlı geçmişten).</summary>
        [RelayCommand]
        private async Task LoadOlderChat()
        {
            if (ChatLoadingOlder || Chat.Count == 0) return;
            ChatLoadingOlder = true;
            try
            {
                var r = await CoreClient.Instance.Call("servers.chat", new { id = Id, before = Chat[0].Seq });
                var older = r.Items("messages").Select(m => new ChatLine(m)).Where(l => l.Seq < Chat[0].Seq).ToList();
                for (int i = older.Count - 1; i >= 0; i--) Chat.Insert(0, older[i]);
                ChatHasOlder = r.Bool("more");
            }
            catch (Exception ex) { ChatStatus = ex.Message; }
            finally { ChatLoadingOlder = false; }
        }

        private bool CanSendChat => !ChatSending && ChatDraft.Trim().Length > 0;

        [RelayCommand(CanExecute = nameof(CanSendChat))]
        private async Task SendChat()
        {
            ChatSending = true;
            try
            {
                await CoreClient.Instance.Call("servers.chatSend", new { id = Id, text = ChatDraft.Trim() });
                ChatDraft = "";
                ChatStatus = null;
                await PollChatAsync();
            }
            catch (Exception ex) { ChatStatus = ex.Message; }
            finally { ChatSending = false; }
        }

        [RelayCommand] private Task Join() => _owner.JoinAsync(this);
        [RelayCommand] private Task CopyAddress() => _owner.CopyAsync(Address);
        [RelayCommand] private Task Shortcut() => _owner.ShortcutAsync(this);
        [RelayCommand] private Task Map() => _owner.OpenMapAsync();
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
        /// <summary>İlk yükleme sürüyor: kartların yerinde parlayan kutular durur.</summary>
        [ObservableProperty] private bool _firstLoad;
        [ObservableProperty] private string _onlineCount = "";
        [ObservableProperty] private bool _nobodyOnline;
        public bool IsAdmin => _main.IsAdmin;

        /// <summary>Kütüphane rayı: açık sunucu varsa yeşil nokta, etikette kaç tane açık olduğu.</summary>
        public void UpdateTab()
        {
            if (_main.Tabs.FirstOrDefault(t => t.Key == "servers") is not { } tab) return;
            int open = Servers.Count(s => s.IsOnline);
            tab.Live = open > 0;
            tab.Tip = open > 0 ? L.F("Sunucular · {0} açık", open) : "Sunucular";
        }
        /// <summary>Sunucular sekmesi şu an açık mı (sohbet yoklaması yalnızca açıkken).</summary>
        public bool IsVisible => _main.SelectedTab?.Page == this;

        [RelayCommand]
        public async Task LoadAsync()
        {
            if (_loading) return;
            _loading = true;
            FirstLoad = Servers.Count == 0;
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

                // Açık sohbetler yenilemeden sonra da açık kalır (eski kartların yoklaması durur)
                var openChats = Servers.Where(c => c.ChatOpen).Select(c => c.Id).ToHashSet();
                foreach (var old in Servers) old.ChatOpen = false;
                Servers.Clear();
                foreach (var s in servers.Items())
                {
                    string? packId = s.Text("packId");
                    string label = packId != null
                        ? packs.Items("packs").FirstOrDefault(p => p.Text("id") == packId).Text("name") ?? "vulu paketi"
                        : "Vanilla" + (s.Text("mcVersion") is { Length: > 0 } v ? " " + v : "");
                    // Afiş: sunucunun paketinin kapak resmi (vulu Packs); paket yoksa temanın düz zemini
                    string? banner = packId != null ? packs.Items("packs").FirstOrDefault(p => p.Text("id") == packId).Text("coverUrl") : null;
                    Servers.Add(new ServerCard(this, s, label, banner));
                }
                IsEmpty = Servers.Count == 0;
                foreach (var card in Servers) if (card.HasChat && openChats.Contains(card.Id)) card.ChatOpen = true;
                MarkFriendsHere();
                foreach (var card in Servers) _ = card.PingAsync();
                _timer.Start();
            }
            catch (Exception ex) { Status = Errors.Friendly(ex); }
            finally { _loading = false; FirstLoad = false; }
        }

        private async Task RefreshPresenceAsync()
        {
            try { _presence = await _core.Call("community.presence"); }
            catch { return; /* sunucuya ulaşılamazsa eski liste kalır */ }
            var members = await _main.MembersAsync();
            var others = _presence.Items().Where(p => p.Text("username") != _main.Username)
                .OrderBy(p => p.Text("state") == "playing" ? 0 : 1).ThenBy(p => p.Text("username"), StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true)).ToList();
            Online.Clear();
            foreach (var p in others)
            {
                string name = p.Text("username") ?? "";
                var person = new PersonItem(name, members.TryGetValue(name, out long v) ? v : 0) { State = p.Text("state") };
                (person.Game, person.Rest) = Describe(p);
                Online.Add(person);
            }
            OnlineCount = Online.Count > 0 ? L.F("{0} çevrimiçi", Online.Count) : "";
            NobodyOnline = Online.Count == 0;
            MarkFriendsHere();
        }

        /// <summary>Oyundaysa ("AGLR SMP", "sunucuda · 1 sa 12 dk"); değilse (null, "Çevrimiçi").</summary>
        public (string? Game, string Detail) Describe(JsonElement p)
        {
            if (p.Text("state") != "playing") return (null, "Çevrimiçi");
            string game = p.Text("instance") ?? "Minecraft";
            string? address = p.Text("server");
            var server = address != null ? Servers.FirstOrDefault(s => s.Address == address) : null;
            string where = server != null && server.Name != game ? server.Name : address != null ? "sunucuda" : "";
            string played = "";
            if (p.Text("since") is { } since && DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at))
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
                var here = _presence.Items().Where(p => p.Text("state") == "playing" && p.Text("server") == card.Address).Select(p => p.Text("username")).ToList();
                card.FriendsHere = here.Count > 0 ? L.F("{0} arkadaş burada", here.Count) : null;
            }
        }

        /// <summary>Bağlan: doğru instance seçilir (gerekirse paket kurulur) ve oyun doğrudan sunucuya girer.</summary>
        public async Task JoinAsync(ServerCard s)
        {
            string? instanceId = s.InstanceId;
            if (instanceId == null && s.PackId != null)
            {
                try { instanceId = (await _core.Call("packs.install", new { id = s.PackId })).Text("id"); }
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

        /// <summary>Canlı harita penceresi (vulu sunucusu); kapanınca yenileme durur.</summary>
        public async Task OpenMapAsync()
        {
            var modal = new MapModal(new WorldViewModel(_main) { View = "map", Standalone = true });
            _ = modal.World.OpenAsync();
            await _main.ShowAsync(modal);
            modal.World.Leave();
        }

        public async Task ShortcutAsync(ServerCard s)
        {
            try
            {
                await _core.Call("shortcuts.create", new { kind = "join", id = s.Id, name = s.Name, iconDataUrl = ShortcutIcon.ToDataUrl(s.Favicon) });
                await _main.AlertAsync("Kısayol eklendi", L.F("Masaüstündeki \"{0}\" kısayolu launcher'ı açıp doğrudan {1} sunucusuna bağlanır.", s.Name, s.Name));
            }
            catch (Exception ex) { await _main.AlertAsync("Kısayol oluşturulamadı", ex.Message); }
        }

        [RelayCommand] private Task Add() => EditAsync(null);

        /// <summary>"Davet et": sunucu kartı mesajla bir arkadaşa gönderilir.</summary>
        public Task InviteAsync(ServerCard s) =>
            _main.Social.ShareCardAsync(new { type = "server", id = s.Id, name = s.Name, address = s.Address }, L.F("{0} davetini gönder", s.Name));

        public async Task EditAsync(ServerCard? s)
        {
            var modal = new ServerEditorModal(s);
            _ = modal.LoadPacksAsync();
            if (await _main.ShowAsync(modal) is true) await LoadAsync();
        }

        public async Task DeleteAsync(ServerCard s)
        {
            if (!await _main.ConfirmAsync("Sunucuyu sil", L.F("'{0}' listeden kaldırılsın mı? Sunucunun kendisine dokunulmaz.", s.Name), "Sil", danger: true)) return;
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
                    Packs.Add(new Option(p.Text("id") ?? "", $"{p.Text("name")} · {p.Text("mcVersion")}"));
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
