using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>Kullanıcının launcher ayarları. %LocalAppData%\vuluLauncher\settings.json içinde saklanır.</summary>
    public sealed class AppSettings
    {
        // ── Oyun ──────────────────────────────────────────────────────────
        /// <summary>Paket için ayrıca seçim yapılmadıysa kullanılacak RAM. 0 = paketin önerdiği değer.</summary>
        public int DefaultRamGb { get; set; }

        /// <summary>"auto": gereken Java sürümü Adoptium'dan indirilir. "custom": CustomJavaPath kullanılır.</summary>
        public string JavaMode { get; set; } = "auto";
        public string CustomJavaPath { get; set; } = "";
        public string JvmArgs { get; set; } = "";

        /// <summary>0 = oyunun varsayılan pencere boyutu.</summary>
        public int GameWidth { get; set; }
        public int GameHeight { get; set; }
        public bool Fullscreen { get; set; }

        /// <summary>Launcher penceresinin boyutu: "" = ekrana göre otomatik, yoksa "1280x720" gibi piksel boyutu.</summary>
        public string WindowSize { get; set; } = "";

        /// <summary>Yazı ve arayüz boyutu, yüzde: 90 | 100 | 110 | 125. Pencerenin yakınlaştırmasıyla çarpılır.</summary>
        public int UiScale { get; set; } = 100;
        public static readonly int[] UiScales = { 90, 100, 110, 125 };

        // ── Launcher davranışı ────────────────────────────────────────────
        /// <summary>"keep" | "minimize" | "close"</summary>
        public string OnGameStart { get; set; } = "keep";
        public bool SkipSplash { get; set; }
        /// <summary>Kapat tuşu launcher'ı bildirim alanına küçültür (mesaj ve oyun bildirimleri gelmeye devam eder).</summary>
        public bool CloseToTray { get; set; } = true;
        /// <summary>Bilgisayar açılınca launcher pencere açmadan bildirim alanında başlar (mesaj ve bildirimler gelir).</summary>
        public bool StartWithSystem { get; set; } = true;
        /// <summary>Biri oyuna girince bildirim: "friends" (arkadaşlarım) | "all" (herkes) | "off".</summary>
        public string PlayingNotifications { get; set; } = "friends";
        public bool ConsoleExpanded { get; set; }
        /// <summary>Discord'da "vulu Launcher oynuyor" durumu.</summary>
        public bool DiscordPresence { get; set; } = true;
        /// <summary>Arkadaşlar ne görsün: "full" (ne oynadığım) | "online" (yalnızca çevrimiçi) | "hidden" (görünmez).</summary>
        public string PresenceMode { get; set; } = "full";
        /// <summary>Launcher öndeyken değilse yeni mesajlar Windows bildirimi olarak gösterilir.</summary>
        public bool DmNotifications { get; set; } = true;
        /// <summary>Bildirimde mesajın kendisi görünsün mü (kapalıysa yalnızca "yeni mesaj").</summary>
        public bool DmNotificationPreview { get; set; } = true;
        /// <summary>Kurulu bir vulu pack güncellenince launcher öndeyken değilse bildirim gösterilir.</summary>
        public bool PackUpdateNotifications { get; set; } = true;
        /// <summary>Ekran görüntüm beğenilince ya da yorum gelince bildirim.</summary>
        public bool SocialNotifications { get; set; } = true;
        /// <summary>Toplam oynama süresi ve en çok oynanan paketler profilde görünsün mü.</summary>
        public bool ShowPlaytimeOnProfile { get; set; } = true;

        // ── Ses ve görünüm ────────────────────────────────────────────────
        public bool MusicEnabled { get; set; } = true;
        public int MusicVolume { get; set; } = 40;
        public string AccentColor { get; set; } = "#c27e3e";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        private static readonly object Gate = new();
        private static AppSettings? _current;

        public static AppSettings Current
        {
            get
            {
                lock (Gate) return _current ??= Load();
            }
        }

        public static void Save(AppSettings settings)
        {
            settings.Normalize();
            lock (Gate)
            {
                _current = settings;
                Directory.CreateDirectory(AppPaths.AppData);
                File.WriteAllText(AppPaths.Settings, JsonSerializer.Serialize(settings, Json));
            }
        }

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(AppPaths.Settings))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings), Json);
                    if (loaded != null) { loaded.Normalize(); return loaded; }
                }
            }
            catch { /* bozuk dosya: varsayılanlara dön */ }

            return new AppSettings();
        }

        /// <summary>"1280x720" → (1280, 720); geçersizse ya da otomatikse null.</summary>
        public static (int Width, int Height)? ParseWindowSize(string? value)
        {
            var parts = (value ?? "").Split('x');
            return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)
                && w is >= 800 and <= 7680 && h is >= 500 and <= 4320 ? (w, h) : null;
        }

        /// <summary>Elle düzenlenmiş ya da arayüzden gelen değerleri güvenli aralıklara çeker.</summary>
        private void Normalize()
        {
            if (DefaultRamGb != 0 && !PackCatalog.RamOptionsGb.Contains(DefaultRamGb)) DefaultRamGb = 0;
            if (JavaMode != "custom") JavaMode = "auto";
            CustomJavaPath = (CustomJavaPath ?? "").Trim().Trim('"');
            JvmArgs = (JvmArgs ?? "").Trim();
            GameWidth = Math.Clamp(GameWidth, 0, 7680);
            GameHeight = Math.Clamp(GameHeight, 0, 4320);
            WindowSize = ParseWindowSize(WindowSize) is { } size ? $"{size.Width}x{size.Height}" : "";
            if (!UiScales.Contains(UiScale)) UiScale = 100;
            if (PlayingNotifications is not ("all" or "off")) PlayingNotifications = "friends";
            if (OnGameStart is not ("minimize" or "close")) OnGameStart = "keep";
            MusicVolume = Math.Clamp(MusicVolume, 0, 100);
            if (!System.Text.RegularExpressions.Regex.IsMatch(AccentColor ?? "", "^#[0-9a-fA-F]{6}$")) AccentColor = "#c27e3e";
        }
    }
}
