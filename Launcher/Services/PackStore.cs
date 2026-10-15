using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace VuluLauncher
{
    /// <summary>
    /// vulu Packs: adminin paylaştığı instance'lar. Bilgiler "packs" koleksiyonunda, dosyalar (seçilen
    /// klasörlerin zip'i ve kapak resmi) GridFS'in "packfiles" deposunda durur. Modrinth modları
    /// kopyalanmaz; sürümü sabitlenmiş indirme adresi ve hash'iyle listelenir.
    /// </summary>
    internal sealed class PackStore
    {
        private readonly IMongoCollection<SharedPack> _packs;
        private readonly GridFSBucket _files;

        public PackStore(IMongoDatabase database)
        {
            _packs = database.GetCollection<SharedPack>("packs");
            _files = new GridFSBucket(database, new GridFSBucketOptions { BucketName = "packfiles" });
        }

        /// <summary>
        /// Herkesin paketleri okuyabilmesi için yalnızca okuma yetkili veritabanı kullanıcısı.
        /// Bilgiler ApiConfig'te değilse null döner (admin bağlıysa onun bağlantısı kullanılır).
        /// </summary>
        public static PackStore? CreateReader()
        {
            if (string.IsNullOrWhiteSpace(ApiConfig.PacksReaderUser)) return null;
            var client = AdminService.CreateClient(ApiConfig.PacksReaderUser, ApiConfig.PacksReaderPassword);
            return new PackStore(client.GetDatabase("launcher"));
        }

        public async Task<List<SharedPack>> ListAsync() =>
            await _packs.Find(_ => true).SortByDescending(p => p.UpdatedAt).ToListAsync();

        public async Task<SharedPack?> GetAsync(string id) =>
            ObjectId.TryParse(id, out var objectId)
                ? await _packs.Find(p => p.Id == objectId).FirstOrDefaultAsync()
                : null;

        public async Task DownloadFileAsync(ObjectId fileId, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string part = path + ".part";
            await using (var stream = File.Create(part))
                await _files.DownloadToStreamAsync(fileId, stream);
            File.Move(part, path, overwrite: true);
        }

        /// <summary>Yeni paket ekler ya da (pack.Id doluysa) var olanı yeni revizyonla değiştirir.</summary>
        public async Task<SharedPack> PublishAsync(SharedPack pack, string overridesZip, string? coverPath)
        {
            var previous = pack.Id == ObjectId.Empty ? null : await _packs.Find(p => p.Id == pack.Id).FirstOrDefaultAsync();

            await using (var zip = File.OpenRead(overridesZip))
                pack.OverridesFileId = await _files.UploadFromStreamAsync($"{pack.Name}-overrides.zip", zip);
            pack.OverridesSize = new FileInfo(overridesZip).Length;

            if (coverPath != null)
            {
                await using var cover = File.OpenRead(coverPath);
                pack.CoverFileId = await _files.UploadFromStreamAsync($"{pack.Name}-cover{Path.GetExtension(coverPath)}", cover);
                pack.CoverExt = Path.GetExtension(coverPath).ToLowerInvariant();
            }
            else if (previous != null)
            {
                pack.CoverFileId = previous.CoverFileId; // kapak değiştirilmediyse eskisi kalır
                pack.CoverExt = previous.CoverExt;
            }

            pack.UpdatedAt = DateTime.UtcNow;
            if (previous == null)
            {
                pack.Id = ObjectId.GenerateNewId();
                pack.CreatedAt = pack.UpdatedAt;
                pack.Revision = 1;
                await _packs.InsertOneAsync(pack);
            }
            else
            {
                pack.CreatedAt = previous.CreatedAt;
                pack.Revision = previous.Revision + 1;
                await _packs.ReplaceOneAsync(p => p.Id == pack.Id, pack);

                // Eski dosyalar artık kullanılmıyor
                await TryDeleteFileAsync(previous.OverridesFileId);
                if (coverPath != null && previous.CoverFileId is ObjectId oldCover) await TryDeleteFileAsync(oldCover);
            }
            return pack;
        }

        public async Task DeleteAsync(string id)
        {
            var pack = await GetAsync(id) ?? throw new InvalidOperationException("Paket bulunamadı.");
            await _packs.DeleteOneAsync(p => p.Id == pack.Id);
            await TryDeleteFileAsync(pack.OverridesFileId);
            if (pack.CoverFileId is ObjectId cover) await TryDeleteFileAsync(cover);
        }

        private async Task TryDeleteFileAsync(ObjectId id)
        {
            try { await _files.DeleteAsync(id); }
            catch (GridFSFileNotFoundException) { }
        }
    }

    [BsonIgnoreExtraElements]
    public sealed class SharedPack
    {
        [BsonId] public ObjectId Id { get; set; }
        [BsonElement("name")] public string Name { get; set; } = "";
        [BsonElement("description")] public string Description { get; set; } = "";
        [BsonElement("author")] public string Author { get; set; } = "";
        [BsonElement("mcVersion")] public string McVersion { get; set; } = "";
        [BsonElement("loader")] public string Loader { get; set; } = "vanilla";
        [BsonElement("loaderVersion")] public string? LoaderVersion { get; set; }
        [BsonElement("recommendedRamGb")] public int RecommendedRamGb { get; set; }
        [BsonElement("revision")] public int Revision { get; set; }
        [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
        [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; }
        [BsonElement("mods")] public List<PackMod> Mods { get; set; } = new();
        /// <summary>Pakete dahil edilen klasör/dosyalar (config, kubejs...), bilgi amaçlı.</summary>
        [BsonElement("includes")] public List<string> Includes { get; set; } = new();
        [BsonElement("overridesFileId")] public ObjectId OverridesFileId { get; set; }
        [BsonElement("overridesSize")] public long OverridesSize { get; set; }
        [BsonElement("coverFileId")] public ObjectId? CoverFileId { get; set; }
        [BsonElement("coverExt")] public string? CoverExt { get; set; }
    }

    /// <summary>Modrinth'ten indirilecek bir içerik; sürümü yayınlandığı anda sabitlenir.</summary>
    [BsonIgnoreExtraElements]
    public sealed class PackMod
    {
        [BsonElement("projectId")] public string ProjectId { get; set; } = "";
        [BsonElement("versionId")] public string VersionId { get; set; } = "";
        [BsonElement("title")] public string Title { get; set; } = "";
        [BsonElement("versionNumber")] public string VersionNumber { get; set; } = "";
        [BsonElement("type")] public string Type { get; set; } = "mod";
        /// <summary>Instance klasörüne göre yol, örn. "mods/sodium.jar".</summary>
        [BsonElement("file")] public string File { get; set; } = "";
        [BsonElement("iconUrl")] public string? IconUrl { get; set; }
        [BsonElement("url")] public string Url { get; set; } = "";
        [BsonElement("sha1")] public string? Sha1 { get; set; }
        [BsonElement("sha512")] public string? Sha512 { get; set; }
        [BsonElement("size")] public long Size { get; set; }
    }
}
