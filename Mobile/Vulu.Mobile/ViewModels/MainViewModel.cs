using System.Collections.ObjectModel;
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

        public ObservableCollection<NavTab> Tabs { get; } = new();
        [ObservableProperty] private NavTab _selected;
        public string Username => _session.Username;

        public MainViewModel(Session session, Action logout)
        {
            _session = session;
            Tabs.Add(new NavTab("feed", "Akış", Icons.Feed, new PlaceholderViewModel("Akış", "Arkadaşlarının ekran görüntüleri burada olacak.")));
            Tabs.Add(new NavTab("messages", "Mesajlar", Icons.Messages, new PlaceholderViewModel("Mesajlar", "Uçtan uca şifreli mesajlar burada olacak.")));
            Tabs.Add(new NavTab("friends", "Arkadaşlar", Icons.Friends, new PlaceholderViewModel("Arkadaşlar", "Kim ne oynuyor, arkadaşlık istekleri burada olacak.")));
            Tabs.Add(new NavTab("profile", "Profil", Icons.Profile, new ProfileViewModel(session, logout)));
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
        }

        partial void OnSelectedChanged(NavTab value)
        {
            foreach (var t in Tabs) t.IsSelected = t == value;
            if (value.Page is AdminViewModel admin) _ = admin.RefreshAsync();
        }

        [RelayCommand] private void Select(NavTab tab) => Selected = tab;
    }

    internal static class Icons
    {
        public const string Feed = "M4 5h16v14H4z M4 15l4-4 4 4 3-3 5 5";
        public const string Messages = "M4 5h16v11H9l-5 4z";
        public const string Friends = "M9 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z M2 20c0-3.5 3-6 7-6s7 2.5 7 6 M16 4.5a3.5 3.5 0 0 1 0 6.5 M18 14c2.5.6 4 2.8 4 6";
        public const string Profile = "M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8z M4 21c0-4 3.6-7 8-7s8 3 8 7";
        public const string Admin = "M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z";
    }

    /// <summary>Henüz yapılmamış sayfa (Faz 1).</summary>
    internal sealed class PlaceholderViewModel(string title, string text) : ObservableObject
    {
        public string Title { get; } = title;
        public string Text { get; } = text;
    }

    internal sealed partial class ProfileViewModel(Session session, Action logout) : ObservableObject
    {
        public string Username { get; } = session.Username;
        public string Role { get; } = session.IsAdmin ? "Admin" : "Üye";
        [RelayCommand] private void Logout() => logout();
    }
}
