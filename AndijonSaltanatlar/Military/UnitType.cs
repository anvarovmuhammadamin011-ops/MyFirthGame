using System.Collections.Generic;
using AndijonSaltanatlar.Buildings;

namespace AndijonSaltanatlar.Military
{
    /// <summary>
    /// Jangchi turlari. Saqlashda butun son sifatida yoziladi — yangi turlar faqat OXIRIGA qo'shiladi
    /// (eski saqlangan o'yinlar buzilmaydi).
    /// </summary>
    public enum UnitType
    {
        Piyoda, Qilichboz, Qalqonchi, Nayzador, Kamonchi, Otliq, SaroyQoriqchisi, ElitaOtliq, HarbiyXabarchi, TaminotBirligi,
        // --- kengaytma (faqat oxiriga) ---
        OgirZirhli, Qoriqchi, HarbiyMuhandis, Razvedkachi, Mergan, Tufangchi
    }

    /// <summary>Jangdagi vazifasi (katalog kartasi va jang sahnasi uchun).</summary>
    public enum UnitRole { Piyoda, Otliq, Masofaviy, Yordamchi }

    /// <summary>Jangchi turining bazaviy statistikasi (o'yin balansi uchun shartli qiymatlar, tarixiy emas).</summary>
    public sealed class UnitDef
    {
        public UnitType Type;
        public string NameUz;
        public UnitRole Role;
        public int Health, Attack, Defense, Speed;
        public int TrainingCost, TrainingDays, Upkeep;
        /// <summary>Yollash uchun kerakli bino (null — talab yo'q) va uning eng kam darajasi.</summary>
        public BuildingType? RequiredBuilding;
        public int RequiredLevel = 1;
        /// <summary>Katalog kartasidagi qisqa tavsif.</summary>
        public string Hint = "";
        /// <summary>Katalog belgisi uchun sprayt kaliti (kadr qo'shimchasisiz).</summary>
        public string IconKey = "";
        /// <summary>Zirh (0..60): jangda kelgan zarbaning shuncha foizini yutadi.</summary>
        public int Armor;
        /// <summary>Eng erta tarixiy davr (EraManager.EraIndex): 0 ilk o'rta asrlar ... 3 Bobur davri. Erta davrda yollanmaydi.</summary>
        public int MinEra;
        /// <summary>Kiyim va jihozlar (katalog kartasi va ma'lumot oynasi uchun).</summary>
        public string Equipment = "";
        /// <summary>Kuch (karta va taqqoslash uchun): hujum + mudofaa.</summary>
        public int Power => Attack + Defense;
    }

