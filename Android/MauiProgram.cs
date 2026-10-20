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

            var builder = MauiApp.CreateBuilder();
            builder.UseMauiApp<App>();
            return builder.Build();
        }
    }
}
