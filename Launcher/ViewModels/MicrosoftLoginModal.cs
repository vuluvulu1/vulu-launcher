using System.Text.Json;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>
    /// Microsoft hesabını bağlama: launcher bir kod alır ve tarayıcıda microsoft.com/link sayfasını açar; kişi kodu
    /// yazıp giriş yapınca Core "msLogin" olayını gönderir ve kutu kendiliğinden kapanır (sonuç: Minecraft adı).
    /// </summary>
    internal sealed partial class MicrosoftLoginModal : ModalViewModel
    {
        private readonly CoreClient _core = CoreClient.Instance;
        private string _uri = "https://www.microsoft.com/link";

        [ObservableProperty] private string _code = "";
        [ObservableProperty] private string _status = "Kod alınıyor…";
        [ObservableProperty] private bool _isError;
        [ObservableProperty] private bool _hasCode;

        public override double Width => 420;

        public MicrosoftLoginModal()
        {
            _core.Event += OnEvent;
            _ = BeginAsync();
        }

        private async Task BeginAsync()
        {
            try
            {
                var result = await _core.Call("ms.begin");
                Code = result.Text("userCode") ?? "";
                _uri = result.Text("verificationUri") ?? _uri;
                HasCode = Code.Length > 0;
                Status = "Tarayıcıda açılan sayfaya bu kodu yaz ve Minecraft'ı aldığın Microsoft hesabıyla giriş yap. Bu pencere kendiliğinden kapanır.";
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private void OnEvent(string name, JsonElement data)
        {
            if (name != "msLogin") return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                switch (data.Text("state"))
                {
                    case "done": Finish(data.Text("name")); break;
                    case "error": Fail(data.Text("error") ?? "Giriş başarısız."); break;
                }
            });
        }

        private void Fail(string message)
        {
            Status = message;
            IsError = true;
        }

        private void Finish(string? name)
        {
            _core.Event -= OnEvent;
            Close(name ?? "");
        }

        [RelayCommand]
        private async Task CopyCode()
        {
            if (Code.Length == 0) return;
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { Clipboard: { } clipboard } })
                await clipboard.SetTextAsync(Code);
            Status = "Kod kopyalandı. Tarayıcıdaki sayfaya yapıştır.";
            IsError = false;
        }

        [RelayCommand]
        private void OpenPage() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_uri) { UseShellExecute = true }));

        [RelayCommand]
        private async Task Cancel()
        {
            _core.Event -= OnEvent;
            try { await _core.Call("ms.cancel"); } catch { }
            Close(null);
        }
    }
}
