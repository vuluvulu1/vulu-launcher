using System.Security.Cryptography;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>
    /// Uçtan uca şifreli mesajların kriptografisi (Social/MessageCrypto). Launcher ve mobil uygulama aynı kodu kullanır;
    /// buradaki sabit örnek, ileride herhangi bir değişikliğin eski mesajları ya da öbür uygulamanın mesajlarını
    /// okunamaz hâle getirmesini yakalar.
    /// </summary>
    public class MessageCryptoTests
    {
        // Bir kez üretilmiş, değişmemesi gereken örnek: ali → veli
        private const string APriv = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgHlw+jrtX+OokrN8HufolqodMq2TonUb/0gxnWOII98ShRANCAAR7CGzgjX2bppUVpNuiqXGqABz80ml+NUvsdYbwO2D1SJ8HQ/JPWvkN792OUIJyG0KeLdI824ykfIdglU+yGEoO";
        private const string BPriv = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgmhvEv335bKGi0rmmFj+IAEMz+mnY9Go7gfGNcRSTczahRANCAATgLCLwnWFN18XhLxQbvcLRjvxsrrH2qzGHPoFWiHUbn1i+lLIFzvJ6cmI7bZPLVyWzHR6lTm96Q/hdC1vh1IWP";
        private const string APub = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEewhs4I19m6aVFaTboqlxqgAc/NJpfjVL7HWG8Dtg9UifB0PyT1r5De/djlCCchtCni3SPNuMpHyHYJVPshhKDg==";
        private const string BPub = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE4Cwi8J1hTdfF4S8UG73C0Y78bK6x9qsxhz6BVoh1G59YvpSyBc7yenJiO22Ty1clsx0epU5vekP4XQtb4dSFjw==";
        private const string KA = "fee41ae2f8fe5f1b", KB = "aa21dbfc34cb7f60";
        private static readonly EncryptedMessage Sample = new(
            "OSW3mSkd7djYIAiCR7MCndRjqXZ2VkaEZWdbemd8OjCs+G3i9CciMhc/02X0UXaGTKaf9g==", "FPYBT/aggi/72+MM", "Oh/ljkBAoTyOzPZcljhsAg==");
        private const string SampleText = "Selam veli! Akşam sunucuya gel 🔥";

        private static ECDiffieHellman Private(string pkcs8)
        {
            var key = ECDiffieHellman.Create();
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(pkcs8), out _);
            return key;
        }

        [Fact]
        public void FixedSampleStillDecryptsOnBothSides()
        {
            using var a = Private(APriv);
            using var b = Private(BPriv);
            Assert.Equal(APub, MessageCrypto.PublicKeyOf(a));
            Assert.Equal(KA, MessageCrypto.KeyId(APub));
            Assert.Equal(KB, MessageCrypto.KeyId(BPub));
            // Alıcı da gönderen de (kendi mesajını) okuyabilir
            Assert.Equal(SampleText, MessageCrypto.Decrypt(b, APub, "ali", "veli", KA, KB, Sample));
            Assert.Equal(SampleText, MessageCrypto.Decrypt(a, BPub, "ali", "veli", KA, KB, Sample));
        }

        [Fact]
        public void SafetyCodeIsStableAndSymmetric()
        {
            Assert.Equal("10358 69394 53892 42683 49096 10134", MessageCrypto.SafetyCode(APub, BPub));
            Assert.Equal(MessageCrypto.SafetyCode(APub, BPub), MessageCrypto.SafetyCode(BPub, APub));
        }

        [Fact]
        public void MovedOrTamperedMessageDoesNotDecrypt()
        {
            using var b = Private(BPriv);
            // Sunucu mesajı başka bir kişiye ya da başka bir anahtara "taşıyamaz"
            Assert.Null(MessageCrypto.Decrypt(b, APub, "ali", "ayse", KA, KB, Sample));
            Assert.Null(MessageCrypto.Decrypt(b, APub, "ali", "veli", KA, "0000000000000000", Sample));
            byte[] cipher = Convert.FromBase64String(Sample.Cipher);
            cipher[0] ^= 1;
            Assert.Null(MessageCrypto.Decrypt(b, APub, "ali", "veli", KA, KB, Sample with { Cipher = Convert.ToBase64String(cipher) }));
        }

        [Fact]
        public void RoundTripWithFreshKeys()
        {
            using var a = MessageCrypto.NewKey();
            using var b = MessageCrypto.NewKey();
            string pa = MessageCrypto.PublicKeyOf(a), pb = MessageCrypto.PublicKeyOf(b);
            string ka = MessageCrypto.KeyId(pa), kb = MessageCrypto.KeyId(pb);
            var message = MessageCrypto.Encrypt(a, pb, "ali", "veli", ka, kb, "merhaba");
            Assert.Equal("merhaba", MessageCrypto.Decrypt(b, pa, "ali", "veli", ka, kb, message));
        }

        [Fact]
        public void BackupOpensOnlyWithThePassword()
        {
            using var key = Private(APriv);
            var backup = MessageCrypto.Backup(key, "dogru-sifre");
            using var restored = MessageCrypto.Restore(backup, "dogru-sifre");
            Assert.NotNull(restored);
            Assert.Equal(APub, MessageCrypto.PublicKeyOf(restored!));
            Assert.Null(MessageCrypto.Restore(backup, "yanlis-sifre"));
        }
    }
}
