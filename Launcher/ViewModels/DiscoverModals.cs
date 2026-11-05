using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>Paketin ayrıntıları: bütün etiketler, değişiklikler ve işlemler. Sonuç "act" ya da "delete".</summary>
    internal sealed partial class PackDetailsModal(PackCard card) : ModalViewModel
    {
        public override double Width => 640;
        public PackCard Card { get; } = card;
        public IReadOnlyList<ChangeEntry> RecentChanges => Card.Changelog.Take(5).ToList();
        public bool HasChanges => Card.Changelog.Count > 0;

        /// <summary>Bütün geçmiş: önce son beşi, istenince hepsi.</summary>
        [ObservableProperty] private bool _showAll;
        public bool CanShowAll => Card.Changelog.Count > 5 && !ShowAll;
        public string ShowAllText => $"Bütün geçmiş ({Card.Changelog.Count})";
        public IReadOnlyList<ChangeEntry> Changes => ShowAll ? Card.Changelog : RecentChanges;

        partial void OnShowAllChanged(bool value)
        {
            OnPropertyChanged(nameof(Changes));
            OnPropertyChanged(nameof(CanShowAll));
        }

        [RelayCommand] private void ExpandChanges() => ShowAll = true;
        [RelayCommand] private void Act() => Close("act");
        [RelayCommand] private void Delete() => Close("delete");
    }

    /// <summary>"Güncellensin mi?": son kurulandan bu yana değişenler. Sonuç true ise güncellenir.</summary>
    internal sealed partial class PackUpdateModal(PackCard card, IReadOnlyList<ChangeEntry> changes) : ModalViewModel
    {
        public override double Width => changes.Count > 0 ? 600 : 440;
        public string Title => $"{card.Name} güncellensin mi?";
        public string Revisions => card.InstalledRevision > 0 ? $"rev {card.InstalledRevision} → rev {card.Revision}" : $"rev {card.Revision}";
        public IReadOnlyList<ChangeEntry> Changes { get; } = changes;

        [RelayCommand] private void Ok() => Close(true);
    }

    /// <summary>Kurulacak sürümün seçimi. Sonuç seçilen sürümün kimliği.</summary>
    internal sealed partial class VersionPickerModal(ContentHit hit) : ModalViewModel
    {
        private static readonly Dictionary<string, string> TypeNames = new() { ["release"] = "Kararlı", ["beta"] = "Beta", ["alpha"] = "Alfa" };

        public override double Width => 640;
        public string Title => $"{hit.Title} — sürüm seç";
        public ObservableCollection<VersionRow> Versions { get; } = new();
        [ObservableProperty] private string? _message = "Sürümler yükleniyor...";
        [ObservableProperty] private bool _isError;

        public async Task LoadAsync(string command, string instanceId)
        {
            try
            {
                var versions = await CoreClient.Instance.Call(command, new { projectId = hit.Id, instanceId });
                foreach (var v in versions.Items())
                {
                    string type = v.Str("type") ?? "release";
                    var meta = new[]
                    {
                        v.Str("number"),
                        string.Join(", ", v.Items("gameVersions").Select(g => g.GetString()).TakeLast(3)),
                        string.Join(", ", v.Items("loaders").Select(l => Names.Loader(l.GetString()))),
                        Names.Ago(v.Str("published")),
                    }.Where(s => !string.IsNullOrEmpty(s));
                    Versions.Add(new VersionRow(this, v.Str("id") ?? "", v.Str("name") is { Length: > 0 } n ? n : v.Str("number") ?? "",
                        type == "release" ? null : TypeNames.GetValueOrDefault(type, type), v.Bool("manualOnly"), string.Join(" · ", meta)));
                }
                Message = Versions.Count == 0 ? "Uyumlu sürüm bulunamadı." : null;
            }
            catch (Exception ex)
            {
                Message = "Sürümler alınamadı: " + ex.Message;
                IsError = true;
            }
        }
    }

    internal sealed partial class VersionRow(VersionPickerModal owner, string id, string name, string? badge, bool manualOnly, string meta)
    {
        public string Name { get; } = name;
        public string? Badge { get; } = badge;
        public bool HasBadge => Badge != null;
        public bool ManualOnly { get; } = manualOnly;
        public string Meta { get; } = meta;

        [RelayCommand] private void Pick() => owner.Close(id);
    }

    /// <summary>"Nereye kurulsun?": uyumlu instance'lar önce. Sonuç seçilen instance kimliği.</summary>
    internal sealed partial class TargetPickerModal : ModalViewModel
    {
        public TargetPickerModal(ContentHit hit, IReadOnlyList<InstanceItem> customs)
        {
            Title = $"{hit.Title} nereye kurulsun?";
            foreach (var row in customs.Select(i => new TargetRow(this, i, Problem(hit, i))).OrderBy(r => r.Problem != null ? 1 : 0))
                Rows.Add(row);
        }

        public string Title { get; }
        public ObservableCollection<TargetRow> Rows { get; } = new();
        public bool IsEmpty => Rows.Count == 0;

        /// <summary>İçerik bu instance'a uymuyorsa nedeni; uyuyorsa null.</summary>
        private static string? Problem(ContentHit hit, InstanceItem instance)
        {
            if (hit.GameVersions.Count > 0 && !hit.GameVersions.Contains(instance.McVersion)) return $"Minecraft {instance.McVersion} desteklenmiyor";
            if (hit.Type != "mod") return null;
            if (instance.Loader == "vanilla") return "Vanilla'ya mod kurulamaz";
            bool fits = hit.Loaders.Contains(instance.Loader) || (instance.Loader == "quilt" && hit.Loaders.Contains("fabric"));
            return fits ? null : $"{Names.Loader(instance.Loader)} desteklenmiyor";
        }
    }

    internal sealed partial class TargetRow(TargetPickerModal owner, InstanceItem instance, string? problem)
    {
        public string Name { get; } = instance.Name;
        public string? Problem { get; } = problem;
        public bool Fits => Problem == null;
        public string Meta => Problem ?? instance.Subtitle;

        [RelayCommand] private void Pick() => owner.Close(instance.Id);
    }

    /// <summary>
    /// Yapımcısı başka uygulamalardan indirmeye izin vermeyen CurseForge dosyaları: kullanıcı CurseForge sayfasından indirir,
    /// launcher İndirilenler klasöründe bulup doğrular ve yerine koyar. CurseForge'un kuralı; launcher bunları kendisi indirmez.
    /// </summary>
    internal sealed partial class ManualDownloadsModal : ModalViewModel
    {
        private readonly string? _instanceId;

        public ManualDownloadsModal(JsonElement manual, string? instanceId)
        {
            _instanceId = instanceId;
            foreach (var m in manual.Items())
                Rows.Add(new ManualRow(m.Get("fileId").ToString(), m.Str("title") ?? "", $"{m.Str("folder")}/ · tam olarak bu dosya: {m.Str("fileName")}", m.Str("url") ?? ""));
            Title = Rows.Count == 1 ? $"{Rows[0].Title} elle indirilmeli" : $"{Rows.Count} dosya elle indirilmeli";
            Intro = Rows.Count == 1
                ? "Bu modun yapımcısı, modun başka uygulamalardan indirilmesine izin vermiyor. CurseForge sayfasından yazan dosyayı indir; sayfadaki en yeni sürümü değil, tam olarak bu dosyayı."
                : "Bu dosyaların yapımcıları başka uygulamalardan indirilmelerine izin vermiyor. Her birini CurseForge sayfasından indir; sayfadaki en yeni sürümü değil, tam olarak yazan dosyayı.";
        }

        public override double Width => Rows.Count > 1 ? 640 : 480;
        public string Title { get; }
        public string Intro { get; }
        public ObservableCollection<ManualRow> Rows { get; } = new();
        public bool HasInstance => _instanceId != null;
        public string Help => HasInstance
            ? "İndirdikten sonra \"İndirilenleri kontrol et\"e bas: launcher dosyaları İndirilenler klasöründe bulur, doğru dosya olduklarını kontrol eder ve yerlerine koyar."
            : "Dosyaları instance klasöründe yazan klasöre koy.";

        [ObservableProperty] private string? _note;
        [ObservableProperty] private bool _allPlaced;
        [ObservableProperty] private bool _checking;

        [RelayCommand]
        private async Task Check()
        {
            if (_instanceId == null) return;
            Checking = true;
            try
            {
                var r = await CoreClient.Instance.Call("cf.manualPlace", new { instanceId = _instanceId });
                foreach (var res in r.Items("results"))
                {
                    var row = Rows.FirstOrDefault(x => x.FileId == res.Get("fileId").ToString());
                    if (row == null) continue;
                    string status = res.Str("status") ?? "";
                    row.Placed = status == "placed";
                    row.Status = status == "placed" ? "✓ Yerine kondu" : status == "problem" ? res.Str("problem") ?? "" : "İndirilenler'de henüz yok";
                    row.IsProblem = status == "problem";
                }
                Note = r.Get("downloads").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False
                    ? "İndirilenler klasörü bulunamadı; dosyaları instance klasöründe yazan klasöre kendin koy." : null;
                AllPlaced = Rows.All(x => x.Placed);
            }
            catch (Exception ex) { Note = ex.Message; }
            finally { Checking = false; }
        }

        [RelayCommand]
        private async Task OpenFolder()
        {
            try { await CoreClient.Instance.Call("files.open", new { target = "instance", id = _instanceId }); }
            catch { /* klasör açılamadı */ }
        }
    }

    internal sealed partial class ManualRow(string fileId, string title, string detail, string url) : ObservableObject
    {
        public string FileId { get; } = fileId;
        public string Title { get; } = title;
        public string Detail { get; } = detail;
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private bool _placed;
        [ObservableProperty] private bool _isProblem;

        [RelayCommand]
        private async Task Open()
        {
            try { await CoreClient.Instance.Call("cf.openPage", new { url }); }
            catch { /* tarayıcı açılamadı */ }
        }
    }
}
