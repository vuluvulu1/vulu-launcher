using System.Security.Cryptography;
using Avalonia;
using VuluMobile.Services;

namespace VuluMobile.Desktop
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Oturumu hatırlamayı bilgisayarda da denemek için: Windows'un kullanıcıya bağlı şifrelemesi (DPAPI)
            if (OperatingSystem.IsWindows()) SavedLogin.Protector = new DpapiProtector();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private sealed class DpapiProtector : ISecretProtector
        {
            public byte[] Protect(byte[] data) => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
        }
    }
}
