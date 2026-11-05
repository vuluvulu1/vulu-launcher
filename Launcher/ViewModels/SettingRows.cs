using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VuluLauncher.ViewModels
{
    /// <summary>Ayarlar sayfasındaki bölüm (Oyun, Launcher…): başlık ve satırlar.</summary>
    internal sealed partial class SettingSection(string title) : ObservableObject
    {
        public string Title { get; } = title;
        public ObservableCollection<SettingRow> Rows { get; } = new();
        /// <summary>Aramada hiç satırı kalmayınca gizlenir.</summary>
        [ObservableProperty] private bool _isVisible = true;
        /// <summary>Kaydırırken üstte görünen bölüm menüde seçili olur.</summary>
        [ObservableProperty] private bool _isCurrent;

        public SettingSection Add(SettingRow row) { Rows.Add(row); return this; }
    }

    /// <summary>Ayar satırı: solda başlık ve açıklama, sağda denetim.</summary>
    internal abstract partial class SettingRow(string title, string? hint) : ObservableObject
    {
        public string Title { get; } = title;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasHint))]
        private string? _hint = hint;
        public bool HasHint => !string.IsNullOrEmpty(Hint);

        /// <summary>Koşula bağlı satır (ör. Java yolu yalnızca "Elle seç"te).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsShown))]
        private bool _isAvailable = true;
        /// <summary>Aramaya uymuyorsa false.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsShown))]
        private bool _matchesSearch = true;
        public bool IsShown => IsAvailable && MatchesSearch;

        /// <summary>Aramada bakılan metin.</summary>
        public virtual string SearchText => Title + " " + Hint;
    }

    /// <summary>Birkaç seçenekten biri (yan yana düğmeler).</summary>
    internal sealed partial class ChoiceRow : SettingRow
    {
        private readonly Func<string, Task<bool>> _pick;

        public ChoiceRow(string title, string? hint, IEnumerable<(string Key, string Label)> options, string selected, Func<string, Task<bool>> pick)
            : base(title, hint)
        {
            _pick = pick;
            foreach (var (key, label) in options) Options.Add(new ChoiceOption(this, key, label));
            Selected = selected;
        }

        public ObservableCollection<ChoiceOption> Options { get; } = new();
        [ObservableProperty] private string _selected = "";

        partial void OnSelectedChanged(string value)
        {
            foreach (var option in Options) option.IsSelected = option.Key == value;
        }

        public async Task PickAsync(string key)
        {
            if (key == Selected) return;
            string previous = Selected;
            Selected = key;
            if (!await _pick(key)) Selected = previous;
        }

        public override string SearchText => base.SearchText + " " + string.Join(" ", Options.Select(o => o.Label));
    }

    internal sealed partial class ChoiceOption(ChoiceRow row, string key, string label) : ObservableObject
    {
        public string Key { get; } = key;
        public string Label { get; } = label;
        [ObservableProperty] private bool _isSelected;

        [RelayCommand] private Task Pick() => row.PickAsync(Key);
    }

    /// <summary>Aç/kapa.</summary>
    internal sealed partial class ToggleRow : SettingRow
    {
        private readonly Func<bool, Task<bool>> _change;
        private bool _reverting;

        public ToggleRow(string title, string? hint, bool value, Func<bool, Task<bool>> change) : base(title, hint)
        {
            _value = value;
            _change = change;
        }

        [ObservableProperty] private bool _value;

        partial void OnValueChanged(bool value)
        {
            if (_reverting) return;
            _ = Apply();
            async Task Apply()
            {
                if (await _change(value)) return;
                _reverting = true;
                Value = !value;
                _reverting = false;
            }
        }
    }

    /// <summary>Metin kutusu; Enter'a basılınca ya da kutudan çıkılınca kaydedilir. İsteğe bağlı yanında bir düğme.</summary>
    internal sealed partial class TextRow(string title, string? hint, string value, string placeholder, Func<string, Task<bool>> commit) : SettingRow(title, hint)
    {
        private string _committed = value;

        [ObservableProperty] private string _value = value;
        public string Placeholder { get; } = placeholder;
        public RowAction? Action { get; init; }
        public bool HasAction => Action != null;

        [RelayCommand]
        private async Task Commit()
        {
            if (Value == _committed) return;
            if (await commit(Value)) _committed = Value;
        }

        /// <summary>Değer dışarıdan değişti (ör. "Gözat" ile dosya seçildi): kayıtlı sayılır.</summary>
        public void SetCommitted(string value)
        {
            _committed = value;
            Value = value;
        }
    }

    /// <summary>Kaydırıcı (0–100); bırakınca kaydedilir.</summary>
    internal sealed partial class SliderRow(string title, string? hint, int value, Func<int, Task<bool>> commit) : SettingRow(title, hint)
    {
        [ObservableProperty] private double _value = value;

        public Task CommitAsync() => commit((int)Math.Round(Value));
    }

    /// <summary>Bilgi ve düğmeler (Çıkış yap, Aç, Sil…).</summary>
    internal sealed partial class ActionRow(string title, string? hint) : SettingRow(title, hint)
    {
        /// <summary>Sağda gösterilen değer (ör. "1,2 GB").</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasValue))]
        private string? _value;
        public bool HasValue => !string.IsNullOrEmpty(Value);
        public ObservableCollection<RowAction> Actions { get; } = new();

        public ActionRow With(RowAction action) { Actions.Add(action); return this; }
    }

    internal sealed partial class RowAction(string label, Func<Task> run, bool danger = false) : ObservableObject
    {
        public string Label { get; } = label;
        public bool IsDanger { get; } = danger;
        [ObservableProperty] private bool _isEnabled = true;

        [RelayCommand] private Task Run() => run();
    }

    /// <summary>Oyun penceresi boyutu: hazır boyutlar ya da elle genişlik × yükseklik.</summary>
    internal sealed partial class GameSizeRow : SettingRow
    {
        private static readonly (string Key, string Label)[] Presets =
            { ("0x0", "Varsayılan"), ("1280x720", "1280×720"), ("1600x900", "1600×900"), ("1920x1080", "1920×1080"), ("custom", "Özel") };
        private readonly Func<int, int, Task<bool>> _save;

        public GameSizeRow(string title, string? hint, int width, int height, Func<int, int, Task<bool>> save) : base(title, hint)
        {
            _save = save;
            _width = width > 0 ? width.ToString() : "";
            _height = height > 0 ? height.ToString() : "";
            string key = width == 0 && height == 0 ? "0x0" : Presets.Any(p => p.Key == $"{width}x{height}") ? $"{width}x{height}" : "custom";
            Choice = new ChoiceRow("", null, Presets, key, PickAsync);
            _isCustom = key == "custom";
        }

        public ChoiceRow Choice { get; }
        [ObservableProperty] private string _width;
        [ObservableProperty] private string _height;
        [ObservableProperty] private bool _isCustom;

        private Task<bool> PickAsync(string key)
        {
            IsCustom = key == "custom";
            if (IsCustom) return Task.FromResult(true);
            var parts = key.Split('x');
            Width = parts[0] == "0" ? "" : parts[0];
            Height = parts[1] == "0" ? "" : parts[1];
            return _save(int.Parse(parts[0]), int.Parse(parts[1]));
        }

        [RelayCommand]
        private Task SaveCustom() =>
            _save(int.TryParse(Width, out int w) ? w : 0, int.TryParse(Height, out int h) ? h : 0);
    }

    /// <summary>Bilgisayarda bulunan Java'lar; biri seçilince elle seçilmiş Java olur.</summary>
    internal sealed partial class JavaListRow(string title) : SettingRow(title, null)
    {
        public ObservableCollection<JavaItem> Items { get; } = new();
        public bool IsEmpty => Items.Count == 0;
    }

    internal sealed partial class JavaItem(string version, string source, string path, bool selected, Func<JavaItem, Task> use) : ObservableObject
    {
        public string Version { get; } = version;
        public string Source { get; } = source;
        public string Path { get; } = path;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ButtonText))]
        private bool _isSelected = selected;
        public string ButtonText => IsSelected ? "Seçili ✓" : "Bunu kullan";

        [RelayCommand] private Task Use() => use(this);
    }
}
