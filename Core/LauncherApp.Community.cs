using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// Topluluk: arkadaşların çevrimiçi durumu (presence) ve vulu sunucuları.
    /// Launcher, giriş yapıldıktan sonra 45 sn'de bir ve durum değişince vulu-api'ye ne yaptığını bildirir
    /// (Ayarlar'daki gizlilik seçimine göre: ne oynadığı / yalnızca çevrimiçi / hiç).
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private static readonly TimeSpan PresenceEvery = TimeSpan.FromSeconds(45);
        private readonly SemaphoreSlim _presenceChanged = new(0, 1);
        private readonly object _presenceGate = new();
        private object? _presencePlaying;           // null = launcher'da
        private bool _presenceStarted;

        private void RegisterCommunityCommands()
        {
            _bridge.On("community.presence", async _ => { RequireLogin(); return await _api.GetAsync<JsonElement>("/users/presence"); });
            _bridge.On("community.servers", async _ => await ListServersAsync());
            _bridge.On("community.ping", async args => await PingServerAsync(args.Str("address")));
            _bridge.On("servers.save", async args => await SaveServerAsync(args));
            _bridge.On("servers.delete", async args =>
            {
                RequireAdmin();
                return await _api.DeleteAsync<JsonElement>($"/servers/{Uri.EscapeDataString(args.Str("id"))}");
            });
        }

        // ── Çevrimiçi durum ───────────────────────────────────────────────
        /// <summary>Girişten sonra bir kez başlar; launcher kapanana kadar sürer.</summary>
        private void StartPresence()
        {
            lock (_presenceGate)
            {
                if (_presenceStarted) return;
                _presenceStarted = true;
            }
            _ = Task.Run(async () =>
            {
                bool wasHidden = false;
                while (true)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(_login.Token))
                        {
                            string mode = AppSettings.Current.PresenceMode;
                            if (mode == "hidden")
                            {
                                if (!wasHidden) await _api.DeleteAsync<JsonElement>("/users/me/presence");
                                wasHidden = true;
                            }
                            else
                            {
                                wasHidden = false;
                                object? playing;
                                lock (_presenceGate) playing = _presencePlaying;
                                object body = mode == "online" ? new { state = "online" } : playing ?? new { state = "launcher" };
                                await _api.PostAsync<JsonElement>("/users/me/presence", body);
                            }
                            await CheckPlayingAsync();
                        }
                    }
                    catch (Exception) { /* sunucuya ulaşılamazsa bir sonraki turda denenir */ }

                    try { await _presenceChanged.WaitAsync(PresenceEvery); } catch { }
                    await Task.Delay(TimeSpan.FromSeconds(2));   // art arda gelen değişiklikler birleşir
                }
            });
        }

        private void SignalPresence()
        {
            try { _presenceChanged.Release(); } catch (SemaphoreFullException) { }
        }

        private void PresencePlaying(LaunchTarget target, string? packId, string? server)
        {
            lock (_presenceGate)
                _presencePlaying = new { state = "playing", instance = target.Name, mcVersion = target.McVersion, loader = target.Loader, packId, server };
            SignalPresence();
        }

        private void PresenceIdle()
        {
            lock (_presenceGate) _presencePlaying = null;
            SignalPresence();
        }

        // ── Sunucular ─────────────────────────────────────────────────────
        /// <summary>
        /// Sunucu listesi ve her sunucunun bu bilgisayarda hangi instance'la açılacağı:
        /// bağlı vulu paketi kuruluysa o instance, kurulu değilse paketin kurulması gerekir; paket yoksa Vanilla.
        /// </summary>
        /// <summary>Katılınan adres vulu sunucu listesindeyse adı (Discord'da adres yerine gösterilir); bulunamazsa null.</summary>
        private async Task<string?> ServerNameAsync(string? address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            try
            {
                var lookup = _api.GetAsync<List<JsonElement>>("/servers");
                if (await Task.WhenAny(lookup, Task.Delay(TimeSpan.FromSeconds(4))) != lookup) return null;
                return (await lookup).FirstOrDefault(s => string.Equals(s.GetProperty("address").GetString(), address, StringComparison.OrdinalIgnoreCase)) is { ValueKind: JsonValueKind.Object } match
                    ? match.GetProperty("name").GetString() : null;
            }
            catch { return null; }
        }

        private async Task<object?> ListServersAsync()
        {
            RequireLogin();
            var servers = await _api.GetAsync<List<JsonElement>>("/servers");
            var installed = InstanceStore.List().Where(m => m.SharedPackId != null).ToDictionary(m => m.SharedPackId!, m => m);
            return servers.Select(s =>
            {
                string? packId = s.TryGetProperty("packId", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                installed.TryGetValue(packId ?? "", out var meta);
                return new
                {
                    id = s.GetProperty("id").GetString(),
                    name = s.GetProperty("name").GetString(),
                    address = s.GetProperty("address").GetString(),
                    description = s.TryGetProperty("description", out var d) ? d.GetString() : "",
                    packId,
                    mcVersion = s.TryGetProperty("mcVersion", out var v) ? v.GetString() : "",
                    // Bağlan: hangi instance açılacak (null + packId = önce paket kurulmalı)
                    instanceId = packId == null ? PackPrefix + "vanilla" : meta == null ? null : InstancePrefix + meta.Folder,
                };
            }).ToList();
        }

        private async Task<object?> PingServerAsync(string address)
        {
            RequireLogin();
            var (host, _) = ServerPing.Split(address ?? "");
            if (!System.Text.RegularExpressions.Regex.IsMatch(host, @"^[A-Za-z0-9](?:[A-Za-z0-9.\-]{0,118}[A-Za-z0-9])?$"))
                throw new InvalidOperationException("Geçersiz adres.");
            var status = await ServerPing.PingAsync(address!);
            return new { online = status.Online, players = status.Players, maxPlayers = status.MaxPlayers, version = status.Version, motd = status.Motd, ping = status.PingMs, sample = status.Sample };
        }

        private async Task<object?> SaveServerAsync(JsonElement args)
        {
            RequireAdmin();
            var body = new
            {
                name = args.Str("name"), address = args.Str("address"), description = args.Str("description"),
                packId = string.IsNullOrWhiteSpace(args.Str("packId")) ? null : args.Str("packId"),
                mcVersion = args.Str("mcVersion"),
            };
            string id = args.Str("id");
            return string.IsNullOrEmpty(id)
                ? await _api.PostAsync<JsonElement>("/servers", body)
                : await _api.PutAsync<JsonElement>($"/servers/{Uri.EscapeDataString(id)}", body);
        }
    }
}
