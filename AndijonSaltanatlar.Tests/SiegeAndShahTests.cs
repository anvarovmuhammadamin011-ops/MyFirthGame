using System;
using System.IO;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Military;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;
using Xunit;

namespace AndijonSaltanatlar.Tests
{
    /// <summary>Qamal (asir askarlar qo'shinga qo'shilishi) va shoh qiyofasi (semiz/ingichka, bo'y, ism).</summary>
    [Collection("AppPathsRoot")]
    public class SiegeAndShahTests
    {
        [Fact]
        public void BodyChoicesChangeWidthAndHeightInOrder()
        {
            Vector2 thin = ShahLook.ScaleOf(0, 0), mid = ShahLook.ScaleOf(1, 1), fat = ShahLook.ScaleOf(2, 2);
            Assert.True(thin.X < mid.X && mid.X < fat.X);
            Assert.True(thin.Y < mid.Y && mid.Y < fat.Y);
            Assert.Equal(Vector2.One, mid);
            // Chegaradan tashqari qiymatlar qisiladi
            Assert.Equal(fat, ShahLook.ScaleOf(9, 9));
        }

        [Fact]
        public void ShahNameIsCleanedAndDefaulted()
        {
            Assert.Equal(ShahLook.DefaultName, ShahLook.CleanName("   "));
            Assert.Equal("Bobur", ShahLook.CleanName("  Bobur  "));
            Assert.True(ShahLook.CleanName(new string('a', 40)).Length <= ShahLook.NameMax);
        }

        [Fact]
        public void ShahLookIsSavedAndReloaded()
        {
            string old = AppPaths.Root;
            string dir = Path.Combine(Path.GetTempPath(), "andijon_shah_" + Guid.NewGuid().ToString("N"));
            try
            {
                AppPaths.Root = dir;
                ShahLook.ResetForTests();
                Assert.False(ShahLook.Configured);
                ShahLook.Set("Bobur Mirzo", 2, 0);
                ShahLook.ResetForTests();          // xotiradan o'chiramiz — fayldan qayta o'qiladi
                Assert.True(ShahLook.Configured);
                Assert.Equal("Bobur Mirzo", ShahLook.Name);
                Assert.Equal(2, ShahLook.Body);
                Assert.Equal(0, ShahLook.Height);
                Assert.Equal("Bobur Mirzo", ShahUnit.ShahName);
                Assert.Equal(ShahLook.ScaleOf(2, 0), new ShahUnit(Vector2.Zero).BodyScale);
            }
            finally
            {
                AppPaths.Root = old;
                ShahLook.ResetForTests();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void EnemyShahIsOnEnemyTeam()
        {
            var shah = new ShahUnit(new Vector2(5, 5), "Mirzo Ulug'", 400);
            Assert.Equal(UnitTeam.Enemy, shah.Team);
            Assert.Equal("Shoh Mirzo Ulug'", shah.Name);
            Assert.Equal(400, shah.MaxHitPoints);
        }

        [Fact]
        public void DefectorsJoinTheArmy()
        {
            var state = new GameStateManager();
            ArmyManager mil = state.Military;
            int before = mil.TotalUnits;
            int added = mil.Recruit(UnitType.Nayzador, 3, 2);
            Assert.Equal(3, added);
            Assert.Equal(before + 3, mil.TotalUnits);
            Assert.Equal(3, mil.Units.FindAll(u => u.UnitType == UnitType.Nayzador).Count - CountBefore(mil, before));
        }

        private static int CountBefore(ArmyManager mil, int before)
        {
            int n = 0;
            for (int i = 0; i < before; i++) if (mil.Units[i].UnitType == UnitType.Nayzador) n++;
            return n;
        }
    }
}
