using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Updater
{
    /// <summary>
    /// Güncelleyici ve kaldırma penceresinin durumu. Motor (UpdateEngine / Uninstaller) durumu herhangi bir
    /// iş parçacığından bildirir; arayüze UI iş parçacığında yansır.
    /// </summary>
    internal sealed partial class UpdaterViewModel : ObservableObject
    {
        private readonly string _installDir;
        private readonly UpdateEngine _engine;
        private CancellationTokenSource _cancel = new();
        private bool _started, _uninstalling;

        public UpdaterViewModel(string installDir, IReadOnlyList<int> waitPids, bool uninstallMode, string version)
        {
            _installDir = installDir;
            UninstallMode = uninstallMode;
            Label = (uninstallMode ? "Kaldırma · v" : "Güncelleyici · v") + version;
            _engine = new UpdateEngine(installDir, waitPids, Report);
        }

        public bool UninstallMode { get; }
        public string Label { get; }

        /// <summary>Pencere kapatılmak istenir (görünüm bağlar).</summary>
        public Action? CloseWindow { get; set; }
        public Action? MinimizeWindow { get; set; }

        /// <summary>Dosyalar yer değiştirirken kapatılırsa kurulum yarım kalır; o sırada kapatma engellenir.</summary>
        public bool CanClose => !_engine.IsApplying && !_uninstalling;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsError), nameof(IsConfirm), nameof(IsUninstalled), nameof(IsLaunching), nameof(ShowBar),
            nameof(IsIndeterminate), nameof(ShowNotes), nameof(ShowErrorActions), nameof(CanLaunchAnyway))]
        private string _state = "checking";
        [ObservableProperty] private string _title = "Başlatılıyor…";
        [ObservableProperty] private string _detail = "";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsIndeterminate), nameof(Percent))]
        private double? _progress;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowNotes))]
        private string? _notes;
        [ObservableProperty] private string _notesTitle = "";
        [ObservableProperty] private bool _deleteData;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
        private bool _uninstallSent;

        public bool IsError => State == "error";
        public bool IsConfirm => State == "confirm";
        public bool IsUninstalled => State == "uninstalled";
        public bool IsLaunching => State == "launching";
        public bool ShowBar => State is not ("error" or "confirm" or "uninstalled");
        public bool IsIndeterminate => Progress == null && !IsLaunching;
        public double Percent => IsLaunching ? 100 : (Progress ?? 0) * 100;
        public bool ShowNotes => !string.IsNullOrWhiteSpace(Notes) && !IsError;
        public bool ShowErrorActions => IsError;
        public bool CanLaunchAnyway => IsError && !UninstallMode;

        private void Report(UpdateStatus status) => Dispatcher.UIThread.Post(() => Apply(status));

        private void Apply(UpdateStatus status)
        {
            State = status.State;
            Title = status.Title;
            Detail = status.Detail ?? "";
            Progress = status.Progress;
            if (!string.IsNullOrWhiteSpace(status.Notes))
            {
                Notes = status.Notes.Trim();
                NotesTitle = $"v{status.Version} yenilikleri";
            }
            if (IsConfirm) UninstallSent = false;
            OnPropertyChanged(nameof(Percent));
        }

        /// <summary>Pencere açıldı: güncelleme denetimi (ya da kaldırma sorusu) başlar.</summary>
        public void Start()
        {
            if (_started) return;
            _started = true;
            if (UninstallMode) ShowUninstallQuestion();
            else _ = RunAsync();
        }

        private void ShowUninstallQuestion() =>
            Apply(new UpdateStatus("confirm", "vulu Launcher kaldırılsın mı?", Path.GetFullPath(_installDir).TrimEnd(Path.DirectorySeparatorChar)));

        private async Task RunAsync()
        {
            try
            {
                if (await _engine.RunAsync(_cancel.Token)) LaunchAndClose();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Apply(new UpdateStatus("error", "Güncelleme başarısız", ex.Message));
            }
        }

        [RelayCommand]
        private void Retry()
        {
            if (UninstallMode) { ShowUninstallQuestion(); return; }
            _cancel = new CancellationTokenSource();
            _ = RunAsync();
        }

        [RelayCommand]
        private void Launch()
        {
            if (!UninstallMode) LaunchAndClose();
        }

        private void LaunchAndClose()
        {
            try
            {
                _engine.LaunchLauncher();
                CloseWindow?.Invoke();
            }
            catch (Exception ex)
            {
                Apply(new UpdateStatus("error", "Launcher açılamadı", ex.Message));
            }
        }

        private bool CanUninstall() => !UninstallSent;

        [RelayCommand(CanExecute = nameof(CanUninstall))]
        private async Task Uninstall()
        {
            if (_uninstalling) return;
            UninstallSent = true;
            _uninstalling = true;
            try
            {
                await Uninstaller.RunAsync(_installDir, DeleteData, Report);
            }
            catch (Exception ex)
            {
                Report(new UpdateStatus("error", "Kaldırılamadı", ex.Message));
            }
            finally
            {
                _uninstalling = false;
            }
        }

        [RelayCommand]
        private void Close()
        {
            if (CanClose) CloseWindow?.Invoke();
        }

        [RelayCommand] private void Minimize() => MinimizeWindow?.Invoke();

        /// <summary>Pencere kapandı: süren indirme durdurulur.</summary>
        public void Cancel() => _cancel.Cancel();
    }
}
