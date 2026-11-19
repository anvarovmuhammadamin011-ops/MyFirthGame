using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Interiors;
using Microsoft.Xna.Framework;
using Xunit;

namespace AndijonSaltanatlar.Tests
{
    public sealed class InteriorSystemTests
    {
        [Theory]
        [InlineData(BuildingType.House)]
        [InlineData(BuildingType.Market)]
        [InlineData(BuildingType.Garden)]
        [InlineData(BuildingType.Caravanserai)]
        [InlineData(BuildingType.Barracks)]
        [InlineData(BuildingType.GladiatorArena)]
        [InlineData(BuildingType.Madrasa)]
        [InlineData(BuildingType.Mosque)]
        [InlineData(BuildingType.Tower)]
        [InlineData(BuildingType.Gate)]
        [InlineData(BuildingType.MainHall)]
        public void EveryConstructedLocationTypeIsEnterable(BuildingType type)
        {
            Building b = BuildingFactory.Create(type, new Point(4, 4));
            Assert.True(HouseInterior.IsEnterable(b));
        }

        [Fact]
        public void WallIsInspectedNotEntered()
        {
            Assert.False(HouseInterior.IsEnterable(BuildingFactory.Create(BuildingType.Wall, new Point(4, 4))));
        }

        [Fact]
        public void FarmIsOpenFieldNotEntered()
        {
            // Ferma — ichiga kirilmaydigan ochiq dala (dehqon tashqarida ishlaydi).
            Assert.False(HouseInterior.IsEnterable(BuildingFactory.Create(BuildingType.Farm, new Point(4, 4))));
        }

        [Fact]
        public void BuildingTypesMapToDistinctInteriorTemplates()
        {
            Assert.Equal(InteriorKind.House, InteriorStore.KindOf(BuildingType.House));
            Assert.Equal(InteriorKind.Palace, InteriorStore.KindOf(BuildingType.MainHall));
            Assert.Equal(InteriorKind.Market, InteriorStore.KindOf(BuildingType.Market));
            Assert.Equal(InteriorKind.Farm, InteriorStore.KindOf(BuildingType.Farm));
            Assert.Equal(InteriorKind.Madrasa, InteriorStore.KindOf(BuildingType.Madrasa));
            Assert.Equal(InteriorKind.Mosque, InteriorStore.KindOf(BuildingType.Mosque));
            Assert.Equal(InteriorKind.Tower, InteriorStore.KindOf(BuildingType.Tower));
            Assert.Equal(InteriorKind.Gate, InteriorStore.KindOf(BuildingType.Gate));
        }

        [Fact]
        public void TwoHousesKeepIndependentInteriorStateAcrossSaveAndReload()
        {
            InteriorStore.Clear();
            InteriorState a = InteriorStore.For(101);
            InteriorState b = InteriorStore.For(202);
            a.ChestFood = 15;
            a.MarketStock = 10;
            b.ChestFood = 3;

            var snapshot = InteriorStore.Snapshot();
            InteriorStore.Clear();
            Assert.Equal(0, InteriorStore.For(101).ChestFood);

            InteriorStore.Restore(snapshot);
            Assert.Equal(15, InteriorStore.For(101).ChestFood);
            Assert.Equal(10, InteriorStore.For(101).MarketStock);
            Assert.Equal(3, InteriorStore.For(202).ChestFood);
            InteriorStore.Clear();
        }

        [Fact]
        public void BreadBakingTicksPersistedInteriorState()
        {
            InteriorStore.Clear();
            InteriorState st = InteriorStore.For(7);
            st.Bread = 0;
            HouseInterior.TickOutside(14f);
            Assert.Equal(2, InteriorStore.For(7).Bread);
            InteriorStore.Clear();
        }
    }
}
