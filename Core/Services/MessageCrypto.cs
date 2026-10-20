using System.Security.Cryptography;
using System.Text;

namespace VuluLauncher
{
    /// <summary>Şifreli bir mesajın sunucuya giden hâli.</summary>
    internal sealed record EncryptedMessage(string Cipher, string Nonce, string Salt);

    /// <summary>Gizli anahtarın şifreyle korunan yedeği (sunucuda durur, sunucu açamaz).</summary>
    internal sealed record KeyBackup(string Cipher, string Nonce, string Salt, int Iterations);

    /// <summary>
    /// Uçtan uca şifreli direkt mesajların kriptografisi. Her kullanıcının bir ECDH P-256 kimlik anahtarı vardır.
    ///   Mesaj anahtarı = HKDF-SHA256(ECDH(benim gizli, onun açık), tuz: mesaj başına rastgele, bilgi: "vulu-dm-v1|a|b")
    ///   İçerik       = AES-256-GCM(mesaj anahtarı, nonce: rastgele, ek veri: gönderen|alıcı|anahtar kimlikleri)
    /// İki taraf da aynı sırrı türettiği için gönderen kendi mesajını da okuyabilir. Ek veri, sunucunun bir mesajı
    /// başka bir kişiye ya da başka bir anahtarla "taşımasını" engeller (çözülmez).
    /// Gizli anahtarın yedeği: AES-256-GCM(PBKDF2-SHA256(şifre, tuz, 310 000 tur)).
    /// </summary>
    internal static class MessageCrypto
    {
        public const int BackupIterations = 310_000;
        private const string Info = "vulu-dm-v1";

        public static ECDiffieHellman NewKey() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        public static string PublicKeyOf(ECDiffieHellman key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

        /// <summary>Açık anahtarın kısa kimliği (sunucunun hesapladığıyla aynı).</summary>
        public static string KeyId(string publicKeyB64) =>
            Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKeyB64))).ToLowerInvariant()[..16];

        public static ECDiffieHellman ImportPublic(string publicKeyB64)
        {
            var key = ECDiffieHellman.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyB64), out _);
            if (key.KeySize != 256) throw new CryptographicException("Beklenmeyen anahtar türü.");
            return key;
        }

        // ── Yedek ─────────────────────────────────────────────────────────
        public static KeyBackup Backup(ECDiffieHellman key, string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16), nonce = RandomNumberGenerator.GetBytes(12);
            byte[] wrap = Rfc2898DeriveBytes.Pbkdf2(password, salt, BackupIterations, HashAlgorithmName.SHA256, 32);
            byte[] plain = key.ExportPkcs8PrivateKey();
            try { return new KeyBackup(Seal(wrap, nonce, plain, Array.Empty<byte>()), Convert.ToBase64String(nonce), Convert.ToBase64String(salt), BackupIterations); }
            finally { CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(wrap); }
        }

        /// <summary>Yedeği şifreyle açar; şifre yanlışsa (ör. admin sıfırladıysa) null.</summary>
        public static ECDiffieHellman? Restore(KeyBackup backup, string password)
        {
            byte[] wrap = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(backup.Salt), backup.Iterations, HashAlgorithmName.SHA256, 32);
            byte[]? plain = null;
            try
            {
                plain = Open(wrap, Convert.FromBase64String(backup.Nonce), backup.Cipher, Array.Empty<byte>());
                if (plain == null) return null;
                var key = ECDiffieHellman.Create();
                key.ImportPkcs8PrivateKey(plain, out _);
                return key;
            }
            catch (CryptographicException) { return null; }
            finally { CryptographicOperations.ZeroMemory(wrap); if (plain != null) CryptographicOperations.ZeroMemory(plain); }
        }

        // ── Mesaj ─────────────────────────────────────────────────────────
        public static EncryptedMessage Encrypt(ECDiffieHellman mine, string peerPublicKey, string from, string to,
            string senderKeyId, string recipientKeyId, string text)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16), nonce = RandomNumberGenerator.GetBytes(12);
            byte[] key = MessageKey(mine, peerPublicKey, salt, from, to);
            try
            {
                string cipher = Seal(key, nonce, Encoding.UTF8.GetBytes(text), Aad(from, to, senderKeyId, recipientKeyId));
                return new EncryptedMessage(cipher, Convert.ToBase64String(nonce), Convert.ToBase64String(salt));
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }

        /// <summary>Çözülemezse (yanlış anahtar, değiştirilmiş içerik) null.</summary>
        public static string? Decrypt(ECDiffieHellman mine, string peerPublicKey, string from, string to,
            string senderKeyId, string recipientKeyId, EncryptedMessage message)
        {
            try
            {
                byte[] key = MessageKey(mine, peerPublicKey, Convert.FromBase64String(message.Salt), from, to);
                try
                {
                    var plain = Open(key, Convert.FromBase64String(message.Nonce), message.Cipher, Aad(from, to, senderKeyId, recipientKeyId));
                    return plain == null ? null : Encoding.UTF8.GetString(plain);
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
        }

        /// <summary>
        /// İki kişinin karşılaştırabileceği güvenlik kodu (30 rakam, 6 grup). İki tarafta da aynı çıkar;
        /// farklıysa araya biri girmiş ya da anahtarlardan biri değişmiştir.
        /// </summary>
        public static string SafetyCode(string publicKeyA, string publicKeyB)
        {
            var keys = new[] { publicKeyA, publicKeyB }.OrderBy(k => k, StringComparer.Ordinal).Select(Convert.FromBase64String);
            byte[] hash = SHA256.HashData(keys.SelectMany(k => k).ToArray());
            var groups = Enumerable.Range(0, 6).Select(i => (BitConverter.ToUInt32(hash, i * 4) % 100000).ToString("D5"));
            return string.Join(" ", groups);
        }

        private static byte[] MessageKey(ECDiffieHellman mine, string peerPublicKey, byte[] salt, string from, string to)
        {
            using var peer = ImportPublic(peerPublicKey);
            byte[] shared = mine.DeriveRawSecretAgreement(peer.PublicKey);
            try
            {
                var pair = new[] { from, to }.OrderBy(n => n, StringComparer.Ordinal);
                return HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, salt, Encoding.UTF8.GetBytes($"{Info}|{string.Join("|", pair)}"));
            }
            finally { CryptographicOperations.ZeroMemory(shared); }
        }

        private static byte[] Aad(string from, string to, string senderKeyId, string recipientKeyId) =>
            Encoding.UTF8.GetBytes($"{Info}|{from}|{to}|{senderKeyId}|{recipientKeyId}");

        private static string Seal(byte[] key, byte[] nonce, byte[] plain, byte[] aad)
        {
            byte[] cipher = new byte[plain.Length], tag = new byte[16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, aad);
            return Convert.ToBase64String(cipher.Concat(tag).ToArray());
        }

        private static byte[]? Open(byte[] key, byte[] nonce, string cipherB64, byte[] aad)
        {
            byte[] all = Convert.FromBase64String(cipherB64);
            if (all.Length < 16 || nonce.Length != 12) return null;
            byte[] cipher = all[..^16], tag = all[^16..], plain = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(nonce, cipher, tag, plain, aad);
                return plain;
            }
            catch (CryptographicException) { return null; }
        }
    }
}
