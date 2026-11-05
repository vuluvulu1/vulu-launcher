using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VuluLauncher.ViewModels
{
    /// <summary>
    /// Pencerenin ortasında açılan soru ya da bilgi kutusu (web arayüzündeki confirmDialog/alertDialog).
    /// Sonuç <see cref="Result"/> ile beklenir: tamam ise true, vazgeç ya da Esc ise false.
    /// </summary>
    internal sealed partial class DialogViewModel(string title, string message, string okText, string? cancelText, bool danger) : ObservableObject
    {
        private readonly TaskCompletionSource<bool> _result = new();

        public string Title { get; } = title;
        public string Message { get; } = message;
        public string OkText { get; } = okText;
        public string? CancelText { get; } = cancelText;
        public bool HasCancel => CancelText != null;
        public bool IsDanger { get; } = danger;

        public Task<bool> Result => _result.Task;

        [RelayCommand] private void Ok() => _result.TrySetResult(true);
        [RelayCommand] private void Cancel() => _result.TrySetResult(false);
    }
}
