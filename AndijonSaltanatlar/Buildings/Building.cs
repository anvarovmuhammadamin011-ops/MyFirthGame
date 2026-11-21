using AndijonSaltanatlar.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.Buildings
{
    public enum BuildingType
    {
        House, Market, Tower, Wall, Gate, MainHall, Garden, Barracks, Madrasa, Mosque, Caravanserai, Farm, GladiatorArena,
        // --- kengaytma (saqlashda butun son — faqat oxiriga) ---
        Workshop, Mine, Fishery, Clinic,
        // --- kengaytma 2 (faqat oxiriga) ---
        Armory
    }

    public enum BuildingCategory
    {
        Residential, // turar joy
        Economy,     // savdo
        Defense,     // mudofaa
        Government,  // boshqaruv
        Nature,      // bog'
        Military,    // harbiy
        Culture      // madaniyat / din
    }

    /// <summary>
    /// Barcha binolar uchun asos klass.
    /// Eslatma: chuqurlik saralashi to'g'ri ishlashi uchun binolar izi KVADRAT (Size.X == Size.Y) bo'lishi kerak.
    /// </summary>
    public abstract class Building : IWorldObject
    {
        public int Id { get; set; }
        public BuildingType Type { get; protected set; }
        public string Name { get; protected set; }
        public string Description { get; protected set; } = "";
        /// <summary>Izning shimoliy (eng yuqori) tile'i.</summary>
        public Point Position { get; set; }
        /// <summary>Shahar bilan yaratilgan bino ko'chirilgan bo'lsa — asl joyi (saqlashda kerak). null — ko'chirilmagan.</summary>
        public Point? GeneratedOrigin { get; set; }
        /// <summary>Iz o'lchami tile'larda.</summary>
        public Point Size { get; protected set; } = new Point(1, 1);
        public Point FrontEntranceTile => new Point(Position.X + Size.X / 2, Position.Y + Size.Y);
        public Texture2D SpriteTexture { get; set; }
        public string SpriteKey { get; protected set; }
        public BuildingCategory Category { get; protected set; }
        /// <summary>Unitlar ustidan/ichidan o'ta oladimi (masalan, darvoza).</summary>
        public bool IsWalkable { get; protected set; }
        public int Cost { get; protected set; }
        public int HitPoints { get; set; }
        public int MaxHitPoints { get; protected set; }
        /// <summary>Nechta aholi sig'diradi.</summary>
        public int PopulationCapacity { get; protected set; }
        /// <summary>O'yinchi qurganmi yoki boshlang'ich shaharga tegishlimi.</summary>
        public bool BuiltByPlayer { get; set; }
        /// <summary>Ko'rinish varianti (masalan uy turi yoki darvoza yo'nalishi). Save/Load uchun.</summary>
        public int Variant { get; set; }
        /// <summary>Bino darajasi (1..3). Daraja oshsa statistika yaxshilanadi.</summary>
        public int Level { get; set; } = 1;
        /// <summary>Qurilish progressi (0..1). 1 = tayyor. Qurilayotganda yarim shaffof ko'rinadi.</summary>
        public float BuildProgress { get; set; } = 1f;

        /// <summary>Binoni bir pog'ona ko'taradi: mustahkamlik va (bo'lsa) sig'im oshadi.</summary>
        public void Upgrade()
        {
            Level++;
            // Mustahkamlik va sig'im o'sishi bino turiga bog'liq (devor ko'p HP, uy ko'p sig'im).
            MaxHitPoints += (int)(MaxHitPoints * BuildingUpgradeEffects.HpGrowth(Type));
            HitPoints = MaxHitPoints;
            if (PopulationCapacity > 0) PopulationCapacity += BuildingUpgradeEffects.CapacityDelta(Type, Level);
        }

        // --- IWorldObject ---
        public Texture2D Texture => SpriteTexture;
        public Vector2 DrawPosition { get; set; }
        public bool Flipped { get; set; }
        /// <summary>Chizish tartibi. Ferma — yer kabi tekis: ustida yurgan dehqon va odamlar undan keyin chiziladi.</summary>
        public float Depth => Type == BuildingType.Farm ? Position.X + Position.Y : Position.X + Position.Y + Size.X;
        public int SortLayer => 0;

        protected Building(Point position)
        {
            Position = position;
        }

        protected void SetHitPoints(int hp)
        {
            MaxHitPoints = hp;
            HitPoints = hp;
        }

        public static string CategoryName(BuildingCategory c)
        {
            switch (c)
            {
                case BuildingCategory.Residential: return "Turar joy";
                case BuildingCategory.Economy: return "Savdo";
                case BuildingCategory.Defense: return "Mudofaa";
                case BuildingCategory.Government: return "Boshqaruv";
                case BuildingCategory.Nature: return "Bog‘";
                case BuildingCategory.Military: return "Harbiy";
                default: return "Madaniyat";
            }
        }
    }
}
