using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Economy;
using AndijonSaltanatlar.Government;

namespace AndijonSaltanatlar.Population
{
    /// <summary>
    /// Shahar aholisi: fuqarolar, oilalar, kasblar, ehtiyojlar va baxt. Har o'yin kunida bir marta
    /// hisoblanadi. PROMPT 17 da kengaytirildi:
    ///  - har bir fuqaro ANIQ ish joyiga (bino) biriktiriladi va bu biriktiruv kunma-kun barqaror;
    ///  - Save/Load fuqaroning shaxsini (Id, ism, oila, uy, ish joyi) saqlaydi;
    ///  - yangi qurilgan bo'sh uyga yangi oila ko'chib keladi (yangi mahalla to'ladi);
    ///  - oilaning oziq-ovqat bilan ta'minlanishi (Household.FedRatio) baxt va ochlikka ta'sir qiladi.
    /// </summary>
    public sealed class PopulationManager
    {
        private static readonly string[] GivenNames =
        {
            "Bobur", "Alisher", "Zulfiya", "Nodira", "Tohir", "Oydin", "Sardor", "Gulnora", "Jahongir", "Dilnoza",
            "Akbar", "Malika", "Ulug'bek", "Sevara", "Qodir", "Ra'no", "Abdulloh", "Mohira", "Rustam", "Lola",
            "Anvar", "Shahlo", "Ibrohim", "Nargiza"
        };

        private static readonly string[] FamilyNames = { "Andijoniy", "Asakaviy", "Marg'iloniy", "Farg'oniy", "Qo'qoniy", "Namangoniy" };

        /// <summary>Ishga joylash tartibi (muhimlik bo'yicha): avval oziq-ovqat, keyin savdo va hunar, so'ng xizmat va boshqaruv.</summary>
        private static readonly CitizenProfession[] JobOrder =
        {
            CitizenProfession.Dehqon, CitizenProfession.Baliqchi, CitizenProfession.Chorvador, CitizenProfession.Bogbon,
            CitizenProfession.Savdogar, CitizenProfession.Hunarmand, CitizenProfession.Tashuvchi, CitizenProfession.Konchi,
            CitizenProfession.Quruvchi, CitizenProfession.Soqchi, CitizenProfession.Tabib, CitizenProfession.Muallim,
            CitizenProfession.Omborchi, CitizenProfession.Amaldor, CitizenProfession.SoliqYiguvchi, CitizenProfession.BozorEgasi,
            CitizenProfession.Olim, CitizenProfession.Memor
        };

        private readonly Random _rng = new Random(1560);
        private readonly List<Citizen> _citizens = new List<Citizen>();
        private readonly List<Household> _households = new List<Household>();
        private readonly Dictionary<int, Citizen> _byId = new Dictionary<int, Citizen>();
        private readonly Dictionary<int, Household> _hhById = new Dictionary<int, Household>();
        private readonly Dictionary<CitizenProfession, int> _profCount = new Dictionary<CitizenProfession, int>();
        private int _nextCitizenId = 1;
        private int _nextHouseholdId = 1;

        public bool Initialized { get; private set; }
        public int Count => _citizens.Count;
        public int Workers { get; private set; }
        public int Unemployed => Count - Workers;
        public int Families => _households.Count;
        public int HousingCapacity { get; private set; }
        public int HousingUsed => Count;
        /// <summary>Shahardagi jami ish o'rinlari (band + bo'sh).</summary>
        public int JobCapacity { get; private set; }
        public int FreeJobs => Math.Max(0, JobCapacity - Workers);
        public float AvgHappiness { get; private set; } = 70f;
        public float AvgHealth { get; private set; } = 100f;
        public float AvgEducation { get; private set; }
        public float AvgSecurity { get; private set; }

