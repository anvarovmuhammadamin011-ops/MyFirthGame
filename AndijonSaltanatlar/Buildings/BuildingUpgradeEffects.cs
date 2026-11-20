namespace AndijonSaltanatlar.Buildings
{
    /// <summary>
    /// Har bir bino turi uchun kattalashtirish (upgrade) ta'siri: o'ziga xos daraja nomlari,
    /// narx koeffitsienti, mustahkamlik/sig'im o'sishi, iqtisodiy hissa va panelda ko'rsatiladigan foyda.
    /// Ferma, bozor, uy, kazarma va h.k. — har biri o'zgacha foyda oladi (umumiy emas).
    /// </summary>
    public static class BuildingUpgradeEffects
    {
        /// <summary>Tur bo'yicha 5 daraja nomi (milliy uslubda).</summary>
        public static string[] LevelNames(BuildingType t) => t switch
        {
            BuildingType.House => new[] { "Kulba", "Hovli", "Ko'shk", "Boy hovli", "Saroycha" },
            BuildingType.Farm => new[] { "Tomorqa", "Dala", "Bog'-dala", "Xo'jalik", "Shoh xo'jaligi" },
            BuildingType.Garden => new[] { "Gulzor", "Bog'cha", "Mevazor", "Chorbog'", "Shoh bog'i" },
            BuildingType.Market => new[] { "Rasta", "Bozorcha", "Chorsu", "Katta bozor", "Saltanat bozori" },
            BuildingType.Caravanserai => new[] { "Rabot", "Saroycha", "Karvonsaroy", "Katta karvonsaroy", "Shoh karvonsaroyi" },
            BuildingType.Barracks => new[] { "Qarorgoh", "Navkarxona", "Qo'shingoh", "Qal'a qo'shingohi", "Sarkarda qarorgohi" },
            BuildingType.Tower => new[] { "Qorovulxona", "Nazorat minorasi", "Jang minorasi", "Qal'a minorasi", "Saltanat minorasi" },
            BuildingType.Wall => new[] { "Devor", "Mustahkam devor", "Qal'a devori", "Baland devor", "Saltanat devori" },
            BuildingType.Gate => new[] { "Darvoza", "Temir darvoza", "Qal'a darvozasi", "Katta darvoza", "Saltanat darvozasi" },
            BuildingType.MainHall => new[] { "Qarorgoh", "Hokim saroyi", "Beklik saroyi", "Xon saroyi", "Saltanat saroyi" },
            BuildingType.Madrasa => new[] { "Maktab", "Madrasa", "Katta madrasa", "Oliy madrasa", "Dorul-ilm" },
            BuildingType.Mosque => new[] { "Namozgoh", "Masjid", "Jome masjid", "Katta masjid", "Saltanat masjidi" },
            BuildingType.GladiatorArena => new[] { "Maydon", "Kurash maydoni", "Arena", "Katta arena", "Saltanat arenasi" },
            BuildingType.Workshop => new[] { "Do'kon-ustaxona", "Ustaxona", "Katta ustaxona", "Hunarmandlar rastasi", "Saltanat ustaxonasi" },
            BuildingType.Mine => new[] { "Qazilma", "Kon", "Chuqur kon", "Katta kon", "Saltanat koni" },
            BuildingType.Fishery => new[] { "Baliqchi kulbasi", "Baliqxona", "Katta baliqxona", "Baliq bozori", "Saltanat baliqxonasi" },
            BuildingType.Clinic => new[] { "Tabibxona", "Shifoxona", "Dorushshifo", "Katta dorushshifo", "Saltanat dorushshifosi" },
            _ => new[] { "Oddiy", "Bezatilgan", "Boy", "Hashamatli", "Saltanat" },
        };

        public static string LevelName(BuildingType t, int level)
        {
            string[] names = LevelNames(t);
            int i = System.Math.Clamp(level, 1, names.Length) - 1;
            return names[i];
        }

        /// <summary>Narx koeffitsienti: devor/darvoza arzon, saroy/arena qimmat.</summary>
        public static float CostFactor(BuildingType t) => t switch
        {
            BuildingType.Wall => 0.5f,
            BuildingType.Gate => 0.6f,
            BuildingType.House => 0.8f,
            BuildingType.Garden => 0.8f,
            BuildingType.Farm => 0.9f,
            BuildingType.Caravanserai => 1.1f,
            BuildingType.GladiatorArena => 1.2f,
            BuildingType.MainHall => 1.6f,
            _ => 1.0f,
        };

        /// <summary>Mustahkamlik o'sishi (ulush) — mudofaa binolari ko'proq oladi.</summary>
        public static float HpGrowth(BuildingType t) => t switch
        {
            BuildingType.Wall => 0.5f,
            BuildingType.Gate => 0.4f,
            BuildingType.Tower => 0.35f,
            BuildingType.MainHall => 0.3f,
            _ => 0.25f,
        };

        /// <summary>Aholi sig'imi o'sishi (sig'imi bor binolar; uy eng ko'p).</summary>
        public static int CapacityDelta(BuildingType t, int newLevel) => t switch
        {
            BuildingType.House => newLevel >= 4 ? 4 : 3,
            _ => newLevel >= 4 ? 3 : 2,
        };

        /// <summary>Har keyingi daraja iqtisodiy hissaga qancha qo'shadi (1-daraja = asos, qo'shimcha yo'q).</summary>
        public static float EconomyPerLevel(BuildingType t) => t switch
        {
            BuildingType.Market => 0.5f,
            BuildingType.Caravanserai => 0.5f,
            BuildingType.Farm => 0.5f,
            BuildingType.Garden => 0.4f,
            BuildingType.Workshop => 0.5f,
            BuildingType.Mine => 0.5f,
            BuildingType.Fishery => 0.4f,
            _ => 0f,
        };

        /// <summary>Binoning iqtisodiy hissa koeffitsienti: 1-daraja = 1, yuqori daraja ko'proq.</summary>
        public static float EconomyFactor(BuildingType t, int level) => 1f + (System.Math.Max(1, level) - 1) * EconomyPerLevel(t);
        public static float EconomyFactor(Building b) => b == null ? 0f : EconomyFactor(b.Type, b.Level);

        /// <summary>Panelda ko'rsatiladigan qisqa foyda matni (kattalashtirish nima beradi).</summary>
        public static string Benefit(BuildingType t) => t switch
        {
            BuildingType.House => "aholi sig'imi va oila farovonligi",
            BuildingType.Farm => "hosil va ombor hajmi",
            BuildingType.Garden => "meva hosili va shahar go'zalligi",
            BuildingType.Market => "savdo daromadi va sotuvchilar",
            BuildingType.Caravanserai => "karvonlar soni va masofasi",
            BuildingType.Barracks => "yollash tezligi va askar darajasi",
            BuildingType.Tower => "o'q masofasi va zarbasi",
            BuildingType.Wall => "mustahkamlik",
            BuildingType.Gate => "mustahkamlik va ta'mirlash tezligi",
            BuildingType.Mosque => "aholi farovonligi",
            BuildingType.Madrasa => "ilm va texnologiya tezligi",
            BuildingType.MainHall => "soliq daromadi va qo'riqchilar",
            BuildingType.GladiatorArena => "tomosha daromadi va gladiatorlar kuchi",
            BuildingType.Workshop => "hunarmandlar soni va buyumlar savdosi",
            BuildingType.Mine => "konchilar soni va ma'dan hajmi",
            BuildingType.Fishery => "baliqchilar soni va ovi",
            BuildingType.Clinic => "tabiblar soni va aholi sog'lig'i",
            _ => "mustahkamlik va sig'im",
        };
    }
}
