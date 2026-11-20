using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using AndijonSaltanatlar.Server.Data;
using AndijonSaltanatlar.Server.Services;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Shared.War;

namespace AndijonSaltanatlar.Tests
{
    /// <summary>PROMPT 25 — urush tizimi: qo'shin, yollash, AI jangi, hudud transferi, PvP, tinchlik (haqiqiy EF Core + SQLite).</summary>
    public class WarSystemTests : IDisposable
    {
        private sealed class FakeClock : IServerClock
        {
            public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
            public void Advance(double seconds) => UtcNow = UtcNow.AddSeconds(seconds);
        }

        private readonly SqliteConnection _conn;
        private readonly FakeClock _clock = new FakeClock();

        public WarSystemTests()
        {
            _conn = new SqliteConnection("Data Source=:memory:");
            _conn.Open();
            using GameDbContext db = NewDb();
            db.Database.EnsureCreated();
        }

        public void Dispose() => _conn.Dispose();

        private GameDbContext NewDb() => new GameDbContext(new DbContextOptionsBuilder<GameDbContext>().UseSqlite(_conn).Options);
        private static string Id() => Guid.NewGuid().ToString("N");

        private WarService War(GameDbContext db)
        {
            var online = new OnlineGameService(db, _clock);
            return new WarService(online, new KingdomService(online));
        }

        private async Task<Kingdom> Player(string name, int coin, bool oldAccount = true)
        {
            using GameDbContext db = NewDb();
            var u = new AppUser { Username = name, Email = name + "@t", PasswordHash = "", CreatedAt = oldAccount ? _clock.UtcNow.AddDays(-3) : _clock.UtcNow };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            Kingdom k = await new OnlineGameService(db, _clock).JoinAsync(u.Id, new JoinWorldRequest { WorldId = 1 });
            await db.KingdomResources.Where(r => r.KingdomId == k.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, coin));
            return k;
        }

        private async Task<int> Coin(int kid) { using GameDbContext db = NewDb(); return await db.KingdomResources.Where(r => r.KingdomId == kid).Select(r => r.Coin).FirstAsync(); }

        private async Task<WarOverviewDto> Overview(Kingdom k) { using GameDbContext db = NewDb(); return await War(db).OverviewAsync(k); }

        /// <summary>Jangni oxirigacha o'tkazadi: o'yinchi har raundda berilgan qarorni yuboradi (AI o'zi javob beradi).</summary>
        private async Task<BattleDto> FightToEnd(Kingdom me, int battleId, BattleDecision decision)
        {
            for (int i = 0; i < WarRules.MaxRounds + 1; i++)
            {
                BattleDto b;
                using (GameDbContext db = NewDb()) b = await War(db).BattleAsync(me, battleId);
                if (b.Status == BattleStatus.Completed) return b;
                using GameDbContext db2 = NewDb();
                ActionResultDto r = await War(db2).SubmitDecisionAsync(me, new BattleDecisionRequest { CommandId = Id(), BattleId = battleId, Round = b.Round, Decision = decision });
                Assert.True(r.Success, r.MessageUz);
            }
            using GameDbContext db3 = NewDb();
            return await War(db3).BattleAsync(me, battleId);
        }

        // ------------------------------------------------------------------ Qo'shin
        [Fact]
        public async Task Army_DefaultArmyAndCommanderCreated_FacilitiesListed()
        {
            Kingdom me = await Player("asaka", 1000);
            WarOverviewDto o = await Overview(me);
            Assert.Single(o.Armies);
            Assert.Equal(12, o.Armies[0].UnitCount);
            Assert.True(o.Armies[0].Strength > 0);
            Assert.Single(o.Commanders);
            Assert.Equal(o.Armies[0].Id, o.Commanders[0].AssignedArmyId);
            Assert.Equal(WarRules.Facilities.Count, o.Facilities.Count);
            Assert.Contains(o.Targets, t => t.OwnerIsAi);
            // Himoya bahosi haqiqiy (AI qo'shini oldindan mavjud) — 0 emas.
            Assert.All(o.Targets, t => Assert.True(t.EstimatedDefense > 0, t.RegionName + " himoyasi 0"));
        }

