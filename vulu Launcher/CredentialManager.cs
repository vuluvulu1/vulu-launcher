using System.Security.Cryptography;
using System.Text;

namespace AGLR_Launcher
{
    public static class CredentialManager
    {
        public static void Save(string username, string password)
        {
            byte[] encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(password),
                null,
                DataProtectionScope.CurrentUser);

            Properties.Settings.Default.SavedUsername = username;
            Properties.Settings.Default.SavedPassword = Convert.ToBase64String(encrypted);
            Properties.Settings.Default.RememberMe = true;
            Properties.Settings.Default.Save();
        }

        public static (string username, string password) Load()
        {
            if (!Properties.Settings.Default.RememberMe)
                return ("", "");

            string username = Properties.Settings.Default.SavedUsername;
            string encrypted = Properties.Settings.Default.SavedPassword;

            if (string.IsNullOrEmpty(encrypted))
                return (username, "");

            try
            {
                byte[] decrypted = ProtectedData.Unprotect(
                    Convert.FromBase64String(encrypted),
                    null,
                    DataProtectionScope.CurrentUser);
                return (username, Encoding.UTF8.GetString(decrypted));
            }
            catch
            {
                return (username, "");
            }
        }

        public static void Clear()
        {
            Properties.Settings.Default.SavedUsername = "";
            Properties.Settings.Default.SavedPassword = "";
            Properties.Settings.Default.RememberMe = false;
            Properties.Settings.Default.Save();
        }
    }
}