using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Buildings;

namespace AndijonSaltanatlar.Core
{
    /// <summary>
    /// SHAHAR RIVOJLANISH DARAJASI: shahar qanchalik yuksalgan bo'lsa — aholi va qo'shin shundan rivojlanadi.
    /// Ball = aholi soni + tayyor binolarning darajalari yig'indisi. Daraja 0..8.
    /// Harbiy: har daraja qo'shin umumiy kuchiga +5% (jonli, jangda hisoblanadi).
    /// Aholi: har daraja ko'chib keluvchilar oqimini oshiradi (kuchli shahar odam tortadi).
    /// Jonli hisoblanadi — saqlash/yuklashga bog'liq emas, har doim joriy holatni aks ettiradi.
    /// </summary>
    public static class CityProgress
    {
        /// <summary>Rivojlanish balli: aholi + tayyor binolar darajalari (bino darajasi 2 barobar og'irlik bilan).</summary>
        public static int Score(int population, BuildingManager buildings) => Score(population, buildings?.Buildings);

        /// <summary>Ro'yxatdan ball (test va boshqa manbalar uchun).</summary>
        public static int Score(int population, IEnumerable<Building> buildings)
        {
            int b = 0;
            if (buildings != null)
                foreach (Building x in buildings)
                    if (x.BuildProgress >= 1f) b += x.Level;
            return Math.Max(0, population) + b * 2;
        }

        /// <summary>Balldan daraja (0..8).</summary>
        public static int Level(int score) => Math.Clamp(score / 12, 0, 8);

        /// <summary>Shahar darajasi (qulaylik uchun).</summary>
        public static int Level(int population, BuildingManager buildings) => Level(Score(population, buildings));

        /// <summary>Qo'shin umumiy kuchiga qo'shiladigan foiz (har daraja +5%).</summary>
        public static int MilitaryBonusPercent(int level) => level * 5;

        /// <summary>Ko'chib keluvchilar oqimi koeffitsienti (har daraja +20%).</summary>
        public static float ImmigrationFactor(int level) => 1f + level * 0.2f;
    }
}
