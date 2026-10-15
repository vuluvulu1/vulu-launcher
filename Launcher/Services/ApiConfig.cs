namespace VuluLauncher
{
    /// <summary>
    /// Launcher'ın konuştuğu sunucu adresleri. Tek yerden değişsin diye burada.
    /// </summary>
    internal static class ApiConfig
    {
        public const string BaseUrl = "https://vulu-api.onrender.com";

        /// <summary>
        /// Modpack config dosyalarının indirileceği kök adres.
        /// Dosyalar "{ModpackFilesBaseUrl}/{instance}/{relativePath}" altında bulunmalı.
        /// </summary>
        public const string ModpackFilesBaseUrl =
            "https://raw.githubusercontent.com/vuluvulu1/vulu-launcher/main/modpack";
    }
}
