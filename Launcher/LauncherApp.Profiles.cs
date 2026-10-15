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
            _bridge.On("profile.removeAvatar", async _ => { RequireLogin(); return await _api.DeleteAsync<JsonElement>("/users/me/avatar"); });
            _bridge.On("profile.changePassword", async args => await ChangePasswordAsync(args));
        }

        /// <summary>Resim seçtirir ve yükler. Vazgeçilirse null döner.</summary>
        private async Task<object?> PickAvatarAsync()
        {
            RequireLogin();
            var files = _window.ShowOpenFile("Profil resmi seçin", null, false,
                new[] { ("Resimler", new[] { "png", "jpg", "jpeg", "webp", "gif" }) });
            if (files is not { Length: > 0 }) return null;

            string path = files[0];
            if (!AvatarTypes.TryGetValue(Path.GetExtension(path), out var contentType))
                throw new InvalidOperationException("Desteklenen biçimler: PNG, JPG, WEBP, GIF.");
            if (new FileInfo(path).Length > 1024 * 1024)
                throw new InvalidOperationException("Profil resmi 1 MB'tan büyük olamaz.");

            return await _api.PutBytesAsync<JsonElement>("/users/me/avatar", await File.ReadAllBytesAsync(path), contentType);
        }

        private async Task<object?> ChangePasswordAsync(JsonElement args)
        {
            RequireLogin();
            string current = args.Str("currentPassword");
            string next = args.Str("newPassword");

            await _api.PostAsync<JsonElement>("/users/me/password", new { currentPassword = current, newPassword = next });

            // "Beni hatırla" ile kayıtlıysa yeni şifre kaydedilir; yoksa bir sonraki girişte eski şifre denenirdi
            var saved = CredentialManager.Load();
            if (string.Equals(saved.Username, _login.Username, StringComparison.Ordinal) && !string.IsNullOrEmpty(saved.Password))
                CredentialManager.Save(_login.Username, next);

            Log("Şifren değiştirildi.", "ok");
            return new { success = true };
        }
    }
}
