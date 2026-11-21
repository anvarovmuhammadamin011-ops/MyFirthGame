using System.Collections.Generic;

namespace AndijonSaltanatlar.Military
{
    /// <summary>Yollashda saqlanadigan qo'shin holati (JSON).</summary>
    public sealed class MilitarySnapshot
    {
        public List<int[]> Units { get; set; } = new List<int[]>();  // [type, level, exp]
        public List<int[]> Queue { get; set; } = new List<int[]>();  // [type, daysLeft]
    }

    /// <summary>
    /// O'yinchi qo'shinini boshqaradi: jangchilar ro'yxati, guruhlar, qo'mondonlar, tayyorlash navbati,
    /// tajriba, upkeep va umumiy jangovar kuch. AI saltanatlar mavhum MilitaryStrength bilan boshqariladi.
    /// </summary>
    public sealed class ArmyManager
    {
        private readonly RecruitmentManager _recruit = new RecruitmentManager();
        private int _nextUnit = 1, _nextArmy = 1, _nextCmd = 100;

        public List<MilitaryUnit> Units { get; } = new List<MilitaryUnit>();
        public List<Army> Armies { get; } = new List<Army>();
        public List<Commander> Commanders { get; } = new List<Commander>();

        public int OwnerKingdomId { get; private set; } = 1;
        public int HomeRegionId { get; private set; } = 1;

        public int MaxConcurrent { get => _recruit.MaxConcurrent; set => _recruit.MaxConcurrent = value < 1 ? 1 : value; }
        public int QueueCount => _recruit.Queue.Count;
        public IReadOnlyList<RecruitOrder> Queue => _recruit.Queue;

        public void Initialize(int ownerKingdomId, int homeRegionId)
        {
            OwnerKingdomId = ownerKingdomId;
            HomeRegionId = homeRegionId;
            Units.Clear(); Armies.Clear(); Commanders.Clear();
            _nextUnit = 1; _nextArmy = 1; _nextCmd = 100;

            CreateArmy("Asosiy qo'shin");
            CreateArmy("Shahar qo'riqchilari");
            CreateArmy("Saroy soqchilari");
            CreateArmy("Zaxira qo'shin");

            foreach (Commander c in HeroCatalog.CreateHeroes()) { c.Id = _nextCmd++; c.AssignedArmyId = Armies[0].Id; Commanders.Add(c); }
            Armies[0].CommanderId = Commanders.Count > 0 ? Commanders[0].Id : (int?)null;

            // Boshlang'ich kichik qo'shin
            AddUnit(UnitType.Piyoda); AddUnit(UnitType.Piyoda); AddUnit(UnitType.Qilichboz);
        }

        public Army CreateArmy(string name)
        {
            var a = new Army { Id = _nextArmy++, Name = name, AssignedRegionId = HomeRegionId };
            Armies.Add(a);
            return a;
        }

        private MilitaryUnit AddUnit(UnitType type, int level = 1)
        {
            MilitaryUnit u = MilitaryUnit.Create(_nextUnit++, type, OwnerKingdomId, HomeRegionId);
            for (int i = 1; i < level; i++) TrainingSystem.AddExperience(u, TrainingSystem.XpForNext(i));
            Units.Add(u);
            if (Armies.Count > 0) { Armies[0].UnitIds.Add(u.Id); u.ArmyId = Armies[0].Id; }
            return u;
        }

        /// <summary>Darhol qo'shinga qo'shadi (masalan, qamalda shohi o'lgach tomonimizga o'tgan dushman askarlari).</summary>
        public int Recruit(UnitType type, int count, int level = 1)
        {
            int n = 0;
            for (; n < count; n++) AddUnit(type, System.Math.Clamp(level, 1, 3));
            return n;
        }

        public void Enqueue(UnitType type) => _recruit.Enqueue(type, OwnerKingdomId, HomeRegionId);

        /// <summary>Bir o'yin kunini qayta ishlaydi: tayyor jangchilar qo'shinga qo'shiladi. Soni qaytadi.</summary>
        public int DailyTick()
        {
            List<RecruitOrder> done = _recruit.Tick();
            foreach (RecruitOrder o in done) AddUnit(o.Type);
            return done.Count;
        }

        /// <summary>Mashg'ulot: barcha jangchilarga tajriba qo'shadi; darajaga ko'tarilganlar soni qaytadi.</summary>
        public int TrainAll(int xp)
        {
            int leveled = 0;
            foreach (MilitaryUnit u in Units) if (TrainingSystem.AddExperience(u, xp)) leveled++;
            return leveled;
        }

        public int TotalUnits => Units.Count;

        /// <summary>Urushda halok bo'lganlar: avval eng past darajalilar. Necha kishi olib tashlangani qaytadi.</summary>
        public int RemoveCasualties(int count)
        {
            if (count <= 0) return 0;
            var dead = new List<MilitaryUnit>(Units);
            dead.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.Strength.CompareTo(b.Strength));
            int n = 0;
            foreach (MilitaryUnit u in dead)
            {
                if (n >= count) break;
                Units.Remove(u);
                foreach (Army a in Armies) a.UnitIds.Remove(u.Id);
                n++;
            }
            return n;
        }
        public int TotalUpkeep => MilitaryEconomy.DailyUpkeep(Units);

        /// <summary>Shahar rivojlanish bonusi (%): kuchli shahar qo'shinni kuchaytiradi. Har kuni yangilanadi (CityProgress).</summary>
        public int DevelopmentBonusPercent { get; set; }

        public int TotalStrength
        {
            get
            {
                int s = 0;
                foreach (MilitaryUnit u in Units) s += u.Strength;
                foreach (Commander c in Commanders) s += c.LeadershipBonus;
                return (int)(s * (1f + DevelopmentBonusPercent / 100f));
            }
        }

        public int Count(UnitType type)
        {
            int n = 0;
            foreach (MilitaryUnit u in Units) if (u.UnitType == type) n++;
            return n;
        }

        // ---------- Save / Load ----------
        public MilitarySnapshot Snapshot()
        {
            var s = new MilitarySnapshot();
            foreach (MilitaryUnit u in Units) s.Units.Add(new[] { (int)u.UnitType, u.Level, u.Experience });
            foreach (RecruitOrder o in _recruit.Queue) s.Queue.Add(new[] { (int)o.Type, o.DaysLeft });
            return s;
        }

        public void Restore(MilitarySnapshot s)
        {
            if (s == null) return;
            Units.Clear();
            foreach (Army a in Armies) a.UnitIds.Clear();
            foreach (int[] u in s.Units)
            {
                MilitaryUnit mu = AddUnit((UnitType)u[0], u[1]);
                mu.Experience = u.Length > 2 ? u[2] : 0;
            }
            _recruit.Queue.Clear();
            foreach (int[] o in s.Queue)
                _recruit.Queue.Add(new RecruitOrder { Type = (UnitType)o[0], DaysLeft = o[1], OwnerKingdomId = OwnerKingdomId, RegionId = HomeRegionId });
        }
    }
}
