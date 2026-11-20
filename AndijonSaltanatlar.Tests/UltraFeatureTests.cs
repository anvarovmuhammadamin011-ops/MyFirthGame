using System.Linq;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Interiors;
using AndijonSaltanatlar.Progression;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;
using AndijonSaltanatlar.Economy;
using AndijonSaltanatlar.Environment;
using AndijonSaltanatlar.Military;
using AndijonSaltanatlar.Shared.War;
using AndijonSaltanatlar.UI;
using Xunit;

namespace AndijonSaltanatlar.Tests
{
    /// <summary>ULTRA bosqichlari: 10 xil jangchi, xarid tekshiruvi, jang maydoni, fermer so'rovlari.</summary>
    public class UltraFeatureTests
    {
        [Fact]
        public void UnitCatalog_HasTenDistinctTypes_OldSaveIdsUnchanged()
        {
            Assert.True(UnitCatalog.All.Count >= 10);
            Assert.Equal(UnitCatalog.All.Count, UnitCatalog.All.Select(d => d.Type).Distinct().Count());
            Assert.Equal(UnitCatalog.All.Count, UnitCatalog.All.Select(d => d.NameUz).Distinct().Count());
            // Eski saqlashlardagi butun son qiymatlari o'zgarmagan (yangilari faqat oxiriga qo'shilgan)
            Assert.Equal(0, (int)UnitType.Piyoda);
            Assert.Equal(5, (int)UnitType.Otliq);
            Assert.Equal(9, (int)UnitType.TaminotBirligi);
            Assert.Equal(10, (int)UnitType.OgirZirhli);
            string[] want = { "Piyoda", "Qilichboz", "Nayzachi", "Kamonchi", "Otliq", "Qalqonchi", "Saroy qo'riqchisi", "Elita otliq", "Harbiy xabarchi", "Ta'minot birligi" };
            foreach (string n in want) Assert.Contains(UnitCatalog.All, d => d.NameUz == n);
            Assert.All(UnitCatalog.All, d => Assert.True(d.TrainingCost > 0 && d.Upkeep > 0 && d.Health > 0));
        }

        [Fact]
        public void RecruitUnits_ChargesOnce_RejectsWhenPoor_AndRespectsQueueLimit()
        {
            var s = new GameStateManager();
            s.Military.Initialize(1, 1);
            s.Coins = 1000;
            Assert.True(s.RecruitUnits(UnitType.Piyoda, 3, null));
            Assert.Equal(1000 - 3 * 50, s.Coins);
            Assert.Equal(3, s.Military.QueueCount);

            // Pul yetmasa — hech narsa yechilmaydi, navbat o'zgarmaydi
            s.Coins = 100;
            Assert.False(s.RecruitUnits(UnitType.Otliq, 1, null));
            Assert.Equal(100, s.Coins);
            Assert.Equal(3, s.Military.QueueCount);

            // Navbat chegarasi
            s.Coins = 1_000_000;
            Assert.False(s.RecruitUnits(UnitType.Piyoda, GameStateManager.MaxRecruitQueue, null));
            Assert.Equal(3, s.Military.QueueCount);
        }

        [Fact]
        public void Arena_UsesRealArmy_FinishesAndGivesExperience()
        {
            var s = new GameStateManager();
            s.Military.Initialize(1, 1);
            int xpBefore = s.Military.Units.Sum(u => u.Experience + u.Level * 1000);
            var arena = new ArenaController(null, s);
            Assert.True(arena.Start());
            Assert.Equal(s.Military.TotalUnits, arena.Battle.AttackerGroups.Sum(g => g.Count));
            for (int i = 0; i < 10 && arena.Battle.Status != BattleStatus.Completed; i++) arena.Resolve(BattleDecision.Faol);
            Assert.Equal(BattleStatus.Completed, arena.Battle.Status);
            Assert.Equal(3, s.Military.TotalUnits); // mashq jangi — jangchilar o'lmaydi
            Assert.True(s.Military.Units.Sum(u => u.Experience + u.Level * 1000) > xpBefore);
        }

        [Fact]
        public void Farming_AsksForSeeds_NeverAutoBuys_AndSnoozes()
        {
            var f = new FarmingManager { Seeds = 0 };
            f.BeginDay();
            Assert.False(f.TryConsumeSeed());
            Assert.Equal(1, f.IdleFields);
            f.EndDay(5, 4, Season.Kuz);
            Assert.NotNull(f.Active);
            Assert.Equal("seeds", f.Active.Id);
            Assert.True(f.PopupPending);
            Assert.Equal(0, f.Seeds); // avtomatik xarid yo'q

            // Pul yetmasa — so'rov qoladi
            int coins = 5;
            Assert.False(f.Buy(ref coins, 5, out _));
            Assert.Equal(5, coins);
            Assert.NotNull(f.Active);

            // Keyinroq — 3 kun so'ralmaydi, bitta so'rovdan ortiq yo'q
            f.Later(5);
            Assert.Null(f.Active);
            f.EndDay(6, 4, Season.Kuz); Assert.True(f.Active == null || f.Active.Id != "seeds");
            if (f.Active != null) f.Later(6);
            f.EndDay(7, 4, Season.Kuz); Assert.True(f.Active == null || f.Active.Id != "seeds");
            if (f.Active != null) f.Later(7);
            f.EndDay(8, 4, Season.Kuz);
            Assert.NotNull(f.Active);
            Assert.Equal("seeds", f.Active.Id);

            coins = 10_000;
            int cost = f.Active.Cost, amount = f.Active.Amount;
            Assert.True(f.Buy(ref coins, 8, out _));
            Assert.Equal(10_000 - cost, coins);
            Assert.Equal(amount, f.Seeds);
            Assert.Null(f.Active);
        }

