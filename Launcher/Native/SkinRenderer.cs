using Avalonia.Media.Imaging;
using SkiaSharp;

namespace VuluLauncher.Native
{
    /// <summary>
    /// Minecraft skininin önden görünüşü: baş, gövde, kollar, bacaklar ve ikinci katman (şapka, ceket…).
    /// 64×32 (eski biçim) skinlerde sol kol ve bacak sağdakilerin aynasıdır, ikinci katman yalnızca başta vardır.
    /// Pikseller büyütülürken yumuşatılmaz.
    /// </summary>
    internal static class SkinRenderer
    {
        public static Bitmap? RenderFront(byte[] png, bool slim, int scale = 8)
        {
            using var skin = SKBitmap.Decode(png);
            if (skin == null || skin.Width != 64 || (skin.Height != 64 && skin.Height != 32)) return null;
            bool legacy = skin.Height == 32;
            int arm = slim ? 3 : 4;

            using var surface = SKSurface.Create(new SKImageInfo(16 * scale, 32 * scale, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            using var image = SKImage.FromBitmap(skin);
            var sampling = new SKSamplingOptions(SKFilterMode.Nearest);

            // (kaynak x, y, genişlik, yükseklik) → (hedef x, y); birim: skin pikseli
            void Part(int sx, int sy, int w, int h, int dx, int dy, bool mirror = false)
            {
                var src = SKRect.Create(sx, sy, w, h);
                var dst = SKRect.Create(dx * scale, dy * scale, w * scale, h * scale);
                if (!mirror) { canvas.DrawImage(image, src, dst, sampling); return; }
                canvas.Save();
                canvas.Translate((dx + w) * scale, dy * scale);
                canvas.Scale(-1, 1);
                canvas.DrawImage(image, src, SKRect.Create(0, 0, w * scale, h * scale), sampling);
                canvas.Restore();
            }

            Part(8, 8, 8, 8, 4, 0);                // baş
            Part(20, 20, 8, 12, 4, 8);             // gövde
            Part(44, 20, arm, 12, 4 - arm, 8);     // sağ kol (izleyenin solu)
            Part(4, 20, 4, 12, 4, 20);             // sağ bacak
            if (legacy)
            {
                Part(44, 20, arm, 12, 12, 8, true);
                Part(4, 20, 4, 12, 8, 20, true);
            }
            else
            {
                Part(36, 52, arm, 12, 12, 8);      // sol kol
                Part(20, 52, 4, 12, 8, 20);        // sol bacak
            }
            // İkinci katman
            Part(40, 8, 8, 8, 4, 0);
            if (!legacy)
            {
                Part(20, 36, 8, 12, 4, 8);
                Part(44, 36, arm, 12, 4 - arm, 8);
                Part(52, 52, arm, 12, 12, 8);
                Part(4, 36, 4, 12, 4, 20);
                Part(4, 52, 4, 12, 8, 20);
            }

            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            return new Bitmap(data.AsStream());
        }
    }

    /// <summary>Resmin bir bölgesini kesip istenen boyuta getirir (profil resmi ve banner kırpma).</summary>
    internal static class ImageCrop
    {
        /// <param name="x">Kesilecek bölgenin resimdeki sol üst köşesi ve boyutu (piksel).</param>
        public static byte[] Crop(byte[] source, double x, double y, double w, double h, int outWidth, int outHeight)
        {
            using var bitmap = SKBitmap.Decode(source) ?? throw new InvalidOperationException("Görüntü açılamadı.");
            using var surface = SKSurface.Create(new SKImageInfo(outWidth, outHeight));
            using var image = SKImage.FromBitmap(bitmap);
            surface.Canvas.DrawImage(image, SKRect.Create((float)x, (float)y, (float)w, (float)h), SKRect.Create(0, 0, outWidth, outHeight),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }
}
