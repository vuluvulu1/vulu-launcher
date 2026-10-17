using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Instance kitaplığı araçları: oynama süresi, ekran görüntüsü galerisi ve kurulu vulu pack'lerin
    /// güncelleme denetimi.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private static readonly string[] ScreenshotExtensions = { ".png", ".jpg", ".jpeg" };
        private const int ScreenshotLimit = 1000;
        private static readonly TimeSpan PackCheckInterval = TimeSpan.FromMinutes(20);

        private readonly object _playGate = new();
        private string? _playId;
        private DateTime _playMark;
        private Timer? _playTimer;

        private bool _packChecking;
        private readonly HashSet<string> _packUpdatesNotified = new();

        private void RegisterLibraryCommands()
        {
            _bridge.On("screenshots.list", async args => await Task.Run(() => ListScreenshots(args.Str("id"))));
            _bridge.On("screenshots.open", args => Task.FromResult<object?>(OpenScreenshot(args.Str("id"), args.Str("name"), reveal: false)));
            _bridge.On("screenshots.reveal", args => Task.FromResult<object?>(OpenScreenshot(args.Str("id"), args.Str("name"), reveal: true)));
            _bridge.On("screenshots.copy", args => Task.FromResult<object?>(CopyScreenshot(args.Str("id"), args.Str("name"))));
            _bridge.On("screenshots.delete", args => Task.FromResult<object?>(DeleteScreenshot(args.Str("id"), args.Str("name"))));
            _bridge.On("packs.updates", async _ => await PackUpdatesAsync());
        }

        // ════════════════════════════════════════════════════════════════
        // OYNAMA SÜRESİ
        // ════════════════════════════════════════════════════════════════
        /// <summary>Oyun açıldı: süre dakikada bir kaydedilir (launcher beklenmedik kapanırsa da kaybolmasın).</summary>
        private void StartPlayTime(string instanceId)
        {
            StopPlayTime();
            lock (_playGate)
            {
                _playId = instanceId;
                _playMark = DateTime.UtcNow;
                PlayTimeStore.Add(instanceId, TimeSpan.Zero, newSession: true);
                _playTimer = new Timer(_ => FlushPlayTime(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            }
        }

        private void FlushPlayTime()
        {
            lock (_playGate)
            {
                if (_playId == null) return;
                var now = DateTime.UtcNow;
                try { PlayTimeStore.Add(_playId, now - _playMark, newSession: false); _playMark = now; }
                catch { /* bir sonraki dakikada yeniden denenir */ }
            }
        }

        private void StopPlayTime()
        {
            FlushPlayTime();
            lock (_playGate)
            {
                _playTimer?.Dispose();
                _playTimer = null;
                _playId = null;
            }
        }

        private static object PlayTimeOf(string id)
        {
            var entry = PlayTimeStore.Get(id);
            return new { seconds = entry?.Seconds ?? 0, sessions = entry?.Sessions ?? 0, lastPlayed = entry?.LastPlayed?.ToString("o") };
        }

        // ════════════════════════════════════════════════════════════════
        // EKRAN GÖRÜNTÜLERİ
        // ════════════════════════════════════════════════════════════════
        private string ScreenshotDirOf(string id) => Path.Combine(InstanceDirOf(id), "screenshots");

        /// <summary>Galerideki bir dosya: tek seviye, yalnızca resim uzantıları.</summary>
        private string ScreenshotPath(string id, string name)
        {
            RequireLogin();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !ScreenshotExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
                throw new InvalidOperationException("Geçersiz dosya adı.");
            string path = Path.Combine(ScreenshotDirOf(id), name);
            if (!File.Exists(path)) throw new InvalidOperationException("Ekran görüntüsü bulunamadı.");
            return path;
        }

        private object ListScreenshots(string id)
        {
            RequireLogin();
            string dir = ScreenshotDirOf(id);
            var files = !Directory.Exists(dir) ? new List<FileInfo>() : new DirectoryInfo(dir).EnumerateFiles()
                .Where(f => ScreenshotExtensions.Contains(f.Extension.ToLowerInvariant()))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            return new
            {
                total = files.Count,
                totalMb = Math.Round(files.Sum(f => f.Length) / 1048576.0, 1),
                shots = files.Take(ScreenshotLimit).Select(f => new
                {
                    name = f.Name,
                    url = $"{new Uri(f.FullName).AbsoluteUri}?v={f.LastWriteTimeUtc.Ticks}",
                    takenAt = f.LastWriteTimeUtc.ToString("o"),
                    sizeKb = (int)Math.Ceiling(f.Length / 1024.0),
                }),
            };
        }

        private object OpenScreenshot(string id, string name, bool reveal)
        {
            string path = ScreenshotPath(id, name);
            if (reveal) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path } });
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return new { opened = true };
        }

        /// <summary>
        /// Dosyayı panoya kopyalar (Gezgin'deki "Kopyala" gibi): Discord'a, sohbete ya da bir klasöre yapıştırılabilir.
        /// </summary>
        private object CopyScreenshot(string id, string name)
        {
            string path = ScreenshotPath(id, name);
            if (!FileClipboard.SetFiles(new[] { path })) throw new InvalidOperationException("Pano şu an kullanılamıyor, tekrar dene.");
            return new { copied = true };
        }

        /// <summary>Geri dönüşüm kutusuna taşır (kalıcı silmez).</summary>
        private object DeleteScreenshot(string id, string name)
        {
            string path = ScreenshotPath(id, name);
            if (!FileClipboard.Recycle(path)) throw new InvalidOperationException("Ekran görüntüsü silinemedi.");
            return new { deleted = true };
        }

        // ════════════════════════════════════════════════════════════════
        // VULU PACK GÜNCELLEMELERİ
        // ════════════════════════════════════════════════════════════════
        /// <summary>Kurulu vulu pack'lerden yeni revizyonu yayınlanmış olanlar.</summary>
        private async Task<object?> PackUpdatesAsync()
        {
            RequireLogin();
            var installed = InstanceStore.List().Where(m => m.SharedPackId != null).ToList();
            if (installed.Count == 0) return new { updates = Array.Empty<object>() };
            var packs = (await Packs.ListAsync()).ToDictionary(p => p.Id);
            var updates = installed
                .Where(m => packs.TryGetValue(m.SharedPackId!, out var p) && p.Revision > m.SharedPackRevision)
                .Select(m =>
                {
                    var p = packs[m.SharedPackId!];
                    return new { instanceId = InstancePrefix + m.Folder, packId = p.Id, name = p.Name, from = m.SharedPackRevision, to = p.Revision };
                })
                .ToList();
            return new { updates };
        }

        /// <summary>Girişten sonra ve sonra her 20 dakikada bir: yeni güncelleme varsa arayüze ve (launcher arkadaysa) bildirime.</summary>
        private void StartPackUpdateChecks()
        {
            lock (_packUpdatesNotified)
            {
                if (_packChecking) return;
                _packChecking = true;
            }
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(20));   // girişteki diğer isteklerin önüne geçmesin
                while (true)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(_login.Token))
                        {
                            var result = JsonSerializer.SerializeToElement(await PackUpdatesAsync());
                            var updates = result.GetProperty("updates").EnumerateArray().ToList();
                            _bridge.Emit("packUpdates", result);
                            foreach (var u in updates)
                            {
                                string key = $"{u.GetProperty("packId").GetString()}@{u.GetProperty("to").GetInt32()}";
                                bool fresh;
                                lock (_packUpdatesNotified) fresh = _packUpdatesNotified.Add(key);
                                if (fresh && AppSettings.Current.PackUpdateNotifications && !TrayNotifier.IsActive(_window.WindowHandle))
                                    _tray.Show($"{u.GetProperty("name").GetString()} güncellendi",
                                        $"Paketin yeni sürümü (rev {u.GetProperty("to").GetInt32()}) hazır. Güncellemek için tıkla.",
                                        "pack:" + u.GetProperty("instanceId").GetString());
                            }
                        }
                    }
                    catch (Exception) { /* bağlantı yoksa bir sonraki turda */ }
                    await Task.Delay(PackCheckInterval);
                }
            });
        }
    }

    /// <summary>Panoya dosya koyma (CF_HDROP) ve geri dönüşüm kutusuna taşıma.</summary>
    internal static class FileClipboard
    {
        public static bool SetFiles(IReadOnlyList<string> paths)
        {
            // DROPFILES başlığı + çift sıfırla biten UTF-16 dosya listesi
            string list = string.Join('\0', paths) + "\0\0";
            byte[] chars = System.Text.Encoding.Unicode.GetBytes(list);
            const int headerSize = 20;
            IntPtr mem = GlobalAlloc(0x0042 /* GMEM_MOVEABLE | GMEM_ZEROINIT */, (UIntPtr)(headerSize + chars.Length));
            if (mem == IntPtr.Zero) return false;
            IntPtr ptr = GlobalLock(mem);
            Marshal.WriteInt32(ptr, 0, headerSize);   // pFiles
            Marshal.WriteInt32(ptr, 16, 1);           // fWide
            Marshal.Copy(chars, 0, ptr + headerSize, chars.Length);
            GlobalUnlock(mem);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        EmptyClipboard();
                        if (SetClipboardData(15 /* CF_HDROP */, mem) != IntPtr.Zero) return true;   // bellek artık panonun
                    }
                    finally { CloseClipboard(); }
                    break;
                }
                Thread.Sleep(50);   // başka bir uygulama panoyu tutuyor
            }
            GlobalFree(mem);
            return false;
        }

        public static bool Recycle(string path)
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = 3, // FO_DELETE
                pFrom = path + "\0\0",
                fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400, // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
            };
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted && !File.Exists(path);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string? pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr mem);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr mem);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr mem);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr mem);
    }
}
