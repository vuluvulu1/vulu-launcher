using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VuluLauncher
{
    /// <summary>
    /// Launcher bildirim alanındayken bilgisayara yük olmasın: launcher ve arayüzü çizen WebView2 süreçleri Windows'un
    /// verimlilik moduna (EcoQoS + düşük öncelik) alınır, bellekleri Windows'a geri verilir (çalışma kümesi boşaltılır;
    /// pencere açılınca gereken sayfalar geri yüklenir). Pencere geri gelince normale döner.
    /// Oyun (java) süreçlerine dokunulmaz: yalnızca bu süreç ve onun açtığı msedgewebview2 süreçleri etkilenir.
    /// </summary>
    internal static class Efficiency
    {
        public static void Enter() => Apply(eco: true);
        public static void Leave() => Apply(eco: false);
        /// <summary>Gizliyken arka plandaki işler belleği yavaşça geri doldurur; arada bir yeniden boşaltılır.</summary>
        public static void Trim() => Apply(eco: true, onlyTrim: true);

        private static void Apply(bool eco, bool onlyTrim = false)
        {
            try
            {
                int self = Environment.ProcessId;
                foreach (int pid in WebViewProcesses(self).Prepend(self))
                {
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        if (!onlyTrim)
                        {
                            SetEcoQoS(process.Handle, eco);
                            process.PriorityClass = eco ? ProcessPriorityClass.Idle : ProcessPriorityClass.Normal;
                        }
                        if (eco) EmptyWorkingSet(process.Handle);
                    }
                    catch { /* süreç kapanmış ya da erişilemiyor: geç */ }
                }
            }
            catch { }
        }

        /// <summary>Bu sürecin altındaki (torunlar dahil) msedgewebview2.exe süreçleri.</summary>
        private static IEnumerable<int> WebViewProcesses(int root)
        {
            var parents = new Dictionary<int, int>();
            var names = new Dictionary<int, string>();
            IntPtr snapshot = CreateToolhelp32Snapshot(0x2 /* TH32CS_SNAPPROCESS */, 0);
            if (snapshot == new IntPtr(-1)) return Array.Empty<int>();
            try
            {
                var entry = new PROCESSENTRY32 { dwSize = Marshal.SizeOf<PROCESSENTRY32>() };
                for (bool ok = Process32First(snapshot, ref entry); ok; ok = Process32Next(snapshot, ref entry))
                {
                    parents[entry.th32ProcessID] = entry.th32ParentProcessID;
                    names[entry.th32ProcessID] = entry.szExeFile;
                }
            }
            finally { CloseHandle(snapshot); }

            var result = new List<int>();
            foreach (var (pid, name) in names)
            {
                if (!name.Equals("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase)) continue;
                // Ata zincirinde bu süreç var mı (döngüye karşı en fazla 8 adım)
                int current = pid;
                for (int depth = 0; depth < 8 && parents.TryGetValue(current, out int parent) && parent != 0; depth++)
                {
                    if (parent == root) { result.Add(pid); break; }
                    current = parent;
                }
            }
            return result;
        }

        private static void SetEcoQoS(IntPtr process, bool on)
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = 1,
                ControlMask = 0x1,            // PROCESS_POWER_THROTTLING_EXECUTION_SPEED
                StateMask = on ? 0x1u : 0u,   // açık: verimlilik modu; kapalı: normal hız
            };
            SetProcessInformation(process, 4 /* ProcessPowerThrottling */, ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public int dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public int th32ModuleID, cntThreads, th32ParentProcessID, pcPriClassBase, dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);
        [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(int flags, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
