using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace VuluMobile.Android
{
    [Activity(Label = "vulu Sosyal", Theme = "@style/Theme.AppCompat.NoActionBar", Icon = "@drawable/icon", MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public sealed class MainActivity : AvaloniaMainActivity
    {
    }
}