        [Fact]
        public async Task Recruit_ChargesOnce_DeliversAfterTime_RespectsCapacityAndRequirements()
        {
            Kingdom me = await Player("asaka", 2000);
            int army = (await Overview(me)).Armies[0].Id;
            string cmd = Id();
            using (GameDbContext db = NewDb())
            {
                ActionResultDto r = await War(db).RecruitAsync(me, new RecruitRequest { CommandId = cmd, ArmyId = army, Line = UnitLine.Piyoda, Count = 10 });
                Assert.True(r.Success, r.MessageUz);
            }
            using (GameDbContext db = NewDb())
            {
                ActionResultDto again = await War(db).RecruitAsync(me, new RecruitRequest { CommandId = cmd, ArmyId = army, Line = UnitLine.Piyoda, Count = 10 });
                Assert.True(again.Duplicate); // takror yuborilgan buyruq qayta bajarilmaydi
            }
            Assert.Equal(2000 - 400, await Coin(me.Id));
            WarOverviewDto o = await Overview(me);
            Assert.Single(o.Queue);
            Assert.Equal(12, o.Armies[0].UnitCount);

            _clock.Advance(WarRules.RecruitMinutes(10) + 1);
            o = await Overview(me);
            Assert.Empty(o.Queue);
            Assert.Equal(22, o.Armies[0].UnitCount);
            o = await Overview(me); // ikkinchi o'qish qayta qo'shmaydi
            Assert.Equal(22, o.Armies[0].UnitCount);

            using (GameDbContext db = NewDb())
            {
                ActionResultDto cav = await War(db).RecruitAsync(me, new RecruitRequest { CommandId = Id(), ArmyId = army, Line = UnitLine.Otliq, Count = 1 });
                Assert.False(cav.Success); // Otxona kerak
                ActionResultDto cap = await War(db).RecruitAsync(me, new RecruitRequest { CommandId = Id(), ArmyId = army, Line = UnitLine.Piyoda, Count = 50 });
                Assert.False(cap.Success); // sig'im 30
            }
        }

        [Fact]
        public async Task Facilities_UnlockUpgrades_CommanderHireAndAssign()
        {
            Kingdom me = await Player("asaka", 5000);
            WarOverviewDto o = await Overview(me);
            int army = o.Armies[0].Id;
            using (GameDbContext db = NewDb())
            {
                Assert.False((await War(db).UpgradeAsync(me, new UpgradeUnitsRequest { CommandId = Id(), ArmyId = army, Line = UnitLine.Piyoda, FromLevel = 1, Count = 5 })).Success);
                Assert.True((await War(db).BuildFacilityAsync(me, new FacilityRequest { CommandId = Id(), Kind = FacilityKind.Mashgulot })).Success);
                Assert.True((await War(db).UpgradeAsync(me, new UpgradeUnitsRequest { CommandId = Id(), ArmyId = army, Line = UnitLine.Piyoda, FromLevel = 1, Count = 5 })).Success);
                Assert.True((await War(db).HireCommanderAsync(me, new CommanderRequest { CommandId = Id() })).Success);
            }
            o = await Overview(me);
            Assert.Equal(5, o.Armies[0].Groups.Single(g => g.Line == UnitLine.Piyoda && g.Level == 2).Count);
            Assert.Equal(5, o.Armies[0].Groups.Single(g => g.Line == UnitLine.Piyoda && g.Level == 1).Count);
            Assert.Equal(12, o.Armies[0].UnitCount); // birliklar soni o'zgarmadi
            Assert.Equal(2, o.Commanders.Count);
            int newCmd = o.Commanders[1].Id;
            using (GameDbContext db = NewDb())
                Assert.True((await War(db).AssignCommanderAsync(me, new CommanderRequest { CommandId = Id(), CommanderId = newCmd, ArmyId = army })).Success);
            o = await Overview(me);
            Assert.Equal(newCmd, o.Armies[0].CommanderId);
            Assert.Equal(0, o.Commanders.Single(c => c.Id != newCmd).AssignedArmyId);
        }

