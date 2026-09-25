// test
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AGLR_Launcher
{
    public class LoginService
    {
        private const string ApiBase = "https://vulu-api.onrender.com"; // Deploy sonrası Render URL'si gelecek

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(90)
        };


        public string? Token { get; private set; }
        public bool IsAdmin { get; private set; }  // ← ekle
        public List<string> AllowedPacks { get; private set; } = new();
        public string Username { get; private set; } = string.Empty;

        /// <summary>
        /// API'ye login isteği atar. Başarılıysa token ve izinli paketleri saklar.
        /// </summary>
        public async Task<LoginResult> LoginAsync(string username, string password)
        {
            try
            {
                var response = await _http.PostAsJsonAsync($"{ApiBase}/auth/login", new
                {
                    username,
                    password
                });

                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<LoginResponse>();
                    Token = data?.Token;
                    AllowedPacks = data?.AllowedPacks ?? new List<string>();
                    IsAdmin = data?.IsAdmin ?? false;
                    Username = username; // ← ekle
                    return new LoginResult(true, null);
                }
                else
                {
                    var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                    return new LoginResult(false, error?.Error ?? "Giriş başarısız.");
                }
            }
            catch (Exception ex)
            {
                return new LoginResult(false, "Sunucuya bağlanılamadı: " + ex.Message);
            }
        }

        public bool IsPackAllowed(string packName)
        {
            return AllowedPacks.Contains(packName, StringComparer.OrdinalIgnoreCase);
        }
    }

    public record LoginResult(bool Success, string? ErrorMessage);

    internal class LoginResponse
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("allowedPacks")]
        public List<string>? AllowedPacks { get; set; }
        [JsonPropertyName("isAdmin")]
        public bool IsAdmin { get; set; }
    }

    internal class ErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}