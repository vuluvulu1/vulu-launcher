using Android.App;
using Android.Runtime;
using Avalonia.Android;
using VuluMobile.Services;

namespace VuluMobile.Android
{
    [Application]
    public sealed class AndroidApp : AvaloniaAndroidApplication<App>
    {
        public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();
            Notifications.CreateChannels(this);
            PushHub.Platform = new AndroidPush(this);
            SavedLogin.Protector = new AndroidProtector();
            AppPlatform.Current = new AndroidPlatform(this);
            BackgroundCheck.Schedule(this);
        }
    }
}