        // --- Ehtiyojlar (0..100): har kuni hisoblanadi, baxt, sog'liq va aholi o'sishiga ta'sir qiladi ---
        public float NeedFood { get; private set; } = 70f;
        public float NeedHousing { get; private set; } = 100f;
        public float NeedSecurity { get; private set; } = 50f;
        public float NeedTrade { get; private set; } = 50f;
        public float NeedEducation { get; private set; } = 30f;
        public float NeedHealth { get; private set; } = 30f;
        public float NeedsAverage => (NeedFood + NeedHousing + NeedSecurity + NeedTrade + NeedEducation + NeedHealth) / 6f;

        /// <summary>Xizmat ko'rsatuvchilar soni va aholi soni bo'yicha ehtiyoj qondirilishi (0..100): har perCapita kishiga bitta.</summary>
        public static float Coverage(float providers, int population, float perCapita)
        {
            float need = Math.Max(1f, population / perCapita);
            return Math.Clamp(100f * providers / need, 0f, 100f);
        }

        /// <summary>Ehtiyojlarni joriy kasblar, binolar va oziq-ovqat zaxirasi bo'yicha qayta hisoblaydi.</summary>
        public void ComputeNeeds(int foodStock, int towers, int barracks, int clinics, int madrasas)
        {
            int n = Math.Max(1, Count);
            float days = foodStock / (float)n;
            NeedFood = foodStock <= 0 ? 0f : Math.Clamp(40f + days * 15f, 0f, 100f);
            NeedHousing = Count <= HousingCapacity ? 100f : Math.Clamp(100f * HousingCapacity / n, 0f, 100f);
            NeedSecurity = Coverage(CountProfession(CitizenProfession.Soqchi) + towers + barracks * 2, Count, 12f);
            NeedTrade = Coverage(CountProfession(CitizenProfession.Savdogar) + CountProfession(CitizenProfession.Tashuvchi)
                                 + CountProfession(CitizenProfession.BozorEgasi) * 2 + CountProfession(CitizenProfession.Omborchi) * 0.5f, Count, 14f);
            NeedEducation = Coverage(CountProfession(CitizenProfession.Muallim) * 2 + CountProfession(CitizenProfession.Olim) + madrasas, Count, 18f);
            NeedHealth = Coverage(CountProfession(CitizenProfession.Tabib) * 2 + clinics, Count, 18f);
        }

        public IReadOnlyList<Citizen> Citizens => _citizens;
        public IReadOnlyList<Household> Households => _households;

        public Citizen GetById(int id) => _byId.TryGetValue(id, out Citizen c) ? c : null;
        public Household GetHousehold(int id) => _hhById.TryGetValue(id, out Household h) ? h : null;

        public int CountProfession(CitizenProfession p) => _profCount.TryGetValue(p, out int v) ? v : 0;

        private static readonly (CitizenProfession P, int Slots)[] NoJobs = new (CitizenProfession, int)[0];

        /// <summary>
        /// Bino turi qaysi kasblarga nechta ish o'rni beradi (birinchisi — asosiy kasb). Bir binoda bir necha kasb:
        /// masalan bozorda savdogarlar va bozor egasi, saroyda amaldorlar, soliqchilar va me'mor.
        /// </summary>
        public static (CitizenProfession P, int Slots)[] JobsOf(BuildingType type) => type switch
        {
            BuildingType.Farm => new[] { (CitizenProfession.Dehqon, 6), (CitizenProfession.Chorvador, 2) },
            BuildingType.Garden => new[] { (CitizenProfession.Bogbon, 5) },
            BuildingType.Market => new[] { (CitizenProfession.Savdogar, 4), (CitizenProfession.BozorEgasi, 1) },
            BuildingType.Caravanserai => new[] { (CitizenProfession.Tashuvchi, 3), (CitizenProfession.Omborchi, 2) },
            BuildingType.Barracks => new[] { (CitizenProfession.Soqchi, 6) },
            BuildingType.MainHall => new[] { (CitizenProfession.Amaldor, 4), (CitizenProfession.SoliqYiguvchi, 2), (CitizenProfession.Memor, 1) },
            BuildingType.Madrasa => new[] { (CitizenProfession.Muallim, 3), (CitizenProfession.Olim, 2) },
            BuildingType.Workshop => new[] { (CitizenProfession.Hunarmand, 4), (CitizenProfession.Quruvchi, 3) },
            BuildingType.Mine => new[] { (CitizenProfession.Konchi, 6) },
            BuildingType.Fishery => new[] { (CitizenProfession.Baliqchi, 5) },
            BuildingType.Clinic => new[] { (CitizenProfession.Tabib, 4) },
            _ => NoJobs
        };

