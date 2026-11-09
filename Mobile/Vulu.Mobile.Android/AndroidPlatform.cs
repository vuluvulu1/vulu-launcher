using Android.App;
using Android.Content;
using Android.Content.PM;
using VuluMobile.Services;

namespace VuluMobile.Android
{
    /// <summary>Sürüm, APK kurma ve bildirim kaldırma (launcher'ın Android sürümündeki kurulum yoluyla aynı).</summary>
    internal sealed class AndroidPlatform(Context context) : IAppPlatform
    {
        private PackageInfo? Info => context.PackageManager?.GetPackageInfo(context.PackageName!, 0);

        public int VersionCode => (int)(Info?.LongVersionCode ?? 0);
        public string VersionName => Info?.VersionName ?? "";
        public bool CanInstallPackages => context.PackageManager?.CanRequestPackageInstalls() == true;
        public string CacheDirectory => context.CacheDir?.AbsolutePath ?? Path.GetTempPath();

        public void OpenInstallSettings()
        {
            var intent = new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,
                global::Android.Net.Uri.Parse("package:" + context.PackageName));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }

        public void CancelNotification(string tag) => Notifications.Cancel(context, tag);

        /// <summary>
        /// PackageInstaller oturumu. Android 12+ uygulama kendini bir kez güncelledikten sonra sonraki güncellemeler
        /// onay sormadan kurulabilir; ilk seferde Android onay ister. Sonuç <see cref="UpdateReceiver"/>'a gelir.
        /// </summary>
        public Task<string?> InstallAsync(string apkPath) => Task.Run<string?>(() =>
        {
            try
            {
                var installer = context.PackageManager!.PackageInstaller;
                var options = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
                options.SetAppPackageName(context.PackageName);
                if (OperatingSystem.IsAndroidVersionAtLeast(31))
                    options.SetRequireUserAction((int)PackageInstallUserAction.NotRequired);

                int sessionId = installer.CreateSession(options);
                using var session = installer.OpenSession(sessionId);
                using (var input = File.OpenRead(apkPath))
                using (var output = session.OpenWrite("vulu-sosyal.apk", 0, input.Length))
                {
                    input.CopyTo(output);
                    session.Fsync(output);
                }
                var callback = new Intent(context, typeof(UpdateReceiver)).SetAction(UpdateReceiver.StatusAction);
                var pending = PendingIntent.GetBroadcast(context, sessionId, callback,
                    PendingIntentFlags.UpdateCurrent | (OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0));
                session.Commit(pending!.IntentSender);
                return null;
            }
            catch (Exception ex) { return "Kurulum başlatılamadı: " + ex.Message; }
        });
    }

    /// <summary>Yükleyicinin sonucu: onay gerekiyorsa Android'in onay ekranı açılır; başarısızsa bildirim.</summary>
    [BroadcastReceiver(Exported = false)]
    public sealed class UpdateReceiver : BroadcastReceiver
    {
        internal const string StatusAction = "com.vuluvulu.social.UPDATE_STATUS";

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent?.Action != StatusAction) return;
            var status = (PackageInstallStatus)intent.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure);
            switch (status)
            {
                case PackageInstallStatus.PendingUserAction:
#pragma warning disable CA1422 // eski sürümlerde tek yol bu
                    if (intent.GetParcelableExtra(Intent.ExtraIntent) is Intent confirm)
#pragma warning restore CA1422
                    {
                        confirm.AddFlags(ActivityFlags.NewTask);
                        context.StartActivity(confirm);
                    }
                    break;
                case PackageInstallStatus.Success:
                    break;
                default:
                    string message = intent.GetStringExtra(PackageInstaller.ExtraStatusMessage) ?? status.ToString();
                    Notifications.Show(context, Notifications.UpdatesChannel, "update", "update", "0", "Güncelleme kurulamadı",
                        status == PackageInstallStatus.FailureAborted ? "Kurulum iptal edildi." : message);
                    break;
            }
        }
    }

    /// <summary>Güncelleme kurulunca (uygulama kapanmıştır) "güncellendi" bildirimi.</summary>
    [BroadcastReceiver(Exported = true)]
    [IntentFilter(new[] { Intent.ActionMyPackageReplaced })]
    public sealed class PackageReplacedReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent?.Action != Intent.ActionMyPackageReplaced) return;
            string version = context.PackageManager?.GetPackageInfo(context.PackageName!, 0)?.VersionName ?? "";
            Notifications.Show(context, Notifications.UpdatesChannel, "update", "update", "0", "vulu Sosyal güncellendi",
                $"{version} kuruldu. Açmak için dokun.");
        }
    }
}