    /// <summary>Kengaytiriladigan jangchilar katalogi. Yangi turlar ro'yxat oxiriga qo'shiladi.</summary>
    public static class UnitCatalog
    {
        public static readonly List<UnitDef> All = new List<UnitDef>
        {
            new UnitDef { Type = UnitType.Piyoda,    NameUz = "Piyoda",    Role = UnitRole.Piyoda, Health = 60,  Attack = 10, Defense = 6,  Speed = 5, TrainingCost = 50,  TrainingDays = 1, Upkeep = 1,
                          Armor = 5, Equipment = "To'n, nayza va charm qalqon",
                          Hint = "Arzon, tez tayyorlanadi", IconKey = "unit_spearman" },
            new UnitDef { Type = UnitType.Qilichboz, NameUz = "Qilichboz", Role = UnitRole.Piyoda, Health = 80,  Attack = 16, Defense = 8,  Speed = 5, TrainingCost = 90,  TrainingDays = 2, Upkeep = 2,
                          Armor = 10, Equipment = "Egri qilich, charm sovut",
                          Hint = "Yaqin jangda kuchli", IconKey = "unit_swordsman" },
            new UnitDef { Type = UnitType.Qalqonchi, NameUz = "Qalqonchi", Role = UnitRole.Piyoda, Health = 120, Attack = 8,  Defense = 18, Speed = 4, TrainingCost = 110, TrainingDays = 2, Upkeep = 2,
                          Armor = 25, Equipment = "Katta qalqon, gurzi, dubulg'a",
                          RequiredBuilding = BuildingType.Barracks, Hint = "Saf oldini ushlab turadi", IconKey = "unit_shield" },
            new UnitDef { Type = UnitType.Nayzador,  NameUz = "Nayzachi",  Role = UnitRole.Piyoda, Health = 90,  Attack = 14, Defense = 10, Speed = 5, TrainingCost = 120, TrainingDays = 3, Upkeep = 2,
                          Armor = 10, Equipment = "Uzun nayza, charm sovut",
                          Hint = "Otliqlarga qarshi", IconKey = "unit_spearman" },
            new UnitDef { Type = UnitType.Kamonchi,  NameUz = "Kamonchi",  Role = UnitRole.Masofaviy, Health = 70, Attack = 18, Defense = 6, Speed = 5, TrainingCost = 140, TrainingDays = 3, Upkeep = 3,
                          Armor = 5, Equipment = "Yoy, o'qdon, yengil to'n",
                          RequiredBuilding = BuildingType.Barracks, Hint = "Uzoqdan o'q uzadi", IconKey = "unit_archer" },
            new UnitDef { Type = UnitType.Otliq,     NameUz = "Otliq",     Role = UnitRole.Otliq,  Health = 140, Attack = 22, Defense = 12, Speed = 9, TrainingCost = 250, TrainingDays = 5, Upkeep = 5,
                          Armor = 15, Equipment = "Ot, qilich, kamon, zanjirli sovut",
                          RequiredBuilding = BuildingType.Barracks, Hint = "Tez zarba beradi", IconKey = "unit_cavalry" },
            new UnitDef { Type = UnitType.SaroyQoriqchisi, NameUz = "Saroy qo'riqchisi", Role = UnitRole.Piyoda, Health = 160, Attack = 20, Defense = 22, Speed = 5, TrainingCost = 320, TrainingDays = 4, Upkeep = 6,
                          Armor = 30, Equipment = "Zarhal sovut, oybolta, ipak sallali dubulg'a",
                          RequiredBuilding = BuildingType.MainHall, RequiredLevel = 2, Hint = "Saroyni himoya qiladi", IconKey = "unit_elite" },
            new UnitDef { Type = UnitType.ElitaOtliq, NameUz = "Elita otliq", Role = UnitRole.Otliq, Health = 200, Attack = 32, Defense = 18, Speed = 10, TrainingCost = 480, TrainingDays = 6, Upkeep = 8,
                          Armor = 25, Equipment = "Zirhli ot, uzun nayza, plastinkali sovut",
                          RequiredBuilding = BuildingType.GladiatorArena, Hint = "Eng kuchli otliqlar", IconKey = "unit_cavalry_elite" },
            new UnitDef { Type = UnitType.HarbiyXabarchi, NameUz = "Harbiy xabarchi", Role = UnitRole.Yordamchi, Health = 50, Attack = 6, Defense = 4, Speed = 12, TrainingCost = 70, TrainingDays = 1, Upkeep = 1,
                          Equipment = "Bayroq, burg'u (karnay), xanjar",
                          RequiredBuilding = BuildingType.Tower, Hint = "Ruhni ko'taradi, xabar yetkazadi", IconKey = "unit_herald" },
            new UnitDef { Type = UnitType.TaminotBirligi, NameUz = "Ta'minot birligi", Role = UnitRole.Yordamchi, Health = 90, Attack = 2, Defense = 8, Speed = 4, TrainingCost = 100, TrainingDays = 2, Upkeep = 1,
                          Armor = 5, Equipment = "Qop, xurjun, kichik pichoq",
                          RequiredBuilding = BuildingType.Market, Hint = "Oziq va qurol tashiydi", IconKey = "unit_citizen2" },

            // ----- Kengaytma: yangi birliklar (davr bo'yicha ochiladi) -----
            new UnitDef { Type = UnitType.OgirZirhli, NameUz = "Og'ir zirhli", Role = UnitRole.Piyoda, Health = 170, Attack = 15, Defense = 30, Speed = 3, TrainingCost = 280, TrainingDays = 4, Upkeep = 4,
                          Armor = 40, MinEra = 1, Equipment = "Temir sovut, yopiq dubulg'a, qalqon va gurzi",
                          RequiredBuilding = BuildingType.Barracks, RequiredLevel = 2, Hint = "Zirhi zarbaning 40% ini yutadi, sekin", IconKey = "unit_heavy" },
            new UnitDef { Type = UnitType.Qoriqchi, NameUz = "Shahar qo'riqchisi", Role = UnitRole.Piyoda, Health = 100, Attack = 11, Defense = 14, Speed = 5, TrainingCost = 80, TrainingDays = 1, Upkeep = 1,
                          Armor = 15, Equipment = "Nayza, charm qalqon, mash'al",
                          RequiredBuilding = BuildingType.Tower, Hint = "Darvoza va ko'chalarni qo'riqlaydi", IconKey = "unit_watchman" },
            new UnitDef { Type = UnitType.HarbiyMuhandis, NameUz = "Harbiy muhandis", Role = UnitRole.Yordamchi, Health = 80, Attack = 6, Defense = 8, Speed = 4, TrainingCost = 200, TrainingDays = 3, Upkeep = 3,
                          Armor = 5, MinEra = 1, Equipment = "Bolta, arqon, o'lchov asboblari",
                          RequiredBuilding = BuildingType.Madrasa, Hint = "Qamalda darvozani tezroq buzadi", IconKey = "unit_engineer" },
            new UnitDef { Type = UnitType.Razvedkachi, NameUz = "Razvedkachi", Role = UnitRole.Otliq, Health = 70, Attack = 8, Defense = 5, Speed = 14, TrainingCost = 130, TrainingDays = 2, Upkeep = 2,
                          Equipment = "Yengil to'n, kamon, tezyurar ot",
                          RequiredBuilding = BuildingType.Tower, Hint = "Eng tez: dushmanni oldindan ko'radi", IconKey = "unit_scout" },
            new UnitDef { Type = UnitType.Mergan, NameUz = "Mergan", Role = UnitRole.Masofaviy, Health = 85, Attack = 26, Defense = 9, Speed = 5, TrainingCost = 260, TrainingDays = 4, Upkeep = 4,
                          Armor = 10, MinEra = 2, Equipment = "Murakkab yoy, ikki o'qdon, yengil sovut",
                          RequiredBuilding = BuildingType.Barracks, RequiredLevel = 2, Hint = "Temuriylarning aniq otar kamonchilari", IconKey = "unit_marksman" },
            new UnitDef { Type = UnitType.Tufangchi, NameUz = "Tufangchi", Role = UnitRole.Masofaviy, Health = 80, Attack = 34, Defense = 8, Speed = 4, TrainingCost = 420, TrainingDays = 5, Upkeep = 6,
                          Armor = 5, MinEra = 3, Equipment = "Pilta miltiq (tufang), porox xaltasi",
                          RequiredBuilding = BuildingType.Barracks, RequiredLevel = 3, Hint = "Bobur davri o'qotari (Panipat, 1526)", IconKey = "unit_musketeer" },
        };