        // ------------------------------------------------------------------ AI jangi + hudud
        [Fact]
        public async Task AiBattle_ServerResolvesRounds_CompletesOnce_TransfersRegionWithCitiesAndPopulation()
        {
            Kingdom me = await Player("asaka", 20000);
            WarOverviewDto o = await Overview(me);
            int army = o.Armies[0].Id;
            // Kuchli qo'shin: otxona + otliqlar (resurslar haqiqiy yechiladi).
            using (GameDbContext db = NewDb())
            {
                var w = War(db);
                Assert.True((await w.BuildFacilityAsync(me, new FacilityRequest { CommandId = Id(), Kind = FacilityKind.Qarorgoh })).Success);
                Assert.True((await w.BuildFacilityAsync(me, new FacilityRequest { CommandId = Id(), Kind = FacilityKind.Qarorgoh })).Success);
                Assert.True((await w.BuildFacilityAsync(me, new FacilityRequest { CommandId = Id(), Kind = FacilityKind.Otxona })).Success);
                Assert.True((await w.RecruitAsync(me, new RecruitRequest { CommandId = Id(), ArmyId = army, Line = UnitLine.Otliq, Count = 40 })).Success);
                Assert.True((await w.RecruitAsync(me, new RecruitRequest { CommandId = Id(), ArmyId = army, Line = UnitLine.Piyoda, Count = 40 })).Success);
            }
            _clock.Advance(200);
            o = await Overview(me);
            WarTargetDto target = o.Targets.First(t => t.CanAttack);
            int oldOwner = target.OwnerKingdomId;

            int totalPopBefore, cityId, cityPop;
            using (GameDbContext db = NewDb())
            {
                totalPopBefore = await db.Cities.SumAsync(c => c.Population);
                CityEntity c = await db.Cities.FirstAsync(x => x.RegionId == target.RegionId);
                cityId = c.Id; cityPop = c.Population;
            }
            int coinBefore = await Coin(me.Id);
            int battleId;
            using (GameDbContext db = NewDb())
            {
                var (res, id) = await War(db).StartAiBattleAsync(me, new StartBattleRequest { CommandId = Id(), ArmyId = army, RegionId = target.RegionId });
                Assert.True(res.Success, res.MessageUz);
                battleId = id;
            }
            Assert.Equal(coinBefore - WarRules.ConflictCost, await Coin(me.Id));

            // Noto'g'ri raund va ikkinchi faol jang rad etiladi.
            using (GameDbContext db = NewDb())
            {
                Assert.False((await War(db).SubmitDecisionAsync(me, new BattleDecisionRequest { CommandId = Id(), BattleId = battleId, Round = 3, Decision = BattleDecision.Faol })).Success);
                var (second, _) = await War(db).StartAiBattleAsync(me, new StartBattleRequest { CommandId = Id(), ArmyId = army, RegionId = target.RegionId });
                Assert.False(second.Success);
            }

            BattleDto done = await FightToEnd(me, battleId, BattleDecision.Faol);
            Assert.Equal(BattleStatus.Completed, done.Status);
            Assert.Equal(me.Id, done.WinnerKingdomId);
            Assert.True(done.TerritoryTransferred);
            Assert.True(done.Rounds.Count >= 1 && done.Rounds.Count <= WarRules.MaxRounds);

            using (GameDbContext db = NewDb())
            {
                Assert.Equal(me.Id, (await db.Regions.SingleAsync(r => r.Id == target.RegionId)).OwnerKingdomId);
                CityEntity c = await db.Cities.SingleAsync(x => x.Id == cityId);
                Assert.Equal(me.Id, c.KingdomId);              // shahar saqlandi va yangi saltanatga o'tdi
                Assert.Equal(cityPop, c.Population);             // aholi yo'qolmadi
                Assert.Equal(totalPopBefore, await db.Cities.SumAsync(x => x.Population)); // ikki marta sanalmadi
                Assert.Equal(1, await db.BattleRounds.CountAsync(r => r.BattleId == battleId && r.Round == 1));
                // Qayta "yakunlash" urinishlari hech narsani o'zgartirmaydi.
                Assert.False((await War(db).SubmitDecisionAsync(me, new BattleDecisionRequest { CommandId = Id(), BattleId = battleId, Round = done.Round, Decision = BattleDecision.Faol })).Success);
            }
            WarOverviewDto after = await Overview(me);
            Assert.Null(after.ActiveBattle);
            Assert.Contains(after.History, h => h.Id == battleId);
            Assert.Equal(ArmyStatus.Recovering, after.Armies[0].Status);
            Assert.True(after.Commanders[0].Experience > 0 || after.Commanders[0].Level > 1);
            Assert.Contains(after.AdviceUz, a => a.Contains("saltanatimizga qo'shildi"));

            // Server "qayta ishga tushdi": yangi kontekst — natija saqlangan, hudud qaytib ketmagan.
            using (GameDbContext db = NewDb())
            {
                BattleDto again = await War(db).BattleAsync(me, battleId);
                Assert.Equal(BattleStatus.Completed, again.Status);
                Assert.Equal(me.Id, (await db.Regions.SingleAsync(r => r.Id == target.RegionId)).OwnerKingdomId);
                Assert.NotEqual(oldOwner, me.Id);
            }
        }

        [Fact]
        public async Task Recruits_ArrivingDuringBattle_AreNotLostInThatBattle()
        {
            Kingdom me = await Player("asaka", 5000);
            WarOverviewDto o = await Overview(me);
            int army = o.Armies[0].Id;
            using (GameDbContext db = NewDb())
                Assert.True((await War(db).RecruitAsync(me, new RecruitRequest { CommandId = Id(), ArmyId = army, Line = UnitLine.Piyoda, Count = 10 })).Success);
            WarTargetDto target = o.Targets.First(t => t.CanAttack);
            int battleId;
            using (GameDbContext db = NewDb())
            {
                var (res, id) = await War(db).StartAiBattleAsync(me, new StartBattleRequest { CommandId = Id(), ArmyId = army, RegionId = target.RegionId });
                Assert.True(res.Success, res.MessageUz);
                battleId = id;
            }
            _clock.Advance(WarRules.RecruitMinutes(10) + 1);           // yollash jang paytida tayyor bo'ldi
            Assert.Single((await Overview(me)).Queue);                  // jang tugaguncha qo'shilmaydi
            BattleDto done = await FightToEnd(me, battleId, BattleDecision.Faol);
            Assert.Equal(BattleStatus.Completed, done.Status);
            WarOverviewDto after = await Overview(me);
            Assert.Empty(after.Queue);
            // Jangda 12 ta birlik qatnashgan; yangi 10 tasi yo'qotishsiz qo'shildi.
            int piyoda = after.Armies[0].Groups.Where(g => g.Line == UnitLine.Piyoda).Sum(g => g.Count);
            Assert.True(piyoda >= 10, "yangi birliklar jangda yo'qolgan: " + piyoda);
        }

