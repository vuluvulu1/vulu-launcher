using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VuluLauncher.Native;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    /// <summary>Kütüphanede bir grubun başlığı: ızgarada satır boyu yer kaplar, grup hep yeni satırdan başlar.</summary>
    internal sealed partial class GroupHeader : ObservableObject
    {
        public GroupHeader(string name) => Name = name;
        public string Name { get; }
        [ObservableProperty] private int _count;
        [ObservableProperty] private bool _collapsed;
    }

    /// <summary>Paketlerin grupları (yalnızca bu bilgisayarda, UiState "libraryGroups"). Boş kalan grup kendiliğinden kalkar.</summary>
    internal sealed class GroupStore
    {
        public Dictionary<string, string> Assign { get; set; } = new();
        public List<string> Order { get; set; } = new();
        public List<string> Collapsed { get; set; } = new();

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static GroupStore Load()
        {
            try { if (UiState.Get("libraryGroups") is { Length: > 0 } s && JsonSerializer.Deserialize<GroupStore>(s, Json) is { } g) return g; }
            catch { /* bozuk kayıt: gruplar sıfırdan */ }
            return new GroupStore();
        }

        public void Save()
        {
            Order.RemoveAll(g => !Assign.ContainsValue(g));
            Collapsed.RemoveAll(g => !Order.Contains(g));
            UiState.Set("libraryGroups", JsonSerializer.Serialize(this, Json));
        }

        public string? GroupOf(string id) => Assign.TryGetValue(id, out var g) && Order.Contains(g) ? g : null;

        public void Set(string id, string? group)
        {
            if (string.IsNullOrWhiteSpace(group)) Assign.Remove(id);
            else
            {
                Assign[id] = group;
                if (!Order.Contains(group)) Order.Add(group);
            }
            Save();
        }

        public void Rename(string from, string to)
        {
            foreach (var id in Assign.Where(kv => kv.Value == from).Select(kv => kv.Key).ToList()) Assign[id] = to;
            int i = Order.IndexOf(from);
            if (Order.Contains(to)) Order.Remove(from); else if (i >= 0) Order[i] = to;
            if (Collapsed.Remove(from) && !Collapsed.Contains(to)) Collapsed.Add(to);
            Save();
        }

        public void Ungroup(string group)
        {
            foreach (var id in Assign.Where(kv => kv.Value == group).Select(kv => kv.Key).ToList()) Assign.Remove(id);
            Save();
        }
    }

    /// <summary>
    /// Kütüphanede gruplar: gruplanmamış paketler en üstte, sonra her grup kendi başlığıyla yeni satırdan başlar.
    /// Başlığa tıklayınca grup katlanır. Paket sağ tık › Gruba taşı ile ya da sürüklenip başka bir grubun başlığına veya
    /// kapaklarının arasına bırakılarak taşınır.
    /// </summary>
    internal sealed partial class LibraryView
    {
        private readonly GroupStore _groups = GroupStore.Load();
        private readonly Dictionary<string, GroupHeader> _headers = new();

        private GroupHeader HeaderFor(string name)
        {
            if (!_headers.TryGetValue(name, out var h)) _headers[name] = h = new GroupHeader(name);
            return h;
        }

        /// <summary>Sıralanmış paketler gruplara ayrılır: gruplanmamışlar başta (ardından kurulanlar ve Yeni paket), gruplar sonra.</summary>
        private List<object> Grouped(List<InstanceItem> sorted, IEnumerable<object> tail)
        {
            var items = new List<object>(sorted.Where(i => _groups.GroupOf(i.Id) == null));
            items.AddRange(tail);
            foreach (var name in _groups.Order)
            {
                var members = sorted.Where(i => _groups.GroupOf(i.Id) == name).ToList();
                if (members.Count == 0) continue;
                var header = HeaderFor(name);
                header.Count = members.Count;
                header.Collapsed = _groups.Collapsed.Contains(name);
                items.Add(header);
                if (!header.Collapsed) items.AddRange(members);
            }
            return items;
        }

        private void ToggleGroup(GroupHeader header)
        {
            if (!_groups.Collapsed.Remove(header.Name)) _groups.Collapsed.Add(header.Name);
            _groups.Save();
            Rebuild();
        }

        /// <summary>Sürükleme bitti: kapağın grubu, listede önündeki son başlığınki.</summary>
        private bool AssignFromPosition(InstanceItem item)
        {
            int index = _items.IndexOf(item);
            string? group = null;
            for (int i = index - 1; i >= 0; i--)
                if (_items[i] is GroupHeader h) { group = h.Name; break; }
            if (_groups.GroupOf(item.Id) == group) return false;
            _groups.Set(item.Id, group);
            return true;
        }

        private void MoveToGroup(InstanceItem item, string? group)
        {
            _groups.Set(item.Id, group);
            if (group != null && _groups.Collapsed.Remove(group)) _groups.Save();
            Rebuild();
        }

        private async Task NewGroupAsync(InstanceItem item)
        {
            if (_vm?.Main is not { } main) return;
            if (await main.ShowAsync(new GroupNameModal("Yeni grup", "", _groups.Order)) is string name) MoveToGroup(item, name);
        }

        /// <summary>Kapağın sağ tık menüsündeki "Gruba taşı": var olan gruplar, yeni grup ve gruptan çıkarma.</summary>
        private void FillGroupMenu(MenuFlyout menu, InstanceItem item)
        {
            if (menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Tag as string == "groups") is not { } root) return;
            root.Items.Clear();
            string? current = _groups.GroupOf(item.Id);
            foreach (var name in _groups.Order)
            {
                var entry = new MenuItem { Header = name, IsEnabled = name != current };
                entry.Click += (_, _) => MoveToGroup(item, name);
                root.Items.Add(entry);
            }
            if (_groups.Order.Count > 0) root.Items.Add(new Separator());
            var create = new MenuItem { Header = L.T("Yeni grup…") };
            create.Click += (_, _) => _ = NewGroupAsync(item);
            root.Items.Add(create);
            if (current != null)
            {
                var remove = new MenuItem { Header = L.T("Gruptan çıkar") };
                remove.Click += (_, _) => MoveToGroup(item, null);
                root.Items.Add(remove);
            }
        }

        /// <summary>Başlıktaki ⋯: grubu yeniden adlandır ya da dağıt.</summary>
        private void OnGroupMenu(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: GroupHeader header } button) return;
            var rename = new MenuItem { Header = L.T("Yeniden adlandır") };
            rename.Click += async (_, _) =>
            {
                if (_vm?.Main is not { } main) return;
                if (await main.ShowAsync(new GroupNameModal("Grubu yeniden adlandır", header.Name, _groups.Order.Where(g => g != header.Name))) is string name && name != header.Name)
                {
                    _groups.Rename(header.Name, name);
                    Rebuild();
                }
            };
            var ungroup = new MenuItem { Header = L.T("Grubu kaldır (paketler kalır)") };
            ungroup.Click += (_, _) => { _groups.Ungroup(header.Name); Rebuild(); };
            var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
            flyout.Items.Add(rename);
            flyout.Items.Add(ungroup);
            flyout.ShowAt(button);
            e.Handled = true;
        }
    }
}
