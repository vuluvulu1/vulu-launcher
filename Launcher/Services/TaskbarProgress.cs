using System.Runtime.InteropServices;

namespace VuluLauncher
{
    /// <summary>
    /// Görev çubuğundaki launcher simgesinde indirme ilerlemesi (Steam ve tarayıcılardaki gibi): launcher arkadayken de
    /// ne kadar kaldığı görünür. Duraklatılınca sarı, hata olunca kırmızı; bitince kalkar. Windows'un ITaskbarList3'ü.
    /// </summary>
    internal static class TaskbarProgress
    {
        public enum State { None = 0, Indeterminate = 1, Normal = 2, Error = 4, Paused = 8 }

        private static ITaskbarList3? _list;
        private static bool _failed;

        public static void Set(IntPtr hwnd, State state, double percent = 0)
        {
            if (hwnd == IntPtr.Zero || _failed || !OperatingSystem.IsWindows()) return;
            try
            {
                if (_list == null)
                {
                    _list = (ITaskbarList3)new TaskbarInstance();
                    _list.HrInit();
                }
                _list.SetProgressState(hwnd, state);
                if (state is State.Normal or State.Paused or State.Error)
                    _list.SetProgressValue(hwnd, (ulong)Math.Clamp(Math.Round(percent * 10), 0, 1000), 1000);
            }
            catch { _failed = true; }   // görev çubuğu yoksa (ör. kabuk farklı) sessizce vazgeçilir
        }

        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
        private class TaskbarInstance { }

        [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            // ITaskbarList
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
            // ITaskbarList2
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
            // ITaskbarList3
            void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
            void SetProgressState(IntPtr hwnd, State state);
        }
    }
}
