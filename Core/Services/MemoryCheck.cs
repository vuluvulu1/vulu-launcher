using System.Runtime.InteropServices;

namespace VuluLauncher
{
    /// <summary>
    /// Oyunu açmadan önce seçilen RAM'in bilgisayara sığıp sığmadığına bakar. Java ayrılan belleği oyun sırasında
    /// Windows'tan ister; Windows'un verebileceği (boş RAM + sanal bellekte kalan yer, "commit") yetmezse oyun
    /// "Native memory allocation failed" ile çöker. Sığmayacaksa RAM o açılış için sığan en yüksek değere düşürülür
    /// ve nedeni yazılır; ayardaki seçim değişmez.
    /// </summary>
    internal static class MemoryCheck
    {
        /// <summary>Java'nın heap dışında kullandığı bellek (sınıflar, derlenmiş kod, GC); modlu oyunda daha fazla.</summary>
        private const double ModdedOverheadGb = 1.5, VanillaOverheadGb = 0.75;
        /// <summary>Windows ve diğer programlar için RAM'de bırakılan pay.</summary>
        private const double SystemReserveGb = 2.5;
        private const int MinimumGb = 2;

        public sealed record Result(int RamGb, string? Warning);

        public static Result Fit(int requestedGb, bool modded)
        {
            if (!OperatingSystem.IsWindows() || Read() is not { } status) return new(requestedGb, null);
            return Fit(requestedGb, modded, status, SystemDriveFreeGb());
        }

        /// <summary>Hesabın kendisi (ölçülen değerlerle; testlerde de kullanılır).</summary>
        internal static Result Fit(int requestedGb, bool modded, Status status, double? systemDriveFreeGb)
        {
            double overhead = modded ? ModdedOverheadGb : VanillaOverheadGb;
            int byCommit = (int)Math.Floor(status.AvailCommitGb - overhead);
            int byPhysical = (int)Math.Floor(status.TotalGb - SystemReserveGb);
            int limit = Math.Min(byCommit, byPhysical);
            if (requestedGb <= limit) return new(requestedGb, null);

            string reason = byCommit < byPhysical
                ? $"şu an yalnızca yaklaşık {status.AvailCommitGb:0.#} GB boş bellek var (RAM + sanal bellek)"
                : $"bilgisayarda toplam {status.TotalGb:0.#} GB RAM var, Windows'a da yer kalmalı";
            string hint = byCommit < byPhysical ? " Açık programları (tarayıcı, Discord vb.) kapatmak işe yarar." : "";
            if (byCommit < byPhysical && systemDriveFreeGb is < 5 and var free)
                hint += $" Sistem diskinde yalnızca {free:0.#} GB yer kaldığı için Windows sanal belleği büyütemiyor; orada yer açmak da işe yarar.";

            if (limit >= MinimumGb)
                return new(limit, $"{requestedGb} GB yerine {limit} GB ile açılıyor: {reason}.{hint}");
            return new(MinimumGb, $"Bellek çok az: {reason}. Oyun {MinimumGb} GB ile deneniyor ama açılmayabilir.{hint}");
        }

        public sealed record Status(double TotalGb, double AvailCommitGb);

        public static Status? Read()
        {
            var info = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref info)) return null;
            const double Gb = 1024d * 1024 * 1024;
            return new(info.ullTotalPhys / Gb, info.ullAvailPageFile / Gb);
        }

        private static double? SystemDriveFreeGb()
        {
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                return new DriveInfo(root).AvailableFreeSpace / (1024d * 1024 * 1024);
            }
            catch { return null; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX info);
    }
}
