using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.SmartConstruction;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.CityPlanning
{
    /// <summary>Mahalla turi — PROMPT 15, 15-bo'lim.</summary>
    public enum DistrictType
    {
        Residential, // Turar joy
        Commercial,  // Savdo
        Craft,       // Hunarmandchilik
        Cultural,    // Madaniyat
        Agricultural,// Dehqonchilik
        Garden,      // Bog' va dam olish
        Mixed        // Muvozanatli
    }

    /// <summary>Blueprint elementi holati (rang) — PROMPT 15, 9-bo'lim.</summary>
    public enum BlueprintStatus
    {
        Buildable,     // yashil — qurish mumkin
        NeedsResource, // sariq — resurs/qo'shimcha talab
        Blocked        // qizil — qurish mumkin emas
    }

    /// <summary>Rejadagi bitta bino (preview — GameState'ni o'zgartirmaydi).</summary>
    public class BlueprintItem
    {
        public int Id { get; set; }
        public BuildingType Kind { get; set; }
        public Point Tile { get; set; }     // shimoliy (origin) katak
        public int Size { get; set; } = 1;
        public int Cost { get; set; }
        public BlueprintStatus Status { get; set; } = BlueprintStatus.Buildable;
        public int BuildTimeDays { get; set; } = 3;
        public string BenefitUz { get; set; } = "";
        public string RequirementUz { get; set; } = "";
        public string RequiredTech { get; set; } = "";
        public string StatusReasonUz { get; set; } = "";
        public int PopulationCapacity { get; set; }

        public string NameUz => Kind switch
        {
            BuildingType.House => "Uy",
            BuildingType.Market => "Bozor",
            BuildingType.Garden => "Bog'",
            BuildingType.Farm => "Ferma",
            BuildingType.GladiatorArena => "Jang maydoni",
            BuildingType.Madrasa => "Madrasa",
            BuildingType.Mosque => "Masjid",
            BuildingType.Caravanserai => "Karvonsaroy",
            BuildingType.Barracks => "Qo'shinxona",
            BuildingType.Tower => "Minora",
            BuildingType.Gate => "Darvoza",
            BuildingType.MainHall => "Saroy",
            BuildingType.Workshop => "Ustaxona",
            BuildingType.Mine => "Kon",
            BuildingType.Fishery => "Baliqxona",
            BuildingType.Clinic => "Shifoxona",
            _ => Kind.ToString()
        };

        public bool Covers(int x, int y)
            => x >= Tile.X && x < Tile.X + Size && y >= Tile.Y && y < Tile.Y + Size;

        public BlueprintItem Clone() => (BlueprintItem)MemberwiseClone();
    }

    /// <summary>Blueprint'dagi yo'l katagi; narxi va holati alohida saqlanadi.</summary>
    public class BlueprintRoad
    {
        public Point Tile { get; set; }
        public int Cost { get; set; }
        public BlueprintStatus Status { get; set; } = BlueprintStatus.Buildable;
        public string StatusReasonUz { get; set; } = "";
    }

    /// <summary>
    /// AI tuzgan shahar rejasi (blueprint) — PROMPT 15, 6 & 9-bo'lim. Tasdiqlanmaguncha faqat preview.
    /// Ushbu obyekt GameState'ni o'zgartirmaydi: faqat rejalashtirilgan kataklar va taxminiy narxlar.
    /// </summary>
    public class CityPlan
    {
        public int Id { get; set; }
        public int CreatedDay { get; set; }
        public DistrictType Type { get; set; }
        public string NameUz { get; set; } = "";
        public string RationaleUz { get; set; } = "";
        public Point Anchor { get; set; } = new Point(-1, -1);
        public List<BlueprintItem> Buildings { get; set; } = new();
        public List<BlueprintRoad> Roads { get; set; } = new();

        /// <summary>Preview holati: Draft → ReadyForApproval → Approved. Tasdiqdan keyin navbat boshlanadi.</summary>
        public PlanExecState State { get; set; } = PlanExecState.Draft;

        public int BuildingsCost { get; set; }
        public int RoadsCost { get; set; }
        public int TotalCost => BuildingsCost + RoadsCost;
        public int TotalBuildDays { get; set; }

        /// <summary>Barcha elementlar tekshiruvdan o'tgan va kamida bitta bino quriladigan bo'lsa reja yaroqli.</summary>
        public bool IsValid { get; set; }

        public int BuildableCount
        {
            get { int n = 0; foreach (var b in Buildings) if (b.Status == BlueprintStatus.Buildable) n++; return n; }
        }

        public int HouseCount => Count(BuildingType.House);
        public int MarketCount => Count(BuildingType.Market) + Count(BuildingType.Caravanserai);
        public int GardenCount => Count(BuildingType.Garden);

        public int Count(BuildingType type)
        {
            int n = 0;
            foreach (var b in Buildings) if (b.Kind == type) n++;
            return n;
        }

        public int RoadCount => Roads.Count;

        public int ExpectedHousing
        {
            get
            {
                int n = 0;
                foreach (var b in Buildings)
                    if (b.Status != BlueprintStatus.Blocked) n += b.PopulationCapacity;
                return n;
            }
        }

        public BlueprintItem ItemAt(int x, int y)
        {
            foreach (var b in Buildings) if (b.Covers(x, y)) return b;
            return null;
        }

        /// <summary>Reja nusxasi — bekor qilish/qayta rejalashtirishda eski draftni xavfsiz saqlash uchun.</summary>
        public CityPlan Clone()
        {
            var p = new CityPlan
            {
                Id = Id, CreatedDay = CreatedDay, Type = Type, NameUz = NameUz,
                RationaleUz = RationaleUz, Anchor = Anchor, BuildingsCost = BuildingsCost,
                RoadsCost = RoadsCost, TotalBuildDays = TotalBuildDays, IsValid = IsValid,
                State = State
            };
            foreach (var b in Buildings) p.Buildings.Add(b.Clone());
            foreach (var r in Roads) p.Roads.Add(new BlueprintRoad { Tile = r.Tile, Cost = r.Cost, Status = r.Status, StatusReasonUz = r.StatusReasonUz });
            return p;
        }
    }
}
