using Android.App;
using Android.Runtime;

namespace VuluLauncher
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership) => CrashLog.Attach();

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
