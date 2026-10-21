namespace VuluLauncher
{
    /// <summary>
    /// Launcher'ın çalıştığı platform (Windows penceresi ya da Android uygulaması). Ortak kod pencere, bildirim,
    /// dosya seçme ve şifreleme gibi işletim sistemine bağlı işleri yalnızca bu arayüz üzerinden yapar.
    /// </summary>
    internal interface IPlatformHost
    {
        /// <summary>"windows" ya da "android"; arayüz buna göre bazı düğmeleri gizler.</summary>
        string Platform { get; }

        /// <summary>Oyun bu cihazda başlatılabiliyor mu (Android'de henüz değil).</summary>
        bool CanLaunchGame { get; }

        /// <summary>Launcher kendini Updater ile güncelleyebiliyor mu.</summary>
        bool CanSelfUpdate { get; }

        /// <summary>Masaüstü kısayolu ve Discord durumu gibi masaüstüne özgü özellikler.</summary>
        bool IsDesktop { get; }

        /// <summary>Arayüze JSON mesajı gönderir.</summary>
        void SendToUi(string json);

        /// <summary>Launcher şu an kullanıcının önünde mi (değilse bildirim gösterilir).</summary>
        bool IsForeground { get; }

        void BringToFront();
        void Minimize();
        void Close();

        /// <summary>Çerçevesiz pencereyi sürükleme (Windows). Diğer platformlarda etkisizdir.</summary>
        void BeginWindowDrag();
        void MoveWindowDrag();

        /// <summary>Sistem bildirimi; tıklanınca <see cref="LauncherApp.OnNotificationClicked"/> çağrılır.</summary>
        void Notify(string title, string body, string? tag);

        /// <summary>Tek dosya seçtirir. Vazgeçilirse null. Dönen yol uygulamanın okuyabileceği yerel bir dosyadır.</summary>
        Task<string?> PickFileAsync(string title, string filterName, string[] extensions);

        void OpenUrl(string url);

        /// <summary>Yerel bir dosyanın arayüzde (img src) kullanılabilecek adresi.</summary>
        string FileUrl(string path);

        /// <summary>Dosyayı varsayılan uygulamasıyla açar.</summary>
        void OpenFile(string path);

        /// <summary>Klasörü ya da dosyanın bulunduğu klasörü (dosya seçili olarak) gösterir.</summary>
        void ShowInFolder(string path, bool selectFile);

        /// <summary>Dosyayı panoya kopyalar (yapıştırılabilir).</summary>
        bool CopyFileToClipboard(string path);

        /// <summary>Dosyayı geri dönüşüm kutusuna taşır; yoksa siler.</summary>
        bool MoveToTrash(string path);

        /// <summary>Kullanıcının resimleri ve indirdikleri için klasörler.</summary>
        string PicturesDirectory { get; }
        string DownloadsDirectory { get; }

        /// <summary>Kayıtlı şifreyi bu cihaza/kullanıcıya bağlı olarak şifreler ve çözer.</summary>
        byte[] Protect(byte[] data);
        byte[] Unprotect(byte[] data);

        /// <summary>Launcher'ı belirtilen argümanla açan masaüstü kısayolu oluşturur ve yolunu döner.</summary>
        string CreateShortcut(string name, string arguments, string description, string? iconPath);

        /// <summary>Updater'ı açıp launcher'ı kapatır.</summary>
        void StartLauncherUpdate();

        /// <summary>
        /// İndirilmiş ve doğrulanmış uygulama paketini (Android'de APK) sistemin yükleyicisine verir. Kurulum izni
        /// yoksa kurmadan <see cref="PackageInstallResult.NeedsPermission"/> döner.
        /// </summary>
        Task<PackageInstallResult> InstallPackageAsync(string path);

        /// <summary>Uygulamaya paket kurma izninin verildiği sistem ayarını açar.</summary>
        void OpenInstallPermissionSettings();

        /// <summary>Bağlantı kotalı mı (mobil veri); büyük indirmeler önce sorulur.</summary>
        bool IsMeteredConnection { get; }
    }

    internal enum PackageInstallResult { Started, NeedsPermission }
}
