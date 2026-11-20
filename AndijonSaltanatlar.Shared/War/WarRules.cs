using System;
using System.Collections.Generic;

namespace AndijonSaltanatlar.Shared.War
{
    // =====================================================================================
    // PROMPT 25 — Strategik urush tizimi qoidalari (server va client uchun umumiy, SOF funksiyalar).
    // Jang — o'yin uchun mavhum strategik mexanika: faqat shartli statistikalar (kuch, chidamlilik, ruh,
    // tayyorgarlik) va muvozanatlangan tasodif. Real harbiy taktika yoki qurol ishlatish modellashtirilmaydi.
    // Natijani FAQAT server hisoblaydi; client bu qoidalarni faqat ko'rsatish (narx, oldindan ko'rish) uchun ishlatadi.
    // =====================================================================================

    /// <summary>Birlik yo'nalishi (har yo'nalishda 1..3 daraja).</summary>
    public enum UnitLine { Piyoda = 0, Otliq = 1, Masofaviy = 2, Aravasi = 3, Lager = 4 }

    public enum FacilityKind { Qarorgoh = 0, Mashgulot = 1, Otxona = 2, SaroyQoriqchilari = 3, Ombor = 4, Mudofaa = 5 }

    public enum ArmyStatus { Available = 0, Preparing = 1, Traveling = 2, InBattle = 3, Recovering = 4 }

    public enum BattleStatus { Preparing = 0, WaitingForPlayers = 1, InProgress = 2, Resolving = 3, Completed = 4, Cancelled = 5 }

    /// <summary>Raund uchun yuqori darajadagi qaror (faqat statistik modifikator).</summary>
    public enum BattleDecision { Muvozanatli = 0, Ehtiyotkor = 1, Faol = 2, QaytaTashkil = 3, Muzokara = 4 }

    public enum InvitationStatus { Pending = 0, Accepted = 1, Declined = 2, Expired = 3, Cancelled = 4 }

    public sealed class UnitDefinition
    {
        public UnitLine Line;
        public int Level;
        public string NameUz = "";
        /// <summary>Mavhum kuch (bitta birlik).</summary>
        public int Power;
        /// <summary>Chidamlilik — yo'qotishlarni kamaytiradi.</summary>
        public int Resilience;
        /// <summary>Yollash narxi (L1) yoki shu darajaga ko'tarish narxi (L2/L3), bitta birlik uchun.</summary>
        public int Cost;
        /// <summary>Bir o'yin kunidagi saqlash xarajati (bitta birlik).</summary>
        public int Upkeep;
        public FacilityKind? RequiredFacility;
        public int RequiredFacilityLevel;
        public bool IsSupport => Line == UnitLine.Aravasi || Line == UnitLine.Lager;
    }

    public sealed class FacilityDefinition
    {
        public FacilityKind Kind;
        public string NameUz = "";
        public string EffectUz = "";
        public int MaxLevel;
        public int BaseCost;
        public int UpkeepPerLevel;
        public int CostFor(int nextLevel) => BaseCost * nextLevel;
    }

    /// <summary>Bitta raund natijasi (server hisoblaydi, client faqat ko'rsatadi).</summary>
    public sealed class RoundOutcome
    {
        public int Round;
        public BattleDecision AttackerDecision, DefenderDecision;
        public int AttackerLoss, DefenderLoss;
        public int AttackerPowerAfter, DefenderPowerAfter;
        public int AttackerMoraleAfter, DefenderMoraleAfter;
        public bool Finished;
        /// <summary>1 — hujumchi g'olib, 2 — himoyachi g'olib, 0 — tinchlik/durang.</summary>
        public int Winner;
        public string SummaryUz = "";
    }

    public static class WarRules
    {
        public const int MaxRounds = 5;
        public const int BaseCapacity = 30;
        public const int CapacityPerQarorgoh = 40;
        public const int MaxArmies = 3;
        public const int ConflictCost = 120;          // mojaro e'lon qilish (ta'minot, tayyorgarlik)
        public const int ConflictRelationPenalty = 25;
        public const int CommanderHireCost = 400;
        public const int MaxCommanders = 3;
        public const int DecisionTimeoutSeconds = 120; // PvP: raqib javob bermasa — avtomatik "Muvozanatli"
        public const int InvitationMinutes = 30;
        public const int MaxPendingInvitations = 3;
        public const int NewPlayerProtectionHours = 24;
        public const int TruceHours = 12;
        public const int MinBattleArmy = 5;           // jangga kirish uchun kamida 5 jangovar birlik

