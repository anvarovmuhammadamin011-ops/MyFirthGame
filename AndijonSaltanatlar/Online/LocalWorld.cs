using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Shared.Geo;
using AndijonSaltanatlar.Shared.Heraldry;
using AndijonSaltanatlar.Shared.War;

namespace AndijonSaltanatlar.Online
{
    /// <summary>
    /// SERVERSIZ DUNYO: butun O'zbekiston xaritasi (UzMap) shu qurilmada. O'yinchidan boshqa barcha hududlar AI davlatlarniki
    /// (tarixiy xonliklar va viloyat begliklari). M xaritadan istalgan AI qasriga yurish → qamal → g'alabada qasr
    /// o'yinchiga o'tadi va bayrog'i tikiladi. Holat AppPaths ichidagi faylda (har akkauntga alohida) saqlanadi.
    /// Onlayn xarita oynasi bilan bir xil ko'rinishda chiziladi (WarOverviewDto quriladi).
    /// </summary>
    public sealed class LocalWorld
    {
        public const int PlayerId = 1;
        private const string Player = "player";
        public const string HomeRegion = "Asaka";

        private sealed class Data
        {
            public Dictionary<string, string> Owner { get; set; } = new Dictionary<string, string>();
            public List<string> Capitals { get; set; } = new List<string>();
            public int Conquests { get; set; }
        }

        private readonly string _file;
        private Data _d;
        private static readonly MapKingdomSeed[] AllKingdoms = UzMap.Kingdoms.Concat(UzMap.ProvinceKingdoms).ToArray();

        public string PlayerName { get; set; } = "Saltanatingiz";
        public int[] PlayerColor { get; set; } = { 70, 130, 220 };

        private LocalWorld(string file) { _file = file; }

        /// <summary>Profil (akkaunt) bo'yicha dunyoni yuklaydi; bo'lmasa — yangisini yaratadi.</summary>
        public static LocalWorld Load(string profileKey)
        {
            string safe = new string((profileKey ?? "guest").Where(char.IsLetterOrDigit).Take(40).ToArray());
            var w = new LocalWorld(AndijonSaltanatlar.Core.AppPaths.File("localworld_" + (safe.Length == 0 ? "guest" : safe) + ".json"));
            try { if (File.Exists(w._file)) w._d = JsonSerializer.Deserialize<Data>(File.ReadAllText(w._file)); } catch { w._d = null; }
            if (w._d == null || w._d.Owner.Count == 0) w.Reset();
            // Katalogga keyin qo'shilgan hududlar ham AI davlatga beriladi
            foreach (MapRegionSeed r in UzMap.Regions)
                if (!w._d.Owner.ContainsKey(r.Name)) w._d.Owner[r.Name] = r.Owner ?? UzMap.ProvinceKingdom(r.Province) ?? "andijon";
            return w;
        }

