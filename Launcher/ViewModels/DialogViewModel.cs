using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VuluLauncher.ViewModels
{
    /// <summary>
    /// Pencerenin ortasında açılan kutu (web arayüzündeki openModal). Kutular üst üste açılabilir; en üstteki Esc ile
    /// kapanır. Sonuç <see cref="Result"/> ile beklenir: kapatan neyi verdiyse o, vazgeçilirse null.
    /// </summary>
    internal abstract partial class ModalViewModel : ObservableObject
    {
        private readonly TaskCompletionSource<object?> _result = new();

        public Task<object?> Result => _result.Task;

        /// <summary>Kutunun genişliği (DIP).</summary>
        public virtual double Width => 440;

        public void Close(object? result = null) => _result.TrySetResult(result);

        /// <summary>Vazgeç, Kapat ya da Esc.</summary>
        [RelayCommand] private void Dismiss() => Close(null);
    }

    /// <summary>Soru ya da bilgi kutusu: tamam ise true, vazgeç ise null.</summary>
    internal sealed partial class DialogViewModel(string title, string message, string okText, string? cancelText, bool danger) : ModalViewModel
    {
        public string Title { get; } = title;
        public string Message { get; } = message;
        public string OkText { get; } = okText;
        public string? CancelText { get; } = cancelText;
        public bool HasCancel => CancelText != null;
        public bool IsDanger { get; } = danger;

        [RelayCommand] private void Ok() => Close(true);
    }
}
