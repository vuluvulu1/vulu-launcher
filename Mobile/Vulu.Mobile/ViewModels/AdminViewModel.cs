using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluMobile.Services;

namespace VuluMobile.ViewModels
{
    /// <summary>Bekleyen bir kayıt isteği (GET /admin/registrations).</summary>
    internal sealed partial class RegistrationItem : ObservableObject
    {
        public RegistrationItem(JsonElement r)
        {
            Id = r.GetProperty("id").GetString() ?? "";
            Username = r.GetProperty("username").GetString() ?? "";
            Email = r.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
            Verified = !r.TryGetProperty("emailVerified", out var v) || v.ValueKind != JsonValueKind.False;
            if (r.TryGetProperty("createdAt", out var c) && c.TryDate(out var at))
                When = at.ToLocalTime().ToString("d MMMM, HH:mm", CultureInfo.GetCultureInfo("tr-TR"));
        }

        public string Id { get; }
        public string Username { get; }
        public string Email { get; }
        public string When { get; } = "";
        /// <summary>E-postasını doğrulamayan istek henüz onaylanamaz (sunucu da reddeder).</summary>
        public bool Verified { get; }
        public string State => Verified ? "E-posta doğrulandı" : "E-posta doğrulanmadı";
        [ObservableProperty] private bool _busy;
        /// <summary>Red geri alınamaz: ilk dokunuş sorar, ikincisi reddeder.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RejectText))]
        private bool _confirmReject;
        public string RejectText => ConfirmReject ? "Emin misin?" : "Reddet";
    }

    /// <summary>Bir hesap (GET /admin/users).</summary>
    internal sealed partial class UserItem : ObservableObject
    {
        public UserItem(JsonElement u) => Update(u);

        public string Id { get; private set; } = "";
        public string Username { get; private set; } = "";
        public string Email { get; private set; } = "";
        [ObservableProperty] private bool _isAdmin;
        [ObservableProperty] private bool _isActive;
        [ObservableProperty] private string _seen = "";
        [ObservableProperty] private bool _expanded;
        [ObservableProperty] private bool _busy;
        [ObservableProperty] private string _newPassword = "";
        public string Badges => (IsAdmin ? "Admin" : "Üye") + (IsActive ? "" : " · kapalı");
        partial void OnIsAdminChanged(bool value) => OnPropertyChanged(nameof(Badges));
        partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(Badges));

