using SkiaSharp;

namespace VuluMobile.Services
{
    /// <summary>
    /// Telefondan yüklenen resimleri sunucunun istediği boyuta getirir (Skia, Avalonia'nın zaten kullandığı çizim
    /// kütüphanesi). Sunucu ekran görüntüsünde JPEG ya da WEBP kabul eder; boyut sınırına sığana kadar kalite düşürülür.
    /// </summary>
    internal static class ImageTools
    {
        public sealed record Encoded(byte[] Data, int Width, int Height);

        /// <param name="maxSide">Uzun kenar bundan büyükse küçültülür.</param>
        /// <param name="maxBytes">Çıktı bundan büyük olmaz (kalite 85'ten 40'a kadar düşürülür, sonra boyut küçülür).</param>
        public static Encoded Jpeg(byte[] input, int maxSide, int maxBytes, bool square = false)
        {
            using var codec = SKCodec.Create(new MemoryStream(input)) ?? throw new InvalidOperationException("Resim açılamadı.");
            using var decoded = SKBitmap.Decode(codec) ?? throw new InvalidOperationException("Resim açılamadı.");
            using var source = Orient(decoded, codec.EncodedOrigin);

            SKRectI crop = new(0, 0, source.Width, source.Height);
            if (square)
            {
                int side = Math.Min(source.Width, source.Height);
                crop = SKRectI.Create((source.Width - side) / 2, (source.Height - side) / 2, side, side);
            }

            for (int side = maxSide; side >= 240; side = (int)(side * 0.8))
            {
                double scale = Math.Min(1.0, (double)side / Math.Max(crop.Width, crop.Height));
                int w = Math.Max(1, (int)(crop.Width * scale)), h = Math.Max(1, (int)(crop.Height * scale));
                using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
                surface.Canvas.Clear(SKColors.Black);
                using (var image = SKImage.FromBitmap(source))
                    surface.Canvas.DrawImage(image, SKRect.Create(crop.Left, crop.Top, crop.Width, crop.Height), SKRect.Create(0, 0, w, h),
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                using var snapshot = surface.Snapshot();
                for (int quality = 85; quality >= 40; quality -= 15)
                {
                    using var data = snapshot.Encode(SKEncodedImageFormat.Jpeg, quality);
                    if (data.Size <= maxBytes) return new Encoded(data.ToArray(), w, h);
                }
            }
            throw new InvalidOperationException("Resim yeterince küçültülemedi.");
        }

        /// <summary>Telefon fotoğraflarındaki EXIF yönü: yan çekilmiş resim dik görünsün.</summary>
        private static SKBitmap Orient(SKBitmap bitmap, SKEncodedOrigin origin)
        {
            if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return bitmap.Copy();
            bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var result = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height);
            using var canvas = new SKCanvas(result);
            switch (origin)
            {
                case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, bitmap.Width / 2f, bitmap.Height / 2f); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, result.Height); canvas.RotateDegrees(270); break;
                case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, bitmap.Width / 2f, 0); break;
                case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, bitmap.Height / 2f); break;
                case SKEncodedOrigin.LeftTop: canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); canvas.Scale(1, -1, 0, bitmap.Height / 2f); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(0, result.Height); canvas.RotateDegrees(270); canvas.Scale(1, -1, 0, bitmap.Height / 2f); break;
            }
            canvas.DrawBitmap(bitmap, 0, 0);
            return result;
        }
    }
}
