using VuluLauncher.Views;
using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>Profildeki 3B skin görüntüleyicinin modeli: kutular, doku bölgeleri ve hangi yüzlerin göründüğü.</summary>
    public class SkinViewerTests
    {
        [Fact]
        public void ModernSkinHasBothLayersAndCape()
        {
            // 6 parça temel + 6 parça ikinci katman, her biri 6 yüz; pelerin 6 yüz
            Assert.Equal(12 * 6, SkinModel.Build(slim: false, legacy: false, cape: false).Count);
            Assert.Equal(13 * 6, SkinModel.Build(slim: false, legacy: false, cape: true).Count);
            // Eski (64×32) skinde ikinci katman yalnızca başta
            Assert.Equal(7 * 6, SkinModel.Build(slim: false, legacy: true, cape: false).Count);
        }

        [Fact]
        public void UvRegionsStayInsideTexture()
        {
            foreach (bool slim in new[] { false, true })
                foreach (var f in SkinModel.Build(slim, legacy: false, cape: true))
                {
                    double height = f.Texture == SkinModel.CapeTexture ? 32 : 64;
                    Assert.True(f.Uv.X >= 0 && f.Uv.Y >= 0 && f.Uv.Right <= 64 && f.Uv.Bottom <= height, $"{f.Part} {f.Uv}");
                }
        }

        [Fact]
        public void FrontFacesFaceViewerAtStartAndCapeShowsFromBehind()
        {
            var faces = SkinModel.Build(slim: false, legacy: false, cape: true);
            bool Visible(SkinModel.Face f, double yaw) => SkinModel.View(f.Normal, yaw, 0).Z > 0.001;

            // Önden: yüzün ön yüzü (8,8) görünür, pelerinin dış yüzü (1,1) görünmez
            var headFront = faces.First(f => f.Part == SkinModel.Head && f.Uv.X == 8 && f.Uv.Y == 8);
            var capeOuter = faces.First(f => f.Part == SkinModel.Cape && f.Uv.X == 1 && f.Uv.Y == 1);
            Assert.True(Visible(headFront, 0));
            Assert.False(Visible(capeOuter, 0));
            // Arkadan (yarım tur) tersi
            Assert.False(Visible(headFront, Math.PI));
            Assert.True(Visible(capeOuter, Math.PI));
        }

        [Fact]
        public void SlimArmsAreThreePixelsWide()
        {
            var arm = SkinModel.Build(slim: true, legacy: false, cape: false).First(f => f.Part == SkinModel.RightArm && f.Normal.Z == 1);
            Assert.Equal(3, arm.Uv.Width);
            Assert.Equal(3, arm.TopRight.X - arm.TopLeft.X, 6);
        }
    }
}