        [Fact]
        public async Task Battle_BothNegotiate_EndsInPeaceWithoutTransfer()
        {
            var o = WarRules.ResolveRound(42, 1, 500, 500, 70, BattleDecision.Muzokara, 5, 500, 500, 70, BattleDecision.Muzokara, 5, 0);
            Assert.True(o.Finished);
            Assert.Equal(0, o.Winner);
            // Deterministik: bir xil kirish → bir xil natija.
            var a = WarRules.ResolveRound(7, 2, 800, 900, 60, BattleDecision.Faol, 6, 700, 900, 65, BattleDecision.Ehtiyotkor, 6, 10);
            var b = WarRules.ResolveRound(7, 2, 800, 900, 60, BattleDecision.Faol, 6, 700, 900, 65, BattleDecision.Ehtiyotkor, 6, 10);
            Assert.Equal(a.AttackerLoss, b.AttackerLoss);
            Assert.Equal(a.DefenderLoss, b.DefenderLoss);
        }

        [Fact]
        public async Task Peace_WithAiAfterConflict_CreatesTruceThatBlocksAttack()
        {
            Kingdom me = await Player("asaka", 5000);
            WarOverviewDto o = await Overview(me);
            WarTargetDto target = o.Targets.First(t => t.CanAttack);
            int battleId;
            using (GameDbContext db = NewDb())
            {
                var (res, id) = await War(db).StartAiBattleAsync(me, new StartBattleRequest { CommandId = Id(), ArmyId = o.Armies[0].Id, RegionId = target.RegionId });
                Assert.True(res.Success, res.MessageUz);
                battleId = id;
            }
            await FightToEnd(me, battleId, BattleDecision.Ehtiyotkor);
            // Elchilar bilan munosabatni tiklash (tinchlik shartlarini yaratish).
            using (GameDbContext db = NewDb())
            {
                var online = new OnlineGameService(db, _clock);
                await new KingdomService(online).AdjustRelationAsync(1, me.Id, target.OwnerKingdomId, 60);
            }
            using (GameDbContext db = NewDb())
            {
                ActionResultDto p = await War(db).ProposePeaceAsync(me, new PeaceRequest { CommandId = Id(), KingdomId = target.OwnerKingdomId });
                Assert.True(p.Success, p.MessageUz);
            }
            o = await Overview(me);
            Assert.Contains(o.Conflicts, c => c.KingdomId == target.OwnerKingdomId && !c.Active && c.TruceMinutesLeft > 0);
            Assert.False(o.Targets.First(t => t.OwnerKingdomId == target.OwnerKingdomId).CanAttack);
        }

