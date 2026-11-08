using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>Sorun listesindeki satır (eksik mod, sorunlu dosya, çökme nedeni).</summary>
    internal sealed partial class HealthItem : ObservableObject
    {
        public HealthItem(string title, string? detail, string? warn = null, string? badge = null, string? iconUrl = null, string? link = null, string? linkText = null)
        {
            Title = title; Detail = detail; Warn = warn; Badge = badge; Link = link; LinkText = linkText;
            if (iconUrl != null) _ = LoadIcon(iconUrl);
        }

        public string Title { get; }
        public string? Detail { get; }
        public string? Warn { get; }
        public string? Badge { get; }
        public string? Link { get; }
        public string? LinkText { get; }
        public bool HasDetail => !string.IsNullOrEmpty(Detail);
        public bool HasWarn => Warn != null;
        public bool HasBadge => Badge != null;
        public bool HasLink => Link != null;
        [ObservableProperty] private Bitmap? _icon;

        private async Task LoadIcon(string url) => Icon = await Images.LoadAsync(url, 64);

        /// <summary>CurseForge sayfaları cf.openPage ile, diğerleri (Modrinth) tarayıcıda açılır.</summary>
        [RelayCommand]
        private async Task OpenLink()
        {
            if (Link == null) return;
            bool curseForge = Link.StartsWith("https://www.curseforge.com/", StringComparison.Ordinal) || Link.StartsWith("https://curseforge.com/", StringComparison.Ordinal);
            try { await CoreClient.Instance.Call(curseForge ? "cf.openPage" : "links.open", new { url = Link }); } catch { /* açılamadı */ }
        }
    }

    /// <summary>Eksik bağımlılıklar. Sonuç "install" (kur ve oyna), "ignore" (yine de oyna) ya da null.</summary>
    internal sealed partial class MissingDepsModal : ModalViewModel
    {
        public MissingDepsModal(JsonElement missing, InstanceItem instance)
        {
            int total = 0, installable = 0;
            foreach (var m in missing.Items())
            {
                total++;
                bool found = m.Text("projectId") != null;
                if (found) installable++;
                Items.Add(new HealthItem(m.Text("title") ?? m.Text("id") ?? "?",
                    "İsteyen: " + string.Join(", ", m.Items("requiredBy").Select(r => r.GetString())),
                    found ? null : "CurseForge'ta da Modrinth'te de bulunamadı; elle kurman gerekiyor.",
                    m.Text("source") is { } src ? (src == "curseforge" ? "CurseForge" : "Modrinth") : null,
                    m.Text("iconUrl"), m.Text("pageUrl") ?? m.Text("searchUrl"), m.Text("pageUrl") != null ? "Sayfa ↗" : "Ara ↗"));
            }
            Intro = total == 1
                ? "Bir mod, kurulu olmayan başka bir modu istiyor. Oyun bu hâliyle büyük ihtimalle \"Error loading mods\" hatasıyla açılmaz."
                : $"{total} mod eksik. Oyun bu hâliyle büyük ihtimalle \"Error loading mods\" hatasıyla açılmaz.";
            CanInstall = instance.Kind == "custom" && installable > 0;
            InstallText = installable == total ? "Eksikleri kur ve oyna" : $"Bulunanları kur ({installable}) ve oyna";
        }

        public override double Width => 520;
        public string Intro { get; }
        public ObservableCollection<HealthItem> Items { get; } = new();
        public bool CanInstall { get; }
        public string InstallText { get; }

        [RelayCommand] private void Install() => Close("install");
        [RelayCommand] private void Ignore() => Close("ignore");
    }

    /// <summary>
    /// Açılıştan önce bulunan dosya sorunları: boş/bozuk jar'lar, kopyalar, yerine konmamış elle indirilecekler.
    /// Sonuç "retry", "ignore" ya da null.
    /// </summary>
    internal sealed partial class FileProblemsModal : ModalViewModel
    {
        private readonly MainViewModel _main;
        private readonly InstanceItem _instance;
        private readonly JsonElement _manual;
        private readonly List<string> _badFiles = new();

        public FileProblemsModal(MainViewModel main, JsonElement files, InstanceItem instance)
        {
            _main = main;
            _instance = instance;
            _manual = files.Get("manual").Clone();
            // Fabric ve Quilt aynı kimlikteki iki dosyadan yalnızca yenisini yükler (oyun açılır); Forge ve NeoForge açılmaz
            bool picksOne = instance.Loader is "fabric" or "quilt";
            bool severe = _manual.Items().Any();
            foreach (var b in files.Items("broken"))
            {
                severe = true;
                Add(b.Text("file") ?? "", b.Text("reason"));
            }
            foreach (var d in files.Items("duplicates"))
            {
                severe |= !picksOne || d.Text("reason") != "version";
                string copyOf = d.Text("copyOf") ?? "";
                Add(d.Text("file") ?? "", d.Text("reason") == "version"
                    ? picksOne ? $"{copyOf} ile aynı mod (ikisi de aynı kimliği kullanıyor). Oyun yalnızca yeni sürümü yükler; bu dosya boşuna duruyor."
                               : $"{copyOf} ile aynı mod; aynı modun iki sürümü birlikte yüklenemez."
                    : $"{copyOf} dosyasının kopyası; aynı mod iki kez yüklenir.");
            }
            foreach (var m in _manual.Items()) Manual.Add(new HealthItem(m.Text("title") ?? "", m.Text("fileName"), iconUrl: m.Text("iconUrl")));

            // Yalnızca elle indirilecek dosya varsa bozuk dosya anlatımı yerine ne yapılacağı söylenir
            bool onlyManual = Bad.Count == 0 && Manual.Count > 0;
            Title = onlyManual ? (Manual.Count == 1 ? "Bir mod elle indirilmeli" : $"{Manual.Count} mod elle indirilmeli")
                : severe ? "Mod dosyalarında sorun var" : "Gereksiz mod dosyası var";
            Intro = onlyManual
                ? "Yapımcıları bu modların başka uygulamalardan indirilmesine izin vermiyor. \"Elle indir…\"e bas, açılan sayfalardan indir; launcher dosyaları kendiliğinden yerine koyar ve oyun açılmaya devam eder."
                : (severe ? "Oyun bu dosyalarla büyük ihtimalle açılmaz." : "Oyun açılır, ama bu dosyalar boşuna yükleniyor.")
                  + " Sorunlu dosyalar silinmez, instance klasöründeki mods-yedek klasörüne taşınır.";
            CanMove = instance.Kind == "custom" && Bad.Count > 0;

            void Add(string file, string? text)
            {
                _badFiles.Add(file);
                Bad.Add(new HealthItem(file.StartsWith("mods/", StringComparison.Ordinal) ? file[5..] : file, text));
            }
        }

        public override double Width => 640;
        public string Title { get; }
        public string Intro { get; }
        public ObservableCollection<HealthItem> Bad { get; } = new();
        public ObservableCollection<HealthItem> Manual { get; } = new();
        public bool HasBad => Bad.Count > 0;
        public bool HasManual => Manual.Count > 0;
        [ObservableProperty] private bool _canMove;
        [ObservableProperty] private string _moveText = "Yedeğe taşı";
        [ObservableProperty] private string _manualText = "Elle indir…";

        [RelayCommand]
        private async Task Move()
        {
            CanMove = false;
            try
            {
                await CoreClient.Instance.Call("mods.backupFiles", new { instanceId = _instance.Id, files = _badFiles });
                MoveText = "✓ mods-yedek klasörüne taşındı";
            }
            catch (Exception ex) { MoveText = ex.Message; CanMove = true; }
        }

        [RelayCommand]
        private async Task OpenManual()
        {
            var modal = new ManualDownloadsModal(_manual, _instance.Kind == "custom" ? _instance.Id : null) { CloseWhenDone = true };
            await _main.ShowAsync(modal);
            if (!modal.AllPlaced) return;
            ManualText = "✓ Hepsi yerinde";
            // Başka sorun kalmadıysa oyun kendiliğinden açılmaya devam eder (Prism gibi)
            if (Bad.Count == 0) Close("retry");
        }

        [RelayCommand] private void Retry() => Close("retry");
        [RelayCommand] private void Ignore() => Close("ignore");
    }

    /// <summary>Oyun çöktü: nedenler, öneriler ve tek tıklık çözümler. Sonuç "replay" ise yeniden açılır.</summary>
    internal sealed partial class CrashModal : ModalViewModel
    {
        private readonly MainViewModel _main;

        public CrashModal(MainViewModel main, JsonElement crash)
        {
            _main = main;
            InstanceId = crash.Text("instanceId") ?? "";
            Report = crash.Text("report");
            Title = $"{crash.Text("name")} çöktü";
            var findings = crash.Items("findings").ToList();
            Intro = findings.Count == 1 && findings[0].Text("kind") == "unknown" ? "Oyun beklenmedik şekilde kapandı." : "Oyunun kapanma nedeni büyük ihtimalle şu:";
            foreach (var f in findings) Findings.Add(new CrashFinding(this, f));
        }

        public override double Width => 640;
        public string InstanceId { get; }
        public string? Report { get; }
        public bool HasReport => Report != null;
        public string Title { get; }
        public string Intro { get; }
        public ObservableCollection<CrashFinding> Findings { get; } = new();
        /// <summary>Bir çözüm uygulandıysa "Tekrar oyna" görünür.</summary>
        [ObservableProperty] private bool _changed;

        public async Task<bool> RunAsync(JsonElement action)
        {
            var core = CoreClient.Instance;
            try
            {
                switch (action.Text("type"))
                {
                    case "install":
                        await core.Call("mods.installMissing", new { instanceId = InstanceId, projectIds = action.Items("projectIds").Select(p => p.GetString()).ToList() });
                        break;
                    case "update":
                        await core.Call("mods.update", new { instanceId = InstanceId, projectId = action.Text("projectId"), versionId = action.Text("versionId") });
                        break;
                    case "disable":
                        await core.Call("instances.setContentEnabled", new { id = InstanceId, file = action.Text("file"), enabled = false });
                        break;
                }
                Changed = true;
                _main.Home.RefreshDetails();
                return true;
            }
            catch (Exception ex)
            {
                _main.Home.AppendLog(ex.Message, "error");
                return false;
            }
        }

        [RelayCommand]
        private async Task OpenReport()
        {
            try { await CoreClient.Instance.Call("crash.openReport", new { instanceId = InstanceId, file = Report }); }
            catch (Exception ex) { _main.Home.AppendLog(ex.Message, "error"); }
        }

        [RelayCommand]
        private async Task OpenFolder()
        {
            try { await CoreClient.Instance.Call("files.open", new { target = "instance", id = InstanceId }); } catch { /* açılamadı */ }
        }

        [RelayCommand] private void Replay() => Close("replay");
    }

    internal sealed partial class CrashFinding(CrashModal owner, JsonElement f)
    {
        public string Title { get; } = f.Text("title") ?? "";
        public string Detail { get; } = f.Text("detail") ?? "";
        public string Fix { get; } = "Öneri: " + f.Text("fix");
        public List<CrashAction> Actions { get; } = f.Items("actions").Select(a => new CrashAction(owner, a.Clone())).ToList();
        public bool HasActions => Actions.Count > 0;
    }

    internal sealed partial class CrashAction(CrashModal owner, JsonElement action) : ObservableObject
    {
        public bool IsPrimary { get; } = action.Text("type") != "disable";
        [ObservableProperty] private string _label = action.Text("label") ?? "";
        [ObservableProperty] private bool _canRun = true;

        [RelayCommand]
        private async Task Run()
        {
            CanRun = false;
            if (await owner.RunAsync(action)) Label += " ✓";
            else CanRun = true;
        }
    }
}
