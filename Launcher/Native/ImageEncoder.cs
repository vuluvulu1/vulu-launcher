using SkiaSharp;

namespace VuluLauncher.Native
{
    /// <summary>
    /// Paylaşılan ekran görüntüleri: uzun kenarı sınıra indirilip WebP'ye çevrilir; boyut sınırına sığana dek kalite,
    /// sonra ölçek düşürülür (vulu sunucusunun alanı sınırlı). Web arayüzündeki tarayıcı kodlayıcısının yerine.
    /// </summary>
    internal static class ImageEncoder
    {
        internal sealed record Encoded(string DataUrl, int Width, int Height, int Bytes);

        public static Encoded Encode(byte[] source, int maxSide, int maxBytes)
        {
            using var original = SKBitmap.Decode(source) ?? throw new InvalidOperationException("Görüntü açılamadı.");
            double scale = Math.Min(1, (double)maxSide / Math.Max(original.Width, original.Height));
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int w = Math.Max(1, (int)Math.Round(original.Width * scale)), h = Math.Max(1, (int)Math.Round(original.Height * scale));
                using var resized = w == original.Width && h == original.Height
                    ? original.Copy()
                    : original.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
                using var image = SKImage.FromBitmap(resized);
                foreach (int quality in new[] { 85, 75, 65 })
                {
                    using var data = image.Encode(SKEncodedImageFormat.Webp, quality);
                    if (data != null && data.Size <= maxBytes)
                        return new Encoded("data:image/webp;base64," + Convert.ToBase64String(data.ToArray()), w, h, (int)data.Size);
                }
                scale *= 0.8;
            }
            throw new InvalidOperationException("Görüntü yeterince küçültülemedi.");
        }

        /// <summary>"data:image/png;base64,..." → bayt.</summary>
        public static byte[] FromDataUrl(string dataUrl) => Convert.FromBase64String(dataUrl[(dataUrl.IndexOf(',') + 1)..]);
    }
}
