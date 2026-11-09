using System.Globalization;

namespace VuluMobile.ViewModels
{
    /// <summary>Kısa zaman yazıları: "şimdi", "5 dk", "14:32", "dün", "3 Kas".</summary>
    internal static class When
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        public static string Short(DateTime local)
        {
            var now = DateTime.Now;
            var ago = now - local;
            if (ago < TimeSpan.FromMinutes(1)) return "şimdi";
            if (ago < TimeSpan.FromHours(1)) return $"{(int)ago.TotalMinutes} dk";
            if (local.Date == now.Date) return local.ToString("HH:mm", Tr);
            if (local.Date == now.Date.AddDays(-1)) return "dün";
            return local.ToString(local.Year == now.Year ? "d MMM" : "d MMM yyyy", Tr);
        }

        /// <summary>Sohbetteki mesaj saati.</summary>
        public static string Clock(DateTime local) =>
            local.Date == DateTime.Now.Date ? local.ToString("HH:mm", Tr) : local.ToString("d MMM HH:mm", Tr);

        /// <summary>"2 saattir" gibi süre (oyunda olma).</summary>
        public static string Since(DateTime local)
        {
            var d = DateTime.Now - local;
            if (d < TimeSpan.FromMinutes(1)) return "az önce başladı";
            if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes} dakikadır";
            return $"{(int)d.TotalHours} saattir";
        }
    }
}
