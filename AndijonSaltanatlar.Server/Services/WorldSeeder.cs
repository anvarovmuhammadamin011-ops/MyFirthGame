using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AndijonSaltanatlar.Server.Data;
using AndijonSaltanatlar.Shared.Geo;
using AndijonSaltanatlar.Shared.Heraldry;

namespace AndijonSaltanatlar.Server.Services
{
    /// <summary>
    /// Dunyoning siyosiy xaritasi — butun O'ZBEKISTON (UzMap): 13 viloyat, ~93 hudud (tuman/shahar), tarixiy
    /// AI saltanatlar (Andijon, Farg'ona, Xiva, Buxoro, Qo'qon). Qolgan hududlar mustaqil — o'yinchi istalganini
    /// boshlang'ich yurt qilib tanlaydi. Xarita koordinatasi (0..100) RegionEntity.Latitude (X) va Longitude (Y) da.
    /// Eski dunyolar (faqat Farg'ona vodiysi) birinchi ishga tushishda yangi xaritaga ko'chiriladi: egalik, shaharlar,
    /// aholi va binolar saqlanadi — faqat koordinata/viloyat yangilanadi va yetishmayotgan hududlar qo'shiladi.
    /// </summary>
    public static class WorldSeeder
    {
        private static readonly (int R, int G, int B)[] PlayerColors =
        {
            (50, 107, 150), (60, 150, 110), (200, 140, 50), (140, 80, 160), (40, 150, 170), (170, 70, 90),
            (210, 180, 60), (90, 90, 200), (200, 100, 150), (100, 160, 60)
        };

        public static (int, int, int) PlayerColor(int index) => PlayerColors[Math.Abs(index) % PlayerColors.Length];

        /// <summary>
        /// Dunyoni to'ldirish va o'yinchi hududini berish ketma-ket bajariladi: ikki o'yinchi bir vaqtda kirsa ham
        /// AI saltanatlar ikki marta yaratilmaydi va ikkalasiga bitta hudud berilmaydi (bitta server jarayoni uchun).
        /// </summary>
        private static readonly System.Threading.SemaphoreSlim Gate = new System.Threading.SemaphoreSlim(1, 1);

        /// <summary>
        /// "Barcha shaharlar AI davlat" rejimi (appsettings: World:AiOwnsAllCities). Yoqilganda egasiz hududlar o'z viloyatining
        /// AI davlatiga beriladi va yangi o'yinchi AI davlatning poytaxt bo'lmagan qishlog'idan boshlaydi.
        /// </summary>
        public static bool AiOwnsAllCities { get; set; }

        /// <summary>Dunyodagi egasiz hududlarni viloyat AI davlatlariga beradi (kerakli AI davlatlar yaratiladi). Necha hudud berilgani qaytadi.</summary>
        public static async Task<int> FillNeutralWithAiAsync(GameDbContext db, int worldId)
        {
            var neutral = await db.Regions.Where(x => x.WorldId == worldId && x.OwnerKingdomId == 0).ToListAsync();
            if (neutral.Count == 0) return 0;
            var byKey = new Dictionary<string, Kingdom>();
            int given = 0;
            foreach (RegionEntity r in neutral)
            {
                string key = UzMap.ProvinceKingdom(r.Province);
                if (key == null) continue;
                if (!byKey.TryGetValue(key, out Kingdom k))
                {
                    MapKingdomSeed ks = UzMap.Kingdoms.Concat(UzMap.ProvinceKingdoms).First(x => x.Key == key);
                    k = await db.Kingdoms.FirstOrDefaultAsync(x => x.WorldId == worldId && x.Name == ks.Name);
                    if (k == null)
                    {
                        k = new Kingdom { WorldId = worldId, Name = ks.Name, RulerName = ks.Ruler, IsAi = true, ColorR = ks.R, ColorG = ks.G, ColorB = ks.B, AiStrategy = ks.Strategy, Flag = ks.Flag };
                        db.Kingdoms.Add(k);
                        await db.SaveChangesAsync();
                        db.KingdomResources.Add(new KingdomResource { KingdomId = k.Id, Coin = 3000, Food = 800 });
                    }
                    byKey[key] = k;
                }
                r.OwnerKingdomId = k.Id;
                given++;
            }
            await db.SaveChangesAsync();
            foreach (Kingdom k in byKey.Values)
            {
                var regionIds = await db.Regions.Where(x => x.OwnerKingdomId == k.Id).Select(x => x.Id).ToListAsync();
                var cities = await db.Cities.Where(c => regionIds.Contains(c.RegionId)).OrderByDescending(c => c.Population).ToListAsync();
                foreach (CityEntity c in cities) c.KingdomId = k.Id;
                if (cities.Count > 0 && !cities.Any(c => c.IsCapital)) cities[0].IsCapital = true;
            }
            await db.SaveChangesAsync();
            return given;
        }

