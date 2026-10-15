using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VuluLauncher
{
    /// <summary>
    /// "Beni hatırla" bilgisini kullanıcının AppData klasöründe saklar.
    /// Şifre Windows DPAPI ile şifrelenir; yalnızca aynı Windows kullanıcısı çözebilir.
    /// </summary>
    public static class CredentialManager
    {
        private sealed record Saved(string Username, string Password);

        public static void Save(string username, string password)
        {
            byte[] encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser);

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

                byte[] decrypted = ProtectedData.Unprotect(
                    Convert.FromBase64String(saved.Password), null, DataProtectionScope.CurrentUser);
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
