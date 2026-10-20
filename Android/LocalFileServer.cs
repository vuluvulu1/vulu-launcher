namespace VuluLauncher
{
    /// <summary>
    /// Android WebView arayüzün kendi adresinden (https://0.0.0.1) file:// resimlerini açmaz. Arka planlar, logolar ve
    /// ekran görüntüleri bunun yerine "/__local/..." adresinden verilir; yalnızca uygulamanın kendi klasörlerindeki
    /// dosyalar okunur.
    /// </summary>
    internal static class LocalFileServer
    {
        private const string Prefix = "/__local/";

        public static string UrlFor(string path) =>
            "https://0.0.0.1" + Prefix + Uri.EscapeDataString(Path.GetFullPath(path));

        public static void Attach(HybridWebView webView)
        {
            webView.WebResourceRequested += (_, e) =>
            {
                string requested = e.Uri.AbsolutePath;
                if (!requested.StartsWith(Prefix, StringComparison.Ordinal)) return;
                e.Handled = true;

                string path = Path.GetFullPath(Uri.UnescapeDataString(requested[Prefix.Length..]));
                if (!IsAllowed(path) || !File.Exists(path))
                {
                    e.SetResponse(404, "Not Found");
                    return;
                }
                e.SetResponse(200, "OK", ContentType(path), File.OpenRead(path));
            };
        }

        private static bool IsAllowed(string path)
        {
            string[] roots = { FileSystem.AppDataDirectory, FileSystem.CacheDirectory };
            return roots.Any(root => path.StartsWith(Path.GetFullPath(root).TrimEnd('/') + "/", StringComparison.Ordinal));
        }

        private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }
}
