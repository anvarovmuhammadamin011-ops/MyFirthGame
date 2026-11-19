using System.Collections.Generic;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Shared.War;
using AndijonSaltanatlar.UI;
using Microsoft.Xna.Framework;
using Xunit;

namespace AndijonSaltanatlar.Tests
{
    /// <summary>TABS uslubidagi real vaqt jang: grafikasiz simulyatsiya to'g'ri yakunlanadi.</summary>
    public class TabsBattleTests
    {
        private static List<UnitGroupDto> G(UnitLine line, int level, int count) =>
            new List<UnitGroupDto> { new UnitGroupDto { Line = line, Level = level, Count = count } };

        private static TabsBattle Make(List<UnitGroupDto> mine, List<UnitGroupDto> foe)
        {
            var b = new TabsBattle(null, new GameStateManager());
            b.Setup(mine, foe, Color.Blue, Color.Red, "Biz", "Raqib", 0f);
            return b;
        }

        [Fact]
        public void Deploy_PlacesBothArmies_AndWaitsForStart()
        {
            var b = Make(G(UnitLine.Piyoda, 2, 6), G(UnitLine.Piyoda, 1, 4));
            Assert.Equal(TabsBattle.Phase.Deploy, b.Stage);
            Assert.Equal(6, b.AliveCount(true));
            Assert.Equal(4, b.AliveCount(false));
            b.Simulate(30f);                       // joylashtirishda vaqt o'tmaydi
            Assert.Equal(TabsBattle.Phase.Deploy, b.Stage);
        }

        [Fact]
        public void StrongerArmy_Wins()
        {
            var b = Make(G(UnitLine.Piyoda, 2, 20), G(UnitLine.Piyoda, 1, 3));
            b.BeginFight(false);
            b.Simulate(160f);
            Assert.True(b.Finished);
            Assert.Equal(1, b.Winner);
            Assert.True(b.MineSurvival > b.FoeSurvival);
            Assert.Equal(0, b.AliveCount(false));
        }

        [Fact]
        public void WeakerArmy_Loses()
        {
            var b = Make(G(UnitLine.Piyoda, 1, 3), G(UnitLine.Otliq, 2, 12));
            b.BeginFight(false);
            b.Simulate(160f);
            Assert.True(b.Finished);
            Assert.Equal(2, b.Winner);
        }

        [Fact]
        public void Archers_KillFromRange()
        {
            var b = Make(G(UnitLine.Masofaviy, 2, 12), G(UnitLine.Piyoda, 1, 2));
            b.BeginFight(false);
            b.Simulate(160f);
            Assert.True(b.Finished);
            Assert.Equal(1, b.Winner);
        }
    }
}
