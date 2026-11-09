using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using VuluMobile.Services;

namespace VuluMobile.Android
{
    /// <summary>Kayıtlı giriş: Android Keystore'daki AES anahtarıyla şifrelenir (launcher'ın Android sürümüyle aynı yol).</summary>
    internal sealed class AndroidProtector : ISecretProtector
    {
        private const string KeyAlias = "vulu-sosyal-login";

        public byte[] Protect(byte[] data)
        {
            var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.EncryptMode, Key());
            byte[] iv = cipher.GetIV()!;
            byte[] encrypted = cipher.DoFinal(data)!;
            return [(byte)iv.Length, .. iv, .. encrypted];
        }

        public byte[] Unprotect(byte[] data)
        {
            int ivLength = data[0];
            var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.DecryptMode, Key(), new GCMParameterSpec(128, data[1..(1 + ivLength)]));
            return cipher.DoFinal(data[(1 + ivLength)..])!;
        }

        private static IKey Key()
        {
            var store = KeyStore.GetInstance("AndroidKeyStore")!;
            store.Load(null);
            if (store.GetKey(KeyAlias, null) is { } existing) return existing;

            var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
            generator.Init(new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes(KeyProperties.BlockModeGcm)
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                .Build());
            return generator.GenerateKey()!;
        }
    }
}
