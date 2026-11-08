using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VuluLauncher
{
    /// <summary>Oyunun gerçek Microsoft oturumu: Minecraft profili ve erişim token'ı.</summary>
    public sealed record MicrosoftSession(string Name, string Uuid, string AccessToken);

    /// <summary>Cihaz koduyla girişin ilk adımı: kişi <see cref="VerificationUri"/> sayfasına gidip kodu yazar.</summary>
    public sealed record DeviceCode(string UserCode, string VerificationUri, int ExpiresIn, int Interval, string Code);

    /// <summary>
    /// Microsoft hesabıyla giriş (cihaz kodu akışı): Microsoft → Xbox Live → XSTS → Minecraft servisleri.
    /// Uzun ömürlü yenileme token'ı ve kısa ömürlü Minecraft token'ı platformun korumasıyla şifrelenip (Windows'ta
    /// DPAPI) vulu hesabına bağlı saklanır; başka bir vulu hesabıyla girilince kullanılmaz.
    /// Azure uygulama kimliği sunucudan gelir (GET /auth/microsoft); ayarlı değilse Microsoft girişi kapalıdır.
    /// </summary>
    internal sealed class MicrosoftAuth
    {
        private const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0";
        private const string Scope = "XboxLive.signin offline_access";
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
        private static string StorePath => Path.Combine(AppPaths.AppData, "microsoft.json");

        private sealed class Stored
        {
            public string VuluUser { get; set; } = "";
            public string RefreshToken { get; set; } = "";
            public string McToken { get; set; } = "";
            public DateTime McExpiresUtc { get; set; }
            public string Name { get; set; } = "";
            public string Uuid { get; set; } = "";
        }

        /// <summary>Bu vulu hesabı için kayıtlı Microsoft girişi var mı (yenileme token'ı).</summary>
        public static bool HasLogin(string vuluUser) => Load(vuluUser) != null;

        /// <summary>Kayıtlı Microsoft hesabının Minecraft adı (yoksa null).</summary>
        public static string? StoredName(string vuluUser) => Load(vuluUser)?.Name;

        public static void Forget()
        {
            try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch { }
        }

        // ── Giriş ─────────────────────────────────────────────────────────
        public static async Task<DeviceCode> BeginAsync(string clientId, CancellationToken cancel)
        {
            using var response = await Http.PostAsync($"{Authority}/devicecode", Form(("client_id", clientId), ("scope", Scope)), cancel);
            var json = await ReadJson(response, cancel);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Microsoft girişi başlatılamadı: " + Describe(json));
            return new DeviceCode(json.MsText("user_code") ?? "", json.MsText("verification_uri") ?? "https://microsoft.com/link",
                (int)json.MsNum("expires_in"), Math.Max(2, (int)json.MsNum("interval")), json.MsText("device_code") ?? "");
        }

        /// <summary>Kişi kodu girene kadar bekler; sonra Minecraft oturumunu kurar ve saklar.</summary>
        public static async Task<MicrosoftSession> CompleteAsync(string clientId, DeviceCode code, string vuluUser, CancellationToken cancel)
        {
            var deadline = DateTime.UtcNow.AddSeconds(code.ExpiresIn > 0 ? code.ExpiresIn : 900);
            int interval = code.Interval;
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > deadline) throw new InvalidOperationException("Kodun süresi doldu; tekrar dene.");
                await Task.Delay(TimeSpan.FromSeconds(interval), cancel);

                using var response = await Http.PostAsync($"{Authority}/token", Form(
                    ("grant_type", "urn:ietf:params:oauth:grant-type:device_code"), ("client_id", clientId), ("device_code", code.Code)), cancel);
                var json = await ReadJson(response, cancel);
                if (response.IsSuccessStatusCode)
                    return await FinishAsync(json.MsText("access_token") ?? "", json.MsText("refresh_token") ?? "", vuluUser, cancel);

                switch (json.MsText("error"))
                {
                    case "authorization_pending": continue;
                    case "slow_down": interval += 5; continue;
                    case "authorization_declined": throw new InvalidOperationException("Giriş reddedildi.");
                    case "expired_token": throw new InvalidOperationException("Kodun süresi doldu; tekrar dene.");
                    default: throw new InvalidOperationException("Microsoft girişi başarısız: " + Describe(json));
                }
            }
        }

        /// <summary>
        /// Oyun için geçerli bir Minecraft oturumu: saklanan token geçerliyse o, değilse yenileme token'ıyla yenisi.
        /// Kayıtlı giriş yoksa null.
        /// </summary>
        public static async Task<MicrosoftSession?> GetSessionAsync(string clientId, string vuluUser, CancellationToken cancel)
        {
            var stored = Load(vuluUser);
            if (stored == null) return null;
            if (stored.McExpiresUtc > DateTime.UtcNow.AddMinutes(10) && stored.McToken.Length > 0)
                return new MicrosoftSession(stored.Name, stored.Uuid, stored.McToken);

            using var response = await Http.PostAsync($"{Authority}/token", Form(
                ("grant_type", "refresh_token"), ("client_id", clientId), ("refresh_token", stored.RefreshToken), ("scope", Scope)), cancel);
            var json = await ReadJson(response, cancel);
            if (!response.IsSuccessStatusCode)
            {
                // Yenileme token'ı geçersizse (şifre değişti, izin kaldırıldı) yeniden giriş gerekir
                if (json.MsText("error") == "invalid_grant") Forget();
                throw new InvalidOperationException("Microsoft oturumu yenilenemedi: " + Describe(json));
            }
            return await FinishAsync(json.MsText("access_token") ?? "", json.MsText("refresh_token") ?? stored.RefreshToken, vuluUser, cancel);
        }

        // ── Xbox Live → Minecraft ─────────────────────────────────────────
        private static async Task<MicrosoftSession> FinishAsync(string msToken, string refreshToken, string vuluUser, CancellationToken cancel)
        {
            var xbl = await PostJson("https://user.auth.xboxlive.com/user/authenticate", new
            {
                Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + msToken },
                RelyingParty = "http://auth.xboxlive.com",
                TokenType = "JWT",
            }, null, cancel);
            string xblToken = xbl.json.MsText("Token") ?? throw new InvalidOperationException("Xbox Live girişi başarısız.");

            var xsts = await PostJson("https://xsts.auth.xboxlive.com/xsts/authorize", new
            {
                Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } },
                RelyingParty = "rp://api.minecraftservices.com/",
                TokenType = "JWT",
            }, null, cancel);
            if (!xsts.ok) throw new InvalidOperationException(XstsError(xsts.json));
            string xstsToken = xsts.json.MsText("Token") ?? "";
            string uhs = xsts.json.TryGetProperty("DisplayClaims", out var claims) && claims.TryGetProperty("xui", out var xui) && xui.GetArrayLength() > 0
                ? xui[0].MsText("uhs") ?? "" : "";

            var mc = await PostJson("https://api.minecraftservices.com/authentication/login_with_xbox",
                new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" }, null, cancel);
            if (!mc.ok) throw new InvalidOperationException("Minecraft servislerine giriş yapılamadı: " + Describe(mc.json));
            string mcToken = mc.json.MsText("access_token") ?? "";
            int expiresIn = (int)mc.json.MsNum("expires_in");

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcToken);
            using var profileResponse = await Http.SendAsync(request, cancel);
            if (profileResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException("Bu Microsoft hesabında Minecraft: Java Edition yok.");
            var profile = await ReadJson(profileResponse, cancel);
            if (!profileResponse.IsSuccessStatusCode) throw new InvalidOperationException("Minecraft profili alınamadı: " + Describe(profile));

            var session = new MicrosoftSession(profile.MsText("name") ?? "", (profile.MsText("id") ?? "").ToLowerInvariant(), mcToken);
            Save(new Stored
            {
                VuluUser = vuluUser,
                RefreshToken = refreshToken,
                McToken = mcToken,
                McExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn > 0 ? expiresIn : 3600),
                Name = session.Name,
                Uuid = session.Uuid,
            });
            return session;
        }

        private static string XstsError(JsonElement json) => (long)json.MsNum("XErr") switch
        {
            2148916233 => "Bu Microsoft hesabının Xbox profili yok. minecraft.net'e bir kez giriş yapıp profil oluştur.",
            2148916235 => "Xbox Live bu ülkede kullanılamıyor.",
            2148916236 or 2148916237 => "Bu hesap için Xbox sayfasında yetişkin doğrulaması gerekiyor.",
            2148916238 => "Bu bir çocuk hesabı; bir aile grubuna eklenmesi gerekiyor.",
            _ => "Xbox Live yetkilendirmesi başarısız: " + Describe(json),
        };

        // ── Saklama ───────────────────────────────────────────────────────
        private static Stored? Load(string vuluUser)
        {
            try
            {
                if (!File.Exists(StorePath)) return null;
                var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(StorePath));
                if (stored == null || !string.Equals(stored.VuluUser, vuluUser, StringComparison.OrdinalIgnoreCase)) return null;
                stored.RefreshToken = Decrypt(stored.RefreshToken);
                stored.McToken = Decrypt(stored.McToken);
                return stored.RefreshToken.Length > 0 ? stored : null;
            }
            catch { return null; }
        }

        private static void Save(Stored stored)
        {
            Directory.CreateDirectory(AppPaths.AppData);
            var copy = new Stored
            {
                VuluUser = stored.VuluUser,
                RefreshToken = Encrypt(stored.RefreshToken),
                McToken = Encrypt(stored.McToken),
                McExpiresUtc = stored.McExpiresUtc,
                Name = stored.Name,
                Uuid = stored.Uuid,
            };
            File.WriteAllText(StorePath, JsonSerializer.Serialize(copy));
        }

        private static string Encrypt(string value) =>
            value.Length == 0 ? "" : Convert.ToBase64String(CredentialManager.Protect(Encoding.UTF8.GetBytes(value)));

        private static string Decrypt(string value) =>
            value.Length == 0 ? "" : Encoding.UTF8.GetString(CredentialManager.Unprotect(Convert.FromBase64String(value)));

        // ── HTTP ──────────────────────────────────────────────────────────
        private static FormUrlEncodedContent Form(params (string Key, string Value)[] pairs) =>
            new(pairs.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)));

        private static async Task<(bool ok, JsonElement json)> PostJson(string url, object body, string? bearer, CancellationToken cancel)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var response = await Http.SendAsync(request, cancel);
            return (response.IsSuccessStatusCode, await ReadJson(response, cancel));
        }

        private static async Task<JsonElement> ReadJson(HttpResponseMessage response, CancellationToken cancel)
        {
            string text = await response.Content.ReadAsStringAsync(cancel);
            try { return text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : default; }
            catch (JsonException) { return default; }
        }

        private static string Describe(JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Object) return "beklenmedik cevap";
            string text = json.MsText("error_description") ?? json.MsText("errorMessage") ?? json.MsText("error") ?? json.MsText("Message") ?? "bilinmeyen hata";
            // Microsoft'un açıklamasının sonundaki takip kimlikleri kişiye bir şey söylemez
            int trace = text.IndexOf(" Trace ID:", StringComparison.Ordinal);
            return trace > 0 ? text[..trace] : text;
        }
    }
}

namespace VuluLauncher
{
    internal static class MsJson
    {
        public static string? MsText(this JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        public static double MsNum(this JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    }
}
