using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Tasks;
using Android.Provider;
using Firebase;
using Firebase.Messaging;
using VuluMobile.Services;

namespace VuluMobile.Android
{
    /// <summary>Firebase Cloud Messaging: cihaz token'ı ve bildirim izni.</summary>
    internal sealed class AndroidPush(Context context) : IPushPlatform
    {
        private const string PostNotifications = global::Android.Manifest.Permission.PostNotifications;

        public string? LastError { get; private set; }

        public bool NotificationsEnabled =>
            ((NotificationManager?)context.GetSystemService(Context.NotificationService))?.AreNotificationsEnabled() == true;

        public async Task<string?> GetTokenAsync()
        {
            try
            {
                // google-services.json ile derlendiyse Firebase kendiliğinden başlar; değilse push yoktur
                if (FirebaseApp.GetApps(context).Count == 0 && FirebaseApp.InitializeApp(context) == null)
                {
                    LastError = "Firebase ayarı bu sürümde yok.";
                    return null;
                }
#pragma warning disable CS0618 // GetToken() binding'de eski diye işaretli ama Firebase'in önerdiği yol bu
                var result = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.Object>();
#pragma warning restore CS0618
                LastError = result == null ? "Firebase boş token döndü." : null;
                return result?.ToString();
            }
            catch (Exception ex)
            {
                // Çoğunlukla: Google Play Hizmetleri yok/eski ya da Firebase'e ulaşılamadı
                LastError = ex.Message;
                return null;
            }
        }

        public void RequestPermission()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;
            var activity = MainActivity.Current;
            if (activity == null || activity.CheckSelfPermission(PostNotifications) == Permission.Granted) return;
            activity.RequestPermissions(new[] { PostNotifications }, 1);
        }

        public void AskOrOpenSettings()
        {
            var activity = MainActivity.Current;
            // Android 13+: hiç sorulmadıysa ya da bir kez reddedildiyse sorulabilir. İkinci redden sonra Android
            // pencereyi göstermez; o zaman uygulamanın bildirim ayarları açılır.
            if (OperatingSystem.IsAndroidVersionAtLeast(33) && activity != null
                && activity.CheckSelfPermission(PostNotifications) != Permission.Granted
                && !MainActivity.PermissionAsked)
            {
                RequestPermission();
                return;
            }
            var intent = new Intent(Settings.ActionAppNotificationSettings)
                .PutExtra(Settings.ExtraAppPackage, context.PackageName)
                .AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
    }

    /// <summary>Google'ın Task'ını await edilebilir yapar.</summary>
    internal static class GmsTaskExtensions
    {
        public static Task<T?> AsAsync<T>(this global::Android.Gms.Tasks.Task task) where T : Java.Lang.Object
        {
            var source = new TaskCompletionSource<T?>();
            task.AddOnCompleteListener(new CompleteListener(t =>
            {
                if (t.IsSuccessful) source.TrySetResult(t.Result as T);
                else source.TrySetException(t.Exception ?? new Exception("Firebase görevi başarısız."));
            }));
            return source.Task;
        }

        private sealed class CompleteListener(Action<global::Android.Gms.Tasks.Task> done) : Java.Lang.Object, IOnCompleteListener
        {
            public void OnComplete(global::Android.Gms.Tasks.Task task) => done(task);
        }
    }
}
