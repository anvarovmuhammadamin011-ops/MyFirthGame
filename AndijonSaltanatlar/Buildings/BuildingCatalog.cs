using System.Collections.Generic;
using AndijonSaltanatlar.Core;

namespace AndijonSaltanatlar.Buildings
{
    /// <summary>Qurilish menyusidagi bitta bino haqidagi ma'lumot.</summary>
    public sealed class CatalogEntry
    {
        public BuildingType Type;
        public string Name;
        public string Category;
        public int Cost;
        public string IconKey;
        public string Description;
        /// <summary>Qurish uchun kerakli texnologiya id'si (bo'sh = doim ochiq).</summary>
        public string RequiredTech = "";
    }

    /// <summary>
    /// O'yinchi qura oladigan binolar ro'yxati. Yangi bino qo'shish uchun — shu yerga bitta
    /// qator (va kerak bo'lsa BuildingFactory'ga tur). Sprite'lar mavjud art'dan olinadi.
    /// </summary>
    public static class BuildingCatalog
    {
        public static readonly List<CatalogEntry> Buildable = new List<CatalogEntry>
        {
            new CatalogEntry { Type = BuildingType.House,        Name = "Uy",          Category = "Turar joy", Cost = House.BuildCost,  IconKey = SpriteKeys.House + "_0",  Description = "Hovlili paxsa uy. Shaharga 5 aholi qo'shadi." },
            new CatalogEntry { Type = BuildingType.Garden,       Name = "Bog'",        Category = "Bog'",      Cost = 90,               IconKey = SpriteKeys.Garden,        Description = "Chorbog'. Oziq-ovqat yetishtiradi." },
            new CatalogEntry { Type = BuildingType.Farm,          Name = "Ferma",       Category = "Xo'jalik",  Cost = Farm.BuildCost,  IconKey = SpriteKeys.Farm,          Description = "Dehqonlar ekin ekib, oziq-ovqat yetishtiradi." },
            new CatalogEntry { Type = BuildingType.Fishery,      Name = "Baliqxona",   Category = "Xo'jalik",  Cost = Fishery.BuildCost, IconKey = SpriteKeys.Fishery,      Description = "Baliqchilar ariq va daryodan baliq tutadi. Oziq-ovqat beradi." },
            new CatalogEntry { Type = BuildingType.Mine,         Name = "Kon",         Category = "Xo'jalik",  Cost = Mine.BuildCost,   IconKey = SpriteKeys.Mine,         Description = "Konchilar tosh va ma'dan qazib oladi. Savdoga mahsulot." },
            new CatalogEntry { Type = BuildingType.Market,       Name = "Bozor",       Category = "Savdo",     Cost = Market.BuildCost, IconKey = SpriteKeys.Market,        Description = "Gumbazli savdo toqi. Savdogarlar va bozor egasi ishlaydi." },
            new CatalogEntry { Type = BuildingType.Workshop,     Name = "Ustaxona",    Category = "Savdo",     Cost = Workshop.BuildCost, IconKey = SpriteKeys.Workshop,   Description = "Hunarmandlar buyum yasaydi, quruvchilar qurilishni tezlashtiradi.", RequiredTech = "hunarmandchilik" },
            new CatalogEntry { Type = BuildingType.Caravanserai, Name = "Karvonsaroy", Category = "Savdo",     Cost = 500,              IconKey = SpriteKeys.Caravanserai,  Description = "Ipak yo'li karvonlari qo'nadi. Savdoni jonlantiradi.", RequiredTech = "karvon-savdosi" },
            new CatalogEntry { Type = BuildingType.Barracks,     Name = "Qo'shinxona", Category = "Harbiy",    Cost = 300,              IconKey = SpriteKeys.Barracks,      Description = "Navkarlar turadigan va mashq qiladigan qo'rg'on." },
            new CatalogEntry { Type = BuildingType.GladiatorArena, Name = "Jang maydoni", Category = "Harbiy", Cost = GladiatorArenaBuilding.BuildCost, IconKey = SpriteKeys.GladiatorArena, Description = "Gladiatorlar mashq qiladigan maydon." },
            new CatalogEntry { Type = BuildingType.Armory,       Name = "Qurolxona",   Category = "Harbiy",    Cost = Armory.BuildCost, IconKey = SpriteKeys.Barracks,      Description = "Qurol-yarog' quyiladi. Yollash tezlashadi, shahar harbiy qudrati oshadi." },
            new CatalogEntry { Type = BuildingType.Madrasa,      Name = "Madrasa",     Category = "Madaniyat", Cost = 600,              IconKey = SpriteKeys.Madrasa,       Description = "Ilm maskani. Koshinli peshtoq.", RequiredTech = "me'morchilik" },
            new CatalogEntry { Type = BuildingType.Mosque,       Name = "Masjid",      Category = "Madaniyat", Cost = 700,              IconKey = SpriteKeys.Mosque,        Description = "Ko'k gumbazli jome masjidi.", RequiredTech = "gumbaz-qurilishi" },
            new CatalogEntry { Type = BuildingType.Clinic,       Name = "Shifoxona",   Category = "Madaniyat", Cost = Clinic.BuildCost, IconKey = SpriteKeys.Clinic,        Description = "Tabiblar aholini davolaydi: sog'liq va baxt oshadi.", RequiredTech = "tibbiyot" },
            new CatalogEntry { Type = BuildingType.Tower,        Name = "Minora",      Category = "Mudofaa",   Cost = Tower.BuildCost,  IconKey = SpriteKeys.Tower,         Description = "Qo'riqlash minorasi." },
            new CatalogEntry { Type = BuildingType.Gate,         Name = "Darvoza",     Category = "Mudofaa",   Cost = 80,               IconKey = SpriteKeys.Gate,          Description = "Shahar darvozasi. Jangchilar o'tadi." },
            new CatalogEntry { Type = BuildingType.Wall,         Name = "Devor",       Category = "Mudofaa",   Cost = Wall.BuildCost,   IconKey = SpriteKeys.Wall,          Description = "Qal'a devori bo'lagi. Sudrab qurish mumkin." },
            new CatalogEntry { Type = BuildingType.MainHall,     Name = "Saroy",       Category = "Boshqaruv", Cost = 1500,             IconKey = SpriteKeys.MainHall,      Description = "Hokim o'rdasi. Saltanatning yuragi.", RequiredTech = "shaharsozlik" },
        };

        public static string RequiredTech(BuildingType type)
        {
            foreach (CatalogEntry e in Buildable) if (e.Type == type) return e.RequiredTech;
            return "";
        }
    }
}
