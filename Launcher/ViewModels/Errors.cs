namespace VuluLauncher.ViewModels
{
    /// <summary>Hata ekranlarında gösterilecek sade açıklama: ham istisna metni yerine ne olduğu ve ne yapılabileceği.</summary>
    internal static class Errors
    {
        public static string Friendly(Exception ex)
        {
            string m = ex.Message;
            if (m.Contains("giriş yap", StringComparison.OrdinalIgnoreCase) || m.Contains("oturum", StringComparison.OrdinalIgnoreCase)
                || m.Contains("sign in", StringComparison.OrdinalIgnoreCase) || m.Contains("session", StringComparison.OrdinalIgnoreCase))
                return "Oturumun kapalı ya da süresi dolmuş. Yeniden giriş yapınca burası dolar.";
            if (ex is HttpRequestException or TimeoutException or TaskCanceledException
                || m.Contains("bağlan", StringComparison.OrdinalIgnoreCase) || m.Contains("ulaşılam", StringComparison.OrdinalIgnoreCase)
                || m.Contains("couldn't reach", StringComparison.OrdinalIgnoreCase) || m.Contains("connect", StringComparison.OrdinalIgnoreCase)
                || m.Contains("host", StringComparison.OrdinalIgnoreCase) || m.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || m.Contains("connection", StringComparison.OrdinalIgnoreCase))
                return "Sunucuya ulaşılamadı. İnternet bağlantını kontrol edip tekrar dene.";
            return m.Length == 0 ? "Bilinmeyen bir hata oluştu." : m.TrimEnd('.') + ".";
        }
    }
}
