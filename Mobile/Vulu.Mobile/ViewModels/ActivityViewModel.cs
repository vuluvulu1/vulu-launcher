using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluMobile.Services;

namespace VuluMobile.ViewModels
{
    /// <summary>Bildirimler listesinin bir satırı; dokununca ilgili ekran açılır.</summary>
    internal sealed class ActivityItem(string kind, string id, string user, string text, DateTime at)
    {
        public string Kind { get; } = kind;
        public string Id { get; } = id;
        public string User { get; } = user;
        public string Text { get; } = text;
        public DateTime At { get; } = at;
        public string When => ViewModels.When.Short(At);
    }

    /// <summary>
    /// Bildirimler sekmesi: son bir haftada ekran görüntülerine gelen beğeni ve yorumlar, bekleyen arkadaşlık
    /// istekleri ve okunmamış mesajlar, yeniden eskiye. Telefona gelen push'ların uygulama içindeki karşılığı.
    /// </summary>
    internal sealed partial class ActivityViewModel(Session session, Action<string, string> open) : ObservableObject
    {
        public ObservableCollection<ActivityItem> Items { get; } = new();
        [ObservableProperty] private string? _status;
        [ObservableProperty] private bool _loading;
        public bool IsEmpty => !Loading && Items.Count == 0 && Status == null;

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (Loading) return;
            Loading = true;
            try
            {
                string since = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-7).ToString("o"));
                var eventsTask = session.Api.GetAsync<JsonElement>($"/shots/events?since={since}");
                var friendsTask = session.Api.GetAsync<JsonElement>("/friends");
                var convTask = session.Api.GetAsync<JsonElement>("/messages/conversations");
                var list = new List<ActivityItem>();

                try
                {
                    foreach (var e in (await eventsTask).GetProperty("events").EnumerateArray())
                    {
                        string from = e.GetProperty("from").GetString() ?? "";
                        bool comment = e.GetProperty("kind").GetString() == "comment";
                        string text = comment ? $"yorum yaptı: {e.GetProperty("text").GetString()}" : "ekran görüntünü beğendi.";
                        e.GetProperty("createdAt").TryDate(out var at);
                        list.Add(new ActivityItem("shot", e.GetProperty("shotId").GetString() ?? "", from, text, at.ToLocalTime()));
                    }
                }
                catch { /* bir kaynak gelmezse ötekiler yine gösterilir */ }
                try
                {
                    foreach (var u in (await friendsTask).GetProperty("incoming").EnumerateArray())
                        list.Add(new ActivityItem("friend.request", u.GetString() ?? "", u.GetString() ?? "", "seni arkadaş olarak eklemek istiyor.", DateTime.Now));
                }
                catch { }
                try
                {
                    foreach (var c in (await convTask).EnumerateArray())
                    {
                        int unread = c.GetProperty("unread").GetInt32();
                        if (unread == 0) continue;
                        string user = c.GetProperty("username").GetString() ?? "";
                        c.GetProperty("lastAt").TryDate(out var at);
                        list.Add(new ActivityItem("message", user, user, unread == 1 ? "sana mesaj gönderdi." : $"sana {unread} mesaj gönderdi.", at.ToLocalTime()));
                    }
                }
                catch { }

                Items.Clear();
                foreach (var item in list.OrderByDescending(i => i.At)) Items.Add(item);
                Status = null;
            }
            catch (Exception ex) { Status = ex.Message; }
            finally { Loading = false; OnPropertyChanged(nameof(IsEmpty)); }
        }

        [RelayCommand] private void Open(ActivityItem item) => open(item.Kind, item.Id);
    }
}
