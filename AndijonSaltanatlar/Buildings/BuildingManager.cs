using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.World;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.Buildings
{
    /// <summary>Tur bo'yicha bino yaratadi. Yangi bino turi qo'shilsa — faqat shu yerga bitta qator.</summary>
    public static class BuildingFactory
    {
        public static Building Create(BuildingType type, Point position, int variant = 0)
        {
            Building b;
            switch (type)
            {
                case BuildingType.House: b = new House(position, variant); break;
                case BuildingType.Market: b = new Market(position); break;
                case BuildingType.Tower: b = new Tower(position); break;
                case BuildingType.Wall: b = new Wall(position); break;
                case BuildingType.Gate: b = new Gate(position, variant != 0); break;
                case BuildingType.MainHall: b = new MainHall(position); break;
                case BuildingType.Garden: b = new Garden(position); break;
                case BuildingType.Farm: b = new Farm(position); break;
                case BuildingType.GladiatorArena: b = new GladiatorArenaBuilding(position); break;
                case BuildingType.Barracks: b = new Barracks(position); break;
                case BuildingType.Madrasa: b = new Madrasa(position); break;
                case BuildingType.Mosque: b = new Mosque(position); break;
                case BuildingType.Caravanserai: b = new Caravanserai(position); break;
                case BuildingType.Workshop: b = new Workshop(position); break;
                case BuildingType.Mine: b = new Mine(position); break;
                case BuildingType.Fishery: b = new Fishery(position); break;
                case BuildingType.Clinic: b = new Clinic(position); break;
                case BuildingType.Armory: b = new Armory(position); break;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
            b.Variant = variant;
            return b;
        }
    }

    /// <summary>
    /// Barcha binolar ro'yxati, joylashtirish qoidalari va xaritadagi egallash (occupancy) bilan sinxronlash.
    /// </summary>
    public sealed class BuildingManager
    {
        private readonly MapManager _map;
        private readonly AssetManager _assets;
        private readonly int[] _counts = new int[Enum.GetValues(typeof(BuildingType)).Length];
        private int _nextId = 1;

        public List<Building> Buildings { get; } = new List<Building>();
        public int TotalPopulation { get; private set; }

        public BuildingManager(MapManager map, AssetManager assets)
        {
            _map = map;
            _assets = assets;
        }

        public int Count(BuildingType type) => _counts[(int)type];

        /// <summary>
        /// Shu turdagi binolarning daraja-vaznli yig'indisi: 1-darajada = oddiy sanoq, yuqori daraja
        /// ko'proq hissa qo'shadi (kattalashtirilgan bozor/ferma/karvonsaroy ko'proq ishlab chiqaradi).
        /// </summary>
        public float UpgradePower(BuildingType type)
        {
            float sum = 0f;
            for (int i = 0; i < Buildings.Count; i++)
            {
                Building b = Buildings[i];
                if (b.Type == type) sum += BuildingUpgradeEffects.EconomyFactor(b);
            }
            return sum;
        }

        /// <summary>
        /// Shu joyga size x size bino qo'yish mumkinmi.
        /// strictTerrain=false — generator uchun: faqat chegara va bandlik tekshiriladi.
        /// </summary>
        public bool CanPlace(Point pos, int size, bool strictTerrain = true)
        {
            for (int y = pos.Y; y < pos.Y + size; y++)
                for (int x = pos.X; x < pos.X + size; x++)
                {
                    if (!_map.InBounds(x, y)) return false;
                    object occ = _map.GetOccupant(x, y);
                    if (occ is Building) return false;
                    if (occ is Decoration d && !d.Removable) return false;
                    if (strictTerrain && !_map.IsBuildableTerrain(x, y)) return false;
                }
            return true;
        }

        /// <summary>Binoni xaritaga qo'shadi. Muvaffaqiyatsiz bo'lsa false qaytaradi.</summary>
        public bool Add(Building b, bool strictTerrain = true)
        {
            int size = b.Size.X;
            if (!CanPlace(b.Position, size, strictTerrain)) return false;

            // Yo'lda turgan daraxt/butalar olib tashlanadi.
            for (int y = b.Position.Y; y < b.Position.Y + size; y++)
                for (int x = b.Position.X; x < b.Position.X + size; x++)
                    if (_map.GetOccupant(x, y) is Decoration d) _map.RemoveDecoration(d);

            b.Id = _nextId++;
            b.SpriteTexture = _assets.Get(b.SpriteKey);
            b.DrawPosition = WorldObjectExtensions.AnchorBottomCenter(b.SpriteTexture, b.Position, size);
            Buildings.Add(b);
            _map.SetOccupant(b.Position, size, b);
            _counts[(int)b.Type]++;
            TotalPopulation += b.PopulationCapacity;
            return true;
        }

        /// <summary>Joy bo'shmi (binoning o'zi egallagan kataklar bo'sh hisoblanadi — ko'chirish uchun).</summary>
        public bool CanPlaceIgnoring(Point pos, int size, Building self, bool strictTerrain = true)
        {
            for (int y = pos.Y; y < pos.Y + size; y++)
                for (int x = pos.X; x < pos.X + size; x++)
                {
                    if (!_map.InBounds(x, y)) return false;
                    object occ = _map.GetOccupant(x, y);
                    if (occ is Building && !ReferenceEquals(occ, self)) return false;
                    if (occ is Decoration d && !d.Removable) return false;
                    if (strictTerrain && !_map.IsBuildableTerrain(x, y)) return false;
                }
            return true;
        }

        /// <summary>
        /// Binoni yangi joyga ko'chiradi: Id, daraja, mustahkamlik, qurilish holati va boshqa ma'lumotlar o'zgarmaydi.
        /// Joy band bo'lsa hech narsa o'zgarmaydi va false qaytadi.
        /// </summary>
        public bool Move(Building b, Point pos, bool strictTerrain = true)
        {
            int size = b.Size.X;
            if (!Buildings.Contains(b) || !CanPlaceIgnoring(pos, size, b, strictTerrain)) return false;
            _map.SetOccupant(b.Position, size, null);
            for (int y = pos.Y; y < pos.Y + size; y++)
                for (int x = pos.X; x < pos.X + size; x++)
                    if (_map.GetOccupant(x, y) is Decoration d) _map.RemoveDecoration(d);
            b.Position = pos;
            b.DrawPosition = WorldObjectExtensions.AnchorBottomCenter(b.SpriteTexture, pos, size);
            _map.SetOccupant(pos, size, b);
            return true;
        }

        public void Remove(Building b)
        {
            if (!Buildings.Remove(b)) return;
            _map.SetOccupant(b.Position, b.Size.X, null);
            _counts[(int)b.Type]--;
            TotalPopulation -= b.PopulationCapacity;
        }

        public Building GetAt(int x, int y) => _map.GetOccupant(x, y) as Building;
    }
}
