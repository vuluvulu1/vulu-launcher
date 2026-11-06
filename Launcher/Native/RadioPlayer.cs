using Avalonia.Threading;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace VuluLauncher.Native
{
    /// <summary>
    /// Radyo çalar: Windows'un kendi medya oynatıcısı (MP3, AAC, HLS yayınları ve yerel dosyalar; ek kütüphane gerekmez).
    /// Durdurunca kaynak bırakılır, yayın bağlantısı kapanır (boşuna veri inmesin). Olaylar arayüz iş parçacığına gelir.
    /// </summary>
    internal sealed class RadioPlayer : IDisposable
    {
        private readonly MediaPlayer _player = new() { AudioCategory = MediaPlayerAudioCategory.Media };

        public RadioPlayer()
        {
            // Klavyedeki medya tuşları ve Windows'un ses kartı launcher'ın değil: sistem medya kontrolleri kapalı
            _player.CommandManager.IsEnabled = false;
            _player.MediaFailed += (_, _) => Dispatcher.UIThread.Post(() => Failed?.Invoke());
            _player.MediaEnded += (_, _) => Dispatcher.UIThread.Post(() => Ended?.Invoke());
            _player.PlaybackSession.PlaybackStateChanged += (_, _) => Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
        }

        /// <summary>Yayın açılamadı (istasyon kapalı, biçim desteklenmiyor).</summary>
        public event Action? Failed;
        /// <summary>Şarkı bitti (vulu Radyo'da sıradaki çalar).</summary>
        public event Action? Ended;
        public event Action? StateChanged;

        public bool IsPlaying => _player.Source != null && _player.PlaybackSession.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Opening or MediaPlaybackState.Buffering;

        /// <summary>0–100.</summary>
        public double Volume
        {
            get => _player.Volume * 100;
            set => _player.Volume = Math.Clamp(value, 0, 100) / 100;
        }

        public bool Muted
        {
            get => _player.IsMuted;
            set => _player.IsMuted = value;
        }

        /// <summary>http(s) yayını ya da file:// yerel şarkı.</summary>
        public void Play(string url)
        {
            var old = _player.Source as MediaSource;
            _player.Source = MediaSource.CreateFromUri(new Uri(url));
            old?.Dispose();
            _player.Play();
        }

        public void Stop()
        {
            _player.Pause();
            var old = _player.Source as MediaSource;
            _player.Source = null;
            old?.Dispose();
            StateChanged?.Invoke();
        }

        public void Dispose()
        {
            Stop();
            _player.Dispose();
        }
    }
}
