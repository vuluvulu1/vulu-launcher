using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using VuluMobile.Services;

namespace VuluMobile.Android
{
    [Activity(Label = "vulu Sosyal", Theme = "@style/Theme.AppCompat.NoActionBar", Icon = "@drawable/icon", MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public sealed class MainActivity : AvaloniaMainActivity
    {
        /// <summary>Bildirim izni sormak için açık olan ekran.</summary>
        internal static MainActivity? Current { get; private set; }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Current = this;
            HandleNotification(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            HandleNotification(intent);
        }

        protected override void OnDestroy()
        {
            if (Current == this) Current = null;
            base.OnDestroy();
        }

        /// <summary>Bildirime dokunularak açıldıysa: Firebase'in gösterdiği bildirimde veri alanları da extra olarak gelir.</summary>
        private static void HandleNotification(Intent? intent)
        {
            string? kind = intent?.GetStringExtra(Notifications.KindExtra);
            if (string.IsNullOrEmpty(kind)) return;
            intent!.RemoveExtra(Notifications.KindExtra);
            PushHub.OnNotificationOpened(kind);
        }
    }
}
