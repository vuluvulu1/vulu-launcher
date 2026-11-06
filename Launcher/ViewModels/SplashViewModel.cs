using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VuluLauncher.ViewModels
{
    /// <summary>
    /// Açılış sahnesi: perde kapalıyken "vulu Launcher" yazısı çizilir, giriş ve ana ekran arkada hazırlanır;
    /// yazı bitince perdeler açılır. Animasyonu <see cref="Views.SplashView"/> oynatır.
    /// </summary>
    internal sealed partial class SplashViewModel : ObservableObject
    {
        /// <summary>Perdeler en erken bu kadar süre sonra açılır (ms): yazı çizilip karşılama görünmüş olur.</summary>
        public const int OpenAtMs = 2400;

        private readonly Stopwatch _clock = new();

        [ObservableProperty] private bool _isVisible;
        [ObservableProperty] private string _greeting = "";
        [ObservableProperty] private string _status = "";

        /// <summary>Sahne baştan oynamalı (görünüm dinler).</summary>
        public event Action? Played;

        /// <summary>Perdeleri açan animasyon (görünüm verir); bitince tamamlanır.</summary>
        public Func<Task>? Opener { get; set; }

        public long ElapsedMs => _clock.ElapsedMilliseconds;

        public void Begin(string username)
        {
            Greeting = $"Hoş geldin, {username}";
            Status = "";
            _clock.Restart();
            IsVisible = true;
            Played?.Invoke();
        }

        /// <summary>Yazı tamamlanınca perdeleri açar ve sahneyi kaldırır; arkadaki ekran hazır olmalı.</summary>
        public async Task OpenAsync()
        {
            if (!IsVisible) return;
            long wait = OpenAtMs - _clock.ElapsedMilliseconds;
            if (wait > 0) await Task.Delay((int)wait);
            Status = "";
            if (Opener != null) await Opener();
            IsVisible = false;
        }
    }
}
