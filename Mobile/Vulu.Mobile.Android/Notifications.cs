using Android.App;
using Android.Content;

namespace VuluMobile.Android
{
    /// <summary>Bildirim kanalları ve uygulamanın kendi gösterdiği bildirimler.</summary>
    internal static class Notifications
    {
        public const string RegistrationsChannel = "registrations";
        public const string MessagesChannel = "messages";
        public const string SocialChannel = "social";
        public const string UpdatesChannel = "updates";
        /// <summary>
        /// Bildirime dokununca açılacak ekran. Firebase'in arka planda gösterdiği bildirimlerde veri alanları
        /// (kind, id) aynı adlarla extra olarak gelir; uygulamanın gösterdiklerinde de aynı adlar kullanılır.
        /// </summary>
        public const string KindExtra = "kind";
        public const string IdExtra = "id";

        /// <summary>Kullanıcı Android ayarlarından her kanalı ayrıca kapatabilir ya da sesini değiştirebilir.</summary>
        public static void CreateChannels(Context context)
        {
            var manager = Manager(context);
            if (manager == null) return;
            manager.CreateNotificationChannel(new NotificationChannel(RegistrationsChannel, "Kayıt istekleri", NotificationImportance.High)
            {
                Description = "Adminler için: yeni bir kayıt isteği onay bekliyor.",
            });
            manager.CreateNotificationChannel(new NotificationChannel(MessagesChannel, "Mesajlar", NotificationImportance.High)
            {
                Description = "Yeni şifreli mesaj. İçerik bildirimde görünmez.",
            });
            manager.CreateNotificationChannel(new NotificationChannel(SocialChannel, "Arkadaşlar ve akış", NotificationImportance.Default)
            {
                Description = "Arkadaşlık istekleri, ekran görüntülerine beğeni ve yorumlar.",
            });
            manager.CreateNotificationChannel(new NotificationChannel(UpdatesChannel, "Güncellemeler", NotificationImportance.Low)
            {
                Description = "vulu Sosyal'ın yeni sürümü.",
            });
        }

        private static NotificationManager? Manager(Context context) =>
            (NotificationManager?)context.GetSystemService(Context.NotificationService);

        private static PendingIntent OpenIntent(Context context, string kind, string id, int request)
        {
            var intent = new Intent(context, typeof(MainActivity)).PutExtra(KindExtra, kind).PutExtra(IdExtra, id)
                .AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            return PendingIntent.GetActivity(context, request, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
        }

        /// <summary>
        /// Uygulama öndeyken Firebase bildirimi kendisi göstermez; aynı tag ile burada gösterilir. Arka plandayken
        /// Firebase aynı tag'le gösterdiği için ikisi tek bildirim olarak kalır ve ikisi de aynı şekilde kaldırılır.
        /// </summary>
        public static void Show(Context context, string channel, string tag, string kind, string id, string title, string body)
        {
            var notification = new Notification.Builder(context, channel)
                .SetSmallIcon(Resource.Drawable.ic_notification)
                .SetContentTitle(title)
                .SetContentText(body)
                .SetStyle(new Notification.BigTextStyle().BigText(body))
                .SetAutoCancel(true)
                .SetContentIntent(OpenIntent(context, kind, id, tag.GetHashCode()))
                .Build();
            Manager(context)?.Notify(tag, 0, notification);
        }

        /// <summary>
        /// Kayıt isteği: bildirimin üstünde Onayla ve Reddet. Reddet geri alınamadığı için önce "Emin misin?" sorar
        /// (bildirim "Evet, reddet" düğmesiyle yenilenir). İşlem <see cref="RegistrationActionReceiver"/>'da yapılır.
        /// </summary>
        public static void ShowRegistration(Context context, string id, string username, string title, string body, bool confirmReject = false, string? status = null)
        {
            var builder = new Notification.Builder(context, RegistrationsChannel)
                .SetSmallIcon(Resource.Drawable.ic_notification)
                .SetContentTitle(title)
                .SetContentText(status ?? body)
                .SetAutoCancel(true)
                .SetOnlyAlertOnce(true)
                .SetContentIntent(OpenIntent(context, "registration", id, ("reg" + id).GetHashCode()));
            if (status == null)
            {
                builder.AddAction(Action(context, id, username, RegistrationActionReceiver.Approve, "Onayla", title, body));
                builder.AddAction(confirmReject
                    ? Action(context, id, username, RegistrationActionReceiver.RejectConfirmed, "Evet, reddet", title, body)
                    : Action(context, id, username, RegistrationActionReceiver.Reject, "Reddet", title, body));
            }
            Manager(context)?.Notify(RegistrationTag(id), 0, builder.Build());
        }

        private static Notification.Action Action(Context context, string id, string username, string action, string label, string title, string body)
        {
            var intent = new Intent(context, typeof(RegistrationActionReceiver)).SetAction(action)
                .PutExtra(IdExtra, id).PutExtra("username", username).PutExtra("title", title).PutExtra("body", body);
            var pending = PendingIntent.GetBroadcast(context, (action + id).GetHashCode(), intent,
                PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
            return new Notification.Action.Builder((global::Android.Graphics.Drawables.Icon?)null, label, pending).Build();
        }

        public static string RegistrationTag(string id) => "registration-" + id;

        public static void Cancel(Context context, string tag) => Manager(context)?.Cancel(tag, 0);
    }
}
