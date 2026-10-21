using Android.Content;

namespace VuluLauncher
{
    /// <summary>
    /// Açılışta çöken uygulama hiçbir şey göstermeden kapanır. Yakalanmayan hata bir dosyaya yazılır ve bir sonraki
    /// açılışta kullanıcıya gösterilir; metin kopyalanıp geliştiriciye iletilebilir.
    /// </summary>
    internal static class CrashLog
    {
        private static string FilePath =>
            Path.Combine(Android.App.Application.Context.FilesDir!.AbsolutePath, "last-crash.txt");

        public static void Attach()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(e.ExceptionObject as Exception);
            Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Write(e.Exception);
        }

        public static void Write(Exception? exception)
        {
            if (exception == null) return;
            try
            {
                string version = LauncherUpdateService.LocalVersion;
                File.WriteAllText(FilePath, $"vulu Launcher {version} · Android {Android.OS.Build.VERSION.Release} · " +
                    $"{Android.OS.Build.Manufacturer} {Android.OS.Build.Model}\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n{exception}");
            }
            catch { /* kayıt da yazılamıyorsa yapacak bir şey yok */ }
        }

        /// <summary>Önceki çökme kaydı varsa gösterir ve siler.</summary>
        public static void ShowPrevious(Android.App.Activity activity)
        {
            string text;
            try
            {
                if (!File.Exists(FilePath)) return;
                text = File.ReadAllText(FilePath);
                File.Delete(FilePath);
            }
            catch { return; }

            new AndroidX.AppCompat.App.AlertDialog.Builder(activity)
                .SetTitle("vulu Launcher beklenmedik şekilde kapandı")!
                .SetMessage(text.Length > 4000 ? text[..4000] + "…" : text)!
                .SetPositiveButton("Kopyala", (_, _) =>
                {
                    var clipboard = (ClipboardManager?)activity.GetSystemService(Context.ClipboardService);
                    clipboard?.SetPrimaryClip(ClipData.NewPlainText("vulu crash", text));
                })!
                .SetNegativeButton("Kapat", (_, _) => { })!
                .Show();
        }
    }
}
