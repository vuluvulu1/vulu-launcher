using System.Text;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Security.Keystore;
using AndroidX.Core.App;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace VuluLauncher
{
    /// <summary>Android: HybridWebView, bildirimler, Keystore ve paylaşma menüsü.</summary>
    internal sealed class AndroidHost : IPlatformHost
    {
        internal const string NotificationChannel = "vulu";
        private const string KeyAlias = "vulu-credentials";

        private readonly HybridWebView _webView;
        private int _notificationId;

        public AndroidHost(HybridWebView webView)
        {
            _webView = webView;
            CreateNotificationChannel();
        }

        public string Platform => "android";
        // Oyun motoru (Amethyst) APK'ya eklendiyse oyun telefonda açılır
        public bool CanLaunchGame => AmethystRunner.Available;
        public IGameRunner? GameRunner => AmethystRunner.Available ? new AmethystRunner() : null;
        public bool CanSelfUpdate => false;
        public bool IsDesktop => false;

        public void SendToUi(string json) =>
            MainThread.BeginInvokeOnMainThread(() => _webView.SendRawMessage(json));

        public bool IsForeground => MainActivity.IsResumed;

        public void BringToFront() { }

        public void Minimize() => MainThread.BeginInvokeOnMainThread(() => Platform_Activity?.MoveTaskToBack(true));

        public void Close() => MainThread.BeginInvokeOnMainThread(() => Platform_Activity?.Finish());

        public void BeginWindowDrag() { }
        public void MoveWindowDrag() { }

        private static Activity? Platform_Activity => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;

        // ── Bildirimler ──────────────────────────────────────────────────
        private static void CreateNotificationChannel()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
            var channel = new NotificationChannel(NotificationChannel, "vulu Launcher", NotificationImportance.Default)
            {
                Description = "Mesajlar, beğeniler ve paket güncellemeleri"
            };
            var manager = (NotificationManager?)Android.App.Application.Context.GetSystemService(Context.NotificationService);
            manager?.CreateNotificationChannel(channel);
        }

        public void Notify(string title, string body, string? tag)
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(context, typeof(MainActivity));
            intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            if (tag != null) intent.PutExtra(MainActivity.NotificationTagExtra, tag);

            int id = Interlocked.Increment(ref _notificationId);
            var pending = PendingIntent.GetActivity(context, id, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

            var notification = new NotificationCompat.Builder(context, NotificationChannel)
                // Durum çubuğu ikonu tek renk olmalı; renkli uygulama ikonu beyaz kare görünür
                .SetSmallIcon(Android.Resource.Drawable.SymActionChat)
                .SetContentTitle(title)
                .SetContentText(body)
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(body))
                .SetContentIntent(pending)
                .SetAutoCancel(true)
                .Build();

            try { NotificationManagerCompat.From(context).Notify(id, notification); }
            catch (Java.Lang.SecurityException) { /* bildirim izni verilmedi */ }
        }

        // ── Dosyalar ─────────────────────────────────────────────────────
        public async Task<string?> PickFileAsync(string title, string filterName, string[] extensions)
        {
            var mimeTypes = extensions.Select(MimeOf).Distinct().ToArray();
            var options = new PickOptions
            {
                PickerTitle = title,
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.Android] = mimeTypes
                })
            };
            var result = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(options));
            if (result == null) return null;

            // Seçilen dosya bir content:// adresi olabilir; uzantısıyla birlikte önbelleğe kopyalanır ki
            // ortak kod onu normal bir dosya gibi okuyabilsin
            string extension = Path.GetExtension(result.FileName);
            string target = Path.Combine(FileSystem.CacheDirectory, "picked", Guid.NewGuid().ToString("N") + extension);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using (var source = await result.OpenReadAsync())
            await using (var file = File.Create(target))
                await source.CopyToAsync(file);
            return target;
        }

        private static string MimeOf(string extension) => extension.ToLowerInvariant() switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            "gif" => "image/gif",
            _ => "*/*"
        };

        public void OpenUrl(string url) =>
            MainThread.BeginInvokeOnMainThread(async () => await Launcher.Default.OpenAsync(url));

        public string FileUrl(string path) => LocalFileServer.UrlFor(path);

        public void OpenFile(string path) =>
            MainThread.BeginInvokeOnMainThread(async () =>
                await Launcher.Default.OpenAsync(new OpenFileRequest(Path.GetFileName(path), new ReadOnlyFile(path))));

        /// <summary>Android'de dosya gezgini yok: dosyalar paylaşma menüsüyle gösterilir.</summary>
        public void ShowInFolder(string path, bool selectFile)
        {
            if (!selectFile || !File.Exists(path))
                throw new InvalidOperationException("Klasörler bu cihazda açılamıyor.");
            MainThread.BeginInvokeOnMainThread(async () =>
                await Share.Default.RequestAsync(new ShareFileRequest(Path.GetFileName(path), new ShareFile(path))));
        }

        public bool CopyFileToClipboard(string path)
        {
            // Resmi panoya koymak yerine paylaşma menüsü açılır (Discord, WhatsApp, galeri…)
            ShowInFolder(path, selectFile: true);
            return true;
        }

        public bool MoveToTrash(string path)
        {
            File.Delete(path);
            return !File.Exists(path);
        }

        public string PicturesDirectory =>
            Android.App.Application.Context.GetExternalFilesDir(Android.OS.Environment.DirectoryPictures)?.AbsolutePath
            ?? Path.Combine(FileSystem.AppDataDirectory, "Pictures");

        public string DownloadsDirectory =>
            Android.App.Application.Context.GetExternalFilesDir(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath
            ?? Path.Combine(FileSystem.AppDataDirectory, "Downloads");

        // ── Kayıtlı şifre: Android Keystore'daki AES anahtarıyla (cihazdan çıkarılamaz) ──
        public byte[] Protect(byte[] data)
        {
            var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.EncryptMode, CredentialKey());
            byte[] iv = cipher.GetIV()!;
            byte[] encrypted = cipher.DoFinal(data)!;
            return [(byte)iv.Length, .. iv, .. encrypted];
        }

        public byte[] Unprotect(byte[] data)
        {
            int ivLength = data[0];
            var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.DecryptMode, CredentialKey(), new GCMParameterSpec(128, data[1..(1 + ivLength)]));
            return cipher.DoFinal(data[(1 + ivLength)..])!;
        }

        private static IKey CredentialKey()
        {
            var store = KeyStore.GetInstance("AndroidKeyStore")!;
            store.Load(null);
            if (store.GetKey(KeyAlias, null) is { } existing) return existing;

            var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
            generator.Init(new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes(KeyProperties.BlockModeGcm)
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                .Build());
            return generator.GenerateKey()!;
        }

        // ── Masaüstüne özgü ──────────────────────────────────────────────
        public string CreateShortcut(string name, string arguments, string description, string? iconPath) =>
            throw new InvalidOperationException("Kısayollar bu cihazda desteklenmiyor.");

        public void StartLauncherUpdate() =>
            throw new InvalidOperationException("Android sürümü yeni APK ile güncellenir.");

        // ── Kendi kendini güncelleme ─────────────────────────────────────
        public bool IsMeteredConnection
        {
            get
            {
                var manager = (Android.Net.ConnectivityManager?)Android.App.Application.Context.GetSystemService(Context.ConnectivityService);
                return manager?.IsActiveNetworkMetered ?? false;
            }
        }

        public void OpenInstallPermissionSettings() => MainThread.BeginInvokeOnMainThread(() =>
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
                Android.Net.Uri.Parse("package:" + context.PackageName));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        });

        /// <summary>
        /// APK'yı PackageInstaller oturumuyla kurdurur. Android 12+ uygulama kendisini bir kez güncelledikten sonra
        /// (yükleyicisi kendisi olunca) sonraki güncellemeler onay sormadan kurulabilir; ilk seferde Android onay ister.
        /// Sonuç <see cref="AppUpdateReceiver"/>'a gelir.
        /// </summary>
        public Task<PackageInstallResult> InstallPackageAsync(string path) => Task.Run(() =>
        {
            var context = Android.App.Application.Context;
            if (!context.PackageManager!.CanRequestPackageInstalls()) return PackageInstallResult.NeedsPermission;

            var installer = context.PackageManager.PackageInstaller;
            var options = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
            options.SetAppPackageName(context.PackageName);
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
                options.SetRequireUserAction((int)PackageInstallUserAction.NotRequired);

            int sessionId = installer.CreateSession(options);
            using var session = installer.OpenSession(sessionId);
            using (var input = File.OpenRead(path))
            using (var output = session.OpenWrite("vulu-launcher.apk", 0, input.Length))
            {
                input.CopyTo(output);
                session.Fsync(output);
            }

            // Yükleyici sonucu bu yayına ekler; Android 12+ bunun için değiştirilebilir (mutable) PendingIntent ister
            var callback = new Intent(context, typeof(AppUpdateReceiver)).SetAction(AppUpdateReceiver.StatusAction);
            var pending = PendingIntent.GetBroadcast(context, sessionId, callback,
                PendingIntentFlags.UpdateCurrent | (OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0));
            session.Commit(pending!.IntentSender);
            return PackageInstallResult.Started;
        });
    }
}
