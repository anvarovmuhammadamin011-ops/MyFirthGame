using System;
using System.Collections.Generic;
using AndijonSaltanatlar.AdvancedEconomy;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Population;
using AndijonSaltanatlar.Units;

namespace AndijonSaltanatlar.NPC
{
    /// <summary>
    /// NPC muloqotining markazi:
    ///  - xaritadagi ko'rinadigan figuralarni (CitizenUnit) haqiqiy fuqarolarga bog'laydi;
    ///  - fuqaro profilini haqiqiy manbalardan yig'adi;
    ///  - fuqarolar xotirasini (NPCMemory) saqlaydi — faqat suhbatlashilgan fuqarolar uchun.
    /// Ko'rinmaydigan fuqarolar uchun alohida obyekt yaratilmaydi: ular faqat statistik hisobda qatnashadi.
    /// </summary>
    public sealed class NPCInteractionManager
    {
        private readonly Dictionary<int, NPCMemory> _memories = new Dictionary<int, NPCMemory>();
        /// <summary>Ko'rinadigan fuqaro figuralari tartibi bo'yicha bog'langan fuqaro Id'lari.</summary>
        private readonly List<int> _bindings = new List<int>();

        public IReadOnlyList<int> Bindings => _bindings;
        public IEnumerable<NPCMemory> Memories => _memories.Values;

        /// <summary>Fuqaro xotirasi (kerak bo'lganda yaratiladi).</summary>
        public NPCMemory MemoryOf(int citizenId)
        {
            if (!_memories.TryGetValue(citizenId, out NPCMemory m))
            {
                m = new NPCMemory { CitizenId = citizenId };
                _memories[citizenId] = m;
            }
            return m;
        }

        public bool HasMemory(int citizenId) => _memories.ContainsKey(citizenId);

        // ------------------------------------------------------------------
        // Figura ↔ fuqaro bog'lanishi
        // ------------------------------------------------------------------

        /// <summary>
        /// Har bir ko'rinadigan figurani haqiqiy fuqaroga bog'laydi. Figura ko'rinishi (savdogar, dehqon...)
        /// mos kasbdagi fuqaroga beriladi; "xaridor" ko'rinishidagilar birinchi navbatda ishsiz fuqarolarga.
        /// saved berilsa (Save/Load) — aynan o'sha bog'lanish tiklanadi.
        /// </summary>
        public void BindUnits(UnitManager units, PopulationManager pop, List<int> saved = null)
        {
            _bindings.Clear();
            var used = new HashSet<int>();
            int index = 0;
            foreach (Unit u in units.Units)
            {
                if (!(u is CitizenUnit cu)) continue;
                Citizen c = null;
                if (saved != null && index < saved.Count) c = pop.GetById(saved[index]);
                if (c == null || used.Contains(c.Id)) c = PickFor(cu.Kind, pop, used);
                index++;
                if (c == null) { _bindings.Add(0); continue; }
                used.Add(c.Id);
                _bindings.Add(c.Id);
                cu.BindCitizen(c.Id, c.Name, DescribeShort(c));
            }
        }

        private static Citizen PickFor(int kind, PopulationManager pop, HashSet<int> used)
        {
            CitizenProfession[] wanted;
            switch (kind)
            {
                case CitizenArt.Merchant: wanted = new[] { CitizenProfession.Savdogar }; break;
                case CitizenArt.Farmer: wanted = new[] { CitizenProfession.Dehqon, CitizenProfession.Bogbon }; break;
                case CitizenArt.Craftsman: wanted = new[] { CitizenProfession.Tashuvchi, CitizenProfession.Muallim }; break;
                case CitizenArt.Buyer: wanted = new[] { CitizenProfession.None }; break;
                case CitizenArt.Citizen: wanted = new[] { CitizenProfession.None, CitizenProfession.Amaldor, CitizenProfession.Muallim, CitizenProfession.Soqchi }; break;
                default:
                    // Kasb kiyimidagi figura (konchi, baliqchi, olim ...) — shu kasbdagi fuqaro
                    CitizenProfession p0 = ProfessionLooks.ProfessionOf(kind);
                    wanted = p0 == CitizenProfession.None ? new[] { CitizenProfession.None } : new[] { p0, CitizenProfession.None };
                    break;
            }
            foreach (CitizenProfession p in wanted)
                foreach (Citizen c in pop.Citizens)
                    if (c.Profession == p && !used.Contains(c.Id)) return c;
            foreach (Citizen c in pop.Citizens)
                if (!used.Contains(c.Id)) return c;
            return null;
        }

        /// <summary>Kunlik yangilash: figuraning ismi/tavsifi fuqaroning hozirgi holatiga moslanadi; ketgan fuqaro almashtiriladi.</summary>
        public void RefreshUnits(UnitManager units, PopulationManager pop)
        {
            if (units == null) return;
            var used = new HashSet<int>(_bindings);
            int index = 0;
            foreach (Unit u in units.Units)
            {
                if (!(u is CitizenUnit cu)) continue;
                if (index >= _bindings.Count) _bindings.Add(0);
                Citizen c = pop.GetById(_bindings[index]);
                if (c == null)
                {
                    c = PickFor(cu.Kind, pop, used);
                    _bindings[index] = c?.Id ?? 0;
                    if (c != null) used.Add(c.Id);
                }
                if (c != null) cu.BindCitizen(c.Id, c.Name, DescribeShort(c));
                index++;
            }
        }