        public void Update(JsonElement u)
        {
            Id = u.GetProperty("id").GetString() ?? "";
            Username = u.GetProperty("username").GetString() ?? "";
            Email = u.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
            IsAdmin = u.GetProperty("isAdmin").GetBoolean();
            IsActive = u.GetProperty("isActive").GetBoolean();
            Seen = u.TryGetProperty("lastSeenAt", out var s) && s.ValueKind == JsonValueKind.String && s.TryDate(out var at) ? "son görülme " + When.Short(at.ToLocalTime()) : "hiç görülmedi";
        }
    }

    /// <summary>
    /// Admin paneli: kayıt istekleri, kullanıcılar (aç/kapat, admin, şifre sıfırlama), ayarlar ve duyuru. Bütün
    /// işlemler giriş yapan adminin oturumuyla vulu-api üzerinden yapılır; yetkiyi sunucu denetler. Paket yükleme,
    /// radyo, depolama ve rol oluşturma masaüstünde kalır.
    /// </summary>
    internal sealed partial class AdminViewModel(Session session) : ObservableObject
    {
        /// <summary>0 kayıtlar, 1 kullanıcılar, 2 ayarlar.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(OnRegistrations), nameof(OnUsers), nameof(OnSettings))]
        private int _section;
        public bool OnRegistrations => Section == 0;
        public bool OnUsers => Section == 1;
        public bool OnSettings => Section == 2;

        [RelayCommand]
        private void ShowSection(string index)
        {
            Section = int.Parse(index, CultureInfo.InvariantCulture);
            if (Section == 1 && AllUsers.Count == 0) _ = LoadUsersAsync();
            if (Section == 2) _ = LoadSettingsAsync();
        }

        // ── Kayıt istekleri ───────────────────────────────────────────────
        public ObservableCollection<RegistrationItem> Registrations { get; } = new();
        [ObservableProperty] private string? _status;
        [ObservableProperty] private bool _loading;
        public bool IsEmpty => !Loading && Registrations.Count == 0 && Status == null;

        public event Action<int>? PendingChanged;

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (Section == 1) { await LoadUsersAsync(); return; }
            if (Section == 2) { await LoadSettingsAsync(); return; }
            if (Loading) return;
            Loading = true;
            try
            {
                var list = await session.Api.GetAsync<JsonElement>("/admin/registrations");
                Registrations.Clear();
                if (list.ValueKind == JsonValueKind.Array)
                    foreach (var r in list.EnumerateArray()) Registrations.Add(new RegistrationItem(r));
                Status = null;
            }
            catch (Exception ex) { Status = ex.Message; }
            finally
            {
                Loading = false;
                OnPropertyChanged(nameof(IsEmpty));
                PendingChanged?.Invoke(Registrations.Count);
            }
        }

        [RelayCommand] private Task ApproveAsync(RegistrationItem item) => DecideAsync(item, "approve", $"{item.Username} onaylandı, hesabı açıldı.");
        [RelayCommand]
        private async Task RejectAsync(RegistrationItem item)
        {
            if (!item.ConfirmReject) { item.ConfirmReject = true; return; }
            await DecideAsync(item, "reject", $"{item.Username} isteği reddedildi.");
        }

        private async Task DecideAsync(RegistrationItem item, string action, string done)
        {
            if (item.Busy) return;
            item.Busy = true;
            try
            {
                await session.Api.PostAsync<JsonElement>($"/admin/registrations/{Uri.EscapeDataString(item.Id)}/{action}", new { });
                Registrations.Remove(item);
                Status = done;
            }
            catch (Exception ex) { Status = ex.Message; }
            finally
            {
                item.Busy = false;
                OnPropertyChanged(nameof(IsEmpty));
                PendingChanged?.Invoke(Registrations.Count);
            }
        }

        // ── Kullanıcılar ──────────────────────────────────────────────────
        private readonly List<UserItem> AllUsers = new();
        public ObservableCollection<UserItem> Users { get; } = new();
        [ObservableProperty] private string _search = "";
        [ObservableProperty] private string? _usersStatus;
        public string UsersSummary => AllUsers.Count == 0 ? "" : $"{AllUsers.Count} hesap · {AllUsers.Count(u => u.IsAdmin)} admin · {AllUsers.Count(u => !u.IsActive)} kapalı";

        partial void OnSearchChanged(string value) => FilterUsers();

        private async Task LoadUsersAsync()
        {
            try
            {
                var list = await session.Api.GetAsync<JsonElement>("/admin/users");
                AllUsers.Clear();
                foreach (var u in list.EnumerateArray()) AllUsers.Add(new UserItem(u));
                FilterUsers();
                UsersStatus = null;
            }
            catch (Exception ex) { UsersStatus = ex.Message; }
            OnPropertyChanged(nameof(UsersSummary));
        }

        private void FilterUsers()
        {
            string q = Search.Trim();
            Users.Clear();
            foreach (var u in AllUsers.Where(u => q.Length == 0 || u.Username.Contains(q, StringComparison.OrdinalIgnoreCase)
                         || u.Email.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(100))
                Users.Add(u);
        }

        [RelayCommand]
        private void ToggleUser(UserItem user)
        {
            foreach (var u in Users) if (u != user) u.Expanded = false;
            user.Expanded = !user.Expanded;
        }

        [RelayCommand] private Task ToggleActiveAsync(UserItem user) => PatchAsync(user, new { isActive = !user.IsActive }, user.IsActive ? $"{user.Username} hesabı kapatıldı." : $"{user.Username} hesabı açıldı.");
        [RelayCommand] private Task ToggleAdminAsync(UserItem user) => PatchAsync(user, new { isAdmin = !user.IsAdmin }, user.IsAdmin ? $"{user.Username} artık admin değil." : $"{user.Username} artık admin.");

        [RelayCommand]
        private async Task ResetPasswordAsync(UserItem user)
        {
            if (user.NewPassword.Length < 6) { UsersStatus = "Yeni şifre en az 6 karakter olmalı."; return; }
            await PatchAsync(user, new { password = user.NewPassword }, $"{user.Username} şifresi değişti; açık oturumları kapandı.");
            user.NewPassword = "";
        }

        private async Task PatchAsync(UserItem user, object body, string done)
        {
            if (user.Busy) return;
            user.Busy = true;
            try
            {
                user.Update(await session.Api.PatchAsync<JsonElement>($"/admin/users/{Uri.EscapeDataString(user.Id)}", body));
                UsersStatus = done;
            }
            catch (Exception ex) { UsersStatus = ex.Message; }
            finally { user.Busy = false; OnPropertyChanged(nameof(UsersSummary)); }
        }

        // ── Ayarlar ve duyuru ─────────────────────────────────────────────
        private bool _loadingSettings;
        [ObservableProperty] private bool _approvalByAdmin = true;
        [ObservableProperty] private bool _requireOwnership;
        [ObservableProperty] private string? _settingsStatus;
        [ObservableProperty] private string? _announcement;
        private string? _announcementId;
        [ObservableProperty] private string _announcementDraft = "";
        [ObservableProperty] private string _announcementHours = "";

        private async Task LoadSettingsAsync()
        {
            try
            {
                _loadingSettings = true;
                var s = await session.Api.GetAsync<JsonElement>("/admin/settings");
                ApprovalByAdmin = !s.TryGetProperty("registrationApproval", out var a) || a.GetString() != "auto";
                RequireOwnership = s.TryGetProperty("requireMinecraftOwnership", out var o) && o.ValueKind == JsonValueKind.True;
                var an = await session.Api.GetAsync<JsonElement>("/announcements");
                if (an.TryGetProperty("announcement", out var x) && x.ValueKind == JsonValueKind.Object)
                {
                    _announcementId = x.GetProperty("id").GetString();
                    Announcement = x.GetProperty("text").GetString();
                }
                else { _announcementId = null; Announcement = null; }
                SettingsStatus = null;
            }
            catch (Exception ex) { SettingsStatus = ex.Message; }
            finally { _loadingSettings = false; }
        }

        partial void OnApprovalByAdminChanged(bool value) => SaveSetting(new { registrationApproval = value ? "admin" : "auto" });
        partial void OnRequireOwnershipChanged(bool value) => SaveSetting(new { requireMinecraftOwnership = value });

        private async void SaveSetting(object body)
        {
            if (_loadingSettings) return;
            try { await session.Api.PutAsync<JsonElement>("/admin/settings", body); SettingsStatus = "Kaydedildi."; }
            catch (Exception ex) { SettingsStatus = ex.Message; }
        }

        [RelayCommand]
        private async Task PublishAnnouncementAsync()
        {
            string text = AnnouncementDraft.Trim();
            if (text.Length == 0) return;
            int.TryParse(AnnouncementHours, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours);
            try
            {
                var r = await session.Api.PostAsync<JsonElement>("/announcements", new { text, hours = Math.Max(0, hours) });
                var a = r.GetProperty("announcement");
                _announcementId = a.GetProperty("id").GetString();
                Announcement = a.GetProperty("text").GetString();
                AnnouncementDraft = "";
                AnnouncementHours = "";
                SettingsStatus = "Duyuru yayınlandı; launcher'larda görünür.";
            }
            catch (Exception ex) { SettingsStatus = ex.Message; }
        }

        [RelayCommand]
        private async Task RemoveAnnouncementAsync()
        {
            if (_announcementId == null) return;
            try
            {
                await session.Api.DeleteAsync<JsonElement>($"/announcements/{Uri.EscapeDataString(_announcementId)}");
                _announcementId = null;
                Announcement = null;
                SettingsStatus = "Duyuru kaldırıldı.";
            }
            catch (Exception ex) { SettingsStatus = ex.Message; }
        }
    }
}
