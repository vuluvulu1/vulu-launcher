using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// vulu-api'ye giriş token'ıyla istek atar. Sunucunun döndürdüğü {"error": "..."} mesajı
    /// InvalidOperationException olarak fırlatılır; arayüz bunu doğrudan gösterir.
    /// </summary>
    internal sealed class ApiClient
    {
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        // Paket yüklemesi büyük olabilir; tek istekte uzun sürebilir
        private static readonly HttpClient Http = VuluShared.ServerConnection.CreateClient(TimeSpan.FromMinutes(30));

        private readonly Func<string?> _token;

        public ApiClient(Func<string?> token)
        {
            _token = token;
        }

        /// <summary>Oturum anahtarı; API'den doğrudan dosya indirirken (paket aynası) başlığa konur.</summary>
        public string? Token => _token();

        public Task<T> GetAsync<T>(string path) => SendAsync<T>(HttpMethod.Get, path, null);

        public Task<T> PostAsync<T>(string path, object body) =>
            SendAsync<T>(HttpMethod.Post, path, JsonContent.Create(body, options: Json));

        public Task<T> PutAsync<T>(string path, object body) =>
            SendAsync<T>(HttpMethod.Put, path, JsonContent.Create(body, options: Json));

        public Task<T> PutBytesAsync<T>(string path, byte[] data, string contentType)
        {
            var content = new ByteArrayContent(data);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return SendAsync<T>(HttpMethod.Put, path, content);
        }

        public Task<T> PatchAsync<T>(string path, object body) =>
            SendAsync<T>(HttpMethod.Patch, path, JsonContent.Create(body, options: Json));

        public Task<T> DeleteAsync<T>(string path) => SendAsync<T>(HttpMethod.Delete, path, null);

        /// <summary>Dosyayı akış halinde gönderir (belleğe tamamı alınmaz).</summary>
        public async Task<T> UploadFileAsync<T>(string path, string filePath)
        {
            await using var stream = File.OpenRead(filePath);
            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Headers.ContentLength = stream.Length;
            return await SendAsync<T>(HttpMethod.Post, path, content);
        }

        /// <summary>Yanıtı ".part" dosyasına indirip tamamlanınca yerine taşır.</summary>
        public async Task DownloadAsync(string path, string target, IProgress<long>? bytesRead = null)
        {
            using var response = await RawAsync(HttpMethod.Get, path, null, HttpCompletionOption.ResponseHeadersRead);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string part = target + ".part";
            try
            {
                await using (var file = File.Create(part))
                await using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = await stream.ReadAsync(buffer)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read));
                        bytesRead?.Report(read);
                    }
                }
                File.Move(part, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(part)) File.Delete(part);
            }
        }

        private async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent? content)
        {
            using var response = await RawAsync(method, path, content, HttpCompletionOption.ResponseContentRead);
            return await response.Content.ReadFromJsonAsync<T>(Json)
                ?? throw new InvalidDataException("Sunucu boş cevap döndü.");
        }

        private async Task<HttpResponseMessage> RawAsync(HttpMethod method, string path, HttpContent? content, HttpCompletionOption completion)
        {
            var request = new HttpRequestMessage(method, ApiConfig.BaseUrl + path) { Content = content };
            string? token = _token();
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response;
            try
            {
                response = await Http.SendAsync(request, completion);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException("Sunucuya ulaşılamadı: " + ex.Message);
            }

            if (response.IsSuccessStatusCode) return response;

            string message = response.StatusCode switch
            {
                HttpStatusCode.NotFound => "Bulunamadı.",
                HttpStatusCode.Unauthorized => "Oturum süresi doldu, tekrar giriş yapın.",
                HttpStatusCode.RequestEntityTooLarge => "Dosya çok büyük.",
                _ => $"Sunucu hatası ({(int)response.StatusCode})."
            };
            try
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(Json);
                if (!string.IsNullOrWhiteSpace(error?.Error)) message = error.Error;
            }
            catch { /* JSON olmayan hata gövdesi */ }

            response.Dispose();
            throw new ApiException(response.StatusCode, message);
        }
    }

    internal sealed class ApiException(HttpStatusCode status, string message) : InvalidOperationException(message)
    {
        public HttpStatusCode Status { get; } = status;
    }
}
