using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace VuluLauncher
{
    public class LoginService
    {
        private static readonly HttpClient _http = VuluShared.ServerConnection.CreateClient(TimeSpan.FromSeconds(90));


        public string? Token { get; private set; }

        /// <summary>Şifre değişince sunucu yeni token verir; eski token'lar geçersiz olur.</summary>
        public void UpdateToken(string token) => Token = token;
        public bool IsAdmin { get; private set; }
        public string Username { get; private set; } = string.Empty;

        /// <summary>
        /// API'ye login isteği atar. Başarılıysa token'ı saklar.
        /// </summary>
        public async Task<LoginResult> LoginAsync(string username, string password)
        {
            try
            {
                var response = await _http.PostAsJsonAsync($"{ApiConfig.BaseUrl}/auth/login", new
                {
                    username,
                    password
                });

                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<LoginResponse>();
                    Token = data?.Token;
                    IsAdmin = data?.IsAdmin ?? false;
                    Username = username;
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

    }

    public record LoginResult(bool Success, string? ErrorMessage);

    internal class LoginResponse
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("isAdmin")]
        public bool IsAdmin { get; set; }
    }

    internal class ErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}