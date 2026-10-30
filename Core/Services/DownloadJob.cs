using System.Collections.Concurrent;
using System.Diagnostics;

namespace VuluLauncher
{
    /// <summary>
    /// Arayüzdeki indirme panelini besleyen iş: hangi dosyaların indiği, ne kadar kaldığı, hız ve kalan süre.
    /// İş, başlatıldığı async akışın "Current" işi olur; içinden çağrılan servisler (Java, oyun dosyaları)
    /// kendi dosyalarını aynı işe ekler. Durum en çok ~6 kez/sn arayüze gönderilir.
    ///
    /// Hiçbir şey indirmeden hızla biten işler (her şey zaten kurulu) panelde hiç görünmez.
    /// </summary>
    internal sealed class DownloadJob : IDisposable
    {
        /// <summary>LauncherApp tarafından köprüye bağlanır.</summary>
        public static Action<object>? Emit;

        private static readonly AsyncLocal<DownloadJob?> CurrentJob = new();
        private static int _nextId;
        private static readonly ConcurrentDictionary<int, DownloadJob> Running = new();

        /// <summary>Arayüzdeki "iptal et": işin bütün indirmeleri durur, iş "İptal edildi" diye biter.</summary>
        public static bool Cancel(int id)
        {
            if (!Running.TryGetValue(id, out var job)) return false;
            job._cancel.Cancel();
            job.SetPaused(false);   // duraklatılmışsa bekleyen indirmeler de iptali görsün
            return true;
        }

        /// <summary>Arayüzdeki "duraklat/sürdür": launcher'ın kendi indirmeleri parça arasında bekler.</summary>
        public static bool Pause(int id, bool paused)
        {
            if (!Running.TryGetValue(id, out var job)) return false;
            job.SetPaused(paused);
            return true;
        }

        private readonly CancellationTokenSource _cancel = new();
        private volatile TaskCompletionSource? _resume;   // null değilse duraklatılmış
        private volatile bool _pausable = true;

        /// <summary>İptal edilince tetiklenir; iş boyunca yapılan indirmelere verilir.</summary>
        public CancellationToken Token => _cancel.Token;

        /// <summary>Duraklatılabilir mi: CmlLib'in oyun dosyası indirmesi dışarıdan durdurulamıyor, yalnızca iptal edilir.</summary>
        public bool Pausable { set { _pausable = value; if (!value) SetPaused(false); Touch(); } }

        private void SetPaused(bool paused)
        {
            lock (_gate)
            {
                if (paused && _pausable && _resume == null) _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                else if (!paused && _resume != null) { _resume.TrySetResult(); _resume = null; }
                _dirty = true;
            }
        }

        /// <summary>Duraklatıldıysa sürdürülene (ya da iptal edilene) kadar bekler.</summary>
        public async Task WaitIfPausedAsync(CancellationToken cancellation)
        {
            var resume = _resume;
            if (resume != null) await resume.Task.WaitAsync(cancellation);
        }

        /// <summary>Bu async akışta süren iş; yoksa null.</summary>
        public static DownloadJob? Current => CurrentJob.Value is { IsFinished: false } job ? job : null;

        private const int ShowAfterMs = 1200;
        private const int EmitIntervalMs = 160;

        private readonly object _gate = new();
        private readonly List<DownloadItem> _items = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Queue<(double Seconds, long Bytes)> _samples = new();
        private readonly Timer _timer;
        private readonly int _id = Interlocked.Increment(ref _nextId);
        private readonly DownloadJob? _previous;

        private string _title;
        private string _phase = "";
        private string _state = "running";
        private string _message = "";
        private bool _visible, _dirty = true;

        // Dosya dosya izlenemeyen işler (oyun dosyaları) toplu sayı bildirir
        private int _aggregateDone, _aggregateTotal;
        private long _aggregateBytes, _aggregateBytesTotal;
        private readonly Queue<string> _aggregateRecent = new();

        public bool IsFinished => _state != "running";

        public DownloadJob(string title)
        {
            _title = title;
            _previous = CurrentJob.Value;
            CurrentJob.Value = this;
            Running[_id] = this;
            _timer = new Timer(_ => Tick(), null, EmitIntervalMs, EmitIntervalMs);
        }

        public string Title { set { lock (_gate) { _title = value; _dirty = true; } } }

        /// <summary>Şu an yapılan iş: "Modlar indiriliyor", "Java 21 kuruluyor"…</summary>
        public string Phase { set { lock (_gate) { _phase = value; _dirty = true; } } }

        public DownloadItem Add(string name, long size)
        {
            var item = new DownloadItem(this, name, size);
            lock (_gate) { _items.Add(item); _dirty = true; }
            return item;
        }

        /// <summary>Toplu ilerleme (CmlLib): kaç dosyadan kaçı tamam, en son hangisi.</summary>
        public void Aggregate(int done, int total, string? current)
        {
            lock (_gate)
            {
                _aggregateDone = done;
                _aggregateTotal = total;
                if (!string.IsNullOrEmpty(current) && (_aggregateRecent.Count == 0 || _aggregateRecent.Last() != current))
                {
                    _aggregateRecent.Enqueue(current);
                    while (_aggregateRecent.Count > 4) _aggregateRecent.Dequeue();
                }
                _dirty = true;
            }
        }

        public void AggregateBytes(long done, long total)
        {
            lock (_gate) { _aggregateBytes = done; _aggregateBytesTotal = total; _dirty = true; }
        }

        public void Complete(string message) => Finish("done", message);

        public void Fail(string message) => Finish("failed", _cancel.IsCancellationRequested ? "İptal edildi" : message);

