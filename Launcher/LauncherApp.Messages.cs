using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VuluLauncher
{
    /// <summary>
    /// Uçtan uca şifreli direkt mesajlar (kriptografi: <see cref="MessageCrypto"/>).
    /// Girişte kimlik anahtarı vulu-api'deki şifreli yedekten açılır (yoksa üretilip yedeklenir). Mesajlar burada
    /// şifrelenip çözülür; sunucu ve arayüz dışındaki hiçbir yer düz metni görmez.
    /// Kişilerin anahtar kimlikleri bu bilgisayarda hatırlanır: değişirse kullanıcı uyarılır (araya giren biri olabilir).
    /// </summary>
    internal sealed partial class LauncherApp
    {
        private static readonly Regex DmName = new(@"^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);
        private static readonly string KnownKeysFile = Path.Combine(AppPaths.AppData, "dm-keys.json");
        private const int DmMaxLength = 2000;

        private readonly object _dmGate = new();
        private ECDiffieHellman? _dmKey;
        private string? _dmPublic, _dmKeyId, _dmUser, _dmError;
        private readonly Dictionary<string, (string PublicKey, string KeyId, DateTime FetchedAt)> _dmPeers = new(StringComparer.OrdinalIgnoreCase);
        private bool _dmPolling;
        private DateTime? _dmSince;

        private sealed class KnownKey
        {
            public string KeyId { get; set; } = "";
            public string PublicKey { get; set; } = "";
            public bool Verified { get; set; }
            /// <summary>Daha önce görülen anahtarlar: eski mesajlar onlarla çözülür.</summary>
            public List<string> Previous { get; set; } = new();
            public bool Changed { get; set; }
        }

        private void RegisterMessageCommands()
        {
            _bridge.On("dm.status", _ => Task.FromResult<object?>(new { ready = _dmKey != null, error = _dmError }));
            _bridge.On("dm.conversations", async _ => await DmConversationsAsync());
            _bridge.On("dm.history", async args => await DmHistoryAsync(args.Str("user"), args.Str("before")));
            _bridge.On("dm.send", async args => await DmSendAsync(args.Str("user"), args.Str("text")));
            _bridge.On("dm.read", async args => { RequireDm(args.Str("user")); return await _api.PostAsync<JsonElement>($"/messages/with/{args.Str("user")}/read", new { }); });
            _bridge.On("dm.delete", async args => { RequireLogin(); return await _api.DeleteAsync<JsonElement>($"/messages/{Uri.EscapeDataString(args.Str("id"))}"); });
            _bridge.On("dm.safety", async args => await DmSafetyAsync(args.Str("user")));
            _bridge.On("dm.verify", args => Task.FromResult<object?>(DmVerify(args.Str("user"))));
        }

        // ── Kimlik anahtarı ───────────────────────────────────────────────
        /// <summary>
        /// Girişten sonra: anahtar yedeği şifreyle açılır. Yedek yoksa yeni anahtar üretilir. Şifre yedeği açmıyorsa
        /// (admin şifreyi sıfırladıysa) yeni anahtar üretilir; eski mesajlar okunamaz hâle gelir.
        /// </summary>
        private async Task SetUpMessagesAsync(string username, string password)
        {
            try
            {
                ECDiffieHellman? key = null;
                try
                {
                    var me = await _api.GetAsync<JsonElement>("/messages/keys/me");
                    var b = me.GetProperty("backup");
                    key = MessageCrypto.Restore(new KeyBackup(b.GetProperty("cipher").GetString()!, b.GetProperty("nonce").GetString()!,
                        b.GetProperty("salt").GetString()!, b.GetProperty("iterations").GetInt32()), password);
                    if (key == null) Log("Mesaj anahtarı şifrenle açılamadı (şifre sıfırlanmış olabilir); yeni anahtar oluşturuldu. Eski mesajlar okunamayacak.", "warn");
                }
                catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound) { }

                if (key == null)
                {
                    key = MessageCrypto.NewKey();
                    await UploadKeyAsync(key, password);
                }
                lock (_dmGate)
                {
                    _dmKey?.Dispose();
                    _dmKey = key;
                    _dmPublic = MessageCrypto.PublicKeyOf(key);
                    _dmKeyId = MessageCrypto.KeyId(_dmPublic);
                    _dmUser = username;
                    _dmError = null;
                }
                StartDmPolling();
            }
            catch (Exception ex)
            {
                _dmError = "Mesajlaşma şu an kullanılamıyor: " + ex.Message;
            }
        }

        private async Task UploadKeyAsync(ECDiffieHellman key, string password)
        {
            var backup = await Task.Run(() => MessageCrypto.Backup(key, password));
            await _api.PutAsync<JsonElement>("/messages/keys/me", new
            {
                publicKey = MessageCrypto.PublicKeyOf(key),
                backup = new { cipher = backup.Cipher, nonce = backup.Nonce, salt = backup.Salt, iterations = backup.Iterations },
            });
        }

        /// <summary>Şifre değişince yedek yeni şifreyle yeniden şifrelenir (anahtar aynı kalır).</summary>
        private async Task RewrapMessageKeyAsync(string newPassword)
        {
            ECDiffieHellman? key;
            lock (_dmGate) key = _dmKey;
            if (key == null) return;
            try { await UploadKeyAsync(key, newPassword); }
            catch (Exception ex) { Log("Mesaj anahtarı yedeği güncellenemedi: " + ex.Message, "warn"); }
        }

        private void RequireDm(string user)
        {
            RequireLogin();
            if (_dmKey == null) throw new InvalidOperationException(_dmError ?? "Mesajlaşma hazırlanıyor, birkaç saniye sonra tekrar dene.");
            if (!DmName.IsMatch(user ?? "")) throw new InvalidOperationException("Kullanıcı geçersiz.");
        }

        // ── Kişilerin anahtarları ─────────────────────────────────────────
        private Dictionary<string, KnownKey> LoadKnown()
        {
            try
            {
                var all = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, KnownKey>>>(File.ReadAllText(KnownKeysFile));
                return all != null && _dmUser != null && all.TryGetValue(_dmUser, out var mine)
                    ? new Dictionary<string, KnownKey>(mine, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
            }
            catch { return new(StringComparer.OrdinalIgnoreCase); }
        }

        private void SaveKnown(Dictionary<string, KnownKey> known)
        {
            Dictionary<string, Dictionary<string, KnownKey>> all;
            try { all = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, KnownKey>>>(File.ReadAllText(KnownKeysFile)) ?? new(); }
            catch { all = new(); }
            all[_dmUser!] = known;
            Directory.CreateDirectory(AppPaths.AppData);
            File.WriteAllText(KnownKeysFile, JsonSerializer.Serialize(all));
        }

        /// <summary>Kişinin güncel açık anahtarı (1 dk önbellek). İlk görülüşte hatırlanır; sonradan değişirse işaretlenir.</summary>
        private async Task<(string PublicKey, string KeyId, KnownKey Known)> PeerKeyAsync(string user, bool fresh = false)
        {
            lock (_dmGate)
                if (!fresh && _dmPeers.TryGetValue(user, out var cached) && DateTime.UtcNow - cached.FetchedAt < TimeSpan.FromMinutes(1))
                    return (cached.PublicKey, cached.KeyId, LoadKnown().GetValueOrDefault(user) ?? new KnownKey { KeyId = cached.KeyId, PublicKey = cached.PublicKey });

            var key = await _api.GetAsync<JsonElement>($"/messages/keys/{user}");
            string pub = key.GetProperty("publicKey").GetString()!;
            string keyId = MessageCrypto.KeyId(pub);        // sunucunun söylediği kimliğe değil, anahtarın kendisine güvenilir
            lock (_dmGate) _dmPeers[user] = (pub, keyId, DateTime.UtcNow);

            var known = LoadKnown();
            if (!known.TryGetValue(user, out var k))
                known[user] = k = new KnownKey { KeyId = keyId, PublicKey = pub };
            else if (k.KeyId != keyId)
            {
                if (!k.Previous.Contains(k.PublicKey)) k.Previous.Add(k.PublicKey);
                k.KeyId = keyId; k.PublicKey = pub; k.Verified = false; k.Changed = true;
            }
            SaveKnown(known);
            return (pub, keyId, k);
        }

        // ── Yazışmalar ────────────────────────────────────────────────────
        private async Task<object?> DmConversationsAsync()
        {
            RequireLogin();
            return await _api.GetAsync<JsonElement>("/messages/conversations");
        }

        private async Task<object?> DmHistoryAsync(string user, string before)
        {
            RequireDm(user);
            string query = string.IsNullOrEmpty(before) ? "" : $"?before={Uri.EscapeDataString(before)}";
            var raw = await _api.GetAsync<List<JsonElement>>($"/messages/with/{user}{query}");

            (string PublicKey, string KeyId, KnownKey Known)? peer = null;
            string? peerError = null;
            try { peer = await PeerKeyAsync(user); }
            catch (ApiException ex) { peerError = ex.Message; }

            var messages = raw.Select(m => DecryptForView(m, user, peer?.Known)).Reverse().ToList();
            return new
            {
                messages,
                peerReady = peer != null,
                peerError,
                keyChanged = peer?.Known.Changed ?? false,
                verified = peer?.Known.Verified ?? false,
            };
        }

        /// <summary>Bir mesajı arayüz için çözer; çözülemezse nedenini söyler.</summary>
        private object DecryptForView(JsonElement m, string peerName, KnownKey? known)
        {
            string from = m.GetProperty("from").GetString()!, to = m.GetProperty("to").GetString()!;
            string senderKeyId = m.GetProperty("senderKeyId").GetString()!, recipientKeyId = m.GetProperty("recipientKeyId").GetString()!;
            bool mine = string.Equals(from, _dmUser, StringComparison.OrdinalIgnoreCase);
            string myKeyIdInMessage = mine ? senderKeyId : recipientKeyId, peerKeyIdInMessage = mine ? recipientKeyId : senderKeyId;

            string? text = null, problem = null;
            if (myKeyIdInMessage != _dmKeyId) problem = "Bu mesaj eski bir anahtarınla şifrelenmiş; okunamıyor.";
            else
            {
                // Kişinin o mesajdaki anahtarı: güncel ya da daha önce görülen biri
                var candidates = known == null ? new List<string>() : new[] { known.PublicKey }.Concat(known.Previous).ToList();
                string? peerPub = candidates.FirstOrDefault(p => MessageCrypto.KeyId(p) == peerKeyIdInMessage);
                if (peerPub == null) problem = "Bu mesaj, kişinin bu bilgisayarın tanımadığı bir anahtarıyla şifrelenmiş.";
                else
                {
                    ECDiffieHellman key;
                    lock (_dmGate) key = _dmKey!;
                    text = MessageCrypto.Decrypt(key, peerPub, from, to, senderKeyId, recipientKeyId,
                        new EncryptedMessage(m.GetProperty("cipher").GetString()!, m.GetProperty("nonce").GetString()!, m.GetProperty("salt").GetString()!));
                    if (text == null) problem = "Bu mesaj çözülemedi (bozulmuş ya da değiştirilmiş olabilir).";
                }
            }
            return new
            {
                id = m.GetProperty("id").GetString(), from, to, mine, text, problem,
                createdAt = m.GetProperty("createdAt").GetString(),
                read = m.TryGetProperty("readAt", out var r) && r.ValueKind == JsonValueKind.String,
                peer = peerName,
            };
        }

        private async Task<object?> DmSendAsync(string user, string text)
        {
            RequireDm(user);
            text = (text ?? "").Trim();
            if (text.Length == 0) throw new InvalidOperationException("Boş mesaj gönderilemez.");
            if (text.Length > DmMaxLength) throw new InvalidOperationException($"Mesaj en fazla {DmMaxLength} karakter olabilir.");

            for (int attempt = 0; ; attempt++)
            {
                var (pub, keyId, known) = await PeerKeyAsync(user, fresh: attempt > 0);
                ECDiffieHellman mineKey; string myKeyId, me;
                lock (_dmGate) { mineKey = _dmKey!; myKeyId = _dmKeyId!; me = _dmUser!; }
                var enc = MessageCrypto.Encrypt(mineKey, pub, me, user, myKeyId, keyId, text);
                try
                {
                    var sent = await _api.PostAsync<JsonElement>("/messages", new { to = user, cipher = enc.Cipher, nonce = enc.Nonce, salt = enc.Salt, senderKeyId = myKeyId, recipientKeyId = keyId });
                    return new { message = DecryptForView(sent, user, known), keyChanged = known.Changed };
                }
                catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.Conflict && attempt == 0) { /* anahtar değişmiş: güncelini alıp yeniden şifrele */ }
            }
        }

        // ── Güvenlik kodu ─────────────────────────────────────────────────
        private async Task<object?> DmSafetyAsync(string user)
        {
            RequireDm(user);
            var (pub, _, known) = await PeerKeyAsync(user, fresh: true);
            return new { code = MessageCrypto.SafetyCode(_dmPublic!, pub), verified = known.Verified, changed = known.Changed };
        }

        /// <summary>Kullanıcı güvenlik kodunu karşı tarafla karşılaştırıp onayladı: anahtar "doğrulandı" sayılır, uyarı kalkar.</summary>
        private object DmVerify(string user)
        {
            RequireDm(user);
            var known = LoadKnown();
            if (!known.TryGetValue(user, out var k)) throw new InvalidOperationException("Önce bir mesaj aç.");
            k.Verified = true; k.Changed = false;
            SaveKnown(known);
            return new { verified = true };
        }

        // ── Yeni mesajları dinleme ────────────────────────────────────────
        /// <summary>Launcher açıkken birkaç saniyede bir yeni mesajlar alınır; gelenler çözülüp arayüze gönderilir.</summary>
        private void StartDmPolling()
        {
            lock (_dmGate)
            {
                if (_dmPolling) return;
                _dmPolling = true;
            }
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(_login.Token) && _dmKey != null)
                        {
                            string query = _dmSince is { } since ? $"?since={Uri.EscapeDataString(since.ToString("o"))}" : "";
                            var update = await _api.GetAsync<JsonElement>("/messages/updates" + query);
                            if (update.TryGetProperty("now", out var now) && now.TryGetDateTime(out var at)) _dmSince = at.ToUniversalTime();
                            var incoming = update.GetProperty("messages").EnumerateArray().ToList();
                            var views = new List<object>();
                            foreach (var group in incoming.GroupBy(m => m.GetProperty("from").GetString()!))
                            {
                                KnownKey? known = null;
                                try { known = (await PeerKeyAsync(group.Key)).Known; } catch { }
                                views.AddRange(group.Select(m => DecryptForView(m, group.Key, known)));
                            }
                            _bridge.Emit("dmUpdate", new { messages = views, unread = update.GetProperty("unread").GetInt32() });
                        }
                    }
                    catch (Exception) { /* bağlantı yoksa bir sonraki turda */ }
                    await Task.Delay(TimeSpan.FromSeconds(4));
                }
            });
        }
    }
}
