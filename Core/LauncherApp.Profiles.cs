using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Profiller ve topluluk: kullanıcılar birbirinin profilini görür, kendi avatarını, açıklamasını
    /// ve şifresini değiştirir. Hepsi vulu-api üzerinden; sunucunun cevabı arayüze olduğu gibi iletilir.
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private static readonly Dictionary<string, string> AvatarTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp", [".gif"] = "image/gif"
        };

        private void RegisterProfileCommands()
        {
            _bridge.On("profile.list", async _ => { RequireLogin(); return await _api.GetAsync<JsonElement>("/users"); });
            _bridge.On("profile.me", async _ => { RequireLogin(); return await _api.GetAsync<JsonElement>("/users/me"); });
            _bridge.On("profile.get", async args =>
            {
                RequireLogin();
                return await _api.GetAsync<JsonElement>("/users/" + Uri.EscapeDataString(args.Str("username")));
            });
            _bridge.On("profile.saveBio", async args =>
            {
                RequireLogin();
                return await _api.PutAsync<JsonElement>("/users/me", new { bio = args.Str("bio") });
            });
            _bridge.On("profile.pickAvatar", async _ => await PickAvatarAsync());
            _bridge.On("profile.uploadAvatar", async args => await UploadAvatarAsync(args.Str("dataUrl")));
            _bridge.On("profile.removeAvatar", async _ => { RequireLogin(); return await _api.DeleteAsync<JsonElement>("/users/me/avatar"); });
            _bridge.On("profile.pickBanner", async _ => await PickBannerAsync());
            _bridge.On("profile.uploadBanner", async args => await UploadBannerAsync(args.Str("dataUrl")));
            _bridge.On("profile.removeBanner", async _ => { RequireLogin(); return await _api.DeleteAsync<JsonElement>("/users/me/banner"); });
            _bridge.On("profile.changePassword", async args => await ChangePasswordAsync(args));

            // Skin: 64×64 PNG; beden tipi "default" (Klasik/Steve) ya da "slim" (İnce/Alex)
            _bridge.On("profile.pickSkin", async args => await PickSkinAsync(args.Str("model") == "slim" ? "slim" : "default"));
            _bridge.On("profile.setSkinModel", async args =>
            {
                RequireLogin();
                return await _api.PutAsync<JsonElement>("/users/me/skin/model", new { model = args.Str("model") == "slim" ? "slim" : "default" });
            });
            _bridge.On("profile.removeSkin", async _ => { RequireLogin(); return await _api.DeleteAsync<JsonElement>("/users/me/skin"); });
        }

        /// <summary>Skin dosyası seçtirir ve yükler (boyutu sunucu da denetler). Vazgeçilirse null.</summary>
        private async Task<object?> PickSkinAsync(string model)
        {
            RequireLogin();
            string? path = await _host.PickFileAsync("Skin seçin (64×64 PNG)", "PNG", new[] { "png" });
            if (path == null) return null;

            if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Skin bir PNG dosyası olmalı.");
            if (new FileInfo(path).Length > 64 * 1024)
                throw new InvalidOperationException("Skin dosyası 64 KB'tan büyük olamaz.");

            return await _api.PutBytesAsync<JsonElement>($"/users/me/skin?model={model}", await File.ReadAllBytesAsync(path), "image/png");
        }

        /// <summary>Resim seçtirir ve yükler. Vazgeçilirse null döner.</summary>
        private const long AvatarUploadMaxBytes = 1024 * 1024;

        /// <summary>
        /// Profil resmi seçtirir ve arayüze data adresi olarak verir: arayüz kırptırıp küçültür, sonra
        /// profile.uploadAvatar ile yükler (GIF'ler hareketli kalsın diye olduğu gibi gider). Vazgeçilirse null.
        /// </summary>
        private async Task<object?> PickAvatarAsync()
        {
            RequireLogin();
            string? path = await _host.PickFileAsync("Profil resmi seçin", "Resimler", new[] { "png", "jpg", "jpeg", "webp", "gif" });
            if (path == null) return null;

            if (!AvatarTypes.TryGetValue(Path.GetExtension(path), out var contentType))
                throw new InvalidOperationException("Desteklenen biçimler: PNG, JPG, WEBP, GIF.");
            long size = new FileInfo(path).Length;
            if (size > BannerSourceMaxBytes) throw new InvalidOperationException("Resim çok büyük (en fazla 25 MB).");
            if (contentType == "image/gif" && size > AvatarUploadMaxBytes)
                throw new InvalidOperationException("Hareketli GIF profil resmi en fazla 1 MB olabilir.");
            return new { dataUrl = $"data:{contentType};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(path))}", type = contentType, size };
        }

        private async Task<object?> UploadAvatarAsync(string dataUrl)
        {
            RequireLogin();
            var match = System.Text.RegularExpressions.Regex.Match(dataUrl ?? "", "^data:(image/(?:png|jpeg|webp|gif));base64,([A-Za-z0-9+/]+={0,2})$");
            if (!match.Success) throw new InvalidOperationException("Profil resmi hazırlanamadı.");
            byte[] bytes = Convert.FromBase64String(match.Groups[2].Value);
            if (bytes.Length > AvatarUploadMaxBytes) throw new InvalidOperationException("Profil resmi 1 MB'tan büyük olamaz.");
            return await _api.PutBytesAsync<JsonElement>("/users/me/avatar", bytes, match.Groups[1].Value);
        }

        private const long BannerSourceMaxBytes = 25 * 1024 * 1024, BannerUploadMaxBytes = 1536 * 1024;

        /// <summary>
        /// Banner resmi seçtirir ve arayüze data adresi olarak verir: arayüz büyük resimleri yüklemeden önce küçültür
        /// (GIF'ler hareketli kalsın diye olduğu gibi gönderilir). Vazgeçilirse null.
        /// </summary>
        private async Task<object?> PickBannerAsync()
        {
            RequireLogin();
            string? path = await _host.PickFileAsync("Banner resmi seçin", "Resimler", new[] { "png", "jpg", "jpeg", "webp", "gif" });
            if (path == null) return null;

            if (!AvatarTypes.TryGetValue(Path.GetExtension(path), out var contentType))
                throw new InvalidOperationException("Desteklenen biçimler: PNG, JPG, WEBP, GIF.");
            long size = new FileInfo(path).Length;
            if (size > BannerSourceMaxBytes) throw new InvalidOperationException("Resim çok büyük (en fazla 25 MB).");
            if (contentType == "image/gif" && size > BannerUploadMaxBytes)
                throw new InvalidOperationException("Hareketli GIF banner en fazla 1,5 MB olabilir.");
            return new { dataUrl = $"data:{contentType};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(path))}", type = contentType, size };
        }

        private async Task<object?> UploadBannerAsync(string dataUrl)
        {
            RequireLogin();
            var match = System.Text.RegularExpressions.Regex.Match(dataUrl ?? "", "^data:(image/(?:png|jpeg|webp|gif));base64,([A-Za-z0-9+/]+={0,2})$");
            if (!match.Success) throw new InvalidOperationException("Banner hazırlanamadı.");
            byte[] bytes = Convert.FromBase64String(match.Groups[2].Value);
            if (bytes.Length > BannerUploadMaxBytes) throw new InvalidOperationException("Banner 1,5 MB'tan büyük olamaz.");
            return await _api.PutBytesAsync<JsonElement>("/users/me/banner", bytes, match.Groups[1].Value);
        }

        private async Task<object?> ChangePasswordAsync(JsonElement args)
        {
            RequireLogin();
            string current = args.Str("currentPassword");
            string next = args.Str("newPassword");

            var result = await _api.PostAsync<JsonElement>("/users/me/password", new { currentPassword = current, newPassword = next });
            // Sunucu bu hesabın bütün eski oturumlarını kapatır; bu oturum yeni token'la devam eder
            if (result.TryGetProperty("token", out var token) && token.GetString() is { Length: > 0 } fresh)
                _login.UpdateToken(fresh);

            // "Beni hatırla" ile kayıtlıysa yeni şifre kaydedilir; yoksa bir sonraki girişte eski şifre denenirdi
            var saved = CredentialManager.Load();
            if (string.Equals(saved.Username, _login.Username, StringComparison.Ordinal) && !string.IsNullOrEmpty(saved.Password))
                CredentialManager.Save(_login.Username, next);

            await RewrapMessageKeyAsync(next);   // mesaj anahtarının yedeği yeni şifreyle
            Log("Şifren değiştirildi.", "ok");
            return new { success = true };
        }
    }
}