        /// <summary>Binodagi shu kasb uchun ish o'rinlari: asosiy kasbga har daraja +1 o'rin.</summary>
        public static int JobSlotsAt(Building b, CitizenProfession p)
        {
            var jobs = JobsOf(b.Type);
            for (int i = 0; i < jobs.Length; i++)
                if (jobs[i].P == p) return jobs[i].Slots + (i == 0 ? Math.Max(0, b.Level - 1) : 0);
            return 0;
        }

        /// <summary>Asosiy kasb va uning ish o'rinlari (eski API — bitta kasb).</summary>
        public static int JobSlots(BuildingType type, out CitizenProfession profession)
        {
            var jobs = JobsOf(type);
            if (jobs.Length == 0) { profession = CitizenProfession.None; return 0; }
            profession = jobs[0].P;
            return jobs[0].Slots;
        }

        public static string ProfessionName(CitizenProfession p)
        {
            switch (p)
            {
                case CitizenProfession.Dehqon: return "Dehqon";
                case CitizenProfession.Bogbon: return "Bog'bon";
                case CitizenProfession.Savdogar: return "Savdogar";
                case CitizenProfession.Hunarmand: return "Hunarmand";
                case CitizenProfession.Quruvchi: return "Quruvchi";
                case CitizenProfession.Tabib: return "Tabib";
                case CitizenProfession.Muallim: return "Ustoz";
                case CitizenProfession.Soqchi: return "Soqchi";
                case CitizenProfession.Amaldor: return "Amaldor";
                case CitizenProfession.Tashuvchi: return "Karvonchi";
                case CitizenProfession.Chorvador: return "Chorvador";
                case CitizenProfession.Konchi: return "Konchi";
                case CitizenProfession.Baliqchi: return "Baliqchi";
                case CitizenProfession.Omborchi: return "Omborchi";
                case CitizenProfession.SoliqYiguvchi: return "Soliq yig'uvchi";
                case CitizenProfession.BozorEgasi: return "Bozor egasi";
                case CitizenProfession.Olim: return "Olim";
                case CitizenProfession.Memor: return "Me'mor";
                default: return "Ishsiz";
            }
        }

        /// <summary>Kasb guruhi (aholi paneli uchun): ishlab chiqaruvchi, iqtisodiy yoki boshqaruv-ilm.</summary>
        public static string ProfessionGroup(CitizenProfession p) => p switch
        {
            CitizenProfession.Dehqon or CitizenProfession.Bogbon or CitizenProfession.Chorvador or CitizenProfession.Hunarmand
                or CitizenProfession.Quruvchi or CitizenProfession.Konchi or CitizenProfession.Baliqchi => "Ishlab chiqaruvchilar",
            CitizenProfession.Savdogar or CitizenProfession.Tashuvchi or CitizenProfession.Omborchi
                or CitizenProfession.SoliqYiguvchi or CitizenProfession.BozorEgasi => "Iqtisodiy aholi",
            CitizenProfession.None => "Ishsizlar",
            CitizenProfession.Soqchi => "Soqchilar",
            _ => "Boshqaruv va ilm-fan"
        };

