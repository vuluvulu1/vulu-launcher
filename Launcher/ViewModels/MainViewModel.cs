using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>Başlık çubuğundaki sayfa sekmesi.</summary>
    internal sealed partial class PageTab(string key, string title, string icon, object page) : ObservableObject
    {
        public string Key { get; } = key;
        public string Title { get; } = title;
        /// <summary>Simge (SVG yolu, 24×24 kutuda).</summary>
        public Avalonia.Media.Geometry Icon { get; } = Avalonia.Media.StreamGeometry.Parse(icon);
        public object Page { get; } = page;
        [ObservableProperty] private bool _isVisible = true;
        [ObservableProperty] private bool _isActive;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBadge))]
        private int _badge;
        public bool HasBadge => Badge > 0;
    }

    /// <summary>Sağ altta birkaç saniye görünen bildirim kartı (launcher içinde).</summary>
    internal sealed record ToastItem(string Title, string Body);

    /// <summary>Pencerenin tamamı: giriş ekranı ya da sekmeli ana görünüm, bildirim kartları.</summary>
    internal sealed partial class MainViewModel : ObservableObject
    {
        private readonly CoreClient _core = CoreClient.Instance;

        public LoginViewModel Login { get; }
        public HomeViewModel Home { get; }
        public SettingsViewModel Settings { get; }
        public DiscoverViewModel Discover { get; }
        public ServersViewModel Servers { get; }
        public SocialViewModel Social { get; }
        public ProfileViewModel Profile { get; }
        public AdminViewModel Admin { get; }
        public ObservableCollection<PageTab> Tabs { get; } = new();
        public ObservableCollection<ToastItem> Toasts { get; } = new();
        /// <summary>Açık kutular; sonuncusu en üstte.</summary>
        public ObservableCollection<ModalViewModel> Modals { get; } = new();

        [ObservableProperty] private bool _loggedIn;
        [ObservableProperty] private string _username = "";
        [ObservableProperty] private string _userInitial = "";
        [ObservableProperty] private string _version = "";
        [ObservableProperty] private bool _isAdmin;
        /// <summary>Başlık çubuğundaki profil resmi.</summary>
        [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _myAvatar;
        [ObservableProperty] private PageTab? _selectedTab;

        public MainViewModel()
        {
            Login = new LoginViewModel(EnterAsync);
            Home = new HomeViewModel();
            Home.Main = this;
            // Güncellemesi olan paketler Keşfet sekmesinde rozet olur
            Home.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(HomeViewModel.PackUpdateCount)) Tabs[1].Badge = Home.PackUpdateCount;
            };
            Settings = new SettingsViewModel(this);
            Discover = new DiscoverViewModel(this);
            Servers = new ServersViewModel(this);
            Social = new SocialViewModel(this);
            Profile = new ProfileViewModel(this);
            Admin = new AdminViewModel(this);
            Tabs.Add(new PageTab("home", "Oyna", "M7 4l13 8-13 8z", Home));
            Tabs.Add(new PageTab("discover", "Keşfet", "M12 3a9 9 0 1 0 0 18a9 9 0 0 0 0-18zM15.5 8.5l-2 5-5 2 2-5z", Discover));
            Tabs.Add(new PageTab("servers", "Sunucular", "M3 4h18v7H3zM3 13h18v7H3zM7 7.5h.01M7 16.5h.01", Servers));
            Tabs.Add(new PageTab("social", "Sosyal", "M9 4.8a3.2 3.2 0 1 0 0 6.4a3.2 3.2 0 0 0 0-6.4zM3.5 19a5.5 5.5 0 0 1 11 0M17 6.5a2.5 2.5 0 1 0 0 5a2.5 2.5 0 0 0 0-5zM15.5 14.2A4.5 4.5 0 0 1 20.5 19", Social));
            Tabs.Add(new PageTab("settings", "Ayarlar", "M12 9a3 3 0 1 0 0 6a3 3 0 0 0 0-6zM12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M5.3 18.7l2.1-2.1M16.6 7.4l2.1-2.1", Settings));
            Tabs.Add(new PageTab("admin", "Admin", "M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z", Admin) { IsVisible = false });
            // Profil başlık çubuğunda sekme olarak görünmez; kullanıcı adına ya da bir kişiye tıklanınca açılır
            Tabs.Add(new PageTab("profile", "Profil", "M12 12a4 4 0 1 0 0-8a4 4 0 0 0 0 8zM4 20a8 8 0 0 1 16 0", Profile) { IsVisible = false });
            SelectedTab = Tabs[0];

            _core.Event += OnCoreEvent;
        }

        /// <summary>Açılış: oturum zaten açıksa (bildirim alanından dönüş) doğrudan ana görünüme, yoksa girişe.</summary>
        public async Task StartAsync()
        {
#if DEBUG
            // Yalnızca geliştirmede: giriş yapmadan örnek verilerle sayfalar (arayüz denemesi için)
            if (Environment.GetEnvironmentVariable("VULU_UI_PREVIEW") == "1")
            {
                Username = "önizleme";
                UserInitial = "Ö";
                LoggedIn = true;
                Version = "v" + (await _core.Call("app.init")).Str("version");
                Home.LoadPreview();
                // VULU_UI_PAGE=settings: o sekmeyle açılır
                if (Tabs.FirstOrDefault(t => t.Key == Environment.GetEnvironmentVariable("VULU_UI_PAGE")) is { } page) SelectedTab = page;
                return;
            }
#endif
            try
            {
                var init = await _core.Call("app.init");
                Version = "v" + init.Str("version");
                Avatars.ApiBase = init.Str("apiBaseUrl") ?? "";
                Home.ApplyInit(init);
                if (init.Get("session") is { ValueKind: JsonValueKind.Object } session)
                {
                    Home.GameRunning = init.Bool("gameRunning");
                    await EnterAsync(session);
                    return;
                }
                Login.Prepare(init, auto: true);
            }
            catch (Exception ex)
            {
                Login.SetStatus(ex.Message, true);
            }
        }

        private async Task EnterAsync(JsonElement session)
        {
            Username = session.Str("username") ?? "";
            UserInitial = Username.Length > 0 ? Username[..1].ToUpperInvariant() : "";
            IsAdmin = session.Bool("isAdmin");
            Tabs.First(t => t.Key == "admin").IsVisible = IsAdmin;
            _members = null;
            SelectedTab = Tabs[0];
            LoggedIn = true;
            _ = Social.StartAsync();
            _ = RefreshMyAvatarAsync();
            if (IsAdmin) _ = Admin.RefreshRegistrationsAsync();
            await Home.LoadAsync();
        }

        /// <summary>Sorup çıkış yapar; oyun çalışıyorsa Core izin vermez, nedeni gösterilir.</summary>
        [RelayCommand]
        public async Task LogoutAsync()
        {
            if (!await ConfirmAsync("Çıkış yap", "Hesaptan çıkış yapılsın mı?", "Çıkış yap")) return;
            try { await _core.Call("auth.logout"); }
            catch (Exception ex) { await AlertAsync("Çıkış yapılamadı", ex.Message); return; }
            LoggedIn = false;
            Social.Stop();
            var init = await _core.Call("app.init");
            Login.Prepare(init, auto: false);
        }

        private Dictionary<string, long>? _members;

        /// <summary>Profil resmi ya da adı değişince üye listesi yeniden alınır.</summary>
        public void ForgetMembers() => _members = null;

        /// <summary>Kendi profilin (başlık çubuğundaki kullanıcı adına tıklanınca).</summary>
        [RelayCommand]
        private Task OpenMyProfile() => OpenProfileAsync(Username);

        public async Task OpenProfileAsync(string username)
        {
            SelectedTab = Tabs.First(t => t.Key == "profile");
            await Profile.OpenAsync(username);
        }

        public async Task RefreshMyAvatarAsync()
        {
            try
            {
                var me = await _core.Call("profile.me");
                MyAvatar = await Images.LoadAsync(Avatars.Url(Username, (long)me.Num("avatarVersion")), 64);
            }
            catch { /* baş harf kalır */ }
        }

        /// <summary>Alınmış üye listesi (henüz alınmadıysa boş).</summary>
        public Dictionary<string, long> MembersCached => _members ?? new();

        /// <summary>Üyeler: kullanıcı adı → profil resmi sürümü (avatarlar için). Bir kez alınır.</summary>
        public async Task<Dictionary<string, long>> MembersAsync()
        {
            if (_members != null) return _members;
            try
            {
                var list = await _core.Call("profile.list");
                _members = list.Items().ToDictionary(m => m.Str("username") ?? "", m => (long)m.Num("avatarVersion"));
            }
            catch { return new(); }
            return _members;
        }

        /// <summary>Kutuyu açar ve kapanmasını bekler; sonuç kutunun verdiği değerdir (vazgeçilirse null).</summary>
        public async Task<object?> ShowAsync(ModalViewModel modal)
        {
            Modals.Add(modal);
            try { return await modal.Result; }
            finally { Modals.Remove(modal); }
        }

        /// <summary>Soru kutusu: tamam denirse true.</summary>
        public async Task<bool> ConfirmAsync(string title, string message, string okText, string? cancelText = "Vazgeç", bool danger = false) =>
            await ShowAsync(new DialogViewModel(title, message, okText, cancelText, danger)) is true;

        /// <summary>Keşfet'i bu instance'a içerik kurmak için açar (son kullanılan kaynak: CurseForge ya da Modrinth).</summary>
        public async Task OpenDiscoverForAsync(InstanceItem instance)
        {
            Discover.Content.PresetTarget(instance.Id);
            string source = UiState.Get("discoverSource") == "modrinth" ? "modrinth" : "curseforge";
            Discover.SetSource(source);
            SelectedTab = Tabs[1];
            await Task.CompletedTask;
        }

        /// <summary>Oyna sekmesine geçer; verilen paket seçili olur (kurulumdan sonra).</summary>
        public async Task ShowHomeAsync(string? selectId)
        {
            SelectedTab = Tabs[0];
            await Home.ReloadInstancesAsync(selectId);
        }

        public Task AlertAsync(string title, string message) => ConfirmAsync(title, message, "Tamam", null);

        [RelayCommand] private void Minimize() => _core.Send("window.minimize");
        [RelayCommand] private void Close() => _core.Send("window.close");

        [RelayCommand]
        private void SelectTab(PageTab tab) => SelectedTab = tab;

        partial void OnSelectedTabChanged(PageTab? value)
        {
            foreach (var tab in Tabs) tab.IsActive = tab == value;
            // Ayarlar her açılışta baştan okunur (başka yerden değişen ayarlar da görünsün)
            if (value?.Page is SettingsViewModel settings) _ = settings.LoadAsync();
            if (value?.Page is DiscoverViewModel discover) _ = discover.OpenAsync();
            if (value?.Page is ServersViewModel servers) _ = servers.LoadAsync();
            if (value?.Page is SocialViewModel social) _ = social.OpenAsync();
            if (value?.Page is AdminViewModel admin) _ = admin.OpenAsync();
            // Başka sayfada mod kurulduysa ya da kaldırıldıysa Oyna'daki sayılar güncel olsun
            if (value?.Page is HomeViewModel && LoggedIn) Home.RefreshDetails();
        }

        public void ShowToast(string title, string body)
        {
            var toast = new ToastItem(title, body);
            Toasts.Add(toast);
            while (Toasts.Count > 3) Toasts.RemoveAt(0);
            DispatcherTimer.RunOnce(() => Toasts.Remove(toast), TimeSpan.FromSeconds(6));
        }

        private void OnCoreEvent(string name, JsonElement data)
        {
            switch (name)
            {
                case "dmUpdate":
                    Social.OnDmUpdate(data);
                    break;
                case "toast":
                    ShowToast(data.Str("title") ?? "", data.Str("body") ?? "");
                    break;
                default:
                    Home.OnCoreEvent(name, data);
                    break;
            }
        }
    }

    /// <summary>Henüz native arayüze taşınmamış sayfa.</summary>
    internal sealed class PlaceholderViewModel(string title)
    {
        public string Title { get; } = title;
    }
}
