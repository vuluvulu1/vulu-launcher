using System.IO.Compression;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Başka bir yerden paket getirme: Modrinth modpack'i (.mrpack), CurseForge modpack zip'i ya da Prism/MultiMC dışa
    /// aktarma zip'i. Türü dosyanın içinden anlaşılır (uzantıdan değil). Dosya seçtirilebilir ya da sürükle-bırakla gelir.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private static readonly string[] ImportExtensions = ["mrpack", "zip"];

        private void RegisterImportCommands()
        {
            // path boşsa dosya seçtirilir; vazgeçilirse null
            _bridge.On("instances.pickImport", async _ =>
            {
                RequireLogin();
                string? path = await _host.PickFileAsync("İçe aktarılacak paketi seç", "Modpack (.mrpack, .zip)", ImportExtensions);
                return path == null ? null : new { path, name = Path.GetFileNameWithoutExtension(path) };
            });
            _bridge.On("instances.import", async args => await ImportPackAsync(RequireGameDevice(args.Str("path"))));
            // Modpack sürümleri ve sürüme geçiş (CurseForge/Modrinth'ten kurulan paketler)
            _bridge.On("instances.modpackVersions", async args => await ModpackVersionsAsync(args.Str("id")));
            _bridge.On("instances.switchModpack", async args => await SwitchModpackAsync(RequireGameDevice(args.Str("id")), args.Str("versionId")));
        }

        private static bool IsCurseForgePack(InstanceMeta meta) => meta.ModpackProjectId?.StartsWith(CurseForgeService.Source + ":", StringComparison.Ordinal) == true;

        private async Task<object?> ModpackVersionsAsync(string id)
        {
            RequireLogin();
            var meta = RequireCustomInstance(id);
            if (meta.ModpackProjectId == null) throw new InvalidOperationException("Bu paket CurseForge ya da Modrinth'ten kurulmadı.");
            if (IsCurseForgePack(meta))
            {
                var files = await _curseforge.PackVersionsAsync(int.Parse(meta.ModpackProjectId[(CurseForgeService.Source.Length + 1)..]));
                return new
                {
                    current = meta.ModpackVersion,
                    versions = files.Select(f => new
                    {
                        id = f.Id.ToString(), name = f.DisplayName, type = f.ReleaseType switch { 2 => "beta", 3 => "alpha", _ => "release" },
                        date = f.FileDate.ToString("o"), mc = f.GameVersions.Where(v => ReleaseVersion.IsMatch(v)).ToList(),
                        current = f.DisplayName == meta.ModpackVersion, available = !string.IsNullOrEmpty(f.DownloadUrl),
                    }),
                };
            }
            var versions = await _modrinth.PackVersionsAsync(meta.ModpackProjectId);
            return new
            {
                current = meta.ModpackVersion,
                versions = versions.Select(v => new
                {
                    id = v.Id, name = v.VersionNumber, type = v.VersionType, date = v.DatePublished,
                    mc = v.GameVersions.Where(x => ReleaseVersion.IsMatch(x)).ToList(), current = v.VersionNumber == meta.ModpackVersion, available = true,
                }),
            };
        }

        private async Task<object?> SwitchModpackAsync(string id, string versionId)
        {
            RequireLogin();
            if (_game.IsRunningFor(id)) throw new InvalidOperationException("Paket açıkken sürümü değiştirilemez.");
            var meta = RequireCustomInstance(id);
            List<ManualDownload> manual = new();
            if (IsCurseForgePack(meta)) manual = await _curseforge.SwitchAsync(meta, RequireCfId(versionId, "sürüm"));
            else await _modrinth.SwitchAsync(meta, versionId);
            return new { success = true, id, name = meta.Name, version = meta.ModpackVersion, manual = ManualList(manual) };
        }

        private async Task<object?> ImportPackAsync(string path)
        {
            RequireLogin();
            try
            {
                if (!File.Exists(path)) throw new InvalidOperationException("Dosya bulunamadı.");
                if (!ImportExtensions.Contains(Path.GetExtension(path).TrimStart('.').ToLowerInvariant()))
                    throw new InvalidOperationException("Yalnızca .mrpack ya da .zip modpack dosyaları içe aktarılabilir.");

                string kind;
                using (var zip = ZipFile.OpenRead(path))
                    kind = zip.GetEntry("modrinth.index.json") != null ? "modrinth"
                        : zip.GetEntry("manifest.json") != null ? "curseforge"
                        : FindPrismRoot(zip) != null ? "prism"
                        : throw new InvalidOperationException("Bu dosya tanınmadı: Modrinth (.mrpack), CurseForge modpack zip'i ya da Prism/MultiMC dışa aktarması olmalı.");

                InstanceMeta instance;
                List<ManualDownload> manual = new();
                switch (kind)
                {
                    case "modrinth": instance = await _modrinth.ImportPackAsync(path); break;
                    case "curseforge": (instance, manual) = await _curseforge.ImportZipAsync(path); break;
                    default: instance = await ImportPrismAsync(path); break;
                }
                Log(L.F("{0} içe aktarıldı.", instance.Name), "ok");
                return new { success = true, error = (string?)null, id = InstancePrefix + instance.Folder, name = instance.Name, manual = ManualList(manual) };
            }
            catch (InvalidDataException) { throw new InvalidOperationException("Dosya bozuk ya da yarım (zip açılamadı)."); }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                Log(L.F("İçe aktarılamadı: {0}", ex.Message), "error");
                throw;
            }
        }

        /// <summary>Prism/MultiMC dışa aktarmasında mmc-pack.json'un bulunduğu klasör ("" kök ya da "Ad/"); yoksa null.</summary>
        internal static string? FindPrismRoot(ZipArchive zip)
        {
            foreach (var e in zip.Entries)
            {
                if (!e.FullName.EndsWith("mmc-pack.json", StringComparison.OrdinalIgnoreCase)) continue;
                string dir = e.FullName[..^"mmc-pack.json".Length];
                if (dir.Count(c => c == '/') <= 1) return dir;
            }
            return null;
        }

        /// <summary>
        /// Prism/MultiMC dışa aktarması: sürüm ve yükleyici mmc-pack.json'dan, ad instance.cfg'den; oyun klasörü
        /// (.minecraft ya da minecraft) yeni instance'a açılır (modlar, ayarlar, dünyalar).
        /// </summary>
        internal static async Task<InstanceMeta> ImportPrismAsync(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            string root = FindPrismRoot(zip) ?? "";
            using var job = new DownloadJob(L.F("{0} içe aktarılıyor", Path.GetFileNameWithoutExtension(path))) { Phase = "Paket okunuyor" };

            JsonDocument pack;
            using (var s = zip.GetEntry(root + "mmc-pack.json")!.Open()) pack = await JsonDocument.ParseAsync(s);
            string? mc = null, loader = null, loaderVersion = null;
            foreach (var c in pack.RootElement.GetProperty("components").EnumerateArray())
            {
                string uid = c.TryGetProperty("uid", out var u) ? u.GetString() ?? "" : "";
                string? version = c.TryGetProperty("version", out var v) ? v.GetString() : null;
                switch (uid)
                {
                    case "net.minecraft": mc = version; break;
                    case "net.fabricmc.fabric-loader": (loader, loaderVersion) = ("fabric", version); break;
                    case "org.quiltmc.quilt-loader": (loader, loaderVersion) = ("quilt", version); break;
                    case "net.minecraftforge": (loader, loaderVersion) = ("forge", version); break;
                    case "net.neoforged": (loader, loaderVersion) = ("neoforge", version); break;
                }
            }
            if (string.IsNullOrEmpty(mc)) throw new InvalidOperationException("Dışa aktarma hangi Minecraft sürümünü kullandığını belirtmiyor.");

            string name = Path.GetFileNameWithoutExtension(path);
            if (zip.GetEntry(root + "instance.cfg") is { } cfg)
            {
                using var reader = new StreamReader(cfg.Open());
                while (await reader.ReadLineAsync() is { } line)
                    if (line.StartsWith("name=", StringComparison.Ordinal) && line.Length > 5) { name = line[5..].Trim(); break; }
            }
            if (name.Length > 40) name = name[..40];
            job.Title = L.F("{0} içe aktarılıyor", name);

            var instance = InstanceStore.Create(name, mc, loader ?? "vanilla", loader == null ? null : loaderVersion);
            try
            {
                string dir = InstanceStore.PathOf(instance.Folder);
                job.Phase = "Dosyalar açılıyor";
                string game = zip.Entries.Any(e => e.FullName.StartsWith(root + ".minecraft/", StringComparison.Ordinal)) ? root + ".minecraft/" : root + "minecraft/";
                await Task.Run(() => ModrinthService.ExtractOverrides(zip, game, dir));
                job.Complete(L.F("{0} içe aktarıldı", name));
                return instance;
            }
            catch (Exception ex)
            {
                job.Fail(ex.Message);
                try { InstanceStore.Delete(instance.Folder); } catch { }
                throw;
            }
        }
    }
}
