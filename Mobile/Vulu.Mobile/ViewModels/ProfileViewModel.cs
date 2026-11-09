using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluMobile.Services;

namespace VuluMobile.ViewModels
{
    /// <summary>
    /// Profil: fotoğraf, açıklama, oynama süresi ve roller; telefon bildirimleri ve tercihleri; uygulama sürümü ve
    /// güncelleme; çıkış.
    /// </summary>
    internal sealed partial class ProfileViewModel : ObservableObject
    {
        private readonly Session _session;
        private readonly Action _logout;
        private readonly AppUpdate _update = new();
        private bool _loadingPrefs;

        public ProfileViewModel(Session session, Action logout)
        {
            _session = session;
            _logout = logout;
            session.PushStateChanged += () => { OnPropertyChanged(nameof(PushState)); OnPropertyChanged(nameof(CanAskPermission)); };
            _ = LoadAsync();
        }

        public string Username => _session.Username;
        public bool IsAdmin => _session.IsAdmin;
        [ObservableProperty] private string _role = "";

        // ── Profil ────────────────────────────────────────────────────────
        [ObservableProperty] private string _bio = "";
        private string _savedBio = "";
        [ObservableProperty] private string? _playtime;
        public ObservableCollection<string> TopGames { get; } = new();
        [ObservableProperty] private string? _profileStatus;
        [ObservableProperty] private bool _avatarBusy;
        public bool BioChanged => Bio.Trim() != _savedBio;
        partial void OnBioChanged(string value) => OnPropertyChanged(nameof(BioChanged));

        private async Task LoadAsync()
        {
            try
            {
                var me = await _session.Api.GetAsync<JsonElement>("/users/me");
                _loadingPrefs = true;
                _savedBio = me.TryGetProperty("bio", out var b) ? b.GetString() ?? "" : "";
                Bio = _savedBio;
                var roles = me.TryGetProperty("roles", out var r) && r.ValueKind == JsonValueKind.Array
                    ? r.EnumerateArray().Select(x => x.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "").Where(x => x.Length > 0).ToList()
                    : new List<string>();
                if (_session.IsAdmin) roles.Insert(0, "Admin");
                Role = roles.Count > 0 ? string.Join(" · ", roles) : "Üye";
                TopGames.Clear();
                if (me.TryGetProperty("playtime", out var p) && p.ValueKind == JsonValueKind.Object)
                {
                    Playtime = Hours(p.GetProperty("totalSeconds").GetDouble());
                    foreach (var t in p.GetProperty("top").EnumerateArray().Take(3))
                        TopGames.Add($"{t.GetProperty("name").GetString()} · {Hours(t.GetProperty("seconds").GetDouble())}");
                }
                else Playtime = null;
                if (me.TryGetProperty("notifications", out var n2) && n2.ValueKind == JsonValueKind.Object)
                {
                    RegistrationNotifications = Flag(n2, "registrations");
                    MessageNotifications = Flag(n2, "messages");
                    FriendNotifications = Flag(n2, "friends");
                    ShotNotifications = Flag(n2, "shots");
                }
                ProfileStatus = null;
            }
            catch (Exception ex) { ProfileStatus = ex.Message; }
            finally { _loadingPrefs = false; OnPropertyChanged(nameof(BioChanged)); }
        }

        private static bool Flag(JsonElement e, string name) => !e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.False;
        private static string Hours(double seconds) => seconds < 3600 ? $"{(int)(seconds / 60)} dakika" : $"{seconds / 3600:0.#} saat";

        [RelayCommand]
        private async Task SaveBioAsync()
        {
            try
            {
                await _session.Api.PutAsync<JsonElement>("/users/me", new { bio = Bio.Trim() });
                _savedBio = Bio.Trim();
                OnPropertyChanged(nameof(BioChanged));
                ProfileStatus = "Açıklama kaydedildi.";
            }
            catch (Exception ex) { ProfileStatus = ex.Message; }
        }