        // --- Qasrni kuchaytirish (egallangan hudud: devor, darvoza, minoralar) — serversiz va onlayn bir xil ---
        /// <summary>Qasr himoyasining eng yuqori darajasi.</summary>
        public const int MaxFortify = 5;
        /// <summary>Joriy darajadan keyingisiga o'tish narxi (tanga): 150, 270, 390, 510, 630.</summary>
        public static int FortifyCost(int currentLevel) => 150 + 120 * Math.Clamp(currentLevel, 0, MaxFortify - 1);
        /// <summary>Jangda himoyachiga qo'shimcha kuch (%): har daraja +12%.</summary>
        public static int FortifyDefensePct(int level) => 12 * Math.Clamp(level, 0, MaxFortify);

        // --- AI davlatlar hujumi (onlayn: faqat o'yindagi, himoyasiz o'yinchilarga) ---
        /// <summary>O'yinchi birinchi marta kuzatilgandan keyin birinchi AI hujumigacha (soniya).</summary>
        public const int AiAttackFirstDelaySeconds = 480;
        /// <summary>Keyingi hujumgacha: egallangan qasr bo'lsa 6–9 daqiqa, bo'lmasa 10–14 daqiqa.</summary>
        public static int AiAttackGapSeconds(bool hasConquests, int roll0to99) =>
            hasConquests ? 360 + roll0to99 * 180 / 100 : 600 + roll0to99 * 240 / 100;
        /// <summary>AI yurishi kamida shuncha soniya davom etadi — o'yinchi ogohlantirishni ko'rib, kuchaytirishga ulgursin.</summary>
        public const int AiMarchMinSeconds = 90;
        public const int MarchCost = 80;              // yurish ta'minoti (tanga)
        public const int PlunderPercent = 20;         // poytaxt qamalidagi o'lja: xazinaning 20%
        public const int PlunderMax = 2000;

        /// <summary>Qo'shin tezligi: har 100 km uchun soniya (o'yin tezlashtirilgan).</summary>
        public const double MarchSecondsPer100Km = 16;
        public const int MaxMarchSeconds = 240;

        /// <summary>
        /// Yurish vaqti (real soniya): O'zbekiston xaritasidagi haqiqiy masofaga (km) bog'liq. Qo'shni tumanlar — ~20 s,
        /// Andijon → Toshkent — ~1 daqiqa, Andijon → Nukus — ~3 daqiqa (eng ko'pi 4 daqiqa).
        /// </summary>
        public static int MarchSeconds(double fromX, double fromY, double toX, double toY)
        {
            double km = Geo.UzMap.DistanceKm(fromX, fromY, toX, toY);
            return (int)Math.Clamp(18 + km * MarchSecondsPer100Km / 100.0, 20, MaxMarchSeconds);
        }

        /// <summary>Qasr garnizoni (militsiya) hajmi: shahar aholisiga bog'liq, 8..30.</summary>
        public static int GarrisonSize(int population) => Math.Clamp(population / 25, 8, 30);

