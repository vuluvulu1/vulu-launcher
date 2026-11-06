using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VuluLauncher
{
    /// <summary>
    /// Launcher bildirim alanındayken bilgisayara yük olmasın: launcher süreci Windows'un verimlilik moduna (EcoQoS +
    /// düşük öncelik) alınır, belleği Windows'a geri verilir (çalışma kümesi boşaltılır; pencere açılınca gereken
    /// sayfalar geri yüklenir). Pencere geri gelince normale döner. Oyun (java) süreçlerine dokunulmaz.
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
                using var process = Process.GetCurrentProcess();
                if (!onlyTrim)
                {
                    SetEcoQoS(process.Handle, eco);
                    process.PriorityClass = eco ? ProcessPriorityClass.Idle : ProcessPriorityClass.Normal;
                }
                if (eco) EmptyWorkingSet(process.Handle);
            }
            catch { }
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

        [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);
        [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr process);
    }
}
