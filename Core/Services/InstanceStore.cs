using System.Text;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>Instance'a Modrinth'ten kurulmuş bir içerik (mod, kaynak paketi, shader).</summary>
    public sealed class InstalledContent
    {
        /// <summary>Nereden kuruldu: "curseforge"; boşsa Modrinth (eski kayıtlar).</summary>
        public string? Source { get; set; }
        public string ProjectId { get; set; } = "";
        public string VersionId { get; set; } = "";
        public string Title { get; set; } = "";
        public string VersionNumber { get; set; } = "";
        /// <summary>"mod" | "resourcepack" | "shader"</summary>
        public string Type { get; set; } = "mod";
        /// <summary>Instance klasörüne göre dosya yolu, örn. "mods/sodium.jar".</summary>
        public string File { get; set; } = "";
        public string? IconUrl { get; set; }
        /// <summary>vulu Packs'ten geldiyse true; paket güncellenince bu içerikler yenisiyle eşitlenir.</summary>
        public bool FromPack { get; set; }
    }

    /// <summary>Kullanıcının oluşturduğu instance'ın bilgileri; instances/&lt;klasör&gt;/instance.json içinde durur.</summary>
    public sealed class InstanceMeta
    {
        public string Name { get; set; } = "";
        public string McVersion { get; set; } = "";
        public string Loader { get; set; } = "vanilla";
        public string? LoaderVersion { get; set; }
        /// <summary>0 = ayarlardaki varsayılan.</summary>
        public int RamGb { get; set; }
        public string? IconUrl { get; set; }
        /// <summary>Modrinth modpack'inden kurulduysa projenin adı/kimliği.</summary>
        public string? ModpackProjectId { get; set; }
        public string? ModpackVersion { get; set; }
        /// <summary>vulu Packs'ten kurulduysa (ya da oradan yayınlandıysa) paketin kimliği ve revizyonu.</summary>
        public string? SharedPackId { get; set; }
        public int SharedPackRevision { get; set; }
        /// <summary>Paketin logosu bir kez indirildi mi (kullanıcı sonradan kaldırırsa geri getirilmez).</summary>
        public bool PackLogoSynced { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<InstalledContent> Content { get; set; } = new();

        /// <summary>Klasör adı; dosyaya yazılmaz, yüklenirken doldurulur.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string Folder { get; set; } = "";
    }

    /// <summary>Kullanıcı instance'larını diskten okur ve yazar.</summary>
    internal static class InstanceStore
    {
        private const string MetaFile = "instance.json";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        private static readonly object Gate = new();

        public static string PathOf(string folder) => Path.Combine(AppPaths.Instances, folder);

        public static List<InstanceMeta> List()
        {
            var list = new List<InstanceMeta>();
            if (!Directory.Exists(AppPaths.Instances)) return list;

            foreach (var dir in Directory.GetDirectories(AppPaths.Instances))
            {
                var meta = TryLoad(Path.GetFileName(dir));
                if (meta != null) list.Add(meta);
            }
            return list.OrderBy(m => m.CreatedAt).ToList();
        }

        public static InstanceMeta Get(string folder) =>
            TryLoad(folder) ?? throw new InvalidOperationException($"Instance bulunamadı: {folder}");

        public static InstanceMeta? TryLoad(string folder)
        {
            if (!IsValidFolderName(folder) || PackCatalog.IsReservedFolder(folder)) return null;

            string file = Path.Combine(PathOf(folder), MetaFile);
            if (!File.Exists(file)) return null;

            try
            {
                lock (Gate)
                {
                    var meta = JsonSerializer.Deserialize<InstanceMeta>(File.ReadAllText(file), Json);
                    if (meta == null) return null;
                    meta.Folder = folder;
                    return meta;
                }
            }
            catch
            {
                return null;
            }
        }

        public static void Save(InstanceMeta meta)
        {
            if (!IsValidFolderName(meta.Folder) || PackCatalog.IsReservedFolder(meta.Folder))
                throw new InvalidOperationException("Geçersiz instance klasörü.");

            lock (Gate)
            {
                string dir = PathOf(meta.Folder);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, MetaFile), JsonSerializer.Serialize(meta, Json));
            }
        }

        /// <summary>Yeni instance için klasör açar ve bilgilerini kaydeder.</summary>
        public static InstanceMeta Create(string name, string mcVersion, string loader, string? loaderVersion)
        {
            Validate(ref name, mcVersion, loader, loaderVersion);

            var meta = new InstanceMeta
            {
                Name = name,
                McVersion = mcVersion.Trim(),
                Loader = loader,
                LoaderVersion = loader == "vanilla" ? null : loaderVersion!.Trim(),
                Folder = UniqueFolder(name)
            };
            Save(meta);
            return meta;
        }

        /// <summary>
        /// Var olan instance'ın adını, sürümünü ya da yükleyicisini değiştirir.
        /// Klasör aynı kalır; dünyalar, modlar ve ayarlar yerinde durur.
        /// </summary>
        public static InstanceMeta Update(string folder, string name, string mcVersion, string loader, string? loaderVersion)
        {
            Validate(ref name, mcVersion, loader, loaderVersion);

            var meta = Get(folder);
            meta.Name = name;
            meta.McVersion = mcVersion.Trim();
            meta.Loader = loader;
            meta.LoaderVersion = loader == "vanilla" ? null : loaderVersion!.Trim();
            Save(meta);
            return meta;
        }

        private static void Validate(ref string name, string mcVersion, string loader, string? loaderVersion)
        {
            name = (name ?? "").Trim();
            if (name.Length is 0 or > 40)
                throw new InvalidOperationException("Instance adı 1-40 karakter olmalı.");
            if (!PackCatalog.Loaders.Contains(loader))
                throw new InvalidOperationException($"Bilinmeyen mod yükleyici: {loader}");
            if (string.IsNullOrWhiteSpace(mcVersion))
                throw new InvalidOperationException("Minecraft sürümü seçilmedi.");
            if (loader != "vanilla" && string.IsNullOrWhiteSpace(loaderVersion))
                throw new InvalidOperationException("Mod yükleyici sürümü seçilmedi.");
        }

        public static void Delete(string folder)
        {
            // Yalnızca instance.json içeren kullanıcı instance'ları silinebilir
            _ = Get(folder);
            Directory.Delete(PathOf(folder), recursive: true);
        }

        private static string UniqueFolder(string name)
        {
            var slug = new StringBuilder();
            foreach (char c in name.Normalize(NormalizationForm.FormD))
            {
                if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9') slug.Append(c);
                else if ((c is ' ' or '-' or '_') && slug.Length > 0 && slug[^1] != '-') slug.Append('-');
            }

            string baseName = slug.ToString().Trim('-');
            if (baseName.Length == 0) baseName = "instance";
            if (baseName.Length > 32) baseName = baseName[..32].Trim('-');

            string folder = baseName;
            for (int i = 2; PackCatalog.IsReservedFolder(folder) || Directory.Exists(PathOf(folder)); i++)
                folder = $"{baseName}-{i}";
            return folder;
        }

        private static bool IsValidFolderName(string folder) =>
            !string.IsNullOrWhiteSpace(folder) &&
            folder.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            folder.Trim('.').Length > 0;
    }
}
