using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    internal sealed record AdminCard(string Title, string Value, string Sub, bool Attention, string? ActionText, Func<Task>? Action)
    {
        public bool HasAction => ActionText != null;
        public IRelayCommand Run => new AsyncRelayCommand(() => Action?.Invoke() ?? Task.CompletedTask);
    }

    internal sealed record AdminRole(string Id, string Name, string Color, int Members, int Packs)
    {
        public IBrush Brush => Avalonia.Media.Color.TryParse(Color, out var c) ? new SolidColorBrush(c) : Brushes.Gray;
    }

    internal sealed partial class AdminUserRow(AdminViewModel owner, JsonElement u, List<AdminRole> roles, string me) : ObservableObject
    {
        public JsonElement Data { get; } = u.Clone();
        public string Id { get; } = u.Str("id") ?? "";
        public string Username { get; } = u.Str("username") ?? "";
        public bool IsAdmin { get; } = u.Bool("isAdmin");
        public bool IsActive { get; } = u.Bool("isActive");
        public bool IsSelf { get; } = u.Str("username") == me;
        public string Meta { get; } = string.Join(" · ", new[] { u.Str("email"), u.Str("lastSeenAt") is { } s ? "Son görülme: " + Names.Ago(s) : "Henüz giriş yapmadı" }.Where(x => !string.IsNullOrEmpty(x)));
        public List<AdminRole> Roles { get; } = u.Items("roles").Select(r => roles.FirstOrDefault(x => x.Id == r.GetString())).OfType<AdminRole>().ToList();
        [RelayCommand] private Task Edit() => owner.EditUserAsync(this);
        [RelayCommand] private Task Delete() => owner.DeleteUserAsync(this);
    }

    internal sealed partial class RegistrationRow(AdminViewModel owner, JsonElement r) : ObservableObject
    {
        public string Id { get; } = r.Str("id") ?? "";
        public string Username { get; } = r.Str("username") ?? "";
        public string Email { get; } = r.Str("email") ?? "";
        // Alanı olmayan (eski sunucu) istekler doğrulanmış sayılır
        public bool Verified { get; } = r.Get("emailVerified").ValueKind != JsonValueKind.False;
        public string Meta => string.Join(" · ", new[] { Email, Verified ? null : "e-posta doğrulanmadı", r.Str("createdAt") is { } c ? "İstek: " + Names.Ago(c) : null }.Where(x => !string.IsNullOrEmpty(x)));
        [ObservableProperty] private bool _busy;
        [RelayCommand] private Task Approve() => owner.DecideAsync(this, true);
        [RelayCommand] private Task Reject() => owner.DecideAsync(this, false);
    }

    internal sealed partial class SongRow(AdminViewModel owner, JsonElement s) : ObservableObject
    {
        public string Id { get; } = s.Str("id") ?? "";
        public string Size { get; } = SettingsViewModel.FormatBytes(s.Num("size"));
        public string SavedTitle { get; set; } = s.Str("title") ?? "";
        public string SavedArtist { get; set; } = s.Str("artist") ?? "";
        [ObservableProperty] private string _title = s.Str("title") ?? "";
        [ObservableProperty] private string _artist = s.Str("artist") ?? "";
        [RelayCommand] private Task Save() => owner.SaveSongAsync(this);
        [RelayCommand] private Task Delete() => owner.DeleteSongAsync(this);
    }

    internal sealed record StoragePart(string Label, string Size, IBrush Brush, double Fraction);

    /// <summary>Admin paneli: özet, kullanıcılar, roller, kayıt istekleri, duyuru, radyo ve sunucu alanı.</summary>
    internal sealed partial class AdminViewModel(MainViewModel main) : ObservableObject
    {
        private static readonly (string Key, string Label)[] TabList =
            { ("overview", "Özet"), ("users", "Kullanıcılar"), ("roles", "Roller"), ("registrations", "Kayıt istekleri"), ("announcement", "Duyuru"), ("radio", "Radyo"), ("storage", "Alan") };
        private static readonly (int Hours, string Label)[] Durations = { (0, "Kaldırılana kadar"), (3, "3 saat"), (24, "1 gün"), (72, "3 gün"), (168, "1 hafta") };
        public static readonly string[] RoleColors = { "#c9a992", "#e3b04f", "#7cc47f", "#5fa8d3", "#9b7fd1", "#e2604a", "#d17fb0", "#9c704c" };
        private static readonly Dictionary<string, string> StorageColors = new() { ["packs"] = "#c27e3e", ["shots"] = "#5b8def", ["images"] = "#9b6bd6", ["messages"] = "#4caf6a" };

        private readonly CoreClient _core = CoreClient.Instance;
        private List<JsonElement> _registrations = new();
        private List<AdminRole> _roles = new();

        public ObservableCollection<Choice> Tabs { get; } = new(TabList.Select(t => new Choice(t.Key, t.Label)));
        public ObservableCollection<AdminCard> Cards { get; } = new();
        public ObservableCollection<AdminUserRow> Users { get; } = new();
        public ObservableCollection<AdminRole> Roles { get; } = new();
        public ObservableCollection<RegistrationRow> Registrations { get; } = new();
        public ObservableCollection<SongRow> Songs { get; } = new();
        public ObservableCollection<StoragePart> StorageParts { get; } = new();
        public ObservableCollection<Option> DurationOptions { get; } = new(Durations.Select(d => new Option(d.Hours.ToString(CultureInfo.InvariantCulture), d.Label)));

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsOverview), nameof(IsUsers), nameof(IsRoles), nameof(IsRegistrations), nameof(IsAnnouncement), nameof(IsRadio), nameof(IsStorage))]
        private string _tab = "overview";
        public bool IsOverview => Tab == "overview";
        public bool IsUsers => Tab == "users";
        public bool IsRoles => Tab == "roles";
        public bool IsRegistrations => Tab == "registrations";
        public bool IsAnnouncement => Tab == "announcement";
        public bool IsRadio => Tab == "radio";
        public bool IsStorage => Tab == "storage";

        [ObservableProperty] private string _status = "";
        [ObservableProperty] private bool _noRegistrations;
        [ObservableProperty] private string _summary = "";
        // Duyuru
        [ObservableProperty] private string _announcementText = "";
        [ObservableProperty] private Option? _duration;
        [ObservableProperty] private string? _currentAnnouncement;
        [ObservableProperty] private string? _currentAnnouncementMeta;
        [ObservableProperty] private string? _announcementNote;
        private string? _currentAnnouncementId;
        // Radyo
        [ObservableProperty] private bool _radioReady = true;
        [ObservableProperty] private string _radioUsage = "";
        [ObservableProperty] private double _radioFraction;
        [ObservableProperty] private string? _radioNote;
        [ObservableProperty] private string _uploadText = "Şarkı yükle";
        // Alan
        [ObservableProperty] private string _storageTotal = "";
        [ObservableProperty] private string? _storageWarning;

        /// <summary>Bekleyen kayıt istekleri: Admin sekmesinde rozet.</summary>
        public async Task RefreshRegistrationsAsync()
        {
            if (!main.IsAdmin) return;
            try { _registrations = (await _core.Call("admin.registrations")).Items().Select(r => r.Clone()).ToList(); }
            catch { /* rozet bir sonraki denemede güncellenir */ }
            main.Tabs.First(t => t.Key == "admin").Badge = _registrations.Count;
            Tabs.First(t => t.Key == "registrations").IsSelected = Tab == "registrations";
        }

        public async Task OpenAsync()
        {
            await RefreshRegistrationsAsync();
            await SelectAsync(Tab);
        }

        [RelayCommand] private Task SelectTab(Choice tab) => SelectAsync(tab.Key);

        private async Task SelectAsync(string key)
        {
            Tab = key;
            foreach (var t in Tabs) t.IsSelected = t.Key == key;
            Status = "Yükleniyor…";
            try
            {
                switch (key)
                {
                    case "overview": await LoadOverviewAsync(); break;
                    case "users": await LoadUsersAsync(); break;
                    case "roles": await LoadRolesAsync(); break;
                    case "registrations": await RefreshRegistrationsAsync(); LoadRegistrations(); break;
                    case "announcement": await LoadAnnouncementAsync(); break;
                    case "radio": await LoadRadioAsync(); break;
                    case "storage": await LoadStorageAsync(); break;
                }
                Status = "";
            }
            catch (Exception ex) { Status = "Hata: " + ex.Message; }
        }

        // ── Özet ─────────────────────────────────────────────────────────
        private async Task LoadOverviewAsync()
        {
            JsonElement counts = default, packs = default, announcement = default, presence = default;
            await Task.WhenAll(
                Try(async () => counts = await _core.Call("feedback.counts")),
                Try(async () => packs = await _core.Call("packs.list")),
                Try(async () => announcement = (await _core.Call("announcement.get")).Get("announcement")),
                Try(async () => presence = await _core.Call("community.presence")),
                main.Servers.Servers.Count == 0 ? main.Servers.LoadAsync() : Task.WhenAll(main.Servers.Servers.Select(s => s.PingAsync())));
            var members = await main.MembersAsync();

            int launcherFeedback = counts.Int("launcher");
            var packList = packs.Items("packs").ToList();
            int packFeedback = packList.Sum(p => p.Int("openFeedback"));
            var online = presence.Items().Where(p => p.Str("username") != main.Username).ToList();
            int playing = online.Count(p => p.Str("state") == "playing");
            var servers = main.Servers.Servers;

            Cards.Clear();
            Cards.Add(new AdminCard("Kayıt istekleri", _registrations.Count.ToString(CultureInfo.InvariantCulture),
                _registrations.Count > 0 ? string.Join(", ", _registrations.Take(3).Select(r => r.Str("username"))) + (_registrations.Count > 3 ? "…" : "") : "Bekleyen istek yok.",
                _registrations.Count > 0, _registrations.Count > 0 ? "İncele" : null, () => SelectAsync("registrations")));
            Cards.Add(new AdminCard("Launcher geri bildirimleri", launcherFeedback.ToString(CultureInfo.InvariantCulture),
                launcherFeedback > 0 ? "Açık hata bildirimi ya da öneri var." : "Açık bildirim yok.", launcherFeedback > 0, null, null));
            Cards.Add(new AdminCard("Paket geri bildirimleri", packFeedback.ToString(CultureInfo.InvariantCulture),
                packFeedback > 0 ? string.Join(", ", packList.Where(p => p.Int("openFeedback") > 0).Select(p => $"{p.Str("name")} ({p.Int("openFeedback")})")) : "Açık bildirim yok.",
                packFeedback > 0, "Paketlere git", () => { main.SelectedTab = main.Tabs[1]; return Task.CompletedTask; }));
            Cards.Add(new AdminCard("Çevrimiçi", online.Count.ToString(CultureInfo.InvariantCulture),
                online.Count > 0 ? $"{playing} kişi oyunda · {members.Count} üye" : $"{members.Count} üye, şu an kimse çevrimiçi değil.", false, "Kullanıcılar", () => SelectAsync("users")));
            Cards.Add(new AdminCard("Duyuru", announcement.ValueKind == JsonValueKind.Object ? "Yayında" : "Yok",
                announcement.ValueKind == JsonValueKind.Object ? announcement.Str("text") ?? "" : "Oyna sayfasının üstünde herkese bir mesaj gösterebilirsin.",
                false, announcement.ValueKind == JsonValueKind.Object ? "Düzenle" : "Duyuru yaz", () => SelectAsync("announcement")));
            Cards.Add(new AdminCard("Sunucular", servers.Count.ToString(CultureInfo.InvariantCulture),
                servers.Count == 0 ? "Henüz sunucu eklenmemiş." : $"{servers.Count(s => s.IsOnline)} açık · {servers.Sum(s => int.TryParse(s.Players.Split('/')[0], out int n) ? n : 0)} oyuncu",
                false, "Sunuculara git", () => { main.SelectedTab = main.Tabs[2]; return Task.CompletedTask; }));

            static async Task Try(Func<Task> run) { try { await run(); } catch { /* kart boş kalır */ } }
        }

        // ── Kullanıcılar ─────────────────────────────────────────────────
        private async Task LoadUsersAsync()
        {
            var users = await _core.Call("admin.users");
            try { await LoadRoleListAsync(); } catch { _roles = new(); }
            Users.Clear();
            foreach (var u in users.Items()) Users.Add(new AdminUserRow(this, u, _roles, main.Username));
            Summary = $"{Users.Count} kullanıcı";
        }

        private async Task LoadRoleListAsync()
        {
            _roles = (await _core.Call("admin.roles")).Items()
                .Select(r => new AdminRole(r.Str("id") ?? "", r.Str("name") ?? "", r.Str("color") ?? "#c9a992", r.Int("members"), r.Int("packs"))).ToList();
        }

        [RelayCommand] private Task AddUser() => EditUserAsync(null);

        public async Task EditUserAsync(AdminUserRow? user)
        {
            if (await main.ShowAsync(new UserEditorModal(user, _roles, main.Username)) is true) await LoadUsersAsync();
        }

        public async Task DeleteUserAsync(AdminUserRow user)
        {
            if (!await main.ConfirmAsync("Kullanıcı sil", $"'{user.Username}' silinsin mi? Hesap ve profil resmi silinir; paylaştığı paketler yerinde kalır. Bu işlem geri alınamaz.", "Sil", danger: true)) return;
            try { await _core.Call("admin.deleteUser", new { id = user.Id, username = user.Username }); }
            catch (Exception ex) { await main.AlertAsync("Silinemedi", ex.Message); }
            await LoadUsersAsync();
        }

        // ── Roller ───────────────────────────────────────────────────────
        private async Task LoadRolesAsync()
        {
            await LoadRoleListAsync();
            Roles.Clear();
            foreach (var r in _roles) Roles.Add(r);
            Summary = Roles.Count > 0 ? $"{Roles.Count} rol" : "Henüz rol yok";
        }

        [RelayCommand] private Task AddRole() => EditRoleAsync(null);
        [RelayCommand] private Task EditRole(AdminRole role) => EditRoleAsync(role);

        private async Task EditRoleAsync(AdminRole? role)
        {
            if (await main.ShowAsync(new RoleEditorModal(role)) is true) await LoadRolesAsync();
        }

        [RelayCommand]
        private async Task DeleteRole(AdminRole role)
        {
            if (!await main.ConfirmAsync("Rolü sil", $"\"{role.Name}\" silinsin mi? {(role.Members > 0 ? $"{role.Members} kişiden kaldırılır. " : "")}Bir paketin erişimini belirleyen rol silinemez.", "Sil", danger: true)) return;
            try { await _core.Call("admin.deleteRole", new { id = role.Id }); }
            catch (Exception ex) { await main.AlertAsync("Silinemedi", ex.Message); }
            await LoadRolesAsync();
        }

        // ── Kayıt istekleri ──────────────────────────────────────────────
        private void LoadRegistrations()
        {
            Registrations.Clear();
            foreach (var r in _registrations) Registrations.Add(new RegistrationRow(this, r));
            Summary = Registrations.Count > 0 ? $"{Registrations.Count} bekleyen istek" : "";
            NoRegistrations = Registrations.Count == 0;
        }

        public async Task DecideAsync(RegistrationRow row, bool approve)
        {
            if (!approve && !await main.ConfirmAsync("Kayıt isteğini reddet", $"{row.Username} ({row.Email}) isteği silinsin mi? Bu kişi isterse tekrar kayıt olabilir.", "Reddet", danger: true)) return;
            row.Busy = true;
            try { await _core.Call(approve ? "admin.approveRegistration" : "admin.rejectRegistration", new { id = row.Id, username = row.Username }); }
            catch (Exception ex) { await main.AlertAsync(approve ? "Onaylanamadı" : "Reddedilemedi", ex.Message); }
            await RefreshRegistrationsAsync();
            LoadRegistrations();
        }

        // ── Duyuru ───────────────────────────────────────────────────────
        private async Task LoadAnnouncementAsync()
        {
            Duration ??= DurationOptions[0];
            AnnouncementNote = null;
            var a = (await _core.Call("announcement.get")).Get("announcement");
            if (a.ValueKind != JsonValueKind.Object) { CurrentAnnouncement = null; _currentAnnouncementId = null; return; }
            _currentAnnouncementId = a.Str("id");
            CurrentAnnouncement = a.Str("text");
            CurrentAnnouncementMeta = "Şu anki duyuru" + (a.Str("expiresAt") is { } exp && DateTime.TryParse(exp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? $" · {at.ToLocalTime().ToString("d MMMM HH:mm", CultureInfo.GetCultureInfo("tr-TR"))} tarihine kadar" : "");
        }

        [RelayCommand]
        private async Task PublishAnnouncement()
        {
            if (AnnouncementText.Trim().Length == 0) { AnnouncementNote = "Önce bir şey yaz."; return; }
            try
            {
                await _core.Call("announcement.post", new { text = AnnouncementText, hours = int.Parse(Duration?.Key ?? "0", CultureInfo.InvariantCulture) });
                AnnouncementText = "";
                UiState.Set("announcementDismissed", null);
                await LoadAnnouncementAsync();
                _ = main.Home.RefreshAnnouncementAsync();
            }
            catch (Exception ex) { AnnouncementNote = ex.Message; }
        }

        [RelayCommand]
        private async Task RemoveAnnouncement()
        {
            try
            {
                await _core.Call("announcement.delete", new { id = _currentAnnouncementId });
                await LoadAnnouncementAsync();
                _ = main.Home.RefreshAnnouncementAsync();
            }
            catch (Exception ex) { await main.AlertAsync("Kaldırılamadı", ex.Message); }
        }

        // ── Radyo ────────────────────────────────────────────────────────
        private async Task LoadRadioAsync()
        {
            var data = await _core.Call("radio.adminList");
            Songs.Clear();
            foreach (var s in data.Items("songs")) Songs.Add(new SongRow(this, s));
            double used = data.Num("usedBytes"), cap = data.Num("capBytes");
            RadioUsage = $"{Songs.Count} şarkı · {SettingsViewModel.FormatBytes(used)} / {SettingsViewModel.FormatBytes(cap)}";
            RadioFraction = cap > 0 ? Math.Min(1, used / cap) : 0;
            RadioReady = data.Get("ready").ValueKind != JsonValueKind.False;
        }

        [RelayCommand]
        private async Task UploadSong()
        {
            RadioNote = null;
            UploadText = "Yükleniyor…";
            try
            {
                var r = await _core.Call("radio.upload");
                if (r.Bool("uploaded")) await LoadRadioAsync();
            }
            catch (Exception ex) { RadioNote = ex.Message; }
            UploadText = "Şarkı yükle";
        }

        public async Task SaveSongAsync(SongRow song)
        {
            if (song.Title.Trim() == song.SavedTitle && song.Artist.Trim() == song.SavedArtist) return;
            try
            {
                var updated = await _core.Call("radio.updateSong", new { id = song.Id, title = song.Title, artist = song.Artist });
                song.SavedTitle = updated.Str("title") ?? song.Title;
                song.SavedArtist = updated.Str("artist") ?? song.Artist;
            }
            catch (Exception ex)
            {
                song.Title = song.SavedTitle;
                song.Artist = song.SavedArtist;
                await main.AlertAsync("Kaydedilemedi", ex.Message);
            }
        }

        public async Task DeleteSongAsync(SongRow song)
        {
            if (!await main.ConfirmAsync("Şarkıyı sil", $"\"{song.Title}\" vulu Radyo'dan silinsin mi?", "Sil", danger: true)) return;
            try { await _core.Call("radio.deleteSong", new { id = song.Id }); await LoadRadioAsync(); }
            catch (Exception ex) { await main.AlertAsync("Silinemedi", ex.Message); }
        }

        // ── Alan ─────────────────────────────────────────────────────────
        private async Task LoadStorageAsync()
        {
            var s = await _core.Call("admin.storage");
            double limit = Math.Max(1, s.Num("limitBytes"));
            var parts = s.Items("parts").ToList();
            double known = parts.Sum(p => p.Num("bytes"));
            double used = s.Get("usedBytes").ValueKind == JsonValueKind.Number ? Math.Max(s.Num("usedBytes"), known) : known;
            double ratio = used / limit;
            StorageTotal = $"{SettingsViewModel.FormatBytes(used)} / {SettingsViewModel.FormatBytes(limit)} kullanılıyor (%{Math.Round(ratio * 100)})";
            StorageWarning = ratio >= 0.9 ? "Alan neredeyse dolu. Dolarsa paket yayınlama ve resim yüklemeleri hata vermeye başlar; kullanılmayan paketleri kaldırmayı düşün."
                : ratio >= 0.7 ? "Alanın çoğu doldu. Büyük paketleri ve eski ekran görüntülerini gözden geçirmek iyi olabilir." : null;
            StorageParts.Clear();
            foreach (var p in parts)
                StorageParts.Add(new StoragePart(p.Str("label") ?? "", SettingsViewModel.FormatBytes(p.Num("bytes")),
                    new SolidColorBrush(Color.Parse(StorageColors.GetValueOrDefault(p.Str("key") ?? "", "#A1958A"))), p.Num("bytes") / limit));
            StorageParts.Add(new StoragePart("Diğer (kullanıcılar, paket listesi, dizinler)", SettingsViewModel.FormatBytes(Math.Max(0, used - known)),
                new SolidColorBrush(Color.Parse("#A1958A")), Math.Max(0, used - known) / limit));
        }
    }

    /// <summary>Kullanıcı ekle / düzenle. Sonuç true ise kaydedildi.</summary>
    internal sealed partial class UserEditorModal : ModalViewModel
    {
        private readonly AdminUserRow? _user;

        public UserEditorModal(AdminUserRow? user, List<AdminRole> roles, string me)
        {
            _user = user;
            IsSelf = user?.Username == me;
            _username = user?.Username ?? "";
            _email = user?.Data.Str("email") ?? "";
            _isAdmin = user?.IsAdmin ?? false;
            _isActive = user?.IsActive ?? true;
            var mine = user?.Data.Items("roles").Select(r => r.GetString()).ToHashSet() ?? new HashSet<string?>();
            foreach (var r in roles) RoleBoxes.Add(new RoleBox(r, mine.Contains(r.Id)));
        }

        public bool IsEdit => _user != null;
        public bool IsNew => !IsEdit;
        public bool IsSelf { get; }
        public bool CanChangeFlags => !IsSelf;
        public string Title => IsEdit ? $"Düzenle — {_user!.Username}" : "Yeni kullanıcı";
        public string SaveText => IsEdit ? "Kaydet" : "Ekle";
        public string PasswordLabel => IsEdit ? "Yeni şifre" : "Şifre";
        public string PasswordHint => IsEdit ? "Boş bırakırsan değişmez" : "En az 6 karakter";
        public ObservableCollection<RoleBox> RoleBoxes { get; } = new();
        public bool NoRoles => RoleBoxes.Count == 0;
        [ObservableProperty] private string _username;
        [ObservableProperty] private string _email;
        [ObservableProperty] private string _password = "";
        [ObservableProperty] private bool _isAdmin;
        [ObservableProperty] private bool _isActive;
        [ObservableProperty] private string? _error;

        partial void OnUsernameChanged(string value)
        {
            string clean = System.Text.RegularExpressions.Regex.Replace(value, "[^a-zA-Z0-9_]", "");
            if (clean != value) Username = clean;
        }

        [RelayCommand]
        private async Task Save()
        {
            Error = null;
            try
            {
                await CoreClient.Instance.Call("admin.saveUser", new
                {
                    id = _user?.Id ?? "", username = Username.Trim(), email = Email.Trim(), password = Password,
                    isAdmin = IsAdmin, isActive = IsActive, roles = RoleBoxes.Where(r => r.Checked).Select(r => r.Role.Id).ToList(),
                });
                Close(true);
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    internal sealed partial class RoleBox(AdminRole role, bool isChecked) : ObservableObject
    {
        public AdminRole Role { get; } = role;
        [ObservableProperty] private bool _checked = isChecked;
    }

    /// <summary>Rol ekle / düzenle: ad ve renk. Sonuç true ise kaydedildi.</summary>
    internal sealed partial class RoleEditorModal : ModalViewModel
    {
        private readonly AdminRole? _role;

        public RoleEditorModal(AdminRole? role)
        {
            _role = role;
            _name = role?.Name ?? "";
            _color = role?.Color ?? AdminViewModel.RoleColors[0];
        }

        public string Title => _role != null ? $"Rolü düzenle — {_role.Name}" : "Yeni rol";
        public string SaveText => _role != null ? "Kaydet" : "Ekle";
        public List<string> Colors { get; } = AdminViewModel.RoleColors.ToList();
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Preview))]
        private string _name;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Brush))]
        private string _color;
        [ObservableProperty] private string? _error;
        public string Preview => Name.Trim().Length > 0 ? Name.Trim() : "Rol";
        public IBrush Brush => Avalonia.Media.Color.TryParse(Color, out var c) ? new SolidColorBrush(c) : Brushes.Gray;

        [RelayCommand] private void PickColor(string color) => Color = color;

        [RelayCommand]
        private async Task Save()
        {
            Error = null;
            try
            {
                await CoreClient.Instance.Call("admin.saveRole", new { id = _role?.Id ?? "", name = Name.Trim(), color = Color });
                Close(true);
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }
}
