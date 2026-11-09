using Android.App;
using Android.Runtime;
using Avalonia.Android;

namespace VuluMobile.Android
{
    [Application]
    public sealed class AndroidApp : AvaloniaAndroidApplication<App>
    {
        public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }
    }
}