        public static readonly List<UnitDefinition> Units = new List<UnitDefinition>
        {
            new UnitDefinition { Line = UnitLine.Piyoda, Level = 1, NameUz = "Oddiy piyoda", Power = 10, Resilience = 12, Cost = 40, Upkeep = 1 },
            new UnitDefinition { Line = UnitLine.Piyoda, Level = 2, NameUz = "Tajribali piyoda", Power = 16, Resilience = 18, Cost = 35, Upkeep = 2, RequiredFacility = FacilityKind.Mashgulot, RequiredFacilityLevel = 1 },
            new UnitDefinition { Line = UnitLine.Piyoda, Level = 3, NameUz = "Saroy qo'riqchisi", Power = 24, Resilience = 26, Cost = 70, Upkeep = 3, RequiredFacility = FacilityKind.SaroyQoriqchilari, RequiredFacilityLevel = 1 },
            new UnitDefinition { Line = UnitLine.Otliq, Level = 1, NameUz = "Oddiy otliq", Power = 18, Resilience = 14, Cost = 90, Upkeep = 3, RequiredFacility = FacilityKind.Otxona, RequiredFacilityLevel = 1 },
            new UnitDefinition { Line = UnitLine.Otliq, Level = 2, NameUz = "Tajribali otliq", Power = 27, Resilience = 20, Cost = 60, Upkeep = 4, RequiredFacility = FacilityKind.Mashgulot, RequiredFacilityLevel = 1 },
            new UnitDefinition { Line = UnitLine.Otliq, Level = 3, NameUz = "Elita otliq", Power = 38, Resilience = 28, Cost = 110, Upkeep = 5, RequiredFacility = FacilityKind.Otxona, RequiredFacilityLevel = 2 },
            new UnitDefinition { Line = UnitLine.Masofaviy, Level = 1, NameUz = "Oddiy masofaviy birlik", Power = 14, Resilience = 8, Cost = 60, Upkeep = 2 },
            new UnitDefinition { Line = UnitLine.Masofaviy, Level = 2, NameUz = "Tajribali masofaviy birlik", Power = 22, Resilience = 12, Cost = 55, Upkeep = 3, RequiredFacility = FacilityKind.Mashgulot, RequiredFacilityLevel = 2 },
            new UnitDefinition { Line = UnitLine.Aravasi, Level = 1, NameUz = "Ta'minot aravasi", Power = 0, Resilience = 10, Cost = 70, Upkeep = 2, RequiredFacility = FacilityKind.Ombor, RequiredFacilityLevel = 1 },
            new UnitDefinition { Line = UnitLine.Lager, Level = 1, NameUz = "Harbiy lager xodimlari", Power = 2, Resilience = 6, Cost = 50, Upkeep = 1, RequiredFacility = FacilityKind.Ombor, RequiredFacilityLevel = 1 },
        };

        public static readonly List<FacilityDefinition> Facilities = new List<FacilityDefinition>
        {
            new FacilityDefinition { Kind = FacilityKind.Qarorgoh, NameUz = "Harbiy qarorgoh", EffectUz = "Qo'shin sig'imi +40, qo'shimcha qo'shin", MaxLevel = 3, BaseCost = 300, UpkeepPerLevel = 6 },
            new FacilityDefinition { Kind = FacilityKind.Mashgulot, NameUz = "Mashg'ulot maydoni", EffectUz = "Tajribali birliklar (2-daraja), tayyorgarlik", MaxLevel = 2, BaseCost = 250, UpkeepPerLevel = 5 },
            new FacilityDefinition { Kind = FacilityKind.Otxona, NameUz = "Otlar uchun inshoot", EffectUz = "Otliq birliklar; 2-darajada elita otliq", MaxLevel = 2, BaseCost = 350, UpkeepPerLevel = 6 },
            new FacilityDefinition { Kind = FacilityKind.SaroyQoriqchilari, NameUz = "Saroy qo'riqchilari qarorgohi", EffectUz = "Saroy qo'riqchisi (3-daraja piyoda)", MaxLevel = 1, BaseCost = 600, UpkeepPerLevel = 10 },
            new FacilityDefinition { Kind = FacilityKind.Ombor, NameUz = "Harbiy ombor", EffectUz = "Ta'minot aravasi va lager xodimlari; ruh tiklanishi", MaxLevel = 2, BaseCost = 250, UpkeepPerLevel = 4 },
            new FacilityDefinition { Kind = FacilityKind.Mudofaa, NameUz = "Shahar mudofaa inshooti", EffectUz = "Himoyada kuch +8% har darajada", MaxLevel = 3, BaseCost = 300, UpkeepPerLevel = 5 },
        };

        public static readonly string[] CommanderNames =
        {
            "Sarkarda Qodirbek", "Sarkarda Jahongir", "Sarkarda Ulug'bek", "Sarkarda Nodirbek", "Sarkarda Sherzod",
            "Sarkarda Bahodir", "Sarkarda Dilshod", "Sarkarda Anvarbek", "Sarkarda Rustam", "Sarkarda Farhod"
        };

