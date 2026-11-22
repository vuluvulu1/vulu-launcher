using System.Net;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>Hızlı kazanımlar: sade hata iletileri, günlük gizleme, bağlantıdan proje, dünyaya doğrudan giriş.</summary>
    public class QuickWinTests
    {
        [Fact]
        public void FriendlyErrorsTranslateRawMessages()
        {
            Assert.Contains("yarıda", FriendlyErrors.Message(new OperationCanceledException()));
            Assert.Contains("zamanında", FriendlyErrors.Message(new TaskCanceledException("x", new TimeoutException())));
            Assert.Contains("404", FriendlyErrors.Message(new HttpRequestException("x", null, HttpStatusCode.NotFound)));
            Assert.Contains("ulaşılamadı", FriendlyErrors.Message(new HttpRequestException("No such host")));
            Assert.Contains("başka bir program", FriendlyErrors.Message(new IOException("locked", unchecked((int)0x80070020))));
            Assert.Contains("yer kalmadı", FriendlyErrors.Message(new IOException("full", unchecked((int)0x80070070))));
            // Launcher'ın kendi iletisi olduğu gibi kalır
            Assert.Equal("Önce giriş yapmalısınız.", FriendlyErrors.Message(new InvalidOperationException("Önce giriş yapmalısınız.")));
        }

        [Fact]
        public void LogShareHidesUserAndTokens()
        {
            string user = Environment.UserName;
            string log = $"Loading C:\\Users\\{user}\\AppData\\x.jar\n--accessToken abcdefghijklmnopqrstuvwxyz0123456789 --version 1.21.1";
            string redacted = LogShare.Redact(log);
            Assert.DoesNotContain($"\\{user}\\", redacted);
            Assert.Contains("<kullanıcı>", redacted);
            Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz0123456789", redacted);
            Assert.Contains("1.21.1", redacted);   // sürüm numaraları bozulmaz
        }

        [Theory]
        [InlineData("https://modrinth.com/mod/sodium", "modrinth", "mod", "sodium")]
        [InlineData("https://modrinth.com/modpack/fabulously-optimized/versions", "modrinth", "modpack", "fabulously-optimized")]
        [InlineData("https://www.curseforge.com/minecraft/mc-mods/jei", "curseforge", "mod", "jei")]
        [InlineData("https://www.curseforge.com/minecraft/modpacks/all-the-mods-10", "curseforge", "modpack", "all-the-mods-10")]
        [InlineData("https://www.curseforge.com/minecraft/texture-packs/faithful-32x", "curseforge", "resourcepack", "faithful-32x")]
        public void ParsesProjectLinks(string link, string source, string type, string slug)
        {
            Assert.True(ViewModels.ContentSearchViewModel.TryParseLink(link, out var s, out var t, out var g));
            Assert.Equal((source, type, slug), (s, t, g));
        }

        [Fact]
        public void PlainSearchIsNotALink() => Assert.False(ViewModels.ContentSearchViewModel.TryParseLink("sodium", out _, out _, out _));

        [Theory]
        [InlineData("1.21.1", true)]
        [InlineData("1.20", true)]
        [InlineData("1.19.4", false)]
        [InlineData("1.12.2", false)]
        [InlineData("24w14a", true)]
        public void QuickPlayNeedsNewMinecraft(string version, bool expected) => Assert.Equal(expected, GameLauncher.SupportsQuickPlay(version));
    }
}
