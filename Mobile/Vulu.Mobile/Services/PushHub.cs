using Avalonia.Threading;

namespace VuluMobile.Services
{
    /// <summary>Platformun push bildirimi altyapısı (Android'de Firebase Cloud Messaging).</summary>
    public interface IPushPlatform
    {
        /// <summary>Bu cihazın FCM token'ı; push kullanılamıyorsa (Firebase ayarı yok, Google hizmetleri yok) null.</summary>
        Task<string?> GetTokenAsync();
        /// <summary>Android 13 ve üstünde bildirim izni ister (verilmişse bir şey yapmaz).</summary>
        void RequestPermission();
        /// <summary>Bildirimler bu telefonda gösterilebiliyor mu (izin verilmiş ve kullanıcı kapatmamış).</summary>
        bool NotificationsEnabled { get; }
        /// <summary>
        /// "İzin ver" düğmesi: izin henüz sorulabiliyorsa sorar, kullanıcı daha önce reddettiyse (Android ikinci
        /// kez sormaz) uygulamanın bildirim ayarlarını açar.
        /// </summary>
        void AskOrOpenSettings();
        /// <summary>Firebase token'ı alınamadıysa nedeni (ekranda gösterilir).</summary>
        string? LastError { get; }
    }

    /// <summary>
    /// Platform kabuğu (Vulu.Mobile.Android) ile arayüz arasındaki köprü: push altyapısı, yenilenen token ve
    /// dokunulan bildirim buradan geçer. Platform tarafı farklı iş parçacıklarından çağırabilir; arayüze UI
    /// iş parçacığında iletilir.
    /// </summary>
    public static class PushHub
    {
        public static IPushPlatform? Platform { get; set; }

        private static readonly object Gate = new();
        private static (string Kind, string Id)? _pendingOpen;

        internal static event Action<string>? TokenRefreshed;
        internal static event Action? OpenRequested;
        /// <summary>Uygulama öne geldi (ör. ayarlardan izin verilip dönüldü): bildirim durumu yeniden okunur.</summary>
        internal static event Action? Resumed;

        /// <summary>Uygulama ekranda mı: arka plandayken mesajlar daha seyrek sorulur.</summary>
        public static bool InForeground { get; private set; } = true;

        public static void OnResumed()
        {
            InForeground = true;
            Dispatcher.UIThread.Post(() => Resumed?.Invoke());
        }

        public static void OnPaused() => InForeground = false;

        /// <summary>FCM token'ı değişti (Firebase'in kendi yenilemesi); giriş yapılmışsa sunucuya yeniden kaydedilir.</summary>
        public static void OnNewToken(string token) => Dispatcher.UIThread.Post(() => TokenRefreshed?.Invoke(token));

        /// <summary>
        /// Bildirime dokunuldu: "registration" → Admin, "message" → o kişiyle sohbet, "friend.*" → Arkadaşlar,
        /// "shot" → o ekran görüntüsü. Giriş yapılmadıysa girişten sonra açılır.
        /// </summary>
        public static void OnNotificationOpened(string kind, string? id = null)
        {
            lock (Gate) _pendingOpen = (kind, id ?? "");
            Dispatcher.UIThread.Post(() => OpenRequested?.Invoke());
        }

        internal static (string Kind, string Id)? TakePendingOpen()
        {
            lock (Gate) { var open = _pendingOpen; _pendingOpen = null; return open; }
        }

        /// <summary>Şu an ekranda açık sohbetin kişisi: o kişiden gelen mesaj bildirim olarak gösterilmez.</summary>
        public static string? OpenChatPeer { get; internal set; }

        /// <summary>Yeni mesaj push'u geldi: yoklama beklemeden hemen sorulur.</summary>
        internal static event Action? MessagePushed;
        public static void OnMessagePushed() => Dispatcher.UIThread.Post(() => MessagePushed?.Invoke());
    }
}
