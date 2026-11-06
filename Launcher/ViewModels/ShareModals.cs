using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>Rol seçimi ("Kimler erişebilir"): hiçbiri seçilmezse herkes.</summary>
    internal sealed partial class RolePick(string id, string name, string color) : ObservableObject
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public IBrush Brush { get; } = Color.TryParse(color, out var c) ? new SolidColorBrush(c) : Brushes.Gray;
        [ObservableProperty] private bool _isSelected;
    }

    /// <summary>Rol seçici: Paylaş ve Erişim pencerelerinde ortak.</summary>
    internal sealed partial class RoleChooser : ObservableObject
    {
        public RoleChooser(JsonElement roles, IEnumerable<string> chosen)
        {
            var set = chosen.ToHashSet();
            foreach (var r in roles.Items())
                Roles.Add(new RolePick(r.Text("id") ?? "", r.Text("name") ?? "", r.Text("color") ?? "#c9a992") { IsSelected = set.Contains(r.Text("id") ?? "") });
            Paint();
        }

        public ObservableCollection<RolePick> Roles { get; } = new();
        public bool HasRoles => Roles.Count > 0;
        [ObservableProperty] private bool _everyone;
        [ObservableProperty] private string _hint = "";
        public List<string> Chosen => Roles.Where(r => r.IsSelected).Select(r => r.Id).ToList();

        [RelayCommand] private void Toggle(RolePick role) { role.IsSelected = !role.IsSelected; Paint(); }
        [RelayCommand] private void All() { foreach (var r in Roles) r.IsSelected = false; Paint(); }

        private void Paint()
        {
            var names = Roles.Where(r => r.IsSelected).Select(r => r.Name).ToList();
            Everyone = names.Count == 0;
            Hint = names.Count > 0 ? $"Yalnızca {string.Join(", ", names)} rolündekiler ve adminler görür." : "Bütün vulu kullanıcıları görür.";
        }
    }

    /// <summary>Paketi kimlerin görebileceği (admin). Yeni revizyon açılmaz; kuranların paketi değişmez. Sonuç true ise kaydedildi.</summary>
    internal sealed partial class PackAccessModal(string packId, string packName, JsonElement roles, IEnumerable<string> chosen) : ModalViewModel
    {
        public string Title { get; } = $"{packName} · Kimler erişebilir";
        public RoleChooser Chooser { get; } = new(roles, chosen);
        [ObservableProperty] private string? _error;

        [RelayCommand]
        private async Task Save()
        {
            Error = null;
            try
            {
                await CoreClient.Instance.Call("packs.setRoles", new { id = packId, roles = Chooser.Chosen });
                Close(true);
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    internal sealed partial class ShareEntry(string path, string? label, string? note, string size, bool isChecked) : ObservableObject
    {
        public string Path { get; } = path;
        public string? Label { get; } = label;
        public bool HasLabel => Label != null;
        public string? Note { get; } = note;
        public bool HasNote => !string.IsNullOrEmpty(Note);
        public string Size { get; } = size;
        [ObservableProperty] private bool _checked = isChecked;
    }

    /// <summary>
    /// vulu Packs'te paylaş / paylaşımı güncelle (admin): ad, açıklama, kapak, simge, önerilen RAM, erişim ve pakete
    /// girecek klasörler. Paylaşmadan önceki denetimin uyarıları da gösterilir. Sonuç true ise yayınlandı.
    /// </summary>
    internal sealed partial class ShareModal : ModalViewModel
    {
        private static readonly Dictionary<string, string> Labels = new()
        {
            ["config"] = "Mod ayarları", ["defaultconfigs"] = "Varsayılan mod ayarları", ["kubejs"] = "KubeJS betikleri", ["mods"] = "Modlar",
            ["resourcepacks"] = "Kaynak paketleri", ["shaderpacks"] = "Shaderlar", ["datapacks"] = "Veri paketleri", ["global_packs"] = "Genel veri ve kaynak paketleri",
            ["openloader"] = "OpenLoader paketleri", ["fancymenu_data"] = "FancyMenu ekranları", ["optionsshaders.txt"] = "Seçili shader (OptiFine)",
            ["options.txt"] = "Oyun ayarları", ["servers.dat"] = "Sunucu listesi", ["schematics"] = "Şemalar", ["journeymap"] = "JourneyMap ayarları", ["xaero"] = "Xaero harita ayarları",
        };

        private readonly MainViewModel _main;
        private readonly InstanceItem _instance;
        private string _coverPath = "";

        public ShareModal(MainViewModel main, InstanceItem instance, JsonElement info)
        {
            _main = main;
            _instance = instance;
            var existing = info.Get("existing");
            IsUpdate = existing.ValueKind == JsonValueKind.Object;
            Title = IsUpdate ? $"{info.Text("name")} paketini güncelle" : "vulu Packs'te paylaş";
            SubmitText = IsUpdate ? $"Güncelle (rev {existing.Int("revision") + 1})" : "Yayınla";
            CoverText = IsUpdate && existing.Bool("hasCover") ? "Mevcut kapak kalacak · değiştirmek için tıkla" : "Kapak resmi seç";
            _name = info.Text("name") ?? instance.Name;
            _description = info.Text("description") ?? "";
            _backgroundAsIcon = info.Bool("backgroundAsIcon");
            foreach (var e in info.Items("entries"))
            {
                string path = e.Text("path") ?? "";
                Entries.Add(new ShareEntry(path + (e.Bool("isDir") ? "/" : ""), Labels.GetValueOrDefault(path.ToLowerInvariant()), e.Text("note"),
                    $"{e.Int("files")} dosya · {HomeViewModel.FormatSize(e.Num("sizeMb"))}", e.Bool("checkedByDefault")));
            }
            var ram = info.Items("ramOptionsGb").Select(g => g.GetInt32()).ToList();
            if (ram.Count == 0) ram = main.Home.RamOptions.ToList();
            int recommended = ram.Contains(info.Int("recommendedRamGb")) ? info.Int("recommendedRamGb") : ram.ElementAtOrDefault(1);
            foreach (var gb in ram) RamOptions.Add(new Choice(gb.ToString(CultureInfo.InvariantCulture), gb + " GB") { IsSelected = gb == recommended });
            HasRoles = info.Get("roles").ValueKind == JsonValueKind.Array;
            Chooser = new RoleChooser(info.Get("roles"), existing.Items("roles").Select(r => r.GetString() ?? ""));
            var check = info.Get("check");
            HasCheck = check.ValueKind == JsonValueKind.Array;
            foreach (var i in check.Items()) Issues.Add(new HealthItem(i.Text("title") ?? "", i.Text("detail")));
            _ = LoadIconAsync();
        }

        public override double Width => 900;
        public string Title { get; }
        public string SubmitText { get; }
        public bool IsUpdate { get; }
        public bool HasRoles { get; }
        public RoleChooser Chooser { get; }
        public ObservableCollection<ShareEntry> Entries { get; } = new();
        public ObservableCollection<Choice> RamOptions { get; } = new();
        public ObservableCollection<HealthItem> Issues { get; } = new();
        public bool HasCheck { get; }
        public bool CheckOk => HasCheck && Issues.Count == 0;
        public bool HasIssues => Issues.Count > 0;
        public string IssuesTitle => $"Paylaşmadan önce bak ({Issues.Count})";
        public bool NoEntries => Entries.Count == 0;
        [ObservableProperty] private string _coverText;
        [ObservableProperty] private Bitmap? _cover;
        [ObservableProperty] private Bitmap? _icon;
        [ObservableProperty] private string _name;
        [ObservableProperty] private string _description;
        [ObservableProperty] private string _changeNote = "";
        [ObservableProperty] private bool _backgroundAsIcon;
        [ObservableProperty] private string? _error;
        [ObservableProperty] private bool _busy;

        private async Task LoadIconAsync()
        {
            var fresh = _main.Home.Instances.FirstOrDefault(i => i.Id == _instance.Id) ?? _instance;
            Icon = await Images.LoadAsync(fresh.CustomIconUrl, 96);
        }

        [RelayCommand] private void PickRam(Choice ram) { foreach (var r in RamOptions) r.IsSelected = r == ram; }

        [RelayCommand]
        private async Task PickCover()
        {
            try
            {
                var picked = await CoreClient.Instance.Call("packs.pickCover");
                if (picked.Text("path") is not { Length: > 0 } path) return;
                _coverPath = path;
                Cover = await Images.LoadAsync(picked.Text("url"), 640);
                CoverText = "";
            }
            catch (Exception ex) { Error = ex.Message; }
        }

        /// <summary>Simge hemen instance'a kaydedilir (Görünüm penceresindekiyle aynı dosya), yayınlanınca pakete gider.</summary>
        [RelayCommand]
        private async Task PickIcon()
        {
            try
            {
                var result = await CoreClient.Instance.Call("instances.pickIcon", new { id = _instance.Id });
                if (!result.Bool("changed")) return;
                Images.Forget(_instance.CustomIconUrl);
                await _main.Home.ReloadInstancesAsync(_instance.Id);
                await LoadIconAsync();
            }
            catch (Exception ex) { Error = ex.Message; }
        }

        [RelayCommand]
        private async Task Publish()
        {
            Error = null;
            Busy = true;
            try
            {
                var args = new Dictionary<string, object?>
                {
                    ["instanceId"] = _instance.Id, ["name"] = Name, ["description"] = Description, ["changeNote"] = ChangeNote,
                    ["includes"] = Entries.Where(e => e.Checked).Select(e => e.Path.TrimEnd('/')).ToList(),
                    ["ramGb"] = int.Parse(RamOptions.FirstOrDefault(r => r.IsSelected)?.Key ?? "4", CultureInfo.InvariantCulture),
                    ["coverPath"] = _coverPath, ["backgroundAsIcon"] = BackgroundAsIcon, ["update"] = IsUpdate,
                };
                // Rol listesi gelmediyse gönderilmez: sunucuda paketin erişimi olduğu gibi kalır
                if (HasRoles) args["roles"] = Chooser.Chosen;
                await CoreClient.Instance.Call("packs.publish", args);
                Close(true);
            }
            catch (Exception ex) { Error = ex.Message; Busy = false; }
        }
    }
}
