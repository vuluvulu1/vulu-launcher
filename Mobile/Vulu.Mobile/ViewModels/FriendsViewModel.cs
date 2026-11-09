using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluMobile.Services;

namespace VuluMobile.ViewModels
{
    /// <summary>Bir arkadaş ya da istek satırı.</summary>
    internal sealed partial class FriendItem(string username) : ObservableObject
    {
        public string Username { get; } = username;
        /// <summary>"Oynuyor · vulu resmi", "Çevrimiçi" ya da "Çevrimdışı".</summary>
        [ObservableProperty] private string _status = "Çevrimdışı";
        [ObservableProperty] private bool _online;
        [ObservableProperty] private bool _playing;
        [ObservableProperty] private bool _busy;
        /// <summary>Arkadaşlıktan çıkarmak geri alınamaz: ilk dokunuş sorar.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RemoveText))]
        private bool _confirmRemove;
        public string RemoveText => ConfirmRemove ? "Emin misin?" : "Çıkar";
        /// <summary>Satıra dokununca altında düğmeler açılır.</summary>
        [ObservableProperty] private bool _expanded;
    }

    /// <summary>
    /// Arkadaşlar: kim ne oynuyor (GET /users/presence), gelen ve gönderilen istekler, kullanıcı adıyla istek
    /// gönderme. Arkadaşa dokununca mesaj ve çıkar düğmeleri açılır.
    /// </summary>
    internal sealed partial class FriendsViewModel(Session session, Action<string> openChat) : ObservableObject
    {
        public ObservableCollection<FriendItem> Friends { get; } = new();
        public ObservableCollection<FriendItem> Incoming { get; } = new();
        public ObservableCollection<FriendItem> Outgoing { get; } = new();

        [ObservableProperty] private string? _status;
        [ObservableProperty] private bool _loading;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AddCommand))]
        private string _addName = "";
        [ObservableProperty] private string? _addResult;

        public bool HasIncoming => Incoming.Count > 0;
        public bool HasOutgoing => Outgoing.Count > 0;
        public bool NoFriends => !Loading && Friends.Count == 0;
        public string FriendsHeader => Friends.Count == 0 ? "ARKADAŞLAR" : $"ARKADAŞLAR · {Friends.Count(f => f.Online)}/{Friends.Count} ÇEVRİMİÇİ";

        /// <summary>Gelen istek sayısı (sekme rozeti).</summary>
        public event Action<int>? IncomingChanged;

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (Loading) return;
            Loading = true;
            try
            {
                var friendsTask = session.Api.GetAsync<JsonElement>("/friends");
                var presenceTask = session.Api.GetAsync<JsonElement>("/users/presence");
                var lists = await friendsTask;
                JsonElement presence = default;
                try { presence = await presenceTask; } catch { }

                var online = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                if (presence.ValueKind == JsonValueKind.Array)
                    foreach (var p in presence.EnumerateArray())
                        if (p.GetProperty("username").GetString() is { } u) online[u] = p;

                Fill(Friends, lists.GetProperty("friends"), online);
                Fill(Incoming, lists.GetProperty("incoming"), null);
                Fill(Outgoing, lists.GetProperty("outgoing"), null);
                Status = null;
            }
            catch (Exception ex) { Status = ex.Message; }
            finally
            {
                Loading = false;
                Changed();
            }
        }

        private static void Fill(ObservableCollection<FriendItem> list, JsonElement names, Dictionary<string, JsonElement>? online)
        {
            var items = names.EnumerateArray().Select(n => new FriendItem(n.GetString() ?? "")).Where(f => f.Username.Length > 0).ToList();
            if (online != null)
            {
                foreach (var f in items)
                {
                    if (!online.TryGetValue(f.Username, out var p)) continue;
                    f.Online = true;
                    f.Playing = p.TryGetProperty("state", out var s) && s.GetString() == "playing";
                    string where = Str(p, "server") is { Length: > 0 } server ? server : Str(p, "instance");
                    string since = p.TryGetProperty("since", out var si) && si.TryDate(out var at) ? When.Since(at.ToLocalTime()) : "";
                    f.Status = f.Playing
                        ? "Oynuyor" + (where.Length > 0 ? " · " + where : "") + (since.Length > 0 ? " · " + since : "")
                        : "Çevrimiçi · launcher açık";
                }
                // Oynayanlar üstte, sonra çevrimiçiler, sonra adına göre
                items = items.OrderByDescending(f => f.Playing).ThenByDescending(f => f.Online)
                    .ThenBy(f => f.Username, StringComparer.OrdinalIgnoreCase).ToList();
            }
            list.Clear();
            foreach (var f in items) list.Add(f);
        }

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private void Changed()
        {
            OnPropertyChanged(nameof(HasIncoming));
            OnPropertyChanged(nameof(HasOutgoing));
            OnPropertyChanged(nameof(NoFriends));
            OnPropertyChanged(nameof(FriendsHeader));
            IncomingChanged?.Invoke(Incoming.Count);
        }

        private bool CanAdd => AddName.Trim().Length >= 3;

        [RelayCommand(CanExecute = nameof(CanAdd))]
        private async Task AddAsync()
        {
            try
            {
                var r = await session.Api.PostAsync<JsonElement>("/friends/requests", new { username = AddName.Trim() });
                string name = r.GetProperty("username").GetString() ?? AddName.Trim();
                AddResult = r.GetProperty("status").GetString() == "accepted" ? $"{name} ile artık arkadaşsınız." : $"{name} kişisine istek gönderildi.";
                AddName = "";
                await RefreshAsync();
            }
            catch (Exception ex) { AddResult = ex.Message; }
        }

        [RelayCommand]
        private void Toggle(FriendItem item)
        {
            foreach (var f in Friends) if (f != item) { f.Expanded = false; f.ConfirmRemove = false; }
            item.Expanded = !item.Expanded;
            item.ConfirmRemove = false;
        }

        [RelayCommand] private void Message(FriendItem item) => openChat(item.Username);

        [RelayCommand] private Task AcceptAsync(FriendItem item) => ActAsync(item, () => session.Api.PostAsync<JsonElement>($"/friends/requests/{Uri.EscapeDataString(item.Username)}/accept", new { }));
        [RelayCommand] private Task DeclineAsync(FriendItem item) => ActAsync(item, () => session.Api.DeleteAsync<JsonElement>($"/friends/requests/{Uri.EscapeDataString(item.Username)}"));
        [RelayCommand] private Task CancelAsync(FriendItem item) => ActAsync(item, () => session.Api.DeleteAsync<JsonElement>($"/friends/requests/{Uri.EscapeDataString(item.Username)}"));

        [RelayCommand]
        private async Task RemoveAsync(FriendItem item)
        {
            if (!item.ConfirmRemove) { item.ConfirmRemove = true; return; }
            await ActAsync(item, () => session.Api.DeleteAsync<JsonElement>($"/friends/{Uri.EscapeDataString(item.Username)}"));
        }

        private async Task ActAsync(FriendItem item, Func<Task<JsonElement>> call)
        {
            if (item.Busy) return;
            item.Busy = true;
            try { await call(); Status = null; }
            catch (Exception ex) { Status = ex.Message; }
            finally { item.Busy = false; }
            await RefreshAsync();
        }
    }
}
