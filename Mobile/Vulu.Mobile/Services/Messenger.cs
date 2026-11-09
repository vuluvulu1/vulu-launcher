using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Threading;
using VuluLauncher;

namespace VuluMobile.Services
{
    /// <summary>Ekranda gösterilecek, çözülmüş bir mesaj.</summary>
    internal sealed record ChatMessage(string Id, string From, string To, bool Mine, string? Text, string? Problem,
        DateTime CreatedAt, bool Read, string Peer);

    /// <summary>
    /// Uçtan uca şifreli mesajlar, launcher'la aynı biçimde (<see cref="MessageCrypto"/>, Core/LauncherApp.Messages.cs).
    /// Kimlik anahtarı sunucudaki şifreli yedekten şifreyle açılır. Telefon yedeği açamazsa yeni anahtar ÜRETMEZ
    /// (launcher'daki mesaj geçmişini okunmaz hâle getirirdi); yalnızca hiç anahtar yoksa üretir.
    /// Kişilerin anahtar kimlikleri telefonda hatırlanır: değişirse sohbette uyarı çıkar.
    /// </summary>
    internal sealed class Messenger
    {
        private readonly ApiClient api;

        public Messenger(ApiClient api)
        {
            this.api = api;
            PushHub.MessagePushed += () => _wake.TrySetResult();
        }

        private const int MaxLength = 2000;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private static string KnownFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vulu-sosyal", "dm-keys.json");

        private readonly object _gate = new();
        private ECDiffieHellman? _key;
        private string? _keyId, _me, _public;
        private readonly Dictionary<string, (string PublicKey, string KeyId, DateTime At)> _peers = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _poll;
        private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DateTime? _since;

        public bool Ready => _key != null;
        public string? Error { get; private set; }
        public int Unread { get; private set; }

        /// <summary>Yeni gelen mesajlar (UI iş parçacığında).</summary>
        public event Action<IReadOnlyList<ChatMessage>>? Received;
        /// <summary>Okunmamış toplamı ya da hazır olma durumu değişti (UI iş parçacığında).</summary>
        public event Action? StateChanged;

        private sealed class KnownKey
        {
            public string KeyId { get; set; } = "";
            public string PublicKey { get; set; } = "";
            public List<string> Previous { get; set; } = new();
            public bool Changed { get; set; }
            /// <summary>Güvenlik kodu karşı tarafla karşılaştırılıp onaylandı.</summary>
            public bool Verified { get; set; }
        }

        public async Task SetUpAsync(string username, string password)
        {
            Stop();
            try
            {
                ECDiffieHellman? key;
                try
                {
                    var me = await api.GetAsync<JsonElement>("/messages/keys/me");
                    var b = me.GetProperty("backup");
                    var backup = new KeyBackup(b.GetProperty("cipher").GetString()!, b.GetProperty("nonce").GetString()!,
                        b.GetProperty("salt").GetString()!, b.GetProperty("iterations").GetInt32());
                    key = await Task.Run(() => MessageCrypto.Restore(backup, password))
                        ?? throw new InvalidOperationException("mesaj anahtarın bu şifreyle açılamadı. Launcher'dan bir kez giriş yapıp tekrar dene.");
                }
                catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound)
                {
                    key = MessageCrypto.NewKey();
                    var fresh = key;
                    var backup = await Task.Run(() => MessageCrypto.Backup(fresh, password));
                    await api.PutAsync<JsonElement>("/messages/keys/me", new
                    {
                        publicKey = MessageCrypto.PublicKeyOf(key),
                        backup = new { cipher = backup.Cipher, nonce = backup.Nonce, salt = backup.Salt, iterations = backup.Iterations },
                    });
                }
                lock (_gate)
                {
                    _key?.Dispose();
                    _key = key;
                    _public = MessageCrypto.PublicKeyOf(key);
                    _keyId = MessageCrypto.KeyId(_public);
                    _me = username;
                    _peers.Clear();
                }
                Error = null;
                StartPolling();
            }
            catch (Exception ex) { Error = "Mesajlaşma şu an kullanılamıyor: " + ex.Message; }
            finally { Dispatcher.UIThread.Post(() => StateChanged?.Invoke()); }
        }

        public void Stop()
        {
            _poll?.Cancel();
            _poll = null;
            lock (_gate) { _key?.Dispose(); _key = null; _keyId = null; _me = null; _public = null; }
            _since = null;
            Unread = 0;
        }

