using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace VuluLauncher
{
    /// <summary>
    /// Masaüstü kısayolunun simgesi: paketin (ya da sunucunun) simgesi 256 px kare, yuvarlak köşeli PNG olarak.
    /// Çekirdek bunu ICO'ya çevirip kısayola verir; resim yoksa boş döner ve launcher'ın simgesi kullanılır.
    /// </summary>
    internal static class ShortcutIcon
    {
        private const int Size = 256;

        public static string ToDataUrl(Bitmap? source)
        {
            if (source == null || source.PixelSize.Width < 1 || source.PixelSize.Height < 1) return "";
            try
            {
                using var target = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
                using (var ctx = target.CreateDrawingContext())
                {
                    // Ortadan kare kırpılır (kapak resimleri dikdörtgen olabilir)
                    double w = source.Size.Width, h = source.Size.Height, side = Math.Min(w, h);
                    var src = new Rect((w - side) / 2, (h - side) / 2, side, side);
                    // Küçük piksel simgeler (sunucu simgesi 64 px) bulanıklaşmasın
                    var mode = source.PixelSize.Width <= 64 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
                    using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = mode }))
                    using (ctx.PushGeometryClip(new RectangleGeometry(new Rect(0, 0, Size, Size), 44, 44)))
                        ctx.DrawImage(source, src, new Rect(0, 0, Size, Size));
                }
                using var ms = new MemoryStream();
                target.Save(ms);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
            catch { return ""; }
        }
    }
}
