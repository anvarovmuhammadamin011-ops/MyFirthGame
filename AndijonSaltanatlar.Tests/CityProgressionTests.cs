using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Military;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;
using Xunit;

namespace AndijonSaltanatlar.Tests
{
    /// <summary>
    /// Shahar rivojlanishi → qo'shin va aholi; yangi birliklar (Boltador, Zambarakchi), Qurolxona,
    /// silliq hayot pozalari, idle nafas olish va ekrandan tashqari throttling.
    /// </summary>
    public sealed class CityProgressionTests
    {
        // ------------------------------------------------------------------ shahar darajasi
        [Fact]
        public void CityLevel_GrowsWithPopulationAndBuildingLevels_Capped()
        {
            Assert.Equal(0, CityProgress.Level(CityProgress.Score(0, (IEnumerable<Building>)null)));

            var small = new List<Building> { new Workshop(new Point(0, 0)) };
            var big = new List<Building> { new Workshop(new Point(0, 0)), new Armory(new Point(5, 0)) };
            big[0].Level = 3; big[1].Level = 3;
            int lowScore = CityProgress.Score(20, small);
            int highScore = CityProgress.Score(20, big);
            Assert.True(highScore > lowScore);
            Assert.True(CityProgress.Level(CityProgress.Score(200, big)) > CityProgress.Level(lowScore));

            // Qurilayotgan bino hisoblanmaydi
            var building = new List<Building> { new Armory(new Point(0, 0)) { BuildProgress = 0.4f } };
            Assert.Equal(10, CityProgress.Score(10, building));

            Assert.Equal(8, CityProgress.Level(100000));
            Assert.Equal(40, CityProgress.MilitaryBonusPercent(8));
            Assert.True(CityProgress.ImmigrationFactor(5) > CityProgress.ImmigrationFactor(0));
            Assert.Equal(1f, CityProgress.ImmigrationFactor(0));
        }

        [Fact]
        public void DevelopmentBonus_RaisesArmyStrength_Live()
        {
            var army = new ArmyManager();
            army.Recruit(UnitType.Piyoda, 3);
            int baseStrength = army.TotalStrength;
            Assert.True(baseStrength > 0);

            army.DevelopmentBonusPercent = 20;
            Assert.Equal((int)(baseStrength * 1.2f), army.TotalStrength);

            army.DevelopmentBonusPercent = 0;   // shahar zaiflashsa — bonus ham yo'qoladi (jonli)
            Assert.Equal(baseStrength, army.TotalStrength);
        }

        // ------------------------------------------------------------------ yangi birliklar
        [Fact]
        public void NewUnits_ExistGatedAndPlayable()
        {
            UnitDef axe = UnitCatalog.Get(UnitType.Boltador);
            UnitDef cannon = UnitCatalog.Get(UnitType.Zambarakchi);
            Assert.Equal(UnitType.Boltador, axe.Type);
            Assert.Equal(UnitType.Zambarakchi, cannon.Type);
            Assert.False(string.IsNullOrEmpty(axe.Equipment));
            Assert.False(string.IsNullOrEmpty(cannon.Equipment));

            // Davr cheklovi: zambarak faqat Bobur davrida, bolta — ilk davrdan keyin
            Assert.False(GameStateManager.UnitRequirementMet(cannon, null, researched: 11));
            Assert.True(GameStateManager.UnitRequirementMet(cannon, null, researched: 12));
            Assert.False(GameStateManager.UnitRequirementMet(axe, null, researched: 0));
            Assert.True(GameStateManager.UnitRequirementMet(axe, null, researched: 3));

            // Katalog: har tur bitta va tartibda (saqlash int bo'yicha — oxiriga qo'shilgan)
            Assert.Equal(Enum.GetValues(typeof(UnitType)).Length, UnitCatalog.All.Count);
            Assert.Equal(UnitType.Zambarakchi, UnitCatalog.All.Last().Type);
        }

        [Fact]
        public void Zambarakchi_LongestReach_Boltador_PiercesArmor()
        {
            Unit cannon = UnitLooks.Create(UnitType.Zambarakchi, Vector2.Zero);
            Unit musket = UnitLooks.Create(UnitType.Tufangchi, Vector2.Zero);
            Assert.True(cannon.IsArcher);
            Assert.True(cannon.AttackRange > musket.AttackRange);

            Unit axe = UnitLooks.Create(UnitType.Boltador, Vector2.Zero);
            Unit sword = UnitLooks.Create(UnitType.Qilichboz, Vector2.Zero);
            Unit heavy = UnitLooks.Create(UnitType.OgirZirhli, Vector2.Zero);   // 40% zirh
            Assert.IsType<SwordsmanUnit>(axe);
            Assert.Equal(UnitType.Boltador, UnitLooks.TypeOf(axe));
            Assert.Equal(12, UnitLooks.Damage(sword, heavy, 20));   // to'liq zirh
            Assert.Equal(16, UnitLooks.Damage(axe, heavy, 20));     // yarim zirh (20%)
        }