        // ------------------------------------------------------------------
        // Boshlang'ich aholi
        // ------------------------------------------------------------------
        public void Initialize(BuildingManager buildings)
        {
            ClearAll();
            ComputeHousing(buildings);
            int target = Math.Min(42, Math.Max(14, HousingCapacity * 3 / 5));
            foreach (Building b in buildings.Buildings)
            {
                if (Count >= target) break;
                if (b.Type == BuildingType.House) CreateHousehold(b.Id, b.PopulationCapacity, -1);
            }
            AssignJobs(buildings);
            RecomputeStats(buildings);
            Initialized = true;
        }

        private void ClearAll()
        {
            _citizens.Clear();
            _households.Clear();
            _byId.Clear();
            _hhById.Clear();
            _nextCitizenId = 1;
            _nextHouseholdId = 1;
        }

        private Household CreateHousehold(int homeId, int cap, int members)
        {
            if (cap <= 0) cap = 5;
            var hh = new Household { Id = _nextHouseholdId++, HomeBuildingId = homeId, FamilyName = Pick(FamilyNames) };
            int n = members > 0 ? members : Math.Max(1, Math.Min(cap, 2 + _rng.Next(cap >= 4 ? 3 : 2)));
            _households.Add(hh);
            _hhById[hh.Id] = hh;
            for (int i = 0; i < n; i++) AddMember(hh, 16 + _rng.Next(50), 64 + _rng.Next(16));
            return hh;
        }

        /// <summary>Sinov uchun: uysiz yangi kelgan oila (n kishi) — BuildingManager talab qilinmaydi.</summary>
        internal void AddNewcomers(int n)
        {
            var hh = new Household { Id = _nextHouseholdId++, FamilyName = Pick(FamilyNames) };
            _households.Add(hh);
            _hhById[hh.Id] = hh;
            for (int i = 0; i < n; i++) AddMember(hh, 20 + i % 40, 70f);
        }

        private Citizen AddMember(Household hh, int age, float happiness)
        {
            var c = new Citizen
            {
                Id = _nextCitizenId++,
                HouseholdId = hh.Id,
                HomeBuildingId = hh.HomeBuildingId,
                Name = Pick(GivenNames) + " " + hh.FamilyName,
                Age = age,
                Happiness = happiness,
                CurrentState = CitizenState.AtHome
            };
            hh.MemberIds.Add(c.Id);
            _citizens.Add(c);
            _byId[c.Id] = c;
            return c;
        }

        private string Pick(string[] a) => a[_rng.Next(a.Length)];

        private void ComputeHousing(BuildingManager b)
        {
            int cap = 0;
            foreach (Building x in b.Buildings) cap += x.PopulationCapacity;
            HousingCapacity = cap;
        }

        // ------------------------------------------------------------------
        // Ish joylari — har bir fuqaro aniq binoga biriktiriladi (barqaror)
        // ------------------------------------------------------------------
        public void AssignJobs(BuildingManager b) => AssignJobs(b.Buildings);

        /// <summary>
        /// Ish joylari: har bir QURIB BITKAZILGAN binoning har bir kasbi uchun bo'sh o'rinlar (JobsOf, daraja bilan ko'payadi).
        /// Fuqaro o'z ishida qoladi (barqaror); ishsizlar kasblar muhimligi tartibida joylashadi.
        /// </summary>
        public void AssignJobs(IEnumerable<Building> buildings)
        {
            // 1) (bino, kasb) bo'yicha bo'sh o'rinlar
            var free = new Dictionary<(int, CitizenProfession), int>();
            var order = new List<Building>();
            int capacity = 0;
            foreach (Building x in buildings)
            {
                if (x.BuildProgress < 1f) continue;   // qurilayotgan binoda hali ish yo'q
                var jobs = JobsOf(x.Type);
                if (jobs.Length == 0) continue;
                order.Add(x);
                foreach (var j in jobs)
                {
                    int slots = JobSlotsAt(x, j.P);
                    free[(x.Id, j.P)] = slots;
                    capacity += slots;
                }
            }
            JobCapacity = capacity;

            // 2) Mavjud ish joyi hali ham bor bo'lsa — fuqaro o'z ishida qoladi.
            foreach (Citizen c in _citizens)
            {
                int wid = c.WorkplaceBuildingId ?? 0;
                var key = (wid, c.Profession);
                if (c.IsEmployed && wid != 0 && free.TryGetValue(key, out int left) && left > 0)
                {
                    free[key] = left - 1;
                    c.CurrentState = CitizenState.Working;
                }
                else
                {
                    c.IsEmployed = false;
                    c.Profession = CitizenProfession.None;
                    c.WorkplaceBuildingId = null;
                }
            }

            // 3) Ishsizlarni bo'sh o'rinlarga (kasblar muhimligi tartibida) joylash.
            foreach (Citizen c in _citizens)
            {
                if (c.IsEmployed) continue;
                foreach (CitizenProfession p in JobOrder)
                {
                    Building target = null;
                    foreach (Building x in order)
                        if (free.TryGetValue((x.Id, p), out int n) && n > 0) { target = x; break; }
                    if (target == null) continue;
                    free[(target.Id, p)]--;
                    c.Profession = p;
                    c.IsEmployed = true;
                    c.WorkplaceBuildingId = target.Id;
                    c.CurrentState = CitizenState.Working;
                    break;
                }
            }
            RecountProfessions();
        }