        public static string DescribeShort(Citizen c)
            => PopulationManager.ProfessionName(c.Profession) + ". " + (c.IsEmployed ? "Ishda." : "Ish qidirmoqda.");

        /// <summary>Fuqaroning ko'rinadigan figurasi (bo'lsa).</summary>
        public CitizenUnit UnitOf(int citizenId, UnitManager units)
        {
            if (units == null || citizenId <= 0) return null;
            foreach (Unit u in units.Units)
                if (u is CitizenUnit cu && cu.CitizenId == citizenId) return cu;
            return null;
        }

        // ------------------------------------------------------------------
        // Profil — har safar haqiqiy manbalardan yig'iladi
        // ------------------------------------------------------------------
        public CitizenInteractionProfile BuildProfile(Citizen c, CityLifeManager life, GameStateManager state, BuildingManager buildings)
        {
            if (c == null) return null;
            Household hh = state.Pop.GetHousehold(c.HouseholdId);
            int districtId = life.Districts.DistrictOfCitizen(c);
            var p = new CitizenInteractionProfile
            {
                CitizenId = c.Id,
                DisplayName = c.Name,
                Age = c.Age,
                HouseholdId = c.HouseholdId,
                DistrictId = districtId,
                DistrictName = life.Districts.NameOf(districtId),
                Profession = c.Profession,
                IsEmployed = c.IsEmployed,
                OccupationId = CitizenInteractionProfile.OccupationKey(c.Profession),
                OccupationName = PopulationManager.ProfessionName(c.Profession),
                Happiness = c.Happiness,
                Health = c.Health,
                Education = c.Education,
                Hunger = c.Hunger
            };
            if (hh != null)
            {
                p.FamilyName = hh.FamilyName;
                p.FamilySize = hh.MemberIds.Count;
                p.WorkingMembers = hh.WorkingMembers;
                p.HouseholdSavings = hh.SharedMoney;
                p.HouseholdIncome = hh.LastIncome;
                p.HouseholdExpense = hh.LastExpense;
                p.FedRatio = hh.FedRatio;
            }

            Building home = c.HomeBuildingId.HasValue ? NPCConcernAnalyzer.FindBuilding(buildings, c.HomeBuildingId.Value) : null;
            p.HomeBuildingId = home?.Id ?? 0;
            p.HomeName = home != null ? "Uy, " + p.DistrictName : "Uysiz";

            Building work = c.WorkplaceBuildingId.HasValue ? NPCConcernAnalyzer.FindBuilding(buildings, c.WorkplaceBuildingId.Value) : null;
            p.WorkplaceBuildingId = work?.Id ?? 0;
            p.WorkplaceName = work != null ? work.Name : "Ish joyi yo'q";

            p.ActiveConcernIds = NPCConcernAnalyzer.Analyze(c, life, state, buildings);
            p.CurrentActivity = Activity(c, work, p);
            p.Merchant = life.Economy.Merchants.GetMerchant(c, life.Economy.Prices);
            p.PersonalityTrait = NPCPersonality.For(c).DominantTraitUz;

            // Ishonch: suhbatlashilgan fuqaroda — xotirasidagi munosabat; aks holda baxtdan kelib chiqadi.
            if (_memories.TryGetValue(c.Id, out NPCMemory mem))
            {
                p.TrustInGovernment = mem.Trust;
                p.TrustLabel = mem.TrustLabelUz;
            }
            else
            {
                p.TrustInGovernment = Math.Clamp(c.Happiness * 0.7f + 12f, 0f, 100f);
                p.TrustLabel = new NPCMemory { Trust = p.TrustInGovernment }.TrustLabelUz;
            }
            return p;
        }

        private static string Activity(Citizen c, Building work, CitizenInteractionProfile p)
        {
            if (p.FedRatio < 0.45f) return "Oilasiga yegulik qidirmoqda";
            if (!c.IsEmployed) return "Ish qidirmoqda";
            switch (c.Profession)
            {
                case CitizenProfession.Bogbon: return "Bog'da hosilga qaramoqda";
                case CitizenProfession.Savdogar: return "Rastasida savdo qilmoqda";
                case CitizenProfession.Tashuvchi: return "Karvon yuklarini tashimoqda";
                case CitizenProfession.Soqchi: return "Navbatchilikda";
                case CitizenProfession.Amaldor: return "Devonda ish yuritmoqda";
                case CitizenProfession.Muallim: return "Shogirdlarga saboq bermoqda";
                default: return work != null ? work.Name + "da ishlamoqda" : "Ishlamoqda";
            }
        }

        // ------------------------------------------------------------------
        // Save / Load
        // ------------------------------------------------------------------
        public List<NPCMemory> SnapshotMemories() => new List<NPCMemory>(_memories.Values);

        public void RestoreMemories(List<NPCMemory> saved, PopulationManager pop)
        {
            _memories.Clear();
            if (saved == null) return;
            foreach (NPCMemory m in saved)
            {
                if (m == null || pop.GetById(m.CitizenId) == null) continue; // ketgan fuqaro xotirasi kerak emas
                m.Events ??= new List<NPCMemoryEvent>();
                m.DialogueLastDay ??= new Dictionary<string, int>();
                _memories[m.CitizenId] = m;
            }
        }
    }
}