        public static UnitDefinition Unit(UnitLine line, int level)
        {
            foreach (UnitDefinition u in Units) if (u.Line == line && u.Level == level) return u;
            return null;
        }

        public static int MaxLevel(UnitLine line)
        {
            int m = 1;
            foreach (UnitDefinition u in Units) if (u.Line == line && u.Level > m) m = u.Level;
            return m;
        }

        public static FacilityDefinition Facility(FacilityKind k)
        {
            foreach (FacilityDefinition f in Facilities) if (f.Kind == k) return f;
            return null;
        }

        public static int Capacity(int qarorgohLevel) => BaseCapacity + CapacityPerQarorgoh * Math.Max(0, qarorgohLevel);
        public static int MaxArmyCount(int qarorgohLevel) => Math.Min(MaxArmies, 1 + Math.Max(0, qarorgohLevel));

        /// <summary>Talab qilinadigan inshoot bajarilganmi.</summary>
        public static bool Unlocked(UnitDefinition u, Func<FacilityKind, int> levelOf)
            => u.RequiredFacility == null || levelOf(u.RequiredFacility.Value) >= u.RequiredFacilityLevel;

        public static string RequirementText(UnitDefinition u)
            => u.RequiredFacility == null ? "" : Facility(u.RequiredFacility.Value).NameUz + " " + u.RequiredFacilityLevel + "-daraja";

        public static string CommanderRank(int level) => level >= 3 ? "Mashhur sarkarda" : level == 2 ? "Tajribali sarkarda" : "Yangi sarkarda";
        public static int CommanderXpForNext(int level) => level >= 3 ? int.MaxValue : 100 * level;
        /// <summary>Sarkarda bonusi: qo'shin kuchiga +5% / +10% / +15% (cheklangan, cheksiz o'smaydi).</summary>
        public static int CommanderPowerPercent(int level) => 5 * Math.Clamp(level, 0, 3);
        public static int CommanderMoraleBonus(int level) => 4 * Math.Clamp(level, 0, 3);

        public static string DecisionName(BattleDecision d)
        {
            switch (d)
            {
                case BattleDecision.Ehtiyotkor: return "Ehtiyotkor yondashuv";
                case BattleDecision.Faol: return "Faol yondashuv";
                case BattleDecision.QaytaTashkil: return "Qayta tashkil etish";
                case BattleDecision.Muzokara: return "Muzokara taklifi";
                default: return "Muvozanatli yondashuv";
            }
        }

        public static string DecisionHint(BattleDecision d)
        {
            switch (d)
            {
                case BattleDecision.Ehtiyotkor: return "Yo'qotish kam, ta'sir ham kam";
                case BattleDecision.Faol: return "Ta'sir kuchli, yo'qotish ham ko'p";
                case BattleDecision.QaytaTashkil: return "Ruhni tiklaydi, ta'sir past";
                case BattleDecision.Muzokara: return "Ikkala tomon rozi bo'lsa jang tinch tugaydi";
                default: return "O'rtacha ta'sir va yo'qotish";
            }
        }

        public static string StatusName(ArmyStatus s)
        {
            switch (s)
            {
                case ArmyStatus.Preparing: return "Tayyorlanmoqda";
                case ArmyStatus.Traveling: return "Yo'lda";
                case ArmyStatus.InBattle: return "Jangda";
                case ArmyStatus.Recovering: return "Tiklanmoqda";
                default: return "Tayyor";
            }
        }

        // ------------------------------------------------------------------ Qo'shin kuchi
        /// <summary>
        /// Qo'shin samarali kuchi: birliklar kuchi × sarkarda bonusi × tayyorgarlik (60%..100%).
        /// Ta'minot aravalari tayyorgarlikni, lager xodimlari chidamlilikni oshiradi (cheklangan).
        /// </summary>
        public static int ArmyPower(IEnumerable<(UnitLine line, int level, int count)> groups, int commanderLevel, int readiness)
        {
            long raw = 0;
            foreach (var g in groups)
            {
                UnitDefinition u = Unit(g.line, g.level);
                if (u != null && g.count > 0) raw += (long)u.Power * g.count;
            }
            double cmd = 1 + CommanderPowerPercent(commanderLevel) / 100.0;
            double ready = 0.6 + 0.4 * Math.Clamp(readiness, 0, 100) / 100.0;
            return (int)Math.Min(int.MaxValue / 4, raw * cmd * ready);
        }

