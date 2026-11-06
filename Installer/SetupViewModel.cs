using System.Collections.ObjectModel;
using System.Net;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluShared;

namespace Installer
{
    /// <summary>İlerleme adımı: İndir, Doğrula, Kur, Kısayollar.</summary>
    internal sealed partial class SetupStep(string key, string label) : ObservableObject
    {
        public string Key { get; } = key;
        public string Label { get; } = label;
        [ObservableProperty] private bool _isActive;
        [ObservableProperty] private bool _isDone;
    }

    /// <summary>
    /// Kurulum penceresi: seçenekler (klasör, kısayollar) → ilerleme → bitiş. Sürüm bilgisi açılışta sunucudan alınır;
    /// kurulum InstallEngine ile yapılır, durumu herhangi bir iş parçacığından gelir ve arayüze UI iş parçacığında yansır.
    /// </summary>
    internal sealed partial class SetupViewModel : ObservableObject
    {
        private const string DirHintText = "Oyun dosyaları ve dünyalar da bu klasörde durur.";
        private LatestRelease? _latest;
        private string _installedTo = "";

        public SetupViewModel(string? installedDir)
        {
            Reinstall = installedDir != null;
            Directory = installedDir ?? InstallInfo.DefaultDirectory;
            InstallText = Reinstall ? "Güncelle / Onar" : "Kur";
            foreach (var (key, label) in new[] { ("download", "İndir"), ("verify", "Doğrula"), ("install", "Kur"), ("shortcuts", "Kısayollar") })
                Steps.Add(new SetupStep(key, label));
            ShowDirectory(Directory, InstallEngine.FreeSpaceMb(Directory), null, InstallEngine.PathWarning(Directory));
        }

        public bool Reinstall { get; }
        public string InstallText { get; }
        public ObservableCollection<SetupStep> Steps { get; } = new();

