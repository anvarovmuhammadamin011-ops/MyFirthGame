using System;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Units;

namespace AndijonSaltanatlar.World
{
    /// <summary>
    /// Xaritadagi hudud shahri: qamal (SiegeMode) va egallangan shaharga kirish (CityVisitMode) uchun bir xil urug' —
    /// bir xil ko'chalar, devorlar, binolar va aholi. Shu sababli qamal qilgan shahringizga keyin kirsangiz, o'sha shaharni ko'rasiz.
    /// </summary>
    public static class RegionCity
    {
        public static int Seed(int regionId) => 7919 * (Math.Abs(regionId) + 1);

        public static void Build(AssetManager assets, int regionId, out MapManager map, out BuildingManager buildings, out UnitManager units)
        {
            map = new MapManager(assets, GameConfig.MapSize, GameConfig.MapSize);
            buildings = new BuildingManager(map, assets);
            units = new UnitManager(assets);
            CityGenerator.Generate(map, buildings, units, Seed(regionId));
            CitizenSpawner.Spawn(map, units);
            PatrolController.RouteAll(map, units);
        }
    }
}
