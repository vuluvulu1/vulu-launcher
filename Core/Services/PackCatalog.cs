namespace VuluLauncher
{
    /// <summary>Launcher'la gelen hazır paket (şu an yalnızca Vanilla). Kullanıcı bunları değiştiremez.</summary>
    /// <param name="Id">Arayüzle konuşurken kullanılan kimlik.</param>
    /// <param name="McVersion">Minecraft sürümü.</param>
    /// <param name="Loader">"vanilla" | "fabric" | "quilt" | "forge" | "neoforge"</param>
    /// <param name="LoaderVersion">Mod yükleyici sürümü; vanilla için null.</param>
    /// <param name="FolderName">instances altındaki klasör.</param>
    /// <param name="Logo">wwwroot'a göre logo yolu; yoksa paket adı yazıyla gösterilir.</param>
    /// <param name="Background">wwwroot'a göre arka plan görseli; yoksa düz gradyan kullanılır.</param>
    /// <param name="BackgroundStyle">"cover": görsel tüm alanı kaplar; "side": küçük görseller için, sağa yaslanıp karanlığa karışır.</param>
    public sealed record Pack(
        string Id,
        string Name,
        string Description,
        string McVersion,
        string Loader,
        string? LoaderVersion,
        string FolderName,
        int RecommendedRamGb,
        string? Logo,
        string? Background,
        string? Thumbnail,
        string BackgroundStyle = "cover");

    public static class PackCatalog
    {
        public static readonly int[] RamOptionsGb = { 4, 8, 12, 16 };

        public static readonly string[] Loaders = { "vanilla", "fabric", "quilt", "forge", "neoforge" };

        public static readonly IReadOnlyList<Pack> All = new[]
        {
            new Pack("vanilla", "Vanilla", "Modsuz, sade Minecraft.",
                "1.20.1", "vanilla", null, "Vanilla", 4,
                "assets/minecraftlogo.png", "assets/launchervanillabg.jpg", "assets/launchervanillabg.jpg"),
        };

        public static Pack? Find(string id) =>
            All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Kullanıcı instance'ları sunucu paketlerinin klasörlerini kullanamaz.</summary>
        public static bool IsReservedFolder(string folder) =>
            All.Any(p => string.Equals(p.FolderName, folder, StringComparison.OrdinalIgnoreCase));
    }
}
