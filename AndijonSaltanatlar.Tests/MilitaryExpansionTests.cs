using System.Linq;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Military;
using AndijonSaltanatlar.Progression;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;
using Xunit;

namespace AndijonSaltanatlar.Tests
{
    /// <summary>Kengaytirilgan qo'shin: yangi birliklar, tarixiy davr cheklovi, zirh, nayza-otliq ustunligi va ko'rinishlar.</summary>
    public sealed class MilitaryExpansionTests
    {
        private static readonly string[] RenderedFamilies =
        {
            SpriteKeys.UnitSwordsman, SpriteKeys.UnitSpearman, SpriteKeys.UnitShield, SpriteKeys.UnitPatrol, SpriteKeys.UnitCavalry,
            SpriteKeys.UnitElite, "unit_citizen0", "unit_citizen1", "unit_citizen2", "unit_citizen3", "unit_citizen4", "unit_citizen5"
        };

        [Fact]
        public void RequestedUnits_AllExist_WithStatsAndLooks()
        {
            UnitType[] requested =
            {
                UnitType.Piyoda, UnitType.Kamonchi, UnitType.Otliq, UnitType.OgirZirhli, UnitType.Qoriqchi,
                UnitType.SaroyQoriqchisi, UnitType.HarbiyMuhandis, UnitType.Razvedkachi, UnitType.Mergan, UnitType.Tufangchi
            };
            foreach (UnitType t in requested)
            {
                UnitDef d = UnitCatalog.Get(t);
                Assert.Equal(t, d.Type);
                Assert.False(string.IsNullOrEmpty(d.Equipment));
                Assert.True(d.TrainingCost > 0 && d.TrainingDays > 0 && d.Health > 0 && d.Speed > 0);
                // Ko'rinish: 3D-render oilasi yoki undan qayta bo'yalgan oila
                Assert.True(RenderedFamilies.Contains(d.IconKey) || SpriteVariants.Find(d.IconKey) != null, d.IconKey);
            }
            // Og'ir zirhli — eng zirhli va eng sekin piyoda; razvedkachi — eng tez
            Assert.Equal(UnitCatalog.All.Max(x => x.Armor), UnitCatalog.Get(UnitType.OgirZirhli).Armor);
            Assert.Equal(UnitCatalog.All.Max(x => x.Speed), UnitCatalog.Get(UnitType.Razvedkachi).Speed);
        }

        [Fact]
        public void HistoricalEra_LocksLateUnits_UntilResearched()
        {
            UnitDef musket = UnitCatalog.Get(UnitType.Tufangchi);
            UnitDef marksman = UnitCatalog.Get(UnitType.Mergan);
            // Bino tekshirilmaydi (null) — faqat davr
            Assert.False(GameStateManager.UnitRequirementMet(musket, null, researched: 0));
            Assert.False(GameStateManager.UnitRequirementMet(musket, null, researched: 11));   // Temuriylar — hali o'qotar yo'q
            Assert.True(GameStateManager.UnitRequirementMet(musket, null, researched: 12));    // Bobur davri
            Assert.False(GameStateManager.UnitRequirementMet(marksman, null, researched: 6));
            Assert.True(GameStateManager.UnitRequirementMet(marksman, null, researched: 7));
            Assert.Contains("Bobur davri", UnitCatalog.LockText(musket, 0));
            Assert.True(GameStateManager.UnitRequirementMet(UnitCatalog.Get(UnitType.Piyoda), null, researched: 0));
            Assert.Equal(3, EraManager.EraIndex(12));
            Assert.Equal("Temuriylar davri", EraManager.EraName(7));
        }

        [Fact]
        public void RecruitUnits_RejectsUnitFromLaterEra()
        {
            var s = new GameStateManager();
            s.Coins = 10000;
            Assert.False(s.RecruitUnits(UnitType.Tufangchi, 1, null));
            Assert.Equal(10000, s.Coins);
            Assert.True(s.RecruitUnits(UnitType.Qoriqchi, 2, null));
        }

        [Fact]
        public void UnitLooks_CreatesRightClass_LookArmorAndReach()
        {
            Unit heavy = UnitLooks.Create(UnitType.OgirZirhli, new Vector2(5, 5));
            Assert.IsType<GuardUnit>(heavy);
            Assert.Equal("unit_heavy", heavy.SpriteKey);
            Assert.Equal(UnitCatalog.Get(UnitType.OgirZirhli).Armor, heavy.Armor);
            Assert.Equal(UnitType.OgirZirhli, UnitLooks.TypeOf(heavy));

            Unit musket = UnitLooks.Create(UnitType.Tufangchi, new Vector2(5, 5));
            Assert.True(musket.IsArcher);
            Assert.True(musket.AttackRange > UnitLooks.Create(UnitType.Kamonchi, new Vector2(5, 5)).AttackRange);

            Assert.IsType<CavalryUnit>(UnitLooks.Create(UnitType.Razvedkachi, new Vector2(5, 5)));
            Assert.Equal("kamon", UnitLooks.GroupOf(UnitType.Mergan));
            Assert.Equal("nayza", UnitLooks.GroupOf(UnitType.Qoriqchi));
            Assert.Equal("otliq", UnitLooks.GroupOf(UnitType.Razvedkachi));
        }

