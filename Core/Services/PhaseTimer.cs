using System.Diagnostics;

namespace VuluLauncher
{
    /// <summary>
    /// Oyun açılışının adımlarını ölçer: her adımın süresi günlük dosyasına yazılır (yavaş açılışları teşhis için).
    /// </summary>
    internal sealed class PhaseTimer
    {
        private readonly Stopwatch _total = Stopwatch.StartNew();
        private readonly List<(string Name, long Ms)> _phases = new();
        private long _last;
        private readonly string _title;

        public PhaseTimer(string title) => _title = title;

        /// <summary>O ana kadarki adımı adıyla kaydeder.</summary>
        public void Mark(string name)
        {
            long now = _total.ElapsedMilliseconds;
            lock (_phases) _phases.Add((name, now - _last));
            _last = now;
        }

        public long TotalMs => _total.ElapsedMilliseconds;

        public string Summary()
        {
            lock (_phases)
                return $"{_title}: {TotalMs} ms (" + string.Join(", ", _phases.Select(p => $"{p.Name} {p.Ms}")) + ")";
        }

        public void Write() => LogFile.Write("[açılış] " + Summary());
    }
}
