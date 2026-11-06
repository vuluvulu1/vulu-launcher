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
    // ── Sol liste ────────────────────────────────────────────────────────
    internal sealed record SocialHeader(string Title, int Count)
    {
        public bool HasCount => Count > 0;
    }

    internal sealed record SocialHint(string Text);

    internal sealed partial class SocialFeedLink(SocialViewModel owner, bool active) : ObservableObject
    {
        public bool IsActive { get; } = active;
        [RelayCommand] private void Open() => owner.ShowFeed();
    }

    /// <summary>Soldaki kişi satırı: avatar, durum, okunmamış sayısı, satır düğmeleri.</summary>
    internal sealed partial class SocialRow(PersonItem person, string sub, Func<Task> open) : ObservableObject
    {
        public PersonItem Person { get; } = person;
        public string Sub { get; } = sub;
        public int Badge { get; init; }
        public bool HasBadge => Badge > 0;
        public bool IsActive { get; init; }
        public List<RowAction> Actions { get; init; } = new();
        public bool HasActions => Actions.Count > 0;
        [RelayCommand] private Task Open() => open();
    }

    // ── Akış ─────────────────────────────────────────────────────────────
    /// <summary>Paylaşılan ekran görüntüsü.</summary>
    internal sealed partial class ShotCard : ObservableObject
    {
        private readonly SocialViewModel _owner;

        public ShotCard(SocialViewModel owner, JsonElement s, long avatarVersion)
        {
            _owner = owner;
            Id = s.Text("id") ?? "";
            Author = new PersonItem(s.Text("author") ?? "", avatarVersion);
            Caption = s.Text("caption") ?? "";
            CreatedAt = s.Text("createdAt");
            Meta = string.Join(" · ", new[] { s.Text("packName"), Names.Ago(CreatedAt) }.Where(x => !string.IsNullOrEmpty(x)));
            _likes = s.Int("likes");
            _liked = s.Bool("liked");
            _commentCount = s.Int("commentCount");
            CanDelete = s.Bool("canDelete");
            ThumbUrl = s.Text("thumbUrl");
            _ = LoadThumb();
        }

        public string Id { get; }
        public PersonItem Author { get; }
        public string Caption { get; }
        public bool HasCaption => Caption.Length > 0;
        public string? CreatedAt { get; }
        public string Meta { get; }
        public bool CanDelete { get; }
        public string? ThumbUrl { get; }
        [ObservableProperty] private Bitmap? _thumb;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LikeText))]
        private int _likes;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LikeText))]
        private bool _liked;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CommentText))]
        private int _commentCount;
        public string LikeText => (Liked ? "♥ " : "♡ ") + Likes;
        public string CommentText => "💬 " + CommentCount;

        private async Task LoadThumb() => Thumb = await Images.LoadAsync(ThumbUrl, 480);

        [RelayCommand]
        public async Task Like()
        {
            try
            {
                var r = await CoreClient.Instance.Call("social.like", new { id = Id, like = !Liked });
                Liked = r.Bool("liked");
                Likes = r.Int("likes");
            }
            catch (Exception ex) { _owner.Main.Home.AppendLog(ex.Message, "error"); }
        }

        [RelayCommand] private Task Open() => _owner.ViewShotAsync(this);
    }

    /// <summary>"Şimdi" kartları: duyuru, açık sunucular, oyundaki arkadaşlar.</summary>
    internal sealed record LiveAnnouncement(string Text, string Meta);

    internal sealed partial class LiveServer(ServerCard server, string players, List<PersonItem> people, Func<Task> join) : ObservableObject
    {
        public ServerCard Server { get; } = server;
        public string Players { get; } = players;
        public List<PersonItem> People { get; } = people;
        [RelayCommand] private Task Join() => join();
    }

    internal sealed partial class LiveFriend(PersonItem person, string detail, Func<Task> open, Func<Task>? join) : ObservableObject
    {
        public PersonItem Person { get; } = person;
        public string Detail { get; } = detail;
        public bool CanJoin => join != null;
        [RelayCommand] private Task Open() => open();
        [RelayCommand] private Task Join() => join?.Invoke() ?? Task.CompletedTask;
    }

    // ── Sohbet ───────────────────────────────────────────────────────────
    /// <summary>Mesajla paylaşılan paket ya da sunucu daveti.</summary>
    internal sealed partial class DmCardItem(SocialViewModel owner, JsonElement card, bool mine) : ObservableObject
    {
        public string Type { get; } = card.Text("type") ?? "pack";
        public string Id { get; } = card.Text("id") ?? "";
        public string Name { get; } = card.Text("name") ?? "";
        public string? Address { get; } = card.Text("address");
        public bool Mine { get; } = mine;
        public bool HasAddress => Address != null;
        public string Icon => Type == "pack" ? "📦" : "🎮";
        public string Kind => Type == "pack" ? "vulu pack" : "Sunucu daveti";
        public string ActionText => Type == "pack" ? "Paketi aç" : "Katıl";
        [ObservableProperty] private string? _note;
        [ObservableProperty] private bool _busy;

        [RelayCommand]
        private async Task Act()
        {
            Busy = true;
            try { Note = await owner.OpenCardAsync(this); }
            catch (Exception ex) { Note = ex.Message; }
            finally { Busy = false; }
        }
    }

    internal sealed partial class ChatMessage : ObservableObject
    {
        private readonly SocialViewModel _owner;

        public ChatMessage(SocialViewModel owner, JsonElement m)
        {
            _owner = owner;
            Id = m.Text("id") ?? "";
            Mine = m.Bool("mine");
            Text = m.Text("text");
            Problem = m.Text("problem");
            CreatedAt = DateTime.TryParse(m.Text("createdAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at.ToLocalTime() : DateTime.Now;
            CreatedAtRaw = m.Text("createdAt") ?? "";
            Read = m.Bool("read");
            if (m.Get("card").ValueKind == JsonValueKind.Object) Card = new DmCardItem(owner, m.Get("card").Clone(), Mine);
        }

        public string Id { get; }
        public bool Mine { get; }
        public string? Text { get; }
        public string? Problem { get; }
        public DateTime CreatedAt { get; }
        public string CreatedAtRaw { get; }
        public bool Read { get; }
        public DmCardItem? Card { get; }
        public bool HasCard => Card != null;
        public bool HasBubble => Text != null || Problem != null || Card == null;
        public string Bubble => Text ?? Problem ?? "";
        public bool IsBroken => Problem != null && Text == null;
        public string Time => CreatedAt.ToString("HH:mm", CultureInfo.InvariantCulture) + (Mine && Read ? " · okundu" : "");
        /// <summary>Günün ilk mesajıysa üstünde tarih ayracı.</summary>
        public string? Day { get; set; }
        public bool HasDay => Day != null;

        [RelayCommand] private Task Delete() => _owner.DeleteMessageAsync(this);
    }

    /// <summary>
    /// Sosyal: solda kişiler (istekler, konuşmalar ve arkadaşlar tek listede), ortada akış ya da seçilen kişiyle sohbet.
    /// Mesajlar uçtan uca şifreli; şifreleme Core'da yapılır, burada yalnızca düz metin görünür.
    /// </summary>
    internal sealed partial class SocialViewModel : ObservableObject
    {
        private const int DmMax = 2000;
        private readonly CoreClient _core = CoreClient.Instance;
        private readonly DispatcherTimer _friendsTimer, _liveTimer;
        private List<string> _friends = new(), _incoming = new(), _outgoing = new();
        private List<JsonElement> _conversations = new();
        private JsonElement _presence;
        private bool _friendsLoaded, _shotsLoading, _shotsLoadedOnce, _loadingOlder, _reachedStart;
        private int _shotsRequest, _dmUnread;

        public SocialViewModel(MainViewModel main)
        {
            Main = main;
            _friendsTimer = new DispatcherTimer(TimeSpan.FromSeconds(60), DispatcherPriority.Background, async (_, _) => await RefreshFriendsAsync());
            _liveTimer = new DispatcherTimer(TimeSpan.FromSeconds(60), DispatcherPriority.Background, async (_, _) =>
            {
                if (Main.SelectedTab?.Page != this || !IsFeed) { _liveTimer!.Stop(); return; }
                await RefreshLiveAsync();
            });
        }

        public MainViewModel Main { get; }

        public ObservableCollection<object> List { get; } = new();
        public ObservableCollection<ShotCard> Shots { get; } = new();
        public ObservableCollection<object> Live { get; } = new();
        public ObservableCollection<ChatMessage> Messages { get; } = new();

        [ObservableProperty] private string _search = "";
        partial void OnSearchChanged(string value) => RenderList();

        /// <summary>"feed" ya da "chat".</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFeed), nameof(IsChat))]
        private string _view = "feed";
        public bool IsFeed => View == "feed";
        public bool IsChat => View == "chat";

        // Akış
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsMine))]
        private string _filter = "all";
        public bool IsMine => Filter == "mine";
        [ObservableProperty] private bool _moreShots;
        [ObservableProperty] private string _shotsStatus = "";
        [ObservableProperty] private bool _shotsEmpty;
        public bool HasLive => Live.Count > 0;

        // Sohbet
        [ObservableProperty] private string? _active;
        [ObservableProperty] private bool _peerReady = true;
        [ObservableProperty] private string? _peerNotice;
        [ObservableProperty] private bool _verified;
        [ObservableProperty] private bool _keyChanged;
        [ObservableProperty] private string _draft = "";
        [ObservableProperty] private string? _chatMessage;
        [ObservableProperty] private bool _sending;
        public string LockText => Verified ? "🔒 Doğrulandı" : "🔒 Uçtan uca şifreli";
        public string Placeholder => $"{Active} kişisine yaz…";
        public string Counter => Draft.Length > DmMax - 200 ? $"{Draft.Length}/{DmMax}" : "";
        partial void OnVerifiedChanged(bool value) => OnPropertyChanged(nameof(LockText));
        partial void OnActiveChanged(string? value) => OnPropertyChanged(nameof(Placeholder));
        partial void OnDraftChanged(string value) => OnPropertyChanged(nameof(Counter));

        /// <summary>Sosyal sekmesindeki rozet: okunmamış mesajlar ve bekleyen arkadaşlık istekleri.</summary>
        private void UpdateBadge() => Main.Tabs.First(t => t.Key == "social").Badge = _dmUnread + _incoming.Count;

        // ── Giriş ve sekme ───────────────────────────────────────────────
        /// <summary>Girişten sonra: istekler ve okunmamış mesajlar düzenli sorulur (rozet).</summary>
        public async Task StartAsync()
        {
            _friendsTimer.Start();
            await RefreshFriendsAsync();
            await LoadConversationsAsync();
            _dmUnread = _conversations.Sum(c => c.Int("unread"));
            UpdateBadge();
        }

        public void Stop()
        {
            _friendsTimer.Stop();
            _liveTimer.Stop();
            _friends = new(); _incoming = new(); _outgoing = new(); _conversations = new();
            _friendsLoaded = false;
            _dmUnread = 0;
            UpdateBadge();
        }

        /// <summary>Sekme açıldı: okunmamış mesaj varsa o konuşma açılır, yoksa akış (ya da açık kalan sohbet).</summary>
        public async Task OpenAsync()
        {
            RenderList();
            await Task.WhenAll(LoadConversationsAsync(), RefreshFriendsAsync(), RefreshPresenceAsync(), Main.MembersAsync());
            RenderList();
            if (IsChat && Active != null) return;
            var unread = _conversations.FirstOrDefault(c => c.Int("unread") > 0);
            if (unread.ValueKind == JsonValueKind.Object) await OpenConversationAsync(unread.Text("username")!);
            else ShowFeed();
        }

        private async Task LoadConversationsAsync()
        {
            try { _conversations = (await _core.Call("dm.conversations")).Items().Select(c => c.Clone()).ToList(); }
            catch { /* sunucuya ulaşılamazsa eski liste kalır */ }
        }

        private async Task RefreshFriendsAsync()
        {
            try
            {
                var r = await _core.Call("friends.list");
                _friends = r.Items("friends").Select(x => x.GetString() ?? "").ToList();
                _incoming = r.Items("incoming").Select(x => x.GetString() ?? "").ToList();
                _outgoing = r.Items("outgoing").Select(x => x.GetString() ?? "").ToList();
                _friendsLoaded = true;
            }
            catch { /* eski liste kalır */ }
            UpdateBadge();
            if (Main.SelectedTab?.Page == this) RenderList();
        }

        private async Task RefreshPresenceAsync()
        {
            try { _presence = (await _core.Call("community.presence")).Clone(); } catch { /* eski kalır */ }
        }

        private JsonElement PresenceOf(string name) => _presence.Items().FirstOrDefault(p => p.Text("username") == name);

        private PersonItem Person(string name)
        {
            var person = new PersonItem(name, Main.MembersCached.TryGetValue(name, out long v) ? v : 0);
            var p = PresenceOf(name);
            if (p.ValueKind == JsonValueKind.Object)
            {
                person.State = p.Text("state");
                (person.Game, person.Rest) = Main.Servers.Describe(p);
            }
            return person;
        }

        // ── Sol liste ────────────────────────────────────────────────────
        /// <summary>Akış, istekler, kişiler (okunmamışlar, oyundakiler, çevrimiçiler önce), gönderilen istekler; arama varsa diğer üyeler.</summary>
        public void RenderList()
        {
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            string query = Search.Trim().ToLower(tr);
            bool Match(string name) => query.Length == 0 || name.ToLower(tr).Contains(query);
            JsonElement Conv(string name) => _conversations.FirstOrDefault(c => c.Text("username") == name);

            List.Clear();
            if (query.Length == 0) List.Add(new SocialFeedLink(this, IsFeed));

            var incoming = _incoming.Where(Match).ToList();
            if (incoming.Count > 0)
            {
                List.Add(new SocialHeader("İstekler", incoming.Count));
                foreach (var name in incoming)
                    List.Add(new SocialRow(Person(name), "Arkadaş olmak istiyor", () => OpenConversationAsync(name))
                    {
                        Actions = { new RowAction("Kabul et", () => FriendActionAsync("accept", name)), new RowAction("Reddet", () => FriendActionAsync("decline", name)) },
                    });
            }

            var known = _conversations.Select(c => c.Text("username") ?? "").Concat(_friends).Where(n => n != Main.Username && n.Length > 0).ToHashSet();
            int Rank(string name)
            {
                var p = PresenceOf(name);
                int state = p.ValueKind != JsonValueKind.Object ? 2 : p.Text("state") == "playing" ? 0 : 1;
                return (Conv(name).Int("unread") > 0 ? 0 : 10) + state;
            }
            var people = known.Where(Match).OrderBy(Rank)
                .ThenByDescending(n => Conv(n).Text("lastAt") ?? "")
                .ThenBy(n => n, StringComparer.Create(tr, true)).ToList();
            List.Add(new SocialHeader("Kişiler", 0));
            foreach (var name in people)
            {
                var person = Person(name);
                var conv = Conv(name);
                string sub = person.IsOnline ? (person.Game != null ? $"{person.Game} · {person.Rest}" : person.Rest ?? "")
                    : conv.ValueKind == JsonValueKind.Object ? Names.Ago(conv.Text("lastAt")) : "";
                var row = new SocialRow(person, sub, () => OpenConversationAsync(name)) { Badge = conv.Int("unread"), IsActive = IsChat && Active == name };
                // Arkadaş bir vulu sunucusunda oynuyorsa yanına tek tıkla gidilir
                var p = PresenceOf(name);
                var server = p.Text("state") == "playing" && p.Text("server") is { } addr ? Main.Servers.Servers.FirstOrDefault(s => s.Address == addr) : null;
                if (server != null) row.Actions.Add(new RowAction("Katıl", () => Main.Servers.JoinAsync(server)));
                List.Add(row);
            }
            if (people.Count == 0)
                List.Add(new SocialHint(!_friendsLoaded ? "Yükleniyor…" : query.Length > 0 ? "Bu adla bir arkadaşın ya da konuşman yok." : "Henüz kimse yok. Yukarıdan birini ara ve arkadaş ekle."));

            var outgoing = _outgoing.Where(Match).ToList();
            if (outgoing.Count > 0)
            {
                List.Add(new SocialHeader("Gönderdiğin istekler", outgoing.Count));
                foreach (var name in outgoing)
                    List.Add(new SocialRow(Person(name), "Yanıt bekleniyor", () => OpenConversationAsync(name)) { Actions = { new RowAction("Geri al", () => FriendActionAsync("decline", name)) } });
            }

            if (query.Length > 0)
            {
                var members = Main.MembersCached.Keys;
                var others = members.Where(n => n != Main.Username && !known.Contains(n) && !_incoming.Contains(n) && !_outgoing.Contains(n) && Match(n))
                    .OrderBy(n => n, StringComparer.Create(tr, true)).Take(30).ToList();
                List.Add(new SocialHeader("Topluluk", others.Count));
                foreach (var name in others)
                    List.Add(new SocialRow(Person(name), "", () => OpenConversationAsync(name)) { Actions = { new RowAction("+ Ekle", () => FriendActionAsync("request", name)) } });
                if (others.Count == 0) List.Add(new SocialHint("Bu adla bir üye yok."));
            }
        }

        /// <summary>İstek gönder / kabul et / reddet / geri al / çıkar; sonra liste yenilenir.</summary>
        public async Task FriendActionAsync(string action, string username)
        {
            if (action == "remove" && !await Main.ConfirmAsync("Arkadaşlıktan çıkar", $"{username} arkadaşlarından çıkarılsın mı?", "Çıkar", danger: true)) return;
            try
            {
                var result = await _core.Call("friends." + action, new { username });
                if (action == "request" && result.Text("status") == "accepted") Main.ShowToast("Arkadaş", $"{username} ile artık arkadaşsınız.");
            }
            catch (Exception ex) { await Main.AlertAsync("Olmadı", ex.Message); }
            await RefreshFriendsAsync();
            await RefreshPresenceAsync();
            RenderList();
        }

        /// <summary>Kişinin arkadaşlık durumu: "friend", "incoming", "outgoing" ya da null.</summary>
        public string? FriendState(string username) =>
            _friends.Contains(username) ? "friend" : _incoming.Contains(username) ? "incoming" : _outgoing.Contains(username) ? "outgoing" : null;

        // ── Akış ─────────────────────────────────────────────────────────
        public void ShowFeed()
        {
            Active = null;
            View = "feed";
            Messages.Clear();
            RenderList();
            if (!_shotsLoadedOnce) { _shotsLoadedOnce = true; _ = LoadShotsAsync(false); }
            _ = RefreshLiveAsync();
            _liveTimer.Start();
        }

        [RelayCommand]
        private Task SetFilter(string filter)
        {
            Filter = filter;
            return LoadShotsAsync(false);
        }

        [RelayCommand] private Task MoreShotsLoad() => LoadShotsAsync(true);

        private async Task LoadShotsAsync(bool append)
        {
            if (_shotsLoading && append) return;
            _shotsLoading = true;
            int request = ++_shotsRequest;
            if (!append) { Shots.Clear(); ShotsEmpty = false; ShotsStatus = "Yükleniyor…"; }
            try
            {
                var result = await _core.Call("social.feed", new { before = append && Shots.Count > 0 ? Shots[^1].CreatedAt : "", author = IsMine ? Main.Username : "" });
                if (request != _shotsRequest) return;
                var members = await Main.MembersAsync();
                foreach (var s in result.Items("shots")) Shots.Add(new ShotCard(this, s, members.GetValueOrDefault(s.Text("author") ?? "")));
                MoreShots = result.Bool("more");
                ShotsStatus = "";
                ShotsEmpty = Shots.Count == 0;
            }
            catch (Exception ex)
            {
                if (request == _shotsRequest) ShotsStatus = "Ekran görüntüleri alınamadı: " + ex.Message;
            }
            finally { if (request == _shotsRequest) _shotsLoading = false; }
        }

        /// <summary>Canlı kartlar: duyuru, açık sunucular (içerideki oyuncularla), oyundaki arkadaşlar.</summary>
        private async Task RefreshLiveAsync()
        {
            var servers = Main.Servers.Servers;
            if (servers.Count == 0) await Main.Servers.LoadAsync();
            JsonElement announcement = default;
            await Task.WhenAll(
                RefreshPresenceAsync(),
                Task.Run(async () => { try { announcement = (await _core.Call("announcement.get")).Get("announcement").Clone(); } catch { } }),
                Task.WhenAll(Main.Servers.Servers.Select(s => s.PingAsync())));

            var members = await Main.MembersAsync();
            Live.Clear();
            if (announcement.ValueKind == JsonValueKind.Object)
                Live.Add(new LiveAnnouncement(announcement.Text("text") ?? "", string.Join(" · ", new[] { announcement.Text("author"), Names.Ago(announcement.Text("createdAt")) }.Where(x => !string.IsNullOrEmpty(x)))));
            foreach (var s in Main.Servers.Servers.Where(s => s.IsOnline))
            {
                var names = s.Sample.Length > 0 ? s.Sample.Split(", ") : Array.Empty<string>();
                int count = int.TryParse(s.Players.Split('/')[0], out int n) ? n : 0;
                Live.Add(new LiveServer(s, count > 0 ? $"{count} kişi oyunda" : "Şu an kimse yok · ilk giren sen ol",
                    names.Take(8).Select(x => new PersonItem(x, members.GetValueOrDefault(x))).ToList(), () => Main.Servers.JoinAsync(s)));
            }
            var playing = _presence.Items().Where(p => p.Text("username") != Main.Username && p.Text("state") == "playing" && _friends.Contains(p.Text("username") ?? "")).Take(6).ToList();
            foreach (var p in playing)
            {
                string name = p.Text("username")!;
                var person = Person(name);
                var server = p.Text("server") is { } addr ? Main.Servers.Servers.FirstOrDefault(x => x.Address == addr) : null;
                Live.Add(new LiveFriend(person, $"{person.Game} · {person.Rest}".Trim(' ', '·'), () => OpenConversationAsync(name),
                    server != null ? () => Main.Servers.JoinAsync(server) : null));
            }
            OnPropertyChanged(nameof(HasLive));
        }

        /// <summary>Bildirimden: o ekran görüntüsü açılır (akışta yoksa başa eklenir).</summary>
        public async Task OpenSharedShotAsync(string id)
        {
            if (Main.SelectedTab?.Page != this) Main.SelectedTab = Main.Tabs.First(t => t.Key == "social");
            JsonElement data;
            try { data = await _core.Call("social.shot", new { id }); }
            catch (Exception ex) { await Main.AlertAsync("Ekran görüntüsü", ex.Message); return; }
            var shot = Shots.FirstOrDefault(s => s.Id == id);
            if (shot == null)
            {
                shot = new ShotCard(this, data, Main.MembersCached.GetValueOrDefault(data.Text("author") ?? ""));
                Shots.Insert(0, shot);
            }
            await ViewShotAsync(shot);
        }

        public async Task ViewShotAsync(ShotCard shot)
        {
            var modal = new ShotViewerModal(this, Shots.ToList(), Shots.IndexOf(shot));
            var result = await Main.ShowAsync(modal);
            if (result is ShotCard remove) await DeleteShotAsync(remove);
            else if (result is string author) await OpenConversationAsync(author);
        }

        private async Task DeleteShotAsync(ShotCard shot)
        {
            bool own = shot.Author.Username == Main.Username;
            if (!await Main.ConfirmAsync("Ekran görüntüsünü kaldır",
                    own ? "Bu ekran görüntüsü Sosyal'den kaldırılsın mı? Bilgisayarındaki dosyaya dokunulmaz." : $"{shot.Author.Username} kişisinin ekran görüntüsü kaldırılsın mı?",
                    "Kaldır", danger: true)) return;
            try
            {
                await _core.Call("social.delete", new { id = shot.Id });
                Shots.Remove(shot);
                ShotsEmpty = Shots.Count == 0;
            }
            catch (Exception ex) { await Main.AlertAsync("Kaldırılamadı", ex.Message); }
        }

        /// <summary>"+ Paylaş": önce paket, sonra o paketin ekran görüntülerinden biri; küçültülüp açıklamayla paylaşılır.</summary>
        [RelayCommand]
        private async Task Share()
        {
            var instances = Main.Home.Instances.ToList();
            InstanceItem? instance = instances.Count == 1 ? instances[0] : await Main.ShowAsync(new InstancePickerModal(instances)) as InstanceItem;
            if (instance == null) return;
            if (await Main.ShowAsync(new ScreenshotsModal(Main, instance, pick: true)) is not string name) return;

            ImageEncoder.Encoded full, thumb;
            try
            {
                var local = await _core.Call("social.localImage", new { id = instance.Id, name });
                var bytes = ImageEncoder.FromDataUrl(local.Text("dataUrl") ?? "");
                (full, thumb) = await Task.Run(() => (ImageEncoder.Encode(bytes, 1920, 1100 * 1024), ImageEncoder.Encode(bytes, 480, 150 * 1024)));
            }
            catch (Exception ex) { await Main.AlertAsync("Paylaşılamadı", ex.Message); return; }

            var modal = new ShareShotModal(instance, full, thumb);
            if (await Main.ShowAsync(modal) is true)
            {
                Filter = "all";
                ShowFeed();
                await LoadShotsAsync(false);
            }
        }

        // ── Sohbet ───────────────────────────────────────────────────────
        public async Task OpenConversationAsync(string user)
        {
            if (Main.SelectedTab?.Page != this) Main.SelectedTab = Main.Tabs.First(t => t.Key == "social");
            Active = user;
            View = "chat";
            _liveTimer.Stop();
            Messages.Clear();
            ChatMessage = "Yükleniyor…";
            PeerReady = true; PeerNotice = null; Verified = false; KeyChanged = false; Draft = "";
            RenderList();
            try
            {
                var result = await _core.Call("dm.history", new { user });
                if (Active != user) return;
                SetMessages(result.Items("messages").Select(m => new ChatMessage(this, m)).ToList());
                _reachedStart = result.Items("messages").Count() < 50;
                PeerReady = result.Get("peerReady").ValueKind != JsonValueKind.False;
                PeerNotice = PeerReady ? null : result.Text("peerError") ?? $"{user} henüz mesajlaşmayı kullanmadı; launcher'ın yeni sürümüyle bir kez giriş yaptığında yazabilirsin.";
                Verified = result.Bool("verified");
                KeyChanged = result.Bool("keyChanged");
                ScrollToEnd?.Invoke();
                await _core.Call("dm.read", new { user });
                int unread = _conversations.FirstOrDefault(c => c.Text("username") == user).Int("unread");
                if (unread > 0)
                {
                    _dmUnread = Math.Max(0, _dmUnread - unread);
                    await LoadConversationsAsync();
                    UpdateBadge();
                    RenderList();
                }
            }
            catch (Exception ex) { ChatMessage = ex.Message; }
        }

        /// <summary>Görünüm, yeni mesaj gelince sohbeti en alta kaydırır.</summary>
        public Action? ScrollToEnd { get; set; }

        private void SetMessages(List<ChatMessage> list)
        {
            Messages.Clear();
            string last = "";
            foreach (var m in list)
            {
                string day = m.CreatedAt.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR"));
                m.Day = day != last ? day : null;
                last = day;
                Messages.Add(m);
            }
            ChatMessage = Messages.Count == 0 ? "Henüz mesaj yok. İlk mesajı sen yaz." : null;
        }

        /// <summary>Yukarı kaydırınca daha eski mesajlar.</summary>
        public async Task LoadOlderAsync()
        {
            if (_loadingOlder || _reachedStart || Messages.Count == 0 || Active is not { } user) return;
            _loadingOlder = true;
            try
            {
                var result = await _core.Call("dm.history", new { user, before = Messages[0].CreatedAtRaw });
                var older = result.Items("messages").Select(m => new ChatMessage(this, m)).ToList();
                _reachedStart = older.Count < 50;
                SetMessages(older.Concat(Messages).ToList());
            }
            catch { /* sonra yeniden denenir */ }
            _loadingOlder = false;
        }

        [RelayCommand]
        private async Task Send() => await SendAsync(null);

        [RelayCommand]
        private async Task AttachCard()
        {
            if (await Main.ShowAsync(new CardPickerModal()) is JsonElement card) await SendAsync(card);
        }

        private async Task SendAsync(JsonElement? card)
        {
            string text = Draft.Trim();
            if (Active is not { } user || (text.Length == 0 && card == null)) return;
            Sending = true;
            if (await SendToAsync(user, text, card)) Draft = "";
            Sending = false;
        }

        /// <summary>Mesaj gönderir; açık yazışmaya eklenir. Başarılıysa true.</summary>
        public async Task<bool> SendToAsync(string user, string text, JsonElement? card)
        {
            try
            {
                var result = await _core.Call("dm.send", card is { } c ? new { user, text, card = c } : (object)new { user, text });
                if (Active == user)
                {
                    SetMessages(Messages.Append(new ChatMessage(this, result.Get("message"))).ToList());
                    if (result.Bool("keyChanged")) KeyChanged = true;
                    ScrollToEnd?.Invoke();
                }
                await LoadConversationsAsync();
                RenderList();
                return true;
            }
            catch (Exception ex)
            {
                await Main.AlertAsync("Mesaj gönderilemedi", ex.Message);
                return false;
            }
        }

        public async Task DeleteMessageAsync(ChatMessage m)
        {
            if (!await Main.ConfirmAsync("Mesajı sil", "Bu mesaj iki taraftan da silinsin mi?", "Sil", danger: true)) return;
            try
            {
                await _core.Call("dm.delete", new { id = m.Id });
                SetMessages(Messages.Where(x => x != m).ToList());
            }
            catch (Exception ex) { await Main.AlertAsync("Silinemedi", ex.Message); }
        }

        [RelayCommand]
        private async Task SafetyCode()
        {
            if (Active is not { } user) return;
            JsonElement info;
            try { info = await _core.Call("dm.safety", new { user }); }
            catch (Exception ex) { await Main.AlertAsync("Güvenlik kodu", ex.Message); return; }
            if (await Main.ShowAsync(new SafetyModal(user, info.Text("code") ?? "", info.Bool("verified"))) is not true) return;
            try
            {
                await _core.Call("dm.verify", new { user });
                if (Active == user) { Verified = true; KeyChanged = false; }
            }
            catch (Exception ex) { await Main.AlertAsync("Doğrulanamadı", ex.Message); }
        }

        [RelayCommand]
        private async Task NewMessage()
        {
            if (await PickUserAsync("Yeni mesaj") is { } user) await OpenConversationAsync(user);
        }

        /// <summary>Kullanıcı seçtirir (son yazışılanlar önce); vazgeçilirse null.</summary>
        public async Task<string?> PickUserAsync(string title)
        {
            var members = await Main.MembersAsync();
            var recent = _conversations.Select(c => c.Text("username")).ToList();
            var ordered = members.Where(m => m.Key != Main.Username)
                .OrderBy(m => recent.IndexOf(m.Key) is var i && i >= 0 ? i : int.MaxValue)
                .Select(m => new PersonItem(m.Key, m.Value)).ToList();
            return await Main.ShowAsync(new UserPickerModal(title, ordered)) as string;
        }

        /// <summary>Paket ya da sunucu kartını bir kişiye gönderir ve yazışmayı açar.</summary>
        public async Task ShareCardAsync(object card, string title)
        {
            if (await PickUserAsync(title) is not { } user) return;
            var element = JsonSerializer.SerializeToElement(card, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (await SendToAsync(user, "", element)) await OpenConversationAsync(user);
        }

        /// <summary>Karttaki düğme: paketi kur/güncelle/aç ya da sunucuya katıl. Gösterilecek not döner.</summary>
        public async Task<string?> OpenCardAsync(DmCardItem card)
        {
            if (card.Type == "pack")
            {
                await Main.Discover.Packs.LoadAsync();
                var pack = Main.Discover.Packs.Cards.FirstOrDefault(p => p.Id == card.Id);
                if (pack == null) return "Bu paket artık vulu Packs'te yok.";
                await Main.Discover.Packs.ActAsync(pack);
                return null;
            }
            if (Main.Servers.Servers.Count == 0) await Main.Servers.LoadAsync();
            var server = Main.Servers.Servers.FirstOrDefault(s => s.Id == card.Id || s.Address == card.Address);
            if (server != null) { await Main.Servers.JoinAsync(server); return null; }
            if (card.Address != null) await Main.Servers.CopyAsync(card.Address);
            return "Bu sunucu vulu listesinde yok; adres kopyalandı. Oyunda Çok Oyunculu → Sunucu ekle ile bağlanabilirsin.";
        }

        /// <summary>Core birkaç saniyede bir yeni mesajları sorar: okunmamış sayısı ve gelenler.</summary>
        public void OnDmUpdate(JsonElement update)
        {
            _dmUnread = update.Int("unread");
            UpdateBadge();
            var incoming = update.Items("messages").ToList();
            if (incoming.Count == 0) return;
            bool added = false;
            foreach (var m in incoming)
            {
                string peer = m.Text("peer") ?? "";
                bool inChat = Main.SelectedTab?.Page == this && IsChat && Active == peer;
                if (inChat && Messages.All(x => x.Id != m.Text("id")))
                {
                    Messages.Add(new ChatMessage(this, m));
                    added = true;
                    _ = _core.Call("dm.read", new { user = peer });
                }
                else if (Main.SelectedTab?.Page != this)
                {
                    string preview = m.Text("text") is { } t ? (t.Length > 80 ? t[..80] : t)
                        : m.Get("card").ValueKind == JsonValueKind.Object ? (m.Get("card").Text("type") == "pack" ? "📦 " : "🎮 ") + m.Get("card").Text("name") : "yeni mesaj";
                    Main.Home.AppendLog($"💬 {peer}: {preview}", "accent");
                }
            }
            if (added) { SetMessages(Messages.ToList()); ScrollToEnd?.Invoke(); }
            _ = LoadConversationsAsync().ContinueWith(_ => Dispatcher.UIThread.Post(RenderList));
        }
    }
}
