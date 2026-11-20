using AndijonSaltanatlar.Core;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.Buildings
{
    /// <summary>Ustaxona — hunarmandlar (kulol, temirchi, to'quvchi) va quruvchilar ishxonasi.</summary>
    public sealed class Workshop : Building
    {
        public const int BuildCost = 260;

        public Workshop(Point position) : base(position)
        {
            Type = BuildingType.Workshop;
            Name = "Ustaxona";
            Description = "Hunarmandlar kulolchilik, temirchilik va to'quvchilik qiladi; quruvchilar shu yerdan ishga chiqadi.";
            Size = new Point(3, 3);
            Category = BuildingCategory.Economy;
            Cost = BuildCost;
            SetHitPoints(700);
            SpriteKey = SpriteKeys.Workshop;
        }
    }

    /// <summary>Kon — konchilar tosh va ma'dan qazib oladi.</summary>
    public sealed class Mine : Building
    {
        public const int BuildCost = 320;

        public Mine(Point position) : base(position)
        {
            Type = BuildingType.Mine;
            Name = "Kon";
            Description = "Tog' yonbag'ridagi kon: konchilar tosh, mis va temir qazib oladi.";
            Size = new Point(3, 3);
            Category = BuildingCategory.Economy;
            Cost = BuildCost;
            SetHitPoints(900);
            SpriteKey = SpriteKeys.Mine;
        }
    }

    /// <summary>Baliqxona — baliqchilar daryo va ariqlardan baliq tutib, quritadi.</summary>
    public sealed class Fishery : Building
    {
        public const int BuildCost = 180;

        public Fishery(Point position) : base(position)
        {
            Type = BuildingType.Fishery;
            Name = "Baliqxona";
            Description = "Baliqchilar uyi: to'rlar, qayiq va baliq quritish so'rilari.";
            Size = new Point(3, 3);
            Category = BuildingCategory.Nature;
            Cost = BuildCost;
            SetHitPoints(500);
            SpriteKey = SpriteKeys.Fishery;
        }
    }

    /// <summary>Shifoxona (dorushshifo) — tabiblar aholini davolaydi.</summary>
    public sealed class Clinic : Building
    {
        public const int BuildCost = 420;

        public Clinic(Point position) : base(position)
        {
            Type = BuildingType.Clinic;
            Name = "Shifoxona";
            Description = "Dorushshifo: tabiblar bemorlarni dorivor o'tlar bilan davolaydi. Aholi sog'lig'i va baxti oshadi.";
            Size = new Point(3, 3);
            Category = BuildingCategory.Culture;
            Cost = BuildCost;
            SetHitPoints(800);
            SpriteKey = SpriteKeys.Clinic;
        }
    }
}
