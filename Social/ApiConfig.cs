namespace VuluLauncher
{
    /// <summary>
    /// Launcher'ın konuştuğu sunucu adresleri. Tek yerden değişsin diye burada.
    /// </summary>
    internal static class ApiConfig
    {
        public static readonly string BaseUrl =
#if DEBUG
            // Yalnızca geliştirme derlemesinde: testler yerel bir vulu-api'ye yönlendirilebilir. Yayın derlemesinde adres sabittir.
            Environment.GetEnvironmentVariable("VULU_API_URL") ??
#endif
            "https://vuluvulu.com.tr/api";
    }
}
