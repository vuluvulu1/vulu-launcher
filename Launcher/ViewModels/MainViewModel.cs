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
        /// <summary>Sekme çubuğunda (ya da rayın üst kısmında) görünür. Ayarlar ayrı bir dişli simgesiyle açılır.</summary>
        public bool InBar => IsVisible && Key != "settings";
        /// <summary>Kütüphane rayında üstüne gelince çıkan etiket: ad ve canlı bilgi ("Sunucular · 2 açık").</summary>
        [ObservableProperty] private string _tip = title;
        /// <summary>Canlı yeşil nokta (ör. açık bir sunucu var).</summary>
        [ObservableProperty] private bool _live;
        partial void OnIsVisibleChanged(bool value) => OnPropertyChanged(nameof(InBar));
    }

    /// <summary>Pencerenin tamamı: giriş ekranı ya da sekmeli ana görünüm, bildirim kartları.</summary>
    internal sealed partial class MainViewModel : ObservableObject
    {
        private readonly CoreClient _core = CoreClient.Instance;

        public LoginViewModel Login { get; }
        public HomeViewModel Home { get; }
        public SettingsViewModel Settings { get; }
        /// <summary>vulu Packs listesi (Yeni paket penceresi, paket güncelleme, sohbetteki paket kartları).</summary>
        public PacksViewModel Packs { get; }
        public ServersViewModel Servers { get; }
        public SocialViewModel Social { get; }
        public ProfileViewModel Profile { get; }
        public AdminViewModel Admin { get; }
        public NotificationsViewModel Notifications { get; }
        public RadioViewModel Radio { get; }
        public DownloadsViewModel Downloads { get; }
        public SplashViewModel Splash { get; } = new();
        public ObservableCollection<PageTab> Tabs { get; } = new();
        public ToastStack Toasts { get; }
        /// <summary>Açık kutular; sonuncusu en üstte.</summary>
        public ObservableCollection<ModalViewModel> Modals { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowShell), nameof(HomeOverlay), nameof(ShowBackdrop), nameof(ShowSynthwave), nameof(ShowAmbient))]
        private bool _loggedIn;
        /// <summary>
        /// Kayıtlı hesapla açılışta ana ekran, perdenin animasyonu başlamadan önce perdenin arkasında kurulur: kurulum arayüzü
        /// bir süre meşgul eder ve bu, animasyonun ortası yerine henüz hiçbir şeyin oynamadığı ilk ana denk gelir.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowShell), nameof(HomeOverlay), nameof(ShowBackdrop), nameof(ShowSynthwave), nameof(ShowAmbient))]
        private bool _warming;
        /// <summary>Ana ekran (sekmeler, sayfa) görünür: giriş yapıldı ya da perde arkasında önceden kuruluyor.</summary>
        public bool ShowShell => LoggedIn || Warming;
        /// <summary>Oyna açık: paketin resmi başlık çubuğunun altına kadar uzanır (Meşale).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HomeOverlay), nameof(ShowBackdrop), nameof(ShowSynthwave))]
        private bool _isHomePage = true;
        public bool HomeOverlay => ShowShell && IsHomePage;
        /// <summary>Oyna dışındaki sayfaların zemini: seçili paketin resmi, bulanık ve karartılmış.</summary>
        public bool ShowBackdrop => ShowShell && !IsHomePage && !Themes.HasScene && !Themes.IsGrafit && !Themes.IsKutuphane;
        /// <summary>Ufkun öbür ucu teması: Oyna dışındaki sayfalarda (girişte de) paket resmi yerine synthwave sahnesi.</summary>
        public bool ShowSynthwave => Themes.HasScene && !(ShowShell && IsHomePage);
        /// <summary>Kütüphane: bütün sayfaların arkasında seçili paketin resmi, çok bulanık ve koyu; üstten aşağı sönen sıcak renk.</summary>
        public bool ShowAmbient => ShowShell && Themes.IsKutuphane;

        /// <summary>Pencere verir: geçiş animasyonu (MainWindow.Transition).</summary>
        public Func<string, Action, Task>? PlayThemeTransition { get; set; }

        /// <summary>Temayı geçiş animasyonuyla değiştirir (Ayarlar › Tema).</summary>
        /// <remarks>
        /// Geçiş sürerken gelen seçimler birikmez: yalnızca en son seçilen tema, süren geçiş bitince uygulanır.
        /// (Art arda hızlı değiştirmek üst üste geçişler, yarım uygulanmış temalar ve çökme demekti.)
        /// </remarks>
        public async Task SwitchThemeAsync(string theme)
        {
            _queuedTheme = Themes.Normalize(theme);
            if (_switchingTheme) return;
            _switchingTheme = true;
            try
            {
                while (_queuedTheme is { } next)
                {
                    _queuedTheme = null;
                    if (next == Themes.Current) continue;
                    void Apply() { Themes.Apply(next); ThemeChanged(); }
                    if (PlayThemeTransition != null) await PlayThemeTransition(next, Apply);
                    else Apply();
                }
            }
            finally { _switchingTheme = false; }
        }

        private string? _queuedTheme;
        private bool _switchingTheme;

        /// <summary>Tema değişti (Ayarlar): zeminler yeniden seçilir.</summary>
        public void ThemeChanged()
        {
            OnPropertyChanged(nameof(ShowBackdrop));
            OnPropertyChanged(nameof(ShowSynthwave));
            OnPropertyChanged(nameof(ShowAmbient));
        }
        [ObservableProperty] private string _username = "";
        [ObservableProperty] private string _userInitial = "";
        [ObservableProperty] private string _version = "";
        [ObservableProperty] private bool _isAdmin;
        /// <summary>Başlık çubuğundaki profil resmi.</summary>
        [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _myAvatar;
        [ObservableProperty] private PageTab? _selectedTab;
        /// <summary>Ayarlar sekmesi: sekme çubuğunda değil, ayrı bir dişli simgesiyle açılır.</summary>
        public PageTab SettingsTab { get; }

        public MainViewModel()
        {
            Toasts = new ToastStack(tag => { if (tag != null) _ = RouteAsync(tag); });
            // Üst üste açılan kutularda yalnızca en üsttekinin kapat düğmesi görünür
            Modals.CollectionChanged += (_, _) => { foreach (var m in Modals) m.IsTop = m == Modals[^1]; };
            Login = new LoginViewModel(EnterAsync);
            Home = new HomeViewModel();
            Home.Main = this;
            // Güncellemesi olan paketler Oyna sekmesinde rozet olur
            Home.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(HomeViewModel.PackUpdateCount))
            {
                Tabs[0].Badge = Home.PackUpdateCount;
                Tabs[0].Tip = Home.PackUpdateCount > 0 ? $"Oyna · {Home.PackUpdateCount} paket güncellemesi" : "Oyna";
            }
            };
            Settings = new SettingsViewModel(this);
            Packs = new PacksViewModel(this);
            Servers = new ServersViewModel(this);
            Social = new SocialViewModel(this);
            Profile = new ProfileViewModel(this);
            Admin = new AdminViewModel(this);
            Notifications = new NotificationsViewModel(this);
            Radio = new RadioViewModel(this);
            Downloads = new DownloadsViewModel(this);
            Tabs.Add(new PageTab("home", "Oyna", "M5 4h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM14 4h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM5 13h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1v-5a1 1 0 0 1 1-1zM14 13h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1v-5a1 1 0 0 1 1-1z", Home));
            Tabs.Add(new PageTab("servers", "Sunucular", "M3 4h18v7H3zM3 13h18v7H3zM7 7.5h.01M7 16.5h.01", Servers));
            Tabs.Add(new PageTab("social", "Sosyal", "M9 4.8a3.2 3.2 0 1 0 0 6.4a3.2 3.2 0 0 0 0-6.4zM3.5 19a5.5 5.5 0 0 1 11 0M17 6.5a2.5 2.5 0 1 0 0 5a2.5 2.5 0 0 0 0-5zM15.5 14.2A4.5 4.5 0 0 1 20.5 19", Social));
            Tabs.Add(new PageTab("settings", "Ayarlar", "M12 9a3 3 0 1 0 0 6a3 3 0 0 0 0-6zM12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M5.3 18.7l2.1-2.1M16.6 7.4l2.1-2.1", Settings));
            Tabs.Add(new PageTab("admin", "Admin", "M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z", Admin) { IsVisible = false });
            // Profil başlık çubuğunda sekme olarak görünmez; kullanıcı adına ya da bir kişiye tıklanınca açılır
            Tabs.Add(new PageTab("profile", "Profil", "M12 12a4 4 0 1 0 0-8a4 4 0 0 0 0 8zM4 20a8 8 0 0 1 16 0", Profile) { IsVisible = false });
            SelectedTab = Tabs[0];
            SettingsTab = Tabs.First(t => t.Key == "settings");

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
                // VULU_UI_SPLASH=1: temanın açılışı oynar, 6 sn sonra açılır (açılış animasyonunu denemek için)
                if (Environment.GetEnvironmentVariable("VULU_UI_SPLASH") == "1")
                {
                    Splash.Begin(Username);
                    Avalonia.Threading.DispatcherTimer.RunOnce(() => _ = Splash.OpenAsync(), TimeSpan.FromSeconds(6));
                }
                // VULU_UI_PAGE=settings: o sekmeyle açılır
                if (Tabs.FirstOrDefault(t => t.Key == Environment.GetEnvironmentVariable("VULU_UI_PAGE")) is { } page) SelectedTab = page;
                else if (Environment.GetEnvironmentVariable("VULU_UI_PAGE") is { } world && world is "board" or "map") { SelectedTab = Tabs.First(t => t.Key == "social"); Social.ShowWorld(world); }
                _ = PerfProbe.RunAsync(this);
                // VULU_UI_LOADING=1: yükleme iskeletleri açık kalır (yükleniyor görünümünü denemek için)
                if (Environment.GetEnvironmentVariable("VULU_UI_LOADING") == "1")
                    Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                    {
                        Packs.Skeleton = true;
                        Social.ShotSkeleton = true;
                        Servers.FirstLoad = true;
                        Servers.Status = null;
                        Social.World.Loading = true;
                        Social.World.Error = null;
                    }, TimeSpan.FromSeconds(2.5));
                // VULU_UI_MODAL=1: örnek bildirim kartı ve soru kutusu (7 sn sonra kendiliğinden kapanır)
                if (Environment.GetEnvironmentVariable("VULU_UI_MODAL") == "1")
                    Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                    {
                        Notifications.Unread++;
                        ShowToast("Kopyalandı", "Sunucu adresi panoya kopyalandı.");
                        // Masaüstü bildirimleri (gerçek bildirim yolu: Core → WindowsHost.Notify)
                        _ = _core.Call("debug.notify", new { title = "Yeni mesaj", body = "Akşam sunucuya girecek misin? Yeni haritayı denemek istiyorum.", tag = "dm:deneme" });
                        _ = _core.Call("debug.notify", new { title = "Sunucu daveti", body = "deneme seni vulu Resmi'ye çağırıyor.", tag = "invite:deneme:vulu Resmi" });
                        _ = _core.Call("debug.notify", new { title = "Paket güncellendi", body = "vulu Resmi için yeni sürüm hazır.", tag = "pack:vulu-resmi" });
                        var dialog = new DialogViewModel("Paketi kaldır", "Bu paket ve içindeki dünyalar silinecek. Emin misin?", "Kaldır", "Vazgeç", true);
                        _ = ShowAsync(dialog);
                        Avalonia.Threading.DispatcherTimer.RunOnce(() => dialog.Close(), TimeSpan.FromSeconds(7));
                    }, TimeSpan.FromSeconds(2.5));
                // VULU_UI_INSTANCE=settings: Kütüphane'de ilk kişisel paketin sayfası o sekmeyle açılır
                if (Environment.GetEnvironmentVariable("VULU_UI_INSTANCE") is { Length: > 0 } instTab)
                    Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                    {
                        if (Home.Instances.FirstOrDefault(i => i.IsCustom) is not { } custom) return;
                        Home.OpenPageCommand.Execute(custom);
                        Home.SelectPageTabCommand.Execute(instTab);
                    }, TimeSpan.FromSeconds(2));
                // VULU_UI_EMPTY=1: kişisel paket yokmuş gibi (Kütüphane'nin karşılama alanını görmek için)
                if (Environment.GetEnvironmentVariable("VULU_UI_EMPTY") == "1")
                    Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                    {
                        foreach (var i in Home.Instances.Where(i => i.IsCustom).ToList()) Home.Instances.Remove(i);
                    }, TimeSpan.FromSeconds(2));
                // VULU_UI_DOWNLOAD=1: örnek indirme paneli (yerini görmek için)
                if (Environment.GetEnvironmentVariable("VULU_UI_DOWNLOAD") == "1")
                    Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                    {
                        Downloads.Title = "Fabric 1.21 hazırlanıyor";
                        Downloads.Phase = "Mod yükleyici hazırlanıyor";
                        Downloads.Running = true;
                        Downloads.Percent = 34;
                        Downloads.PercentText = "34%";
                        Downloads.Stats = "1.420 / 4.225 dosya";
                        Downloads.Visible = true;
                    }, TimeSpan.FromSeconds(2));
                // VULU_UI_GUIDE=1: ilk açılış rehberi açılır
                if (Environment.GetEnvironmentVariable("VULU_UI_GUIDE") == "1")
                    Avalonia.Threading.DispatcherTimer.RunOnce(() => _ = ShowTourAsync(), TimeSpan.FromSeconds(2));
                // VULU_UI_ADMIN=users: Admin panelinin o sekmesi açılır
                if (Environment.GetEnvironmentVariable("VULU_UI_ADMIN") is { Length: > 0 } adminTab)
                    Avalonia.Threading.DispatcherTimer.RunOnce(() => _ = Admin.SelectAsync(adminTab), TimeSpan.FromSeconds(2));
                // VULU_UI_DETAILS=1: Oyna'daki Ayrıntılar çekmecesi açık gelir
                if (Environment.GetEnvironmentVariable("VULU_UI_DETAILS") == "1")
                    Avalonia.Threading.DispatcherTimer.RunOnce(() => Home.DetailsOpen = true, TimeSpan.FromSeconds(2));
                // VULU_UI_OPEN=packs / curseforge / modrinth / blank: Yeni paket penceresi o kaynakla; content: İçerik ekle
                if (Environment.GetEnvironmentVariable("VULU_UI_OPEN") is { Length: > 0 } open)
                    Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                    {
                        if (open != "content") _ = OpenNewPackAsync(open);
                        else if (Home.Instances.FirstOrDefault(i => i.IsCustom) is { } custom) _ = OpenAddContentAsync(custom);
                        // Oturum yok: istekler başarısız olunca örnek veriler konur
                        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                        {
                            foreach (var m in Modals)
                            {
                                if (m is NewPackModal np) { np.Search.LoadPreview(); np.Blank.LoadPreview(); }
                                if (m is AddContentModal ac) ac.Search.LoadPreview();
                            }
                            // VULU_UI_PROJECT=1: ilk sonucun ayrıntı penceresi açılır
                            if (Environment.GetEnvironmentVariable("VULU_UI_PROJECT") == "1" && Modals.OfType<AddContentModal>().FirstOrDefault() is { } add && add.Search.Hits.Count > 0)
                                add.Search.Hits[0].DetailsCommand.Execute(null);
                        }, TimeSpan.FromSeconds(2));
                    }, TimeSpan.FromSeconds(2));
                // VULU_UI_THEMESPAM=1: temalar 150 ms arayla 15 sn boyunca değiştirilir (art arda değiştirme çökmesi denemesi)
                if (Environment.GetEnvironmentVariable("VULU_UI_THEMESPAM") == "1")
                    Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                    {
                        int n = 0;
                        var spam = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                        spam.Tick += (_, _) =>
                        {
                            if (++n > 100) { spam.Stop(); LogFile.Write("[tema] art arda değiştirme denemesi bitti"); return; }
                            _ = SwitchThemeAsync(new[] { Themes.Mesale, Themes.Ufuk, Themes.Derin, Themes.Grafit, Themes.Kutuphane }[n % 5]);
                        };
                        spam.Start();
                    }, TimeSpan.FromSeconds(3));
                // VULU_UI_INSTALL=1: kütüphanede kurulmakta olan örnek paket (önce bekler, sonra halka dolar)
                if (Environment.GetEnvironmentVariable("VULU_UI_INSTALL") == "1")
                    Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                    {
                        var pending = Home.BeginInstall("Örnek Modpack", null);
                        int tick = 0;
                        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                        timer.Tick += (_, _) =>
                        {
                            if (++tick < 5) return;
                            pending.Indeterminate = false;
                            pending.Progress = Math.Min(1, (tick - 5) / 40.0);
                            pending.Phase = $"Modpack indiriliyor · %{Math.Round(pending.Progress * 100)}";
                            if (pending.Progress >= 1) tick = 4;   // döngü: baştan
                        };
                        timer.Start();
                    }, TimeSpan.FromSeconds(1));
                // VULU_UI_SAMPLE=1: vulu Packs'e örnek paket kartları (kart görünümünü denemek için)
                if (Environment.GetEnvironmentVariable("VULU_UI_SAMPLE") == "1")
                    Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                    {
                        // Raydaki canlı işaretler: açık sunucu noktası, okunmamış rozeti, etiketler
                        var servers = Tabs.First(t => t.Key == "servers");
                        servers.Live = true;
                        servers.Tip = "Sunucular · 2 açık";
                        var social = Tabs.First(t => t.Key == "social");
                        social.Badge = 3;
                        social.Tip = "Sosyal · 4 arkadaş çevrimiçi, 1 oyunda";
                        Social.LoadLivePreview();
                        Home.InfoMessage = "Henüz dünya yok. Oyunu açıp bir dünya oluşturunca burada görünür.";
                        Packs.Skeleton = false;
                        Packs.Status = "4 paket";
                        Packs.StatusIsError = false;
                        Packs.Cards.Clear();
                        foreach (var (name, loader) in new[] { ("Örnek Paket", "fabric"), ("Macera", "forge"), ("Sade", "vanilla"), ("Teknik", "neoforge") })
                            Packs.Cards.Add(new PackCard(Packs, JsonDocument.Parse(
                                $"{{\"id\":\"{name}\",\"name\":\"{name}\",\"description\":\"Önizleme için örnek paket açıklaması.\",\"loader\":\"{loader}\",\"author\":\"vulu\"}}").RootElement));
                    }, TimeSpan.FromSeconds(4));
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
                    // Ana ekran perdenin arkasında kurulur, perde animasyonu bundan sonra başlar
                    Splash.Begin(saved, play: false);
                    SelectedTab = Tabs[0];
                    Warming = true;
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                    Splash.Play();
                    using var slow = new CancellationTokenSource();
                    _ = Task.Delay(SplashViewModel.OpenAtMs - 600, slow.Token).ContinueWith(t =>
                    {
                        if (!t.IsCanceled) Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!LoggedIn && Splash.IsVisible) Splash.Status = "Sunucuya bağlanılıyor…"; });
                    }, TaskScheduler.Default);
                    await Login.Prepare(init, auto: true);
                    slow.Cancel();
                    Warming = false;
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
#if DEBUG
            // Yalnızca geliştirmede: VULU_UI_PAGE=board / map / profile ile giriş sonrası o sayfa açılır (arayüz denemesi)
            if (Environment.GetEnvironmentVariable("VULU_UI_PAGE") is { Length: > 0 } devPage)
            {
                if (devPage == "profile") _ = OpenProfileAsync(Username);
                else if (devPage is "board" or "map") { SelectedTab = Tabs.First(t => t.Key == "social"); Social.ShowWorld(devPage); }
                else if (devPage.StartsWith("admin:", StringComparison.Ordinal) && IsAdmin)
                {
                    SelectedTab = Tabs.First(t => t.Key == "admin");
                    _ = Admin.SelectTabCommand.ExecuteAsync(Admin.Tabs.First(t => t.Key == devPage[6..]));
                }
                else if (Tabs.FirstOrDefault(t => t.Key == devPage) is { } devTab) SelectedTab = devTab;
            }
