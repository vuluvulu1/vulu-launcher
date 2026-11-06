using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    internal sealed partial class DescriptionItem : ObservableObject
    {
        public DescriptionItem(DescriptionBlock block)
        {
            Block = block;
            if (block.ImageUrl != null) _ = Load(block.ImageUrl);
        }

        public DescriptionBlock Block { get; }
        [ObservableProperty] private Bitmap? _image;
        private async Task Load(string url) => Image = await Images.LoadAsync(url, 760);
    }

    internal sealed partial class GalleryItem : ObservableObject
    {
        public GalleryItem(string thumb, string url, string? title)
        {
            Url = url;
            Title = title;
            _ = Load(thumb);
        }

        public string Url { get; }
        public string? Title { get; }
        public bool HasTitle => !string.IsNullOrEmpty(Title);
        [ObservableProperty] private Bitmap? _image;
        private async Task Load(string url) => Image = await Images.LoadAsync(url, 480);
    }

    internal sealed record ProjectLink(string Label, string Url)
    {
        public IAsyncRelayCommand Open => new AsyncRelayCommand(async () =>
        {
            try { await CoreClient.Instance.Call("links.open", new { url = Url }); } catch { /* açılamadı */ }
        });
    }

    /// <summary>
    /// Keşfet'teki projenin ayrıntıları launcher'ın içinde: açıklama, galeri, sürümler ve bilgiler; buradan kurulur.
    /// Sonuç: "install" ya da seçilen sürümün kimliği.
    /// </summary>
    internal sealed partial class ProjectDetailsModal : ModalViewModel
    {
        private static readonly Dictionary<string, string> TypeNames = new() { ["release"] = "Kararlı", ["beta"] = "Beta", ["alpha"] = "Alfa" };
        private readonly ContentHit _hit;
        private readonly string _source, _versionsCommand, _instanceId;

        public ProjectDetailsModal(ContentHit hit, bool curseForge, string instanceId)
        {
            _hit = hit;
            _source = curseForge ? "curseforge" : "modrinth";
            _versionsCommand = curseForge ? "cf.versions" : "modrinth.versions";
            _instanceId = hit.Type == "modpack" ? "" : instanceId;
            PageText = (curseForge ? "CurseForge" : "Modrinth") + " ↗";
            foreach (var (key, label) in new[] { ("about", "Açıklama"), ("gallery", "Galeri"), ("versions", "Sürümler") }) Tabs.Add(new Choice(key, label) { IsSelected = key == "about" });
            Facts.Add(new Tag("Yapımcı: " + hit.Author));
            Facts.Add(new Tag(hit.Meta));
            foreach (var t in hit.Tags) Facts.Add(t);
            _ = LoadAsync();
        }

        public override double Width => 860;
        public ContentHit Hit => _hit;
        public string PageText { get; }
        public ObservableCollection<Choice> Tabs { get; } = new();
        public ObservableCollection<Tag> Facts { get; } = new();
        public ObservableCollection<DescriptionItem> Blocks { get; } = new();
        public ObservableCollection<GalleryItem> Gallery { get; } = new();
        public ObservableCollection<VersionRow> Versions { get; } = new();
        public ObservableCollection<ProjectLink> Links { get; } = new();
        public ObservableCollection<string> McFilters { get; } = new();
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsAbout), nameof(IsGallery), nameof(IsVersions))]
        private string _tab = "about";
        public bool IsAbout => Tab == "about";
        public bool IsGallery => Tab == "gallery";
        public bool IsVersions => Tab == "versions";
        public bool HasLinks => Links.Count > 0;
        [ObservableProperty] private string? _message = "Yükleniyor…";
        [ObservableProperty] private string? _versionsMessage;
        [ObservableProperty] private string? _mcFilter;
        private List<(VersionRow Row, List<string> Mc)>? _allVersions;

        partial void OnMcFilterChanged(string? value) => RenderVersions();

        private async Task LoadAsync()
        {
            try
            {
                var d = await CoreClient.Instance.Call("content.details", new { source = _source, id = _hit.Id });
                var blocks = d.Text("html") is { } html ? Description.FromHtml(html) : Description.FromMarkdown(d.Text("markdown") ?? "");
                foreach (var b in blocks) Blocks.Add(new DescriptionItem(b));
                if (Blocks.Count == 0) Blocks.Add(new DescriptionItem(new DescriptionBlock("p", _hit.Description.Length > 0 ? _hit.Description : "Açıklama yok.")));
                foreach (var g in d.Items("gallery")) Gallery.Add(new GalleryItem(g.Text("thumb") ?? g.Text("url") ?? "", g.Text("url") ?? "", g.Text("title")));
                foreach (var l in d.Items("links")) Links.Add(new ProjectLink(l.Text("label") ?? "", l.Text("url") ?? ""));
                OnPropertyChanged(nameof(HasLinks));
                Tabs[1] = new Choice("gallery", Gallery.Count > 0 ? $"Galeri ({Gallery.Count})" : "Galeri") { IsSelected = Tab == "gallery" };
                Message = null;
            }
            catch (Exception ex) { Message = "Ayrıntılar alınamadı: " + ex.Message; }
        }

        [RelayCommand]
        private async Task PickTab(Choice tab)
        {
            Tab = tab.Key;
            foreach (var t in Tabs) t.IsSelected = t.Key == tab.Key;
            if (Tab == "versions" && _allVersions == null) await LoadVersionsAsync();
        }

        private async Task LoadVersionsAsync()
        {
            VersionsMessage = "Sürümler yükleniyor…";
            try
            {
                var list = await CoreClient.Instance.Call(_versionsCommand, new { projectId = _hit.Id, instanceId = _instanceId });
                // Bu modal sürüm satırlarının sahibi olarak davranır: satıra basınca sonuç sürüm kimliği olur
                var picker = new VersionPickerModal(_hit);
                _allVersions = new();
                foreach (var v in list.Items())
                {
                    string type = v.Text("type") ?? "release";
                    var mc = v.Items("gameVersions").Select(g => g.GetString() ?? "").ToList();
                    var meta = string.Join(" · ", new[] { string.Join(", ", mc.TakeLast(3)), string.Join(", ", v.Items("loaders").Select(l => Names.Loader(l.GetString()))), Names.Ago(v.Text("published")) }
                        .Where(s => !string.IsNullOrEmpty(s)));
                    _allVersions.Add((new VersionRow(picker, v.Text("id") ?? "", v.Text("name") is { Length: > 0 } n ? n : v.Text("number") ?? "",
                        type == "release" ? null : TypeNames.GetValueOrDefault(type, type), v.Bool("manualOnly"), meta), mc));
                }
                _ = picker.Result.ContinueWith(t => { if (t.Result is string id) Avalonia.Threading.Dispatcher.UIThread.Post(() => Close(id)); });
                McFilters.Clear();
                McFilters.Add("Tüm Minecraft sürümleri");
                foreach (var v in _allVersions.SelectMany(x => x.Mc).Distinct().Where(v => System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d+\.\d+")).OrderByDescending(v => v, new VersionComparer()))
                    McFilters.Add(v);
                McFilter = McFilters[0];
                RenderVersions();
            }
            catch (Exception ex) { VersionsMessage = "Sürümler alınamadı: " + ex.Message; }
        }

        private void RenderVersions()
        {
            if (_allVersions == null) return;
            Versions.Clear();
            foreach (var (row, mc) in _allVersions)
                if (McFilter == null || McFilter == McFilters.FirstOrDefault() || mc.Contains(McFilter)) Versions.Add(row);
            VersionsMessage = Versions.Count == 0 ? "Uyumlu sürüm bulunamadı." : null;
        }

        [RelayCommand] private void Install() => Close("install");
        [RelayCommand] private void OpenPage() => Close("page");

        private sealed class VersionComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                static string Key(string? v) => string.Join(".", (v ?? "").Split('.', '-').Select(p => p.PadLeft(4, '0')));
                return string.CompareOrdinal(Key(x), Key(y));
            }
        }
    }
}
