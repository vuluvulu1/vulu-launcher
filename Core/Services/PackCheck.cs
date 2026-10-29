using System.IO.Compression;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    /// <summary>Paylaşmadan önceki denetimin bir bulgusu.</summary>
    /// <param name="Kind">duplicate | missing | override</param>
    internal sealed record PackIssue(string Kind, string Title, string Detail);

    /// <summary>
    /// Paylaşılacak instance'ın modlarını denetler: aynı modun iki sürümü, kurulu olmayan zorunlu bağımlılıklar ve
    /// oyunun aynı dünya oluşturma dosyasını değiştiren modlar (ör. Geophilic ile Terralith aynı biyom dosyalarını
    /// değiştirir; yalnızca biri geçerli olur). Yalnızca okur.
    /// </summary>
    internal static class PackCheck
    {
        // Oyunun kendi dünya oluşturma dosyaları: iki mod aynı dosyayı verirse biri diğerini tamamen ezer.
        // Etiketler (tags/) birleşir, onlar sayılmaz.
        private static readonly Regex WorldgenFile = new(@"^data/minecraft/(worldgen/[a-z0-9_/.\-]+|dimension(_type)?/[a-z0-9_/.\-]+)\.json$", RegexOptions.Compiled);
        private const int MaxIssues = 12;

        public static IReadOnlyList<PackIssue> Run(string instanceDir, string loader)
        {
            if (loader == "vanilla") return Array.Empty<PackIssue>();
            var scan = ModScanner.Scan(instanceDir, loader);
            var issues = new List<PackIssue>();

            // ── Aynı mod iki dosya ────────────────────────────────────────
            foreach (var group in scan.Mods
                .SelectMany(m => m.PrimaryIds.Select(id => (Id: id, Mod: m)))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Select(x => x.Mod.File).Distinct().Count() > 1))
            {
                issues.Add(new PackIssue("duplicate", $"{group.First().Mod.Name} iki kez var",
                    $"Aynı modun birden çok dosyası: {string.Join(", ", group.Select(x => x.Mod.File).Distinct())}. Oyun açılmaz ya da yanlış sürümü yükler; eskisini sil."));
            }

            // ── Eksik bağımlılık ──────────────────────────────────────────
            foreach (var missing in scan.Missing)
                issues.Add(new PackIssue("missing", $"{missing.Id} eksik",
                    $"{string.Join(", ", missing.RequiredBy)} bu modu istiyor ama pakette yok. Kuranların oyunu açılmaz."));

            // ── Aynı dünya oluşturma dosyasını değiştiren modlar ──────────
            var owners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var mod in scan.Mods)
            {
                try
                {
                    using var zip = ZipFile.OpenRead(Path.Combine(instanceDir, "mods", mod.File));
                    foreach (var entry in zip.Entries)
                    {
                        if (!WorldgenFile.IsMatch(entry.FullName)) continue;
                        if (!owners.TryGetValue(entry.FullName, out var list)) owners[entry.FullName] = list = new List<string>();
                        if (!list.Contains(mod.Name)) list.Add(mod.Name);
                    }
                }
                catch (Exception) { /* okunamayan jar atlanır */ }
            }
            foreach (var clash in owners.Where(o => o.Value.Count > 1)
                .GroupBy(o => string.Join(" + ", o.Value.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)))
                .OrderByDescending(g => g.Count()))
            {
                var files = clash.Select(o => Path.GetFileNameWithoutExtension(o.Key)).Take(4).ToList();
                issues.Add(new PackIssue("override", $"{clash.Key} aynı dünya dosyalarını değiştiriyor",
                    $"{clash.Count()} dosyada ({string.Join(", ", files)}{(clash.Count() > files.Count ? "…" : "")}) yalnızca birinin değişikliği geçerli olur; "
                    + "diğerinin o kısmı boşa gider. Birlikte çalışmak için yapılmamışlarsa birini kaldır."));
            }

            return issues.Take(MaxIssues).ToList();
        }
    }
}
