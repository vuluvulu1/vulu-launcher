using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>Admin › Pelerinler listesindeki bir pelerin.</summary>
    internal sealed partial class CapeRow(AdminViewModel owner, JsonElement data, Bitmap? preview) : ObservableObject
    {
        public JsonElement Data { get; } = data;
        public string Key => Data.Text("key") ?? "";
        public string Name => Data.Text("name") ?? "";
        public string Earn => Data.Text("earn") ?? "";
        public bool AdminOnly => Data.Bool("adminOnly");
        public Bitmap? Preview { get; } = preview;
        public string Meta
        {
            get
            {
                string source = Data.Bool("custom") ? "PNG" : Data.Get("art").ValueKind == JsonValueKind.String ? "Hazır çizim" : "Desen";
                string owners = AdminOnly ? "adminlere özel" : $"{Data.Int("owners")} kişide";
                return $"{source} · {owners}";
            }
        }
        [ObservableProperty] private bool _isSelected;
        [RelayCommand] private void Edit() => owner.EditCape(this);
    }

    /// <summary>Pelerinin sahibi (Admin › Pelerinler › Sahipler).</summary>
    internal sealed partial class CapeOwner(AdminViewModel owner, string username, string meta, bool worn) : ObservableObject
    {
        public string Username { get; } = username;
        public string Meta { get; } = meta;
        public bool Worn { get; } = worn;
        [RelayCommand] private Task Revoke() => owner.RevokeCapeOwnerAsync(this);
    }

    /// <summary>Admin paneli › Pelerinler: katalog, ekleme/düzenleme/silme, canlı önizleme ve sahipler.</summary>
    internal sealed partial class AdminViewModel
    {
        public ObservableCollection<CapeRow> CapeRows { get; } = new();
        public ObservableCollection<CapeOwner> CapeOwners { get; } = new();
        public ObservableCollection<Option> Emblems { get; } = new();
        public ObservableCollection<Option> Arts { get; } = new();

        public bool IsCapes => Tab == "capes";

        // Düzenleyici
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EditorTitle))]
        private bool _capeEditing;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EditorTitle), nameof(CanDeleteCape))]
        private bool _capeIsNew;
        [ObservableProperty] private string _capeKey = "";
        [ObservableProperty] private string _capeName = "";
        [ObservableProperty] private string _capeEarn = "";
        [ObservableProperty] private decimal? _capeOrder = 1;
        [ObservableProperty] private bool _capeAdminOnly;
        /// <summary>Doku kaynağı: "design" (desen), "art" (hazır çizim), "png" (yüklenen) ya da "keep" (değiştirme).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDesign), nameof(IsArt), nameof(IsPng))]
        private string _capeSource = "design";
        [ObservableProperty] private string _colorBase = "#2a1650";
        [ObservableProperty] private string _colorEdge = "#f2a33a";
        [ObservableProperty] private string _colorInk = "#ffc873";
        [ObservableProperty] private string _colorInk2 = "#ffffff";
        [ObservableProperty] private Option? _emblem;
        [ObservableProperty] private Option? _art;
        [ObservableProperty] private string? _pngName;
        private string? _pngData;
        [ObservableProperty] private Bitmap? _capeFace;
        [ObservableProperty] private byte[]? _capePreviewBytes;
        [ObservableProperty] private byte[]? _mySkinBytes;
        [ObservableProperty] private bool _mySkinSlim;
        [ObservableProperty] private string? _capeNote;
        [ObservableProperty] private bool _capeOk;
        [ObservableProperty] private string _grantUsername = "";
        [ObservableProperty] private string _ownersTitle = "";

        public bool IsDesign => CapeSource == "design";
        public bool IsArt => CapeSource == "art";
        public bool IsPng => CapeSource == "png";
        public string EditorTitle => CapeIsNew ? "Yeni pelerin" : $"{CapeName} düzenleniyor";
        public bool CanDeleteCape => !CapeIsNew;
        public bool ShowOwners => !CapeIsNew && !CapeAdminOnly;
        partial void OnCapeAdminOnlyChanged(bool value) => OnPropertyChanged(nameof(ShowOwners));
        partial void OnCapeIsNewChanged(bool value) => OnPropertyChanged(nameof(ShowOwners));

        public IBrush BaseBrush => Brush(ColorBase);
        public IBrush EdgeBrush => Brush(ColorEdge);
        public IBrush InkBrush => Brush(ColorInk);
        public IBrush Ink2Brush => Brush(ColorInk2);
        private static IBrush Brush(string hex) => Color.TryParse(hex, out var c) ? new SolidColorBrush(c) : Brushes.Transparent;

        // Desen değişince önizleme biraz bekleyip yenilenir (her tuşta istek gitmesin)
        partial void OnColorBaseChanged(string value) { OnPropertyChanged(nameof(BaseBrush)); SchedulePreview(); }
        partial void OnColorEdgeChanged(string value) { OnPropertyChanged(nameof(EdgeBrush)); SchedulePreview(); }
        partial void OnColorInkChanged(string value) { OnPropertyChanged(nameof(InkBrush)); SchedulePreview(); }
        partial void OnColorInk2Changed(string value) { OnPropertyChanged(nameof(Ink2Brush)); SchedulePreview(); }
        partial void OnEmblemChanged(Option? value) => SchedulePreview();
        partial void OnArtChanged(Option? value) => SchedulePreview();
        partial void OnCapeSourceChanged(string value) => SchedulePreview();

        private DispatcherTimer? _previewTimer;
        private bool _loadingEditor;
        private void SchedulePreview()
        {
            if (_loadingEditor || !CapeEditing) return;
            _previewTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, async (_, _) => { _previewTimer!.Stop(); await RefreshCapePreviewAsync(); });
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private async Task LoadCapesAsync()
        {
            var r = await _core.Call("admin.capes");
            if (Emblems.Count == 0) foreach (var e in r.Items("emblems")) Emblems.Add(new Option(e.Text("key") ?? "", e.Text("name") ?? ""));
            if (Arts.Count == 0) foreach (var a in r.Items("arts")) Arts.Add(new Option(a.Text("key") ?? "", a.Text("name") ?? ""));
            await FillCapeRowsAsync(r);
            if (MySkinBytes == null) _ = LoadMySkinAsync();
        }

        private async Task FillCapeRowsAsync(JsonElement r)
        {
            string? selected = CapeRows.FirstOrDefault(c => c.IsSelected)?.Key;
            var rows = new List<CapeRow>();
            foreach (var c in r.Items("capes"))
            {
                var png = await Images.LoadBytesAsync(c.Text("texture"));
                rows.Add(new CapeRow(this, c.Clone(), png == null ? null : CapePreview.Render(png, 4)) { IsSelected = c.Text("key") == selected });
            }
            CapeRows.Clear();
            foreach (var row in rows) CapeRows.Add(row);
        }

        /// <summary>Önizlemedeki 3B karakter adminin kendi skinini giyer.</summary>
        private async Task LoadMySkinAsync()
        {
            try
            {
                var me = await _core.Call("profile.me");
                MySkinSlim = me.Text("skinModel") == "slim";
                if (me.Text("skinHash") is { Length: > 0 } hash) MySkinBytes = await Images.LoadBytesAsync($"{Avatars.ApiBase}/yggdrasil/textures/{hash}?c=1");
            }
            catch { /* varsayılan karakter */ }
        }

        [RelayCommand]
        private void NewCape()
        {
            foreach (var c in CapeRows) c.IsSelected = false;
            _loadingEditor = true;
            CapeIsNew = true;
            CapeKey = ""; CapeName = ""; CapeEarn = ""; CapeAdminOnly = false;
            CapeOrder = CapeRows.Count + 1;
            CapeSource = "design";
            ColorBase = "#2a1650"; ColorEdge = "#f2a33a"; ColorInk = "#ffc873"; ColorInk2 = "#ffffff";
            Emblem = Emblems.FirstOrDefault(e => e.Key == "vl") ?? Emblems.FirstOrDefault();
            Art = Arts.FirstOrDefault();
            PngName = null; _pngData = null;
            CapeOwners.Clear();
            CapeNote = null;
            CapeEditing = true;
            _loadingEditor = false;
            _ = RefreshCapePreviewAsync();
        }

        public void EditCape(CapeRow row)
        {
            foreach (var c in CapeRows) c.IsSelected = c == row;
            var d = row.Data;
            _loadingEditor = true;
            CapeIsNew = false;
            CapeKey = row.Key; CapeName = row.Name; CapeEarn = row.Earn; CapeAdminOnly = row.AdminOnly;
            CapeOrder = (decimal)d.Num("order");
            var design = d.Get("design");
            if (design.ValueKind == JsonValueKind.Object)
            {
                CapeSource = "design";
                ColorBase = design.Text("base") ?? "#000000"; ColorEdge = design.Text("edge") ?? "#ffffff";
                ColorInk = design.Text("ink") ?? "#ffffff"; ColorInk2 = design.Text("ink2") ?? "#ffffff";
                Emblem = Emblems.FirstOrDefault(e => e.Key == design.Text("emblem")) ?? Emblems.FirstOrDefault();
            }
            else if (d.Text("art") is { } art) { CapeSource = "art"; Art = Arts.FirstOrDefault(a => a.Key == art) ?? Arts.FirstOrDefault(); }
            else CapeSource = "keep";   // yüklenmiş PNG: yenisi seçilmedikçe aynı kalır
            PngName = d.Bool("custom") ? "Yüklenmiş PNG" : null; _pngData = null;
            CapeNote = null;
            CapeEditing = true;
            _loadingEditor = false;
            _ = RefreshCapePreviewAsync(d.Text("texture"));
            _ = LoadCapeOwnersAsync();
        }

        [RelayCommand] private void SetCapeSource(string source) => CapeSource = source;
        [RelayCommand] private void CloseCapeEditor() { CapeEditing = false; foreach (var c in CapeRows) c.IsSelected = false; }

        /// <summary>Temanın renklerini desene koyar (zemin, kenar, amblem, ikinci renk).</summary>
        [RelayCommand]
        private void UseTheme(string theme)
        {
            (ColorBase, ColorEdge, ColorInk, ColorInk2) = theme switch
            {
                "ufuk" => ("#2a0a4a", "#ff2df1", "#9ff4ff", "#ffd36e"),
                "derin" => ("#071214", "#2fe6d8", "#8ff7ee", "#2fe6d8"),
                _ => ("#1c140d", "#f2a33a", "#ffc873", "#d9b48a"),
            };
        }

        [RelayCommand]
        private async Task PickCapePng()
        {
            try
            {
                var r = await _core.Call("admin.pickCapePng");
                if (r.ValueKind != JsonValueKind.Object) return;
                _pngData = r.Text("data");
                PngName = r.Text("name");
                CapeSource = "png";
                await RefreshCapePreviewAsync();
            }
            catch (Exception ex) { CapeOk = false; CapeNote = ex.Message; }
        }

        /// <summary>Önizleme: sunucu dokuyu çizer (kaydetmeden); düz yüz ve 3B karakterde gösterilir.</summary>
        private async Task RefreshCapePreviewAsync(string? textureUrl = null)
        {
            try
            {
                byte[]? png;
                if (textureUrl != null || CapeSource == "keep") png = await Images.LoadBytesAsync(textureUrl ?? CapeRows.FirstOrDefault(c => c.Key == CapeKey)?.Data.Text("texture"));
                else
                {
                    object args = CapeSource switch
                    {
                        "art" => new { art = Art?.Key ?? "" },
                        "png" => new { png = _pngData ?? "" },
                        _ => new { design = Design() },
                    };
                    if (CapeSource == "png" && _pngData == null) return;
                    var r = await _core.Call("admin.capePreview", args);
                    png = Convert.FromBase64String(r.Text("data") ?? "");
                }
                if (png == null) return;
                CapePreviewBytes = png;
                CapeFace = CapePreview.Render(png, 8);
                if (CapeNote != null && !CapeOk) CapeNote = null;
            }
            catch (Exception ex) { CapeOk = false; CapeNote = ex.Message; }
        }

        private object Design() => new { @base = ColorBase.Trim(), edge = ColorEdge.Trim(), ink = ColorInk.Trim(), ink2 = ColorInk2.Trim(), emblem = Emblem?.Key ?? "none" };

        [RelayCommand]
        private async Task SaveCape()
        {
            CapeOk = false;
            if (CapeIsNew && CapeKey.Trim().Length == 0) CapeKey = SlugKey(CapeName);
            object cape = CapeSource switch
            {
                "art" => new { key = CapeKey.Trim(), name = CapeName, earn = CapeEarn, order = CapeOrder ?? 0, adminOnly = CapeAdminOnly, art = Art?.Key },
                "png" => new { key = CapeKey.Trim(), name = CapeName, earn = CapeEarn, order = CapeOrder ?? 0, adminOnly = CapeAdminOnly, png = _pngData },
                "keep" => new { key = CapeKey.Trim(), name = CapeName, earn = CapeEarn, order = CapeOrder ?? 0, adminOnly = CapeAdminOnly },
                _ => new { key = CapeKey.Trim(), name = CapeName, earn = CapeEarn, order = CapeOrder ?? 0, adminOnly = CapeAdminOnly, design = Design() },
            };
            try
            {
                CapeNote = "Kaydediliyor…";
                var r = await _core.Call("admin.saveCape", new { isNew = CapeIsNew, cape });
                string key = CapeKey.Trim().ToLowerInvariant();
                await FillCapeRowsAsync(r);
                if (CapeRows.FirstOrDefault(c => c.Key == key) is { } row) EditCape(row);
                CapeOk = true;
                CapeNote = "Kaydedildi ✓ Takanlar oyunda bir sonraki girişlerinde yeni hâlini görür.";
            }
            catch (Exception ex) { CapeNote = ex.Message; }
        }

        [RelayCommand]
        private async Task DeleteCape()
        {
            var row = CapeRows.FirstOrDefault(c => c.Key == CapeKey);
            string who = row is { AdminOnly: false } ? $" {row.Data.Int("owners")} kişiden alınır, takanlarda çıkarılır." : "";
            if (!await main.ConfirmAsync("Pelerini sil", $"\"{CapeName}\" silinsin mi?{who} Bu geri alınamaz.", "Sil", danger: true)) return;
            try
            {
                var r = await _core.Call("admin.deleteCape", new { key = CapeKey });
                CapeEditing = false;
                await FillCapeRowsAsync(r);
            }
            catch (Exception ex) { CapeOk = false; CapeNote = ex.Message; }
        }

        private async Task LoadCapeOwnersAsync()
        {
            CapeOwners.Clear();
            OwnersTitle = "";
            if (CapeIsNew || CapeAdminOnly) return;
            try
            {
                var tr = CultureInfo.GetCultureInfo("tr-TR");
                var list = await _core.Call("admin.capeOwners", new { key = CapeKey });
                foreach (var o in list.Items())
                {
                    string date = DateTime.TryParse(o.Text("earnedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at.ToLocalTime().ToString("d MMM yyyy", tr) : "";
                    string meta = string.Join(" · ", new[] { date, o.Text("note") ?? "", o.Bool("worn") ? "takılı" : "" }.Where(x => x.Length > 0));
                    CapeOwners.Add(new CapeOwner(this, o.Text("username") ?? "", meta, o.Bool("worn")));
                }
                OwnersTitle = CapeOwners.Count == 0 ? "Henüz kimsede yok." : $"{CapeOwners.Count} kişide var";
            }
            catch (Exception ex) { OwnersTitle = ex.Message; }
        }

        [RelayCommand]
        private async Task GrantCapeToOwner()
        {
            string name = GrantUsername.Trim();
            if (name.Length == 0) return;
            try
            {
                var r = await _core.Call("progress.grantCape", new { username = name, key = CapeKey, note = "" });
                CapeOk = true;
                CapeNote = r.Bool("added") ? $"{name} kullanıcısına verildi ✓" : $"{name} kullanıcısında zaten var.";
                GrantUsername = "";
                await LoadCapeOwnersAsync();
                await FillCapeRowsAsync(await _core.Call("admin.capes"));
            }
            catch (Exception ex) { CapeOk = false; CapeNote = ex.Message; }
        }

        public async Task RevokeCapeOwnerAsync(CapeOwner o)
        {
            if (!await main.ConfirmAsync("Pelerini geri al", $"{o.Username} kullanıcısının \"{CapeName}\" pelerini geri alınsın mı?", "Geri al")) return;
            try
            {
                await _core.Call("progress.revokeCape", new { username = o.Username, key = CapeKey });
                await LoadCapeOwnersAsync();
                await FillCapeRowsAsync(await _core.Call("admin.capes"));
            }
            catch (Exception ex) { CapeOk = false; CapeNote = ex.Message; }
        }

        private static string SlugKey(string text)
        {
            var map = new Dictionary<char, char> { ['ç'] = 'c', ['ğ'] = 'g', ['ı'] = 'i', ['ö'] = 'o', ['ş'] = 's', ['ü'] = 'u', ['â'] = 'a', ['î'] = 'i', ['û'] = 'u' };
            var chars = text.ToLower(CultureInfo.GetCultureInfo("tr-TR")).Select(ch => map.TryGetValue(ch, out var r) ? r : ch)
                .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-');
            string s = string.Join("-", new string(chars.ToArray()).Split('-', StringSplitOptions.RemoveEmptyEntries));
            return s.Length > 32 ? s[..32].Trim('-') : s;
        }
    }
}
