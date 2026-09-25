using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace AGLR_Launcher
{
    public class ManifestService
    {
        private const string ApiBase = "https://vulu-api.onrender.com";
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

        public static readonly string[] TrackedFolders =
        {
            "config",
            "kubejs",
            "defaultconfigs",
            "fancymenu_data"
        };

        private readonly string _token;

        public ManifestService(string token)
        {
            _token = token;
        }

        // ── Manifest çek ──────────────────────────────────────────────────
        public async Task<ManifestRoot?> GetManifestAsync()
        {
            return await _http.GetFromJsonAsync<ManifestRoot>($"{ApiBase}/manifest");
        }

        // ── Instance klasörünü tara ───────────────────────────────────────
        public List<ManifestFile> ScanInstance(string instancePath, string instanceName)
        {
            var files = new List<ManifestFile>();

            foreach (var folder in TrackedFolders)
            {
                string folderPath = Path.Combine(instancePath, folder);
                if (!Directory.Exists(folderPath)) continue;

                foreach (var file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(instancePath, file)
                        .Replace('\\', '/');

                    string hash = ComputeSHA256(file);
                    long size = new FileInfo(file).Length;

                    string url = $"https://raw.githubusercontent.com/vuluvulu1/vulu-launcher/main/modpack/{instanceName}/{relativePath}";

                    files.Add(new ManifestFile
                    {
                        Path = relativePath,
                        Hash = hash,
                        Url = url,
                        Size = size
                    });
                }
            }

            return files;
        }

        // ── Manifest push ─────────────────────────────────────────────────
        public async Task<bool> PushManifestAsync(string instance, List<ManifestFile> files, List<string>? deletedFiles = null, string? modsVersion = null, string? modsUrl = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/manifest/push");
            request.Headers.Add("Authorization", $"Bearer {_token}");
            request.Content = JsonContent.Create(new
            {
                instance,
                files,
                deletedFiles = deletedFiles ?? new List<string>(),
                modsVersion,
                modsUrl
            });

            var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }

        // ── Güncelleme + silme kontrolü ───────────────────────────────────
        public async Task<(List<ManifestFile> ToUpdate, List<string> ToDelete)>
            GetChangesAsync(string instancePath, string instanceName)
        {
            var manifest = await GetManifestAsync();
            if (manifest?.Instances == null)
                return (new List<ManifestFile>(), new List<string>());

            if (!manifest.Instances.TryGetValue(instanceName, out var instanceManifest))
                return (new List<ManifestFile>(), new List<string>());

            // Güncellenecek dosyalar
            var toUpdate = new List<ManifestFile>();
            foreach (var file in instanceManifest.Files)
            {
                string localPath = Path.Combine(instancePath, file.Path.Replace('/', '\\'));

                if (!File.Exists(localPath))
                {
                    toUpdate.Add(file);
                    continue;
                }

                string localHash = ComputeSHA256(localPath);
                if (localHash != file.Hash)
                    toUpdate.Add(file);
            }

            // Silinecek dosyalar
            var toDelete = new List<string>();
            if (instanceManifest.DeletedFiles != null)
            {
                foreach (var deletedPath in instanceManifest.DeletedFiles)
                {
                    string localPath = Path.Combine(instancePath, deletedPath.Replace('/', '\\'));
                    if (File.Exists(localPath))
                        toDelete.Add(localPath);
                }
            }

            return (toUpdate, toDelete);
        }

        // ── Dosya indir ───────────────────────────────────────────────────
        public async Task DownloadFileAsync(string url, string localPath,
            IProgress<int>? progress = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var file = File.Create(localPath);

            byte[] buffer = new byte[81920];
            long downloaded = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (total > 0)
                    progress?.Report((int)(downloaded * 100 / total));
            }
        }

        private static string ComputeSHA256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLower();
        }
    }

    public class ManifestRoot
    {
        [JsonPropertyName("instances")]
        public Dictionary<string, InstanceManifest>? Instances { get; set; }
    }

    public class InstanceManifest
    {
        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; } = string.Empty;

        [JsonPropertyName("files")]
        public List<ManifestFile> Files { get; set; } = new();

        [JsonPropertyName("deletedFiles")]
        public List<string>? DeletedFiles { get; set; }

        [JsonPropertyName("modsVersion")]
        public string? ModsVersion { get; set; }

        [JsonPropertyName("modsUrl")]
        public string? ModsUrl { get; set; }
    }

    public class ManifestFile
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("hash")]
        public string Hash { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}