        private void RecountProfessions()
        {
            _profCount.Clear();
            Workers = 0;
            foreach (Citizen c in _citizens)
            {
                if (c.IsEmployed) Workers++;
                _profCount[c.Profession] = CountProfession(c.Profession) + 1;
            }
        }

        // ------------------------------------------------------------------
        // Kunlik hisob
        // ------------------------------------------------------------------
        public void DailyTick(BuildingManager b, ResourceManager res, GovernmentPolicy policy,
                              float healthBonus = 0f, float eduBonus = 0f, float stabilityBonus = 0f)
        {
            ComputeHousing(b);
            bool housed = HousingUsed <= HousingCapacity;
            bool cityHasFood = res.Get(ResourceType.Food) > 0;
            int guards = b.Count(BuildingType.Barracks) + b.Count(BuildingType.Tower);
            int schools = b.Count(BuildingType.Madrasa);
            float taxPain = policy.PopulationTax * 0.6f;
            ComputeNeeds(res.Get(ResourceType.Food), b.Count(BuildingType.Tower), b.Count(BuildingType.Barracks), b.Count(BuildingType.Clinic), schools);
            float needsMood = (NeedsAverage - 50f) * 0.2f;           // ehtiyojlar qondirilgani: ±10 baxt
            float healthCare = (NeedHealth - 50f) * 0.2f;            // tabiblar: ±10 sog'liq

            foreach (Citizen c in _citizens)
            {
                // Oilaning bugungi ta'minoti (1 = to'q, 0.5 = yarim ulush, 0 = och). Bozor/oila iqtisodi belgilaydi.
                Household hh = GetHousehold(c.HouseholdId);
                float fed = cityHasFood ? (hh != null ? hh.FedRatio : 1f) : 0f;
                float comfort = hh != null ? hh.Comfort : 0f;

                if (fed >= 0.99f) c.Hunger = Math.Max(0f, c.Hunger - 20f);
                else if (fed >= 0.45f) c.Hunger = Approach(c.Hunger, 45f, 12f);
                else c.Hunger = Math.Min(100f, c.Hunger + 25f);

                c.Energy = Math.Min(100f, c.Energy + 10f);
                float eduTarget = (schools > 0 ? Math.Min(100, 55 + schools * 10) : 20) * (1f + eduBonus);
                c.Education = Approach(c.Education, Math.Min(100f, eduTarget), 4f);
                c.Health = Approach(c.Health, Math.Min(100f, (fed >= 0.45f ? 90 : 55) + healthBonus * 20f + healthCare), 6f);

                float target = 50f;
                target += c.IsEmployed ? 14 : -16;
                target += housed ? 10 : -22;
                target += fed >= 0.99f ? 10 : (fed >= 0.45f ? -4 : -20);
                target += guards > 0 ? 6 : -4;
                target += stabilityBonus * 20f;
                target += comfort * 4f;
                target += needsMood;
                target -= taxPain;
                target -= c.Hunger * 0.2f;
                c.Happiness = Clamp100(Approach(c.Happiness, target, 8f));
                c.CurrentState = c.IsEmployed ? CitizenState.Working : CitizenState.Idle;
            }

            foreach (Household hh in _households)
            {
                float sum = 0; int n = 0;
                foreach (int id in hh.MemberIds) { Citizen m = GetById(id); if (m != null) { sum += m.Happiness; n++; } }
                if (n > 0) hh.Happiness = sum / n;
            }
            RecomputeStats(b);
        }

