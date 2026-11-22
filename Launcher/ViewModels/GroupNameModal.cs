using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VuluLauncher.ViewModels
{
    /// <summary>Kütüphane grubunun adı (yeni grup ya da yeniden adlandırma). Sonuç: kırpılmış ad.</summary>
    internal sealed partial class GroupNameModal : ModalViewModel
    {
        private readonly HashSet<string> _taken;

        public GroupNameModal(string title, string name, IEnumerable<string> taken)
        {
            Title = title;
            _name = name;
            _taken = taken.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            SaveText = name.Length > 0 ? "Kaydet" : "Oluştur";
        }

        public string Title { get; }
        public string SaveText { get; }
        public override double Width => 400;
        [ObservableProperty] private string _name;
        [ObservableProperty] private string? _error;

        [RelayCommand]
        private void Save()
        {
            string name = Name.Trim();
            if (name.Length == 0) { Error = "Bir ad yaz."; return; }
            if (_taken.Contains(name)) { Error = "Bu adla bir grup zaten var."; return; }
            Close(name);
        }
    }
}
