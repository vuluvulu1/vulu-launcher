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
        /// <summary>
        /// Java'nın heap dışında kullandığı bellek (sınıflar, derlenmiş kod, GC, mod kütüphaneleri). Mod sayısıyla büyür:
        /// 632 modlu bir pakette ölçülen ~4 GB (heap 8 GB, sürecin toplamı 12,1 GB).
        /// </summary>
        private const double ModdedOverheadGb = 1.5, PerModOverheadGb = 2.75 / 650, MaxModdedOverheadGb = 4.5, VanillaOverheadGb = 0.75;
        /// <summary>Windows ve diğer programlar için RAM'de bırakılan pay.</summary>
        private const double SystemReserveGb = 2.5;
        private const int MinimumGb = 2;

        /// <param name="Tight">Sığan RAM paketin rahat açılması için gerekenden az: oyun bellek yetmeyip kapanabilir.</param>
        public sealed record Result(int RamGb, string? Warning, bool Tight = false);

        public static Result Fit(int requestedGb, bool modded, int modCount = 0)
        {
            if (!OperatingSystem.IsWindows() || Read() is not { } status) return new(requestedGb, null);
            return Fit(requestedGb, modded, status, SystemDriveFreeGb(), modCount);
        }

        public static double OverheadGb(bool modded, int modCount) =>
            modded ? Math.Min(MaxModdedOverheadGb, ModdedOverheadGb + PerModOverheadGb * Math.Max(0, modCount)) : VanillaOverheadGb;

        /// <summary>Paketin rahat açılması için gereken en az heap (büyük paketler 3 GB ile yükleme sırasında boğulur).</summary>
        public static int MinimumHeapGb(bool modded, int modCount) =>
            !modded ? MinimumGb : modCount >= 400 ? 6 : modCount >= 200 ? 5 : modCount >= 100 ? 4 : 3;

        /// <summary>Hesabın kendisi (ölçülen değerlerle; testlerde de kullanılır).</summary>
        internal static Result Fit(int requestedGb, bool modded, Status status, double? systemDriveFreeGb, int modCount = 0)
        {
            double overhead = OverheadGb(modded, modCount);
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

            if (limit < MinimumGb)
                return new(MinimumGb, $"Bellek çok az: {reason}. Oyun {MinimumGb} GB ile deneniyor ama açılmayabilir.{hint}", Tight: true);
            int need = Math.Min(requestedGb, MinimumHeapGb(modded, modCount));
            int ram = limit;
            if (ram < need)
            {
                string pack = modCount > 0 ? $"Bu paket ({modCount} mod) en az {need} GB ister" : $"Oyun en az {need} GB ister";
                return new(ram, $"{pack}, ama {reason}. {ram} GB ile deneniyor; açılırken ya da oynarken bellek yetmeyip kapanabilir.{hint}", Tight: true);
            }
            return new(ram, $"{requestedGb} GB yerine {ram} GB ile açılıyor: {reason}.{hint}");
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
