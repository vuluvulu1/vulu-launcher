using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Arkadaşlık istekleri. Arkadaşlar birbirinin ne oynadığını görür; istekler arayüz tarafından düzenli aralıklarla
    /// sorulur, yeni gelen istek launcher öndeyken rozet, arkadayken bildirim olur.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private HashSet<string>? _knownFriendRequests;   // null: henüz ilk liste alınmadı (açılışta bildirim yok)

        private void RegisterFriendCommands()
        {
            _bridge.On("friends.list", async _ => await ListFriendsAsync());
            _bridge.On("friends.request", async args =>
            {
                RequireLogin();
                return await _api.PostAsync<JsonElement>("/friends/requests", new { username = args.Str("username").Trim() });
            });
            _bridge.On("friends.accept", async args =>
            {
                RequireLogin();
                return await _api.PostAsync<JsonElement>($"/friends/requests/{Uri.EscapeDataString(args.Str("username"))}/accept", new { });
            });
            // Gelen isteği reddeder ya da gönderileni geri alır
            _bridge.On("friends.decline", async args =>
            {
                RequireLogin();
                return await _api.DeleteAsync<JsonElement>($"/friends/requests/{Uri.EscapeDataString(args.Str("username"))}");
            });
            _bridge.On("friends.remove", async args =>
            {
                RequireLogin();
                return await _api.DeleteAsync<JsonElement>($"/friends/{Uri.EscapeDataString(args.Str("username"))}");
            });
        }

        private async Task<object?> ListFriendsAsync()
        {
            RequireLogin();
            var list = await _api.GetAsync<FriendList>("/friends");
            var incoming = list.Incoming.ToHashSet(StringComparer.Ordinal);
            if (_knownFriendRequests != null)
                foreach (var name in incoming.Where(n => !_knownFriendRequests.Contains(n)))
                    ShowNotification("Arkadaşlık isteği", $"{name} seni arkadaş olarak eklemek istiyor.", "friend:" + name);
            _knownFriendRequests = incoming;
            return new { friends = list.Friends, incoming = list.Incoming, outgoing = list.Outgoing };
        }

        private sealed class FriendList
        {
            public List<string> Friends { get; set; } = new();
            public List<string> Incoming { get; set; } = new();
            public List<string> Outgoing { get; set; } = new();
        }
    }
}
