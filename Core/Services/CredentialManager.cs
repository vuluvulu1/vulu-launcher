using System.Text;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// "Beni hatırla" bilgisini kullanıcının AppData klasöründe saklar.
    /// Şifre platformun kendi koruması ile şifrelenir (Windows'ta DPAPI, Android'de Keystore); yalnızca aynı
    /// kullanıcı/cihaz çözebilir.
    /// </summary>
    internal static class CredentialManager
    {
        private sealed record Saved(string Username, string Password);

        /// <summary>Platform başlarken ayarlar.</summary>
        public static Func<byte[], byte[]> Protect = _ => throw new InvalidOperationException("Şifre koruması ayarlanmadı.");
        public static Func<byte[], byte[]> Unprotect = _ => throw new InvalidOperationException("Şifre koruması ayarlanmadı.");

        public static void Save(string username, string password)
        {
            byte[] encrypted = Protect(Encoding.UTF8.GetBytes(password));

            Directory.CreateDirectory(AppPaths.AppData);
            File.WriteAllText(AppPaths.Credentials,
                JsonSerializer.Serialize(new Saved(username, Convert.ToBase64String(encrypted))));
        }

        public static (string Username, string Password) Load()
        {
            try
            {
                if (!File.Exists(AppPaths.Credentials)) return ("", "");

                var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(AppPaths.Credentials));
                if (saved == null) return ("", "");
                if (string.IsNullOrEmpty(saved.Password)) return (saved.Username, "");

                byte[] decrypted = Unprotect(Convert.FromBase64String(saved.Password));
                return (saved.Username, Encoding.UTF8.GetString(decrypted));
            }
            catch
            {
                return ("", "");
            }
        }

        public static void Clear()
        {
            try { if (File.Exists(AppPaths.Credentials)) File.Delete(AppPaths.Credentials); }
            catch { }
        }
    }
}