        public void MonthlyGrowth(BuildingManager b, ResourceManager res)
        {
            ComputeHousing(b);
            int room = HousingCapacity - HousingUsed;
            if (AvgHappiness > 58f && res.Get(ResourceType.Food) > Count && room > 0)
            {
                // Ehtiyojlari yaxshi qondirilgan shaharga ko'proq odam ko'chib keladi
                int n = Math.Max(1, Math.Min(room, room / 4 + 1 + (NeedsAverage >= 65f ? 2 : 0)));
                for (int i = 0; i < n; i++) AddImmigrant(b);
            }
            else if (AvgHappiness < 30f && Count > 6)
            {
                int n = 1 + _rng.Next(2);
                for (int i = 0; i < n && _citizens.Count > 6; i++) RemoveCitizen(_citizens[_citizens.Count - 1]);
            }
            AssignJobs(b);
            RecomputeStats(b);
        }

        /// <summary>
        /// Yangi kelgan fuqaro: avval bo'sh turgan uyga yangi oila bo'lib joylashadi (yangi mahalla shunday
        /// to'ladi), bo'sh uy bo'lmasa — eng kam a'zoli, joyi bor oilaga qo'shiladi.
        /// </summary>
        private void AddImmigrant(BuildingManager b)
        {
            var occupied = new HashSet<int>();
            foreach (Household h in _households) if (h.HomeBuildingId.HasValue) occupied.Add(h.HomeBuildingId.Value);
            if (b != null)
                foreach (Building x in b.Buildings)
                    if (x.Type == BuildingType.House && x.BuildProgress >= 1f && !occupied.Contains(x.Id))
                    {
                        CreateHousehold(x.Id, x.PopulationCapacity, 1);
                        _citizens[_citizens.Count - 1].Happiness = 62f;
                        return;
                    }

            Household best = null;
            foreach (Household h in _households)
                if (h.MemberIds.Count < 5 && (best == null || h.MemberIds.Count < best.MemberIds.Count)) best = h;
            if (best == null && _households.Count > 0) best = _households[_rng.Next(_households.Count)];
            if (best != null) AddMember(best, 16 + _rng.Next(30), 60f);
        }

        /// <summary>
        /// To'liq aholi: har bir tayyor uyda oila yashaydi va uy sig'imigacha to'ladi; kayfiyat yuqori
        /// (xursand aholi ko'chib ketmaydi). Qo'shilgan fuqarolar sonini qaytaradi.
        /// </summary>
        public int FillToCapacity(BuildingManager b)
        {
            if (b == null) return 0;
            ComputeHousing(b);
            int added = 0;
            var living = new Dictionary<int, int>();
            var homeOf = new Dictionary<int, Household>();
            foreach (Household h in _households)
            {
                if (!h.HomeBuildingId.HasValue) continue;
                int id = h.HomeBuildingId.Value;
                living[id] = (living.TryGetValue(id, out int n) ? n : 0) + h.MemberIds.Count;
                if (!homeOf.ContainsKey(id)) homeOf[id] = h;
            }
            foreach (Building x in b.Buildings)
            {
                if (x.Type != BuildingType.House || x.BuildProgress < 1f) continue;
                int cap = Math.Max(1, x.PopulationCapacity);
                int now = living.TryGetValue(x.Id, out int ln) ? ln : 0;
                if (!homeOf.TryGetValue(x.Id, out Household hh))
                {
                    hh = CreateHousehold(x.Id, cap, 1);
                    homeOf[x.Id] = hh;
                    now += 1; added++;
                }
                while (now < cap) { AddMember(hh, 16 + _rng.Next(48), 82f); now++; added++; }
            }
            foreach (Citizen c in _citizens) if (c.Happiness < 85f) c.Happiness = 85f;
            AssignJobs(b);
            RecomputeStats(b);
            return added;
        }

