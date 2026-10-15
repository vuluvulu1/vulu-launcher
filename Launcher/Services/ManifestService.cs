using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace VuluLauncher
{
    public class ManifestService
    {
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
            return await _http.GetFromJsonAsync<ManifestRoot>($"{ApiConfig.BaseUrl}/manifest");
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

                    string url = $"{ApiConfig.ModpackFilesBaseUrl}/{instanceName}/{relativePath}";

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
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiConfig.BaseUrl}/manifest/push");
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

        /// <summary>
        /// Manifest'ten gelen göreli yolu instance klasörü içindeki tam yola çevirir.
        /// Yol instance dışına çıkıyorsa ya da takip edilen klasörlerden birinde değilse null döner.
        /// </summary>
        public static string? ResolveTrackedPath(string instancePath, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;

            string normalized = relativePath.Replace('\\', '/');
            if (Path.IsPathRooted(normalized)) return null;

            string root = Path.GetFullPath(instancePath)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, normalized));

            // ".." çözüldükten sonraki gerçek yola bakılır
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;

            string firstSegment = full[root.Length..].Split(Path.DirectorySeparatorChar)[0];
            return TrackedFolders.Contains(firstSegment, StringComparer.OrdinalIgnoreCase) ? full : null;
        }

        // ── Güncelleme + silme kontrolü ───────────────────────────────────
        /// <summary>
        /// Lokal dosyaları manifest ile karşılaştırır. Geçersiz yollu girdiler atlanır.
        /// </summary>
        public Task<(List<ManifestFile> ToUpdate, List<string> ToDelete)>
            GetChangesAsync(string instancePath, InstanceManifest instanceManifest)
        {
            return Task.Run(() =>
            {
                // Güncellenecek dosyalar
                var toUpdate = new List<ManifestFile>();
                foreach (var file in instanceManifest.Files)
                {
                    string? localPath = ResolveTrackedPath(instancePath, file.Path);
                    if (localPath == null) continue;

                    if (!File.Exists(localPath) ||
                        !string.Equals(ComputeSHA256(localPath), file.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        toUpdate.Add(file);
                    }
                }

                // Silinecek dosyalar
                var toDelete = new List<string>();
                foreach (var deletedPath in instanceManifest.DeletedFiles ?? new List<string>())
                {
                    string? localPath = ResolveTrackedPath(instancePath, deletedPath);
                    if (localPath != null && File.Exists(localPath))
                        toDelete.Add(localPath);
                }

                return (toUpdate, toDelete);
            });
        }

        // ── Dosya indir ───────────────────────────────────────────────────
        /// <summary>
        /// Dosyayı önce ".part" olarak indirir, (verildiyse) SHA-256'sını doğrular,
        /// sonra hedefin üzerine taşır. Yarım kalan indirme mevcut dosyayı bozmaz.
        /// </summary>
        public async Task DownloadFileAsync(string url, string localPath,
            IProgress<int>? progress = null, string? expectedSha256 = null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException($"Geçersiz indirme adresi: {url}");

            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            string partPath = localPath + ".part";

            try
            {
                using (var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    long? total = response.Content.Headers.ContentLength;
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var file = File.Create(partPath);

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

                if (!string.IsNullOrEmpty(expectedSha256) &&
                    !string.Equals(ComputeSHA256(partPath), expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Dosya doğrulanamadı (hash uyuşmuyor): {Path.GetFileName(localPath)}");
                }

                File.Move(partPath, localPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(partPath)) File.Delete(partPath);
            }
        }

        private static string ComputeSHA256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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