        public static UnitDef Get(UnitType t)
        {
            foreach (UnitDef d in All) if (d.Type == t) return d;
            return All[0];
        }

        public static string RoleName(UnitRole r)
        {
            switch (r)
            {
                case UnitRole.Otliq: return "Otliq";
                case UnitRole.Masofaviy: return "Masofaviy";
                case UnitRole.Yordamchi: return "Yordamchi";
                default: return "Piyoda";
            }
        }

        /// <summary>Talab qilinadigan bino nomi (kartada "QULF: ..." uchun).</summary>
        public static string RequirementText(UnitDef d)
        {
            if (d.RequiredBuilding == null) return "";
            string name;
            switch (d.RequiredBuilding.Value)
            {
                case BuildingType.Barracks: name = "Qo'shinxona"; break;
                case BuildingType.GladiatorArena: name = "Jang maydoni"; break;
                case BuildingType.MainHall: name = "Saroy"; break;
                case BuildingType.Tower: name = "Minora"; break;
                case BuildingType.Market: name = "Bozor"; break;
                case BuildingType.Madrasa: name = "Madrasa"; break;
                default: name = d.RequiredBuilding.Value.ToString(); break;
            }
            return d.RequiredLevel > 1 ? name + " " + d.RequiredLevel + "-d." : name;
        }

        /// <summary>Qulf sababi: davr yetmagan bo'lsa — davr nomi (necha texnologiya kerak), aks holda bino talabi.</summary>
        public static string LockText(UnitDef d, int researched)
        {
            if (AndijonSaltanatlar.Progression.EraManager.EraIndex(researched) < d.MinEra)
                return AndijonSaltanatlar.Progression.EraManager.EraNameOf(d.MinEra) + " (" + AndijonSaltanatlar.Progression.EraManager.ResearchFor(d.MinEra) + " texnologiya)";
            return RequirementText(d);
        }

        public static string LevelName(int level)
        {
            switch (level)
            {
                case 1: return "Yangi";
                case 2: return "Tajribali";
                case 3: return "Mohir";
                case 4: return "Elita";
                default: return "Saltanat faxri";
            }
        }
    }
}
