using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    internal sealed record DownloadRow(string Name, string Size, double? Percent, string? Error)
    {
        public bool HasPercent => Percent != null;
        public bool HasError => Error != null;
    }

    /// <summary>
    /// İndirme paneli: Core'daki indirme işinin anlık durumu (hangi dosyalar iniyor, ne kadarı kaldı, hız, kalan süre).
    /// Yeni iş öncekinin yerini alır; tamamlanan iş, üstüne gelinmiyorsa birkaç saniye sonra kapanır.
    /// </summary>
    internal sealed partial class DownloadsViewModel(MainViewModel main) : ObservableObject
    {
        private int _jobId = -1;
        private string _state = "";
        private IDisposable? _hideTimer;

        public ObservableCollection<DownloadRow> Failed { get; } = new();
        public ObservableCollection<DownloadRow> Active { get; } = new();
        public ObservableCollection<DownloadRow> Recent { get; } = new();

        [ObservableProperty] private bool _visible;
        [ObservableProperty] private bool _expanded = UiState.Get("downloadsExpanded") != "false";
        [ObservableProperty] private bool _hovered;
        [ObservableProperty] private string _title = "";
        [ObservableProperty] private string _phase = "";
        [ObservableProperty] private bool _running;
        [ObservableProperty] private bool _failedState;
        [ObservableProperty] private bool _paused;
        [ObservableProperty] private bool _pausable;
        [ObservableProperty] private double _percent;
        [ObservableProperty] private bool _indeterminate;
        [ObservableProperty] private string _percentText = "";
        [ObservableProperty] private string _stats = "";
        [ObservableProperty] private string _activeTitle = "";
        [ObservableProperty] private string? _empty;
        public bool HasFailed => Failed.Count > 0;
        public bool HasActive => Active.Count > 0;
        public bool HasRecent => Recent.Count > 0;
        public bool Done => !Running && !FailedState;
        public string PauseText => Paused ? "Sürdür" : "Duraklat";
        /// <summary>Duraklatma yalnızca süren ve duraklatılabilen işte.</summary>
        public bool CanPause => Pausable && Running;
        partial void OnPausableChanged(bool value) => OnPropertyChanged(nameof(CanPause));
        partial void OnRunningChanged(bool value) => OnPropertyChanged(nameof(CanPause));
        partial void OnPausedChanged(bool value) => OnPropertyChanged(nameof(PauseText));
        partial void OnExpandedChanged(bool value) => UiState.Set("downloadsExpanded", value ? "true" : "false");

        partial void OnHoveredChanged(bool value)
        {
            if (!value) ScheduleHide();
        }

        public void OnDownload(JsonElement job)
        {
            int id = job.Int("id");
            // Aynı işin eski bir durumu bitişten sonra gelirse yok sayılır
            if (id == _jobId && _state != "running") return;
            _jobId = id;
            _state = job.Str("state") ?? "running";
            Running = _state == "running";
            FailedState = _state == "failed";
            OnPropertyChanged(nameof(Done));
            Paused = job.Bool("paused");
            Pausable = job.Bool("pausable");
            Title = Running ? job.Str("title") ?? "" : job.Str("message") ?? job.Str("title") ?? "";
            Phase = Running ? (Paused ? "Duraklatıldı" : job.Str("phase") ?? "Hazırlanıyor") : _state == "done" ? "Tamamlandı" : "Durdu";

            double filesDone = job.Get("files").Num("done"), filesTotal = job.Get("files").Num("total");
            double bytesDone = job.Get("bytes").Num("done"), bytesTotal = job.Get("bytes").Num("total");
            double? pct = _state == "done" ? 100 : bytesTotal > 0 ? Math.Min(100, bytesDone * 100 / bytesTotal) : filesTotal > 0 ? filesDone * 100 / filesTotal : null;
            Indeterminate = pct == null;
            Percent = pct ?? 0;
            PercentText = pct is { } p ? $"{Math.Floor(p)}%" : "";

            var stats = new List<string>();
            if (filesTotal > 0) stats.Add($"{filesDone:0} / {filesTotal:0} dosya");
            if (bytesTotal > 0) stats.Add($"{Bytes(bytesDone)} / {Bytes(bytesTotal)}");
            else if (bytesDone > 0) stats.Add(Bytes(bytesDone));
            if (Running && job.Num("speed") > 0) stats.Add(Bytes(job.Num("speed")) + "/sn");
            if (Running && job.Get("eta").ValueKind == JsonValueKind.Number) stats.Add("~" + Duration(job.Num("eta")) + " kaldı");
            if (!Running && job.Num("elapsed") > 0) stats.Add(Duration(job.Num("elapsed")) + " sürdü");
            Stats = string.Join("   ", stats);

            Failed.Clear();
            foreach (var f in job.Items("failed")) Failed.Add(new DownloadRow(f.Str("name") ?? "", "", null, f.Str("error")));
            Active.Clear();
            foreach (var a in job.Items("active"))
            {
                double size = a.Num("size"), done = a.Num("done");
                // Oyun dosyaları tek tek izlenmez; yalnızca son denetlenen dosyaların adı gelir
                bool tracked = size > 0 || done > 0;
                string name = a.Str("name") ?? "";
                Active.Add(new DownloadRow(tracked ? name : name.Split('/', '\\').Last(), size > 0 ? $"{Bytes(done)} / {Bytes(size)}" : "", size > 0 ? Math.Min(100, done * 100 / size) : null, null));
            }
            Recent.Clear();
            foreach (var r in job.Items("recent")) Recent.Add(new DownloadRow(r.Str("name") ?? "", r.Num("size") > 0 ? Bytes(r.Num("size")) : "", null, null));
            int queued = job.Int("queued");
            ActiveTitle = (Running ? "Şu an" : "Son işlenen") + (queued > 0 ? $" · sırada {queued}" : "");
            Empty = Failed.Count + Active.Count + Recent.Count == 0 ? (Running ? "Dosyalar hazırlanıyor…" : "İndirilecek dosya yoktu.") : null;
            OnPropertyChanged(nameof(HasFailed));
            OnPropertyChanged(nameof(HasActive));
            OnPropertyChanged(nameof(HasRecent));

            Visible = true;
            if (Running) { _hideTimer?.Dispose(); _hideTimer = null; }
            else if (_state == "done") ScheduleHide();
        }

        private void ScheduleHide()
        {
            if (_state != "done" || !Visible) return;
            _hideTimer?.Dispose();
            _hideTimer = DispatcherTimer.RunOnce(() =>
            {
                if (Hovered) { ScheduleHide(); return; }
                Visible = false;
            }, TimeSpan.FromSeconds(6));
        }

        [RelayCommand] private void Toggle() => Expanded = !Expanded;
        [RelayCommand] private void Close() { _hideTimer?.Dispose(); Visible = false; }

        [RelayCommand]
        private async Task Pause()
        {
            try { await CoreClient.Instance.Call("downloads.pause", new { id = _jobId, paused = !Paused }); } catch { /* iş bitmiş olabilir */ }
        }

        [RelayCommand]
        private async Task Cancel()
        {
            if (!await main.ConfirmAsync("İndirme iptal edilsin mi?", $"{Title} durdurulur; yarım inen dosyalar silinir.", "İptal et", "Devam et", danger: true)) return;
            try { await CoreClient.Instance.Call("downloads.cancel", new { id = _jobId }); } catch { /* iş bitmiş olabilir */ }
        }

        public static string Bytes(double bytes)
        {
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            if (bytes <= 0) return "0 MB";
            double mb = bytes / 1024 / 1024;
            if (mb < 1) return Math.Max(1, Math.Round(bytes / 1024)) + " KB";
            if (mb < 100) return mb.ToString("0.0", tr) + " MB";
            if (mb < 1024) return Math.Round(mb) + " MB";
            return (mb / 1024).ToString("0.00", tr) + " GB";
        }

        public static string Duration(double seconds)
        {
            if (seconds < 60) return Math.Max(1, Math.Round(seconds)) + " sn";
            int minutes = (int)(seconds / 60), rest = (int)Math.Round(seconds % 60);
            return rest > 0 ? $"{minutes} dk {rest} sn" : $"{minutes} dk";
        }
    }
}
