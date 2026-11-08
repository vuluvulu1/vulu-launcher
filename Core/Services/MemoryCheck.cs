using System.Runtime.InteropServices;

namespace VuluLauncher
{
    /// <summary>
    /// Oyunu açmadan önce seçilen RAM'in bilgisayara sığıp sığmadığına bakar. Java ayrılan belleği oyun sırasında
    /// Windows'tan ister; Windows'un verebileceği (boş RAM + sanal bellekte kalan yer, "commit") yetmezse oyun
    /// "Native memory allocation failed" ile çöker. Sığmayacaksa RAM o açılış için sığan en yüksek değere düşürülür
    /// ve nedeni yazılır; ayardaki seçim değişmez.
    ///
    /// Sanal bellek (pagefile) Windows tarafından yönetiliyorsa ya da üst sınırı daha büyükse, Windows onu gerektikçe
    /// diskteki boş yer kadar büyütür; o pay da sayılır. Sayılmazsa küçük pagefile'lı bilgisayarlarda RAM boşuna düşer
    /// (8 GB seçilip 3 GB ile açılır) ve büyük paketler bu kez heap dolup çöker.
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
        /// <summary>Pagefile büyürken diskte boş bırakılan pay (Windows diski tamamen doldurmaz, güncellemeler de yer ister).</summary>
        private const double DiskReserveGb = 2;
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
            int byCommit = (int)Math.Floor(status.AvailCommitGb + status.GrowableGb - overhead);
            int byPhysical = (int)Math.Floor(status.TotalGb - SystemReserveGb);
            int limit = Math.Min(byCommit, byPhysical);
            if (requestedGb <= limit) return new(requestedGb, null);

            string reason = byCommit < byPhysical
                ? $"şu an yalnızca yaklaşık {status.AvailCommitGb + status.GrowableGb:0.#} GB boş bellek var (RAM + sanal bellek)"
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

        /// <param name="GrowableGb">Pagefile'ın diskteki boş yerle daha ne kadar büyüyebileceği.</param>
        public sealed record Status(double TotalGb, double AvailCommitGb, double GrowableGb = 0);

        public static Status? Read()
        {
            var info = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref info)) return null;
            const double Gb = 1024d * 1024 * 1024;
            double total = info.ullTotalPhys / Gb;
            // Commit sınırı = RAM + bütün pagefile'ların şu anki boyu
            double pagefileNow = Math.Max(0, (info.ullTotalPageFile - (double)info.ullTotalPhys) / Gb);
            double growable = 0;
            try { if (OperatingSystem.IsWindows()) growable = PagefileGrowableGb(total, pagefileNow); } catch { /* okunamazsa büyüme sayılmaz */ }
            return new(total, info.ullAvailPageFile / Gb, growable);
        }

        /// <summary>
        /// Pagefile ayarına göre büyüme payı. "?:\pagefile.sys" ya da "C:\pagefile.sys 0 0" Windows'un yönettiği
        /// pagefile'dır (üst sınırı RAM'in 3 katı ve diskin sekizde biri); "C:\pagefile.sys 2048 8192" elle verilmiş
        /// sınırdır. Pay, o diskte boş bırakılması gereken yeri aşmaz.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static double PagefileGrowableGb(double totalRamGb, double pagefileNowGb)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            if (key?.GetValue("PagingFiles") is not string[] entries) return 0;
            const double Gb = 1024d * 1024 * 1024;
            double maxTotal = 0;
            double diskRoom = 0;
            foreach (var raw in entries)
            {
                var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || !parts[0].EndsWith("pagefile.sys", StringComparison.OrdinalIgnoreCase)) continue;
                string drive = parts[0].StartsWith("?:", StringComparison.Ordinal)
                    ? Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"
                    : parts[0][..3];
                var disk = new DriveInfo(drive);
                bool managed = parts.Length < 3 || (parts[1] == "0" && parts[2] == "0");
                double max = managed
                    ? Math.Min(3 * totalRamGb, disk.TotalSize / Gb / 8)
                    : double.TryParse(parts[2], out var mb) ? mb / 1024 : 0;
                maxTotal += max;
                diskRoom += Math.Max(0, disk.AvailableFreeSpace / Gb - DiskReserveGb);
            }
            return Math.Max(0, Math.Min(maxTotal - pagefileNowGb, diskRoom));
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