        // ------------------------------------------------------------------ PvP
        [Fact]
        public async Task Pvp_InviteAcceptBothDecide_SameResultForBoth_TimeoutAutoDecides()
        {
            Kingdom pc = await Player("pc", 3000);
            Kingdom phone = await Player("telefon", 3000);
            int pcArmy = (await Overview(pc)).Armies[0].Id;
            WarOverviewDto ph = await Overview(phone);
            int phoneArmy = ph.Armies[0].Id;

            using (GameDbContext db = NewDb())
                Assert.True((await War(db).InviteAsync(pc, new PvpInviteRequest { CommandId = Id(), ToKingdomId = phone.Id, ArmyId = pcArmy })).Success);
            ph = await Overview(phone);
            BattleInvitationDto inv = Assert.Single(ph.Invitations);
            Assert.True(inv.Incoming);

            int battleId;
            using (GameDbContext db = NewDb())
            {
                var (res, id) = await War(db).RespondAsync(phone, new PvpRespondRequest { CommandId = Id(), InvitationId = inv.Id, Accept = true, ArmyId = phoneArmy });
                Assert.True(res.Success, res.MessageUz);
                battleId = id;
            }
            // Ikkalasi bitta BattleId ni ko'radi.
            Assert.Equal(battleId, (await Overview(pc)).ActiveBattle.Id);
            Assert.Equal(battleId, (await Overview(phone)).ActiveBattle.Id);

            // 1-raund: ikkala qaror → server hisoblaydi.
            using (GameDbContext db = NewDb())
            {
                Assert.True((await War(db).SubmitDecisionAsync(pc, new BattleDecisionRequest { CommandId = Id(), BattleId = battleId, Round = 1, Decision = BattleDecision.Faol })).Success);
                Assert.False((await War(db).SubmitDecisionAsync(pc, new BattleDecisionRequest { CommandId = Id(), BattleId = battleId, Round = 1, Decision = BattleDecision.Ehtiyotkor })).Success); // takror qaror
            }
            using (GameDbContext db = NewDb())
                Assert.True((await War(db).SubmitDecisionAsync(phone, new BattleDecisionRequest { CommandId = Id(), BattleId = battleId, Round = 1, Decision = BattleDecision.Muvozanatli })).Success);
            BattleDto b1;
            using (GameDbContext db = NewDb()) b1 = await War(db).BattleAsync(pc, battleId);
            Assert.True(b1.Rounds.Count >= 1);

            // Telefon javob bermay qoldi: vaqt tugagach server avtomatik qaror qiladi; jang oxiriga yetadi.
            for (int i = 0; i < WarRules.MaxRounds + 1; i++)
            {
                BattleDto cur;
                using (GameDbContext db = NewDb()) cur = await War(db).BattleAsync(pc, battleId);
                if (cur.Status == BattleStatus.Completed) break;
                using (GameDbContext db = NewDb())
                    await War(db).SubmitDecisionAsync(pc, new BattleDecisionRequest { CommandId = Id(), BattleId = battleId, Round = cur.Round, Decision = BattleDecision.Muvozanatli });
                _clock.Advance(WarRules.DecisionTimeoutSeconds + 1);
            }
            BattleDto forPc, forPhone;
            using (GameDbContext db = NewDb()) forPc = await War(db).BattleAsync(pc, battleId);
            using (GameDbContext db = NewDb()) forPhone = await War(db).BattleAsync(phone, battleId);
            Assert.Equal(BattleStatus.Completed, forPc.Status);
            Assert.Equal(forPc.WinnerKingdomId, forPhone.WinnerKingdomId);
            Assert.Equal(forPc.AttackerPower, forPhone.AttackerPower);
            Assert.False(forPc.TerritoryTransferred); // garovsiz jang — hudud o'zgarmaydi
        }

        [Fact]
        public async Task Pvp_ProtectionDeclineAndOptOut_NoLossWithoutConsent()
        {
            Kingdom pc = await Player("pc", 3000);
            Kingdom newbie = await Player("yangi", 3000, oldAccount: false);
            Kingdom phone = await Player("telefon", 3000);
            int pcArmy = (await Overview(pc)).Armies[0].Id;
            using (GameDbContext db = NewDb())
            {
                var w = War(db);
                Assert.False((await w.InviteAsync(pc, new PvpInviteRequest { CommandId = Id(), ToKingdomId = newbie.Id, ArmyId = pcArmy })).Success); // himoya
                // Yangi o'yinchi o'zi PvP ni yoqsa — himoyadan ixtiyoriy voz kechadi va taklif qabul qilinadi.
                Assert.True((await w.SetPvpAsync(newbie, new PvpSettingRequest { CommandId = Id(), AcceptsPvp = true })).Success);
                Assert.True((await w.InviteAsync(pc, new PvpInviteRequest { CommandId = Id(), ToKingdomId = newbie.Id, ArmyId = pcArmy })).Success);
                Assert.True((await w.InviteAsync(pc, new PvpInviteRequest { CommandId = Id(), ToKingdomId = phone.Id, ArmyId = pcArmy })).Success);
                Assert.False((await w.InviteAsync(pc, new PvpInviteRequest { CommandId = Id(), ToKingdomId = phone.Id, ArmyId = pcArmy })).Success); // takror taklif
            }
            int phoneRegions;
            using (GameDbContext db = NewDb()) phoneRegions = await db.Regions.CountAsync(r => r.OwnerKingdomId == phone.Id);
            WarOverviewDto ph = await Overview(phone);
            using (GameDbContext db = NewDb())
            {
                var (res, _) = await War(db).RespondAsync(phone, new PvpRespondRequest { CommandId = Id(), InvitationId = ph.Invitations[0].Id, Accept = false });
                Assert.True(res.Success);
                Assert.True((await War(db).SetPvpAsync(phone, new PvpSettingRequest { CommandId = Id(), AcceptsPvp = false })).Success);
            }
            using (GameDbContext db = NewDb())
            {
                Assert.Equal(phoneRegions, await db.Regions.CountAsync(r => r.OwnerKingdomId == phone.Id));
                Assert.Equal(0, await db.Battles.CountAsync());
                Assert.False((await War(db).InviteAsync(pc, new PvpInviteRequest { CommandId = Id(), ToKingdomId = phone.Id, ArmyId = pcArmy })).Success); // o'chirilgan
            }
        }