#endif
            // Perde açılana kadar görünmeyen işler (sosyal akış, bildirimler, profil resmi...) açılıştan sonra başlar:
            // sonuçları arayüzü yeniden düzenletir ve açılış animasyonunu takıltırdı
            void StartBackground()
            {
                _ = Social.StartAsync();
                _ = RefreshMyAvatarAsync();
                if (IsAdmin) _ = Admin.RefreshRegistrationsAsync();
                _ = Notifications.LoadAsync();
                _ = RefreshFeedbackCountAsync();
                _ = ShowWhatsNewAsync(auto: true);
            }
            if (!useSplash) StartBackground();
            await Home.LoadAsync();
            if (useSplash)
            {
                await Splash.OpenAsync();
                StartBackground();
            }
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
            finally
            {
                // Önce çıkış animasyonu (0,15 sn), sonra kaldır
                modal.Closing = true;
                await Task.Delay(150);
                Modals.Remove(modal);
            }
        }

        /// <summary>Soru kutusu: tamam denirse true.</summary>
        public async Task<bool> ConfirmAsync(string title, string message, string okText, string? cancelText = "Vazgeç", bool danger = false) =>
            await ShowAsync(new DialogViewModel(title, message, okText, cancelText, danger)) is true;

        /// <summary>Bu instance'a CurseForge ya da Modrinth'ten mod, kaynak paketi ya da shader kurma penceresi.</summary>
        public async Task OpenAddContentAsync(InstanceItem instance)
        {
            await ShowAsync(new AddContentModal(this, instance));
            Home.RefreshDetails();
        }

        /// <summary>Yeni paket penceresi: boş instance, vulu Packs, CurseForge ya da Modrinth modpack'i.</summary>
        public async Task OpenNewPackAsync(string? source = null)
        {
            if (!LoggedIn || Modals.Any(m => m is NewPackModal)) return;
            if (await ShowAsync(new NewPackModal(this, source)) is string id) await Home.ReloadInstancesAsync(id);
        }

        /// <summary>Oyna sekmesine geçer; verilen paket seçili olur (kurulumdan sonra).</summary>
        public async Task ShowHomeAsync(string? selectId)
        {
            // Kurulum Yeni paket penceresinden başladıysa pencere kapanır, paket Oyna'da seçili görünür
            foreach (var modal in Modals.OfType<NewPackModal>().ToList()) modal.Close();
            SelectedTab = Tabs[0];
            await Home.ReloadInstancesAsync(selectId);
        }

        /// <summary>
        /// Yeni paket kurulumu: pencere hemen kapanır, Oyna'ya geçilir ve paket kütüphanede ilerleme halkasıyla belirir;
        /// bitince gerçek pakete dönüşüp seçilir. Hata olursa kapak kalkar, hata çağırana döner.
        /// </summary>
        public async Task<JsonElement> InstallInLibraryAsync(string name, Avalonia.Media.Imaging.Bitmap? cover, Func<Task<JsonElement>> install)
        {
            foreach (var modal in Modals.OfType<NewPackModal>().ToList()) modal.Close();
            SelectedTab = Tabs[0];
            var pending = Home.BeginInstall(name, cover);
            try
            {
                var result = await install();
                pending.Indeterminate = false;
                pending.Progress = 1;
                await Home.ReloadInstancesAsync(result.Text("id"));   // gerçek kapak gelince bekleyen kalkar (boşluk olmaz)
                return result;
            }
            finally { Home.EndInstall(pending); }
        }

        public Task AlertAsync(string title, string message) => ConfirmAsync(title, message, "Tamam", null);

        /// <summary>
        /// Yenilikler: güncellemeden sonraki ilk açılışta o sürümün notları bir kez gösterilir (ilk kurulumda değil);
        /// Ayarlar › Launcher'dan her zaman yeniden açılır.
        /// </summary>
        public async Task ShowWhatsNewAsync(bool auto)
        {
            if (!LoggedIn) return;
            string? seen = UiState.Get("whatsNewSeen");
            if (auto)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));   // açılış bitsin, başka kutular önce açılsın
                if (seen == null) { UiState.Set("whatsNewSeen", Version); return; }   // ilk kurulum: gösterilmez
                if (seen == Version) return;
            }
            JsonElement r;
            try { r = await _core.Call("launcher.notes"); }
            catch (Exception ex) { if (!auto) await AlertAsync("Yenilikler alınamadı", ex.Message); return; }
            string version = r.Text("version") ?? Version;
            string? notes = r.Text("notes");
            if (string.IsNullOrWhiteSpace(notes))
            {
                if (!auto) await AlertAsync(Version, "Bu sürümün notları bulunamadı.");
                return;   // notlar henüz yoksa otomatik açılış bir sonraki sefer yeniden dener
            }
            if (auto && Modals.Count > 0) return;   // başka bir kutu açıksa üstüne binmesin; sonraki açılışta gösterilir
            UiState.Set("whatsNewSeen", Version);
            await AlertAsync($"v{version} ile gelenler", notes.Trim());
        }

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
        /// <summary>Başlık çubuğundaki tam ekran düğmesi (F11 de aynı işi yapar).</summary>
        [ObservableProperty] private bool _isFullscreen;
        [RelayCommand] private void ToggleFullscreen() => Views.MainWindow.Current?.SetFullscreen(!IsFullscreen);
        [RelayCommand] private void Close() => _core.Send("window.close");

        [RelayCommand]
        private void SelectTab(PageTab tab) => SelectedTab = tab;

        partial void OnSelectedTabChanged(PageTab? value)
        {
            foreach (var tab in Tabs) tab.IsActive = tab == value;
            IsHomePage = value?.Page is HomeViewModel;
            // Ayarlar her açılışta baştan okunur (başka yerden değişen ayarlar da görünsün)
            if (value?.Page is SettingsViewModel settings) _ = settings.LoadAsync();

            if (value?.Page is ServersViewModel servers) _ = servers.LoadAsync();
            if (value?.Page is not SocialViewModel) Social.World.Leave();   // harita yenilemesi Sosyal'den çıkınca durur
            if (value?.Page is SocialViewModel social) _ = social.OpenAsync();
            if (value?.Page is AdminViewModel admin) _ = admin.OpenAsync();
            // Başka sayfada mod kurulduysa ya da kaldırıldıysa Oyna'daki sayılar güncel olsun
            if (value?.Page is HomeViewModel && LoggedIn) Home.RefreshDetails();
        }

        /// <summary>Launcher içindeki küçük bilgi kartı (ör. "Kopyalandı"); asıl bildirimler masaüstünde çıkar.</summary>
        public void ShowToast(string title, string body, string? tag = null, string? avatar = null) => Toasts.Show(title, body, tag, avatar);

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
                    Home.OnInstallDownload(data);
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
