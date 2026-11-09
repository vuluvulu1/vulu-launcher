using Android.App;
using Firebase.Messaging;
using VuluMobile.Services;

namespace VuluMobile.Android
{
    /// <summary>
    /// Firebase'den gelen mesajlar. Uygulama arka plandayken bildirimli mesajları Firebase kendisi gösterir; buraya
    /// yalnızca uygulama öndeyken gelenler ve sessiz (yalnızca veri) mesajlar düşer.
    /// </summary>
    [Service(Exported = false)]
    [IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
    public sealed class MessagingService : FirebaseMessagingService
    {
        public override void OnNewToken(string token) => PushHub.OnNewToken(token);

        public override void OnMessageReceived(RemoteMessage message)
        {
            var data = message.Data;
            if (data == null || !data.TryGetValue("kind", out var kind)) return;
            data.TryGetValue("id", out var id);
            if (string.IsNullOrEmpty(id)) return;

            switch (kind)
            {
                case "registration":
                    var n = message.GetNotification();
                    data.TryGetValue("username", out var username);
                    Notifications.ShowRegistration(this, id, n?.Title ?? "Yeni kayıt isteği", n?.Body ?? $"{username} katılmak istiyor.");
                    break;
                case "registration.resolved":
                    Notifications.Cancel(this, id);
                    break;
            }
        }
    }
}
