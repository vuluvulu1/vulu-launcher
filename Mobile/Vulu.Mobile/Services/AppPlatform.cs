namespace VuluMobile.Services
{
    /// <summary>Telefona özgü işler: sürüm bilgisi, bağlantı açma, bildirim kaldırma. Masaüstü test penceresinde yoktur.</summary>
    public interface IAppPlatform
    {
        /// <summary>Kurulu sürümün numarası (her derlemede artar) ve görünen adı.</summary>
        int VersionCode { get; }
        string VersionName { get; }
        /// <summary>
        /// Bağlantıyı tarayıcıda açar. Güncelleme APK'sı böyle indirilir ve tarayıcının/dosya yöneticisinin
        /// yükleyicisiyle kurulur: uygulamanın kendisi "uygulama yükleme" izni istemez (Play Protect bu izni
        /// tanınmayan uygulamalarda güvensiz sayıyor).
        /// </summary>
        void OpenUrl(string url);
        /// <summary>Gösterilen bir bildirimi kaldırır (ör. sohbet açılınca o kişinin mesaj bildirimi).</summary>
        void CancelNotification(string tag);
        /// <summary>Uygulamanın kendi klasöründe geçici dosyalar için yer.</summary>
        string CacheDirectory { get; }
    }

    public static class AppPlatform
    {
        public static IAppPlatform? Current { get; set; }
    }
}
