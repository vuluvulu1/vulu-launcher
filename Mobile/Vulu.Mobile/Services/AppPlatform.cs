namespace VuluMobile.Services
{
    /// <summary>Telefona özgü işler: sürüm bilgisi, APK kurma, bildirim kaldırma. Masaüstü test penceresinde yoktur.</summary>
    public interface IAppPlatform
    {
        /// <summary>Kurulu sürümün numarası (her derlemede artar) ve görünen adı.</summary>
        int VersionCode { get; }
        string VersionName { get; }
        /// <summary>Kullanıcı bu uygulamaya "bilinmeyen uygulamaları yükleme" iznini verdi mi.</summary>
        bool CanInstallPackages { get; }
        void OpenInstallSettings();
        /// <summary>APK'yı Android'in yükleyicisine verir; Android onay ekranı açar. Hata varsa nedeni, yoksa null.</summary>
        Task<string?> InstallAsync(string apkPath);
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
