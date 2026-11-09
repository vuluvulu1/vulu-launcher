using Android.App;
using Android.Content;

namespace VuluMobile.Android
{
    /// <summary>Bildirim kanalları ve uygulama öndeyken gelen bildirimin gösterilmesi.</summary>
    internal static class Notifications
    {
        public const string RegistrationsChannel = "registrations";
        public const string KindExtra = "kind";

        /// <summary>Kullanıcı Android ayarlarından kanalı ayrıca kapatabilir ya da sesini değiştirebilir.</summary>
        public static void CreateChannels(Context context)
        {
            var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
            manager?.CreateNotificationChannel(new NotificationChannel(RegistrationsChannel, "Kayıt istekleri", NotificationImportance.High)
            {
                Description = "Adminler için: yeni bir kayıt isteği onay bekliyor.",
            });
        }

        /// <summary>
        /// Uygulama öndeyken Firebase bildirimi kendisi göstermez; aynı tag ile burada gösterilir. Arka plandayken
        /// Firebase aynı tag'le gösterdiği için ikisi tek bildirim olarak kalır ve ikisi de aynı şekilde kaldırılır.
        /// </summary>
        public static void ShowRegistration(Context context, string id, string title, string body)
        {
            var intent = new Intent(context, typeof(MainActivity)).PutExtra(KindExtra, "registration")
                .AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            var open = PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            var notification = new Notification.Builder(context, RegistrationsChannel)
                .SetSmallIcon(Resource.Drawable.ic_notification)
                .SetContentTitle(title)
                .SetContentText(body)
                .SetAutoCancel(true)
                .SetContentIntent(open)
                .Build();
            ((NotificationManager?)context.GetSystemService(Context.NotificationService))?.Notify(Tag(id), 0, notification);
        }

        /// <summary>Başka bir admin karar verdi: bildirim kalkar.</summary>
        public static void Cancel(Context context, string id) =>
            ((NotificationManager?)context.GetSystemService(Context.NotificationService))?.Cancel(Tag(id), 0);

        private static string Tag(string id) => "registration-" + id;
    }
}
