namespace VuluLauncher
{
    /// <summary>
    /// Launcher'ın konuştuğu sunucu adresleri. Tek yerden değişsin diye burada.
    /// </summary>
    internal static class ApiConfig
    {
        public const string BaseUrl = "https://vulu-api.onrender.com";

        /// <summary>
        /// vulu Packs'i herkesin okuyabilmesi için MongoDB Atlas'ta oluşturulan, YALNIZCA
        /// launcher.packs, launcher.packfiles.files ve launcher.packfiles.chunks üzerinde "find"
        /// yetkisi olan kullanıcı. Exe'nin içinde durduğu için herkes görebilir; başka yetki vermeyin.
        /// Boş bırakılırsa paketleri yalnızca admin bağlantısı olan kullanıcılar görür.
        /// </summary>
        public const string PacksReaderUser = "";
        public const string PacksReaderPassword = "";
    }
}
