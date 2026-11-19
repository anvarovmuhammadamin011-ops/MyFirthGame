using System;
using System.Collections.Generic;
using System.Linq;

namespace AndijonSaltanatlar.Shared.Geo
{
    /// <summary>Viloyat (xaritada yozuv sifatida chiziladi).</summary>
    public sealed record MapProvince(string Key, string Name, double LabelLon, double LabelLat);

    /// <summary>Hudud (tuman/shahar) — har birida bitta shahar. Owner — boshlang'ich AI saltanat kaliti (null = mustaqil).</summary>
    public sealed record MapRegionSeed(string Province, string Name, double Lon, double Lat, int Population, string Owner = null);

    /// <summary>Tarixiy AI saltanatlar (XIX asr boshi).</summary>
    public sealed record MapKingdomSeed(string Key, string Name, string Ruler, int R, int G, int B, string Strategy, string Flag);

    /// <summary>
    /// O'ZBEKISTON xaritasi: viloyatlar, tumanlar (hududlar), daryolar, tog' tizmalari va chegara.
    /// Koordinatalar haqiqiy uzunlik/kenglikda saqlanadi va o'yin uchun 0..100 xarita birligiga o'giriladi
    /// (RegionEntity.Latitude = X, Longitude = Y — tarixiy nomlar). Server ham, o'yin ham shu bitta manbadan foydalanadi.
    /// Yevropa keyingi bosqichda alohida xarita varag'i sifatida qo'shiladi.
    /// </summary>
    public static class UzMap
    {
        /// <summary>Xarita versiyasi: World.MapVersion shundan kichik bo'lsa — dunyo yangi xaritaga ko'chiriladi.</summary>
        public const int Version = 2;

        public const double LonMin = 55.6, LonMax = 73.4, LatMin = 37.0, LatMax = 45.9;
        private const double MidLatRad = (LatMin + LatMax) / 2 * Math.PI / 180;

        /// <summary>Xarita varag'ining haqiqiy kenglik/balandlik nisbati (km bo'yicha).</summary>
        public static readonly float Aspect = (float)((LonMax - LonMin) * Math.Cos(MidLatRad) / (LatMax - LatMin));

        /// <summary>Varaqning eni va bo'yi kilometrda.</summary>
        public static readonly double WidthKm = (LonMax - LonMin) * 111.32 * Math.Cos(MidLatRad);
        public static readonly double HeightKm = (LatMax - LatMin) * 111.0;

        public static float X(double lon) => (float)((lon - LonMin) / (LonMax - LonMin) * 100.0);
        public static float Y(double lat) => (float)((LatMax - lat) / (LatMax - LatMin) * 100.0);
        public static double Lon(double x) => LonMin + x / 100.0 * (LonMax - LonMin);
        public static double Lat(double y) => LatMax - y / 100.0 * (LatMax - LatMin);