        [Fact]
        public void EveryBuilding_UpgradesToLevel5_CostGrows_AndArtGetsBigger()
        {
            foreach (BuildingType t in System.Enum.GetValues(typeof(BuildingType)))
            {
                Building b = BuildingFactory.Create(t, Point.Zero);
                b.BuildProgress = 1f;
                int lastCost = 0, hp = b.MaxHitPoints;
                while (BuildingUpgradeSystem.CanUpgrade(b))
                {
                    int cost = BuildingUpgradeSystem.UpgradeCost(b);
                    Assert.True(cost > lastCost, t + " narxi oshmadi");
                    lastCost = cost;
                    b.Upgrade();
                }
                Assert.Equal(5, b.Level);
                Assert.True(b.MaxHitPoints > hp);
            }
            Canvas basic = BuildingArt.Market();
            Canvas l5 = LevelArt.Compose(basic, 4, 5);
            Assert.Equal(basic.Width, l5.Width);
            Assert.True(l5.Height > basic.Height + 20, "5-daraja balandroq bo'lishi kerak");
        }

        [Fact]
        public void Palace_OnlyGuardsMayEnter()
        {
            Building hall = BuildingFactory.Create(BuildingType.MainHall, Point.Zero);
            Building house = BuildingFactory.Create(BuildingType.House, Point.Zero);
            var citizen = new CitizenUnit(new[] { Vector2.Zero, Vector2.One }, 0);
            var guard = new SwordsmanUnit(new[] { Vector2.Zero, Vector2.One });
            Assert.False(HouseInterior.MayEnter(hall, citizen));
            Assert.True(HouseInterior.MayEnter(hall, guard));
            Assert.True(HouseInterior.MayEnter(house, citizen));
        }

        [Fact]
        public void Campaign_BattleResultChangesWorld_AndArmyKeepsSurvivors()
        {
            var s = new GameStateManager();
            s.Military.Initialize(1, 1);
            var W = s.World;
            var target = W.Regions.Regions.First(r => r.OwnerKingdomId != 0 && r.OwnerKingdomId != W.Kingdoms.PlayerKingdomId);
            int historyBefore = W.Battles.History.Count;
            var arena = new ArenaController(null, s);
            Assert.True(arena.StartCampaign(target.Id));
            Assert.True(arena.IsCampaign);
            for (int i = 0; i < 10 && arena.Battle.Status != BattleStatus.Completed; i++) arena.Resolve(BattleDecision.Faol);
            Assert.Equal(BattleStatus.Completed, arena.Battle.Status);
            Assert.Equal(historyBefore + 1, W.Battles.History.Count);
            Assert.True(s.Military.TotalUnits >= 1);
            bool won = arena.Battle.WinnerKingdomId == 1;
            Assert.Equal(won, target.OwnerKingdomId == W.Kingdoms.PlayerKingdomId);
        }

        [Fact]
        public void TendedFarm_GrowsVisiblyThenHarvests()
        {
            var farm = (Farm)BuildingFactory.Create(BuildingType.Farm, Point.Zero);
            Assert.Equal(0f, farm.Growth);
            Assert.True(farm.PlantCrop());
            farm.AdvanceGrowth(0.4f);
            Assert.Equal(FarmCropStage.Growing, farm.CropStage);
            Assert.InRange(farm.Growth, 0.39f, 0.41f);
            farm.AdvanceGrowth(0.7f);
            Assert.Equal(FarmCropStage.Mature, farm.CropStage);
            Assert.Equal(1f, farm.Growth);
            Assert.Equal(Farm.CropYield, farm.HarvestCrop());
            Assert.Equal(FarmCropStage.Empty, farm.CropStage);
            Assert.Equal(0f, farm.Growth);
        }

        [Fact]
        public void Farming_YieldBonus_FromToolsAndFertilizer()
        {
            var f = new FarmingManager { ToolsLevel = 2, FertilizerCharges = 1 };
            Assert.Equal(22, f.ApplyYieldBonus(12));   // 12 * (1 + 2*0.25 + 0.3 o'g'it) = 21.6 -> 22
            Assert.Equal(0, f.FertilizerCharges);
            Assert.Equal(18, f.ApplyYieldBonus(12));   // o'g'it tugadi: 12 * 1.5
        }
    }
}