        private void RemoveCitizen(Citizen c)
        {
            _citizens.Remove(c);
            _byId.Remove(c.Id);
            Household hh = GetHousehold(c.HouseholdId);
            if (hh == null) return;
            hh.MemberIds.Remove(c.Id);
            if (hh.MemberIds.Count == 0) { _households.Remove(hh); _hhById.Remove(hh.Id); }
        }

        public void AdjustHappiness(float delta)
        {
            foreach (Citizen c in _citizens) c.Happiness = Clamp100(c.Happiness + delta);
            RecomputeStats(null);
        }

        private void RecomputeStats(BuildingManager b)
        {
            if (_citizens.Count == 0)
            {
                AvgHappiness = AvgHealth = 50f;
                AvgEducation = 0f;
                return;
            }
            float h = 0, he = 0, ed = 0;
            foreach (Citizen c in _citizens) { h += c.Happiness; he += c.Health; ed += c.Education; }
            AvgHappiness = h / _citizens.Count;
            AvgHealth = he / _citizens.Count;
            AvgEducation = ed / _citizens.Count;
            if (b != null) AvgSecurity = Math.Min(100, b.Count(BuildingType.Barracks) * 20 + b.Count(BuildingType.Tower) * 6);
        }

        private static float Approach(float cur, float target, float step)
        {
            if (cur < target) return Math.Min(target, cur + step);
            if (cur > target) return Math.Max(target, cur - step);
            return cur;
        }

        private static float Clamp100(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);

        // ------------------------------------------------------------------
        // Save / Load — fuqaroning shaxsi saqlanadi
        // ------------------------------------------------------------------
        public List<CitizenSnapshot> Snapshot(BuildingManager b = null)
        {
            var list = new List<CitizenSnapshot>();
            foreach (Citizen c in _citizens)
            {
                var s = new CitizenSnapshot
                {
                    Id = c.Id, Name = c.Name, HouseholdId = c.HouseholdId,
                    Profession = (int)c.Profession, Age = c.Age, Happiness = c.Happiness, Health = c.Health,
                    Education = c.Education, Hunger = c.Hunger, Energy = c.Energy, Money = c.PersonalMoney,
                    Employed = c.IsEmployed
                };
                Building work = FindBuilding(b, c.WorkplaceBuildingId);
                if (work != null) { s.WorkX = work.Position.X; s.WorkY = work.Position.Y; }
                list.Add(s);
            }
            return list;
        }

        public List<HouseholdSnapshot> SnapshotHouseholds(BuildingManager b)
        {
            var list = new List<HouseholdSnapshot>();
            foreach (Household h in _households)
            {
                var s = new HouseholdSnapshot
                {
                    Id = h.Id, FamilyName = h.FamilyName, Money = h.SharedMoney, FoodReserve = h.FoodReserve,
                    FedRatio = h.FedRatio, Comfort = h.Comfort, LastIncome = h.LastIncome, LastExpense = h.LastExpense
                };
                Building home = FindBuilding(b, h.HomeBuildingId);
                if (home != null) { s.HomeX = home.Position.X; s.HomeY = home.Position.Y; }
                list.Add(s);
            }
            return list;
        }

        private static Building FindBuilding(BuildingManager b, int? id)
        {
            if (b == null || !id.HasValue) return null;
            foreach (Building x in b.Buildings) if (x.Id == id.Value) return x;
            return null;
        }

