using Android.Content;
using Android.Content.PM;
using Android.Gms.Tasks;
using Firebase;
using Firebase.Messaging;
using VuluMobile.Services;

namespace VuluMobile.Android
{
    /// <summary>Firebase Cloud Messaging: cihaz token'ı ve bildirim izni.</summary>
    internal sealed class AndroidPush(Context context) : IPushPlatform
    {
        public async Task<string?> GetTokenAsync()
        {
            try
            {
                // google-services.json ile derlendiyse Firebase kendiliğinden başlar; değilse push yoktur
                if (FirebaseApp.GetApps(context).Count == 0 && FirebaseApp.InitializeApp(context) == null) return null;
                var result = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.Object>();
                return result?.ToString();
            }
            catch (Exception)
            {
                return null;   // Google Play Hizmetleri yok ya da Firebase'e ulaşılamadı
            }
        }

        public void RequestPermission()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;
            var activity = MainActivity.Current;
            if (activity == null || activity.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) == Permission.Granted) return;
            activity.RequestPermissions(new[] { global::Android.Manifest.Permission.PostNotifications }, 1);
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
