using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Culture;
using AndijonSaltanatlar.Technology;

namespace AndijonSaltanatlar.Civilization
{
    /// <summary>JSON'ga saqlanadigan sivilizatsiya holati.</summary>
    public sealed class CivSnapshot
    {
        public int Science { get; set; }
        public int Culture { get; set; }
        public int Progress { get; set; }
        public string Current { get; set; } = "";
        public List<string> Researched { get; set; } = new List<string>();
        public List<int[]> AI { get; set; } = new List<int[]>(); // [kingdomId, science, techCount]
    }

    /// <summary>
    /// Sivilizatsiya rivojlanishini boshqaradi: o'yinchining ilm/madaniyat ishlab chiqarishi va
    /// tadqiqoti, hamda AI saltanatlarning mustaqil (mavhum) texnologik rivojlanishi.
    /// Bonuslar ochilgan texnologiyalardan har safar qayta hisoblanadi — ikki marta qo'shilmaydi.
    /// </summary>
    public sealed class CivilizationManager
    {
        public CivilizationState Player { get; } = new CivilizationState();
        private readonly Dictionary<int, int[]> _ai = new Dictionary<int, int[]>(); // id -> [science, techCount]

        public int LastSciencePerDay { get; private set; }
        public int LastCulturePerDay { get; private set; }

        public void Initialize(int playerKingdomId, IEnumerable<int> aiKingdomIds)
        {
            Player.KingdomId = playerKingdomId;
            Player.Science = 0; Player.Culture = 0;
            Player.Researched.Clear(); Player.CurrentResearch = ""; Player.CurrentProgress = 0;
            _ai.Clear();
            foreach (int id in aiKingdomIds) _ai[id] = new[] { 0, 0 };
        }

        public CivilizationModifiers PlayerModifiers() => Player.Modifiers();
        public int AITechCount(int kingdomId) => _ai.TryGetValue(kingdomId, out int[] a) ? a[1] : 0;

        /// <summary>O'yinchi shahrining bir kunlik ilm va madaniyat ishlab chiqarishi + tadqiqot progressi.</summary>
        public string DailyTick(BuildingManager buildings, float eduAvg, int scholars = 0)
        {
            CivilizationModifiers mod = Player.Modifiers();
            float resBonus = mod.Get(TechEffectType.ResearchProductionBonus);
            float cultBonus = mod.Get(TechEffectType.CulturalInfluenceBonus);

            // Olimlar (madrasadagi falakiyotchi va riyoziyotchilar) har biri kuniga +2 ilm beradi
            int science = (int)((1 + buildings.Count(BuildingType.Madrasa) * 5 + (int)(eduAvg / 20f) + scholars * 2) * (1f + resBonus));
            LastSciencePerDay = science;

            LastCulturePerDay = CultureManager.DailyCulture(buildings, cultBonus);
            Player.Culture += LastCulturePerDay;

            if (Player.IsResearching)
                return ResearchManager.Advance(Player, science);
            Player.Science += science;
            return null;
        }

        /// <summary>AI saltanatning mavhum texnologik rivojlanishi. Yangi daraja ochilsa true.</summary>
        public bool AITick(int kingdomId, int economicStrength)
        {
            if (!_ai.TryGetValue(kingdomId, out int[] a)) return false;
            a[0] += 5 + economicStrength / 10;
            int cost = 100 + a[1] * 60;
            if (a[0] >= cost) { a[0] -= cost; a[1]++; return true; }
            return false;
        }

        public CivSnapshot Snapshot()
        {
            var s = new CivSnapshot
            {
                Science = Player.Science, Culture = Player.Culture,
                Current = Player.CurrentResearch, Progress = Player.CurrentProgress
            };
            s.Researched.AddRange(Player.Researched);
            foreach (KeyValuePair<int, int[]> kv in _ai) s.AI.Add(new[] { kv.Key, kv.Value[0], kv.Value[1] });
            return s;
        }

        public void Restore(CivSnapshot s)
        {
            if (s == null) return;
            Player.Science = s.Science; Player.Culture = s.Culture;
            Player.CurrentResearch = s.Current ?? ""; Player.CurrentProgress = s.Progress;
            Player.Researched.Clear();
            if (s.Researched != null) foreach (string id in s.Researched) Player.Researched.Add(id);
            if (s.AI != null) foreach (int[] a in s.AI) if (a.Length >= 3) _ai[a[0]] = new[] { a[1], a[2] };
        }
    }
}
