using Avalonia.Media.Imaging;
using VuluLauncher;

namespace VuluMobile.Services
{
    /// <summary>
    /// Sunucudan gelen resimler (profil fotoğrafı, ekran görüntüsü) için bellek önbelleği. Aynı resim için tek
    /// indirme yapılır; olmayan resim (404) de hatırlanır. Uygulama kapanınca önbellek gider.
    /// </summary>
    internal static class Images
    {
        private static readonly Dictionary<string, Task<Bitmap?>> Cache = new();
        private const int MaxEntries = 300;
        public static ApiClient? Api { get; set; }

        public static Task<Bitmap?> AvatarAsync(string username) => GetAsync($"/users/{Uri.EscapeDataString(username)}/avatar", 96);

        /// <param name="decodeWidth">Büyük resimler bu genişliğe küçültülerek açılır (bellek); 0: olduğu gibi.</param>
        public static Task<Bitmap?> GetAsync(string path, int decodeWidth = 0)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(path, out var cached) && !cached.IsFaulted) return cached;
                if (Cache.Count >= MaxEntries) Cache.Clear();
                return Cache[path] = LoadAsync(path, decodeWidth);
            }
        }

        /// <summary>Resim değişti (ör. profil fotoğrafı yüklendi): bir dahaki istekte yeniden indirilir.</summary>
        public static void Forget(string path) { lock (Cache) Cache.Remove(path); }

        public static void ForgetAvatar(string username)
        {
            Forget($"/users/{Uri.EscapeDataString(username)}/avatar");
            AvatarChanged?.Invoke(username);
        }

        /// <summary>Bir kullanıcının fotoğrafı değişti: ekrandaki <c>Avatar</c>'lar yeniden yükler.</summary>
        public static event Action<string>? AvatarChanged;

        private static async Task<Bitmap?> LoadAsync(string path, int decodeWidth)
        {
            if (Api == null) return null;
            byte[]? data = await Api.GetBytesAsync(path);
            if (data == null) return null;
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream(data);
                return decodeWidth > 0 ? Bitmap.DecodeToWidth(stream, decodeWidth) : new Bitmap(stream);
            });
        }
    }
}
