using System.Text;
using System.Text.Json;

namespace VuluMobile.Services
{
    /// <summary>Platformun şifreleme anahtarı (Android'de Keystore; anahtar cihazdan çıkarılamaz).</summary>
    public interface ISecretProtector
    {
        byte[] Protect(byte[] data);
        byte[] Unprotect(byte[] data);
    }

    /// <summary>
    /// Oturumu hatırlama: kullanıcı adı ve şifre, platformun anahtarıyla şifrelenip uygulamanın kendi klasöründe
    /// saklanır. Token yerine şifre saklanır çünkü sunucunun token'ı 7 günde biter ve mesaj şifrelemesinin
    /// anahtarı da şifreden çözülür. Platform anahtar vermezse (ör. masaüstü test penceresi) hiçbir şey saklanmaz.
    /// </summary>
    public static class SavedLogin
    {
        public static ISecretProtector? Protector { get; set; }

        private sealed record Saved(string Username, string Password);

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vulu-sosyal", "login.json");

        internal static void Save(string username, string password)
        {
            if (Protector == null) return;
            try
            {
                string encrypted = Convert.ToBase64String(Protector.Protect(Encoding.UTF8.GetBytes(password)));
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(username, encrypted)));
            }
            catch { /* hatırlanmazsa bir sonraki açılışta yeniden giriş istenir */ }
        }

        public static (string Username, string Password)? Load()
        {
            if (Protector == null) return null;
            try
            {
                if (!File.Exists(FilePath)) return null;
                var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath));
                if (saved == null || saved.Username.Length == 0 || saved.Password.Length == 0) return null;
                string password = Encoding.UTF8.GetString(Protector.Unprotect(Convert.FromBase64String(saved.Password)));
                return (saved.Username, password);
            }
            catch
            {
                // Anahtar silinmiş (uygulama verisi yedekten dönmüş vb.): dosya çözülemez, unutulur
                Clear();
                return null;
            }
        }

        internal static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }
    }
}
