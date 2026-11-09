using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluMobile.Services;

namespace VuluMobile.ViewModels
{
    /// <summary>Alt sekme: simge yolu (24×24 SVG path), başlık ve sayfa modeli.</summary>
    internal sealed partial class NavTab(string key, string title, string icon, ObservableObject page) : ObservableObject
    {
        public string Key { get; } = key;
        public string Title { get; } = title;
        public string Icon { get; } = icon;
        public ObservableObject Page { get; } = page;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBadge))]
        private int _badge;
        public bool HasBadge => Badge > 0;
    }

    /// <summary>Giriş sonrası ana ekran: Akış · Mesajlar · Arkadaşlar · Bildirimler · Profil, adminlerde ayrıca Admin.</summary>
    internal sealed partial class MainViewModel : ObservableObject
    {
        private readonly Session _session;
        private readonly FeedViewModel _feed;
        private readonly MessagesViewModel _messages;

        public ObservableCollection<NavTab> Tabs { get; } = new();
        [ObservableProperty] private NavTab _selected;
        public string Username => _session.Username;

        public MainViewModel(Session session, Action logout)
        {
            _session = session;
            var signOut = logout;
            logout = () => { PushHub.OpenRequested -= OpenPending; signOut(); };
            var feed = new FeedViewModel(session);
            _feed = feed;
            var messages = new MessagesViewModel(session);
            var messagesTab = new NavTab("messages", "Mesajlar", Icons.Messages, messages);
            var friends = new FriendsViewModel(session, user => { Selected = messagesTab; messages.OpenWith(user); });
            var friendsTab = new NavTab("friends", "Arkadaşlar", Icons.Friends, friends);
            _messages = messages;
            var activity = new ActivityViewModel(session, (kind, id) => { PushHub.OnNotificationOpened(kind, id); });
            Tabs.Add(new NavTab("feed", "Akış", Icons.Feed, feed));
            Tabs.Add(messagesTab);
            Tabs.Add(friendsTab);
            Tabs.Add(new NavTab("activity", "Bildirimler", Icons.Bell, activity));
            // Rozetler: okunmamış mesaj ve gelen arkadaşlık isteği
            Action unread = () => messagesTab.Badge = session.Messages.Unread;
            session.Messages.StateChanged += unread;
            friends.IncomingChanged += count => friendsTab.Badge = count;
            var signOut2 = logout;
            logout = () => { session.Messages.StateChanged -= unread; signOut2(); };
            _ = feed.EnsureLoadedAsync();
            _ = messages.RefreshAsync();
            _ = friends.RefreshAsync();
            var profile = new ProfileViewModel(session, logout);
            var profileTab = new NavTab("profile", "Profil", Icons.Profile, profile);
            Tabs.Add(profileTab);
            // Yeni sürüm varsa Profil sekmesinde rozet
            profile.UpdateChanged += available => profileTab.Badge = available ? 1 : 0;
            if (AppPlatform.Current != null) _ = profile.CheckUpdateAsync();
            if (session.IsAdmin)
            {
                var admin = new AdminViewModel(session);
                var tab = new NavTab("admin", "Admin", Icons.Admin, admin);
                admin.PendingChanged += count => tab.Badge = count;
                Tabs.Add(tab);
                _ = admin.RefreshAsync();
            }
            _selected = Tabs[0];
            _selected.IsSelected = true;

            // Bildirime dokunularak açıldıysa (ya da açıkken dokunulursa) ilgili sekme
            PushHub.OpenRequested += OpenPending;
            OpenPending();
        }

        private void OpenPending()
        {
            if (PushHub.TakePendingOpen() is not { } open) return;
            NavTab? Tab(string key) => Tabs.FirstOrDefault(t => t.Key == key);
            switch (open.Kind)
            {
                case "registration" when Tab("admin") is { } admin:
                    Selected = admin;
                    break;
                case "message" when open.Id.Length > 0:
                    Selected = Tab("messages")!;
                    _messages.OpenWith(open.Id);
                    break;
                case "friend.request" or "friend.accepted":
                    Selected = Tab("friends")!;
                    break;
                case "shot" when open.Id.Length > 0:
                    Selected = Tab("feed")!;
                    _ = _feed.OpenByIdAsync(open.Id);
                    break;
            }
        }

        partial void OnSelectedChanged(NavTab value)
        {
            foreach (var t in Tabs) t.IsSelected = t == value;
            switch (value.Page)
            {
                case AdminViewModel admin: _ = admin.RefreshAsync(); break;
                case FriendsViewModel friends: _ = friends.RefreshAsync(); break;
                case MessagesViewModel messages when messages.Chat == null: _ = messages.RefreshAsync(); break;
                case FeedViewModel feed: _ = feed.EnsureLoadedAsync(); break;
                case ActivityViewModel activity: _ = activity.RefreshAsync(); break;
            }
        }

        [RelayCommand] private void Select(NavTab tab) => Selected = tab;

        /// <summary>
        /// Android geri tuşu: açık sohbet/resim kapanır, sonra ilk sekmeye dönülür; ilk sekmedeyse uygulama
        /// arka plana gider (false).
        /// </summary>
        public bool GoBack()
        {
            if (Selected.Page is IBackHandler page && page.GoBack()) return true;
            if (Selected != Tabs[0]) { Selected = Tabs[0]; return true; }
            return false;
        }
    }

    /// <summary>Kendi içinde alt sayfası olan sekme (sohbet, tam boy resim): geri tuşu önce onu kapatır.</summary>
    internal interface IBackHandler
    {
        bool GoBack();
    }

    internal static class Icons
    {
        public const string Feed = "M4 5h16v14H4z M4 15l4-4 4 4 3-3 5 5";
        public const string Messages = "M4 5h16v11H9l-5 4z";
        public const string Friends = "M9 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z M2 20c0-3.5 3-6 7-6s7 2.5 7 6 M16 4.5a3.5 3.5 0 0 1 0 6.5 M18 14c2.5.6 4 2.8 4 6";
        public const string Profile = "M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8z M4 21c0-4 3.6-7 8-7s8 3 8 7";
        public const string Bell = "M6 16V11a6 6 0 0 1 12 0v5l2 2H4z M10 20a2 2 0 0 0 4 0";
        public const string Admin = "M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z";
    }

    /// <summary>Henüz yapılmamış sayfa (Faz 1).</summary>
    internal sealed class PlaceholderViewModel(string title, string text) : ObservableObject
    {
        public string Title { get; } = title;
        public string Text { get; } = text;
    }
}
