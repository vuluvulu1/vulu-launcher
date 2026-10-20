namespace VuluLauncher
{
    /// <summary>
    /// Yerel dosyaların (arka planlar, logolar, ekran görüntüleri) arayüze verilen adresi. Windows'ta file:// adresidir;
    /// Android'de WebView yerel dosyaları doğrudan açamadığı için platform kendi adresini verir.
    /// </summary>
    internal static class LocalFiles
    {
        public static Func<string, string> Url = path => new Uri(path).AbsoluteUri;
    }
}
