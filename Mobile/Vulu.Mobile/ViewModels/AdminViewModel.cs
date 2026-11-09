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
            if (r.TryGetProperty("createdAt", out var c) && c.TryGetDateTime(out var at))
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

    /// <summary>
    /// Admin paneli. Faz 0'da kayıt istekleri: listele, onayla, reddet. Kullanıcılar, ayarlar ve duyuru Faz 1'de.
    /// Bütün işlemler giriş yapan adminin oturumuyla vulu-api üzerinden yapılır; yetkiyi sunucu denetler.
    /// </summary>
    internal sealed partial class AdminViewModel(Session session) : ObservableObject
    {
        public ObservableCollection<RegistrationItem> Registrations { get; } = new();
        [ObservableProperty] private string? _status;
        [ObservableProperty] private bool _loading;
        public bool IsEmpty => !Loading && Registrations.Count == 0 && Status == null;

        public event Action<int>? PendingChanged;

        [RelayCommand]
        public async Task RefreshAsync()
        {
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
    }
}
