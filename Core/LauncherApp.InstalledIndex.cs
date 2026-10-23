using System.Collections.Concurrent;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Bir instance'ta hangi CurseForge ve Modrinth projelerinin kurulu olduğu: kayıtlı içeriklerin yanında
    /// dosyaların hash'iyle (Modrinth: SHA-1, CurseForge: parmak izi) de bulunur. Böylece bir paketin getirdiği ya da
    /// elle konan modlar da Keşfet'te "kurulu" görünür. Sonuç klasördeki dosyalar değişene kadar bellekte kalır.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private sealed record InstalledIndex(string Stamp, HashSet<string> Modrinth, HashSet<string> CurseForge);

        private readonly ConcurrentDictionary<string, InstalledIndex> _installedIndex = new();
        private static readonly string[] IndexedFolders = { "mods", "resourcepacks", "shaderpacks" };

        private void RegisterInstalledIndexCommands()
        {
            _bridge.On("content.installedIndex", async args => await InstalledIndexAsync(args.Str("instanceId")));
        }

        private async Task<object?> InstalledIndexAsync(string instanceId)
        {
            RequireLogin();
            var meta = RequireCustomInstance(instanceId);
            string dir = InstanceStore.PathOf(meta.Folder);
            var files = IndexedFolders.Select(f => Path.Combine(dir, f)).Where(Directory.Exists)
                .SelectMany(f => Directory.EnumerateFiles(f).Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                    || p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)))
                .Select(p => new FileInfo(p)).ToList();
            string stamp = $"{files.Count}:{files.Sum(f => f.Length)}:{(files.Count == 0 ? 0 : files.Max(f => f.LastWriteTimeUtc.Ticks))}:{meta.Content.Count}";

            if (!_installedIndex.TryGetValue(meta.Folder, out var index) || index.Stamp != stamp)
            {
                var modrinth = meta.Content.Where(c => c.Source != CurseForgeService.Source).Select(c => c.ProjectId).ToHashSet();
                var curseforge = meta.Content.Where(c => c.Source == CurseForgeService.Source).Select(c => c.ProjectId).ToHashSet();
                var recorded = meta.Content.Select(c => Path.GetFullPath(Path.Combine(dir, c.File))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                // Kayıtlı olmayan dosyalar hash'iyle tanınır
                var unknown = files.Where(f => !recorded.Contains(f.FullName) && !recorded.Contains(f.FullName.Replace(".disabled", ""))).ToList();
                var hashes = await Task.Run(() => unknown.Select(f => { try { return FileHashes.Of(f.FullName); } catch { return null; } }).Where(h => h != null).ToList());
                FileHashes.Save();

                if (hashes.Count > 0)
                {
                    try { foreach (var v in (await _modrinth.VersionsByHashesAsync(hashes.Select(h => h!.Sha1))).Values) modrinth.Add(v.ProjectId); }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
                    try { foreach (int id in await _curseforge.MatchFingerprintsAsync(hashes.Select(h => h!.Fingerprint))) curseforge.Add(id.ToString()); }
                    catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { }
                }
                index = new InstalledIndex(stamp, modrinth, curseforge);
                _installedIndex[meta.Folder] = index;
            }
            return new { modrinth = index.Modrinth, curseforge = index.CurseForge, recorded = meta.Content.Select(c => c.ProjectId) };
        }
    }
}
