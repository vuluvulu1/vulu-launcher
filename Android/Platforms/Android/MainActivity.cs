using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace VuluLauncher
{
    // Arayüz masaüstündeki yatay yerleşimle tasarlandı; telefon ve tablette yatay açılır
    [Activity(
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ScreenOrientation = ScreenOrientation.SensorLandscape,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                               ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        internal const string NotificationTagExtra = "vulu.notificationTag";

        /// <summary>Uygulama şu an ekranda mı; değilse gelen mesajlar bildirim olur.</summary>
        internal static bool IsResumed { get; private set; }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

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
            if (intent?.GetStringExtra(NotificationTagExtra) is { } tag)
                MainPage.Current?.NotificationClicked(tag);
        }
    }
}
