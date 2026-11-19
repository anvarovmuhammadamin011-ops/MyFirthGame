using System;
using System.IO;
using System.Linq;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Online;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Shared.Geo;
using Xunit;

namespace AndijonSaltanatlar.Tests
{
    /// <summary>Serversiz dunyo: butun O'zbekiston, o'yinchidan boshqa hamma — AI davlatlar; qamal g'alabasi qasrni beradi va saqlanadi.</summary>
    [Collection("AppPathsRoot")]
    public class LocalWorldTests
    {
        private static T WithRoot<T>(Func<T> body)
        {
            string old = AppPaths.Root;
            string dir = Path.Combine(Path.GetTempPath(), "andijon_lw_" + Guid.NewGuid().ToString("N"));
            try { AppPaths.Root = dir; return body(); }
            finally { AppPaths.Root = old; try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void EveryOtherCastleIsAi_OnlyHomeIsMine()
        {
            WithRoot(() =>
            {
                WarOverviewDto w = LocalWorld.Load("test").BuildOverview();
                Assert.Equal(UzMap.Regions.Length, w.MapRegions.Count);
                WarMapRegionDto home = Assert.Single(w.MapRegions, r => r.IsMine);
                Assert.Equal(LocalWorld.HomeRegion, home.Name);
                Assert.All(w.MapRegions.Where(r => !r.IsMine), r =>
                {
                    Assert.True(r.OwnerIsAi);
                    Assert.True(r.CanSiege);
                    Assert.NotEqual(0, r.OwnerKingdomId);
                    Assert.False(string.IsNullOrEmpty(r.RulerName));
                    Assert.True(r.MarchSeconds >= 10);
                });
                // Har AI davlatning poytaxti bor
                foreach (var g in w.MapRegions.Where(r => !r.IsMine).GroupBy(r => r.OwnerKingdomId))
                    Assert.Single(g, r => r.IsCapital);
                return 0;
            });
        }

        [Fact]
        public void WonSiege_GivesCastle_AndIsSaved()
        {
            WithRoot(() =>
            {
                LocalWorld lw = LocalWorld.Load("test");
                WarMapRegionDto target = lw.BuildOverview().MapRegions.First(r => !r.IsMine && !r.IsCapital);
                Assert.Contains("muvaffaqiyatsiz", lw.ApplySiege(new SiegeFinishRequest { RegionId = target.RegionId, Won = false }));
                Assert.Contains("sizniki", lw.ApplySiege(new SiegeFinishRequest { RegionId = target.RegionId, Won = true }));
                Assert.True(LocalWorld.Load("test").BuildOverview().MapRegions.Single(r => r.RegionId == target.RegionId).IsMine); // fayldan qayta o'qildi
                Assert.Equal(2, LocalWorld.Load("test").MyRegionCount);
                Assert.Equal(1, LocalWorld.Load("boshqa").MyRegionCount); // boshqa akkauntning dunyosi alohida
                return 0;
            });
        }

        [Fact]
        public void CapturedCapital_MovesAiCapitalToAnotherCity()
        {
            WithRoot(() =>
            {
                LocalWorld lw = LocalWorld.Load("test");
                WarMapRegionDto cap = lw.BuildOverview().MapRegions.First(r => !r.IsMine && r.IsCapital && r.OwnerKingdomId > 100);
                lw.ApplySiege(new SiegeFinishRequest { RegionId = cap.RegionId, Won = true });
                var left = lw.BuildOverview().MapRegions.Where(r => r.OwnerKingdomId == cap.OwnerKingdomId).ToList();
                if (left.Count > 0) Assert.Single(left, r => r.IsCapital);
                return 0;
            });
        }

        [Fact]
        public void GoogleIdToken_NameAndSubAreRead()
        {
            string Part(string json) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            string jwt = Part("{\"alg\":\"RS256\"}") + "." + Part("{\"sub\":\"12345\",\"given_name\":\"Muhammadamin\",\"email\":\"x@y.uz\"}") + ".sig";
            var (name, email, sub) = CloudSession.ReadIdToken(jwt);
            Assert.Equal("Muhammadamin", name);
            Assert.Equal("x@y.uz", email);
            Assert.Equal("12345", sub);
            Assert.Equal("Hukmdor", CloudSession.ReadIdToken("buzuq").Name);
        }
    }
}
