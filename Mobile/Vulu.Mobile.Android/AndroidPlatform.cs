using Android.App;
using Android.Content;
using Android.Content.PM;
using VuluMobile.Services;

namespace VuluMobile.Android
{
    /// <summary>Sürüm bilgisi, bağlantı açma ve bildirim kaldırma.</summary>
    internal sealed class AndroidPlatform(Context context) : IAppPlatform
    {
        private PackageInfo? Info => context.PackageManager?.GetPackageInfo(context.PackageName!, 0);

        public int VersionCode => (int)(Info?.LongVersionCode ?? 0);
        public string VersionName => Info?.VersionName ?? "";
        public string CacheDirectory => context.CacheDir?.AbsolutePath ?? Path.GetTempPath();

        public void OpenUrl(string url)
        {
            var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }

        public void CancelNotification(string tag) => Notifications.Cancel(context, tag);
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
