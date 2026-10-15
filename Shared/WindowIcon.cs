using System.Runtime.InteropServices;

namespace VuluShared
{
    /// <summary>
    /// Pencere ikonunu doğrudan Windows'a verir. Photino'nun SetIconFile'ı çerçevesiz pencerede görev
    /// çubuğuna ikon taşımıyor (görev çubuğunda boş Windows ikonu görünüyordu). Büyük ikon görev çubuğu ve
    /// Alt+Tab, küçük ikon pencere listesi içindir; ikisi de .ico içinden ekranın ölçeğine uygun boyutta seçilir.
    /// </summary>
    public static class WindowIcon
    {
        public static void Apply(IntPtr hwnd, string icoPath)
        {
            if (hwnd == IntPtr.Zero || !File.Exists(icoPath)) return;
            try
            {
                IntPtr big = LoadImage(IntPtr.Zero, icoPath, ImageIcon,
                    GetSystemMetrics(SmCxIcon), GetSystemMetrics(SmCyIcon), LrLoadFromFile);
                IntPtr small = LoadImage(IntPtr.Zero, icoPath, ImageIcon,
                    GetSystemMetrics(SmCxSmIcon), GetSystemMetrics(SmCySmIcon), LrLoadFromFile);

                if (big != IntPtr.Zero)
                {
                    SendMessage(hwnd, WmSetIcon, (IntPtr)IconBig, big);
                    SetClassLongPtr(hwnd, GclpHIcon, big);
                }
                if (small != IntPtr.Zero)
                {
                    SendMessage(hwnd, WmSetIcon, (IntPtr)IconSmall, small);
                    SetClassLongPtr(hwnd, GclpHIconSm, small);
                }
            }
            catch
            {
                // İkon konamazsa pencere yine açılır
            }
        }

        private const uint ImageIcon = 1;
        private const uint LrLoadFromFile = 0x10;
        private const uint WmSetIcon = 0x0080;
        private const int IconSmall = 0, IconBig = 1;
        private const int SmCxIcon = 11, SmCyIcon = 12, SmCxSmIcon = 49, SmCySmIcon = 50;
        private const int GclpHIcon = -14, GclpHIconSm = -34;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint load);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
        private static extern IntPtr SetClassLongPtr(IntPtr hwnd, int index, IntPtr value);
    }
}
