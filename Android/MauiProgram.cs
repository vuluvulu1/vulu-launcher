using System.Reflection;

namespace VuluLauncher
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // Oyun ve uygulama verileri uygulamanın kendi klasörüne yazılır (APK'nın klasörü salt okunur)
            // Motor varsa instance'lar ve oyun dosyaları onun ana klasöründe durur: oyun süreci (:game) dosyaları orada arar
            if (AmethystRunner.Available)
                AppPaths.Initialize(
                    Path.Combine(FileSystem.AppDataDirectory, "vuluLauncher"),
                    AmethystRunner.GameHome,
                    gameCore: new AmethystRunner().GameRoot);
            else
                AppPaths.Initialize(
                    Path.Combine(FileSystem.AppDataDirectory, "vuluLauncher"),
                    Path.Combine(FileSystem.AppDataDirectory, "game"));
            // Android sürümü kendi numarasıyla ilerler (0.1.x); Vulu.Core'un sürümü güncelleme denetimini yanıltırdı
            LauncherUpdateService.LocalVersion = VuluShared.Releases.Clean(typeof(MauiProgram).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

            // Telefonda oyun cihazın belleğinin en fazla yarısını alır; masaüstündeki 4–16 GB seçenekleri burada anlamsız
            if (AmethystRunner.Available)
            {
                // 8 GB'lık telefon kendini ~7,6 GB bildirir; en yakın tam GB'a yuvarlanır
                long totalGb = (DeviceTotalMemoryBytes() + 512L * 1024 * 1024) / (1024L * 1024 * 1024);
                var options = new[] { 2, 3, 4, 6 }.Where(gb => gb * 2 <= Math.Max(totalGb, 4)).ToArray();
                PackCatalog.UseRamOptions(options.Length > 0 ? options : new[] { 2 });
            }

            var builder = MauiApp.CreateBuilder();
            builder.UseMauiApp<App>();
            return builder.Build();
        }

        private static long DeviceTotalMemoryBytes()
        {
            var manager = (Android.App.ActivityManager?)Android.App.Application.Context.GetSystemService(Android.Content.Context.ActivityService);
            var info = new Android.App.ActivityManager.MemoryInfo();
            manager?.GetMemoryInfo(info);
            return info.TotalMem;
        }
    }
}
