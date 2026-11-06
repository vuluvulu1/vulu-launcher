using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>
    /// Yeni instance ya da (existing verilirse) düzenleme. Sonuç oluşturulan/düzenlenen instance'ın kimliği.
    /// </summary>
    internal sealed partial class InstanceEditorModal : ModalViewModel
    {
        private readonly InstanceItem? _existing;
        private readonly CoreClient _core = CoreClient.Instance;
        private int _requestId;
        private bool _ready;

        public InstanceEditorModal(InstanceItem? existing, string? presetName = null, string? presetMc = null, string? presetLoader = null)
        {
            _existing = existing;
            _name = existing?.Name ?? presetName ?? "";
            _wantedMc = existing?.McVersion ?? presetMc;
            _loader = existing?.Loader ?? presetLoader ?? "vanilla";
            _ = LoadAsync();
        }

        private readonly string? _wantedMc;

        public bool IsEdit => _existing != null;
        public string Title => IsEdit ? $"{_existing!.Name} — düzenle" : "Yeni instance";
        public string SubmitText => IsEdit ? "Kaydet" : "Oluştur";
        public string Hint => IsEdit
            ? "Dünyalar, modlar ve ayarlar yerinde kalır. Sürümü ya da yükleyiciyi değiştirirsen kurulu modlar uyumsuz kalabilir."
            : "Oyun dosyaları, mod yükleyici ve gereken Java ilk açılışta indirilir.";

        public ObservableCollection<string> McVersions { get; } = new();
        public ObservableCollection<Choice> Loaders { get; } = new();
        public ObservableCollection<Option> LoaderVersions { get; } = new();

        [ObservableProperty] private string _name;
        [ObservableProperty] private string? _mcVersion;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowLoaderVersion))]
        private string _loader;
        [ObservableProperty] private Option? _loaderVersion;
        [ObservableProperty] private string? _error;
        [ObservableProperty] private bool _canSubmit;
        [ObservableProperty] private bool _loaderVersionsLoading;

        public bool ShowLoaderVersion => Loader != "vanilla";

        private async Task LoadAsync()
        {
            try
            {
                var options = await _core.Call("instances.options");
                foreach (var v in options.Items("minecraftVersions")) McVersions.Add(v.GetString() ?? "");
                if (_wantedMc != null && !McVersions.Contains(_wantedMc)) McVersions.Insert(0, _wantedMc);
                foreach (var l in options.Items("loaders"))
                    Loaders.Add(new Choice(l.GetString() ?? "", Names.Loader(l.GetString())) { IsSelected = l.GetString() == Loader });
                _ready = true;
                McVersion = _wantedMc ?? McVersions.FirstOrDefault();
            }
            catch (Exception ex) { Error = "Sürüm listesi alınamadı: " + ex.Message; }
        }

        partial void OnMcVersionChanged(string? value) { if (_ready) _ = LoadLoaderVersionsAsync(); }

        [RelayCommand]
        private void PickLoader(Choice choice)
        {
            foreach (var l in Loaders) l.IsSelected = l == choice;
            Loader = choice.Key;
            _ = LoadLoaderVersionsAsync();
        }

        /// <summary>Seçilen Minecraft sürümü ve yükleyici için yükleyici sürümleri; ilki önerilen.</summary>
        private async Task LoadLoaderVersionsAsync()
        {
            int current = ++_requestId;
            Error = null;
            LoaderVersions.Clear();
            if (Loader == "vanilla") { CanSubmit = McVersion != null; return; }
            CanSubmit = false;
            LoaderVersionsLoading = true;
            try
            {
                var versions = (await _core.Call("instances.loaderVersions", new { loader = Loader, mcVersion = McVersion }))
                    .Items().Select(v => v.GetString() ?? "").ToList();
                if (current != _requestId) return;
                if (versions.Count == 0) { Error = "Bu Minecraft sürümü için bu yükleyici yok."; return; }
                for (int i = 0; i < versions.Count; i++) LoaderVersions.Add(new Option(versions[i], i == 0 ? $"{versions[i]} (önerilen)" : versions[i]));
                // Düzenlerken sürüm ve yükleyici değişmediyse şu an kurulu olan seçili gelir
                if (_existing is { LoaderVersion: { Length: > 0 } installed } && Loader == _existing.Loader && McVersion == _existing.McVersion)
                {
                    if (LoaderVersions.All(o => o.Key != installed)) LoaderVersions.Insert(0, new Option(installed, installed));
                    LoaderVersion = LoaderVersions.First(o => o.Key == installed);
                }
                else LoaderVersion = LoaderVersions[0];
                CanSubmit = true;
            }
            catch (Exception ex)
            {
                if (current == _requestId) Error = "Sürümler alınamadı: " + ex.Message;
            }
            finally { if (current == _requestId) LoaderVersionsLoading = false; }
        }

        [RelayCommand]
        private async Task Submit()
        {
            if (!CanSubmit || McVersion == null) return;
            CanSubmit = false;
            Error = null;
            try
            {
                var result = await _core.Call(IsEdit ? "instances.update" : "instances.create", new
                {
                    id = _existing?.Id ?? "",
                    name = Name.Trim().Length > 0 ? Name.Trim() : $"{Names.Loader(Loader)} {McVersion}",
                    mcVersion = McVersion,
                    loader = Loader,
                    loaderVersion = Loader == "vanilla" ? "" : LoaderVersion?.Key ?? "",
                });
                Close(result.Text("id"));
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                CanSubmit = true;
            }
        }
    }

    /// <summary>Kısa metin sorusu (ör. çoğaltılan instance'ın adı). Sonuç yazılan metin.</summary>
    internal sealed partial class PromptModal(string title, string label, string value, string okText) : ModalViewModel
    {
        public string Title { get; } = title;
        public string Label { get; } = label;
        public string OkText { get; } = okText;
        [ObservableProperty] private string _value = value;

        [RelayCommand]
        private void Ok()
        {
            if (Value.Trim().Length > 0) Close(Value.Trim());
        }
    }

    /// <summary>Simge, logo ve arka plan: değişiklik hemen uygulanır, pencere açık kalır.</summary>
    internal sealed partial class AppearanceModal : ModalViewModel
    {
        private readonly HomeViewModel _home;
        private InstanceItem _item;

        public AppearanceModal(HomeViewModel home, InstanceItem item)
        {
            _home = home;
            _item = item;
            Refresh();
        }

        public override double Width => 600;
        public string Title => $"{_item.Name} — görünüm";
        public bool IsCustom => _item.Kind == "custom";
        public bool IsShared => _item.SharedPackId != null;
        public string LogoReset => _item.Kind == "server" ? "Kaldırırsan paketin kendi logosuna dönülür." : "Kaldırırsan ad yazıyla gösterilir.";
        public string? BackgroundReset => _item.Kind == "server" ? "Kaldırırsan paketin kendi arka planına dönülür." : null;

        [ObservableProperty] private Bitmap? _icon;
        [ObservableProperty] private Bitmap? _logo;
        [ObservableProperty] private Bitmap? _background;
        [ObservableProperty] private bool _hasIcon, _hasLogo, _hasBackground, _backgroundAsIcon;
        [ObservableProperty] private string? _error;
        public string Name => _item.Name;

        private void Refresh()
        {
            HasIcon = _item.CustomIconUrl != null;
            HasLogo = _item.HasCustomLogo;
            HasBackground = _item.HasCustomBackground;
            _backgroundAsIcon = _item.BackgroundAsIcon;
            OnPropertyChanged(nameof(BackgroundAsIcon));
            _ = LoadImagesAsync();
        }

        private async Task LoadImagesAsync()
        {
            Icon = await Images.LoadAsync(_item.CustomIconUrl, 128);
            Logo = await Images.LoadAsync(_item.LogoUrl, 480);
            Background = await Images.LoadAsync(_item.BackgroundUrl, 640);
        }

        partial void OnBackgroundAsIconChanged(bool value)
        {
            _ = Apply();
            async Task Apply()
            {
                try { await CoreClient.Instance.Call("instances.setBackgroundAsIcon", new { id = _item.Id, value }); }
                catch (Exception ex) { Error = ex.Message; }
                await ReloadAsync();
            }
        }

        [RelayCommand] private Task PickIcon() => Run("instances.pickIcon");
        [RelayCommand] private Task ClearIcon() => Run("instances.clearIcon");
        [RelayCommand] private Task PickLogo() => Run("instances.pickLogo");
        [RelayCommand] private Task ClearLogo() => Run("instances.clearLogo");
        [RelayCommand] private Task PickBackground() => Run("instances.pickBackground");
        [RelayCommand] private Task ClearBackground() => Run("instances.clearBackground");
        [RelayCommand] private void Done() => Close(true);

        private async Task Run(string command)
        {
            Error = null;
            try
            {
                var result = await CoreClient.Instance.Call(command, new { id = _item.Id });
                if (!result.Bool("changed")) return;   // dosya seçmeden kapattı
                // Aynı dosya adına yeni resim yazılmış olabilir: önbellekteki eskisi atılır
                Images.Forget(_item.CustomIconUrl); Images.Forget(_item.LogoUrl); Images.Forget(_item.BackgroundUrl); Images.Forget(_item.ThumbnailUrl);
                await ReloadAsync();
            }
            catch (Exception ex) { Error = ex.Message; }
        }

        private async Task ReloadAsync()
        {
            await _home.ReloadInstancesAsync(_item.Id);
            if (_home.Instances.FirstOrDefault(i => i.Id == _item.Id) is { } fresh) _item = fresh;
            Refresh();
        }
    }

    /// <summary>Instance'ın içeriği: aç/kapa, kaldır, güncellemeleri denetle ve güncelle.</summary>
    internal sealed partial class ContentModal : ModalViewModel
    {
        private static readonly Dictionary<string, string> TypeNames = new() { ["mod"] = "Mod", ["resourcepack"] = "Kaynak paketi", ["shader"] = "Shader" };
        private readonly MainViewModel _main;
        private readonly InstanceItem _item;
        private readonly CoreClient _core = CoreClient.Instance;
        private Dictionary<string, JsonElement>? _updates;

        public ContentModal(MainViewModel main, InstanceItem item)
        {
            _main = main;
            _item = item;
            _ = LoadAsync();
        }

        public override double Width => 720;
        public string Title => $"{_item.Name} — içerik";
        public ObservableCollection<ContentRow> Rows { get; } = new();
        [ObservableProperty] private string _summary = "Yükleniyor...";
        [ObservableProperty] private string _checkText = "Güncellemeleri denetle";
        [ObservableProperty] private bool _canCheck = true;
        [ObservableProperty] private int _stableUpdates;
        [ObservableProperty] private bool _isEmpty;
        public bool HasStableUpdates => StableUpdates > 0;
        public string UpdateAllText => $"Tümünü güncelle ({StableUpdates})";

        partial void OnStableUpdatesChanged(int value)
        {
            OnPropertyChanged(nameof(HasStableUpdates));
            OnPropertyChanged(nameof(UpdateAllText));
        }

        public async Task LoadAsync()
        {
            JsonElement content;
            try { content = await _core.Call("instances.content", new { id = _item.Id }); }
            catch (Exception ex) { Summary = "Hata: " + ex.Message; return; }
            Rows.Clear();
            foreach (var i in content.Items("items"))
            {
                string file = i.Text("file") ?? "";
                var update = _updates != null && _updates.TryGetValue(file, out var u) ? u : (JsonElement?)null;
                Rows.Add(new ContentRow(this, file, i.Text("title") ?? file,
                    string.Join(" · ", new[] { TypeNames.GetValueOrDefault(i.Text("type") ?? ""), i.Text("version"), i.Num("sizeMb").ToString("0.#", CultureInfo.GetCultureInfo("tr-TR")) + " MB" }.Where(s => !string.IsNullOrEmpty(s))),
                    i.Get("enabled").ValueKind != JsonValueKind.False, i.Text("iconUrl"), update));
            }
            IsEmpty = Rows.Count == 0;
            Summary = $"{Rows.Count} dosya · Minecraft {content.Text("mcVersion")} · {Names.Loader(content.Text("loader"))}";
            StableUpdates = _updates?.Values.Count(u => u.Bool("stable")) ?? 0;
        }

        [RelayCommand]
        private async Task CheckUpdates()
        {
            CanCheck = false;
            CheckText = "Denetleniyor…";
            try
            {
                var result = await _core.Call("instances.checkUpdates", new { id = _item.Id });
                _updates = result.Items("updates").ToDictionary(u => u.Text("file") ?? "", u => u.Clone());
            }
            catch (Exception ex) { _updates = null; _main.Home.AppendLog("Güncellemeler denetlenemedi: " + ex.Message, "error"); }
            CheckText = _updates is { Count: 0 } ? "Güncel ✓" : "Güncellemeleri denetle";
            CanCheck = true;
            await LoadAsync();
        }

        [RelayCommand] private Task UpdateAll() => UpdateAsync(_updates?.Values.Where(u => u.Bool("stable")).ToList() ?? new());

        public async Task UpdateAsync(List<JsonElement> list)
        {
            foreach (var u in list)
            {
                try
                {
                    var r = await _core.Call("mods.update", new { instanceId = _item.Id, projectId = u.Text("projectId"), versionId = u.Text("versionId"), source = u.Text("source") ?? "" });
                    if (r.Items("manual").Any()) await _main.ShowAsync(new ManualDownloadsModal(r.Get("manual"), _item.Id));
                }
                catch (Exception ex) { _main.Home.AppendLog(ex.Message, "error"); }
            }
            _updates = null;
            CheckText = "Güncellemeleri denetle";
            await LoadAsync();
            _main.Home.RefreshDetails();
        }

        public async Task SetEnabledAsync(ContentRow row, bool enabled)
        {
            try { await _core.Call("instances.setContentEnabled", new { id = _item.Id, file = row.File, enabled }); _updates = null; }
            catch (Exception ex) { await _main.AlertAsync("Değiştirilemedi", ex.Message); }
            await LoadAsync();
            _main.Home.RefreshDetails();
        }

        public async Task RemoveAsync(ContentRow row)
        {
            if (!await _main.ConfirmAsync("Dosyayı kaldır", $"'{row.Title}' bu instance'tan kaldırılsın mı?", "Kaldır", danger: true)) return;
            try { await _core.Call("instances.removeContent", new { id = _item.Id, file = row.File }); }
            catch (Exception ex) { await _main.AlertAsync("Kaldırılamadı", ex.Message); }
            await LoadAsync();
            _main.Home.RefreshDetails();
        }

        [RelayCommand]
        private async Task AddContent()
        {
            Close();
            await _main.OpenDiscoverForAsync(_item);
        }
    }

    internal sealed partial class ContentRow : ObservableObject
    {
        private readonly ContentModal _owner;
        private readonly JsonElement? _update;
        private bool _loading = true;

        public ContentRow(ContentModal owner, string file, string title, string meta, bool enabled, string? iconUrl, JsonElement? update)
        {
            _owner = owner;
            File = file;
            Title = title;
            Meta = meta;
            _enabled = enabled;
            _update = update;
            UpdateText = update is { } u ? (u.Bool("stable") ? $"Güncelle ({u.Text("to")})" : $"Güncelle ({u.Text("to")} · {u.Text("type")})") : null;
            _loading = false;
            _ = LoadIcon(iconUrl);
        }

        public string File { get; }
        public string Title { get; }
        public string Meta { get; }
        public string? UpdateText { get; }
        public bool HasUpdate => UpdateText != null;
        [ObservableProperty] private bool _enabled;
        [ObservableProperty] private Bitmap? _icon;

        private async Task LoadIcon(string? url) => Icon = await Images.LoadAsync(url, 64);

        partial void OnEnabledChanged(bool value) { if (!_loading) _ = _owner.SetEnabledAsync(this, value); }

        [RelayCommand] private Task Update() => _update is { } u ? _owner.UpdateAsync(new List<JsonElement> { u }) : Task.CompletedTask;
        [RelayCommand] private Task Remove() => _owner.RemoveAsync(this);
    }

    /// <summary>Dünyalar ve yedekleri: yedekle, geri yükle, yedeği sil.</summary>
    internal sealed partial class WorldsModal : ModalViewModel
    {
        private readonly MainViewModel _main;
        private readonly InstanceItem _item;
        private readonly CoreClient _core = CoreClient.Instance;

        public WorldsModal(MainViewModel main, InstanceItem item)
        {
            _main = main;
            _item = item;
            _ = LoadAsync();
        }

        public override double Width => 680;
        public string Title => $"{_item.Name} — dünyalar";
        public ObservableCollection<WorldRow> Worlds { get; } = new();
        [ObservableProperty] private string _summary = "Yükleniyor...";
        [ObservableProperty] private bool _running;
        [ObservableProperty] private bool _isEmpty;

        public async Task LoadAsync()
        {
            JsonElement data;
            try { data = await _core.Call("worlds.list", new { id = _item.Id }); }
            catch (Exception ex) { Summary = ex.Message; return; }
            Running = data.Bool("running");
            Worlds.Clear();
            foreach (var w in data.Items("worlds").OrderByDescending(w => w.Text("lastPlayed")))
                Worlds.Add(new WorldRow(this, w));
            IsEmpty = Worlds.Count == 0;
            Summary = Worlds.Count > 0 ? $"{Worlds.Count} dünya" : "";
        }

        public async Task Act(Func<Task<JsonElement>> run, string? done)
        {
            try { await run(); if (done != null) _main.Home.AppendLog(done, "ok"); }
            catch (Exception ex) { await _main.AlertAsync("Olmadı", ex.Message); }
            await LoadAsync();
        }

        public Task Backup(WorldRow w) => Act(() => _core.Call("worlds.backup", new { id = _item.Id, world = w.Name }), $"{w.Name} yedeklendi.");

        public async Task Restore(WorldRow w, BackupRow b)
        {
            if (!await _main.ConfirmAsync("Geri yükle", $"'{w.Name}' bu yedekteki hâline döndürülsün mü? Şimdiki hâli önce ayrıca yedeklenir.", "Geri yükle")) return;
            await Act(() => _core.Call("worlds.restore", new { id = _item.Id, world = w.Name, backup = b.Name }), $"{w.Name} geri yüklendi.");
        }

        public async Task DeleteBackup(WorldRow w, BackupRow b)
        {
            if (!await _main.ConfirmAsync("Yedeği sil", "Bu yedek kalıcı olarak silinsin mi?", "Sil", danger: true)) return;
            await Act(() => _core.Call("worlds.deleteBackup", new { id = _item.Id, world = w.Name, backup = b.Name }), null);
        }

        [RelayCommand]
        private async Task OpenBackups()
        {
            try { await _core.Call("worlds.openBackups", new { id = _item.Id }); } catch { /* klasör açılamadı */ }
        }
    }

    internal sealed partial class WorldRow : ObservableObject
    {
        private readonly WorldsModal _owner;

        public WorldRow(WorldsModal owner, JsonElement w)
        {
            _owner = owner;
            Name = w.Text("name") ?? "";
            Meta = $"Son oynanma: {Names.Ago(w.Text("lastPlayed"))} · {w.Num("sizeMb").ToString("0.#", CultureInfo.GetCultureInfo("tr-TR"))} MB";
            foreach (var b in w.Items("backups")) Backups.Add(new BackupRow(this, b));
            _ = LoadIcon(w.Text("icon"));
        }

        public string Name { get; }
        public string Meta { get; }
        public bool Running => _owner.Running;
        public ObservableCollection<BackupRow> Backups { get; } = new();
        [ObservableProperty] private Bitmap? _icon;

        private async Task LoadIcon(string? url) => Icon = await Images.LoadAsync(url, 64);

        [RelayCommand] private Task Backup() => _owner.Backup(this);
        public Task Restore(BackupRow b) => _owner.Restore(this, b);
        public Task Delete(BackupRow b) => _owner.DeleteBackup(this, b);
    }

    internal sealed partial class BackupRow(WorldRow world, JsonElement b)
    {
        public string Name { get; } = b.Text("name") ?? "";
        public string Text { get; } = $"{Names.Ago(b.Text("createdAt"))} · {b.Num("sizeMb").ToString("0.#", CultureInfo.GetCultureInfo("tr-TR"))} MB"
            + (b.Bool("auto") ? " · otomatik" : (b.Text("name") ?? "").Contains("-geri-yukleme-oncesi-") ? " · geri yükleme öncesi" : "");
        public bool Running => world.Running;

        [RelayCommand] private Task Restore() => world.Restore(this);
        [RelayCommand] private Task Delete() => world.Delete(this);
    }

    /// <summary>Ekran görüntüleri galerisi: aç, klasörde göster, kopyala, sil.</summary>
    internal sealed partial class ScreenshotsModal : ModalViewModel
    {
        private readonly MainViewModel _main;
        private readonly InstanceItem _item;
        private readonly CoreClient _core = CoreClient.Instance;

        public ScreenshotsModal(MainViewModel main, InstanceItem item, bool pick = false)
        {
            _main = main;
            _item = item;
            IsPick = pick;
            _ = LoadAsync();
        }

        public override double Width => 860;
        public string Title => IsPick ? "Paylaşılacak ekran görüntüsünü seç" : $"{_item.Name} — ekran görüntüleri";
        /// <summary>Seçim için açıldıysa tıklanan görüntünün adı sonuç olur (paylaşmak için).</summary>
        public bool IsPick { get; }
        public bool NotPick => !IsPick;
        public ObservableCollection<ShotItem> Shots { get; } = new();
        [ObservableProperty] private string _summary = "Yükleniyor...";
        [ObservableProperty] private bool _isEmpty;

        private async Task LoadAsync()
        {
            JsonElement data;
            try { data = await _core.Call("screenshots.list", new { id = _item.Id }); }
            catch (Exception ex) { Summary = ex.Message; return; }
            Shots.Clear();
            foreach (var s in data.Items("shots")) Shots.Add(new ShotItem(this, s.Text("name") ?? "", s.Text("url"), s.Text("takenAt")));
            int total = data.Int("total");
            IsEmpty = total == 0;
            Summary = total == 0 ? "" : $"{total} ekran görüntüsü · {HomeViewModel.FormatSize(data.Num("totalMb"))}"
                + (total > Shots.Count ? $" · en yeni {Shots.Count} tanesi gösteriliyor" : "");
        }

        public async Task Run(string command, ShotItem shot)
        {
            if (IsPick && command == "screenshots.open") { Close(shot.Name); return; }
            try
            {
                await _core.Call(command, new { id = _item.Id, name = shot.Name });
                if (command == "screenshots.copy") _main.ShowToast("Kopyalandı", "Ekran görüntüsü panoya kopyalandı; yapıştırabilirsin.");
            }
            catch (Exception ex) { await _main.AlertAsync("Olmadı", ex.Message); }
        }

        public async Task Delete(ShotItem shot)
        {
            if (!await _main.ConfirmAsync("Ekran görüntüsünü sil", "Bu ekran görüntüsü geri dönüşüm kutusuna taşınsın mı?", "Sil", danger: true)) return;
            await Run("screenshots.delete", shot);
            await LoadAsync();
        }
    }

    internal sealed partial class ShotItem : ObservableObject
    {
        private readonly ScreenshotsModal _owner;

        public ShotItem(ScreenshotsModal owner, string name, string? url, string? takenAt)
        {
            _owner = owner;
            Name = name;
            When = Names.Ago(takenAt);
            _ = LoadImage(url);
        }

        public string Name { get; }
        public string When { get; }
        [ObservableProperty] private Bitmap? _image;

        private async Task LoadImage(string? url) => Image = await Images.LoadAsync(url, 400);

        [RelayCommand] private Task Open() => _owner.Run("screenshots.open", this);
        [RelayCommand] private Task Reveal() => _owner.Run("screenshots.reveal", this);
        [RelayCommand] private Task Copy() => _owner.Run("screenshots.copy", this);
        [RelayCommand] private Task Delete() => _owner.Delete(this);
    }

    /// <summary>"Dosyaları doğrula" sonucu.</summary>
    internal sealed partial class VerifyModal : ModalViewModel
    {
        public VerifyModal(string name, JsonElement r)
        {
            var repaired = r.Items("repaired").Select(x => x.GetString() ?? "").ToList();
            var moved = r.Items("moved").Select(x => (x.GetString() ?? "").Replace("mods/", "")).ToList();
            var missing = r.Items("missing").Select(x => x.GetString() ?? "").ToList();
            var manual = r.Items("manual").Select(m => m.Text("title") ?? "").ToList();
            var notes = r.Items("notes").Select(x => x.GetString() ?? "").ToList();
            string? gameError = r.Text("gameError");
            int fixedCount = repaired.Count + moved.Count;
            bool clean = fixedCount == 0 && gameError == null && missing.Count == 0 && manual.Count == 0 && notes.Count == 0;

            Title = clean ? $"{name}: sorun yok" : $"{name} doğrulandı";
            Intro = clean ? "Dosyalarda bir sorun bulunmadı." : fixedCount > 0 ? "Bulunan sorunlar onarıldı. Aşağıda kalanlar varsa onlara bak." : "Bazı şeyler kendiliğinden onarılamadı:";
            int checkedCount = r.Int("checkedCount");
            Items.Add(new VerifyLine(true, checkedCount > 0 ? $"{checkedCount} içerik denetlendi" : "İçerik listesi denetlendi",
                repaired.Count > 0 ? "Yeniden indirilenler: " + string.Join(", ", repaired) : "Hepsi sağlam."));
            if (moved.Count > 0) Items.Add(new VerifyLine(true, $"{moved.Count} sorunlu dosya mods-yedek klasörüne taşındı", string.Join(", ", moved)));
            Items.Add(new VerifyLine(gameError == null, gameError == null ? "Oyun dosyaları denetlendi" : "Oyun dosyaları denetlenemedi",
                gameError ?? "Minecraft, mod yükleyici ve kütüphaneler; eksik ya da bozuk olan yeniden indirildi."));
            foreach (var n in notes) Items.Add(new VerifyLine(false, "Onarılamadı", n));
            if (missing.Count > 0) Items.Add(new VerifyLine(false, "Eksik bağımlılıklar", string.Join(", ", missing) + " — oyunu açarken kurmanı önerecek."));
            if (manual.Count > 0) Items.Add(new VerifyLine(false, $"{manual.Count} dosya elle indirilmeli", string.Join(", ", manual)));
        }

        public override double Width => 560;
        public string Title { get; }
        public string Intro { get; }
        public List<VerifyLine> Items { get; } = new();

        [RelayCommand] private void Ok() => Close(true);
    }

    internal sealed record VerifyLine(bool Ok, string Text, string? Detail)
    {
        public string Mark => Ok ? "✓" : "!";
    }
}