        [RelayCommand]
        private async Task ChangeAvatarAsync()
        {
            try
            {
                byte[]? picked = await Picker.PickImageAsync("Profil fotoğrafı");
                if (picked == null) return;
                AvatarBusy = true;
                var jpeg = await Task.Run(() => ImageTools.Jpeg(picked, 256, 900_000, square: true));
                await _session.Api.PutBytesAsync<JsonElement>("/users/me/avatar", jpeg.Data, "image/jpeg");
                Images.ForgetAvatar(Username);
                ProfileStatus = "Profil fotoğrafı değişti.";
            }
            catch (Exception ex) { ProfileStatus = ex.Message; }
            finally { AvatarBusy = false; }
        }

        // ── Bildirimler ───────────────────────────────────────────────────
        private static bool Permitted => PushHub.Platform?.NotificationsEnabled == true;
        /// <summary>İki ayrı koşul: Android'in bildirim izni ve sunucuya kayıt. İkisi de gerekir.</summary>
        public string PushState => (Permitted, _session.PushReady) switch
        {
            (true, true) => "Bu telefon bildirim alıyor.",
            (false, true) => "Sunucuya kayıtlı ama bildirim izni kapalı: bildirimler gelir ama gösterilemez.",
            (true, false) => _session.PushError ?? "Sunucuya kaydediliyor…",
            (false, false) => "Bildirim izni kapalı. " + (_session.PushError ?? "Sunucuya kaydediliyor…"),
        };
        public bool CanAskPermission => !Permitted && PushHub.Platform != null;

        [RelayCommand] private void AskPermission() => PushHub.Platform?.AskOrOpenSettings();

        [ObservableProperty] private bool _registrationNotifications = true;
        [ObservableProperty] private bool _messageNotifications = true;
        [ObservableProperty] private bool _friendNotifications = true;
        [ObservableProperty] private bool _shotNotifications = true;
        [ObservableProperty] private string? _prefsError;

        partial void OnRegistrationNotificationsChanged(bool value) => SavePref("registrations", value);
        partial void OnMessageNotificationsChanged(bool value) => SavePref("messages", value);
        partial void OnFriendNotificationsChanged(bool value) => SavePref("friends", value);
        partial void OnShotNotificationsChanged(bool value) => SavePref("shots", value);

        private async void SavePref(string name, bool value)
        {
            if (_loadingPrefs) return;
            try
            {
                await _session.Api.PutAsync<JsonElement>("/users/me/notifications", new Dictionary<string, bool> { [name] = value });
                PrefsError = null;
            }
            catch (Exception ex) { PrefsError = ex.Message; }
        }

        // ── Sürüm ve güncelleme ───────────────────────────────────────────
        public string Version => AppPlatform.Current is { } p ? $"vulu Sosyal {p.VersionName}" : "vulu Sosyal (masaüstü deneme)";
        [ObservableProperty] private string? _updateText;
        [ObservableProperty] private bool _updateAvailable;
        [ObservableProperty] private bool _updateBusy;
        [ObservableProperty] private double _updateProgress;

        /// <summary>Yeni sürüm bulununca (sekme rozeti için).</summary>
        public event Action<bool>? UpdateChanged;

        [RelayCommand]
        public async Task CheckUpdateAsync()
        {
            if (AppPlatform.Current == null) { UpdateText = "Güncelleme yalnızca telefonda."; return; }
            UpdateText = "Denetleniyor…";
            var feed = await _update.CheckAsync();
            UpdateAvailable = feed != null;
            UpdateText = feed != null ? $"Yeni sürüm var: {feed.Version}" : "Güncelsin.";
            UpdateChanged?.Invoke(UpdateAvailable);
        }

        [RelayCommand]
        private async Task InstallUpdateAsync()
        {
            UpdateBusy = true;
            UpdateText = "İndiriliyor…";
            try
            {
                var error = await _update.InstallAsync(new Progress<double>(v => UpdateProgress = v * 100));
                UpdateText = error ?? "Android'in kurulum ekranında onayla.";
            }
            finally { UpdateBusy = false; }
        }

        [RelayCommand] private void Logout() => _logout();
    }
}
