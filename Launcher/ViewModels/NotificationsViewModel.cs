using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>Bildirim merkezindeki satır.</summary>
    internal sealed partial class NotifItem : ObservableObject
    {
        private readonly NotificationsViewModel _owner;

        public NotifItem(NotificationsViewModel owner, JsonElement n, long seenAt)
        {
            _owner = owner;
            Id = n.Get("id").ToString();
            Title = n.Str("title") ?? "";
            Body = n.Str("body") ?? "";
            Tag = n.Str("tag");
            At = DateTime.TryParse(n.Str("at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at.ToUniversalTime() : DateTime.UtcNow;
            _isNew = new DateTimeOffset(At).ToUnixTimeMilliseconds() > seenAt;
            // Bir kişiden geliyorsa (mesaj, istek, oyunda, davet) onun resmi; yoksa launcher logosu
            var person = Tag != null ? System.Text.RegularExpressions.Regex.Match(Tag, "^(dm|friend|profile|invite):([^:]+)") : null;
            Person = person is { Success: true } ? person.Groups[2].Value : null;
            Initial = (Person ?? "v")[..1].ToUpperInvariant();
            _ = LoadAvatar(n.Str("avatar"));
        }

        public string Id { get; }
        public string Title { get; }
        public string Body { get; }
        public bool HasBody => Body.Length > 0;
        public string? Tag { get; }
        public DateTime At { get; }
        public string? Person { get; }
        public string Initial { get; }
        public string When => NotificationsViewModel.RelativeTime(At);
        [ObservableProperty] private bool _isNew;
        [ObservableProperty] private Bitmap? _avatar;

        private async Task LoadAvatar(string? file)
        {
            Avatar = file != null ? await Images.LoadAsync(file, 64)
                : Person != null && _owner.Main.MembersCached.TryGetValue(Person, out long v) ? await Images.LoadAsync(Avatars.Url(Person, v), 64) : null;
        }

        [RelayCommand] private Task Open() => _owner.OpenAsync(this);
    }

    /// <summary>
    /// Bildirim merkezi (başlık çubuğundaki zil): son 50 bildirim Core'da tutulur (simge durumundayken gelenler dahil);
    /// okundu bilgisi bu bilgisayarda saklanır. Bildirimin etiketi ilgili sayfayı açar.
    /// </summary>
    internal sealed partial class NotificationsViewModel(MainViewModel main) : ObservableObject
    {
        private readonly CoreClient _core = CoreClient.Instance;
        private long _seenAt = long.TryParse(UiState.Get("notifSeenAt"), out long s) ? s : 0;

        public MainViewModel Main { get; } = main;
        public ObservableCollection<NotifItem> Items { get; } = new();
        [ObservableProperty] private int _unread;
        [ObservableProperty] private bool _isOpen;
        public bool HasUnread => Unread > 0;
        public string Badge => Unread > 9 ? "9+" : Unread.ToString(CultureInfo.InvariantCulture);
        public bool IsEmpty => Items.Count == 0;
        partial void OnUnreadChanged(int value) { OnPropertyChanged(nameof(HasUnread)); OnPropertyChanged(nameof(Badge)); }

        /// <summary>Kapanınca hepsi okundu sayılır.</summary>
        partial void OnIsOpenChanged(bool value) { if (!value) MarkSeen(); }

        public async Task LoadAsync()
        {
            Items.Clear();
            try
            {
                foreach (var n in (await _core.Call("notifications.list")).Items()) Items.Add(new NotifItem(this, n, _seenAt));
            }
            catch { /* liste boş kalır */ }
            Update();
        }

        public void OnNotification(JsonElement n)
        {
            string id = n.Get("id").ToString();
            if (Items.Any(x => x.Id == id)) return;
            Items.Insert(0, new NotifItem(this, n, _seenAt));
            while (Items.Count > 50) Items.RemoveAt(Items.Count - 1);
            Update();
        }

        private void Update()
        {
            Unread = Items.Count(i => i.IsNew);
            OnPropertyChanged(nameof(IsEmpty));
        }

        [RelayCommand]
        private void MarkSeen()
        {
            _seenAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            UiState.Set("notifSeenAt", _seenAt.ToString(CultureInfo.InvariantCulture));
            foreach (var i in Items) i.IsNew = false;
            Update();
        }

        public async Task OpenAsync(NotifItem item)
        {
            IsOpen = false;
            if (item.Tag is { } tag) await Main.RouteAsync(tag.StartsWith("invite:", StringComparison.Ordinal) && item.Person != null ? "dm:" + item.Person : tag);
        }

        /// <summary>"az önce", "5 dk önce", "3 sa önce", sonra gün.</summary>
        public static string RelativeTime(DateTime utc)
        {
            double minutes = (DateTime.UtcNow - utc).TotalMinutes;
            if (minutes < 1) return "az önce";
            if (minutes < 60) return $"{(int)minutes} dk önce";
            if (minutes < 24 * 60) return $"{(int)(minutes / 60)} sa önce";
            return Names.Ago(utc.ToString("o"));
        }
    }
}
