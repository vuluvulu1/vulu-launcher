using Android.App;
using Android.Runtime;

namespace VuluLauncher
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership) => CrashLog.Attach();

        public override void OnCreate()
        {
            // Oyun motoru (Amethyst) her süreçte kendi klasörlerini ve ayarlarını hazırlar
            AmethystRunner.Initialize(this);

            // Oyun ayrı bir süreçte (:game) çalışır; orada launcher arayüzü (MAUI) kurulmaz ki belleği oyuna kalsın
            if (IsGameProcess()) return;
            base.OnCreate();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        private static bool IsGameProcess()
        {
            string name = OperatingSystem.IsAndroidVersionAtLeast(28)
                ? Android.App.Application.ProcessName ?? ""
                : File.ReadAllText("/proc/self/cmdline").TrimEnd('\0');
            return name.EndsWith(":game", StringComparison.Ordinal);
        }
    }
}
