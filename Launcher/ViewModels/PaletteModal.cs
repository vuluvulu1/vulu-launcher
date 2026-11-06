using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VuluLauncher.ViewModels
{
    internal sealed partial class PaletteItem(string kind, string label, string? sub, Func<Task> run) : ObservableObject
    {
        public string Kind { get; } = kind;
        public string Label { get; } = label;
        public string? Sub { get; } = sub;
        public bool HasSub => !string.IsNullOrEmpty(Sub);
        public Func<Task> Run { get; } = run;
        [ObservableProperty] private bool _isActive;
    }

    /// <summary>
    /// Hızlı arama (Ctrl+K): paketler, sayfalar, kişiler ve eylemler tek kutuda; yaz, ok tuşlarıyla seç, Enter ile git.
    /// Kişiler yalnızca bir şey yazılınca görünür. Sonuç seçilen öğe (kutu kapandıktan sonra çalıştırılır).
    /// </summary>
    internal sealed partial class PaletteModal : ModalViewModel
    {
        private readonly List<PaletteItem> _all;

        public PaletteModal(MainViewModel main)
        {
            _all = new();
            foreach (var i in main.Home.Instances)
            {
                var instance = i;
                _all.Add(new PaletteItem("Paket", i.Name, i.Subtitle, () => { main.Home.Selected = instance; main.SelectedTab = main.Tabs[0]; return Task.CompletedTask; }));
            }
            void Page(string key, string label) => _all.Add(new PaletteItem("Sayfa", label, null, () => { main.SelectedTab = main.Tabs.First(t => t.Key == key); return Task.CompletedTask; }));
            Page("home", "Oyna");
            _all.Add(new PaletteItem("Sayfa", "vulu Packs", null, () => { main.Discover.SetSource("packs"); main.SelectedTab = main.Tabs[1]; return Task.CompletedTask; }));
            _all.Add(new PaletteItem("Sayfa", "Mod ara (CurseForge / Modrinth)", null, () => { main.Discover.SetSource("curseforge"); main.SelectedTab = main.Tabs[1]; return Task.CompletedTask; }));
            Page("servers", "Sunucular");
            Page("social", "Sosyal");
            Page("settings", "Ayarlar");
            if (main.IsAdmin) Page("admin", "Admin paneli");
            foreach (var name in main.MembersCached.Keys.Where(n => n != main.Username).OrderBy(n => n, StringComparer.Create(new CultureInfo("tr-TR"), true)))
                _all.Add(new PaletteItem("Kişi", name, main.Social.FriendState(name) == "friend" ? "Arkadaş" : "Üye", () => main.Social.OpenConversationAsync(name)));
            _all.Add(new PaletteItem("Eylem", "Profilim", null, () => main.OpenProfileAsync(main.Username)));
            if (main.Home.Selected is { IsCustom: true } selected)
                _all.Add(new PaletteItem("Eylem", $"{selected.Name}: içeriği yönet", null, () => main.Home.ManageContentCommand.ExecuteAsync(null)));
            _all.Add(new PaletteItem("Eylem", "Yeni instance", null, () => main.Home.NewInstanceCommand.ExecuteAsync(null)));
            Render();
        }

        public override double Width => 560;
        public ObservableCollection<PaletteItem> Shown { get; } = new();
        [ObservableProperty] private string _query = "";
        [ObservableProperty] private bool _noResults;
        private int _index;

        partial void OnQueryChanged(string value) { _index = 0; Render(); }

        private void Render()
        {
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            string q = Query.Trim().ToLower(tr);
            var list = (q.Length > 0 ? _all.Where(i => i.Label.ToLower(tr).Contains(q)) : _all.Where(i => i.Kind != "Kişi")).Take(40).ToList();
            Shown.Clear();
            foreach (var i in list) Shown.Add(i);
            NoResults = Shown.Count == 0;
            Mark();
        }

        private void Mark()
        {
            _index = Math.Clamp(_index, 0, Math.Max(0, Shown.Count - 1));
            for (int i = 0; i < Shown.Count; i++) Shown[i].IsActive = i == _index;
        }

        public void Move(int delta) { _index += delta; Mark(); }

        [RelayCommand]
        public void Choose(PaletteItem? item = null)
        {
            item ??= Shown.ElementAtOrDefault(_index);
            if (item != null) Close(item);
        }
    }
}
