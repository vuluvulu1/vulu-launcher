using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VuluShared
{
    /// <summary>
    /// Kurulumun Windows'taki izleri: klasör, kısayollar ve "Program ekle/kaldır" kaydı. Kurulum programı bunları
    /// oluşturur, Updater'ın kaldırma modu siler. Her şey o kullanıcıya özeldir; yönetici izni gerekmez.
    /// </summary>
    public static class InstallInfo
    {
        public const string AppName = "vulu Launcher";
        /// <summary>
        /// Kurulum klasörünün adı. Boşluksuz: oyun dosyaları da bu klasörde durur ve bazı mod yükleyiciler/modlar
        /// boşluklu yollarda sorun çıkarıyor.
        /// </summary>
        public const string FolderName = "vuluLauncher";
        public const string Publisher = "vulu";
        public const string UninstallSwitch = "--uninstall";

        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\vuluLauncher";

        /// <summary>Varsayılan klasör. Oyun dosyaları da burada durduğu için kurulumda değiştirilebilir.</summary>
        public static string DefaultDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", FolderName);

        public static string StartMenuShortcut => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

        public static string DesktopShortcut => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

        /// <summary>Daha önce kurulduysa klasörü (kayıttan); yoksa null.</summary>
        public static string? InstalledDirectory()
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") as string is { Length: > 0 } dir && Directory.Exists(dir) ? dir : null;
        }

        public static void WriteUninstallEntry(string installDir, string version)
        {
            string launcher = Path.Combine(installDir, Releases.LauncherExeName);
            string updater = Path.Combine(installDir, Releases.UpdaterExeName);

            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", Releases.Clean(version));
            key.SetValue("Publisher", Publisher);
            key.SetValue("InstallLocation", installDir);
            key.SetValue("DisplayIcon", launcher);
            key.SetValue("UninstallString", $"\"{updater}\" {UninstallSwitch}");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(DirectorySize(installDir) / 1024), RegistryValueKind.DWord);
        }

        /// <summary>Updater her güncellemede sürümü günceller ki "Program ekle/kaldır" doğru sürümü göstersin.</summary>
        public static void UpdateRegisteredVersion(string version)
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey, writable: true);
            key?.SetValue("DisplayVersion", Releases.Clean(version));
        }

        public static void RemoveUninstallEntry() =>
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

        public static void CreateShortcut(string shortcutPath, string target, string description, string? arguments = null, string? iconPath = null)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(target);
                link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
                link.SetDescription(description);
                if (!string.IsNullOrEmpty(arguments)) link.SetArguments(arguments);
                link.SetIconLocation(iconPath ?? target, 0);

                Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
                ((IPersistFile)link).Save(shortcutPath, true);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }

        /// <summary>Başlat menüsü ve masaüstü kısayollarını siler.</summary>
        public static void RemoveShortcuts()
        {
            foreach (var path in new[] { StartMenuShortcut, DesktopShortcut })
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch { /* kilitliyse kalır */ }
            }
        }

        private static long DirectorySize(string dir)
        {
            try
            {
                return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }

        // Kısayol dosyası yazmanın desteklenen tek yolu kabuğun COM arayüzüdür.
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink;

        [ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID();
            void IsDirty();
            void Load();
            void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
        }

        // Yalnızca kullanılan yöntemler adlandırıldı; diğerleri arayüzdeki sıralarını korumak için durur.
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath();
            void GetIDList();
            void SetIDList();
            void GetDescription();
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
            void GetWorkingDirectory();
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments();
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey();
            void SetHotkey();
            void GetShowCmd();
            void SetShowCmd();
            void GetIconLocation();
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string file, int index);
            void SetRelativePath();
            void Resolve();
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string target);
        }
    }
}