        public static int CombatUnitCount(IEnumerable<(UnitLine line, int level, int count)> groups)
        {
            int n = 0;
            foreach (var g in groups) if (g.line != UnitLine.Aravasi && g.line != UnitLine.Lager) n += g.count;
            return n;
        }

        public static int Upkeep(IEnumerable<(UnitLine line, int level, int count)> groups)
        {
            int n = 0;
            foreach (var g in groups) { UnitDefinition u = Unit(g.line, g.level); if (u != null) n += u.Upkeep * g.count; }
            return n;
        }

        /// <summary>Yollash vaqti (o'yin daqiqalari): 20 + 2 × son, ko'pi bilan 90 (≈1.5 real daqiqa).</summary>
        public static int RecruitMinutes(int count) => Math.Min(90, 20 + 2 * Math.Max(1, count));

        // ------------------------------------------------------------------ Raund hisoblash
        private static (double dealt, double taken, int morale) Modifiers(BattleDecision d)
        {
            switch (d)
            {
                case BattleDecision.Ehtiyotkor: return (0.75, 0.6, 0);
                case BattleDecision.Faol: return (1.35, 1.3, -2);
                case BattleDecision.QaytaTashkil: return (0.4, 0.8, 14);
                case BattleDecision.Muzokara: return (0.3, 0.9, -3);
                default: return (1.0, 1.0, 0);
            }
        }

        /// <summary>
        /// Bitta raundni hisoblaydi. Deterministik: bir xil (seed, raund, qarorlar, holat) → bir xil natija.
        /// Jang tugaydi: tomonning kuchi boshlang'ichning 25% idan pastga tushsa yoki ruhi 15 dan past bo'lsa;
        /// ikkala tomon muzokara tanlasa — tinchlik; 5-raunddan keyin — qolgan kuch ulushi bo'yicha.
        /// </summary>
        public static RoundOutcome ResolveRound(int seed, int round,
            int aPower, int aStartPower, int aMorale, BattleDecision aDec, int aResiliencePct,
            int dPower, int dStartPower, int dMorale, BattleDecision dDec, int dResiliencePct, int defenseBonusPct)
        {
            var rng = new Random(unchecked(seed * 31 + round * 7919));
            var o = new RoundOutcome { Round = round, AttackerDecision = aDec, DefenderDecision = dDec };

            if (aDec == BattleDecision.Muzokara && dDec == BattleDecision.Muzokara)
            {
                o.AttackerPowerAfter = aPower; o.DefenderPowerAfter = dPower;
                o.AttackerMoraleAfter = aMorale; o.DefenderMoraleAfter = dMorale;
                o.Finished = true; o.Winner = 0;
                o.SummaryUz = "Ikkala tomon muzokaraga rozi bo'ldi — jang tinch yakunlandi.";
                return o;
            }

            var am = Modifiers(aDec);
            var dm = Modifiers(dDec);
            double aEff = aPower * (0.55 + 0.45 * aMorale / 100.0);
            double dEff = dPower * (0.55 + 0.45 * dMorale / 100.0) * (1 + defenseBonusPct / 100.0);

            double toDef = aEff * 0.16 * am.dealt * dm.taken * (0.85 + rng.NextDouble() * 0.3) * (1 - Math.Clamp(dResiliencePct, 0, 40) / 100.0);
            double toAtk = dEff * 0.16 * dm.dealt * am.taken * (0.85 + rng.NextDouble() * 0.3) * (1 - Math.Clamp(aResiliencePct, 0, 40) / 100.0);

            o.DefenderLoss = (int)Math.Min(dPower, Math.Round(toDef));
            o.AttackerLoss = (int)Math.Min(aPower, Math.Round(toAtk));
            o.AttackerPowerAfter = aPower - o.AttackerLoss;
            o.DefenderPowerAfter = dPower - o.DefenderLoss;

            int aMoraleLoss = aPower > 0 ? (int)Math.Round(60.0 * o.AttackerLoss / Math.Max(1, aStartPower)) : 30;
            int dMoraleLoss = dPower > 0 ? (int)Math.Round(60.0 * o.DefenderLoss / Math.Max(1, dStartPower)) : 30;
            o.AttackerMoraleAfter = Math.Clamp(aMorale - aMoraleLoss + am.morale + (o.DefenderLoss > o.AttackerLoss ? 3 : 0), 0, 100);
            o.DefenderMoraleAfter = Math.Clamp(dMorale - dMoraleLoss + dm.morale + (o.AttackerLoss > o.DefenderLoss ? 3 : 0), 0, 100);

            bool aBroken = o.AttackerPowerAfter < aStartPower / 4 || o.AttackerMoraleAfter < 15;
            bool dBroken = o.DefenderPowerAfter < dStartPower / 4 || o.DefenderMoraleAfter < 15;
            if (aBroken || dBroken || round >= MaxRounds)
            {
                o.Finished = true;
                if (aBroken && !dBroken) o.Winner = 2;
                else if (dBroken && !aBroken) o.Winner = 1;
                else
                {
                    double aShare = o.AttackerPowerAfter / (double)Math.Max(1, aStartPower) + o.AttackerMoraleAfter / 200.0;
                    double dShare = o.DefenderPowerAfter / (double)Math.Max(1, dStartPower) + o.DefenderMoraleAfter / 200.0;
                    o.Winner = aShare > dShare + 0.05 ? 1 : dShare > aShare + 0.05 ? 2 : 2; // teng holatda himoyachi ustun
                }
            }
            o.SummaryUz = round + "-raund: hujumchi " + o.AttackerLoss + " kuch yo'qotdi, himoyachi " + o.DefenderLoss + ".";
            return o;
        }