        /// <summary>
        /// Yangi o'yinchi boshlay oladigan hududlar: egasiz hudud; "barcha shaharlar AI" rejimida — AI davlatning poytaxt bo'lmagan
        /// qishlog'i ham (AI davlatda boshqa hudud qolsa). Shunday qilib yangi o'yinchilarga joy tugamaydi.
        /// </summary>
        public static async Task<List<RegionEntity>> StartableRegionsAsync(GameDbContext db, int worldId, bool? includeAiVillages = null)
        {
            var regions = await db.Regions.AsNoTracking().Where(r => r.WorldId == worldId).ToListAsync();
            if (!(includeAiVillages ?? AiOwnsAllCities)) return regions.Where(r => r.OwnerKingdomId == 0).ToList();
            var ai = new HashSet<int>(await db.Kingdoms.AsNoTracking().Where(k => k.WorldId == worldId && k.IsAi).Select(k => k.Id).ToListAsync());
            var ids = regions.Select(r => r.Id).ToList();
            var capitalRegions = new HashSet<int>(await db.Cities.AsNoTracking().Where(c => c.IsCapital && ids.Contains(c.RegionId)).Select(c => c.RegionId).ToListAsync());
            var counts = regions.GroupBy(r => r.OwnerKingdomId).ToDictionary(g => g.Key, g => g.Count());
            return regions.Where(r => r.OwnerKingdomId == 0
                || (ai.Contains(r.OwnerKingdomId) && !capitalRegions.Contains(r.Id) && counts[r.OwnerKingdomId] > 1)).ToList();
        }

        public static async Task SeedAsync(GameDbContext db, World w)
        {
            if (w.MapVersion >= UzMap.Version) return;
            await Gate.WaitAsync();
            try
            {
                int current = await db.Worlds.AsNoTracking().Where(x => x.Id == w.Id).Select(x => x.MapVersion).FirstAsync();
                if (current >= UzMap.Version) { w.MapVersion = current; return; }
                await SeedOrMigrateAsync(db, w.Id);
                await db.Worlds.Where(x => x.Id == w.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.MapVersion, UzMap.Version));
                w.MapVersion = UzMap.Version;
            }
            finally { Gate.Release(); }
        }