        // ── Kişilerin anahtarları ─────────────────────────────────────────
        private Dictionary<string, KnownKey> LoadKnown()
        {
            try
            {
                var all = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, KnownKey>>>(File.ReadAllText(KnownFile));
                if (all != null && _me != null && all.TryGetValue(_me, out var mine)) return new(mine, StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return new(StringComparer.OrdinalIgnoreCase);
        }

        private void SaveKnown(Dictionary<string, KnownKey> known)
        {
            if (_me == null) return;
            try
            {
                Dictionary<string, Dictionary<string, KnownKey>> all;
                try { all = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, KnownKey>>>(File.ReadAllText(KnownFile)) ?? new(); }
                catch { all = new(); }
                all[_me] = known;
                Directory.CreateDirectory(Path.GetDirectoryName(KnownFile)!);
                File.WriteAllText(KnownFile, JsonSerializer.Serialize(all));
            }
            catch { }
        }

        private async Task<(string PublicKey, string KeyId, KnownKey Known)> PeerKeyAsync(string user, bool fresh = false)
        {
            lock (_gate)
                if (!fresh && _peers.TryGetValue(user, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(1))
                    return (c.PublicKey, c.KeyId, LoadKnown().GetValueOrDefault(user) ?? new KnownKey { KeyId = c.KeyId, PublicKey = c.PublicKey });

            var key = await api.GetAsync<JsonElement>($"/messages/keys/{Uri.EscapeDataString(user)}");
            string pub = key.GetProperty("publicKey").GetString()!;
            string keyId = MessageCrypto.KeyId(pub);   // sunucunun söylediğine değil, anahtarın kendisine güvenilir
            lock (_gate) _peers[user] = (pub, keyId, DateTime.UtcNow);

            var known = LoadKnown();
            if (!known.TryGetValue(user, out var k)) known[user] = k = new KnownKey { KeyId = keyId, PublicKey = pub };
            else if (k.KeyId != keyId)
            {
                if (!k.Previous.Contains(k.PublicKey)) k.Previous.Add(k.PublicKey);
                k.KeyId = keyId; k.PublicKey = pub; k.Changed = true; k.Verified = false;
            }
            SaveKnown(known);
            return (pub, keyId, k);
        }

        /// <summary>Uyarıyı kapat: kullanıcı kişinin yeni anahtarını kabul etti.</summary>
        public void AcceptKeyChange(string user)
        {
            var known = LoadKnown();
            if (known.TryGetValue(user, out var k)) { k.Changed = false; SaveKnown(known); }
        }

        /// <summary>
        /// İki kişinin karşılaştıracağı güvenlik kodu (launcher'dakiyle aynı: 30 rakam, 6 grup). Aynıysa araya giren
        /// biri yoktur; kullanıcı onaylayınca kişi "doğrulandı" sayılır.
        /// </summary>
        public async Task<(string Code, bool Verified)> SafetyAsync(string user)
        {
            var (pub, _, known) = await PeerKeyAsync(user, fresh: true);
            string mine = _public ?? throw new InvalidOperationException(Error ?? "Mesajlaşma hazır değil.");
            return (MessageCrypto.SafetyCode(mine, pub), known.Verified);
        }

        public void Verify(string user)
        {
            var known = LoadKnown();
            if (!known.TryGetValue(user, out var k)) return;
            k.Verified = true; k.Changed = false;
            SaveKnown(known);
        }

        // ── Yazışmalar ────────────────────────────────────────────────────
        public sealed record History(List<ChatMessage> Messages, string? PeerError, bool KeyChanged, bool More, bool Verified = false);

        public async Task<History> HistoryAsync(string user, DateTime? before = null)
        {
            string query = before is { } b ? $"?before={Uri.EscapeDataString(b.ToUniversalTime().ToString("o"))}" : "";
            var raw = await api.GetAsync<List<JsonElement>>($"/messages/with/{Uri.EscapeDataString(user)}{query}");
            (string PublicKey, string KeyId, KnownKey Known)? peer = null;
            string? peerError = null;
            try { peer = await PeerKeyAsync(user); }
            catch (ApiException ex) { peerError = ex.Message; }
            var list = raw.Select(m => Decrypt(m, user, peer?.Known)).Reverse().ToList();
            return new History(list, peerError, peer?.Known.Changed ?? false, raw.Count >= 50, peer?.Known.Verified ?? false);
        }

        public async Task<ChatMessage> SendAsync(string user, string text)
        {
            text = text.Trim();
            if (_key == null) throw new InvalidOperationException(Error ?? "Mesajlaşma hazırlanıyor, birkaç saniye sonra tekrar dene.");
            if (text.Length == 0) throw new InvalidOperationException("Boş mesaj gönderilemez.");
            if (text.Length > MaxLength) throw new InvalidOperationException($"Mesaj en fazla {MaxLength} karakter olabilir.");
            // Zarf gibi başlayan düz metin zarfa konur; yoksa karşı tarafta yanlışlıkla zarf sanılır
            string plain = text.StartsWith("{\"v\":", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { v = 1, text }) : text;

            for (int attempt = 0; ; attempt++)
            {
                var (pub, keyId, known) = await PeerKeyAsync(user, fresh: attempt > 0);
                ECDiffieHellman mine; string myKeyId, me;
                lock (_gate) { mine = _key!; myKeyId = _keyId!; me = _me!; }
                var enc = MessageCrypto.Encrypt(mine, pub, me, user, myKeyId, keyId, plain);
                try
                {
                    var sent = await api.PostAsync<JsonElement>("/messages", new { to = user, cipher = enc.Cipher, nonce = enc.Nonce, salt = enc.Salt, senderKeyId = myKeyId, recipientKeyId = keyId });
                    return Decrypt(sent, user, known);
                }
                catch (ApiException ex) when (ex.Status == HttpStatusCode.Conflict && attempt == 0) { /* anahtar değişmiş: yeniden şifrele */ }
            }
        }

        public async Task MarkReadAsync(string user)
        {
            try { await api.PostAsync<JsonElement>($"/messages/with/{Uri.EscapeDataString(user)}/read", new { }); } catch { }
        }

        private ChatMessage Decrypt(JsonElement m, string peerName, KnownKey? known)
        {
            string from = m.GetProperty("from").GetString()!, to = m.GetProperty("to").GetString()!;
            string senderKeyId = m.GetProperty("senderKeyId").GetString()!, recipientKeyId = m.GetProperty("recipientKeyId").GetString()!;
            bool mine = string.Equals(from, _me, StringComparison.OrdinalIgnoreCase);
            string myKeyIdInMessage = mine ? senderKeyId : recipientKeyId, peerKeyIdInMessage = mine ? recipientKeyId : senderKeyId;

            string? text = null, problem = null;
            if (myKeyIdInMessage != _keyId) problem = "Bu mesaj eski bir anahtarınla şifrelenmiş; okunamıyor.";
            else
            {
                var candidates = known == null ? new List<string>() : new[] { known.PublicKey }.Concat(known.Previous).ToList();
                string? peerPub = candidates.FirstOrDefault(p => MessageCrypto.KeyId(p) == peerKeyIdInMessage);
                if (peerPub == null) problem = "Bu mesaj, kişinin bu telefonun tanımadığı bir anahtarıyla şifrelenmiş.";
                else
                {
                    ECDiffieHellman key;
                    lock (_gate) key = _key!;
                    string? plain = MessageCrypto.Decrypt(key, peerPub, from, to, senderKeyId, recipientKeyId,
                        new EncryptedMessage(m.GetProperty("cipher").GetString()!, m.GetProperty("nonce").GetString()!, m.GetProperty("salt").GetString()!));
                    if (plain == null) problem = "Bu mesaj çözülemedi (bozulmuş ya da değiştirilmiş olabilir).";
                    else text = ReadPayload(plain);
                }
            }
            m.GetProperty("createdAt").TryDate(out var at);
            bool read = m.TryGetProperty("readAt", out var r) && r.ValueKind == JsonValueKind.String;
            return new ChatMessage(m.GetProperty("id").GetString()!, from, to, mine, text, problem, at.ToLocalTime(), read, peerName);
        }

        /// <summary>Düz metin ya da launcher'ın zarfı ({"v":1,"text":…,"card":…}); kart (paket/sunucu daveti) metne çevrilir.</summary>
        private static string ReadPayload(string plain)
        {
            if (!plain.StartsWith("{\"v\":", StringComparison.Ordinal)) return plain;
            try
            {
                var p = JsonSerializer.Deserialize<JsonElement>(plain, Json);
                string text = p.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
                if (text.Length > MaxLength) text = text[..MaxLength];
                if (p.TryGetProperty("card", out var c) && c.ValueKind == JsonValueKind.Object)
                {
                    string name = c.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "";
                    if (name.Length > 64) name = name[..64];
                    string card = c.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String && ty.GetString() == "server"
                        ? $"🎮 Sunucuya davet: {name}" : $"📦 Paket paylaştı: {name}";
                    text = text.Length == 0 ? card : text + "\n" + card;
                }
                return text;
            }
            catch (JsonException) { return plain; }
        }

        // ── Yeni mesajları dinleme ────────────────────────────────────────
        private void StartPolling()
        {
            var cts = new CancellationTokenSource();
            _poll = cts;
            _ = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        string query = _since is { } since ? $"?since={Uri.EscapeDataString(since.ToString("o"))}" : "";
                        var update = await api.GetAsync<JsonElement>("/messages/updates" + query);
                        if (cts.IsCancellationRequested) break;
                        if (update.TryGetProperty("now", out var now) && now.TryDate(out var at)) _since = at.ToUniversalTime();
                        var views = new List<ChatMessage>();
                        foreach (var group in update.GetProperty("messages").EnumerateArray().GroupBy(m => m.GetProperty("from").GetString()!))
                        {
                            KnownKey? known = null;
                            try { known = (await PeerKeyAsync(group.Key)).Known; } catch { }
                            views.AddRange(group.Select(m => Decrypt(m, group.Key, known)));
                        }
                        int unread = update.GetProperty("unread").GetInt32();
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (cts.IsCancellationRequested) return;
                            Unread = unread;
                            if (views.Count > 0) Received?.Invoke(views);
                            StateChanged?.Invoke();
                        });
                    }
                    catch { /* bağlantı yoksa bir sonraki turda */ }
                    try
                    {
                        // Push gelince (ya da süre dolunca) bir sonraki tur
                        var wake = _wake.Task;
                        await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(PushHub.InForeground ? 4 : 30), cts.Token), wake);
                        if (wake.IsCompleted) _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    catch (OperationCanceledException) { }
                }
            });
        }
    }
}