        // ------------------------------------------------------------------ AI qarori
        /// <summary>
        /// AI saltanat qarori: xarakteri (AiStrategy) va jang holatiga (kuch nisbati, ruh) bog'liq.
        /// Tasodif deterministik (seed + raund) — server qayta ishga tushsa ham bir xil qaror.
        /// </summary>
        public static BattleDecision AiDecision(string personality, int seed, int round, int myPower, int myStart, int myMorale, int enemyPower)
        {
            var rng = new Random(unchecked(seed * 17 + round * 101 + 5));
            double ratio = myPower / (double)Math.Max(1, enemyPower);
            if (myMorale < 30) return BattleDecision.QaytaTashkil;
            if (ratio < 0.45 && myPower < myStart / 2) return BattleDecision.Muzokara;
            string p = (personality ?? "").ToLowerInvariant();
            if (p.Contains("aggress") || p.Contains("war")) return ratio > 0.8 ? BattleDecision.Faol : BattleDecision.Muvozanatli;
            if (p.Contains("trade") || p.Contains("defens")) return ratio < 1.0 ? BattleDecision.Ehtiyotkor : BattleDecision.Muvozanatli;
            if (ratio > 1.4) return BattleDecision.Faol;
            if (ratio < 0.8) return rng.NextDouble() < 0.5 ? BattleDecision.Ehtiyotkor : BattleDecision.Muvozanatli;
            return rng.NextDouble() < 0.25 ? BattleDecision.Faol : BattleDecision.Muvozanatli;
        }

        /// <summary>AI tinchlik taklifini qabul qiladimi (o'z xarakteri va holatiga qarab).</summary>
        public static bool AiAcceptsPeace(string personality, int relation, int myPower, int enemyPower)
        {
            string p = (personality ?? "").ToLowerInvariant();
            int need = p.Contains("aggress") ? 10 : p.Contains("trade") ? -40 : -20;
            if (myPower < enemyPower * 0.7) need -= 30;   // kuchsiz tomon tinchlikka moyilroq
            return relation >= need;
        }

        /// <summary>Yo'qotishlar birlik guruhlariga taqsimlanadi: kuch yo'qotishi ulushida har guruhdan birliklar kamayadi.</summary>
        public static int UnitsLost(int count, int powerLost, int powerBefore)
        {
            if (count <= 0 || powerBefore <= 0 || powerLost <= 0) return 0;
            double share = Math.Clamp(powerLost / (double)powerBefore, 0, 1);
            return Math.Min(count, (int)Math.Round(count * share * 0.8));
        }
    }
}
