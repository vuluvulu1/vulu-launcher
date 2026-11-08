using System.Net;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Microsoft hesabı: vulu hesabına bağlanır, sunucu Minecraft sahipliğini doğrular. vulu paketleri dışındaki
    /// instance'lar bu hesabın gerçek oturumuyla açılır; vulu paketleri vulu oturumu ve skin sistemiyle açılır.
    /// "Çevrimdışı oyna" seçiliyse her şey vulu oturumuyla açılır; bu seçenek yalnızca sahipliği doğrulanmış
    /// hesaplarda vardır (Minecraft'a sahip olmayan biri çevrimdışı oynayamasın).
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private string? _msClientId;
        private bool _msConfigLoaded;
        private CancellationTokenSource? _msLogin;

        private void RegisterMicrosoftCommands()
        {
            _bridge.On("ms.status", async _ => await MicrosoftStatusAsync());

            // Cihaz kodu alınır ve döner; kişi kodu girince "msLogin" olayı gelir (done ya da error)
            _bridge.On("ms.begin", async args =>
            {
                RequireLogin();
                string clientId = await MicrosoftClientIdAsync()
                    ?? throw new InvalidOperationException("Microsoft girişi bu sunucuda henüz açılmadı.");
                _msLogin?.Cancel();
                var cancel = _msLogin = new CancellationTokenSource(TimeSpan.FromMinutes(16));
                var code = await MicrosoftAuth.BeginAsync(clientId, cancel.Token);
                string vuluUser = _login.Username;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var session = await MicrosoftAuth.CompleteAsync(clientId, code, vuluUser, cancel.Token);
                        await _api.PostAsync<JsonElement>("/users/me/minecraft", new { accessToken = session.AccessToken });
                        Log($"Microsoft hesabı bağlandı: {session.Name}", "ok");
                        _bridge.Emit("msLogin", new { state = "done", name = session.Name });
                    }
                    catch (OperationCanceledException) { _bridge.Emit("msLogin", new { state = "cancelled" }); }
                    catch (Exception ex)
                    {
                        MicrosoftAuth.Forget();
                        _bridge.Emit("msLogin", new { state = "error", error = ex.Message });
                    }
                });
                _host.OpenUrl(code.VerificationUri);
                return new { userCode = code.UserCode, verificationUri = code.VerificationUri, expiresIn = code.ExpiresIn };
            });
            _bridge.On("ms.cancel", _ => { _msLogin?.Cancel(); return Task.FromResult<object?>(null); });

            _bridge.On("ms.unlink", async args =>
            {
                RequireLogin();
                await _api.DeleteAsync<JsonElement>("/users/me/minecraft");
                MicrosoftAuth.Forget();
                AppSettings.Current.PlayOffline = false;
                AppSettings.Save(AppSettings.Current);
                Log("Microsoft hesabının bağlantısı kaldırıldı.", "muted");
                return await MicrosoftStatusAsync();
            });

            _bridge.On("ms.setOffline", async args =>
            {
                RequireLogin();
                bool offline = args.Bool("offline");
                var me = await _api.GetAsync<JsonElement>("/users/me");
                if (offline && !HasVerifiedMinecraft(me))
                    throw new InvalidOperationException("Çevrimdışı oynamak için önce Minecraft'a sahip bir Microsoft hesabı bağla.");
                AppSettings.Current.PlayOffline = offline;
                AppSettings.Save(AppSettings.Current);
                return await MicrosoftStatusAsync();
            });
        }

        private async Task<object> MicrosoftStatusAsync()
        {
            RequireLogin();
            string? clientId = await MicrosoftClientIdAsync();
            JsonElement me = default;
            try { me = await _api.GetAsync<JsonElement>("/users/me"); } catch { /* durum bağlantısız gösterilir */ }
            bool linked = HasVerifiedMinecraft(me);
            string? name = linked && me.GetProperty("minecraft").TryGetProperty("name", out var n) ? n.GetString() : null;
            bool required = me.ValueKind == JsonValueKind.Object && me.TryGetProperty("ownershipRequired", out var r) && r.ValueKind == JsonValueKind.True;
            return new
            {
                available = clientId != null,
                linked,
                name,
                // Bu bilgisayarda oturum var mı (yoksa oyun vulu oturumuyla açılır; yeniden giriş istenir)
                signedIn = MicrosoftAuth.HasLogin(_login.Username),
                offline = AppSettings.Current.PlayOffline,
                ownershipRequired = required,
            };
        }

        private static bool HasVerifiedMinecraft(JsonElement me) =>
            me.ValueKind == JsonValueKind.Object && me.TryGetProperty("minecraft", out var mc) && mc.ValueKind == JsonValueKind.Object
            && mc.TryGetProperty("uuid", out var uuid) && uuid.ValueKind == JsonValueKind.String;

        /// <summary>Azure uygulama kimliği sunucudan bir kez alınır; ayarlı değilse null (Microsoft girişi kapalı).</summary>
        private async Task<string?> MicrosoftClientIdAsync()
        {
            if (_msConfigLoaded) return _msClientId;
            try
            {
                var config = await _api.GetAsync<JsonElement>("/auth/microsoft");
                _msClientId = config.TryGetProperty("clientId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } s ? s : null;
                _msConfigLoaded = true;
            }
            catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound) { _msConfigLoaded = true; }
            catch { /* bir sonraki denemede yeniden sorulur */ }
            return _msClientId;
        }

        /// <summary>vulu paketleri (sunucudan gelen ya da vulu Packs'ten kurulan) vulu oturumu ve skinleriyle açılır.</summary>
        private static bool UsesVuluSession(string instanceId) =>
            AppSettings.Current.PlayOffline
            || instanceId.StartsWith(PackPrefix, StringComparison.Ordinal)
            || (instanceId.StartsWith(InstancePrefix, StringComparison.Ordinal) && InstanceStore.Get(instanceId[InstancePrefix.Length..])?.SharedPackId != null);

        /// <summary>
        /// Oyun için Microsoft oturumu; bu bilgisayarda giriş yoksa ya da yenilenemezse null (oyun vulu oturumuyla
        /// açılır, sahiplik zorunluysa sunucu yine denetler).
        /// </summary>
        private async Task<MicrosoftSession?> MicrosoftSessionForGameAsync()
        {
            try
            {
                if (!MicrosoftAuth.HasLogin(_login.Username)) return null;
                string? clientId = await MicrosoftClientIdAsync();
                if (clientId == null) return null;
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var session = await MicrosoftAuth.GetSessionAsync(clientId, _login.Username, cancel.Token);
                if (session != null) Log($"Microsoft hesabıyla açılıyor: {session.Name}", "muted");
                return session;
            }
            catch (Exception ex)
            {
                Log("Microsoft oturumu kullanılamadı; oyun vulu hesabıyla açılıyor. (" + ex.Message + ")", "warn");
                return null;
            }
        }
    }
}
