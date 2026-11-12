using SkiaSharp;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>"Ufkun öbür ucu" temasının arka plan gölgelendiricisi derleniyor mu (hata olursa sessizce düz gradyana düşerdi).</summary>
    public class SynthwaveTests
    {
        [Fact]
        public void ShaderCompiles()
        {
            using var effect = SKRuntimeEffect.CreateShader(Views.SynthwaveBackdrop.Shader, out string? errors);
            Assert.True(effect != null, errors);
        }

        [Fact]
        public void TransitionShaderCompiles()
        {
            using var effect = SKRuntimeEffect.CreateShader(Views.ThemeTransition.Shader, out string? errors);
            Assert.True(effect != null, errors);
        }

        [Fact]
        public void ShaderDraws()
        {
            using var effect = SKRuntimeEffect.CreateShader(Views.SynthwaveBackdrop.Shader, out _)!;
            var uniforms = new SKRuntimeEffectUniforms(effect) { ["size"] = new[] { 320f, 200f }, ["time"] = 3f, ["rise"] = 1f, ["bright"] = 1f };
            using var shader = effect.ToShader(uniforms);
            using var surface = SKSurface.Create(new SKImageInfo(320, 200));
            using var paint = new SKPaint { Shader = shader };
            surface.Canvas.DrawRect(0, 0, 320, 200, paint);
            using var image = surface.Snapshot();
            using var pixels = image.PeekPixels();
            var sky = pixels.GetPixelColor(160, 10);       // tepede neredeyse siyah
            var sun = pixels.GetPixelColor(160, 70);       // güneşin üst kısmı: sıcak sarı/pembe
            Assert.True(sky.Red < 40 && sky.Green < 40, sky.ToString());
            Assert.True(sun.Red > 200, sun.ToString());
        }

        /// <summary>Elle bakmak için: VULU_SYNTH_PNG=yol verilirse launcher boyutunda bir kare kaydeder.</summary>
        [Fact]
        public void SavePreview()
        {
            string? path = Environment.GetEnvironmentVariable("VULU_SYNTH_PNG");
            if (string.IsNullOrEmpty(path)) return;
            using var effect = SKRuntimeEffect.CreateShader(Views.SynthwaveBackdrop.Shader, out _)!;
            var uniforms = new SKRuntimeEffectUniforms(effect) { ["size"] = new[] { 1320f, 820f }, ["time"] = 5f, ["rise"] = 1f, ["bright"] = 1f };
            using var shader = effect.ToShader(uniforms);
            using var surface = SKSurface.Create(new SKImageInfo(1320, 820));
            using var paint = new SKPaint { Shader = shader };
            surface.Canvas.DrawRect(0, 0, 1320, 820, paint);
            using var data = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, data.ToArray());
        }
    }
}