        // ------------------------------------------------------------------ Qurolxona
        [Fact]
        public void Armory_IsBuildableMilitaryBuilding()
        {
            Building b = BuildingFactory.Create(BuildingType.Armory, new Point(3, 3));
            Assert.IsType<Armory>(b);
            Assert.Equal(BuildingCategory.Military, b.Category);
            Assert.True(b.Cost > 0);
            Assert.Contains(BuildingCatalog.Buildable, e => e.Type == BuildingType.Armory);
            Assert.Equal("Saltanat qurolxonasi", BuildingUpgradeEffects.LevelName(BuildingType.Armory, 5));
        }

        // ------------------------------------------------------------------ animatsiya
        [Fact]
        public void LifeAction_BlendsInAndOut_NoSnap()
        {
            var u = new PatrolUnit(new[] { Vector2.Zero }) { Hold = true };
            u.PlayAction(Unit.ActionDefeat, 2f);

            u.GetLifePose(out _, out float rot0, out float sy0);
            Assert.Equal(0f, rot0, 3);        // boshida — hali pozada emas (keskin emas)
            Assert.Equal(1f, sy0, 3);

            u.Update(1f);                       // o'rtasida — to'liq poza
            u.GetLifePose(out _, out float rotMid, out float syMid);
            Assert.InRange(MathF.Abs(rotMid), 0.19f, 0.21f);
            Assert.InRange(syMid, 0.87f, 0.89f);

            u.Update(0.95f);                    // oxiriga yaqin — asta chiqadi
            u.GetLifePose(out _, out float rotEnd, out _);
            Assert.True(MathF.Abs(rotEnd) < MathF.Abs(rotMid) * 0.5f);
        }

        [Fact]
        public void IdleUnit_Breathes_WorkingFarmerDoesNot()
        {
            var u = new PatrolUnit(new[] { Vector2.Zero }) { Hold = true };
            float maxDev = 0f;
            for (int i = 0; i < 20; i++)
            {
                u.Update(0.1f);
                u.GetLifePose(out _, out _, out float sy);
                maxDev = MathF.Max(maxDev, MathF.Abs(sy - 1f));
            }
            Assert.InRange(maxDev, 0.005f, 0.02f);   // bilinar-bilinmas

            var farmer = new PatrolUnit(new[] { Vector2.Zero }) { Hold = true, Working = true };
            farmer.Update(0.5f);
            farmer.GetLifePose(out Vector2 off, out float rot, out float fsy);
            Assert.Equal(Vector2.Zero, off);
            Assert.Equal(0f, rot);
            Assert.Equal(1f, fsy);
        }

        [Fact]
        public void TalkAction_ProducesGesture()
        {
            var u = new PatrolUnit(new[] { Vector2.Zero }) { Hold = true };
            u.PlayAction(Unit.ActionTalk, 2f);
            float maxRot = 0f;
            for (int i = 0; i < 10; i++)
            {
                u.Update(0.1f);
                u.GetLifePose(out _, out float r, out _);
                maxRot = MathF.Max(maxRot, MathF.Abs(r));
            }
            Assert.True(maxRot > 0.01f);
        }

        // ------------------------------------------------------------------ performance
        [Fact]
        public void OffscreenThrottling_PreservesTotalTime_VisibleEveryFrame()
        {
            var units = new UnitManager(null);
            var a = new PatrolUnit(new[] { Vector2.Zero }) { Id = 1, Hold = true, WorkStyle = 1, OffscreenFrames = 5 };
            var b = new PatrolUnit(new[] { Vector2.Zero }) { Id = 2, Hold = true, WorkStyle = 1, OffscreenFrames = 5 };
            var v = new PatrolUnit(new[] { Vector2.Zero }) { Id = 3, Hold = true, WorkStyle = 1, OffscreenFrames = 0 };
            units.Units.Add(a); units.Units.Add(b); units.Units.Add(v);

            units.Update(0.1f);
            units.Update(0.1f);
            // Ekrandan tashqaridagilar navbatma-navbat (har ikki kadrda bir) yangilanadi; ko'rinadigani — har kadr
            Assert.Equal(0.2f, v.WorkTime, 4);
            Assert.True(a.WorkTime < 0.2f - 1e-4f || b.WorkTime < 0.2f - 1e-4f);

            // Ekranga qaytdi — to'plangan vaqt yo'qolmaydi
            a.OffscreenFrames = 0; b.OffscreenFrames = 0;
            units.Update(0.1f);
            Assert.Equal(0.3f, a.WorkTime, 4);
            Assert.Equal(0.3f, b.WorkTime, 4);
            Assert.Equal(0.3f, v.WorkTime, 4);
        }

        [Fact]
        public void Throttling_NeverAppliesToCombatOrControlledUnits()
        {
            var units = new UnitManager(null);
            var fighter = new PatrolUnit(new[] { Vector2.Zero }) { Id = 1, Hold = true, WorkStyle = 1, OffscreenFrames = 9, AttackTimer = 5f };
            var driven = new PatrolUnit(new[] { Vector2.Zero }) { Id = 2, Hold = true, WorkStyle = 1, OffscreenFrames = 9, ExternallyControlled = true };
            units.Units.Add(fighter); units.Units.Add(driven);
            units.Update(0.1f);
            units.Update(0.1f);
            Assert.Equal(0.2f, fighter.WorkTime, 4);
            Assert.Equal(0.2f, driven.WorkTime, 4);
        }
    }
}