        [Fact]
        public void Armor_AbsorbsDamage_SpearBeatsCavalry()
        {
            Unit sword = UnitLooks.Create(UnitType.Qilichboz, Vector2.Zero);
            Unit heavy = UnitLooks.Create(UnitType.OgirZirhli, Vector2.Zero);
            Unit plain = new PatrolUnit(new[] { Vector2.Zero });
            Assert.Equal(20, UnitLooks.Damage(sword, plain, 20));
            Assert.Equal(12, UnitLooks.Damage(sword, heavy, 20));          // 40% zirh
            Unit spear = UnitLooks.Create(UnitType.Nayzador, Vector2.Zero);
            Unit horse = new CavalryUnit(new[] { Vector2.Zero });
            Assert.Equal(30, UnitLooks.Damage(spear, horse, 20));          // otliqqa qarshi nayza x1.5
            Assert.Equal(1, UnitLooks.Damage(sword, heavy, 1));            // kamida 1
        }

        [Fact]
        public void Recolor_ShiftsClothOnly_KeepsSkinLeatherAndAlpha()
        {
            Color blueCloth = new Color(40, 70, 200, 255);      // ~225° — kiyim
            Color skin = new Color(220, 170, 130, 255);         // ~26° — teri (o'zgarmaydi)
            Color grey = new Color(120, 120, 120, 255);         // to'yinmagan — metall (o'zgarmaydi)
            Color edge = new Color(40, 70, 200, 90);            // yarim shaffof chet — alfa saqlanadi
            Color[] src = { blueCloth, skin, grey, edge, Color.Transparent };
            var dst = new Color[src.Length];
            SpriteVariants.Apply(src, null, dst, 0f, Recolor.Hue(-105f));   // ko'k → yashil
            SpriteVariants.RgbToHsv(dst[0], out float h, out _, out _);
            Assert.InRange(h * 360f, 105f, 135f);
            Assert.Equal(skin, dst[1]);
            Assert.Equal(grey, dst[2]);
            Assert.Equal(90, dst[3].A);
            Assert.Equal(Color.Transparent, dst[4]);

            SpriteVariants.Apply(src, null, dst, 0f, new Recolor(RecolorMode.Gold));
            SpriteVariants.RgbToHsv(dst[0], out h, out float s, out _);
            Assert.InRange(h * 360f, 40f, 48f);
            Assert.True(s >= 0.69f);
        }

        [Fact]
        public void Recolor_DiffMask_RecoversOriginalAndAppliesNewShift()
        {
            // Bir xil poza, ikki xil bo'yalgan oila: faqat kiyim farq qiladi
            Color skin = new Color(220, 170, 130, 255);
            Color c0 = SpriteVariants.HsvToRgb((225f + 175f) % 360f / 360f, 0.8f, 0.78f, 255);   // asl ko'k +175
            Color c1 = SpriteVariants.HsvToRgb((225f + 140f) % 360f / 360f, 0.8f, 0.78f, 255);   // asl ko'k +140
            Color[] src = { c0, skin }, diff = { c1, skin };
            var dst = new Color[2];
            SpriteVariants.Apply(src, diff, dst, 175f, Recolor.Hue(0f));   // aslga qaytarish
            SpriteVariants.RgbToHsv(dst[0], out float h, out _, out _);
            Assert.InRange(h * 360f, 222f, 228f);
            Assert.Equal(skin, dst[1]);
        }

        [Fact]
        public void EveryProfessionKind_HasOwnLookAndLabel()
        {
            for (int k = CitizenArt.RenderedKinds; k < CitizenArt.Kinds; k++)
            {
                Assert.NotNull(SpriteVariants.Find(SpriteKeys.UnitCitizen + k));
                var c = new CitizenUnit(new[] { Vector2.Zero }, k);
                Assert.Equal(SpriteKeys.UnitCitizen + k, c.SpriteKey);
                Assert.False(string.IsNullOrEmpty(c.RoleLabel));
            }
            Assert.Equal(CitizenArt.Kinds - CitizenArt.RenderedKinds,
                Enumerable.Range(CitizenArt.RenderedKinds, CitizenArt.Kinds - CitizenArt.RenderedKinds)
                    .Select(k => new CitizenUnit(new[] { Vector2.Zero }, k).RoleLabel).Distinct().Count());
        }
    }
}
