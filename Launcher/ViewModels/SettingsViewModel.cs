using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>
    /// Ayarlar sayfası. Her değişiklik anında kaydedilir (settings.save; Core pencere boyutu, başlangıç kaydı, Discord
    /// gibi yan etkileri kendisi uygular). Sayfa her açılışta baştan kurulur ki başka yerden değişen ayarlar da görünsün.
    /// </summary>
    internal sealed partial class SettingsViewModel(MainViewModel main) : ObservableObject
    {
        private static readonly (string Key, string Label)[] UiScales = { ("90", "Küçük"), ("100", "Normal"), ("110", "Büyük"), ("125", "Çok büyük") };
        private static readonly (int W, int H)[] WindowSizes = { (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440) };

        private readonly CoreClient _core = CoreClient.Instance;
        private JsonObject _settings = new();
        private IDisposable? _noteTimer;

        public ObservableCollection<SettingSection> Sections { get; } = new();

        [ObservableProperty] private string _search = "";
        [ObservableProperty] private string _note = "";
        [ObservableProperty] private bool _noteIsError;
        [ObservableProperty] private string? _loadError;
        [ObservableProperty] private bool _noResults;

        partial void OnSearchChanged(string value) => Filter();

        public async Task LoadAsync()
        {
            JsonElement info;
            try { info = await _core.Call("settings.get"); }
            catch (Exception ex) { LoadError = "Ayarlar alınamadı: " + ex.Message; return; }
            LoadError = null;
            _settings = JsonNode.Parse(info.Get("settings").GetRawText())!.AsObject();
            Build(info);
            Filter();
        }

        // ── Kaydetme ─────────────────────────────────────────────────────
        private string S(string key, string fallback = "") => _settings[key] is JsonValue v && v.TryGetValue(out string? s) && s != null ? s : fallback;
        private bool B(string key, bool fallback = false) => _settings[key] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;
        private int I(string key) => _settings[key] is JsonValue v && v.TryGetValue(out int n) ? n : 0;

        /// <summary>Değişikliği kaydeder; kaydedilemezse eski ayarlara dönülür ve hata gösterilir.</summary>
        private async Task<bool> Change(params (string Key, JsonNode? Value)[] patch)
        {
            var before = _settings.DeepClone().AsObject();
            foreach (var (key, value) in patch) _settings[key] = value;
            try
            {
                var result = await _core.Call("settings.save", _settings);
                _settings = JsonNode.Parse(result.Get("settings").GetRawText())!.AsObject();
                Flash("Kaydedildi ✓");
                return true;
            }
            catch (Exception ex)
            {
                _settings = before;
                Flash(ex.Message, true);
                return false;
            }
        }

        private void Flash(string text, bool error = false)
        {
            Note = text;
            NoteIsError = error;
            _noteTimer?.Dispose();
            _noteTimer = DispatcherTimer.RunOnce(() => Note = "", TimeSpan.FromMilliseconds(error ? 5000 : 1600));
        }

        // ── Sayfa ────────────────────────────────────────────────────────
        private void Build(JsonElement info)
        {
            Sections.Clear();
            Sections.Add(GameSection(info));
            Sections.Add(LauncherSection(info));
            Sections.Add(LookSection());
            Sections.Add(StorageSection());
            Sections.Add(AccountSection(info));
            Sections[0].IsCurrent = true;
        }

        private SettingSection GameSection(JsonElement info)
        {
            var ram = new List<(string, string)> { ("0", "Önerilen") };
            ram.AddRange(info.Items("ramOptionsGb").Select(gb => (gb.GetInt32().ToString(CultureInfo.InvariantCulture), gb.GetInt32() + " GB")));

            var javaPath = new TextRow("Java dosyası", "Elle seçilen java.exe.", S("customJavaPath"), @"C:\...\bin\java.exe",
                value => Change(("javaMode", "custom"), ("customJavaPath", value)))
            {
                IsAvailable = S("javaMode") == "custom",
                // "Gözat": seçilen dosya elle seçilmiş Java olarak kaydedilir
                Action = new RowAction("Gözat", async () =>
                {
                    var picked = await _core.Call("settings.pickJava");
                    if (picked.Text("path") is not { Length: > 0 } path) return;
                    if (await Change(("javaMode", "custom"), ("customJavaPath", path))) await LoadAsync();
                }),
            };

            var javaMode = new ChoiceRow("Java", "Otomatik modda oyunun istediği sürüm (8, 17, 21...) bilgisayarda kuruluysa o kullanılır, yoksa Eclipse Temurin olarak indirilir.",
                new[] { ("auto", "Otomatik"), ("custom", "Elle seç") }, S("javaMode", "auto"), async value =>
                {
                    javaPath.IsAvailable = value == "custom";
                    // Elle modda yol girilene kadar kaydedilmez; boş yol hata verir
                    if (value == "custom" && javaPath.Value.Trim().Length == 0) return true;
                    return await Change(("javaMode", value), ("customJavaPath", javaPath.Value));
                });

            var detected = new JavaListRow("Bilgisayarda bulunan Java");
            foreach (var j in info.Items("detectedJava"))
            {
                string path = j.Text("path") ?? "";
                detected.Items.Add(new JavaItem("Java " + j.Text("version"), j.Text("source") ?? "", path,
                    S("javaMode") == "custom" && S("customJavaPath") == path,
                    async item =>
                    {
                        if (await Change(("javaMode", "custom"), ("customJavaPath", item.Path))) await LoadAsync();
                    }));
            }

            var installed = info.Items("installedJava").Select(v => "Java " + v.GetInt32()).ToList();
            var removeJava = new RowAction("Sil", async () =>
            {
                if (!await main.ConfirmAsync("Java sürümlerini sil", "İndirilmiş Java sürümleri silinecek. Oyun bir sonraki açılışta gerekeni yeniden indirir.", "Sil", danger: true)) return;
                try { await _core.Call("settings.removeJava"); Flash("Silindi ✓"); }
                catch (Exception ex) { Flash(ex.Message, true); }
                await LoadAsync();
            }, danger: true) { IsEnabled = installed.Count > 0 };

            return new SettingSection("Oyun")
                .Add(new ChoiceRow("Varsayılan RAM", "Bir paket için ayrıca seçim yapmadıysan kullanılır.", ram,
                    I("defaultRamGb").ToString(CultureInfo.InvariantCulture), value => Change(("defaultRamGb", int.Parse(value, CultureInfo.InvariantCulture)))))
                .Add(javaMode)
                .Add(javaPath)
                .Add(detected)
                .Add(new ActionRow("İndirilen Java sürümleri", installed.Count > 0 ? "İndirilmiş: " + string.Join(", ", installed) : "Henüz indirilmiş Java yok; ilk oyun açılışında indirilir.")
                    .With(removeJava))
                .Add(new TextRow("Ek JVM argümanları", "İleri düzey. Boş bırakılırsa varsayılanlar kullanılır.", S("jvmArgs"), "Örn. -XX:+UseZGC",
                    value => Change(("jvmArgs", value))))
                .Add(new GameSizeRow("Oyun penceresi", "Oyun açıldığında pencerenin boyutu. Varsayılan: oyunun kendi boyutu.",
                    I("gameWidth"), I("gameHeight"), (w, h) => Change(("gameWidth", w), ("gameHeight", h))))
                .Add(new ToggleRow("Tam ekran başlat", null, B("fullscreen"), value => Change(("fullscreen", value))));
        }

        private SettingSection LauncherSection(JsonElement info)
        {
            // Ekrana sığmayan pencere boyutları gösterilmez; daha önce seçilmiş ama artık sığmayan da seçili görünsün
            var screen = info.Get("screen");
            int sw = screen.Int("width", int.MaxValue), sh = screen.Int("height", int.MaxValue);
            var sizes = new List<(string, string)> { ("", "Otomatik") };
            sizes.AddRange(WindowSizes.Where(s => s.W <= sw && s.H <= sh).Select(s => ($"{s.W}x{s.H}", $"{s.W}×{s.H}")));
            string current = S("windowSize");
            if (current.Length > 0 && sizes.All(s => s.Item1 != current)) sizes.Add((current, current.Replace('x', '×')));

            Func<bool, Task<bool>> toggle(string key) => value => Change((key, value));

            return new SettingSection("Launcher")
                .Add(new ChoiceRow("Oyun açılınca", "\"Kapat\" seçilirse oyun çıktısı konsolda görünmez ve oynama süresi sayılmaz.",
                    new[] { ("keep", "Açık kalsın"), ("minimize", "Küçült"), ("close", "Kapat") }, S("onGameStart", "keep"), value => Change(("onGameStart", value))))
                .Add(new ToggleRow("Açılış animasyonunu atla", null, B("skipSplash"), toggle("skipSplash")))
                .Add(new ChoiceRow("Launcher boyutu", "Pencerenin boyutu; arayüz pencereyle birlikte ölçeklenir. Ekrana sığmayan boyutlar gösterilmez.",
                    sizes, current, value => Change(("windowSize", value))))
                .Add(new ToggleRow("Bilgisayar açılınca başlat", "Pencere açılmaz; launcher simge durumunda başlar, mesaj ve bildirimler gelir. Simgeye tıklayınca açılır.",
                    B("startWithSystem", true), toggle("startWithSystem")))
                .Add(new ChoiceRow("Kapat tuşu", "Bildirim alanına küçülünce mesaj ve oyun bildirimleri gelmeye devam eder; launcher az kaynak kullanır. Tamamen kapatmak için simgeye sağ tıkla → Çıkış.",
                    new[] { ("true", "Bildirim alanına küçült"), ("false", "Kapat") }, B("closeToTray", true) ? "true" : "false", value => Change(("closeToTray", value == "true"))))
                .Add(new ChoiceRow("Oyuna girenler", "Biri oyuna girince sağ altta \"oyunda\" bildirimi.",
                    new[] { ("friends", "Arkadaşlarım"), ("all", "Herkes"), ("off", "Kapalı") }, S("playingNotifications", "friends"), value => Change(("playingNotifications", value))))
                .Add(new ChoiceRow("Arkadaşların ne görsün", "Sunucular ve profil sayfasındaki arkadaş listesinde.",
                    new[] { ("full", "Ne oynadığım"), ("online", "Sadece çevrimiçi"), ("hidden", "Görünmez") }, S("presenceMode", "full"), value => Change(("presenceMode", value))))
                .Add(new ToggleRow("Mesaj bildirimleri", "Launcher arkadayken ya da oyundayken yeni mesaj gelince Windows bildirimi.", B("dmNotifications", true), toggle("dmNotifications")))
                .Add(new ToggleRow("Bildirimde mesajı göster", "Kapalıysa bildirimde yalnızca kimden geldiği yazar; mesajın kendisi görünmez.", B("dmNotificationPreview", true), toggle("dmNotificationPreview")))
                .Add(new ToggleRow("Beğeni ve yorum bildirimi", "Ekran görüntün beğenilince ya da yorum gelince haber verir.", B("socialNotifications", true), toggle("socialNotifications")))
                .Add(new ToggleRow("Oynama süremi profilimde göster", "Toplam oynama süren ve en çok oynadığın paketler profilinde görünür.", B("showPlaytimeOnProfile", true), toggle("showPlaytimeOnProfile")))
                .Add(new ToggleRow("Paket güncelleme bildirimi", "Kurduğun bir vulu pack güncellenince haber verir.", B("packUpdateNotifications", true), toggle("packUpdateNotifications")))
                .Add(new ToggleRow("Discord'da durum göster", "Discord açıksa arkadaşların ne oynadığını görür: \"vulu Launcher · AGLR SMP oynuyor\".", B("discordPresence", true), toggle("discordPresence")))
                .Add(new ToggleRow("Konsolu büyük aç", null, B("consoleExpanded"), toggle("consoleExpanded")));
        }

        private SettingSection LookSection() =>
            new SettingSection("Ses ve görünüm")
                .Add(new ChoiceRow("Yazı ve arayüz boyutu", "Pencere aynı kalır, içindeki bütün yazılar ve düğmeler büyür ya da küçülür. Büyük ekranlarda \"Büyük\" daha rahat okunur.",
                    UiScales, I("uiScale") is var scale && scale > 0 ? scale.ToString(CultureInfo.InvariantCulture) : "100",
                    value => Change(("uiScale", int.Parse(value, CultureInfo.InvariantCulture)))))
                .Add(new ToggleRow("Radyo", "Soldaki çalar internet radyosu çalar; istasyonu ve türü adına tıklayarak seçebilirsin.", B("musicEnabled", true), async value =>
                {
                    if (!await Change(("musicEnabled", value))) return false;
                    if (value) main.Radio.StartIfEnabled(); else main.Radio.Stop();
                    return true;
                }))
                .Add(new SliderRow("Ses düzeyi", null, I("musicVolume"), async value =>
                {
                    if (!await Change(("musicVolume", value))) return false;
                    main.Radio.Volume = value;
                    return true;
                }));

        /// <summary>Boyutlar diskte dolaşılarak hesaplanır (birkaç saniye sürebilir); bölüm önce "Hesaplanıyor…" ile açılır.</summary>
        private SettingSection StorageSection()
        {
            var cache = new ActionRow("Önbellek", "Hesaplanıyor…");
            var instances = new ActionRow("Paketler", "Kurulu paketlerin modları, ayarları ve dünyaları.");
            var game = new ActionRow("Minecraft dosyaları", "Paketlerin ortak kullandığı oyun sürümleri, kütüphaneler ve sesler/dokular.");
            var java = new ActionRow("Java", "Launcher'ın indirdiği Java sürümleri (yukarıdan silinebilir).");

            async Task Load()
            {
                try
                {
                    var usage = await _core.Call("storage.usage");
                    double shared = usage.Num("modCacheShared");
                    cache.Hint = "Silinmiş ya da güncellenmiş paketlerden kalan modlar ve ekran görüntüsü önizlemeleri."
                        + (shared > 0 ? $" Kurulu paketlerinle paylaşılan {FormatBytes(shared)} mod ek yer kaplamaz, sayılmadı." : "");
                    cache.Value = FormatBytes(usage.Num("cache"));
                    instances.Value = FormatBytes(usage.Num("instances"));
                    game.Value = FormatBytes(usage.Num("game"));
                    java.Value = FormatBytes(usage.Num("java"));
                }
                catch (Exception ex) { cache.Hint = ex.Message; }
            }

            cache.With(new RowAction("Temizle", async () =>
            {
                if (!await main.ConfirmAsync("Önbelleği temizle", "Hiçbir paketin kullanmadığı modlar ve ekran görüntüsü önizlemeleri silinecek. Paketlerine, dünyalarına ve ayarlarına dokunulmaz.", "Temizle", danger: true)) return;
                try
                {
                    var result = await _core.Call("storage.clear");
                    Flash(FormatBytes(result.Num("freed")) + " boşaldı ✓");
                }
                catch (Exception ex) { Flash(ex.Message, true); }
                await Load();
            }, danger: true));
            _ = Load();

            return new SettingSection("Depolama").Add(cache).Add(instances).Add(game).Add(java);
        }

        private SettingSection AccountSection(JsonElement info)
        {
            bool hasPassword = info.Bool("hasSavedPassword");
            string gameFolder = info.Text("gameFolder") ?? "";
            string licenses = Path.Combine(AppContext.BaseDirectory, "licenses");

            return new SettingSection("Hesap ve dosyalar")
                .Add(new ActionRow("Hesap", $"{main.Username} olarak giriş yapıldı.").With(new RowAction("Çıkış yap", main.LogoutAsync)))
                .Add(new ActionRow("Kayıtlı şifre", hasPassword ? $"{info.Text("savedUsername")} için şifre bu bilgisayarda kayıtlı." : "Kayıtlı şifre yok.")
                    .With(new RowAction("Sil", async () =>
                    {
                        await _core.Call("auth.forgetPassword");
                        Flash("Silindi ✓");
                        await LoadAsync();
                    }) { IsEnabled = hasPassword }))
                .Add(new ActionRow("Oyun klasörü", gameFolder)
                    .With(new RowAction("Aç", () => OpenFolder("base")))
                    .With(new RowAction("Instance'lar", () => OpenFolder("instances"))))
                .Add(new ActionRow("Sürüm", null) { Value = "vulu Launcher " + main.Version })
                .Add(new ActionRow("Tanıtım rehberi", "İlk açılışta gösterilen dört adımlık kısa rehber.")
                    .With(new RowAction("Yeniden göster", main.ShowTourAsync)))
                .Add(new ActionRow("Açık kaynak lisansları", "Kullanılan bileşenler ve lisans metinleri.")
                    .With(new RowAction("Göster", () =>
                    {
                        if (Directory.Exists(licenses)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{licenses}\""));
                        return Task.CompletedTask;
                    })));
        }

        private async Task OpenFolder(string target)
        {
            try { await _core.Call("files.open", new { target }); }
            catch (Exception ex) { Flash(ex.Message, true); }
        }

        public static string FormatBytes(double bytes)
        {
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            if (bytes >= 1024d * 1024 * 1024) return (bytes / 1024 / 1024 / 1024).ToString("0.0", tr) + " GB";
            if (bytes >= 1024d * 1024) return (bytes / 1024 / 1024).ToString("0", tr) + " MB";
            if (bytes >= 1024) return (bytes / 1024).ToString("0", tr) + " KB";
            return bytes.ToString("0", tr) + " B";
        }

        // ── Arama ────────────────────────────────────────────────────────
        /// <summary>Aramaya uyan satırlar kalır; hiç satırı kalmayan bölüm gizlenir.</summary>
        private void Filter()
        {
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            string query = Search.Trim().ToLower(tr);
            bool any = false;
            foreach (var section in Sections)
            {
                bool titleMatch = query.Length == 0 || section.Title.ToLower(tr).Contains(query);
                int shown = 0;
                foreach (var row in section.Rows)
                {
                    row.MatchesSearch = titleMatch || row.SearchText.ToLower(tr).Contains(query);
                    if (row.MatchesSearch && row.IsAvailable) shown++;
                }
                section.IsVisible = shown > 0 || query.Length == 0;
                any |= section.IsVisible;
            }
            NoResults = !any;
        }
    }
}
