using SkiaSharp;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>Temalar: ayar doğrulaması ve geçiş gölgelendiricileri (hata olursa geçiş sessizce atlanırdı).</summary>
    public class ThemeTests
    {
        /// <summary>Launcher'ın bildiği her tema ayar doğrulamasından sağ çıkmalı (yoksa yeniden açılışta başka temaya döner).</summary>
        [Theory]
        [InlineData("mesale")]
        [InlineData("kutuphane")]
        public void SettingsKeepEveryTheme(string theme)
        {
            // Varsayılan tema geçişi yapılmış ayarlar: seçilen tema (Meşale dahil) korunur
            var settings = new AppSettings { Theme = theme, ThemeDefaultsVersion = 1 };
            NormalizeSettings(settings);
            Assert.Equal(theme, settings.Theme);
            Assert.Equal(theme, Themes.Normalize(theme));
        }

        /// <summary>Kaldırılan temalar (Ufkun öbür ucu, Derin Karanlık, Grafit) ve bilinmeyenler Kütüphane'ye döner.</summary>
        [Theory]
        [InlineData("ufuk")]
        [InlineData("derin")]
        [InlineData("grafit")]
        [InlineData("yok-boyle-tema")]
        public void RemovedThemesFallBackToKutuphane(string theme)
        {
            var settings = new AppSettings { Theme = theme, ThemeDefaultsVersion = 1 };
            NormalizeSettings(settings);
            Assert.Equal("kutuphane", settings.Theme);
            Assert.Equal("kutuphane", Themes.Normalize(theme));
        }

        [Fact]
        public void MesaleMovesToKutuphaneOnce()
        {
            // 0.28.0: Meşale'deki herkes bir kez Kütüphane'ye geçer; sonradan Meşale'yi seçen orada kalır
            var old = new AppSettings { Theme = "mesale" };
            NormalizeSettings(old);
            Assert.Equal("kutuphane", old.Theme);
            Assert.Equal(1, old.ThemeDefaultsVersion);
            Assert.Equal("kutuphane", new AppSettings().Theme);
        }

        private static void NormalizeSettings(AppSettings settings) =>
            typeof(AppSettings).GetMethod("Normalize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(settings, null);

        [Fact]
        public void PagesTransitionCompiles()
        {
            using var effect = SKRuntimeEffect.CreateShader(Views.ThemeTransition.PagesShader, out string? errors);
            Assert.True(effect != null, errors);
        }

        [Fact]
        public void BurnShaderCompiles()
        {
            using var effect = SKRuntimeEffect.CreateShader(Views.ThemeTransition.BurnShader, out string? errors);
            Assert.True(effect != null, errors);
        }

        [Fact]
        public void TransitionShaderCompiles()
        {
            using var effect = SKRuntimeEffect.CreateShader(Views.ThemeTransition.Shader, out string? errors);
            Assert.True(effect != null, errors);
        }
    }
}
