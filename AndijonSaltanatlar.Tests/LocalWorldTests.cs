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

        private static WarMapRegionDto Conquer(LocalWorld lw)
        {
            WarMapRegionDto t = lw.BuildOverview().MapRegions.First(r => !r.IsMine && !r.IsCapital);
            lw.ApplySiege(new SiegeFinishRequest { RegionId = t.RegionId, Won = true });
            return t;
        }

        /// <summary>Birinchi voqeagacha o'yin vaqtini oldinga suradi.</summary>
        private static AttackEvent Run(LocalWorld lw)
        {
            for (int i = 0; i < 2000; i++) { AttackEvent e = lw.Tick(0.1f); if (e != null) return e; }
            return null;
        }

        [Fact]
        public void Fortify_UpToMax_IsSaved()
        {
            WithRoot(() =>
            {
                LocalWorld lw = LocalWorld.Load("test");
                WarMapRegionDto t = Conquer(lw);
                Assert.Equal(0, lw.FortifyLevel(t.RegionId));
                for (int i = 1; i <= LocalWorld.MaxFortify; i++) Assert.Equal(i, lw.Fortify(t.RegionId));
                Assert.Equal(-1, lw.Fortify(t.RegionId));                                         // 5 dan oshmaydi
                Assert.Equal(LocalWorld.MaxFortify, LocalWorld.Load("test").FortifyLevel(t.RegionId)); // fayldan qayta o'qildi
                Assert.True(LocalWorld.FortifyCost(0) < LocalWorld.FortifyCost(4));
                return 0;
            });
        }

        [Fact]
        public void Fortify_HomeAndAiCastles_Refused()
        {
            WithRoot(() =>
            {
                LocalWorld lw = LocalWorld.Load("test");
                var w = lw.BuildOverview();
                int home = w.MapRegions.Single(r => r.IsMine).RegionId;
                int ai = w.MapRegions.First(r => !r.IsMine).RegionId;
                Assert.Equal(-1, lw.Fortify(home));   // Asaka — odatdagi shahringiz, u yerda binolarni o'zingiz yaxshilaysiz
                Assert.Equal(-1, lw.Fortify(ai));
                Assert.Equal(0, lw.FortifyLevel(ai));
                return 0;
            });
        }

        [Fact]
        public void AiAttack_UnfortifiedCastleIsLost_FortifiedHolds()
        {
            WithRoot(() =>
            {
                LocalWorld lw = LocalWorld.Load("test");
                lw.Rng = new Random(7);
                WarMapRegionDto t = Conquer(lw);
                // Daraja 0: hujum boshlanadi, xaritada qizil yurish ko'rinadi, qasr qo'ldan ketadi
                Assert.Equal(AttackEventKind.Started, lw.StartAttack(t.Name, 5f).Kind);
                MarchDto march = Assert.Single(lw.BuildOverview().Marches);
                Assert.True(march.TargetIsMine);
                Assert.Equal(t.RegionId, march.TargetRegionId);
                Assert.Equal(AttackEventKind.Lost, Run(lw).Kind);
                Assert.False(lw.BuildOverview().MapRegions.Single(r => r.RegionId == t.RegionId).IsMine);
                // Qayta olamiz — daraja yangidan 0; 5 gacha kuchaytirilgan qasr har qanday hujumni qaytaradi
                lw.ApplySiege(new SiegeFinishRequest { RegionId = t.RegionId, Won = true });
                Assert.Equal(0, lw.FortifyLevel(t.RegionId));
                for (int i = 0; i < LocalWorld.MaxFortify; i++) lw.Fortify(t.RegionId);
                for (int n = 0; n < 5; n++)
                {
                    lw.StartAttack(t.Name, 2f);
                    Assert.Equal(AttackEventKind.Held, Run(lw).Kind);
                }
                Assert.True(lw.BuildOverview().MapRegions.Single(r => r.RegionId == t.RegionId).IsMine);
                Assert.Empty(lw.BuildOverview().Marches);
                return 0;
            });
        }

        [Fact]
        public void AiAttack_OnHome_IsRaid_HomeNeverLost()
        {
            WithRoot(() =>
            {
                LocalWorld lw = LocalWorld.Load("test");
                Assert.Equal(AttackEventKind.Started, lw.StartAttack(LocalWorld.HomeRegion, 3f).Kind);
                AttackEvent e = Run(lw);
                Assert.Equal(AttackEventKind.RaidHome, e.Kind);
                Assert.True(e.IsHome);
                Assert.Equal(1, lw.MyRegionCount);
                return 0;
            });
        }

        [Fact]
        public void AiAttack_StartsByItself_AfterClock_AndIsSaved()
        {
            WithRoot(() =>
            {
                LocalWorld lw = LocalWorld.Load("test");
                Assert.True(lw.SecondsToNextAttack >= LocalWorld.FirstAttackDelay - 1f);
                AttackEvent e = null;
                for (int i = 0; i < 600 && e == null; i++) e = lw.Tick(1f);
                Assert.NotNull(e);
                Assert.Equal(AttackEventKind.Started, e.Kind);
                Assert.InRange(e.Seconds, 90, 120);
                Assert.NotNull(LocalWorld.Load("test").IncomingMarch);   // yurish faylda saqlangan
                Assert.Null(lw.StartAttack(null));                        // bir vaqtda bitta yurish
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
