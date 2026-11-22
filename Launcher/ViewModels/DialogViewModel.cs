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

        /// <summary>Kapanıyor: kutu ve karartma kısa bir çıkış animasyonuyla kaybolur, sonra listeden çıkar.</summary>
        [ObservableProperty] private bool _closing;

        /// <summary>En üstteki kutu: yalnızca onun köşesindeki kapat düğmesi görünür (alttakininki karartmanın altında kalsa da seçilmesin).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowCloseButton))]
        private bool _isTop = true;

        /// <summary>Kenardaki "× Esc" düğmesi: kendi kapatma ipucu olan kutular (hızlı arama) gizler.</summary>
        protected virtual bool HasCloseButton => true;
        public bool ShowCloseButton => IsTop && HasCloseButton;

        /// <summary>Vazgeç, Kapat ya da Esc.</summary>
        [RelayCommand] private void Dismiss() => Close(null);

        /// <summary>Seçim listesindeki satır tıklandı (paket, sunucu, instance seçimi); varsayılan: değeri sonuç olarak döner.</summary>
        [RelayCommand] private void PickRow(PickRow row) => Close(row.Value);
    }

    /// <summary>
    /// Günlüğü paylaş: "vulu'da" (bir üyeye mesajla, yardım istemek için) ya da "mclo.gs" (bağlantı panoya). Sonuç
    /// "vulu" / "mclogs"; ikisinde de günlük önce mclo.gs'ye yüklenir (kullanıcı adı ve oturum anahtarları gizlenir).
    /// </summary>
    internal sealed partial class LogShareModal : ModalViewModel
    {
        public override double Width => 520;
        [RelayCommand] private void Vulu() => Close("vulu");
        [RelayCommand] private void Mclogs() => Close("mclogs");
    }

    /// <summary>Sunucunun canlı haritası, Sunucular sayfasından (Sosyal › Harita ile aynı görünüm).</summary>
    internal sealed class MapModal(WorldViewModel world) : ModalViewModel
    {
        public WorldViewModel World { get; } = world;
        public override double Width => 1120;
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
