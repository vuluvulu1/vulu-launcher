using System.Collections.Concurrent;

namespace VuluLauncher
{
    /// <summary>
    /// Aynı anda açık oyunlar: her paket için ayrı bir <see cref="GameLauncher"/>. Bir paket bir kez açılabilir.
    /// Hazırlık (Java, mod yükleyici, oyun dosyaları) sırayla yapılır: paketler oyun dosyalarını ortak klasörde
    /// paylaştığı için iki kurulum aynı dosyaya aynı anda yazmasın. Oyunlar başladıktan sonra birlikte çalışır.
    /// Telefonda oyunu platformun motoru açtığı için aynı anda tek oyun olur.
    /// </summary>
    internal sealed class GameSet
    {
        private readonly Action<string, string> _log;
        private readonly JavaManager _java;
        private readonly IGameRunner? _runner;
        private readonly ConcurrentDictionary<string, GameLauncher> _games = new();
        private readonly ConcurrentDictionary<string, string> _names = new();
        private readonly SemaphoreSlim _prepare = new(1, 1);

        public GameSet(Action<string, string> log, JavaManager java, IGameRunner? runner = null)
        {
            _log = log;
            _java = java;
            _runner = runner;
        }

        /// <summary>Açık (ya da açılmakta olan) oyunlardan biri.</summary>
        public bool IsRunning => !_games.IsEmpty;
        public int Count => _games.Count;
        public bool IsRunningFor(string instanceId) => _games.ContainsKey(instanceId);
        public IReadOnlyList<string> RunningIds => _games.Keys.ToList();

        public event Action<string, BootStatus>? BootProgress;
        public event Action<string, long>? Ready;
        /// <summary>Oyun kapandı: paket kimliği ve kapanış bilgileri (çökme yardımcısı için).</summary>
        public event Action<string, GameExit?>? Exited;

        /// <summary>Son açılıştaki bellek denetimi.</summary>
        public MemoryCheck.Result? LastMemory { get; private set; }

        public async Task LaunchAsync(string instanceId, LaunchTarget target, string username, int ramGb, SkinSession? skins,
            string? serverAddress, PhaseTimer? timer, MicrosoftSession? microsoft, CancellationToken cancel)
        {
            if (_runner != null && IsRunning) throw new InvalidOperationException("Oyun zaten çalışıyor.");
            var game = new GameLauncher(LogFor(instanceId), _java, _runner);
            if (!_games.TryAdd(instanceId, game)) throw new InvalidOperationException(L.F("{0} zaten açık.", target.Name));
            _names[instanceId] = target.Name;
            game.BootProgress += status => BootProgress?.Invoke(instanceId, status);
            game.Ready += ms => Ready?.Invoke(instanceId, ms);
            game.Exited += () =>
            {
                _games.TryRemove(instanceId, out _);
                Exited?.Invoke(instanceId, game.LastExit);
            };

            bool entered = false;
            try
            {
                if (!_prepare.Wait(0))
                {
                    _log("Başka bir paket hazırlanıyor; o bitince bu paket açılacak.", "muted");
                    await _prepare.WaitAsync(cancel);
                }
                entered = true;
                await game.LaunchAsync(target, username, ramGb, skins, serverAddress, timer, microsoft);
                LastMemory = game.LastMemory;
                // Telefonda oyun ayrı süreçte çalışır ve kapanışı izlenmez: listede tutulmaz
                if (!game.IsRunning) _games.TryRemove(instanceId, out _);
            }
            catch
            {
                if (!game.IsRunning) _games.TryRemove(instanceId, out _);
                throw;
            }
            finally
            {
                if (entered) _prepare.Release();
            }
        }

        /// <summary>Paketin oyununu (ya da açılışını) durdurur; kimlik boşsa hepsini.</summary>
        public void Stop(string? instanceId = null)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                foreach (var game in _games.Values) game.Stop();
                return;
            }
            if (_games.TryGetValue(instanceId, out var one)) one.Stop();
        }

        public Task VerifyGameAsync(LaunchTarget target) => new GameLauncher(_log, _java, _runner).VerifyGameAsync(target);

        /// <summary>Birden çok oyun açıkken oyun çıktısının başına paketin adı yazılır (konsolda karışmasın).</summary>
        private Action<string, string> LogFor(string instanceId) => (text, level) =>
        {
            if (Count > 1 && _names.TryGetValue(instanceId, out var name) && level is "game" or "gameError" or "warn")
                text = $"[{name}] {text}";
            _log(text, level);
        };
    }
}
