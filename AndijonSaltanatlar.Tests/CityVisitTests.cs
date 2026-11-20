using System.Collections.Generic;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace AndijonSaltanatlar.Tests
{
    /// <summary>Egallangan shaharga kirish: himoya darajasi faqat devor, darvoza, minora va saroyni ko'taradi.</summary>
    public class CityVisitTests
    {
        private static List<Building> Sample() => new List<Building>
        {
            BuildingFactory.Create(BuildingType.Wall, new Point(4, 4)),
            BuildingFactory.Create(BuildingType.Gate, new Point(5, 4)),
            BuildingFactory.Create(BuildingType.Tower, new Point(6, 4)),
            BuildingFactory.Create(BuildingType.MainHall, new Point(10, 10)),
            BuildingFactory.Create(BuildingType.House, new Point(20, 20)),
        };

        [Fact]
        public void Fortify0_ChangesNothing()
        {
            var list = Sample();
            CityVisitMode.ApplyFortify(list, 0);
            Assert.All(list, b => Assert.Equal(1, b.Level));
        }

        [Fact]
        public void EveryFortifyLevel_VisiblyUpgradesDefences_HouseUntouched()
        {
            var list = Sample();
            int wallHp = list[0].MaxHitPoints;
            for (int f = 1; f <= 5; f++)
            {
                CityVisitMode.ApplyFortify(list, f);
                Assert.Equal(System.Math.Min(5, f + 1), list[0].Level);   // devor
                Assert.Equal(System.Math.Min(5, f + 1), list[1].Level);   // darvoza
                Assert.Equal(System.Math.Min(5, f + 1), list[2].Level);   // minora
                Assert.Equal(System.Math.Max(1, f), list[3].Level);       // saroy
                Assert.Equal(1, list[4].Level);                           // uy — himoya binosi emas
            }
            Assert.True(list[0].MaxHitPoints > wallHp);                     // mustahkamlik ham oshdi
            CityVisitMode.ApplyFortify(list, 5);                            // takror chaqirish xavfsiz
            Assert.Equal(5, list[0].Level);
        }
    }
}