        public void Restore(List<CitizenSnapshot> snap, BuildingManager b, List<HouseholdSnapshot> households = null)
        {
            if (snap == null || snap.Count == 0) return;
            bool identity = households != null && households.Count > 0 && snap[0].Id > 0;
            ClearAll();
            if (identity) RestoreIdentity(snap, households, b);
            else RestoreLegacy(snap, b);
            ComputeHousing(b);
            AssignJobs(b);
            RecomputeStats(b);
            Initialized = true;
        }

        /// <summary>PROMPT 17 formati: har bir fuqaro o'z Id, ismi, oilasi, uyi va ish joyi bilan tiklanadi.</summary>
        private void RestoreIdentity(List<CitizenSnapshot> snap, List<HouseholdSnapshot> households, BuildingManager b)
        {
            foreach (HouseholdSnapshot hs in households)
            {
                var hh = new Household
                {
                    Id = hs.Id, FamilyName = hs.FamilyName ?? "", SharedMoney = hs.Money, FoodReserve = hs.FoodReserve,
                    FedRatio = hs.FedRatio, Comfort = hs.Comfort, LastIncome = hs.LastIncome, LastExpense = hs.LastExpense
                };
                Building home = hs.HomeX >= 0 ? b.GetAt(hs.HomeX, hs.HomeY) : null;
                hh.HomeBuildingId = home?.Id;
                _households.Add(hh);
                _hhById[hh.Id] = hh;
                _nextHouseholdId = Math.Max(_nextHouseholdId, hh.Id + 1);
            }
            foreach (CitizenSnapshot s in snap)
            {
                Household hh = GetHousehold(s.HouseholdId);
                if (hh == null) continue; // buzuq yozuv — o'tkazib yuboramiz
                var c = new Citizen
                {
                    Id = s.Id, Name = s.Name ?? "", HouseholdId = hh.Id, HomeBuildingId = hh.HomeBuildingId,
                    Age = s.Age, Happiness = s.Happiness, Health = s.Health, Education = s.Education,
                    Hunger = s.Hunger, Energy = s.Energy, PersonalMoney = s.Money,
                    IsEmployed = s.Employed, Profession = (CitizenProfession)s.Profession
                };
                Building work = s.WorkX >= 0 ? b.GetAt(s.WorkX, s.WorkY) : null;
                c.WorkplaceBuildingId = work?.Id;
                if (work == null) { c.IsEmployed = false; c.Profession = CitizenProfession.None; }
                hh.MemberIds.Add(c.Id);
                _citizens.Add(c);
                _byId[c.Id] = c;
                _nextCitizenId = Math.Max(_nextCitizenId, c.Id + 1);
            }
        }

        /// <summary>Eski (PROMPT 17 gacha) save: shaxs ma'lumoti yo'q — oilalar qayta tuziladi.</summary>
        private void RestoreLegacy(List<CitizenSnapshot> snap, BuildingManager b)
        {
            var houses = new List<int>();
            foreach (Building x in b.Buildings) if (x.Type == BuildingType.House) houses.Add(x.Id);
            Household hh = null;
            int inHouse = 0, hi = 0;
            foreach (CitizenSnapshot s in snap)
            {
                if (hh == null || inHouse >= 5)
                {
                    hh = new Household
                    {
                        Id = _nextHouseholdId++, FamilyName = Pick(FamilyNames),
                        HomeBuildingId = houses.Count > 0 ? houses[hi++ % houses.Count] : (int?)null
                    };
                    _households.Add(hh);
                    _hhById[hh.Id] = hh;
                    inHouse = 0;
                }
                Citizen c = AddMember(hh, s.Age, s.Happiness);
                c.Health = s.Health; c.Education = s.Education; c.Hunger = s.Hunger; c.Energy = s.Energy;
                c.PersonalMoney = s.Money;
                inHouse++;
            }
        }
    }
}