        // ------------------------------------------------------------------ Xarita: yurish va qamal
        private async Task<int> StrongArmy(Kingdom me)
        {
            int army = (await Overview(me)).Armies[0].Id;
            using (GameDbContext db = NewDb())
            {
                var w = War(db);
                Assert.True((await w.BuildFacilityAsync(me, new FacilityRequest { CommandId = Id(), Kind = FacilityKind.Qarorgoh })).Success);
                Assert.True((await w.BuildFacilityAsync(me, new FacilityRequest { CommandId = Id(), Kind = FacilityKind.Qarorgoh })).Success);
                Assert.True((await w.BuildFacilityAsync(me, new FacilityRequest { CommandId = Id(), Kind = FacilityKind.Otxona })).Success);
                Assert.True((await w.RecruitAsync(me, new RecruitRequest { CommandId = Id(), ArmyId = army, Line = UnitLine.Otliq, Count = 45 })).Success);
                Assert.True((await w.RecruitAsync(me, new RecruitRequest { CommandId = Id(), ArmyId = army, Line = UnitLine.Piyoda, Count = 40 })).Success);
            }
            _clock.Advance(200);
            await Overview(me);
            return army;
        }

        private async Task TickWorld() { using GameDbContext db = NewDb(); await War(db).TickWorldAsync(1); }

        [Fact]
        public async Task March_ToAiCastle_ArrivesOnServerTick_BattleThenTerritory()
        {
            Kingdom me = await Player("asaka", 30000);
            int army = await StrongArmy(me);
            WarOverviewDto o = await Overview(me);
            WarMapRegionDto castle = o.MapRegions.First(r => !r.IsMine && r.OwnerIsAi && r.OwnerKingdomId != 0 && !r.IsCapital && r.CanMarch);
            using (GameDbContext db = NewDb())
            {
                ActionResultDto m = await War(db).MarchAsync(me, new MarchRequest { CommandId = Id(), ArmyId = army, RegionId = castle.RegionId });
                Assert.True(m.Success, m.MessageUz);
            }
            o = await Overview(me);
            MarchDto march = Assert.Single(o.Marches);
            Assert.Equal(castle.RegionId, march.TargetRegionId);
            Assert.Equal(ArmyStatus.Traveling, o.Armies[0].Status);
            await TickWorld();
            Assert.Null((await Overview(me)).ActiveBattle);          // hali yetib kelmadi

            _clock.Advance(castle.MarchSeconds + 1);
            await TickWorld();                                        // server tiki: yetib keldi → jang
            o = await Overview(me);
            Assert.Empty(o.Marches);
            Assert.NotNull(o.ActiveBattle);
            Assert.Equal(castle.RegionId, o.ActiveBattle.RegionId);
            BattleDto done = await FightToEnd(me, o.ActiveBattle.Id, BattleDecision.Faol);
            Assert.Equal(me.Id, done.WinnerKingdomId);
            Assert.True(done.TerritoryTransferred);
            using GameDbContext db2 = NewDb();
            Assert.Equal(me.Id, (await db2.Regions.SingleAsync(r => r.Id == castle.RegionId)).OwnerKingdomId);
        }

        [Fact]
        public async Task March_ToPlayerCapital_OfflineDefenderGarrisonFights_WinnerTakesPlunderNotCapital()
        {
            Kingdom pc = await Player("pc", 30000);
            Kingdom phone = await Player("telefon", 5000);
            int army = await StrongArmy(pc);
            using (GameDbContext db = NewDb())
                Assert.True((await War(db).SetPvpAsync(phone, new PvpSettingRequest { CommandId = Id(), AcceptsPvp = true })).Success);
            // Telefon qo'shini boshqa joyda band (tiklanmoqda) — qasrni garnizon himoya qiladi.
            using (GameDbContext db = NewDb())
                await db.Armies.Where(a => a.KingdomId == phone.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, (int)ArmyStatus.Recovering).SetProperty(a => a.StatusUntil, _clock.UtcNow.AddHours(1)));
            WarOverviewDto o = await Overview(pc);
            WarMapRegionDto capital = o.MapRegions.Single(r => r.OwnerKingdomId == phone.Id);
            Assert.True(capital.IsCapital);
            Assert.True(capital.CanMarch, capital.BlockReasonUz);
            using (GameDbContext db = NewDb())
                Assert.True((await War(db).MarchAsync(pc, new MarchRequest { CommandId = Id(), ArmyId = army, RegionId = capital.RegionId })).Success);
            WarOverviewDto ph = await Overview(phone);
            Assert.Contains(ph.Marches, m => m.TargetIsMine);         // himoyachi xaritada kelayotgan qo'shinni ko'radi

