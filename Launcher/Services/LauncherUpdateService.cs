using System.Reflection;
using VuluShared;

namespace VuluLauncher
{
    public enum UpdateCheckStatus
    {
        UpToDate,
        UpdateAvailable,
        CheckFailed
    }

    /// <summary>
    /// GitHub Releases üzerinden launcher sürüm kontrolünü yapar. İndirme ve kurulum Updater.exe'nin işidir.
    /// </summary>
    public class LauncherUpdateService
    {
        /// <summary>Derlemedeki sürüm (csproj'daki Version); yayın scripti her release'te ayarlar.</summary>
        public static readonly string LocalVersion = Releases.Clean(
            typeof(LauncherUpdateService).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

        private static readonly HttpClient _http = Releases.CreateHttpClient(TimeSpan.FromSeconds(20));

        /// <summary>
        /// Güncelleme gerekip gerekmediğini kontrol eder.
        /// GitHub'a ulaşılamazsa CheckFailed döner ("güncel" sayılmaz).
        /// </summary>
        public async Task<(UpdateCheckStatus Status, ReleaseManifest? Latest)> CheckForUpdateAsync()
        {
            ReleaseManifest latest;
            try
            {
                latest = (await Releases.GetLatestAsync(_http)).Manifest;
            }
            catch
            {
                return (UpdateCheckStatus.CheckFailed, null);
            }

            return Releases.IsNewer(latest.Version, LocalVersion)
                ? (UpdateCheckStatus.UpdateAvailable, latest)
                : (UpdateCheckStatus.UpToDate, latest);
        }
    }
}
