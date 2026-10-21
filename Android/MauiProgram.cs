using System.Reflection;

namespace VuluLauncher
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // Oyun ve uygulama verileri uygulamanın kendi klasörüne yazılır (APK'nın klasörü salt okunur)
            AppPaths.Initialize(
                Path.Combine(FileSystem.AppDataDirectory, "vuluLauncher"),
                Path.Combine(FileSystem.AppDataDirectory, "game"));
            // Android sürümü kendi numarasıyla ilerler (0.1.x); Vulu.Core'un sürümü güncelleme denetimini yanıltırdı
            LauncherUpdateService.LocalVersion = VuluShared.Releases.Clean(typeof(MauiProgram).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

            var builder = MauiApp.CreateBuilder();
            builder.UseMauiApp<App>();
            return builder.Build();
        }
    }
}