            _clock.Advance(capital.MarchSeconds + 1);
            await TickWorld();
            BattleDto b = (await Overview(pc)).ActiveBattle;
            Assert.NotNull(b);
            Assert.True(b.IsPvp);
            Assert.Contains(b.DefenderGroups, g => g.Line == UnitLine.Piyoda); // garnizon
            int phoneCoinBefore = await Coin(phone.Id);
            // Telefon offline: har raund vaqti tugaganda server uning sarkardasi nomidan qaror qiladi.
            for (int i = 0; i < WarRules.MaxRounds + 1; i++)
            {
                BattleDto cur;
                using (GameDbContext db = NewDb()) cur = await War(db).BattleAsync(pc, b.Id);
                if (cur.Status == BattleStatus.Completed) break;
                using (GameDbContext db = NewDb())
                    await War(db).SubmitDecisionAsync(pc, new BattleDecisionRequest { CommandId = Id(), BattleId = b.Id, Round = cur.Round, Decision = BattleDecision.Faol });
                _clock.Advance(WarRules.DecisionTimeoutSeconds + 1);
                await TickWorld();
            }
            BattleDto done;
            using (GameDbContext db = NewDb()) done = await War(db).BattleAsync(phone, b.Id);
            Assert.Equal(BattleStatus.Completed, done.Status);
            Assert.Equal(pc.Id, done.WinnerKingdomId);
            Assert.False(done.TerritoryTransferred);                 // poytaxt egasida qoladi
            int plunder = Math.Min(WarRules.PlunderMax, phoneCoinBefore * WarRules.PlunderPercent / 100);
            Assert.Equal(phoneCoinBefore - plunder, await Coin(phone.Id));
            using GameDbContext db2 = NewDb();
            Assert.Equal(phone.Id, (await db2.Regions.SingleAsync(r => r.Id == capital.RegionId)).OwnerKingdomId);
        }

        [Fact]
        public async Task March_ToProtectedPlayer_Blocked()
        {
            Kingdom pc = await Player("pc", 30000);
            Kingdom fresh = await Player("yangi", 1000, oldAccount: false);
            int army = (await Overview(pc)).Armies[0].Id;
            WarOverviewDto o = await Overview(pc);
            WarMapRegionDto r = o.MapRegions.Single(x => x.OwnerKingdomId == fresh.Id);
            Assert.False(r.CanMarch);
            using GameDbContext db = NewDb();
            Assert.False((await War(db).MarchAsync(pc, new MarchRequest { CommandId = Id(), ArmyId = army, RegionId = r.RegionId })).Success);
        }

        [Fact]
        public async Task Upkeep_ChargedOncePerGameDay()
        {
            Kingdom me = await Player("asaka", 1000);
            WarOverviewDto o = await Overview(me);
            int upkeep = o.DailyUpkeep;
            Assert.True(upkeep > 0);
            _clock.Advance(24 * 60 + 5); // 1 o'yin kuni = 1440 real soniya
            await Overview(me);
            await Overview(me);
            Assert.Equal(1000 - upkeep, await Coin(me.Id));
        }
    
        // ------------------------------------------------------------------ Qasrni kuchaytirish va AI hujumi (onlayn)

        /// <summary>AI qasrini to'g'ridan-to'g'ri o'yinchiga beradi (egallangan qasr) — sinov uchun.</summary>
        private async Task<int> GiveCastle(Kingdom me)
        {
            WarMapRegionDto castle = (await Overview(me)).MapRegions.First(r => !r.IsMine && r.OwnerIsAi && r.OwnerKingdomId != 0 && !r.IsCapital);
            using GameDbContext db = NewDb();
            await db.Regions.Where(r => r.Id == castle.RegionId).ExecuteUpdateAsync(s => s.SetProperty(r => r.OwnerKingdomId, me.Id));
            return castle.RegionId;
        }

        private async Task<ActionResultDto> Fortify(Kingdom me, int regionId)
        {
            using GameDbContext db = NewDb();
            return await War(db).FortifyRegionAsync(me, new FortifyRequest { CommandId = Id(), RegionId = regionId });
        }

        [Fact]
        public async Task Fortify_ConqueredCastle_CostsCoins_UpToMax_CapitalAndForeignRefused()
        {
            Kingdom me = await Player("asaka", 5000);
            int castle = await GiveCastle(me);
            WarMapRegionDto m = (await Overview(me)).MapRegions.Single(r => r.RegionId == castle);
            Assert.True(m.IsMine && m.CanFortify);
            Assert.Equal(0, m.FortifyLevel);
            int spent = 0;
            for (int lv = 0; lv < WarRules.MaxFortify; lv++)
            {
                Assert.True((await Fortify(me, castle)).Success);
                spent += WarRules.FortifyCost(lv);
            }
            Assert.Equal(5000 - spent, await Coin(me.Id));
            m = (await Overview(me)).MapRegions.Single(r => r.RegionId == castle);
            Assert.Equal(WarRules.MaxFortify, m.FortifyLevel);
            Assert.False(m.CanFortify);
            Assert.False((await Fortify(me, castle)).Success);                       // 5 dan oshmaydi
            int capital = (await Overview(me)).MapRegions.Single(r => r.IsMine && r.IsCapital).RegionId;
            Assert.False((await Fortify(me, capital)).Success);                       // poytaxt — shahar ichida
            int foreign = (await Overview(me)).MapRegions.First(r => !r.IsMine && r.OwnerKingdomId != 0).RegionId;
            Assert.False((await Fortify(me, foreign)).Success);
            Assert.Equal(5000 - spent, await Coin(me.Id));                            // rad etilganda tanga yechilmaydi
        }

        [Fact]
        public async Task Fortify_RaisesDefenseSeenByOthers()
        {
            Kingdom me = await Player("asaka", 5000);
            Kingdom other = await Player("pc", 1000);
            int castle = await GiveCastle(me);
            int before = (await Overview(other)).MapRegions.Single(r => r.RegionId == castle).DefenseEstimate;
            for (int i = 0; i < 3; i++) Assert.True((await Fortify(me, castle)).Success);
            int after = (await Overview(other)).MapRegions.Single(r => r.RegionId == castle).DefenseEstimate;
            Assert.True(after > before, before + " -> " + after);
        }

        [Fact]
        public async Task AiAttack_OnlinePlayer_WarningMarch_ThenBattleWithFortifyBonus()
        {
            Kingdom me = await Player("asaka", 5000);
            int castle = await GiveCastle(me);
            for (int i = 0; i < 3; i++) Assert.True((await Fortify(me, castle)).Success);

            using (GameDbContext db = NewDb())
            {
                WarService w = War(db);
                w.IsPlayerOnline = _ => true;
                await w.AiAggressionAsync(1);                                        // birinchi ko'rish: jadval tuziladi
                Assert.DoesNotContain(w.Events, e => e.Name == "ArmyMarching");
            }
            Assert.Empty((await Overview(me)).Marches.Where(x => x.TargetIsMine));
            _clock.Advance(WarRules.AiAttackFirstDelaySeconds + 1);
            using (GameDbContext db = NewDb())
            {
                WarService w = War(db);
                w.IsPlayerOnline = _ => true;
                await w.AiAggressionAsync(1);
                WarEvent ev = Assert.Single(w.Events, e => e.Name == "ArmyMarching");
                Assert.Equal(me.Id, (int)ev.Args[2]);                                 // himoyachi — o'yinchi
            }
            WarOverviewDto o = await Overview(me);
            MarchDto march = Assert.Single(o.Marches, x => x.TargetIsMine);
            Assert.False(march.IsMine);
            Assert.True(march.SecondsLeft >= WarRules.AiMarchMinSeconds - 1);
            using (GameDbContext db = NewDb())
            {
                WarService w = War(db);
                w.IsPlayerOnline = _ => true;
                _clock.Advance(WarRules.AiAttackGapSeconds(true, 99) + 1);
                await w.AiAggressionAsync(1);                                        // bir vaqtda bitta yurish
                Assert.DoesNotContain(w.Events, e => e.Name == "ArmyMarching");
            }

            _clock.Advance(march.SecondsLeft + 1);
            await TickWorld();                                                        // yetib keldi → jang
            o = await Overview(me);
            Assert.NotNull(o.ActiveBattle);
            using GameDbContext db2 = NewDb();
            var b = await db2.Battles.SingleAsync(x => x.Id == o.ActiveBattle.Id);
            Assert.Equal(me.Id, b.DefenderKingdomId);
            Assert.False(b.IsPvp);                                                    // AI hujumi — PvP emas
            int fort = await db2.Regions.Where(r => r.Id == b.RegionId).Select(r => r.Fortify).FirstAsync();
            int mudofaa = await db2.MilitaryFacilities.Where(f => f.KingdomId == me.Id && f.Kind == (int)FacilityKind.Mudofaa).Select(f => f.Level).FirstOrDefaultAsync();
            Assert.Equal(mudofaa * 8 + WarRules.FortifyDefensePct(fort), b.DefenseBonusPct);
            Assert.True(await db2.BattleDecisions.AnyAsync(d => d.BattleId == b.Id && d.KingdomId == b.AttackerKingdomId));   // AI qarori bor
        }

        [Fact]
        public async Task AiAttack_SkipsOfflineAndNewPlayers()
        {
            Kingdom offline = await Player("uxlayotgan", 1000);
            Kingdom newbie = await Player("yangi", 1000, oldAccount: false);
            for (int step = 0; step < 2; step++)
            {
                using (GameDbContext db = NewDb())
                {
                    WarService w = War(db);
                    w.IsPlayerOnline = k => k.Id == newbie.Id;                         // faqat yangi o'yinchi onlayn
                    await w.AiAggressionAsync(1);
                    Assert.DoesNotContain(w.Events, e => e.Name == "ArmyMarching");
                }
                _clock.Advance(WarRules.AiAttackFirstDelaySeconds + 1);
            }
            Assert.Empty((await Overview(offline)).Marches.Where(x => x.TargetIsMine));
            Assert.Empty((await Overview(newbie)).Marches.Where(x => x.TargetIsMine));
        }
    }
}
