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
        /// <summary>Tanıtım rehberinde bu sekme anlatılıyor.</summary>
        [ObservableProperty] private bool _isGlowing;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBadge))]
        private int _badge;
        public bool HasBadge => Badge > 0;
    }

    /// <summary>Sağ altta birkaç saniye görünen bildirim kartı (launcher içinde); tıklanınca etiketi ilgili sayfayı açar.</summary>
    internal sealed partial class ToastItem(MainViewModel main, string title, string body, string? tag, string? avatar) : ObservableObject
    {
        public string Title { get; } = title;
        public string Body { get; } = body;
        public bool HasBody => Body.Length > 0;
        [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _avatar;
        /// <summary>Kapanıyor: sağa kayarak çıkar (web: .toast.leaving .26s).</summary>
        [ObservableProperty] private bool _leaving;
        public async Task LoadAsync() => Avatar = await Images.LoadAsync(avatar, 64);
        [RelayCommand]
        private async Task Open()
        {
            main.Toasts.Remove(this);
            if (tag != null) await main.RouteAsync(tag);
        }
    }

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
        public NotificationsViewModel Notifications { get; }
        public RadioViewModel Radio { get; }
        public DownloadsViewModel Downloads { get; }
        public SplashViewModel Splash { get; } = new();
        public ObservableCollection<PageTab> Tabs { get; } = new();
        public ObservableCollection<ToastItem> Toasts { get; } = new();
        /// <summary>Açık kutular; sonuncusu en üstte.</summary>
        public ObservableCollection<ModalViewModel> Modals { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HomeOverlay), nameof(ShowBackdrop))]
        private bool _loggedIn;
        /// <summary>Oyna açık: paketin resmi başlık çubuğunun altına kadar uzanır (Meşale).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HomeOverlay), nameof(ShowBackdrop))]
        private bool _isHomePage = true;
        public bool HomeOverlay => LoggedIn && IsHomePage;
        /// <summary>Oyna dışındaki sayfaların zemini: seçili paketin resmi, bulanık ve karartılmış.</summary>
        public bool ShowBackdrop => LoggedIn && !IsHomePage;
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
            Notifications = new NotificationsViewModel(this);
            Radio = new RadioViewModel(this);
            Downloads = new DownloadsViewModel(this);
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
                Version = "v" + (await _core.Call("app.init")).Text("version");
                Home.LoadPreview();
                // VULU_UI_PAGE=settings: o sekmeyle açılır
                if (Tabs.FirstOrDefault(t => t.Key == Environment.GetEnvironmentVariable("VULU_UI_PAGE")) is { } page) SelectedTab = page;
                return;
            }