        /// <summary>Pencere işlemleri (görünüm bağlar).</summary>
        public Action? CloseWindow { get; set; }
        public Action? MinimizeWindow { get; set; }
        public Func<Task<string?>>? PickFolder { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsOptions), nameof(IsProgress), nameof(IsDone))]
        private string _page = "options";
        public bool IsOptions => Page == "options";
        public bool IsProgress => Page == "progress";
        public bool IsDone => Page == "done";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
        private string _directory = "";
        [ObservableProperty] private string _dirHint = DirHintText;
        [ObservableProperty] private bool _dirHintIsError;
        [ObservableProperty] private bool _desktopShortcut = true;
        [ObservableProperty] private bool _startMenuShortcut = true;

        [ObservableProperty] private string _versionText = "sürüm bilgisi alınıyor…";
        [ObservableProperty] private string _sizeInfo = "";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasReleaseError))]
        private string? _releaseError;
        public bool HasReleaseError => ReleaseError != null;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(ReloadReleaseCommand))]
        private bool _hasRelease;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReloadReleaseCommand))]
        private bool _loadingRelease;

        [ObservableProperty] private string _statusTitle = "Hazırlanıyor…";
        [ObservableProperty] private string _detail = "";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsIndeterminate), nameof(Percent), nameof(PercentText))]
        private double? _progress;
        public bool IsIndeterminate => Progress == null && InstallError == null;
        public double Percent => (Progress ?? 0) * 100;
        public string PercentText => Progress is { } p ? $"%{Math.Round(p * 100)}" : "";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasInstallError), nameof(IsIndeterminate))]
        private string? _installError;
        public bool HasInstallError => InstallError != null;

        /// <summary>Dosyalar yerleştirilirken kapatılırsa kurulum yarım kalır.</summary>
        public bool Installing { get; private set; }

        [ObservableProperty] private string _doneText = "";

        // ── Klasör ────────────────────────────────────────────────────────
        private void ShowDirectory(string dir, long? freeMb, string? error, string? warning)
        {
            Directory = dir;
            DirHintIsError = error != null || warning != null;
            DirHint = error ?? (warning ?? DirHintText) + (freeMb is { } free ? " · Boş alan: " + FormatMb(free) : "");
        }

        [RelayCommand]
        private async Task Browse()
        {
            if (PickFolder == null || await PickFolder() is not { Length: > 0 } picked) return;
            // Seçilen klasörün içine "vuluLauncher" alt klasörü açılır (oyun dosyaları da burada durur)
            string last = Path.GetFileName(picked.TrimEnd('\\'));
            if (!last.Equals(InstallInfo.FolderName, StringComparison.OrdinalIgnoreCase) && !last.Equals(InstallInfo.AppName, StringComparison.OrdinalIgnoreCase))
                picked = Path.Combine(picked, InstallInfo.FolderName);
            ShowDirectory(picked, InstallEngine.FreeSpaceMb(picked), null, InstallEngine.PathWarning(picked));
        }

        /// <summary>Elle yazılan klasör (alandan çıkınca ya da Enter'da) denetlenir.</summary>
        public void CheckDirectory()
        {
            string asked = Directory;
            try { ShowDirectory(InstallEngine.ValidateDirectory(asked), InstallEngine.FreeSpaceMb(asked), null, InstallEngine.PathWarning(asked)); }
            catch (Exception ex) { ShowDirectory(asked, null, ex.Message, null); }
        }

        // ── Sürüm ─────────────────────────────────────────────────────────
        private bool CanReload() => !LoadingRelease && !HasRelease;

        [RelayCommand(CanExecute = nameof(CanReload))]
        public async Task ReloadReleaseAsync()
        {
            LoadingRelease = true;
            VersionText = "sürüm bilgisi alınıyor…";
            string? error = null;
            try
            {
                using var http = Releases.CreateHttpClient(TimeSpan.FromSeconds(30));
                _latest = await Releases.GetLatestAsync(http);
                var m = _latest.Manifest;
                VersionText = "v" + Releases.Clean(m.Version);
                SizeInfo = $"İndirme {FormatMb(m.Size / 1024.0 / 1024.0)} · Kurulum {FormatMb(m.Files.Sum(f => f.Size) / 1024.0 / 1024.0)}";
                HasRelease = true;
            }
            catch (ReleaseSignatureException ex) { error = ex.Message; }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { error = "Sunucuda henüz yayınlanmış bir vulu Launcher sürümü yok."; }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) { error = "Sunucu şu an çok fazla istek aldı. Birkaç dakika sonra tekrar dene."; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { error = "Sunucuya ulaşılamadı. İnternet bağlantını kontrol edip tekrar dene."; }
            catch (Exception ex) { error = "Sürüm bilgisi okunamadı: " + ex.Message; }
            ReleaseError = error;
            if (error != null) VersionText = "sürüm bilgisi alınamadı";
            LoadingRelease = false;
        }

        // ── Kurulum ───────────────────────────────────────────────────────
        private bool CanInstall() => HasRelease && Directory.Trim().Length > 0;

        [RelayCommand(CanExecute = nameof(CanInstall))]
        private async Task Install()
        {
            if (Installing) return;
            InstallError = null;
            Apply(new InstallStatus("download", "Hazırlanıyor…"));
            Page = "progress";
            Installing = true;
            try
            {
                if (_latest == null)
                {
                    using var http = Releases.CreateHttpClient(TimeSpan.FromSeconds(30));
                    _latest = await Releases.GetLatestAsync(http);
                }
                var engine = new InstallEngine(status => Dispatcher.UIThread.Post(() => Apply(status)));
                var options = new InstallOptions(Directory.Trim(), DesktopShortcut, StartMenuShortcut);
                string dir = await engine.RunAsync(options, _latest, CancellationToken.None);
                _installedTo = dir;
                DoneText = $"vulu Launcher {VersionText} · {dir}";
                Page = "done";
            }
            catch (Exception ex)
            {
                InstallError = ex.Message;
                Progress = 0;
            }
            finally
            {
                Installing = false;
            }
        }

        private void Apply(InstallStatus s)
        {
            int index = Steps.ToList().FindIndex(x => x.Key == s.Step);
            for (int i = 0; i < Steps.Count; i++)
            {
                Steps[i].IsDone = i < index || s.Step == "done";
                Steps[i].IsActive = i == index;
            }
            StatusTitle = s.Title;
            Detail = s.Detail ?? "";
            Progress = s.Progress;
        }

        [RelayCommand] private void Back() => Page = "options";

        [RelayCommand]
        private void Launch()
        {
            try
            {
                InstallEngine.Launch(_installedTo);
                CloseWindow?.Invoke();
            }
            catch (Exception ex)
            {
                Page = "progress";
                InstallError = "Launcher açılamadı: " + ex.Message;
            }
        }

        [RelayCommand]
        private void Close()
        {
            if (!Installing) CloseWindow?.Invoke();
        }

        [RelayCommand] private void Minimize() => MinimizeWindow?.Invoke();

        private static string FormatMb(double mb) =>
            mb >= 1024 ? (mb / 1024).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) + " GB" : Math.Round(mb) + " MB";
    }
}
