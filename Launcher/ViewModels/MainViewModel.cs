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
        [ObservableProperty] private int _badge;
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
        public ObservableCollection<PageTab> Tabs { get; } = new();
        public ObservableCollection<ToastItem> Toasts { get; } = new();

        [ObservableProperty] private bool _loggedIn;
        [ObservableProperty] private string _username = "";
        [ObservableProperty] private string _userInitial = "";
        [ObservableProperty] private string _version = "";
        [ObservableProperty] private PageTab? _selectedTab;
        /// <summary>Açık soru/bilgi kutusu; yoksa null.</summary>
        [ObservableProperty] private DialogViewModel? _dialog;

        public MainViewModel()
        {
            Login = new LoginViewModel(EnterAsync);
            Home = new HomeViewModel();
            Settings = new SettingsViewModel(this);
            Tabs.Add(new PageTab("home", "Oyna", "M7 4l13 8-13 8z", Home));
            Tabs.Add(new PageTab("discover", "Keşfet", "M12 3a9 9 0 1 0 0 18a9 9 0 0 0 0-18zM15.5 8.5l-2 5-5 2 2-5z", new PlaceholderViewModel("Keşfet")));
            Tabs.Add(new PageTab("servers", "Sunucular", "M3 4h18v7H3zM3 13h18v7H3zM7 7.5h.01M7 16.5h.01", new PlaceholderViewModel("Sunucular")));
            Tabs.Add(new PageTab("social", "Sosyal", "M9 4.8a3.2 3.2 0 1 0 0 6.4a3.2 3.2 0 0 0 0-6.4zM3.5 19a5.5 5.5 0 0 1 11 0M17 6.5a2.5 2.5 0 1 0 0 5a2.5 2.5 0 0 0 0-5zM15.5 14.2A4.5 4.5 0 0 1 20.5 19", new PlaceholderViewModel("Sosyal")));
            Tabs.Add(new PageTab("settings", "Ayarlar", "M12 9a3 3 0 1 0 0 6a3 3 0 0 0 0-6zM12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M5.3 18.7l2.1-2.1M16.6 7.4l2.1-2.1", Settings));
            Tabs.Add(new PageTab("admin", "Admin", "M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z", new PlaceholderViewModel("Admin")) { IsVisible = false });
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
            Tabs.First(t => t.Key == "admin").IsVisible = session.Bool("isAdmin");
            SelectedTab = Tabs[0];
            LoggedIn = true;
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
            var init = await _core.Call("app.init");
            Login.Prepare(init, auto: false);
        }

        /// <summary>Soru kutusu: tamam denirse true. Aynı anda tek kutu açılır; yenisi eskisini vazgeçmiş sayar.</summary>
        public async Task<bool> ConfirmAsync(string title, string message, string okText, string? cancelText = "Vazgeç", bool danger = false)
        {
            Dialog?.CancelCommand.Execute(null);
            var dialog = new DialogViewModel(title, message, okText, cancelText, danger);
            Dialog = dialog;
            bool result = await dialog.Result;
            if (Dialog == dialog) Dialog = null;
            return result;
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
