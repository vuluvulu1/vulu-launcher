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
    /// <summary>Profildeki rozet (sezon görevi ya da haftalık birincilik).</summary>
    internal sealed record BadgeItem(string Icon, string Name, string Desc, int Count)
    {
        public string Label => Count > 1 ? $"{Name} ×{Count}" : Name;
        public string Tip => Count > 1 ? $"{Desc} ({Count} kez)" : Desc;
    }

    /// <summary>Sezon görevi: ilerleme çubuğu.</summary>
    internal sealed record QuestItem(string Icon, string Name, string Desc, string ProgressText, double Fraction, bool Done);

    /// <summary>vulu Pelerini: kazanıldıysa takılabilir, kazanılmadıysa nasıl kazanılacağı yazar.</summary>
    internal sealed partial class CapeItem(ProfileViewModel owner, string key, string name, string earn, bool owned, Bitmap? preview, string color) : ObservableObject
    {
        public string Key { get; } = key;
        public string Name { get; } = name;
        public string Earn { get; } = earn;
        public bool Owned { get; } = owned;
        public bool Locked => !Owned;
        public Bitmap? Preview { get; } = preview;
        public IBrush Swatch { get; } = Color.TryParse(color, out var c) ? new SolidColorBrush(c) : Brushes.Gray;
        [ObservableProperty] private bool _isWorn;
        public string Tip => Owned ? (IsWorn ? $"{Name} — takılı. Çıkarmak için tıkla." : $"{Name} — takmak için tıkla.") : $"{Name} — {Earn}";
        partial void OnIsWornChanged(bool value) => OnPropertyChanged(nameof(Tip));
        [RelayCommand] private Task Toggle() => owner.ToggleCapeAsync(this);
    }

    /// <summary>Dolaptaki skin: küçük önizleme, tek tıkla giyilir.</summary>
    internal sealed partial class WardrobeItem(ProfileViewModel owner, string hash, string name, bool slim, Bitmap? preview) : ObservableObject
    {
        public string Hash { get; } = hash;
        public string Name { get; } = name;
        public bool Slim { get; } = slim;
        public Bitmap? Preview { get; } = preview;
        [ObservableProperty] private bool _isWorn;
        [RelayCommand] private Task Wear() => owner.WearAsync(this);
        [RelayCommand] private Task Remove() => owner.RemoveFromWardrobeAsync(this);
        [RelayCommand] private Task Rename() => owner.RenameWardrobeAsync(this);
    }

    internal sealed partial class ProfileViewModel
    {
        public ObservableCollection<BadgeItem> Badges { get; } = new();
        public ObservableCollection<QuestItem> Quests { get; } = new();
        public ObservableCollection<CapeItem> Capes { get; } = new();
        public ObservableCollection<WardrobeItem> Wardrobe { get; } = new();
        public ObservableCollection<string> GrantableCapes { get; } = new();
        private readonly Dictionary<string, string> _capeNames = new();

        [ObservableProperty] private byte[]? _skinBytes;
        [ObservableProperty] private byte[]? _capeBytes;
        [ObservableProperty] private int _level;
        [ObservableProperty] private double _levelFraction;
        [ObservableProperty] private string _levelText = "";
        [ObservableProperty] private bool _hasProgress;
        [ObservableProperty] private string? _seasonName;
        [ObservableProperty] private int _seasonLevel;
        [ObservableProperty] private double _seasonFraction;
        [ObservableProperty] private string _seasonText = "";
        [ObservableProperty] private string _statsText = "";
        [ObservableProperty] private string? _capeNote;
        [ObservableProperty] private string _wardrobeCount = "";
        [ObservableProperty] private string? _grantCape;
        [ObservableProperty] private string? _grantNote;

        public bool HasSeason => SeasonName != null;
        public bool HasBadges => Badges.Count > 0;
        public bool HasCapes => Capes.Count > 0;
        public bool HasWardrobe => Wardrobe.Count > 0;
        public bool ShowWardrobe => IsOwn;
        public bool CanGrantCapes => main.IsAdmin;
        public string BadgesTitle => $"Rozetler ({Badges.Count})";
        partial void OnSeasonNameChanged(string? value) => OnPropertyChanged(nameof(HasSeason));

        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        /// <summary>Seviye, sezon, görevler, rozetler ve pelerinler (sunucu istatistiklerinden).</summary>
        private async Task LoadProgressAsync(int request)
        {
            JsonElement p;
            try { p = IsOwn ? await _core.Call("progress.me") : await _core.Call("progress.user", new { username = Username }); }
            catch { HasProgress = false; return; }
            if (request != _requestId) return;

            Level = p.Int("level");
            double from = p.Num("from"), to = p.Num("to"), xp = p.Num("xp");
            LevelFraction = to > from ? Math.Clamp((xp - from) / (to - from), 0, 1) : 0;
            LevelText = $"{xp.ToString("N0", Tr)} / {to.ToString("N0", Tr)} XP";
            var t = p.Get("totals");
            StatsText = p.Bool("played")
                ? $"{t.Text("playtime")} oynadı · {t.Int("diamonds")} elmas · {t.Text("distance")} yol · {t.Int("advancements")} başarım · {t.Int("deaths")} ölüm"
                : IsOwn ? "vulu sunucusunda oynayınca seviyen ve istatistiklerin burada görünür." : "Henüz vulu sunucusunda oynamamış.";
            HasProgress = true;

            var s = p.Get("season");
            Quests.Clear();
            if (s.ValueKind == JsonValueKind.Object)
            {
                SeasonName = s.Text("name");
                SeasonLevel = s.Int("level");
                double sf = s.Num("from"), st = s.Num("to"), sx = s.Num("xp");
                SeasonFraction = st > sf ? Math.Clamp((sx - sf) / (st - sf), 0, 1) : 0;
                string ends = DateTime.TryParse(s.Text("endsAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end)
                    ? end.ToLocalTime().ToString("d MMMM", Tr) : "";
                SeasonText = $"Sezon seviyesi {SeasonLevel} · {ends} tarihinde biter · seviye {s.Int("capeLevel")} ya da bütün görevler: sezon pelerini";
                foreach (var q in s.Items("quests"))
                {
                    double value = q.Num("value"), target = Math.Max(1, q.Num("target"));
                    Quests.Add(new QuestItem(q.Text("icon") ?? "★", q.Text("name") ?? "", q.Text("desc") ?? "",
                        q.Text("text") ?? (q.Bool("done") ? "Tamamlandı ✓" : $"{value:0.#} / {target:0.#}"), Math.Clamp(value / target, 0, 1), q.Bool("done")));
                }
            }
            else SeasonName = null;

            Badges.Clear();
            foreach (var b in p.Items("badges"))
                Badges.Add(new BadgeItem(b.Text("icon") ?? "★", b.Text("name") ?? "", b.Text("desc") ?? "", b.Int("count", 1)));
            OnPropertyChanged(nameof(HasBadges));
            OnPropertyChanged(nameof(BadgesTitle));

            string worn = p.Text("capeKey") ?? "";
            Capes.Clear();
            GrantableCapes.Clear();
            _capeNames.Clear();
            foreach (var c in p.Items("capes"))
            {
                string key = c.Text("key") ?? "";
                _capeNames[c.Text("name") ?? key] = key;
                var png = await Images.LoadBytesAsync(c.Text("texture"));
                var item = new CapeItem(this, key, c.Text("name") ?? "", c.Text("earn") ?? "", c.Bool("owned"), png == null ? null : CapePreview.Render(png), c.Text("color") ?? "#888888")
                    { IsWorn = key == worn };
                Capes.Add(item);
            }
            if (CanGrantCapes)
            {
                try
                {
                    foreach (var c in (await _core.Call("progress.capes")).Items())
                    {
                        string name = c.Text("name") ?? "";
                        _capeNames[name] = c.Text("key") ?? "";
                        GrantableCapes.Add(name);
                    }
                }
                catch { }
            }
            OnPropertyChanged(nameof(HasCapes));
            OnPropertyChanged(nameof(CanGrantCapes));
        }

        public async Task ToggleCapeAsync(CapeItem cape)
        {
            if (!IsOwn) return;
            if (!cape.Owned) { CapeNote = $"{cape.Name}: {cape.Earn}"; return; }
            string key = cape.IsWorn ? "" : cape.Key;
            try
            {
                await _core.Call("progress.setCape", new { key });
                foreach (var c in Capes) c.IsWorn = c.Key == key;
                CapeNote = key.Length > 0 ? $"{cape.Name} takıldı ✓ Oyunda bir sonraki girişinde herkes görür." : "Pelerin çıkarıldı.";
                main.ForgetMembers();
                var hash = key.Length > 0 ? (await _core.Call("progress.capes")).Items().FirstOrDefault(c => c.Text("key") == key).Text("hash") : null;
                CapeBytes = hash != null ? await Images.LoadBytesAsync($"{Avatars.ApiBase}/yggdrasil/textures/{hash}?c=1") : null;
            }
            catch (Exception ex) { CapeNote = ex.Message; }
        }

        [RelayCommand]
        private async Task GrantCapeToUser()
        {
            if (GrantCape == null || !_capeNames.TryGetValue(GrantCape, out var key)) { GrantNote = "Bir pelerin seç."; return; }
            try
            {
                var r = await _core.Call("progress.grantCape", new { username = Username, key, note = "" });
                GrantNote = r.Bool("added") ? $"{GrantCape} verildi ✓" : "Bu pelerin zaten onda var.";
                await LoadProgressAsync(_requestId);
            }
            catch (Exception ex) { GrantNote = ex.Message; }
        }

        [RelayCommand]
        private async Task RevokeCapeFromUser()
        {
            if (GrantCape == null || !_capeNames.TryGetValue(GrantCape, out var key)) { GrantNote = "Bir pelerin seç."; return; }
            if (!await main.ConfirmAsync("Pelerini geri al", $"{Username} kullanıcısının {GrantCape} pelerini geri alınsın mı?", "Geri al")) return;
            try
            {
                await _core.Call("progress.revokeCape", new { username = Username, key });
                GrantNote = $"{GrantCape} geri alındı.";
                await LoadProgressAsync(_requestId);
            }
            catch (Exception ex) { GrantNote = ex.Message; }
        }

        // ── Dolap ────────────────────────────────────────────────────────
        private async Task LoadWardrobeAsync()
        {
            JsonElement w;
            try { w = await _core.Call("wardrobe.list"); } catch { return; }
            string current = w.Text("current") ?? "";
            var items = new List<WardrobeItem>();
            foreach (var s in w.Items("skins"))
            {
                string hash = s.Text("hash") ?? "";
                bool slim = s.Text("model") == "slim";
                var png = await Images.LoadBytesAsync($"{Avatars.ApiBase}/yggdrasil/textures/{hash}?c=1");
                items.Add(new WardrobeItem(this, hash, s.Text("name") ?? "", slim, png == null ? null : SkinRenderer.RenderFront(png, slim, 4)) { IsWorn = hash == current });
            }
            Wardrobe.Clear();
            foreach (var i in items) Wardrobe.Add(i);
            WardrobeCount = $"{items.Count}/{w.Int("max", 12)}";
            OnPropertyChanged(nameof(HasWardrobe));
            OnPropertyChanged(nameof(ShowWardrobe));
        }

        public async Task WearAsync(WardrobeItem item)
        {
            if (item.IsWorn) return;
            try
            {
                await _core.Call("wardrobe.apply", new { hash = item.Hash });
                await AfterChangeAsync(default);
            }
            catch (Exception ex) { await main.AlertAsync("Skin giyilemedi", ex.Message); }
        }

        public async Task RemoveFromWardrobeAsync(WardrobeItem item)
        {
            if (!await main.ConfirmAsync("Dolaptan çıkar", $"\"{item.Name}\" dolabından çıkarılsın mı?" + (item.IsWorn ? " Şu an giydiğin skin olarak kalır." : ""), "Çıkar")) return;
            try { await _core.Call("wardrobe.remove", new { hash = item.Hash }); await LoadWardrobeAsync(); }
            catch (Exception ex) { await main.AlertAsync("Çıkarılamadı", ex.Message); }
        }

        public async Task RenameWardrobeAsync(WardrobeItem item)
        {
            if (await main.ShowAsync(new PromptModal("Skinin adı", "Dolapta görünecek ad (en çok 32 harf)", item.Name, "Kaydet")) is not string name) return;
            name = name.Length > 32 ? name[..32] : name;
            try { await _core.Call("wardrobe.rename", new { hash = item.Hash, name = name.Trim() }); await LoadWardrobeAsync(); }
            catch (Exception ex) { await main.AlertAsync("Ad değiştirilemedi", ex.Message); }
        }
    }

    /// <summary>Pelerin dokusundan (64×32 ya da HD katı) dış yüzün büyütülmüş önizlemesi.</summary>
    internal static class CapePreview
    {
        public static Bitmap? Render(byte[] png, int scale = 6)
        {
            using var cape = SkiaSharp.SKBitmap.Decode(png);
            if (cape == null || cape.Width < 22 || cape.Height < 17) return null;
            float k = cape.Width / 64f;   // HD doku: 64×32'nin katı
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(10 * scale, 16 * scale));
            using var image = SkiaSharp.SKImage.FromBitmap(cape);
            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            surface.Canvas.DrawImage(image, SkiaSharp.SKRect.Create(1 * k, 1 * k, 10 * k, 16 * k), SkiaSharp.SKRect.Create(0, 0, 10 * scale, 16 * scale),
                new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Nearest));
            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return new Bitmap(data.AsStream());
        }
    }
}
