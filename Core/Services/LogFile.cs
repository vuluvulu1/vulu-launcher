namespace VuluLauncher
{
    /// <summary>
    /// Hata günlüğü: başarısız komutlar (yığın iziyle) ve konsoldaki hata/uyarı satırları
    /// %LOCALAPPDATA%\vuluLauncher\logs\launcher.log'a yazılır. Kullanıcı bir hatayı bildirdiğinde nedeni buradan
    /// görülür. Dosya 1 MB'ı geçince bir öncekinin yerine geçer (launcher.old.log).
    /// </summary>
    internal static class LogFile
    {
        private const long MaxBytes = 1024 * 1024;
        private static readonly object Gate = new();

        public static string Path => System.IO.Path.Combine(AppPaths.AppData, "logs", "launcher.log");

        public static void Write(string text)
        {
            try
            {
                lock (Gate)
                {
                    string path = Path;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                        File.Move(path, System.IO.Path.ChangeExtension(path, ".old.log"), overwrite: true);
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
                }
            }
            catch { /* günlük yazılamazsa launcher yine çalışır */ }
        }
    }
}
