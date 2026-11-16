using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>
    /// Bildirim kartı (launcher içinde sağ altta ya da masaüstünde sağ altta; ikisi de Views/ToastCard).
    /// Tıklanınca etiketi ilgili sayfayı açar. Sunucu davetinde ("invite:kişi:sunucu") ayrıca "Katıl" düğmesi vardır;
    /// kartın geri kalanı o kişiyle konuşmayı açar.
    /// </summary>
    internal sealed partial class ToastItem : ObservableObject
    {
        /// <summary>Ekranda kalacağı süre (saniye); alttaki ince çizgi bununla kısalır.</summary>
        public const double Lifetime = 6;

        private readonly ToastStack _stack;
        private readonly string? _tag;
        private readonly string? _avatarUrl;

        public ToastItem(ToastStack stack, string title, string body, string? tag, string? avatar)
        {
            _stack = stack;
            _tag = tag;
            _avatarUrl = avatar;
            Title = title;
            Body = body;
            Person = PersonOf(tag);
        }

        public string Title { get; }
        public string Body { get; }
        public bool HasBody => Body.Length > 0;
        /// <summary>Bildirim bir kişiden geliyorsa adı (profil resmi yoksa baş harfi görünür).</summary>
        public string? Person { get; }
        public string Initial => string.IsNullOrEmpty(Person) ? "" : char.ToUpperInvariant(Person[0]).ToString();
        public bool IsInvite => _tag != null && _tag.StartsWith("invite:", StringComparison.Ordinal) && _tag.Split(':').Length == 3;
        /// <summary>Arkadaş oyuna girdi ("profile:kişi"): metin yeşil.</summary>
        public bool IsPlaying => _tag != null && _tag.StartsWith("profile:", StringComparison.Ordinal);

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowInitial))] private Avalonia.Media.Imaging.Bitmap? _avatar;
        public bool ShowInitial => Avatar == null && Initial.Length > 0;
        /// <summary>Kapanıyor: sağa kayıp solarak çıkar (0,26 sn).</summary>
        [ObservableProperty] private bool _leaving;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(TimerWidth))] private double _left = Lifetime;
        /// <summary>Üstüne gelindi: süre durur.</summary>
        [ObservableProperty] private bool _paused;
        public double TimerWidth => Math.Max(0, 338 * Left / Lifetime);

        public async Task LoadAsync() => Avatar = await Images.LoadAsync(_avatarUrl, 96);

        [RelayCommand]
        private void Open()
        {
            _stack.Dismiss(this);
            _stack.Open(IsInvite ? "dm:" + Person : _tag);
        }

        [RelayCommand]
        private void Join()
        {
            _stack.Dismiss(this);
            _stack.Open(_tag);
        }

        [RelayCommand] private void Dismiss() => _stack.Dismiss(this);

        /// <summary>Etiketten kişi: "dm:ali", "friend:ali", "profile:ali", "invite:ali:sunucu".</summary>
        private static string? PersonOf(string? tag)
        {
            if (tag == null) return null;
            var parts = tag.Split(':');
            return parts.Length >= 2 && parts[0] is "dm" or "friend" or "profile" or "invite" && parts[1].Length > 0 ? parts[1] : null;
        }
    }

    /// <summary>
    /// Üst üste duran bildirim kartları: en fazla üç tane, her biri 6 sn (üstüne gelinen kartın süresi durur).
    /// Fazlası ve süresi dolan kayarak çıkar. Kartın süresi tek saatle akar; kart kalmayınca saat durur.
    /// </summary>
    internal sealed class ToastStack(Action<string?> open)
    {
        private const int MaxCards = 3;
        private DispatcherTimer? _clock;

        public ObservableCollection<ToastItem> Items { get; } = new();

        /// <summary>Son kart da gitti.</summary>
        public event Action? Emptied;

        public void Open(string? tag) => open(tag);

        public ToastItem Show(string title, string body, string? tag = null, string? avatar = null)
        {
            var toast = new ToastItem(this, title, body, tag, avatar);
            _ = toast.LoadAsync();
            Items.Add(toast);
            foreach (var old in Items.Where(t => !t.Leaving).SkipLast(MaxCards).ToList()) Dismiss(old);
            if (_clock == null)
            {
                _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _clock.Tick += (_, _) =>
                {
                    foreach (var t in Items.ToList())
                        if (!t.Leaving && !t.Paused && (t.Left -= 0.1) <= 0) Dismiss(t);
                    if (Items.Count == 0) _clock.Stop();
                };
            }
            _clock.Start();
            return toast;
        }

        /// <summary>Kart sağa kayıp solarak çıkar (0,26 sn), sonra listeden kalkar.</summary>
        public void Dismiss(ToastItem toast)
        {
            if (toast.Leaving) return;
            toast.Leaving = true;
            DispatcherTimer.RunOnce(() =>
            {
                Items.Remove(toast);
                if (Items.Count == 0) Emptied?.Invoke();
            }, TimeSpan.FromMilliseconds(260));
        }
    }
}
