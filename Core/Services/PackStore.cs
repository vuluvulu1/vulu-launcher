using System.Net;

namespace VuluLauncher
{
    /// <summary>
    /// vulu Packs: adminin paylaştığı instance'lar. Hepsi vulu-api üzerinden: giriş yapan herkes listeler
    /// ve indirir, admin işaretli hesaplar yayınlar ve siler. Modrinth modları kopyalanmaz; sürümü
    /// sabitlenmiş indirme adresi ve hash'iyle listelenir.
    /// </summary>
    internal sealed class PackStore
    {
        private readonly ApiClient _api;

        public PackStore(ApiClient api)
        {
            _api = api;
        }

        public Task<List<SharedPack>> ListAsync() => _api.GetAsync<List<SharedPack>>("/packs");

        public async Task<SharedPack?> GetAsync(string id)
        {
            try { return await _api.GetAsync<SharedPack>($"/packs/{Uri.EscapeDataString(id)}"); }
            catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound) { return null; }
        }

        public Task DownloadOverridesAsync(SharedPack pack, string path, IProgress<long>? bytesRead = null) =>
            _api.DownloadAsync($"/packs/{Uri.EscapeDataString(pack.Id)}/overrides", path, bytesRead);

        public Task DownloadLogoAsync(SharedPack pack, string path) =>
            _api.DownloadAsync($"/packs/{Uri.EscapeDataString(pack.Id)}/logo?v={pack.Revision}", path);

        public Task DownloadCoverAsync(SharedPack pack, string path) =>
            _api.DownloadAsync($"/packs/{Uri.EscapeDataString(pack.Id)}/cover?v={pack.Revision}", path);

        /// <summary>Kapak resminin herkese açık adresi; revizyon değişince tarayıcı önbelleği yenilenir.</summary>
        public static string? CoverUrl(SharedPack pack) =>
            pack.HasCover ? $"{ApiConfig.BaseUrl}/packs/{Uri.EscapeDataString(pack.Id)}/cover?v={pack.Revision}" : null;

        /// <summary>
        /// Dosyaları yükler ve paketi yayınlar. pack.Id doluysa o paketin yeni revizyonu olur.
        /// logoPath null ise paketin logosu kaldırılır (instance'ta logo yok demektir).
        /// </summary>
        public async Task<SharedPack> PublishAsync(SharedPack pack, string overridesZip, string? coverPath, string? logoPath)
        {
            var overrides = await _api.UploadFileAsync<UploadResult>("/packs/files?kind=overrides", overridesZip);
            UploadResult? cover = coverPath == null ? null : await _api.UploadFileAsync<UploadResult>("/packs/files?kind=cover", coverPath);

            // Logoyu tanımayan eski sunucu bu türü reddeder; paket logosuz yayınlanır
            UploadResult? logo = null;
            bool logoSupported = true;
            if (logoPath != null)
            {
                try { logo = await _api.UploadFileAsync<UploadResult>("/packs/files?kind=logo", logoPath); }
                catch (ApiException ex) when (ex.Status == HttpStatusCode.BadRequest) { logoSupported = false; }
            }

            return await _api.PostAsync<SharedPack>("/packs", new
            {
                id = string.IsNullOrEmpty(pack.Id) ? null : pack.Id,
                name = pack.Name,
                description = pack.Description,
                changeNote = pack.ChangeNote,
                mcVersion = pack.McVersion,
                loader = pack.Loader,
                loaderVersion = pack.LoaderVersion,
                recommendedRamGb = pack.RecommendedRamGb,
                mods = pack.Mods,
                includes = pack.Includes,
                overridesFileId = overrides.FileId,
                coverFileId = cover?.FileId,
                coverExt = coverPath == null ? null : Path.GetExtension(coverPath).ToLowerInvariant(),
                logoFileId = logo?.FileId,
                logoExt = logo == null ? null : Path.GetExtension(logoPath!).ToLowerInvariant(),
                clearLogo = logoPath == null && logoSupported
            });
        }

        public Task DeleteAsync(string id) => _api.DeleteAsync<object>($"/packs/{Uri.EscapeDataString(id)}");

        private sealed class UploadResult
        {
            public string FileId { get; set; } = "";
            public long Size { get; set; }
        }
    }

    public sealed class SharedPack
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public string McVersion { get; set; } = "";
        public string Loader { get; set; } = "vanilla";
        public string? LoaderVersion { get; set; }
        public int RecommendedRamGb { get; set; }
        public int Revision { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<PackMod> Mods { get; set; } = new();
        /// <summary>Pakete dahil edilen klasör/dosyalar (config, kubejs...), bilgi amaçlı.</summary>
        public List<string> Includes { get; set; } = new();
        public long OverridesSize { get; set; }
        public bool HasCover { get; set; }
        public string? CoverExt { get; set; }
        public bool HasLogo { get; set; }
        public string? LogoExt { get; set; }
        /// <summary>Revizyon başına değişiklikler (sunucu hesaplar); eskiden yeniye.</summary>
        public List<PackChange> Changelog { get; set; } = new();
        /// <summary>Yayınlarken gönderilen güncelleme notu; sunucudan gelmez.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string ChangeNote { get; set; } = "";
    }

    public sealed class PackChange
    {
        public int Revision { get; set; }
        public DateTime At { get; set; }
        public string? By { get; set; }
        public string Note { get; set; } = "";
        public List<string> Added { get; set; } = new();
        public List<string> Removed { get; set; } = new();
        public List<PackModChange> Updated { get; set; } = new();
        public bool FilesChanged { get; set; }
    }

    public sealed class PackModChange
    {
        public string Title { get; set; } = "";
        public string From { get; set; } = "";
        public string To { get; set; } = "";
    }

    /// <summary>Modrinth'ten indirilecek bir içerik; sürümü yayınlandığı anda sabitlenir.</summary>
    public sealed class PackMod
    {
        /// <summary>"curseforge" ya da "modrinth" (eski paketlerde boş: Modrinth).</summary>
        public string? Source { get; set; }
        public string ProjectId { get; set; } = "";
        public string VersionId { get; set; } = "";
        public string Title { get; set; } = "";
        public string VersionNumber { get; set; } = "";
        public string Type { get; set; } = "mod";
        /// <summary>Instance klasörüne göre yol, örn. "mods/sodium.jar".</summary>
        public string File { get; set; } = "";
        public string? IconUrl { get; set; }
        public string Url { get; set; } = "";
        public string? Sha1 { get; set; }
        public string? Sha512 { get; set; }
        public long Size { get; set; }
    }
}
