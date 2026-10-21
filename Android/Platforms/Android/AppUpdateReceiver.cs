using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.App;

namespace VuluLauncher
{
    /// <summary>
    /// Kendi kendini güncellemenin iki yayını:
    ///   - PackageInstaller'ın sonucu: onay gerekiyorsa Android'in onay ekranı açılır, kurulamadıysa launcher'a bildirilir;
    ///   - MY_PACKAGE_REPLACED: güncelleme kurulunca (launcher kapanmıştır) "güncellendi" bildirimi gösterilir.
    /// </summary>
    [BroadcastReceiver(Exported = false)]
    public class AppUpdateReceiver : BroadcastReceiver
    {
        internal const string StatusAction = "com.vuluvulu.launcher.UPDATE_STATUS";

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
                    break;   // uygulama yeni sürümle değişti; bildirimi PackageReplacedReceiver gösterir
                default:
                    string message = intent.GetStringExtra(PackageInstaller.ExtraStatusMessage) ?? status.ToString();
                    MainThread.BeginInvokeOnMainThread(() => MainPage.Current?.AppUpdateFailed(
                        status == PackageInstallStatus.FailureAborted ? "kurulum iptal edildi" : message));
                    break;
            }
        }
    }

    [BroadcastReceiver(Exported = true)]
    [IntentFilter(new[] { Intent.ActionMyPackageReplaced })]
    public class PackageReplacedReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent?.Action != Intent.ActionMyPackageReplaced) return;

            string version = context.PackageManager?.GetPackageInfo(context.PackageName!, 0)?.VersionName ?? "";
            var open = new Intent(context, typeof(MainActivity)).SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            var pending = PendingIntent.GetActivity(context, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            var notification = new NotificationCompat.Builder(context, AndroidHost.NotificationChannel)
                .SetSmallIcon(Android.Resource.Drawable.StatSysDownloadDone)
                .SetContentTitle("vulu Launcher güncellendi")
                .SetContentText($"v{version} kuruldu. Açmak için dokun.")
                .SetContentIntent(pending)
                .SetAutoCancel(true)
                .Build();
            try { NotificationManagerCompat.From(context).Notify(0x7101, notification); }
            catch (Java.Lang.SecurityException) { /* bildirim izni yok */ }
        }
    }
}
