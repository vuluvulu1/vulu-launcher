using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>
    /// Kurulmakta olan paket: kurulum başlar başlamaz kütüphanede kapağıyla durur, üstünde ilerleme halkası döner;
    /// bitince gerçek pakete dönüşür (HomeViewModel.Pending).
    /// </summary>
    internal sealed partial class PendingInstall(string name, Bitmap? cover) : ObservableObject
    {
        public string Name { get; } = name;
        public Bitmap? Cover { get; } = cover;
        public string Initial => Name.Length > 0 ? Name[..1].ToUpper(CultureInfo.GetCultureInfo("tr-TR")) : "?";

        /// <summary>0..1</summary>
        [ObservableProperty] private double _progress;
        [ObservableProperty] private bool _indeterminate = true;
        [ObservableProperty] private string _phase = "Bekliyor…";

        /// <summary>İlerlemesini izlediği indirme işi (çekirdeğin "download" olayı).</summary>
        internal int? JobId;
    }

    internal sealed partial class HomeViewModel
    {
        /// <summary>Kurulmakta olan paketler; kütüphanede kapakların sonunda gösterilir.</summary>
        public ObservableCollection<PendingInstall> Pending { get; } = new();

        public PendingInstall BeginInstall(string name, Bitmap? cover)
        {
            var pending = new PendingInstall(name, cover);
            Pending.Add(pending);
            return pending;
        }

        public void EndInstall(PendingInstall pending) => Pending.Remove(pending);

        /// <summary>
        /// İndirme işinin durumu kurulan pakete bağlanır: işin adı paketin adıyla başlıyorsa ("X kuruluyor") ya da bekleyen
        /// tek kurulum varsa. İş bitip yenisi başlarsa (ör. modpack, ardından yükleyici) yenisi izlenir.
        /// </summary>
        public void OnInstallDownload(JsonElement data)
        {
            if (Pending.Count == 0) return;
            int id = data.Int("id");
            string title = data.Text("title") ?? "";
            var pending = Pending.FirstOrDefault(p => p.JobId == id)
                ?? Pending.FirstOrDefault(p => p.JobId == null && title.StartsWith(p.Name, StringComparison.OrdinalIgnoreCase))
                ?? (Pending.Count(p => p.JobId == null) == 1 && title.EndsWith("kuruluyor", StringComparison.Ordinal) ? Pending.First(p => p.JobId == null) : null);
            if (pending == null) return;
            pending.JobId = id;

            if (data.Text("state") is { } state && state != "running")
            {
                pending.JobId = null;   // sıradaki iş (varsa) yeniden bağlanır
                pending.Indeterminate = true;
                pending.Phase = state == "done" ? "Hazırlanıyor…" : data.Text("message") ?? "Kurulamadı";
                return;
            }

            var bytes = data.Get("bytes");
            var files = data.Get("files");
            double done = bytes.Num("done"), total = bytes.Num("total");
            double ratio = total > 0 ? done / total : files.Num("total") > 0 ? files.Num("done") / files.Num("total") : -1;
            if (ratio < 0) { pending.Indeterminate = true; pending.Phase = data.Text("phase") is { Length: > 0 } p ? p : "Hazırlanıyor…"; return; }
            pending.Indeterminate = false;
            pending.Progress = Math.Clamp(ratio, 0, 1);
            string phase = data.Text("phase") is { Length: > 0 } ph ? ph : "İndiriliyor";
            pending.Phase = data.Bool("paused") ? "Duraklatıldı" : $"{phase} · %{Math.Round(pending.Progress * 100)}";
        }
    }
}