        private static async Task SeedOrMigrateAsync(GameDbContext db, int worldId)
        {
            // 1) AI saltanatlar (nomi bo'yicha — eski dunyodagilari qayta yaratilmaydi)
            var aiByKey = new Dictionary<string, Kingdom>();
            foreach (MapKingdomSeed ks in UzMap.Kingdoms)
            {
                Kingdom k = await db.Kingdoms.FirstOrDefaultAsync(x => x.WorldId == worldId && x.Name == ks.Name);
                bool created = k == null;
                if (created)
                {
                    k = new Kingdom { WorldId = worldId, Name = ks.Name, RulerName = ks.Ruler, IsAi = true, ColorR = ks.R, ColorG = ks.G, ColorB = ks.B, AiStrategy = ks.Strategy, Flag = ks.Flag };
                    db.Kingdoms.Add(k);
                    await db.SaveChangesAsync();
                    db.KingdomResources.Add(new KingdomResource { KingdomId = k.Id, Coin = 3000, Food = 800 });
                    // AI omborlari: bozorda haqiqiy mol bilan qatnashadi (cheksiz emas).
                    foreach ((string product, int qty) in AiStock(ks.Key))
                        db.Inventory.Add(new InventoryEntity { KingdomId = k.Id, Product = product, Quantity = qty });
                }
                else if (string.IsNullOrEmpty(k.Flag)) k.Flag = ks.Flag;
                aiByKey[ks.Key] = k;
            }
            await db.SaveChangesAsync();

            // 2) Mavjud hududlar: koordinata va viloyat yangi xaritaga
            var existing = await db.Regions.Where(r => r.WorldId == worldId).ToListAsync();
            var names = new HashSet<string>(existing.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
            foreach (RegionEntity r in existing)
            {
                MapRegionSeed s = UzMap.Find(r.Name);
                if (s != null)
                {
                    r.Latitude = UzMap.X(s.Lon); r.Longitude = UzMap.Y(s.Lat); r.Province = s.Province;
                }
                else if (string.IsNullOrEmpty(r.Province))
                {
                    // Katalogda yo'q (masalan "Yangi yurt 12") — eski Farg'ona koordinatasidan o'giriladi.
                    (float x, float y) = UzMap.FromLegacyFergana(r.Latitude, r.Longitude);
                    r.Latitude = x; r.Longitude = y; r.Province = UzMap.NearestProvince(x, y);
                }
            }
            await db.SaveChangesAsync();

            // 3) Yetishmayotgan hududlar (har biri bitta shahar bilan)
            var capitalSet = new HashSet<int>(await db.Cities.Where(c => c.IsCapital && c.KingdomId != 0).Select(c => c.KingdomId).ToListAsync());
            foreach (MapRegionSeed s in UzMap.Regions)
            {
                if (names.Contains(s.Name)) continue;
                int owner = s.Owner != null && aiByKey.TryGetValue(s.Owner, out Kingdom ok) ? ok.Id : 0;
                var region = new RegionEntity
                {
                    WorldId = worldId, Name = s.Name, Province = s.Province, OwnerKingdomId = owner,
                    Latitude = UzMap.X(s.Lon), Longitude = UzMap.Y(s.Lat),
                    Development = Math.Clamp(15 + s.Population / 12, 15, 60), Stability = 60
                };
                db.Regions.Add(region);
                await db.SaveChangesAsync();
                bool capital = owner != 0 && capitalSet.Add(owner);
                db.Cities.Add(NewCity(owner, region.Id, s.Name, s.Population, s.Population + 40 + s.Population / 5, Math.Max(20, s.Population * 7 / 10), capital));
                names.Add(s.Name);
            }
            await db.SaveChangesAsync();
            if (AiOwnsAllCities) await FillNeutralWithAiAsync(db, worldId);
        }

        private static IEnumerable<(string, int)> AiStock(string key) => key switch
        {
            "andijon" => new[] { ("Wheat", 600), ("Cotton", 300) },
            "fargona" => new[] { ("Cotton", 500), ("Fruit", 400) },
            "xiva" => new[] { ("Wheat", 400), ("Vegetable", 300) },
            "buxoro" => new[] { ("Cotton", 400), ("Fruit", 300) },
            "qoqon" => new[] { ("Wheat", 500), ("Fruit", 300) },
            _ => Array.Empty<(string, int)>(),
        };

        private static CityEntity NewCity(int kingdomId, int regionId, string name, int population, int housing, int jobs, bool capital) => new CityEntity
        {
            KingdomId = kingdomId, RegionId = regionId, Name = name, Population = population,
            Households = (int)Math.Ceiling(population / 4.5), HousingCapacity = housing, Jobs = jobs, IsCapital = capital,
            Level = 1 + population / 200, Happiness = 65, Trust = 60,
            GovernorName = "Hokim " + name.Split(' ')[0]
        };

        // ------------------------------------------------------------------ O'yinchining boshlang'ich yurti
        /// <summary>
        /// Eski mijozlar uchun: saltanatning shahri bo'lmasa — avtomatik mustaqil hudud beriladi
        /// (avval PreferredStarts, keyin Andijon viloyati, keyin boshqa mustaqil hududlar).
        /// </summary>
        public static async Task EnsurePlayerHomeAsync(GameDbContext db, Kingdom k)
        {
            await Gate.WaitAsync();
            try
            {
                if (await db.Cities.AnyAsync(c => c.KingdomId == k.Id)) return;
                var free = (await StartableRegionsAsync(db, k.WorldId)).OrderBy(r => r.OwnerKingdomId == 0 ? 0 : 1).ToList();
                RegionEntity pick = UzMap.PreferredStarts.Select(n => free.FirstOrDefault(r => r.Name == n)).FirstOrDefault(r => r != null)
                    ?? free.Where(r => r.Province == "an").OrderBy(r => r.Id).FirstOrDefault()
                    ?? free.OrderBy(r => r.Id).FirstOrDefault();
                if (pick != null) await TakeRegionAsync(db, k, pick);
                else await FoundNewRegionAsync(db, k);
            }
            finally { Gate.Release(); }
        }

        /// <summary>
        /// O'yinchi xaritadan tanlagan boshlang'ich hudud. Faqat mustaqil (hech kimga tegishli bo'lmagan) hudud
        /// va faqat saltanatning hali shahri bo'lmasa. Natija: null — muvaffaqiyat, aks holda sabab (o'zbekcha).
        /// </summary>
        public static async Task<string> ChooseStartAsync(GameDbContext db, Kingdom k, int regionId, bool? includeAiVillages = null)
        {
            await Gate.WaitAsync();
            try
            {
                if (await db.Cities.AnyAsync(c => c.KingdomId == k.Id)) return "Saltanatingizning yurti allaqachon bor.";
                RegionEntity r = await db.Regions.FirstOrDefaultAsync(x => x.Id == regionId && x.WorldId == k.WorldId);
                if (r == null) return "Hudud topilmadi.";
                if (r.OwnerKingdomId != 0 && !(await StartableRegionsAsync(db, k.WorldId, includeAiVillages)).Any(x => x.Id == r.Id))
                    return r.Name + " band — boshqa mustaqil hududni tanlang.";
                await TakeRegionAsync(db, k, r);
                return null;
            }
            finally { Gate.Release(); }
        }

        private static async Task TakeRegionAsync(GameDbContext db, Kingdom k, RegionEntity r)
        {
            // Shartli yangilash: boshqa so'rov shu hududni oldinroq olgan bo'lsa — 0 qator.
            int expected = r.OwnerKingdomId; // egasiz (0) yoki joy beradigan AI davlat
            int changed = await db.Regions.Where(x => x.Id == r.Id && x.OwnerKingdomId == expected)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerKingdomId, k.Id).SetProperty(x => x.Stability, 70));
            if (changed == 0) throw new InvalidOperationException("Hudud band.");
            db.Entry(r).State = EntityState.Detached;
            var cities = await db.Cities.Where(c => c.RegionId == r.Id).OrderByDescending(c => c.Population).ToListAsync();
            if (cities.Count == 0)
                db.Cities.Add(NewCity(k.Id, r.Id, r.Name, 42, 60, 30, true));
            else
                for (int i = 0; i < cities.Count; i++)
                {
                    cities[i].KingdomId = k.Id;
                    cities[i].IsCapital = i == 0;
                    cities[i].GovernorName = "Hokim " + cities[i].Name.Split(' ')[0];
                }
            await db.SaveChangesAsync();
        }

        /// <summary>Bo'sh hudud qolmagan bo'lsa — mamlakat ichida yangi yurt (eng yaqin hududlardan uzoqroq joy).</summary>
        private static async Task FoundNewRegionAsync(GameDbContext db, Kingdom k)
        {
            var taken = await db.Regions.Where(r => r.WorldId == k.WorldId).Select(r => new { r.Latitude, r.Longitude }).ToListAsync();
            var rnd = new Random(k.Id * 7919);
            float bx = 50, by = 50; double best = -1;
            for (int i = 0; i < 400; i++)
            {
                float x = (float)(rnd.NextDouble() * 100), y = (float)(rnd.NextDouble() * 100);
                if (!UzMap.InsideCountry(x, y)) continue;
                double d = taken.Count == 0 ? 1000 : taken.Min(t => UzMap.DistanceKm(x, y, t.Latitude, t.Longitude));
                if (d > best) { best = d; bx = x; by = y; }
            }
            string name = "Yangi yurt " + k.Id;
            var region = new RegionEntity { WorldId = k.WorldId, Name = name, Province = UzMap.NearestProvince(bx, by), OwnerKingdomId = k.Id, Latitude = bx, Longitude = by, Development = 30, Stability = 70 };
            db.Regions.Add(region);
            await db.SaveChangesAsync();
            db.Cities.Add(NewCity(k.Id, region.Id, name, 42, 60, 30, true));
            await db.SaveChangesAsync();
        }

        public static async Task<CityEntity> CapitalOfAsync(GameDbContext db, int kingdomId)
            => await db.Cities.AsNoTracking().Where(c => c.KingdomId == kingdomId).OrderByDescending(c => c.IsCapital).ThenBy(c => c.Id).FirstOrDefaultAsync();

        /// <summary>Ikki shahar orasidagi masofa (km, O'zbekiston xaritasida).</summary>
        public static async Task<double> DistanceAsync(GameDbContext db, CityEntity a, CityEntity b)
        {
            if (a == null || b == null) return 0;
            var ra = await db.Regions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == a.RegionId);
            var rb = await db.Regions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == b.RegionId);
            if (ra == null || rb == null) return 0;
            return UzMap.DistanceKm(ra.Latitude, ra.Longitude, rb.Latitude, rb.Longitude);
        }

        /// <summary>Saltanat bayrog'i (bo'sh/yaroqsiz bo'lsa — rangi asosidagi standart).</summary>
        public static string FlagOf(Kingdom k) => k == null ? "" : FlagSpec.ParseOr(k.Flag, k.Id, k.ColorR, k.ColorG, k.ColorB).Encode();
    }
}
