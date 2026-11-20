using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Economy;
using AndijonSaltanatlar.Population;
using Microsoft.Xna.Framework;
using Xunit;

namespace AndijonSaltanatlar.Tests
{
    /// <summary>Kengaytirilgan kasblar: ish o'rinlari, iqtisodiy ta'sir, ehtiyojlar, ko'rinish va yangi binolar.</summary>
    public sealed class ProfessionSystemTests
    {
        private static List<Building> City(params BuildingType[] types)
        {
            var list = new List<Building>();
            int id = 1;
            foreach (BuildingType t in types)
            {
                Building b = BuildingFactory.Create(t, new Point(id * 6, 10));
                b.Id = id++;
                list.Add(b);
            }
            return list;
        }

        [Fact]
        public void EveryRequestedProfession_GetsJobsInItsBuilding()
        {
            var city = City(BuildingType.Farm, BuildingType.Market, BuildingType.Caravanserai, BuildingType.MainHall, BuildingType.Madrasa,
                            BuildingType.Workshop, BuildingType.Mine, BuildingType.Fishery, BuildingType.Clinic, BuildingType.Barracks);
            var pop = new PopulationManager();
            pop.AddNewcomers(90);
            pop.AssignJobs(city);
            CitizenProfession[] requested =
            {
                CitizenProfession.Dehqon, CitizenProfession.Chorvador, CitizenProfession.Hunarmand, CitizenProfession.Quruvchi,
                CitizenProfession.Konchi, CitizenProfession.Baliqchi, CitizenProfession.Savdogar, CitizenProfession.Tashuvchi,
                CitizenProfession.Omborchi, CitizenProfession.SoliqYiguvchi, CitizenProfession.BozorEgasi, CitizenProfession.Olim,
                CitizenProfession.Muallim, CitizenProfession.Tabib, CitizenProfession.Memor, CitizenProfession.Amaldor
            };
            foreach (CitizenProfession p in requested)
                Assert.True(pop.CountProfession(p) > 0, PopulationManager.ProfessionName(p) + " ishga joylashmadi");
            // Har bir fuqaro kasbi uning ish joyidagi o'ringa mos
            foreach (Citizen c in pop.Citizens.Where(c => c.IsEmployed))
            {
                Building w = city.Single(b => b.Id == c.WorkplaceBuildingId);
                Assert.Contains(PopulationManager.JobsOf(w.Type), j => j.P == c.Profession);
            }
            Assert.Equal(pop.Workers, Math.Min(90, pop.JobCapacity));
        }

        [Fact]
        public void UnfinishedBuilding_GivesNoJobs_LevelAddsSlots()
        {
            var city = City(BuildingType.Mine, BuildingType.Workshop);
            city[1].BuildProgress = 0.4f;          // ustaxona hali qurilmoqda
            city[0].Upgrade(); city[0].Upgrade();  // kon 3-daraja: 6 + 2 o'rin
            var pop = new PopulationManager();
            pop.AddNewcomers(30);
            pop.AssignJobs(city);
            Assert.Equal(8, pop.CountProfession(CitizenProfession.Konchi));
            Assert.Equal(0, pop.CountProfession(CitizenProfession.Hunarmand));
            Assert.Equal(8, pop.JobCapacity);
            // Eski API (bitta asosiy kasb) o'zgarmagan
            Assert.Equal(6, PopulationManager.JobSlots(BuildingType.Farm, out CitizenProfession farm));
            Assert.Equal(CitizenProfession.Dehqon, farm);
        }

        [Fact]
        public void Jobs_StayStable_AcrossReassignment()
        {
            var city = City(BuildingType.Market, BuildingType.Clinic);
            var pop = new PopulationManager();
            pop.AddNewcomers(10);
            pop.AssignJobs(city);
            var before = pop.Citizens.ToDictionary(c => c.Id, c => (c.Profession, c.WorkplaceBuildingId));
            pop.AssignJobs(city);
            foreach (Citizen c in pop.Citizens) Assert.Equal(before[c.Id], (c.Profession, c.WorkplaceBuildingId));
        }

