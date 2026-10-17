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

        // ── Launcher davranışı ────────────────────────────────────────────
        /// <summary>"keep" | "minimize" | "close"</summary>
        public string OnGameStart { get; set; } = "keep";
        public bool SkipSplash { get; set; }
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

        /// <summary>Elle düzenlenmiş ya da arayüzden gelen değerleri güvenli aralıklara çeker.</summary>
        private void Normalize()
        {
            if (DefaultRamGb != 0 && !PackCatalog.RamOptionsGb.Contains(DefaultRamGb)) DefaultRamGb = 0;
            if (JavaMode != "custom") JavaMode = "auto";
            CustomJavaPath = (CustomJavaPath ?? "").Trim().Trim('"');
            JvmArgs = (JvmArgs ?? "").Trim();
            GameWidth = Math.Clamp(GameWidth, 0, 7680);
            GameHeight = Math.Clamp(GameHeight, 0, 4320);
            if (OnGameStart is not ("minimize" or "close")) OnGameStart = "keep";
            MusicVolume = Math.Clamp(MusicVolume, 0, 100);
            if (!System.Text.RegularExpressions.Regex.IsMatch(AccentColor ?? "", "^#[0-9a-fA-F]{6}$")) AccentColor = "#c27e3e";
        }
    }
}