        public bool IsCancelled => _cancel.IsCancellationRequested;

        private void Finish(string state, string message)
        {
            lock (_gate)
            {
                if (IsFinished) return;
                _state = state;
                _message = message;
                _dirty = true;
            }
            _timer.Dispose();
            Running.TryRemove(_id, out _);
            _resume?.TrySetResult();
            // Görünmeden biten iş sessizce kapanır; görünen işin sonucu gösterilir
            if (_visible || state == "failed" && TotalBytes() > 0) Send(force: true);
            if (CurrentJob.Value == this) CurrentJob.Value = _previous;
        }

        /// <summary>Tamamlanmadan bırakılan iş (hata fırlatıldı) başarısız sayılır.</summary>
        public void Dispose()
        {
            if (!IsFinished) Fail("İşlem yarıda kaldı.");
        }

        internal void Touch() { lock (_gate) _dirty = true; }

        private void Tick()
        {
            lock (_gate)
            {
                if (IsFinished || !_dirty) return;
                if (!_visible && DoneBytes() == 0 && _clock.ElapsedMilliseconds < ShowAfterMs) return;
                _visible = true;
            }
            Send(force: false);
        }

        private long DoneBytes() => _items.Sum(i => i.Done) + _aggregateBytes;
        private long TotalBytes() => _items.Sum(i => i.Size) + _aggregateBytesTotal;

        private void Send(bool force)
        {
            object snapshot;
            lock (_gate)
            {
                if (!force && !_dirty) return;
                _dirty = false;

                long done = DoneBytes(), total = TotalBytes();
                double now = _clock.Elapsed.TotalSeconds;
                _samples.Enqueue((now, done));
                while (_samples.Count > 2 && now - _samples.Peek().Seconds > 4) _samples.Dequeue();
                var oldest = _samples.Peek();
                double speed = now - oldest.Seconds > 0.3 ? Math.Max(0, (done - oldest.Bytes) / (now - oldest.Seconds)) : 0;
                double? eta = speed > 1024 && total > done ? (total - done) / speed : null;

                int filesDone = _items.Count(i => i.State == "done") + _aggregateDone;
                int filesTotal = _items.Count + _aggregateTotal;

                var active = _items.Where(i => i.State == "active")
                    .Select(i => (object)new { name = i.Name, size = i.Size, done = i.Done })
                    .Concat(_aggregateRecent.Reverse().Select(n => (object)new { name = n, size = 0L, done = 0L }))
                    .Take(6).ToList();

                snapshot = new
                {
                    id = _id,
                    title = _title,
                    phase = _phase,
                    state = _state,
                    message = _message,
                    paused = _resume != null,
                    pausable = _pausable,
                    files = new { done = filesDone, total = filesTotal },
                    bytes = new { done, total },
                    speed = Math.Round(speed),
                    eta = eta.HasValue ? Math.Round(eta.Value) : (double?)null,
                    elapsed = Math.Round(now),
                    active,
                    queued = _items.Count(i => i.State == "queued"),
                    recent = _items.Where(i => i.State == "done").OrderByDescending(i => i.FinishedAt)
                        .Take(5).Select(i => new { name = i.Name, size = i.Size }).ToList(),
                    failed = _items.Where(i => i.State == "failed").Select(i => new { name = i.Name, error = i.Error }).ToList()
                };
            }
            try { Emit?.Invoke(snapshot); } catch { /* arayüz kapanıyor */ }
        }
    }

    /// <summary>İşteki tek bir dosya. Indirilen bayt sayısını IProgress olarak alır.</summary>
    internal sealed class DownloadItem : IProgress<long>
    {
        private readonly DownloadJob _job;
        private long _done;

        public DownloadItem(DownloadJob job, string name, long size)
        {
            _job = job;
            Name = name;
            Size = Math.Max(0, size);
        }

        public string Name { get; }
        public long Size { get; private set; }
        public long Done => Interlocked.Read(ref _done);
        public string State { get; private set; } = "queued";
        public string Error { get; private set; } = "";
        public long FinishedAt { get; private set; }

        public void Start() { State = "active"; _job.Touch(); }

        /// <summary>Boyut indirme başlayınca öğrenilirse (Content-Length).</summary>
        public void SetSize(long size) { if (size > 0) { Size = size; _job.Touch(); } }

        public void Report(long bytes) { Interlocked.Add(ref _done, bytes); _job.Touch(); }

        public void Complete()
        {
            if (Size > 0) Interlocked.Exchange(ref _done, Size);
            else Size = Done;
            State = "done";
            FinishedAt = Stopwatch.GetTimestamp();
            _job.Touch();
        }

        public void Fail(string error)
        {
            State = "failed";
            Error = error;
            _job.Touch();
        }

        /// <summary>Başarısız denemenin baytları geri alınır (yeniden denemede çift sayılmasın).</summary>
        public void Reset() { Interlocked.Exchange(ref _done, 0); State = "active"; _job.Touch(); }

        /// <summary>İndirir; ilerleme ve sonuç bu öğeye yazılır.</summary>
        public async Task DownloadAsync(string url, string path, string? sha1 = null, string? sha512 = null, CancellationToken cancellation = default,
            string? bearer = null)
        {
            Start();
            try
            {
                await Downloader.DownloadAsync(url, path, this, sha1, sha512, cancellation, SetSize, bearer);
                Complete();
            }
            catch (Exception ex)
            {
                Fail(ex is OperationCanceledException ? "İptal edildi" : ex.Message);
                throw;
            }
        }
    }
}