        [Fact]
        public void EconomyEffects_OfNewProfessions()
        {
            Assert.Equal(20, EconomyManager.ComputeExtraFood(2, 3));
            Assert.Equal(0.25f, EconomyManager.StorageBonus(10), 3);
            Assert.Equal(0.08f, EconomyManager.MarketBonus(1), 3);
            Assert.Equal(0.30f, EconomyManager.TaxCollectorBonus(9), 3);
            Assert.Equal(17, EconomyManager.ComputeGoodsVolume(1, 3));
            Assert.Equal(1f, GameStateManager.ConstructionSpeed(0, 0), 3);
            Assert.Equal(2.12f, GameStateManager.ConstructionSpeed(9, 5), 3);   // 6 quruvchi + 2 me'mor chegarasi
        }

        [Fact]
        public void Needs_Coverage_FromServiceProviders()
        {
            Assert.Equal(0f, PopulationManager.Coverage(0, 100, 18f));
            Assert.Equal(100f, PopulationManager.Coverage(10, 100, 18f));
            Assert.InRange(PopulationManager.Coverage(3, 108, 18f), 49f, 51f);

            var pop = new PopulationManager();
            pop.AddNewcomers(36);
            pop.AssignJobs(City(BuildingType.Clinic, BuildingType.Madrasa));
            pop.ComputeNeeds(foodStock: 360, towers: 0, barracks: 0, clinics: 1, madrasas: 1);
            Assert.Equal(100f, pop.NeedHealth);      // 4 tabib (x2) + 1 shifoxona >= 36/18
            Assert.Equal(100f, pop.NeedEducation);
            Assert.Equal(100f, pop.NeedFood);        // 10 kunlik zaxira
            pop.ComputeNeeds(foodStock: 0, towers: 0, barracks: 0, clinics: 0, madrasas: 0);
            Assert.Equal(0f, pop.NeedFood);
        }

        [Fact]
        public void EveryProfession_HasNameLookAndGroup()
        {
            foreach (CitizenProfession p in Enum.GetValues(typeof(CitizenProfession)))
            {
                if (p == CitizenProfession.None) continue;
                Assert.NotEqual("Ishsiz", PopulationManager.ProfessionName(p));
                Assert.False(string.IsNullOrEmpty(PopulationManager.ProfessionGroup(p)));
                Assert.NotEmpty(ProfessionLooks.LinesOf(p));
            }
            // Kasbning o'z kiyimi bor (dehqon/bog'bon va soqchi bundan mustasno — ular umumiy)
            foreach (CitizenProfession p in new[] { CitizenProfession.Konchi, CitizenProfession.Baliqchi, CitizenProfession.Olim,
                         CitizenProfession.Tabib, CitizenProfession.Memor, CitizenProfession.SoliqYiguvchi, CitizenProfession.BozorEgasi,
                         CitizenProfession.Omborchi, CitizenProfession.Quruvchi, CitizenProfession.Muallim, CitizenProfession.Amaldor,
                         CitizenProfession.Tashuvchi, CitizenProfession.Hunarmand, CitizenProfession.Savdogar, CitizenProfession.Chorvador })
                Assert.Equal(p, ProfessionLooks.ProfessionOf(ProfessionLooks.KindOf(p)));
            Assert.Equal(WorkStyle.Dig, ProfessionLooks.StyleOf(CitizenProfession.Konchi));
            Assert.Equal(WorkStyle.Fish, ProfessionLooks.StyleOf(CitizenProfession.Baliqchi));
        }

        [Fact]
        public void NewBuildings_InCatalog_Factory_AndSaveIdsStable()
        {
            foreach (BuildingType t in new[] { BuildingType.Workshop, BuildingType.Mine, BuildingType.Fishery, BuildingType.Clinic })
            {
                Building b = BuildingFactory.Create(t, new Point(5, 5));
                Assert.Equal(t, b.Type);
                Assert.Equal(new Point(3, 3), b.Size);
                Assert.Contains(BuildingCatalog.Buildable, e => e.Type == t);
                Assert.NotEmpty(PopulationManager.JobsOf(t));
            }
            Assert.Equal("tibbiyot", BuildingCatalog.RequiredTech(BuildingType.Clinic));
            // Saqlashdagi butun son qiymatlari: eskilari o'zgarmagan, yangilari oxirida
            Assert.Equal(10, (int)CitizenProfession.Tashuvchi);
            Assert.Equal(11, (int)CitizenProfession.Chorvador);
            Assert.Equal(12, (int)BuildingType.GladiatorArena);
            Assert.Equal(13, (int)BuildingType.Workshop);
        }
    }
}