        /// <summary>Ikki xarita nuqtasi orasidagi masofa (km).</summary>
        public static double DistanceKm(double x1, double y1, double x2, double y2)
        {
            double dx = (x2 - x1) / 100.0 * WidthKm, dy = (y2 - y1) / 100.0 * HeightKm;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Eski (faqat Farg'ona vodiysi, 0..100) koordinatani yangi xaritaga o'giradi.</summary>
        public static (float X, float Y) FromLegacyFergana(double oldX, double oldY)
            => (X(70.9 + oldX / 100.0 * 2.0), Y(41.15 - oldY / 100.0 * 0.85));

        // ------------------------------------------------------------------ Chegara (uzunlik, kenglik)
        public static readonly (double Lon, double Lat)[] Outline =
        {
            (55.93, 45.00), (58.50, 45.59), (58.69, 45.50), (60.24, 44.78), (61.06, 44.41), (62.01, 43.50), (63.19, 43.65),
            (64.90, 43.73), (66.10, 43.00), (66.02, 41.99), (66.51, 41.99), (66.71, 41.17), (67.99, 41.14), (68.25, 40.75),
            (68.55, 41.05), (68.80, 41.20), (69.07, 41.38), (70.39, 42.08), (70.96, 42.27), (71.26, 42.17), (70.42, 41.52),
            (71.16, 41.14), (71.87, 41.39), (73.06, 40.87), (71.77, 40.15), (71.01, 40.24), (70.60, 40.22), (70.46, 40.50),
            (70.67, 40.96), (69.33, 40.73), (69.01, 40.09), (68.54, 39.53), (67.70, 39.58), (67.44, 39.14), (68.18, 38.90),
            (68.39, 38.16), (67.83, 37.14), (67.08, 37.36), (66.52, 37.36), (66.55, 37.97), (65.22, 38.40), (64.17, 38.89),
            (63.52, 39.36), (62.37, 40.05), (61.88, 41.08), (61.55, 41.27), (60.47, 41.22), (60.08, 41.43), (59.98, 42.22),
            (58.63, 42.75), (57.79, 42.17), (56.93, 41.83), (57.10, 41.32), (55.97, 41.31)
        };

        /// <summary>Orol dengizi — eski xaritalardagidek to'la holatda.</summary>
        public static readonly (double Lon, double Lat)[] Aral =
        {
            (58.25, 44.45), (58.45, 44.05), (59.05, 43.98), (59.80, 43.92), (60.45, 44.10), (61.10, 44.60), (61.55, 45.35),
            (61.65, 46.00), (61.25, 46.60), (60.30, 46.75), (59.20, 46.40), (58.55, 45.80), (58.20, 45.10)
        };

        public static readonly (double Lon, double Lat)[][] Rivers =
        {
            // Amudaryo
            new[] { (68.40, 37.12), (67.80, 37.18), (67.30, 37.26), (66.60, 37.36), (65.90, 37.55), (65.20, 37.85), (64.50, 38.40), (63.60, 39.10),
                    (62.90, 39.80), (62.20, 40.50), (61.40, 41.20), (60.90, 41.55), (60.50, 41.95), (60.00, 42.30), (59.60, 42.55), (59.25, 43.00), (59.05, 43.45), (59.00, 43.95) },
            // Sirdaryo (Norin bilan)
            new[] { (73.40, 41.05), (72.80, 41.02), (72.20, 40.98), (71.80, 40.90), (71.20, 40.78), (70.60, 40.62), (70.10, 40.40), (69.60, 40.28),
                    (69.15, 40.33), (68.95, 40.62), (68.75, 40.95), (68.40, 41.30), (67.95, 41.30), (67.40, 42.00), (66.60, 43.20), (65.50, 44.85), (64.40, 45.30), (63.10, 45.80) },
            // Zarafshon
            new[] { (68.20, 39.42), (67.60, 39.50), (67.20, 39.58), (66.96, 39.70), (66.50, 39.86), (66.00, 39.96), (65.40, 40.12), (64.90, 40.04), (64.45, 39.86), (64.00, 39.62), (63.75, 39.45) },
            // Qashqadaryo
            new[] { (67.20, 39.05), (66.70, 38.98), (66.20, 38.90), (65.75, 38.85), (65.30, 38.98) },
            // Surxondaryo
            new[] { (68.05, 38.55), (67.75, 38.15), (67.50, 37.75), (67.32, 37.30) },
            // Chirchiq
            new[] { (70.05, 41.62), (69.65, 41.45), (69.25, 41.18), (68.85, 40.96) },
            // Qoradaryo
            new[] { (73.20, 40.60), (72.80, 40.70), (72.30, 40.80), (71.85, 40.89) },
        };

        /// <summary>Tog' tizmalari (siyohda cho'qqilar bilan chiziladi).</summary>
        public static readonly (double Lon, double Lat)[][] Ridges =
        {
            new[] { (69.85, 41.75), (70.40, 42.00), (70.95, 42.20) },                    // Ugom-Piskom
            new[] { (70.05, 41.25), (70.55, 41.48), (71.10, 41.62), (71.70, 41.78) },   // Chotqol
            new[] { (69.75, 40.80), (70.20, 40.95), (70.62, 41.08) },                    // Qurama
            new[] { (69.50, 39.85), (70.30, 39.90), (71.00, 39.86), (71.80, 39.80), (72.60, 39.76), (73.30, 39.70) }, // Turkiston-Oloy
            new[] { (72.95, 41.55), (73.30, 41.10) },                                    // Farg'ona tizmasi
            new[] { (67.60, 39.36), (68.30, 39.30), (69.00, 39.36) },                    // Zarafshon
            new[] { (67.70, 38.95), (68.25, 38.85), (68.80, 38.80) },                    // Hisor
            new[] { (66.90, 38.62), (67.20, 38.30), (67.42, 38.00) },                    // Boysuntog'
            new[] { (65.90, 40.62), (66.50, 40.52), (67.10, 40.42) },                    // Nurota
            new[] { (67.70, 39.80), (68.25, 39.82) },                                    // Molguzar
            new[] { (61.40, 42.02), (61.90, 42.40) },                                    // Sultonuvays
        };

        /// <summary>Cho'llar (nuqtali shtrix): Qizilqum va Ustyurt.</summary>
        public static readonly (double LonMin, double LatMin, double LonMax, double LatMax)[] Deserts =
        {
            (61.6, 40.4, 66.2, 43.4),
            (56.1, 42.1, 58.3, 44.8),
        };

        // ------------------------------------------------------------------ Viloyatlar
        public static readonly MapProvince[] Provinces =
        {
            new("qq", "Qoraqalpog'iston", 58.2, 43.0),
            new("xr", "Xorazm", 60.55, 41.95),
            new("nv", "Navoiy", 64.3, 42.4),
            new("bx", "Buxoro", 63.4, 40.3),
            new("sm", "Samarqand", 66.5, 39.45),
            new("qd", "Qashqadaryo", 65.95, 38.45),
            new("sx", "Surxondaryo", 67.35, 37.95),
            new("jz", "Jizzax", 67.45, 40.65),
            new("sr", "Sirdaryo", 68.55, 40.35),
            new("tv", "Toshkent", 69.75, 41.45),
            new("nm", "Namangan", 71.45, 41.2),
            new("fg", "Farg'ona", 71.15, 40.22),
            new("an", "Andijon", 72.45, 40.95),
        };

        // ------------------------------------------------------------------ AI saltanatlar
        public static readonly MapKingdomSeed[] Kingdoms =
        {
            new("andijon", "Andijon xonligi", "Umarbek", 190, 80, 60, "balanced", "1-2-0-0-2-6"),
            new("fargona", "Farg'ona begligi", "Sulaymonbek", 120, 90, 170, "trade", "3-9-6-0-1-0"),
            new("xiva", "Xiva xonligi", "Muhammad Rahimxon", 30, 100, 110, "defensive", "7-13-6-0-6-0"),
            new("buxoro", "Buxoro amirligi", "Amir Haydar", 120, 60, 40, "balanced", "10-2-6-1-3-0"),
            new("qoqon", "Qo'qon xonligi", "Olimxon", 90, 115, 45, "aggressive", "2-5-0-5-5-1"),
        };

        /// <summary>
        /// Viloyat begliklari — "barcha shaharlar AI davlat" rejimida (server sozlamasi World:AiOwnsAllCities)
        /// egasiz hududlar shularga beriladi. Har viloyatning o'z AI davlati bor.
        /// </summary>
        public static readonly MapKingdomSeed[] ProvinceKingdoms =
        {
            new("qongirot", "Qo'ng'irot biyligi", "To'ra biy", 70, 130, 160, "defensive", "4-11-6-0-4-0"),
            new("karmana", "Karmana begligi", "Doniyorbiy", 160, 120, 60, "trade", "5-8-0-2-2-0"),
            new("samarqand", "Samarqand begligi", "Ulug'bek", 40, 90, 160, "balanced", "6-3-6-0-1-2"),
            new("shahrisabz", "Shahrisabz begligi", "Jo'rabek", 150, 50, 90, "aggressive", "8-1-0-3-5-0"),
            new("termiz", "Termiz begligi", "Sayyid Alibek", 180, 140, 40, "defensive", "9-6-6-1-0-0"),
            new("jizzax", "Jizzax begligi", "Yorqinbek", 100, 140, 70, "balanced", "3-4-0-4-3-0"),
            new("sirdaryo", "Sirdaryo begligi", "Erkinbek", 60, 150, 120, "trade", "2-10-6-0-2-0"),
            new("toshkent", "Toshkent begligi", "Yunusxo'ja", 170, 60, 60, "aggressive", "1-7-0-1-4-3"),
            new("namangan", "Namangan begligi", "Rahmonqulbek", 80, 80, 150, "balanced", "6-12-6-0-3-0"),
        };

        /// <summary>Viloyatning egasiz hududlari qaysi AI davlatga tegishli (Kingdoms yoki ProvinceKingdoms kaliti).</summary>
        public static string ProvinceKingdom(string province) => province switch
        {
            "qq" => "qongirot", "xr" => "xiva", "nv" => "karmana", "bx" => "buxoro", "sm" => "samarqand",
            "qd" => "shahrisabz", "sx" => "termiz", "jz" => "jizzax", "sr" => "sirdaryo", "tv" => "toshkent",
            "nm" => "namangan", "fg" => "fargona", "an" => "andijon", _ => null
        };

        // ------------------------------------------------------------------ Hududlar (har biri — bitta shahar)
        public static readonly MapRegionSeed[] Regions =
        {
            // Qoraqalpog'iston
            new("qq", "Nukus", 59.61, 42.46, 160),
            new("qq", "Mo'ynoq", 59.02, 43.70, 70),
            new("qq", "Qo'ng'irot", 58.84, 43.05, 110),
            new("qq", "Chimboy", 59.78, 42.94, 90),
            new("qq", "Taxtako'pir", 60.28, 43.02, 60),
            new("qq", "Beruniy", 60.75, 41.72, 110),
            new("qq", "To'rtko'l", 61.02, 41.55, 120),
            new("qq", "Ustyurt", 57.30, 43.40, 40),
            // Xorazm
            new("xr", "Xiva", 60.36, 41.38, 320, "xiva"),
            new("xr", "Urganch", 60.63, 41.55, 280, "xiva"),
            new("xr", "Hazorasp", 61.07, 41.32, 150, "xiva"),
            new("xr", "Xonqa", 60.86, 41.47, 100),
            new("xr", "Gurlan", 60.39, 41.84, 100),
            new("xr", "Shovot", 60.30, 41.65, 100),
            // Navoiy
            new("nv", "Navoiy", 65.38, 40.10, 160),
            new("nv", "Zarafshon", 64.20, 41.57, 110),
            new("nv", "Uchquduq", 63.56, 42.16, 70),
            new("nv", "Nurota", 65.68, 40.56, 100),
            new("nv", "Konimex", 65.15, 40.28, 60),
            new("nv", "Tomdibuloq", 64.62, 41.73, 50),
            new("nv", "Qiziltepa", 64.88, 40.00, 90),
            // Buxoro
            new("bx", "Buxoro", 64.42, 39.77, 420, "buxoro"),
            new("bx", "G'ijduvon", 64.66, 40.12, 130),
            new("bx", "Qorako'l", 63.85, 39.50, 110),
            new("bx", "Jondor", 64.13, 39.73, 90),
            new("bx", "Qorovulbozor", 64.80, 39.50, 60),
            // Samarqand
            new("sm", "Samarqand", 66.96, 39.65, 460, "buxoro"),
            new("sm", "Kattaqo'rg'on", 66.26, 39.90, 150),
            new("sm", "Urgut", 67.24, 39.40, 140),
            new("sm", "Ishtixon", 66.49, 40.02, 100),
            new("sm", "Bulung'ur", 67.27, 39.76, 100),
            new("sm", "Payariq", 66.90, 40.00, 90),
            new("sm", "Nurobod", 66.27, 39.58, 70),
            // Qashqadaryo
            new("qd", "Qarshi", 65.79, 38.86, 320, "buxoro"),
            new("qd", "Shahrisabz", 66.83, 39.06, 180),
            new("qd", "G'uzor", 66.25, 38.62, 100),
            new("qd", "Koson", 65.58, 39.04, 110),
            new("qd", "Muborak", 65.15, 39.25, 80),
            new("qd", "Dehqonobod", 66.50, 38.35, 60),
            new("qd", "Qamashi", 66.46, 38.82, 80),
            new("qd", "Nishon", 65.70, 38.62, 60),
            // Surxondaryo
            new("sx", "Termiz", 67.30, 37.33, 200),
            new("sx", "Denov", 67.90, 38.27, 150),
            new("sx", "Sherobod", 67.00, 37.67, 100),
            new("sx", "Boysun", 67.20, 38.20, 80),
            new("sx", "Sho'rchi", 67.79, 38.00, 100),
            new("sx", "Qumqo'rg'on", 67.58, 37.83, 90),
            new("sx", "Jarqo'rg'on", 67.42, 37.50, 80),
            // Jizzax
            new("jz", "Jizzax", 67.84, 40.12, 180),
            new("jz", "G'allaorol", 67.59, 40.02, 100),
            new("jz", "Zomin", 68.40, 39.96, 90),
            new("jz", "Do'stlik", 68.03, 40.52, 80),
            new("jz", "Paxtakor", 67.95, 40.32, 70),
            new("jz", "Forish", 67.05, 40.45, 60),
            new("jz", "Arnasoy", 67.60, 40.80, 50),
            new("jz", "Baxmal", 68.08, 39.73, 70),
            // Sirdaryo
            new("sr", "Guliston", 68.78, 40.49, 150),
            new("sr", "Sirdaryo", 68.66, 40.84, 110),
            new("sr", "Yangiyer", 68.82, 40.27, 100),
            new("sr", "Sardoba", 68.17, 40.55, 60),
            // Toshkent
            new("tv", "Toshkent", 69.28, 41.31, 650, "qoqon"),
            new("tv", "Chirchiq", 69.58, 41.47, 170),
            new("tv", "Olmaliq", 69.60, 40.86, 150),
            new("tv", "Angren", 70.14, 41.02, 150),
            new("tv", "Bekobod", 69.05, 40.30, 120),
            new("tv", "Yangiyo'l", 69.05, 41.11, 130),
            new("tv", "Bo'stonliq", 69.95, 41.65, 80),
            new("tv", "Bo'ka", 69.20, 40.81, 90),
            new("tv", "Chinoz", 68.77, 40.94, 100),
            // Namangan
            new("nm", "Namangan", 71.67, 41.00, 420, "qoqon"),
            new("nm", "Chust", 71.24, 41.00, 140),
            new("nm", "Pop", 71.11, 40.87, 120),
            new("nm", "Kosonsoy", 71.55, 41.22, 100),
            new("nm", "Uchqo'rg'on", 72.08, 41.11, 100),
            // Farg'ona
            new("fg", "Qo'qon", 70.94, 40.53, 460, "qoqon"),
            new("fg", "Marg'ilon", 71.70, 40.48, 410, "fargona"),
            new("fg", "Quva", 72.07, 40.52, 260, "fargona"),
            new("fg", "Farg'ona", 71.80, 40.36, 260),
            new("fg", "Rishton", 71.28, 40.36, 120),
            new("fg", "Beshariq", 70.61, 40.43, 100),
            new("fg", "Oltiariq", 71.48, 40.39, 100),
            new("fg", "Quvasoy", 71.98, 40.30, 90),
            // Andijon
            new("an", "Andijon", 72.34, 40.78, 520, "andijon"),
            new("an", "Shahrixon", 72.06, 40.71, 340, "andijon"),
            new("an", "Marhamat", 72.32, 40.50, 180, "andijon"),
            new("an", "Asaka", 72.24, 40.64, 120),
            new("an", "Xo'jaobod", 72.56, 40.67, 110),
            new("an", "Qo'rg'ontepa", 72.74, 40.74, 100),
            new("an", "Baliqchi", 71.86, 40.87, 100),
            new("an", "Izboskan", 72.25, 40.92, 110),
            new("an", "Oltinko'l", 72.15, 40.81, 90),
            new("an", "Paxtaobod", 72.50, 40.93, 110),
            new("an", "Bo'z", 71.91, 40.69, 80),
        };

        /// <summary>Eski mijozlar uchun avtomatik joy: avval shu hududlar (bo'sh bo'lsa), keyin boshqa mustaqil hududlar.</summary>
        public static readonly string[] PreferredStarts = { "Asaka", "Xo'jaobod", "Qo'rg'ontepa", "Baliqchi", "Izboskan", "Oltinko'l" };

        private static readonly Dictionary<string, MapRegionSeed> ByName = Regions.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        private static readonly (float X, float Y)[] OutlineXY = Outline.Select(p => (X(p.Lon), Y(p.Lat))).ToArray();

        public static MapRegionSeed Find(string name) => name != null && ByName.TryGetValue(name, out MapRegionSeed s) ? s : null;
        public static MapProvince Province(string key) => Provinces.FirstOrDefault(p => p.Key == key);
        public static string ProvinceName(string key) => Province(key)?.Name ?? "";

        /// <summary>Xarita nuqtasi (0..100) mamlakat chegarasi ichidami.</summary>
        public static bool InsideCountry(float x, float y) => InPolygon(OutlineXY, x, y);

        public static bool InPolygon((float X, float Y)[] poly, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                (float xi, float yi) = poly[i];
                (float xj, float yj) = poly[j];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        /// <summary>Eng yaqin katalog hududining viloyati (katalogda yo'q hududlar uchun).</summary>
        public static string NearestProvince(float x, float y)
        {
            MapRegionSeed best = null; double bd = double.MaxValue;
            foreach (MapRegionSeed r in Regions)
            {
                double d = DistanceKm(x, y, X(r.Lon), Y(r.Lat));
                if (d < bd) { bd = d; best = r; }
            }
            return best?.Province ?? "an";
        }
    }
}