#endif
            try
            {
                var init = await _core.Call("app.init");
                Version = "v" + init.Text("version");
                Avatars.ApiBase = init.Text("apiBaseUrl") ?? "";
                Home.ApplyInit(init);
                if (init.Get("session") is { ValueKind: JsonValueKind.Object } session)
                {
                    // Pencere bildirim alanından geri açıldı: oturum açık, perde ve rehber atlanır
                    Home.GameRunning = init.Bool("gameRunning");
                    await EnterAsync(session, resume: true);
                    return;
                }
                string saved = init.Text("savedUsername") ?? "";
                if (!AppSettings.Current.SkipSplash && saved.Length > 0 && init.Bool("hasSavedPassword"))
                {
                    // Kayıtlı hesapla giriş perde kapalıyken yapılır; olmazsa perde giriş ekranına açılır
                    Splash.Begin(saved);
                    using var slow = new CancellationTokenSource();
                    _ = Task.Delay(SplashViewModel.OpenAtMs - 600, slow.Token).ContinueWith(t =>
                    {
                        if (!t.IsCanceled) Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!LoggedIn && Splash.IsVisible) Splash.Status = "Sunucuya bağlanılıyor…"; });
                    }, TaskScheduler.Default);
                    await Login.Prepare(init, auto: true);
                    slow.Cancel();
                    if (!LoggedIn) await Splash.OpenAsync();
                    return;
                }
                await Login.Prepare(init, auto: true);
            }
            catch (Exception ex)
            {
                Login.SetStatus(ex.Message, true);
                await Splash.OpenAsync();
            }
        }

        private Task EnterAsync(JsonElement session) => EnterAsync(session, resume: false);

        /// <param name="resume">Oturum zaten açıktı (pencere yeniden açıldı): perde ve rehber gösterilmez.</param>
        private async Task EnterAsync(JsonElement session, bool resume)
        {
            // Perde (otomatik girişte zaten kapalı) arayüzden önce gelir; ana ekran arkada hazırlanır
            bool useSplash = !resume && !AppSettings.Current.SkipSplash;
            if (useSplash && !Splash.IsVisible) Splash.Begin(session.Text("username") ?? "");
            Username = session.Text("username") ?? "";
            UserInitial = Username.Length > 0 ? Username[..1].ToUpperInvariant() : "";
            IsAdmin = session.Bool("isAdmin");
            Tabs.First(t => t.Key == "admin").IsVisible = IsAdmin;
            _members = null;
            SelectedTab = Tabs[0];
            LoggedIn = true;
            _ = Social.StartAsync();
            _ = RefreshMyAvatarAsync();
            if (IsAdmin) _ = Admin.RefreshRegistrationsAsync();
            _ = Notifications.LoadAsync();
            _ = RefreshFeedbackCountAsync();
            await Home.LoadAsync();
            if (useSplash) await Splash.OpenAsync();
            Radio.StartIfEnabled();
            // Bildirime tıklanarak ya da masaüstü kısayoluyla açıldıysa bekleyen istek şimdi yapılır
            await TakeNotificationAsync();
            await RunStartupActionAsync();
            if (!resume && UiState.Get("tourDone") != "true") await ShowTourAsync();
        }

        /// <summary>Tanıtım rehberi; bir kez gösterilir (Ayarlar → Launcher'dan yeniden açılır).</summary>
        public async Task ShowTourAsync()
        {
            var tour = new TourModal(Tabs);
            var result = await ShowAsync(tour);
            UiState.Set("tourDone", "true");
            tour.Glow(null);
            if (result is string key && Tabs.FirstOrDefault(t => t.Key == key && t.IsVisible) is { } tab) SelectedTab = tab;
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
            Radio.Stop();
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
                var list = (await _core.Call("profile.list")).Clone();
                _members = list.Items().ToDictionary(m => m.Text("username") ?? "", m => (long)m.Num("avatarVersion"));
                _memberInfo = list.Items().ToDictionary(m => m.Text("username") ?? "", m => m);
            }
            catch { return new(); }
            return _members;
        }

        private Dictionary<string, JsonElement>? _memberInfo;

        /// <summary>Üyenin listedeki bilgisi (biyografi, son görülme, admin); liste alınmadıysa boş.</summary>
        public JsonElement MemberInfo(string name) =>
            _memberInfo != null && _memberInfo.TryGetValue(name, out var m) ? m : default;

        /// <summary>"Son görülme: dün" (web: lastSeenText); üye bilgisi yoksa boş.</summary>
        public string LastSeenText(string name)
        {
            var info = MemberInfo(name);
            if (info.ValueKind != JsonValueKind.Object) return "";
            string ago = RelativeDay(info.Text("lastSeenAt"));
            return ago.Length > 0 ? "Son görülme: " + ago : "Henüz giriş yapmadı";
        }

        /// <summary>bugün, dün, "5 gün önce" ya da tarih (web: relativeDate, küçük harfle).</summary>
        public static string RelativeDay(string? iso)
        {
            if (iso == null || !DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var at)) return "";
            var local = at.ToLocalTime().Date;
            int days = (int)Math.Round((DateTime.Today - local).TotalDays);
            return days <= 0 ? "bugün" : days == 1 ? "dün" : days < 30 ? $"{days} gün önce"
                : local.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        }

        /// <summary>Masaüstü bildirimine tıklandıysa ilgili sayfa açılır.</summary>
        private async Task TakeNotificationAsync()
        {
            if (!LoggedIn) return;   // giriş yapılınca yeniden denenir
            try
            {
                var pending = await _core.Call("app.takeNotification");
                if (pending.Text("tag") is { } tag) await RouteAsync(tag);
            }
            catch { /* bekleyen yok */ }
        }

        /// <summary>Kısayoldan (ya da açıkken başka bir kısayoldan) gelen istek: paketi oyna ya da sunucuya bağlan.</summary>
        private async Task RunStartupActionAsync()
        {
            if (!LoggedIn) return;
            JsonElement action;
            try { action = await _core.Call("app.takeAction"); } catch { return; }
            if (action.ValueKind != JsonValueKind.Object) return;
            if (Home.GameRunning) { Home.AppendLog("Oyun zaten açık.", "warn"); return; }
            string id = action.Text("id") ?? "";
            if (action.Text("kind") == "play")
            {
                await ShowHomeAsync(id);
                if (Home.Selected?.Id != id) { await AlertAsync("Kısayol", "Bu kısayolun paketi artık yok. Kısayolu silebilirsin."); return; }
                await Home.PlayAsync(false, false, "");
            }
            else if (action.Text("kind") == "join")
            {
                if (Servers.Servers.Count == 0) await Servers.LoadAsync();
                var server = Servers.Servers.FirstOrDefault(s => s.Id == id);
                if (server == null) { await AlertAsync("Kısayol", "Bu kısayolun sunucusu artık vulu listesinde yok."); return; }
                await Servers.JoinAsync(server);
            }
        }

        /// <summary>Bildirimin etiketine göre ilgili sayfayı açar (masaüstü bildirimi, kart ya da bildirim merkezi).</summary>
        public async Task RouteAsync(string tag)
        {
            if (tag.StartsWith("dm:", StringComparison.Ordinal)) await Social.OpenConversationAsync(tag[3..]);
            else if (tag.StartsWith("invite:", StringComparison.Ordinal))
            {
                // Davet kartındaki "Katıl": sunucu vulu listesindeyse katılır, yoksa konuşma açılır
                var parts = tag.Split(':');
                if (Servers.Servers.Count == 0) await Servers.LoadAsync();
                var server = parts.Length > 2 ? Servers.Servers.FirstOrDefault(s => s.Id == parts[2]) : null;
                if (server != null) await Servers.JoinAsync(server);
                else if (parts.Length > 1) await Social.OpenConversationAsync(parts[1]);
            }
            else if (tag.StartsWith("profile:", StringComparison.Ordinal)) await OpenProfileAsync(tag[8..]);
            else if (tag.StartsWith("friend:", StringComparison.Ordinal)) SelectedTab = Tabs.First(t => t.Key == "social");
            else if (tag.StartsWith("shot:", StringComparison.Ordinal)) await Social.OpenSharedShotAsync(tag[5..]);
            else if (tag.StartsWith("registration:", StringComparison.Ordinal) && IsAdmin)
            {
                // Yeni kayıt isteği bildirimi: Admin → Kayıt istekleri
                var adminTab = Tabs.First(t => t.Key == "admin");
                Admin.Tab = "registrations";
                if (SelectedTab == adminTab) await Admin.OpenAsync();
                else SelectedTab = adminTab;
            }
            else if (tag == "crash")
            {
                try
                {
                    var crash = await _core.Call("crash.takePending");
                    if (crash.ValueKind == JsonValueKind.Object) await Home.ShowCrashAsync(crash);
                }
                catch { /* özet kalmamış */ }
            }
            else if (tag.StartsWith("pack:", StringComparison.Ordinal))
            {
                await ShowHomeAsync(tag[5..]);
                await Home.RefreshPackUpdatesAsync();
                if (Home.HasPackUpdate) await Home.UpdatePackCommand.ExecuteAsync(null);
            }
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

        /// <summary>Adminlere açık launcher bildirimlerinin sayısı (geri bildirim düğmesinde rozet).</summary>
        [ObservableProperty] private int _feedbackCount;
        public bool HasFeedbackCount => FeedbackCount > 0;
        partial void OnFeedbackCountChanged(int value) => OnPropertyChanged(nameof(HasFeedbackCount));

        public async Task RefreshFeedbackCountAsync()
        {
            if (!IsAdmin) { FeedbackCount = 0; return; }
            try { FeedbackCount = (await _core.Call("feedback.counts")).Int("launcher"); } catch { /* rozet sonra yenilenir */ }
        }

        /// <summary>Geri bildirim: "launcher", "general" (paket istekleri) ya da paket kimliği.</summary>
        public async Task OpenFeedbackAsync(string pack, string? packName = null)
        {
            await ShowAsync(new FeedbackModal(this, pack, packName));
            _ = RefreshFeedbackCountAsync();
        }

        [RelayCommand] private Task OpenLauncherFeedback() => OpenFeedbackAsync("launcher");

        /// <summary>Hızlı arama (Ctrl+K ya da büyüteç).</summary>
        [RelayCommand]
        private async Task OpenPalette()
        {
            if (!LoggedIn || Modals.Count > 0) return;
            if (await ShowAsync(new PaletteModal(this)) is PaletteItem item) await item.Run();
        }

        [RelayCommand] private void Minimize() => _core.Send("window.minimize");
        [RelayCommand] private void Close() => _core.Send("window.close");

        [RelayCommand]
        private void SelectTab(PageTab tab) => SelectedTab = tab;

        partial void OnSelectedTabChanged(PageTab? value)
        {
            foreach (var tab in Tabs) tab.IsActive = tab == value;
            IsHomePage = value?.Page is HomeViewModel;
            // Ayarlar her açılışta baştan okunur (başka yerden değişen ayarlar da görünsün)
            if (value?.Page is SettingsViewModel settings) _ = settings.LoadAsync();
            if (value?.Page is DiscoverViewModel discover) _ = discover.OpenAsync();
            if (value?.Page is ServersViewModel servers) _ = servers.LoadAsync();
            if (value?.Page is SocialViewModel social) _ = social.OpenAsync();
            if (value?.Page is AdminViewModel admin) _ = admin.OpenAsync();
            // Başka sayfada mod kurulduysa ya da kaldırıldıysa Oyna'daki sayılar güncel olsun
            if (value?.Page is HomeViewModel && LoggedIn) Home.RefreshDetails();
        }

        public void ShowToast(string title, string body, string? tag = null, string? avatar = null)
        {
            var toast = new ToastItem(this, title, body, tag, avatar);
            _ = toast.LoadAsync();
            Toasts.Add(toast);
            while (Toasts.Count > 3) Toasts.RemoveAt(0);
            DispatcherTimer.RunOnce(() =>
            {
                toast.Leaving = true;
                DispatcherTimer.RunOnce(() => Toasts.Remove(toast), TimeSpan.FromMilliseconds(260));
            }, TimeSpan.FromSeconds(6));
        }

        private void OnCoreEvent(string name, JsonElement data)
        {
            switch (name)
            {
                case "dmUpdate":
                    Social.OnDmUpdate(data);
                    break;
                case "toast":
                    ShowToast(data.Text("title") ?? "", data.Text("body") ?? "", data.Text("tag"), data.Text("avatar"));
                    break;
                case "download":
                    Downloads.OnDownload(data);
                    break;
                case "notification":
                    Notifications.OnNotification(data);
                    break;
                case "notificationClicked":
                    _ = TakeNotificationAsync();
                    break;
                case "startupAction":
                    _ = RunStartupActionAsync();
                    break;
                case "registrations":
                    // Bekleyen kayıt istekleri arka planda sorulur; Admin sekmesindeki rozet güncel kalsın
                    if (IsAdmin) Tabs.First(t => t.Key == "admin").Badge = data.Int("count");
                    break;
                case "shotEvent":
                    Home.AppendLog(data.Text("kind") == "like" ? $"♥ {data.Text("from")} ekran görüntünü beğendi." : $"💬 {data.Text("from")}: {data.Text("text")}", "accent");
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
