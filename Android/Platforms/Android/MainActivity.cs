using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace VuluLauncher
{
    // Telefon yerleşimi (css/mobile.css) dikey tasarlandı
    [Activity(
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ScreenOrientation = ScreenOrientation.UserPortrait,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                               ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        internal const string NotificationTagExtra = "vulu.notificationTag";

        /// <summary>Uygulama şu an ekranda mı; değilse gelen mesajlar bildirim olur.</summary>
        internal static bool IsResumed { get; private set; }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            try { base.OnCreate(savedInstanceState); }
            catch (Exception ex)
            {
                // Arayüz kurulamadı: uygulama sessizce kapanmasın, hata gösterilsin
                CrashLog.Write(ex);
                CrashLog.ShowPrevious(this);
                return;
            }
            CrashLog.ShowPrevious(this);
            // Uygulama kapalıyken bildirime dokunulduysa açılış intent'i bildirimi taşır
            DeliverNotification(Intent);

            // Android 13+: bildirimler için izin istenir (reddedilirse launcher bildirimsiz çalışır)
            if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
                CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
                RequestPermissions(new[] { Android.Manifest.Permission.PostNotifications }, 1);
        }

        protected override void OnResume()
        {
            base.OnResume();
            IsResumed = true;
        }

        protected override void OnPause()
        {
            IsResumed = false;
            base.OnPause();
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            DeliverNotification(intent);
        }

        private static string? _pendingNotification;

        /// <summary>Sayfa henüz kurulmadıysa bildirim bekletilir; MainPage kurulunca alır.</summary>
        internal static string? TakePendingNotification()
        {
            string? tag = _pendingNotification;
            _pendingNotification = null;
            return tag;
        }

        private static void DeliverNotification(Intent? intent)
        {
            if (intent?.GetStringExtra(NotificationTagExtra) is not { } tag) return;
            intent.RemoveExtra(NotificationTagExtra);   // ekran dönünce ya da yeniden oluşunca tekrar açılmasın
            if (MainPage.Current is { } page) page.NotificationClicked(tag);
            else _pendingNotification = tag;
        }
    }
}
