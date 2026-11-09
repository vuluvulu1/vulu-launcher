using Android.App;
using Firebase.Messaging;
using VuluMobile.Services;

namespace VuluMobile.Android
{
    /// <summary>
    /// Firebase'den gelen mesajlar. Uygulama arka plandayken bildirimli mesajları Firebase kendisi gösterir; buraya
    /// uygulama öndeyken gelenler ve yalnızca veri taşıyan mesajlar düşer (kayıt istekleri yalnızca veridir, böylece
    /// bildirime Onayla/Reddet düğmeleri eklenebilir).
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
            data.TryGetValue("username", out var username);
            if (string.IsNullOrEmpty(id)) return;
            username ??= "";
            var n = message.GetNotification();
            string? title = n?.Title ?? (data.TryGetValue("title", out var t) ? t : null);
            string? body = n?.Body ?? (data.TryGetValue("body", out var b) ? b : null);

            switch (kind)
            {
                case "registration":
                    Notifications.ShowRegistration(this, id, username, title ?? "Yeni kayıt isteği", body ?? $"{username} katılmak istiyor.");
                    break;
                case "registration.resolved":
                    Notifications.Cancel(this, Notifications.RegistrationTag(id));
                    break;
                case "message":
                    PushHub.OnMessagePushed();
                    // O kişiyle sohbet ekranda açıksa bildirim gereksiz
                    if (PushHub.InForeground && string.Equals(PushHub.OpenChatPeer, id, StringComparison.OrdinalIgnoreCase)) break;
                    Notifications.Show(this, Notifications.MessagesChannel, "message-" + id, "message", id, title ?? id, body ?? "Yeni şifreli mesaj");
                    break;
                case "message.read":
                    Notifications.Cancel(this, "message-" + id);
                    break;
                case "friend.request":
                case "friend.accepted":
                    Notifications.Show(this, Notifications.SocialChannel, "friend-" + id, kind, id,
                        title ?? "Arkadaşlık isteği", body ?? $"{username}");
                    break;
                case "shot":
                    Notifications.Show(this, Notifications.SocialChannel, $"shot-{id}", "shot", id,
                        title ?? "Ekran görüntün", body ?? $"{username}");
                    break;
            }
        }
    }
}