        /// <summary>Yangi dunyo: hamma hudud AI davlatlarniki, o'yinchida — Asaka. Har AI davlatning eng katta shahri — poytaxt.</summary>
        public void Reset()
        {
            _d = new Data();
            foreach (MapRegionSeed r in UzMap.Regions)
                _d.Owner[r.Name] = r.Owner ?? UzMap.ProvinceKingdom(r.Province) ?? "andijon";
            _d.Owner[HomeRegion] = Player;
            foreach (var g in UzMap.Regions.Where(r => _d.Owner[r.Name] != Player).GroupBy(r => _d.Owner[r.Name]))
                _d.Capitals.Add(g.OrderByDescending(r => r.Population).First().Name);
            Save();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file));
                File.WriteAllText(_file, JsonSerializer.Serialize(_d));
            }
            catch { }
        }

        public int MyRegionCount => _d.Owner.Values.Count(v => v == Player);

        private static int KingdomId(string key) => key == Player ? PlayerId : 100 + Array.FindIndex(AllKingdoms, k => k.Key == key);
        private static MapKingdomSeed Seed(string key) => AllKingdoms.FirstOrDefault(k => k.Key == key);

        /// <summary>Xarita oynasi uchun: barcha qasrlar, egalari, bayroqlari, himoya bahosi va yurish vaqti.</summary>
        public WarOverviewDto BuildOverview()
        {
            var dto = new WarOverviewDto();
            MapRegionSeed home = UzMap.Regions.FirstOrDefault(r => _d.Owner[r.Name] == Player) ?? UzMap.Regions[0];
            string myFlag = FlagSpec.ParseOr("", PlayerId, PlayerColor[0], PlayerColor[1], PlayerColor[2]).Encode();
            for (int i = 0; i < UzMap.Regions.Length; i++)
            {
                MapRegionSeed r = UzMap.Regions[i];
                string owner = _d.Owner[r.Name];
                bool mine = owner == Player;
                MapKingdomSeed k = Seed(owner);
                bool capital = !mine && _d.Capitals.Contains(r.Name);
                var m = new WarMapRegionDto
                {
                    RegionId = i + 1, Name = r.Name, CityName = r.Name, OwnerKingdomId = KingdomId(owner),
                    OwnerName = mine ? PlayerName : k?.Name ?? "?", OwnerIsAi = !mine, IsMine = mine, IsCapital = mine ? r.Name == HomeRegion : capital,
                    Color = mine ? PlayerColor : new[] { k?.R ?? 150, k?.G ?? 150, k?.B ?? 150 },
                    Flag = mine ? myFlag : FlagSpec.ParseOr(k?.Flag ?? "", KingdomId(owner), k?.R ?? 150, k?.G ?? 150, k?.B ?? 150).Encode(),
                    Province = r.Province, MapX = UzMap.X(r.Lon), MapY = UzMap.Y(r.Lat), Population = r.Population,
                    RulerName = mine ? "" : k?.Ruler ?? "",
                };
                if (!mine)
                {
                    m.DefenseEstimate = 60 + r.Population / 3 + (capital ? 120 : 0);
                    m.MarchSeconds = Math.Max(10, WarRules.MarchSeconds(UzMap.X(home.Lon), UzMap.Y(home.Lat), m.MapX, m.MapY) / 2);
                    m.CanSiege = true;
                    m.OutcomeUz = (capital ? "Poytaxt! " : "") + "G'alabada " + r.Name + " qasri, shahri va aholisi saltanatingizga o'tadi.";
                }
                else m.BlockReasonUz = "Sizning qasringiz";
                dto.MapRegions.Add(m);
            }
            return dto;
        }

        /// <summary>Qamal natijasi: g'alabada qasr o'yinchiga o'tadi. Xabar qaytadi.</summary>
        public string ApplySiege(SiegeFinishRequest req)
        {
            if (req == null || req.RegionId < 1 || req.RegionId > UzMap.Regions.Length) return "";
            MapRegionSeed r = UzMap.Regions[req.RegionId - 1];
            string owner = _d.Owner[r.Name];
            if (owner == Player) return "";
            if (!req.Won) return r.Name + " qamali muvaffaqiyatsiz — qo'shin chekindi.";
            string name = Seed(owner)?.Name ?? owner;
            _d.Owner[r.Name] = Player;
            _d.Capitals.Remove(r.Name);
            _d.Conquests++;
            // AI davlatda hudud qolsa va poytaxti yo'qolgan bo'lsa — eng katta shahri yangi poytaxt
            var left = UzMap.Regions.Where(x => _d.Owner[x.Name] == owner).ToList();
            if (left.Count > 0 && !left.Any(x => _d.Capitals.Contains(x.Name)))
                _d.Capitals.Add(left.OrderByDescending(x => x.Population).First().Name);
            Save();
            return r.Name + " endi sizniki! Qasrga bayrog'ingiz tikildi." + (left.Count == 0 ? " " + name + " butunlay yengildi!" : "");
        }
    }
}
