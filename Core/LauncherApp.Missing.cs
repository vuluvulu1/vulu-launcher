using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Oyun açılmadan önce bulunan eksik bağımlılıklar: CurseForge'da ve Modrinth'te karşılığı aranır (instance'ın
    /// çoğu hangi kaynaktansa önce o), kullanıcı onaylarsa hepsi tek seferde kurulur. Kurulanların da eksik bağımlılığı
    /// çıkarsa onlar ayrıca sorulmadan kurulur.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private sealed record MissingMod(string Id, IReadOnlyList<string> RequiredBy, string? Source, string? ProjectId, string? Title, string? IconUrl, string? PageUrl);

        private const int MissingRounds = 3;

        private async Task<object?> MissingDependenciesAsync(string instanceId, LaunchTarget target)
        {
            if (target.Loader == "vanilla") return null;
            try
            {
                var scan = await Task.Run(() => ModScanner.Scan(Path.Combine(AppPaths.Instances, target.Folder), target.Loader));
                if (scan.Missing.Count == 0) return null;

                var meta = instanceId.StartsWith(InstancePrefix, StringComparison.Ordinal) ? InstanceStore.Get(instanceId[InstancePrefix.Length..]) : null;
                var resolved = new Dictionary<string, MissingMod>(StringComparer.OrdinalIgnoreCase);
                if (meta != null)
                {
                    var lookup = ResolveMissingAsync(meta, scan, scan.Missing);
                    if (await Task.WhenAny(lookup, Task.Delay(TimeSpan.FromSeconds(20))) == lookup) resolved = await lookup;
                }
                Log($"Eksik bağımlılık: {string.Join(", ", scan.Missing.Select(m => m.Id))}", "warn");
                return scan.Missing.Select(m =>
                {
                    resolved.TryGetValue(m.Id, out var r);
                    return new
                    {
                        id = m.Id, requiredBy = m.RequiredBy, source = r?.Source, projectId = r?.ProjectId, title = r?.Title,
                        iconUrl = r?.IconUrl, pageUrl = r?.PageUrl,
                        searchUrl = $"https://www.curseforge.com/minecraft/search?search={Uri.EscapeDataString(m.Id)}",
                    };
                }).ToList();
            }
            catch (Exception) { return null; }
        }

        /// <summary>Eksik kimliklerin kurulabilir karşılıkları; bulunamayanlar sonuçta yer almaz.</summary>
        private async Task<Dictionary<string, MissingMod>> ResolveMissingAsync(InstanceMeta meta, ModScan scan, IReadOnlyList<MissingDependency> missing)
        {
            var result = new Dictionary<string, MissingMod>(StringComparer.OrdinalIgnoreCase);
            bool curseForgeFirst = meta.Content.Count(c => c.Source == CurseForgeService.Source) * 2 >= meta.Content.Count
                || meta.ModpackProjectId?.StartsWith(CurseForgeService.Source + ":", StringComparison.Ordinal) == true;

            async Task FromCurseForge(IReadOnlyList<MissingDependency> deps)
            {
                try
                {
                    foreach (var (id, mod) in await _curseforge.ResolveModIdsAsync(meta, scan, deps))
                        result[id] = new MissingMod(id, deps.First(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).RequiredBy,
                            CurseForgeService.Source, mod.Id.ToString(), mod.Name, mod.Logo?.ThumbnailUrl, CurseForgeService.PageUrl(mod));
                }
                catch (Exception) { }
            }
            async Task FromModrinth(IReadOnlyList<MissingDependency> deps)
            {
                try
                {
                    foreach (var (id, p) in await _modrinth.ResolveModIdsAsync(meta, scan, deps))
                        if (p != null)
                            result[id] = new MissingMod(id, deps.First(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).RequiredBy,
                                "modrinth", p.Id, p.Title, p.IconUrl, $"https://modrinth.com/mod/{p.Slug}");
                }
                catch (Exception) { }
            }

            var first = curseForgeFirst ? (Func<IReadOnlyList<MissingDependency>, Task>)FromCurseForge : FromModrinth;
            var second = curseForgeFirst ? (Func<IReadOnlyList<MissingDependency>, Task>)FromModrinth : FromCurseForge;
            await first(missing);
            var left = missing.Where(m => !result.ContainsKey(m.Id)).ToList();
            if (left.Count > 0) await second(left);
            return result;
        }

        /// <summary>
        /// Seçilen eksikleri kurar; kurulanların da eksik bağımlılığı çıkarsa (en fazla birkaç tur) onları da kurar.
        /// items: [{ source, projectId }]. Eski çağrılar için projectIds (Modrinth) de kabul edilir.
        /// </summary>
        private async Task<object?> InstallMissingAsync(string instanceId, JsonElement args)
        {
            var meta = RequireCustomInstance(instanceId);
            if (_game.IsRunning) throw new InvalidOperationException("Oyun çalışırken mod kurulamaz.");

            var queue = new List<(string Source, string ProjectId)>();
            if (args.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray())
                    queue.Add((item.Str("source") == CurseForgeService.Source ? CurseForgeService.Source : "modrinth", item.Str("projectId")));
            foreach (var id in args.StrList("projectIds")) queue.Add(("modrinth", id));

            var installed = new List<string>();
            var manual = new List<ManualDownload>();
            var attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string dir = InstanceStore.PathOf(meta.Folder);
            for (int round = 0; round < MissingRounds && queue.Count > 0; round++)
            {
                foreach (var (source, projectId) in queue.Distinct())
                {
                    if (!attempted.Add(source + ":" + projectId)) continue;
                    if (source == CurseForgeService.Source && int.TryParse(projectId, out int modId))
                    {
                        var (done, manualFiles) = await _curseforge.InstallAsync(meta, modId);
                        installed.AddRange(done);
                        manual.AddRange(manualFiles);
                    }
                    else if (System.Text.RegularExpressions.Regex.IsMatch(projectId, "^[A-Za-z0-9]{6,16}$"))
                        installed.AddRange(await _modrinth.InstallAsync(meta, projectId));
                }
                // Kurulanların eksik bağımlılıkları: kullanıcı zaten "eksikleri kur" dedi, yeniden sorulmaz
                var scan = await Task.Run(() => ModScanner.Scan(dir, meta.Loader));
                if (scan.Missing.Count == 0) break;
                var next = await ResolveMissingAsync(meta, scan, scan.Missing);
                queue = next.Values.Where(m => m.Source != null && m.ProjectId != null && !attempted.Contains(m.Source + ":" + m.ProjectId))
                    .Select(m => (m.Source!, m.ProjectId!)).ToList();
            }

            var remaining = (await Task.Run(() => ModScanner.Scan(dir, meta.Loader))).Missing.Select(m => m.Id).ToList();
            if (installed.Count > 0) Log($"Eksik bağımlılıklar kuruldu: {string.Join(", ", installed.Distinct())}", "ok");
            return new { installed = installed.Distinct().ToList(), manual = ManualList(manual), remaining };
        }
    }
}
