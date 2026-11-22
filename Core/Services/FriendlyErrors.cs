using System.Net;
using System.Security.Authentication;

namespace VuluLauncher
{
    /// <summary>
    /// Arayüze giden hatalar için sade Türkçe açıklama: .NET'in ham İngilizce iletileri ("The operation was canceled",
    /// "The process cannot access the file…") yerine ne olduğu ve ne yapılabileceği. Ayrıntılı hali günlüğe yazılır.
    /// Launcher'ın kendi iletileri (InvalidOperationException) zaten Türkçedir, olduğu gibi kalır.
    /// </summary>
    internal static class FriendlyErrors
    {
        public static string Message(Exception ex)
        {
            if (ex is InvalidOperationException && ex.GetType() == typeof(InvalidOperationException)) return ex.Message;
            if (ex is AggregateException { InnerExceptions.Count: 1 } agg) return Message(agg.InnerExceptions[0]);

            switch (ex)
            {
                case TaskCanceledException when ex.InnerException is TimeoutException:
                case TimeoutException:
                    return "Sunucu zamanında cevap vermedi. İnternet bağlantını kontrol edip tekrar dene.";
                case OperationCanceledException:
                    return "İşlem yarıda kesildi. Tekrar dene; sürerse internet bağlantını kontrol et.";
                case HttpRequestException http:
                    return Http(http);
                case UnauthorizedAccessException:
                    return "Bir dosyaya erişim izni yok. Klasör salt okunur olabilir ya da antivirüs engelliyor olabilir.";
                case FileNotFoundException f:
                    return L.F("Dosya bulunamadı{0}.", Name(f.FileName));
                case DirectoryNotFoundException:
                    return "Klasör bulunamadı; taşınmış ya da silinmiş olabilir.";
                case PathTooLongException:
                    return "Dosya yolu Windows için çok uzun. Launcher'ı daha kısa yollu bir klasöre taşımayı dene.";
                case InvalidDataException:
                    return "İndirilen dosya bozuk. Tekrar dene.";
                case System.Text.Json.JsonException:
                    return "Sunucudan beklenmeyen bir cevap geldi. Biraz sonra tekrar dene.";
                case IOException io:
                    return Io(io);
            }
            return ex.Message.Length == 0 ? "Bilinmeyen bir hata oluştu." : ex.Message;
        }

        private static string Http(HttpRequestException ex)
        {
            if (ex.InnerException is AuthenticationException || ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase))
                return "Güvenli bağlantı kurulamadı. Bilgisayarın saat ve tarihini kontrol et; antivirüs ya da VPN engelliyor olabilir.";
            return ex.StatusCode switch
            {
                HttpStatusCode.NotFound => "Dosya sunucuda bulunamadı (404). Kaldırılmış ya da taşınmış olabilir.",
                HttpStatusCode.Forbidden => "Sunucu bu dosyaya erişime izin vermedi (403).",
                HttpStatusCode.Unauthorized => "Oturumun kapalı ya da süresi dolmuş. Yeniden giriş yap.",
                HttpStatusCode.TooManyRequests => "Kısa sürede çok fazla istek gitti. Biraz bekleyip tekrar dene.",
                { } code when (int)code >= 500 => L.F("Sunucu şu an hata veriyor ({0}). Biraz sonra tekrar dene.", (int)code),
                { } code => L.F("Sunucu isteği kabul etmedi ({0}).", (int)code),
                null => "Sunucuya ulaşılamadı. İnternet bağlantını kontrol edip tekrar dene.",
            };
        }

        private static string Io(IOException ex)
        {
            int code = ex.HResult & 0xFFFF;
            return code switch
            {
                32 or 33 => "Bir dosya başka bir program tarafından kullanılıyor (oyun ya da başka bir launcher açık olabilir). Onu kapatıp tekrar dene.",
                112 or 39 => "Diskte yer kalmadı. Biraz yer açıp tekrar dene.",
                _ when ex.Message.Contains("transport connection", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase)
                    => "Bağlantı yarıda koptu. Tekrar dene; sürerse internet bağlantını kontrol et.",
                _ => ex.Message,
            };
        }

        private static string Name(string? path) => string.IsNullOrEmpty(path) ? "" : $": {Path.GetFileName(path)}";
    }
}
