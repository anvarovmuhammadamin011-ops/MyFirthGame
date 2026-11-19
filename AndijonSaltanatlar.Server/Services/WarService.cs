using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AndijonSaltanatlar.Server.Data;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Shared.Farming;
using AndijonSaltanatlar.Shared.War;

namespace AndijonSaltanatlar.Server.Services
{
    /// <summary>Tranzaksiya tugagach dunyoga yuboriladigan real vaqt xabari.</summary>
    public sealed class WarEvent
    {
        public string Name = "";
        public int WorldId;
        public object[] Args = Array.Empty<object>();
    }

    /// <summary>
    /// PROMPT 25 — server-authoritative strategik urush tizimi.
    ///  - Qo'shin, birlik guruhlari, sarkardalar, harbiy inshootlar va yollash navbati serverda.
    ///  - Har buyruq OnlineGameService.RunCommandAsync orqali: CommandId (takror bajarilmaydi) + bitta tranzaksiya.
    ///  - Tanga shartli UPDATE bilan yechiladi ("Coin >= narx") — resurs ikki marta sarflanmaydi.
    ///  - Raund faqat ikkala qaror kelganda hisoblanadi; jang holati shartli UPDATE (Round == r, Status == InProgress)
    ///    bilan o'zgaradi — bir raund ikki marta hisoblanmaydi, jang bir marta yakunlanadi.
    ///  - G'alabadan keyingi hudud transferi AnnexAsync dagi kabi atomik: hudud, shaharlar (aholi bilan), binolar.
    /// Client natijani o'zi hisoblamaydi — faqat qaror yuboradi va server holatini ko'rsatadi.
    /// </summary>
    public sealed class WarService
    {
        private readonly OnlineGameService _online;
        private readonly KingdomService _kingdom;

        public List<WarEvent> Events { get; } = new List<WarEvent>();

        public WarService(OnlineGameService online, KingdomService kingdom)
        {
            _online = online;
            _kingdom = kingdom;
        }

        private GameDbContext Db => _online.Db;
        private DateTime Now => _online.Clock.UtcNow;

        private void Emit(int worldId, string name, params object[] args) => Events.Add(new WarEvent { WorldId = worldId, Name = name, Args = args });

        // ==================================================================
        // Holatni tayyorlash (dangasa: o'qishda yangilanadi)
        // ==================================================================
        public async Task<KingdomWarStateEntity> EnsureStateAsync(Kingdom k)
        {
            KingdomWarStateEntity st = await Db.WarStates.FirstOrDefaultAsync(s => s.KingdomId == k.Id);
            if (st != null) return st;
            long day = await _online.GameMinuteAsync(k.WorldId) / 1440;
            st = new KingdomWarStateEntity { KingdomId = k.Id, LastUpkeepDay = day, CreatedAt = Now };
            Db.WarStates.Add(st);
            try { await Db.SaveChangesAsync(); }
            catch (DbUpdateException) { Db.ChangeTracker.Clear(); return await Db.WarStates.FirstAsync(s => s.KingdomId == k.Id); }

            if (!await Db.Armies.AnyAsync(a => a.KingdomId == k.Id))
            {
                int home = await Db.Regions.Where(r => r.WorldId == k.WorldId && r.OwnerKingdomId == k.Id).Select(r => r.Id).FirstOrDefaultAsync();
                var army = new ArmyEntity { KingdomId = k.Id, Name = k.IsAi ? k.Name + " qo'shini" : "Asosiy qo'shin", RegionId = home, Morale = 70, Readiness = 70 };
                Db.Armies.Add(army);
                await Db.SaveChangesAsync();
                var cmd = new CommanderEntity { KingdomId = k.Id, Name = WarRules.CommanderNames[Math.Abs(k.Id) % WarRules.CommanderNames.Length], Level = k.IsAi ? 2 : 1, ArmyId = army.Id, Portrait = k.Id % 6, AdministrationBonus = 2 };
                Db.Commanders.Add(cmd);
                await Db.SaveChangesAsync();
                army.CommanderId = cmd.Id;
                if (k.IsAi)
                {
                    // AI boshlang'ich qo'shini — hududlari soni va rivojlanishiga bog'liq (cheksiz emas).
                    int regions = await Db.Regions.CountAsync(r => r.WorldId == k.WorldId && r.OwnerKingdomId == k.Id);
                    AddGroup(army.Id, UnitLine.Piyoda, 1, 14 + 4 * regions);
                    AddGroup(army.Id, UnitLine.Masofaviy, 1, 5 + 2 * regions);
                    AddGroup(army.Id, UnitLine.Otliq, 1, 2 + regions);
                    Db.MilitaryFacilities.Add(new MilitaryFacilityEntity { KingdomId = k.Id, Kind = (int)FacilityKind.Mudofaa, Level = 1 });
                }
                else
                {
                    AddGroup(army.Id, UnitLine.Piyoda, 1, 10);
                    AddGroup(army.Id, UnitLine.Masofaviy, 1, 2);
                }
                await Db.SaveChangesAsync();
            }
            return st;
        }

        private void AddGroup(int armyId, UnitLine line, int level, int count)
            => Db.ArmyUnits.Add(new ArmyUnitEntity { ArmyId = armyId, Line = (int)line, Level = level, Count = count });

        /// <summary>Guruhga birlik qo'shadi (qator bo'lmasa yaratadi). Tranzaksiya ichida chaqiriladi.</summary>
        private async Task AddUnitsAsync(int armyId, UnitLine line, int level, int count)
        {
            if (count <= 0) return;
            int l = (int)line;
            int changed = await Db.ArmyUnits.Where(u => u.ArmyId == armyId && u.Line == l && u.Level == level)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Count, u => u.Count + count));
            if (changed == 0) { AddGroup(armyId, line, level, count); await Db.SaveChangesAsync(); }
        }

        public async Task<int> FacilityLevelAsync(int kingdomId, FacilityKind kind)
        {
            int k = (int)kind;
            return await Db.MilitaryFacilities.Where(f => f.KingdomId == kingdomId && f.Kind == k).Select(f => f.Level).FirstOrDefaultAsync();
        }

        private async Task<Dictionary<FacilityKind, int>> FacilityLevelsAsync(int kingdomId)
        {
            var rows = await Db.MilitaryFacilities.AsNoTracking().Where(f => f.KingdomId == kingdomId).ToListAsync();
            var d = new Dictionary<FacilityKind, int>();
            foreach (FacilityDefinition f in WarRules.Facilities) d[f.Kind] = 0;
            foreach (var r in rows) d[(FacilityKind)r.Kind] = r.Level;
            return d;
        }

        /// <summary>
        /// Tayyor yollash buyurtmalarini qo'shinga qo'shadi (har biri BIR MARTA: Delivered shartli yangilanadi),
        /// tiklanish muddati tugagan qo'shinlarni bo'shatadi va o'yin kunlari uchun saqlash xarajatini yechadi.
        /// </summary>
        public async Task TickKingdomAsync(Kingdom k)
        {
            await EnsureStateAsync(k);
            DateTime now = Now;

            // Jangdagi qo'shinga yangi birliklar jang tugaguncha qo'shilmaydi: ular jangda qatnashmagan va
            // jang yo'qotishlari ularga taqsimlanmasligi kerak. Buyurtma navbatda kutadi.
            int inBattleStatus = (int)ArmyStatus.InBattle;
            var busyArmies = await Db.Armies.Where(a => a.KingdomId == k.Id && a.Status == inBattleStatus).Select(a => a.Id).ToListAsync();
            var ready = await Db.RecruitOrders.AsNoTracking().Where(o => o.KingdomId == k.Id && !o.Delivered && o.ReadyAt <= now && !busyArmies.Contains(o.ArmyId)).ToListAsync();
            foreach (RecruitOrderEntity o in ready)
            {
                await using var tx = await Db.Database.BeginTransactionAsync();
                int mark = await Db.RecruitOrders.Where(x => x.Id == o.Id && !x.Delivered).ExecuteUpdateAsync(s => s.SetProperty(x => x.Delivered, true));
                if (mark == 1) await AddUnitsAsync(o.ArmyId, (UnitLine)o.Line, 1, o.Count);
                await tx.CommitAsync();
            }

            int recovering = (int)ArmyStatus.Recovering, available = (int)ArmyStatus.Available;
            await Db.Armies.Where(a => a.KingdomId == k.Id && a.Status == recovering && a.StatusUntil <= now)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, available)
                                          .SetProperty(a => a.Morale, a => a.Morale < 60 ? 60 : a.Morale)
                                          .SetProperty(a => a.Readiness, a => a.Readiness < 60 ? 60 : a.Readiness));

            // Saqlash xarajati: har o'yin kuni (24 real daqiqa) uchun, ko'pi bilan 3 kun orqaga. Bir kun bir marta yechiladi.
            KingdomWarStateEntity st = await Db.WarStates.AsNoTracking().FirstAsync(s => s.KingdomId == k.Id);
            long day = await _online.GameMinuteAsync(k.WorldId) / 1440;
            if (day > st.LastUpkeepDay)
            {
                long last = st.LastUpkeepDay;
                int claimed = await Db.WarStates.Where(s => s.Id == st.Id && s.LastUpkeepDay == last).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUpkeepDay, day));
                if (claimed == 1)
                {
                    int days = (int)Math.Min(3, day - last);
                    int cost = days * await DailyUpkeepAsync(k.Id);
                    if (cost > 0)
                    {
                        int paid = await Db.KingdomResources.Where(r => r.KingdomId == k.Id && r.Coin >= cost).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - cost));
                        if (paid == 0)
                        {
                            // Xazina yetmasa: bor tanga olinadi, qo'shin ruhi va tayyorgarligi pasayadi (qarz yo'q).
                            await Db.KingdomResources.Where(r => r.KingdomId == k.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, 0));
                            await Db.Armies.Where(a => a.KingdomId == k.Id).ExecuteUpdateAsync(s => s
                                .SetProperty(a => a.Morale, a => a.Morale > 15 ? a.Morale - 10 : 5)
                                .SetProperty(a => a.Readiness, a => a.Readiness > 15 ? a.Readiness - 10 : 5));
                        }
                    }
                }
            }
        }

        public async Task<int> DailyUpkeepAsync(int kingdomId)
        {
            var armyIds = await Db.Armies.Where(a => a.KingdomId == kingdomId && !a.IsGarrison).Select(a => a.Id).ToListAsync();
            var groups = await Db.ArmyUnits.AsNoTracking().Where(u => armyIds.Contains(u.ArmyId)).ToListAsync();
            int units = WarRules.Upkeep(groups.Select(g => ((UnitLine)g.Line, g.Level, g.Count)));
            var fac = await Db.MilitaryFacilities.AsNoTracking().Where(f => f.KingdomId == kingdomId).ToListAsync();
            int facilities = fac.Sum(f => WarRules.Facility((FacilityKind)f.Kind).UpkeepPerLevel * f.Level);
            return units + facilities;
        }

        private async Task<List<ArmyUnitEntity>> GroupsAsync(int armyId)
            => await Db.ArmyUnits.AsNoTracking().Where(u => u.ArmyId == armyId && u.Count > 0).OrderBy(u => u.Line).ThenBy(u => u.Level).ToListAsync();

        private static IEnumerable<(UnitLine, int, int)> Tuples(IEnumerable<ArmyUnitEntity> g) => g.Select(x => ((UnitLine)x.Line, x.Level, x.Count));

        private async Task<int> CommanderLevelAsync(ArmyEntity a)
            => a.CommanderId == 0 ? 0 : await Db.Commanders.Where(c => c.Id == a.CommanderId).Select(c => c.Level).FirstOrDefaultAsync();

        public async Task<int> ArmyStrengthAsync(ArmyEntity a)
        {
            var g = await GroupsAsync(a.Id);
            return WarRules.ArmyPower(Tuples(g), await CommanderLevelAsync(a), a.Readiness);
        }

        private async Task<int> UnitsTotalAsync(int kingdomId)
        {
            var armyIds = await Db.Armies.Where(a => a.KingdomId == kingdomId && !a.IsGarrison).Select(a => a.Id).ToListAsync();
            int inArmies = await Db.ArmyUnits.Where(u => armyIds.Contains(u.ArmyId)).SumAsync(u => (int?)u.Count) ?? 0;
            int queued = await Db.RecruitOrders.Where(o => o.KingdomId == kingdomId && !o.Delivered).SumAsync(o => (int?)o.Count) ?? 0;
            return inArmies + queued;
        }

        // ==================================================================
        // Umumiy ko'rinish
        // ==================================================================
        public async Task<WarOverviewDto> OverviewAsync(Kingdom me)
        {
            await TickKingdomAsync(me);
            await TickBattlesAsync(me);
            Db.ChangeTracker.Clear();

            var dto = new WarOverviewDto { KingdomId = me.Id };
            KingdomWarStateEntity st = await Db.WarStates.AsNoTracking().FirstAsync(s => s.KingdomId == me.Id);
            dto.AcceptsPvp = st.AcceptsPvp;
            dto.Protected = await IsProtectedAsync(me);
            dto.Coin = await Db.KingdomResources.Where(r => r.KingdomId == me.Id).Select(r => r.Coin).FirstOrDefaultAsync();
            var levels = await FacilityLevelsAsync(me.Id);
            dto.Capacity = WarRules.Capacity(levels[FacilityKind.Qarorgoh]);
            dto.MaxArmies = WarRules.MaxArmyCount(levels[FacilityKind.Qarorgoh]);
            dto.UnitsTotal = await UnitsTotalAsync(me.Id);
            dto.DailyUpkeep = await DailyUpkeepAsync(me.Id);

            var regionNames = await Db.Regions.AsNoTracking().Where(r => r.WorldId == me.WorldId).ToDictionaryAsync(r => r.Id, r => r.Name);
            var commanders = await Db.Commanders.AsNoTracking().Where(c => c.KingdomId == me.Id).OrderBy(c => c.Id).ToListAsync();
            foreach (ArmyEntity a in await Db.Armies.AsNoTracking().Where(a => a.KingdomId == me.Id && !a.IsGarrison).OrderBy(a => a.Id).ToListAsync())
                dto.Armies.Add(await ArmyDtoAsync(a, regionNames, commanders));
            foreach (CommanderEntity c in commanders) dto.Commanders.Add(ToDto(c));
            foreach (FacilityDefinition f in WarRules.Facilities)
            {
                int lvl = levels[f.Kind];
                dto.Facilities.Add(new FacilityDto
                {
                    Kind = f.Kind, NameUz = f.NameUz, EffectUz = f.EffectUz, Level = lvl, MaxLevel = f.MaxLevel,
                    UpgradeCost = lvl >= f.MaxLevel ? 0 : f.CostFor(lvl + 1), Upkeep = f.UpkeepPerLevel * lvl
                });
            }
            DateTime now = Now;
            foreach (RecruitOrderEntity o in await Db.RecruitOrders.AsNoTracking().Where(o => o.KingdomId == me.Id && !o.Delivered).OrderBy(o => o.ReadyAt).ToListAsync())
                dto.Queue.Add(new RecruitOrderDto { Id = o.Id, ArmyId = o.ArmyId, Count = o.Count, NameUz = WarRules.Unit((UnitLine)o.Line, 1).NameUz, SecondsLeft = Math.Max(0, (int)(o.ReadyAt - now).TotalSeconds) });

            dto.Targets = await TargetsAsync(me, levels);
            dto.Opponents = await OpponentsAsync(me);
            dto.Invitations = await InvitationsAsync(me);
            dto.Conflicts = await ConflictsAsync(me);

            int inProgress = (int)BattleStatus.InProgress;
            BattleEntity active = await Db.Battles.AsNoTracking().Where(b => b.Status == inProgress && (b.AttackerKingdomId == me.Id || b.DefenderKingdomId == me.Id)).OrderByDescending(b => b.Id).FirstOrDefaultAsync();
            if (active != null) dto.ActiveBattle = await BattleDtoAsync(active, me.Id);
            int completed = (int)BattleStatus.Completed;
            foreach (BattleEntity b in await Db.Battles.AsNoTracking().Where(b => b.Status == completed && (b.AttackerKingdomId == me.Id || b.DefenderKingdomId == me.Id)).OrderByDescending(b => b.Id).Take(10).ToListAsync())
                dto.History.Add(await BattleDtoAsync(b, me.Id, withGroups: false));

            await FillMarchesAsync(me, dto);
            await FillMapAsync(me, dto);
            dto.AdviceUz = Advice(dto);
            return dto;
        }

        private async Task<ArmyDto> ArmyDtoAsync(ArmyEntity a, Dictionary<int, string> regionNames, List<CommanderEntity> commanders)
        {
            var g = await GroupsAsync(a.Id);
            CommanderEntity c = commanders.FirstOrDefault(x => x.Id == a.CommanderId);
            var d = new ArmyDto
            {
                Id = a.Id, Name = a.Name, Status = (ArmyStatus)a.Status, StatusUz = WarRules.StatusName((ArmyStatus)a.Status),
                Morale = a.Morale, Readiness = a.Readiness, RegionId = a.RegionId,
                RegionName = regionNames.TryGetValue(a.RegionId, out string rn) ? rn : "-",
                CommanderId = c?.Id ?? 0, CommanderName = c?.Name ?? "Sarkarda tayinlanmagan", CommanderLevel = c?.Level ?? 0,
                Strength = WarRules.ArmyPower(Tuples(g), c?.Level ?? 0, a.Readiness),
                UnitCount = g.Sum(x => x.Count), Upkeep = WarRules.Upkeep(Tuples(g))
            };
            d.ActiveTaskUz = (ArmyStatus)a.Status == ArmyStatus.Traveling ? "Yurishda: " + (regionNames.TryGetValue(a.TargetRegionId, out string tn) ? tn : "?") + " qasriga" : (ArmyStatus)a.Status == ArmyStatus.InBattle ? "Jangda" : (ArmyStatus)a.Status == ArmyStatus.Recovering ? "Jangdan keyin tiklanmoqda" : "Hududni qo'riqlamoqda";
            foreach (ArmyUnitEntity u in g) d.Groups.Add(new UnitGroupDto { Line = (UnitLine)u.Line, Level = u.Level, Count = u.Count, NameUz = WarRules.Unit((UnitLine)u.Line, u.Level)?.NameUz ?? "" });
            return d;
        }

        private static CommanderDto ToDto(CommanderEntity c) => new CommanderDto
        {
            Id = c.Id, Name = c.Name, Level = c.Level, RankUz = WarRules.CommanderRank(c.Level), Experience = c.Experience,
            NextLevelXp = c.Level >= 3 ? 0 : WarRules.CommanderXpForNext(c.Level), PowerBonusPct = WarRules.CommanderPowerPercent(c.Level),
            MoraleBonus = WarRules.CommanderMoraleBonus(c.Level), AdministrationBonus = c.AdministrationBonus, AssignedArmyId = c.ArmyId, Portrait = c.Portrait
        };

        private async Task<bool> IsProtectedAsync(Kingdom k)
        {
            if (k.IsAi || k.OwnerUserId == null) return false;
            if (await Db.WarStates.Where(s => s.KingdomId == k.Id).Select(s => s.ProtectionWaived).FirstOrDefaultAsync()) return false;
            DateTime created = await Db.Users.Where(u => u.Id == k.OwnerUserId).Select(u => u.CreatedAt).FirstOrDefaultAsync();
            return Now - created < TimeSpan.FromHours(WarRules.NewPlayerProtectionHours);
        }

        private async Task<KingdomConflictEntity> ConflictAsync(int worldId, int k1, int k2)
        {
            (int a, int b) = KingdomService.Pair(k1, k2);
            return await Db.KingdomConflicts.FirstOrDefaultAsync(c => c.WorldId == worldId && c.KingdomA == a && c.KingdomB == b);
        }

        private async Task<List<WarTargetDto>> TargetsAsync(Kingdom me, Dictionary<FacilityKind, int> levels)
        {
            var list = new List<WarTargetDto>();
            var regions = await Db.Regions.AsNoTracking().Where(r => r.WorldId == me.WorldId && r.OwnerKingdomId != 0 && r.OwnerKingdomId != me.Id).ToListAsync();
            var kingdoms = await Db.Kingdoms.AsNoTracking().Where(k => k.WorldId == me.WorldId).ToDictionaryAsync(k => k.Id);
            int myRegions = await Db.Regions.CountAsync(r => r.WorldId == me.WorldId && r.OwnerKingdomId == me.Id);
            foreach (RegionEntity r in regions)
            {
                if (!kingdoms.TryGetValue(r.OwnerKingdomId, out Kingdom owner)) continue;
                if (!owner.IsAi) continue; // real o'yinchilar bilan faqat kelishilgan PvP orqali
                await EnsureStateAsync(owner); // AI qo'shini mavjud bo'lsin — himoya bahosi haqiqiy bo'lishi uchun
                int ownerRegions = regions.Count(x => x.OwnerKingdomId == owner.Id);
                int relation = await _kingdom.RelationAsync(me.WorldId, me.Id, owner.Id);
                KingdomConflictEntity conflict = await ConflictAsync(me.WorldId, me.Id, owner.Id);
                int pop = await Db.Cities.Where(c => c.RegionId == r.Id).SumAsync(c => (int?)c.Population) ?? 0;
                ArmyEntity aiArmy = await Db.Armies.AsNoTracking().Where(a => a.KingdomId == owner.Id && !a.IsGarrison).OrderBy(a => a.Id).FirstOrDefaultAsync();
                int defense = aiArmy == null ? 0 : await ArmyStrengthAsync(aiArmy);
                int defLevel = await FacilityLevelAsync(owner.Id, FacilityKind.Mudofaa);
                defense = (int)(defense * (1 + (defLevel * 8 + 5) / 100.0));
                bool truce = conflict != null && conflict.TruceUntil > Now;
                string block = myRegions == 0 ? "Avval o'z hududingiz bo'lishi kerak"
                    : truce ? "Sulh amalda: " + Math.Max(1, (int)(conflict.TruceUntil - Now).TotalMinutes) + " daqiqa"
                    : ownerRegions <= 1 ? "Bu saltanatning yagona hududi — uni tortib olib bo'lmaydi"
                    : "";
                bool alreadyConflict = conflict != null && conflict.Active;
                list.Add(new WarTargetDto
                {
                    RegionId = r.Id, RegionName = r.Name, OwnerKingdomId = owner.Id, OwnerName = owner.Name, OwnerIsAi = owner.IsAi,
                    EstimatedDefense = defense, Relation = relation, RelationAfter = alreadyConflict ? relation : Math.Max(-100, relation - WarRules.ConflictRelationPenalty),
                    Cost = WarRules.ConflictCost, Population = pop, CanAttack = block.Length == 0, BlockReasonUz = block,
                    MapX = (float)r.Latitude, MapY = (float)r.Longitude,
                    ConsequencesUz = (alreadyConflict ? "Mojaro allaqachon davom etmoqda. " : "Munosabat " + relation + " → " + Math.Max(-100, relation - WarRules.ConflictRelationPenalty) + ". ")
                                     + "Ta'minot xarajati " + WarRules.ConflictCost + " tanga. " + owner.Name + " bilan savdo munosabati sovuqlashadi. "
                                     + "G'alaba qozonsangiz " + r.Name + " (" + pop + " aholi) saltanatingizga qo'shiladi, barqarorlik pasayadi."
                });
            }
            return list.OrderBy(t => t.EstimatedDefense).ToList();
        }

        private async Task<List<PvpOpponentDto>> OpponentsAsync(Kingdom me)
        {
            var list = new List<PvpOpponentDto>();
            foreach (Kingdom k in await Db.Kingdoms.AsNoTracking().Where(k => k.WorldId == me.WorldId && !k.IsAi && k.Id != me.Id).ToListAsync())
            {
                KingdomWarStateEntity st = await Db.WarStates.AsNoTracking().FirstOrDefaultAsync(s => s.KingdomId == k.Id);
                bool prot = await IsProtectedAsync(k);
                ArmyEntity a = await Db.Armies.AsNoTracking().Where(x => x.KingdomId == k.Id).OrderBy(x => x.Id).FirstOrDefaultAsync();
                var regions = await Db.Regions.AsNoTracking().Where(r => r.WorldId == me.WorldId && r.OwnerKingdomId == k.Id).OrderBy(r => r.Id).ToListAsync();
                var o = new PvpOpponentDto
                {
                    KingdomId = k.Id, Name = k.Name, RulerName = k.RulerName, Strength = a == null ? 0 : await ArmyStrengthAsync(a),
                    Protected = prot, AcceptsPvp = st?.AcceptsPvp ?? true,
                    NoteUz = prot ? "Yangi o'yinchi himoyasida (" + WarRules.NewPlayerProtectionHours + " soat)" : (st != null && !st.AcceptsPvp) ? "PvP takliflarini o'chirgan" : ""
                };
                if (regions.Count > 1) foreach (RegionEntity r in regions.Skip(1)) o.StakeRegions.Add(new RegionStakeDto { RegionId = r.Id, Name = r.Name });
                list.Add(o);
            }
            return list;
        }

        private async Task<List<BattleInvitationDto>> InvitationsAsync(Kingdom me)
        {
            DateTime now = Now;
            int pending = (int)InvitationStatus.Pending, expired = (int)InvitationStatus.Expired;
            await Db.BattleInvitations.Where(i => i.Status == pending && i.ExpiresAt < now).ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, expired));
            var rows = await Db.BattleInvitations.AsNoTracking().Where(i => i.Status == pending && (i.FromKingdomId == me.Id || i.ToKingdomId == me.Id)).OrderByDescending(i => i.Id).ToListAsync();
            var names = await Db.Kingdoms.AsNoTracking().Where(k => k.WorldId == me.WorldId).ToDictionaryAsync(k => k.Id, k => k.Name);
            var list = new List<BattleInvitationDto>();
            foreach (BattleInvitationEntity i in rows)
            {
                string stake = i.StakeRegionId == 0 ? "" : await Db.Regions.Where(r => r.Id == i.StakeRegionId).Select(r => r.Name).FirstOrDefaultAsync() ?? "";
                list.Add(new BattleInvitationDto
                {
                    Id = i.Id, FromKingdomId = i.FromKingdomId, FromName = names.GetValueOrDefault(i.FromKingdomId, "?"),
                    ToKingdomId = i.ToKingdomId, ToName = names.GetValueOrDefault(i.ToKingdomId, "?"),
                    StakeRegionId = i.StakeRegionId, StakeRegionName = stake, Status = (InvitationStatus)i.Status,
                    Incoming = i.ToKingdomId == me.Id, SecondsLeft = Math.Max(0, (int)(i.ExpiresAt - now).TotalSeconds),
                    TermsUz = stake.Length == 0 ? "Hududsiz o'rtoqlik jangi: g'olib obro' va sarkarda tajribasi oladi."
                        : "Garov: " + stake + ". Taklif yuboruvchi g'alaba qozonsa, " + stake + " unga o'tadi. Rad etsangiz hech narsa yo'qotmaysiz."
                });
            }
            return list;
        }

        private async Task<List<ConflictDto>> ConflictsAsync(Kingdom me)
        {
            var rows = await Db.KingdomConflicts.AsNoTracking().Where(c => c.WorldId == me.WorldId && (c.KingdomA == me.Id || c.KingdomB == me.Id)).ToListAsync();
            var names = await Db.Kingdoms.AsNoTracking().Where(k => k.WorldId == me.WorldId).ToDictionaryAsync(k => k.Id, k => k.Name);
            var list = new List<ConflictDto>();
            foreach (KingdomConflictEntity c in rows)
            {
                int other = c.KingdomA == me.Id ? c.KingdomB : c.KingdomA;
                int truce = Math.Max(0, (int)(c.TruceUntil - Now).TotalMinutes);
                if (!c.Active && truce == 0) continue;
                list.Add(new ConflictDto { KingdomId = other, KingdomName = names.GetValueOrDefault(other, "?"), Active = c.Active, TruceMinutesLeft = truce, Relation = await _kingdom.RelationAsync(me.WorldId, me.Id, other) });
            }
            return list;
        }

        /// <summary>Donishmand Vazir — faqat mavjud statistikadan maslahat (qarorni o'yinchi qabul qiladi).</summary>
        private static List<string> Advice(WarOverviewDto d)
        {
            var a = new List<string>();
            if (d.DailyUpkeep > 0 && d.DailyUpkeep * 3 > d.Coin)
                a.Add("Hazratim, harbiy xarajatlar (kuniga " + d.DailyUpkeep + " tanga) xazinamizga og'ir kelmoqda. Davlat iqtisodiyotini barqaror saqlash muhim.");
            foreach (ConflictDto c in d.Conflicts.Where(c => c.Active && c.Relation < -30))
                a.Add("Hazratim, " + c.KingdomName + " bilan munosabatlarimiz yomonlashmoqda (" + c.Relation + "). Tinchlik muzokaralarini ko'rib chiqishni tavsiya qilaman.");
            BattleDto last = d.History.FirstOrDefault();
            if (last != null && last.TerritoryTransferred && last.WinnerKingdomId == d.KingdomId)
                a.Add("Hazratim, " + last.RegionName + " saltanatimizga qo'shildi. Uning aholisi va infratuzilmasini rivojlantirish bo'yicha reja tayyorlashim mumkin (Saltanat → Hokimlar).");
            if (d.Armies.Sum(x => x.UnitCount) < WarRules.MinBattleArmy)
                a.Add("Hazratim, qo'shinimiz juda kichik. Mudofaa uchun kamida " + WarRules.MinBattleArmy + " ta birlik yollashni tavsiya qilaman.");
            if (a.Count == 0) a.Add("Hazratim, harbiy holat barqaror. Jangdan oldin raqib kuchini va oqibatlarini diqqat bilan baholang.");
            return a;
        }

        // ==================================================================
        // Buyruqlar
        // ==================================================================
        public Task<ActionResultDto> RecruitAsync(Kingdom me, RecruitRequest req) => _online.RunCommandAsync(me, req?.CommandId, async () =>
        {
            await EnsureStateAsync(me);
            int count = Math.Clamp(req.Count, 1, 50);
            UnitDefinition u = WarRules.Unit(req.Line, 1);
            if (u == null) return "Bunday birlik yo'q";
            ArmyEntity army = await Db.Armies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == req.ArmyId && a.KingdomId == me.Id);
            if (army == null) return "Qo'shin topilmadi";
            var levels = await FacilityLevelsAsync(me.Id);
            if (!WarRules.Unlocked(u, k => levels[k])) return u.NameUz + " uchun kerak: " + WarRules.RequirementText(u);
            int capacity = WarRules.Capacity(levels[FacilityKind.Qarorgoh]);
            int total = await UnitsTotalAsync(me.Id);
            if (total + count > capacity) return "Qo'shin sig'imi yetmaydi: " + total + "/" + capacity + " (Harbiy qarorgohni rivojlantiring)";
            int cost = u.Cost * count;
            int paid = await Db.KingdomResources.Where(r => r.KingdomId == me.Id && r.Coin >= cost).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - cost));
            if (paid == 0) return "Tanga yetarli emas (" + cost + ")";
            Db.RecruitOrders.Add(new RecruitOrderEntity { KingdomId = me.Id, ArmyId = army.Id, Line = (int)req.Line, Count = count, ReadyAt = Now.AddSeconds(WarRules.RecruitMinutes(count)) });
            await Db.SaveChangesAsync();
            return null;
        }, "Yollash navbatiga qo'shildi");

        public Task<ActionResultDto> UpgradeAsync(Kingdom me, UpgradeUnitsRequest req) => _online.RunCommandAsync(me, req?.CommandId, async () =>
        {
            UnitDefinition next = WarRules.Unit(req.Line, req.FromLevel + 1);
            if (next == null) return "Bu birlik eng yuqori darajada";
            ArmyEntity army = await Db.Armies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == req.ArmyId && a.KingdomId == me.Id);
            if (army == null) return "Qo'shin topilmadi";
            if ((ArmyStatus)army.Status == ArmyStatus.InBattle) return "Jangdagi qo'shinni rivojlantirib bo'lmaydi";
            var levels = await FacilityLevelsAsync(me.Id);
            if (!WarRules.Unlocked(next, k => levels[k])) return next.NameUz + " uchun kerak: " + WarRules.RequirementText(next);
            int line = (int)req.Line, from = req.FromLevel;
            int have = await Db.ArmyUnits.Where(u => u.ArmyId == army.Id && u.Line == line && u.Level == from).Select(u => u.Count).FirstOrDefaultAsync();
            int count = Math.Min(Math.Clamp(req.Count, 1, 50), have);
            if (count <= 0) return "Bu darajadagi birlik yo'q";
            int cost = next.Cost * count;
            int paid = await Db.KingdomResources.Where(r => r.KingdomId == me.Id && r.Coin >= cost).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - cost));
            if (paid == 0) return "Tanga yetarli emas (" + cost + ")";
            int moved = await Db.ArmyUnits.Where(u => u.ArmyId == army.Id && u.Line == line && u.Level == from && u.Count >= count)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Count, u => u.Count - count));
            if (moved == 0) return "Birliklar soni o'zgardi — qayta urining";
            await AddUnitsAsync(army.Id, req.Line, from + 1, count);
            return null;
        }, "Birliklar yangi darajaga ko'tarildi");

        public Task<ActionResultDto> BuildFacilityAsync(Kingdom me, FacilityRequest req) => _online.RunCommandAsync(me, req?.CommandId, async () =>
        {
            FacilityDefinition f = WarRules.Facility(req.Kind);
            if (f == null) return "Bunday inshoot yo'q";
            int kind = (int)req.Kind;
            MilitaryFacilityEntity row = await Db.MilitaryFacilities.AsNoTracking().FirstOrDefaultAsync(x => x.KingdomId == me.Id && x.Kind == kind);
            int level = row?.Level ?? 0;
            if (level >= f.MaxLevel) return f.NameUz + " eng yuqori darajada";
            int cost = f.CostFor(level + 1);
            int paid = await Db.KingdomResources.Where(r => r.KingdomId == me.Id && r.Coin >= cost).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - cost));
            if (paid == 0) return "Tanga yetarli emas (" + cost + ")";
            if (row == null) { Db.MilitaryFacilities.Add(new MilitaryFacilityEntity { KingdomId = me.Id, Kind = kind, Level = 1 }); await Db.SaveChangesAsync(); }
            else
            {
                int up = await Db.MilitaryFacilities.Where(x => x.Id == row.Id && x.Level == level).ExecuteUpdateAsync(s => s.SetProperty(x => x.Level, level + 1));
                if (up == 0) return "Inshoot hozirgina o'zgardi — qayta urining";
            }
            return null;
        }, "Harbiy inshoot qurildi");

        public Task<ActionResultDto> HireCommanderAsync(Kingdom me, CommanderRequest req) => _online.RunCommandAsync(me, req?.CommandId, async () =>
        {
            await EnsureStateAsync(me);
            int n = await Db.Commanders.CountAsync(c => c.KingdomId == me.Id);
            if (n >= WarRules.MaxCommanders) return "Sarkardalar soni chegarasi: " + WarRules.MaxCommanders;
            int cost = WarRules.CommanderHireCost;
            int paid = await Db.KingdomResources.Where(r => r.KingdomId == me.Id && r.Coin >= cost).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - cost));
            if (paid == 0) return "Tanga yetarli emas (" + cost + ")";
            var used = await Db.Commanders.Where(c => c.KingdomId == me.Id).Select(c => c.Name).ToListAsync();
            string name = WarRules.CommanderNames.FirstOrDefault(x => !used.Contains(x)) ?? "Sarkarda " + (n + 1);
            Db.Commanders.Add(new CommanderEntity { KingdomId = me.Id, Name = name, Level = 1, Portrait = (me.Id + n) % 6, AdministrationBonus = 1 + n });
            await Db.SaveChangesAsync();
            return null;
        }, "Yangi sarkarda xizmatga kirdi");

        public Task<ActionResultDto> AssignCommanderAsync(Kingdom me, CommanderRequest req) => _online.RunCommandAsync(me, req?.CommandId, async () =>
        {
            CommanderEntity c = await Db.Commanders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.CommanderId && x.KingdomId == me.Id);
            ArmyEntity a = await Db.Armies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.ArmyId && x.KingdomId == me.Id);
            if (c == null || a == null) return "Sarkarda yoki qo'shin topilmadi";
            if ((ArmyStatus)a.Status == ArmyStatus.InBattle) return "Jang paytida sarkardani almashtirib bo'lmaydi";
            // Sarkarda bir vaqtda faqat bitta qo'shinga: eski bog'lanishlar tozalanadi.
            await Db.Armies.Where(x => x.KingdomId == me.Id && x.CommanderId == c.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CommanderId, 0));
            await Db.Commanders.Where(x => x.KingdomId == me.Id && x.ArmyId == a.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ArmyId, 0));
            await Db.Armies.Where(x => x.Id == a.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CommanderId, c.Id));
            await Db.Commanders.Where(x => x.Id == c.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ArmyId, a.Id));
            return null;
        }, "Sarkarda tayinlandi");

        public Task<ActionResultDto> CreateArmyAsync(Kingdom me, CreateArmyRequest req) => _online.RunCommandAsync(me, req?.CommandId, async () =>
        {
            await EnsureStateAsync(me);
            int max = WarRules.MaxArmyCount(await FacilityLevelAsync(me.Id, FacilityKind.Qarorgoh));
            if (await Db.Armies.CountAsync(a => a.KingdomId == me.Id && !a.IsGarrison) >= max) return "Qo'shinlar soni chegarasi: " + max + " (Harbiy qarorgohni rivojlantiring)";
            int home = await Db.Regions.Where(r => r.WorldId == me.WorldId && r.OwnerKingdomId == me.Id).Select(r => r.Id).FirstOrDefaultAsync();
            string name = string.IsNullOrWhiteSpace(req.Name) ? (await Db.Armies.CountAsync(a => a.KingdomId == me.Id) + 1) + "-qo'shin" : req.Name.Trim();
            if (name.Length > 30) name = name.Substring(0, 30);
            Db.Armies.Add(new ArmyEntity { KingdomId = me.Id, Name = name, RegionId = home });
            await Db.SaveChangesAsync();
            return null;
        }, "Yangi qo'shin tashkil qilindi");

        public Task<ActionResultDto> SetPvpAsync(Kingdom me, PvpSettingRequest req) => _online.RunCommandAsync(me, req?.CommandId, async () =>
        {
            await EnsureStateAsync(me);
            bool v = req.AcceptsPvp;
            // PvP ni yoqish — yangi o'yinchi himoyasidan ixtiyoriy voz kechish (rozilik aniq). O'chirish himoyani qaytarmaydi.
            if (v) await Db.WarStates.Where(s => s.KingdomId == me.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AcceptsPvp, true).SetProperty(x => x.ProtectionWaived, true));
            else await Db.WarStates.Where(s => s.KingdomId == me.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AcceptsPvp, false));
            return null;
        }, req != null && req.AcceptsPvp ? "PvP takliflari yoqildi" : "PvP takliflari o'chirildi");

        // ==================================================================
        // AI bilan strategik jang
        // ==================================================================
        public async Task<(ActionResultDto result, int battleId)> StartAiBattleAsync(Kingdom me, StartBattleRequest req)
        {
            int battleId = 0;
            await EnsureStateAsync(me);
            await TickKingdomAsync(me);
            ActionResultDto res = await _online.RunCommandAsync(me, req?.CommandId, async () =>
            {
                RegionEntity region = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.RegionId && r.WorldId == me.WorldId);
                if (region == null) return "Hudud topilmadi";
                Kingdom owner = await Db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(k => k.Id == region.OwnerKingdomId);
                if (owner == null || owner.Id == me.Id) return "Bu hududga hujum qilib bo'lmaydi";
                if (!owner.IsAi) return "Real o'yinchi bilan faqat kelishilgan PvP jangi mumkin";
                if (await Db.Regions.CountAsync(r => r.WorldId == me.WorldId && r.OwnerKingdomId == owner.Id) <= 1) return "Bu saltanatning yagona hududi — uni tortib olib bo'lmaydi";
                int inProgress = (int)BattleStatus.InProgress;
                if (await Db.Battles.AnyAsync(b => b.Status == inProgress && (b.AttackerKingdomId == me.Id || b.DefenderKingdomId == me.Id)))
                    return "Sizda allaqachon faol jang bor";
                KingdomConflictEntity conflict = await ConflictAsync(me.WorldId, me.Id, owner.Id);
                if (conflict != null && conflict.TruceUntil > Now) return "Sulh amalda — jang boshlab bo'lmaydi";

                ArmyEntity army = await Db.Armies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == req.ArmyId && a.KingdomId == me.Id);
                string armyBlock = await ArmyReadyBlockAsync(army);
                if (armyBlock != null) return armyBlock;

                await EnsureStateAsync(owner);
                ArmyEntity aiArmy = await Db.Armies.AsNoTracking().Where(a => a.KingdomId == owner.Id && !a.IsGarrison).OrderBy(a => a.Id).FirstOrDefaultAsync();
                if (aiArmy == null) return "Raqib qo'shini topilmadi";
                await AiReplenishAsync(owner, aiArmy);

                int cost = WarRules.ConflictCost;
                int paid = await Db.KingdomResources.Where(r => r.KingdomId == me.Id && r.Coin >= cost).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - cost));
                if (paid == 0) return "Ta'minot uchun tanga yetarli emas (" + cost + ")";

                bool wasActive = conflict != null && conflict.Active;
                await SetConflictAsync(me.WorldId, me.Id, owner.Id, active: true, truceUntil: null);
                if (!wasActive) await _kingdom.AdjustRelationAsync(me.WorldId, me.Id, owner.Id, -WarRules.ConflictRelationPenalty);

                BattleEntity b = await CreateBattleAsync(me.WorldId, isPvp: false, region.Id, territoryStake: true, me, army, owner, aiArmy, cost);
                battleId = b.Id;
                // AI 1-raund qarorini darhol beradi (o'yinchi kutib qolmaydi).
                await AddAiDecisionAsync(b, owner);
                return null;
            }, "Strategik jang boshlandi");
            if (res.Success && !res.Duplicate && battleId > 0) Emit(me.WorldId, "BattleStarted", battleId, me.Id, 0);
            return (res, battleId);
        }

        private async Task<string> ArmyReadyBlockAsync(ArmyEntity army)
        {
            if (army == null) return "Qo'shin topilmadi";
            if ((ArmyStatus)army.Status != ArmyStatus.Available) return army.Name + " hozir tayyor emas (" + WarRules.StatusName((ArmyStatus)army.Status) + ")";
            var g = await GroupsAsync(army.Id);
            if (WarRules.CombatUnitCount(Tuples(g)) < WarRules.MinBattleArmy) return "Jang uchun kamida " + WarRules.MinBattleArmy + " ta jangovar birlik kerak";
            return null;
        }

        /// <summary>AI o'z xazinasidan qo'shinini to'ldiradi (bepul birlik yo'q): boshlang'ich darajagacha, tanga yetgancha.</summary>
        private async Task AiReplenishAsync(Kingdom ai, ArmyEntity army)
        {
            var g = await GroupsAsync(army.Id);
            int regions = await Db.Regions.CountAsync(r => r.WorldId == ai.WorldId && r.OwnerKingdomId == ai.Id);
            int target = 14 + 4 * regions;
            int have = g.Where(x => x.Line == (int)UnitLine.Piyoda).Sum(x => x.Count);
            int need = Math.Max(0, target - have);
            if (need == 0) return;
            UnitDefinition u = WarRules.Unit(UnitLine.Piyoda, 1);
            int coin = await Db.KingdomResources.Where(r => r.KingdomId == ai.Id).Select(r => r.Coin).FirstOrDefaultAsync();
            int afford = Math.Min(need, coin / u.Cost);
            if (afford <= 0) return;
            int cost = afford * u.Cost;
            int paid = await Db.KingdomResources.Where(r => r.KingdomId == ai.Id && r.Coin >= cost).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - cost));
            if (paid == 1) await AddUnitsAsync(army.Id, UnitLine.Piyoda, 1, afford);
        }

        private async Task SetConflictAsync(int worldId, int k1, int k2, bool active, DateTime? truceUntil)
        {
            (int a, int b) = KingdomService.Pair(k1, k2);
            DateTime now = Now;
            int changed = truceUntil == null
                ? await Db.KingdomConflicts.Where(c => c.WorldId == worldId && c.KingdomA == a && c.KingdomB == b).ExecuteUpdateAsync(s => s.SetProperty(c => c.Active, active).SetProperty(c => c.UpdatedAt, now))
                : await Db.KingdomConflicts.Where(c => c.WorldId == worldId && c.KingdomA == a && c.KingdomB == b).ExecuteUpdateAsync(s => s.SetProperty(c => c.Active, active).SetProperty(c => c.TruceUntil, truceUntil.Value).SetProperty(c => c.UpdatedAt, now));
            if (changed == 0)
            {
                Db.KingdomConflicts.Add(new KingdomConflictEntity { WorldId = worldId, KingdomA = a, KingdomB = b, Active = active, TruceUntil = truceUntil ?? DateTime.MinValue, UpdatedAt = now });
                await Db.SaveChangesAsync();
            }
        }

        private async Task<(int start, int morale, int resPct)> SideStatsAsync(ArmyEntity a)
        {
            var g = await GroupsAsync(a.Id);
            int cmd = await CommanderLevelAsync(a);
            int power = WarRules.ArmyPower(Tuples(g), cmd, a.Readiness);
            int arava = g.Where(x => x.Line == (int)UnitLine.Aravasi).Sum(x => x.Count);
            int lager = g.Where(x => x.Line == (int)UnitLine.Lager).Sum(x => x.Count);
            int units = Math.Max(1, g.Sum(x => x.Count));
            int avgRes = g.Sum(x => (WarRules.Unit((UnitLine)x.Line, x.Level)?.Resilience ?? 0) * x.Count) / units;
            int morale = Math.Clamp(a.Morale + WarRules.CommanderMoraleBonus(cmd) + Math.Min(15, arava * 2), 0, 100);
            int resPct = Math.Min(30, avgRes / 2 + Math.Min(10, lager));
            return (power, morale, resPct);
        }

        private async Task<BattleEntity> CreateBattleAsync(int worldId, bool isPvp, int regionId, bool territoryStake, Kingdom attacker, ArmyEntity aArmy, Kingdom defender, ArmyEntity dArmy, int cost)
        {
            var a = await SideStatsAsync(aArmy);
            var d = await SideStatsAsync(dArmy);
            int defLevel = await FacilityLevelAsync(defender.Id, FacilityKind.Mudofaa);
            var b = new BattleEntity
            {
                WorldId = worldId, IsPvp = isPvp, Status = (int)BattleStatus.InProgress, Round = 1,
                Seed = Math.Abs(HashCode.Combine(worldId, attacker.Id, defender.Id, regionId, Now.Ticks)),
                RegionId = regionId, TerritoryStake = territoryStake,
                AttackerKingdomId = attacker.Id, DefenderKingdomId = defender.Id, AttackerArmyId = aArmy.Id, DefenderArmyId = dArmy.Id,
                AttackerStartPower = a.start, DefenderStartPower = d.start, AttackerPower = a.start, DefenderPower = d.start,
                AttackerMorale = a.morale, DefenderMorale = d.morale, AttackerResiliencePct = a.resPct, DefenderResiliencePct = d.resPct,
                DefenseBonusPct = defLevel * 8 + (defender.IsAi ? 5 : 0), CostCoins = cost, CreatedAt = Now, RoundStartedAt = Now
            };
            Db.Battles.Add(b);
            await Db.SaveChangesAsync();
            int inBattle = (int)ArmyStatus.InBattle;
            await Db.Armies.Where(x => x.Id == aArmy.Id || x.Id == dArmy.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, inBattle));
            return b;
        }

        private async Task AddAiDecisionAsync(BattleEntity b, Kingdom ai)
        {
            bool aiAttacks = b.AttackerKingdomId == ai.Id;
            BattleDecision dec = WarRules.AiDecision(ai.AiStrategy, b.Seed, b.Round,
                aiAttacks ? b.AttackerPower : b.DefenderPower, aiAttacks ? b.AttackerStartPower : b.DefenderStartPower,
                aiAttacks ? b.AttackerMorale : b.DefenderMorale, aiAttacks ? b.DefenderPower : b.AttackerPower);
            int round = b.Round, bid = b.Id, kid = ai.Id;
            if (await Db.BattleDecisions.AnyAsync(d => d.BattleId == bid && d.Round == round && d.KingdomId == kid)) return;
            Db.BattleDecisions.Add(new BattleDecisionEntity { BattleId = b.Id, Round = b.Round, KingdomId = ai.Id, Decision = (int)dec, Automatic = true, At = Now });
            await Db.SaveChangesAsync();
        }

        // ==================================================================
        // Raund qarorlari va hisoblash
        // ==================================================================
        public async Task<ActionResultDto> SubmitDecisionAsync(Kingdom me, BattleDecisionRequest req)
        {
            int resolvedBattle = 0;
            RoundOutcome outcome = null;
            ActionResultDto res = await _online.RunCommandAsync(me, req?.CommandId, async () =>
            {
                BattleEntity b = await Db.Battles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.BattleId);
                if (b == null) return "Jang topilmadi";
                if (b.AttackerKingdomId != me.Id && b.DefenderKingdomId != me.Id) return "Siz bu jang ishtirokchisi emassiz";
                if ((BattleStatus)b.Status != BattleStatus.InProgress) return "Jang allaqachon yakunlangan";
                if (req.Round < b.Round) return "Bu raund allaqachon o'tdi (joriy raund: " + b.Round + ")";
                if (req.Round > b.Round) return "Bu raund hali boshlanmagan (joriy raund: " + b.Round + ")";
                if (!Enum.IsDefined(typeof(BattleDecision), req.Decision)) return "Noto'g'ri qaror";
                int bid = b.Id, round = b.Round, kid = me.Id;
                if (await Db.BattleDecisions.AnyAsync(d => d.BattleId == bid && d.Round == round && d.KingdomId == kid)) return "Bu raund uchun qaror allaqachon yuborilgan";
                Db.BattleDecisions.Add(new BattleDecisionEntity { BattleId = b.Id, Round = b.Round, KingdomId = me.Id, Decision = (int)req.Decision, At = Now });
                await Db.SaveChangesAsync();
                Emit(b.WorldId, "BattleDecisionSubmitted", b.Id, b.Round, me.Id);
                outcome = await TryResolveRoundAsync(b.Id);
                resolvedBattle = b.Id;
                return null;
            }, "Qaror qabul qilindi");
            if (res.Success && !res.Duplicate && outcome != null) res.MessageUz = outcome.SummaryUz;
            return res;
        }

        /// <summary>
        /// Ikkala qaror bor bo'lsa raundni hisoblaydi. Shartli UPDATE (Round == r, Status == InProgress) — raund bir marta
        /// hisoblanadi; jang tugasa yakunlash (yo'qotishlar, sarkarda tajribasi, hudud transferi, diplomatiya) shu tranzaksiyada.
        /// </summary>
        private async Task<RoundOutcome> TryResolveRoundAsync(int battleId)
        {
            BattleEntity b = await Db.Battles.AsNoTracking().FirstAsync(x => x.Id == battleId);
            if ((BattleStatus)b.Status != BattleStatus.InProgress) return null;
            int round = b.Round;
            var decisions = await Db.BattleDecisions.AsNoTracking().Where(d => d.BattleId == battleId && d.Round == round).ToListAsync();
            BattleDecisionEntity ad = decisions.FirstOrDefault(d => d.KingdomId == b.AttackerKingdomId);
            BattleDecisionEntity dd = decisions.FirstOrDefault(d => d.KingdomId == b.DefenderKingdomId);
            if (ad == null || dd == null) return null;

            RoundOutcome o = WarRules.ResolveRound(b.Seed, round,
                b.AttackerPower, b.AttackerStartPower, b.AttackerMorale, (BattleDecision)ad.Decision, b.AttackerResiliencePct,
                b.DefenderPower, b.DefenderStartPower, b.DefenderMorale, (BattleDecision)dd.Decision, b.DefenderResiliencePct, b.DefenseBonusPct);

            int inProgress = (int)BattleStatus.InProgress, completed = (int)BattleStatus.Completed;
            int nextStatus = o.Finished ? completed : inProgress;
            int nextRound = o.Finished ? round : round + 1;
            DateTime now = Now;
            int claimed = await Db.Battles.Where(x => x.Id == battleId && x.Round == round && x.Status == inProgress)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AttackerPower, o.AttackerPowerAfter).SetProperty(x => x.DefenderPower, o.DefenderPowerAfter)
                                          .SetProperty(x => x.AttackerMorale, o.AttackerMoraleAfter).SetProperty(x => x.DefenderMorale, o.DefenderMoraleAfter)
                                          .SetProperty(x => x.Round, nextRound).SetProperty(x => x.Status, nextStatus).SetProperty(x => x.RoundStartedAt, now));
            if (claimed == 0) return null; // boshqa so'rov allaqachon hisobladi
            Db.BattleRounds.Add(new BattleRoundEntity { BattleId = battleId, Round = round, AttackerDecision = ad.Decision, DefenderDecision = dd.Decision, AttackerLoss = o.AttackerLoss, DefenderLoss = o.DefenderLoss, SummaryUz = o.SummaryUz });
            await Db.SaveChangesAsync();
            Emit(b.WorldId, "BattleRoundResolved", battleId, round, o.Finished);

            if (o.Finished) await CompleteBattleAsync(battleId, o);
            else
            {
                // AI keyingi raund qarorini darhol beradi.
                BattleEntity nb = await Db.Battles.AsNoTracking().FirstAsync(x => x.Id == battleId);
                foreach (int kid in new[] { nb.AttackerKingdomId, nb.DefenderKingdomId })
                {
                    Kingdom k = await Db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == kid);
                    if (k != null && k.IsAi) await AddAiDecisionAsync(nb, k);
                }
            }
            return o;
        }

        private async Task CompleteBattleAsync(int battleId, RoundOutcome o)
        {
            BattleEntity b = await Db.Battles.AsNoTracking().FirstAsync(x => x.Id == battleId);
            Kingdom att = await Db.Kingdoms.AsNoTracking().FirstAsync(k => k.Id == b.AttackerKingdomId);
            Kingdom def = await Db.Kingdoms.AsNoTracking().FirstAsync(k => k.Id == b.DefenderKingdomId);
            RegionEntity region = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == b.RegionId);
            int winner = o.Winner == 1 ? att.Id : o.Winner == 2 ? def.Id : 0;

            // 1) Yo'qotishlar birlik guruhlariga taqsimlanadi (aholi emas — faqat qo'shin birliklari).
            await ApplyLossesAsync(b.AttackerArmyId, b.AttackerStartPower - o.AttackerPowerAfter, b.AttackerStartPower, o.AttackerMoraleAfter);
            await ApplyLossesAsync(b.DefenderArmyId, b.DefenderStartPower - o.DefenderPowerAfter, b.DefenderStartPower, o.DefenderMoraleAfter);

            // 2) Sarkarda tajribasi (g'olibga ko'proq), darajasi cheklangan (maks. 3).
            await GiveXpAsync(b.AttackerArmyId, winner == att.Id ? 60 : 25);
            await GiveXpAsync(b.DefenderArmyId, winner == def.Id ? 60 : 25);

            // 3) Hudud: hujumchi g'olib bo'lsa va garov bo'lsa — atomik transfer (shaharlar aholi bilan birga o'tadi).
            bool transferred = false;
            string territory;
            int plunder = 0;
            if (winner == att.Id && b.IsCapitalSiege && region != null)
            {
                // Poytaxt saltanatda qoladi (o'yinchi yo'q bo'lib ketmaydi) — g'olib xazinadan o'lja oladi (shartli, bir marta).
                int defCoin = await Db.KingdomResources.Where(r => r.KingdomId == def.Id).Select(r => r.Coin).FirstOrDefaultAsync();
                plunder = Math.Min(WarRules.PlunderMax, defCoin * WarRules.PlunderPercent / 100);
                if (plunder > 0)
                {
                    int amount = plunder;
                    int took = await Db.KingdomResources.Where(r => r.KingdomId == def.Id && r.Coin >= amount).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - amount));
                    if (took == 1) await Db.KingdomResources.Where(r => r.KingdomId == att.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin + amount));
                    else plunder = 0;
                }
                territory = region.Name + " poytaxti qamal qilindi: " + plunder + " tanga o'lja olindi. Poytaxt " + def.Name + " tarkibida qoladi.";
            }
            else if (winner == att.Id && b.TerritoryStake && region != null)
            {
                transferred = await TransferRegionAsync(att, region, def.Id);
                territory = transferred ? region.Name + " " + att.Name + " tarkibiga o'tdi. Shaharlar, binolar va aholi saqlandi." : region.Name + " egasi o'zgarmadi (shartlar bajarilmadi).";
            }
            else territory = region == null ? "" : region.Name + " egasi o'zgarmadi.";

            // 4) Diplomatiya
            string diplomacy;
            if (o.Winner == 0)
            {
                await SetConflictAsync(b.WorldId, att.Id, def.Id, active: false, truceUntil: Now.AddHours(WarRules.TruceHours));
                await _kingdom.AdjustRelationAsync(b.WorldId, att.Id, def.Id, 10);
                diplomacy = "Muzokara: sulh tuzildi (" + WarRules.TruceHours + " soat), munosabat +10.";
            }
            else if (b.IsPvp)
            {
                diplomacy = "Kelishilgan PvP jangi — munosabat o'zgarmadi.";
            }
            else
            {
                await _kingdom.AdjustRelationAsync(b.WorldId, att.Id, def.Id, -10);
                diplomacy = "Mojaro davom etmoqda, munosabat -10. Tinchlik taklif qilish mumkin.";
            }

            if (!b.IsPvp && def.IsAi == false && att.IsAi == false && o.Winner != 0) diplomacy = "Qasr uchun jang (PvP) — munosabat o'zgarmadi.";
            string result = o.Winner == 0 ? "Jang tinch yakunlandi" : (winner == att.Id ? att.Name : def.Name) + " g'alaba qozondi";
            await Db.Battles.Where(x => x.Id == battleId).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.WinnerKingdomId, winner).SetProperty(x => x.Peace, o.Winner == 0).SetProperty(x => x.TerritoryTransferred, transferred)
                .SetProperty(x => x.ResultUz, result + ". " + territory).SetProperty(x => x.PlunderCoins, plunder).SetProperty(x => x.DiplomacyUz, diplomacy).SetProperty(x => x.CompletedAt, Now));
            await Db.Kingdoms.Where(k => k.Id == att.Id || k.Id == def.Id).ExecuteUpdateAsync(s => s.SetProperty(k => k.StateVersion, k => k.StateVersion + 1));

            Emit(b.WorldId, "BattleCompleted", battleId, winner, transferred);
            if (transferred && region != null)
            {
                Emit(b.WorldId, "TerritoryOwnershipUpdated", region.Id, att.Id, def.Id);
                Emit(b.WorldId, "TerritoryTransferred", b.WorldId, region.Id, att.Id, def.Id, region.Name + " jang natijasida " + att.Name + " tarkibiga o'tdi");
            }
        }

        private async Task ApplyLossesAsync(int armyId, int powerLost, int startPower, int moraleAfter)
        {
            var groups = await Db.ArmyUnits.AsNoTracking().Where(u => u.ArmyId == armyId && u.Count > 0).ToListAsync();
            foreach (ArmyUnitEntity g in groups)
            {
                int lost = WarRules.UnitsLost(g.Count, powerLost, startPower);
                if (g.Line == (int)UnitLine.Aravasi || g.Line == (int)UnitLine.Lager) lost /= 2; // ta'minot orqada
                if (lost > 0) await Db.ArmyUnits.Where(u => u.Id == g.Id && u.Count >= lost).ExecuteUpdateAsync(s => s.SetProperty(u => u.Count, u => u.Count - lost));
            }
            int recovering = (int)ArmyStatus.Recovering;
            DateTime until = Now.AddMinutes(3);
            int morale = Math.Max(10, moraleAfter);
            await Db.Armies.Where(a => a.Id == armyId).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, recovering).SetProperty(a => a.StatusUntil, until)
                .SetProperty(a => a.Morale, morale).SetProperty(a => a.Readiness, a => a.Readiness > 30 ? a.Readiness - 20 : 10));
        }

        private async Task GiveXpAsync(int armyId, int xp)
        {
            int cid = await Db.Armies.Where(a => a.Id == armyId).Select(a => a.CommanderId).FirstOrDefaultAsync();
            if (cid == 0) return;
            CommanderEntity c = await Db.Commanders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cid);
            if (c == null) return;
            int exp = c.Experience + xp, level = c.Level;
            while (level < 3 && exp >= WarRules.CommanderXpForNext(level)) { exp -= WarRules.CommanderXpForNext(level); level++; }
            if (level >= 3) exp = Math.Min(exp, 999);
            await Db.Commanders.Where(x => x.Id == cid).ExecuteUpdateAsync(s => s.SetProperty(x => x.Experience, exp).SetProperty(x => x.Level, level));
        }

        /// <summary>
        /// Jang natijasidagi hudud transferi (AnnexAsync dagi kabi atomik): eski ega o'zgarmagan bo'lsagina hudud o'tadi;
        /// shaharlar aholisi, oilalari va ish joylari bilan o'tadi (aholi alohida "resurs" emas — ikki marta sanalmaydi);
        /// binolar yangi saltanatga; yangi boshqaruvga ishonch vaqtincha pasayadi; poytaxt maqomi olib tashlanadi.
        /// </summary>
        private async Task<bool> TransferRegionAsync(Kingdom winner, RegionEntity r, int expectedOwner)
        {
            if (await Db.Regions.CountAsync(x => x.WorldId == winner.WorldId && x.OwnerKingdomId == expectedOwner) <= 1) return false;
            int moved = await Db.Regions.Where(x => x.Id == r.Id && x.OwnerKingdomId == expectedOwner)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerKingdomId, winner.Id).SetProperty(x => x.Stability, x => x.Stability > 20 ? x.Stability - 20 : 0));
            if (moved == 0) return false;
            var cityIds = await Db.Cities.Where(c => c.RegionId == r.Id).Select(c => c.Id).ToListAsync();
            await Db.Cities.Where(c => c.RegionId == r.Id).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.KingdomId, winner.Id).SetProperty(c => c.IsCapital, false)
                .SetProperty(c => c.Trust, c => c.Trust > 45 ? c.Trust - 20 : 25)
                .SetProperty(c => c.GovernorMode, (int)GovernorMode.Manual));
            await Db.Buildings.Where(b => cityIds.Contains(b.CityId)).ExecuteUpdateAsync(s => s.SetProperty(b => b.KingdomId, winner.Id));
            await Db.GovernorPlans.Where(p => cityIds.Contains(p.CityId) && p.Status == 0).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, 2));
            await _kingdom.RefreshPopulationAsync(winner.WorldId);
            return true;
        }

        /// <summary>
        /// Faol janglarni "uyg'otadi": AI qarori yo'q bo'lsa qo'shadi, PvP da raqib vaqtida javob bermasa
        /// (WarRules.DecisionTimeoutSeconds) — avtomatik "Muvozanatli" qaror va raund hisoblanadi. Qayta ulanish va
        /// server restartidan keyin jang shu tarzda davom etadi.
        /// </summary>
        public async Task TickBattlesAsync(Kingdom me)
        {
            int inProgress = (int)BattleStatus.InProgress;
            var battles = await Db.Battles.AsNoTracking().Where(b => b.Status == inProgress && (b.AttackerKingdomId == me.Id || b.DefenderKingdomId == me.Id)).ToListAsync();
            foreach (BattleEntity b in battles) await TickOneBattleAsync(b);
        }

        private async Task TickOneBattleAsync(BattleEntity b)
        {
            {
                await using var tx = await Db.Database.BeginTransactionAsync();
                try
                {
                    foreach (int kid in new[] { b.AttackerKingdomId, b.DefenderKingdomId })
                    {
                        Kingdom k = await Db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == kid);
                        if (k != null && k.IsAi) await AddAiDecisionAsync(b, k);
                    }
                    if (Now - b.RoundStartedAt > TimeSpan.FromSeconds(WarRules.DecisionTimeoutSeconds))
                    {
                        // O'yinchi javob bermadi (offline): uning sarkardasi o'zi qaror qiladi — jang to'xtab qolmaydi.
                        foreach (int kid in new[] { b.AttackerKingdomId, b.DefenderKingdomId })
                        {
                            Kingdom k = await Db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == kid);
                            if (k != null) await AddAiDecisionAsync(b, k);
                        }
                    }
                    await TryResolveRoundAsync(b.Id);
                    await tx.CommitAsync();
                }
                catch (DbUpdateException)
                {
                    await tx.RollbackAsync(); // parallel so'rov allaqachon qo'shgan — keyingi o'qishda davom etadi
                }
                Db.ChangeTracker.Clear();
            }
        }

        public async Task<BattleDto> BattleAsync(Kingdom me, int battleId)
        {
            await TickBattlesAsync(me);
            BattleEntity b = await Db.Battles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == battleId && x.WorldId == me.WorldId);
            return b == null ? null : await BattleDtoAsync(b, me.Id);
        }

        private async Task<BattleDto> BattleDtoAsync(BattleEntity b, int viewer, bool withGroups = true)
        {
            Kingdom att = await Db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(k => k.Id == b.AttackerKingdomId);
            Kingdom def = await Db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(k => k.Id == b.DefenderKingdomId);
            var d = new BattleDto
            {
                Id = b.Id, Status = (BattleStatus)b.Status, IsPvp = b.IsPvp, Round = b.Round, RegionId = b.RegionId,
                RegionName = await Db.Regions.Where(r => r.Id == b.RegionId).Select(r => r.Name).FirstOrDefaultAsync() ?? "",
                AttackerKingdomId = b.AttackerKingdomId, AttackerName = att?.Name ?? "?", DefenderKingdomId = b.DefenderKingdomId, DefenderName = def?.Name ?? "?",
                AttackerColor = att == null ? new[] { 70, 130, 220 } : new[] { att.ColorR, att.ColorG, att.ColorB },
                DefenderColor = def == null ? new[] { 190, 80, 60 } : new[] { def.ColorR, def.ColorG, def.ColorB },
                AttackerFlag = WorldSeeder.FlagOf(att), DefenderFlag = WorldSeeder.FlagOf(def),
                AttackerStartPower = b.AttackerStartPower, DefenderStartPower = b.DefenderStartPower,
                AttackerPower = b.AttackerPower, DefenderPower = b.DefenderPower, AttackerMorale = b.AttackerMorale, DefenderMorale = b.DefenderMorale,
                IAmAttacker = viewer == b.AttackerKingdomId, IAmParticipant = viewer == b.AttackerKingdomId || viewer == b.DefenderKingdomId,
                WinnerKingdomId = b.WinnerKingdomId, Peace = b.Peace, TerritoryTransferred = b.TerritoryTransferred,
                ResultUz = b.ResultUz, DiplomacyUz = b.DiplomacyUz, CostCoins = b.CostCoins, CreatedAt = b.CreatedAt
            };
            d.AttackerCommander = await CommanderNameAsync(b.AttackerArmyId);
            d.DefenderCommander = await CommanderNameAsync(b.DefenderArmyId);
            int round = b.Round, bid = b.Id;
            var decided = await Db.BattleDecisions.AsNoTracking().Where(x => x.BattleId == bid && x.Round == round).Select(x => x.KingdomId).ToListAsync();
            int opponent = viewer == b.AttackerKingdomId ? b.DefenderKingdomId : b.AttackerKingdomId;
            d.MyDecisionSubmitted = decided.Contains(viewer);
            d.OpponentDecisionSubmitted = decided.Contains(opponent);
            d.RoundSecondsLeft = b.IsPvp ? Math.Max(0, WarRules.DecisionTimeoutSeconds - (int)(Now - b.RoundStartedAt).TotalSeconds) : 0;
            foreach (BattleRoundEntity r in await Db.BattleRounds.AsNoTracking().Where(x => x.BattleId == bid).OrderBy(x => x.Round).ToListAsync())
                d.Rounds.Add(new BattleRoundDto { Round = r.Round, AttackerDecision = (BattleDecision)r.AttackerDecision, DefenderDecision = (BattleDecision)r.DefenderDecision, AttackerLoss = r.AttackerLoss, DefenderLoss = r.DefenderLoss, SummaryUz = r.SummaryUz });
            if (withGroups)
            {
                foreach (ArmyUnitEntity u in await GroupsAsync(b.AttackerArmyId)) d.AttackerGroups.Add(new UnitGroupDto { Line = (UnitLine)u.Line, Level = u.Level, Count = u.Count, NameUz = WarRules.Unit((UnitLine)u.Line, u.Level)?.NameUz ?? "" });
                foreach (ArmyUnitEntity u in await GroupsAsync(b.DefenderArmyId)) d.DefenderGroups.Add(new UnitGroupDto { Line = (UnitLine)u.Line, Level = u.Level, Count = u.Count, NameUz = WarRules.Unit((UnitLine)u.Line, u.Level)?.NameUz ?? "" });
            }
            return d;
        }

        private async Task<string> CommanderNameAsync(int armyId)
        {
            int cid = await Db.Armies.Where(a => a.Id == armyId).Select(a => a.CommanderId).FirstOrDefaultAsync();
            return cid == 0 ? "" : await Db.Commanders.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync() ?? "";
        }

        // ==================================================================
        // Xaritadan qasrga yurish (PROMPT 25+): qo'shin yo'lga chiqadi, yetib borganda jang boshlanadi
        // ==================================================================
        private async Task<bool> IsCapitalOrLastAsync(RegionEntity r)
        {
            if (await Db.Cities.AnyAsync(c => c.RegionId == r.Id && c.IsCapital)) return true;
            return r.OwnerKingdomId != 0 && await Db.Regions.CountAsync(x => x.WorldId == r.WorldId && x.OwnerKingdomId == r.OwnerKingdomId) <= 1;
        }

        private async Task<RegionEntity> HomeRegionAsync(Kingdom k, ArmyEntity a)
        {
            RegionEntity home = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == a.RegionId && r.OwnerKingdomId == k.Id);
            return home ?? await Db.Regions.AsNoTracking().Where(r => r.WorldId == k.WorldId && r.OwnerKingdomId == k.Id).OrderBy(r => r.Id).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Real o'yinchiga hujum shartlari: ikkala tomon ham PvP ga rozi (yoqgan) bo'lishi kerak; himoyadagi yangi o'yinchiga
        /// hujum qilib bo'lmaydi. Hujum qilgan o'yinchi o'zi ham PvP ga rozi hisoblanadi (himoyasidan voz kechadi).
        /// </summary>
        private async Task<string> PvpTargetBlockAsync(Kingdom owner)
        {
            if (owner.IsAi) return null;
            await EnsureStateAsync(owner);
            KingdomWarStateEntity st = await Db.WarStates.AsNoTracking().FirstAsync(s => s.KingdomId == owner.Id);
            if (!st.AcceptsPvp) return owner.Name + " PvP ni o'chirgan";
            if (await IsProtectedAsync(owner)) return owner.Name + " yangi o'yinchi himoyasida (u «PvP ni yoqish» ni bosmagan)";
            return null;
        }

        // ==================================================================
        // QAMAL: o'yinchi shahridagi jangchilar bilan (jang mijozda real vaqtda o'tadi)
        // ==================================================================

        /// <summary>Boshlangan qamallar: saltanat → (qasr, boshlangan vaqt, eng qisqa davom). Server qayta ishga tushsa — tozalanadi.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (int RegionId, DateTime Started, int MinSeconds)> PendingSieges =
            new System.Collections.Concurrent.ConcurrentDictionary<int, (int, DateTime, int)>();

        /// <summary>Qamal uchun eng qisqa vaqt (yurish + jang): bundan tez kelgan "g'alaba" qabul qilinmaydi.</summary>
        public const int SiegeMinBattleSeconds = 20;

        private async Task<string> SiegeBlockAsync(Kingdom me, RegionEntity region)
        {
            if (region == null) return "Qasr topilmadi";
            if (region.OwnerKingdomId == me.Id) return "Bu sizning qasringiz";
            if (region.OwnerKingdomId == 0) return "Mustaqil hudud — uni diplomatik yo'l bilan qo'shib oling";
            Kingdom owner = await Db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(k => k.Id == region.OwnerKingdomId);
            if (owner == null) return "Qasr egasi topilmadi";
            KingdomConflictEntity conflict = await ConflictAsync(me.WorldId, me.Id, owner.Id);
            if (conflict != null && conflict.TruceUntil > Now) return "Sulh amalda — yurish mumkin emas";
            return await PvpTargetBlockAsync(owner);
        }

        public async Task<ActionResultDto> SiegeStartAsync(Kingdom me, SiegeStartRequest req)
        {
            await TickKingdomAsync(me);
            string targetName = "";
            int seconds = 0;
            ActionResultDto res = await _online.RunCommandAsync(me, req?.CommandId, async () =>
            {
                RegionEntity region = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.RegionId && r.WorldId == me.WorldId);
                string block = await SiegeBlockAsync(me, region);
                if (block != null) return block;
                RegionEntity home = await Db.Regions.AsNoTracking().Where(r => r.OwnerKingdomId == me.Id).OrderBy(r => r.Id).FirstOrDefaultAsync();
                if (home == null) return "Avval o'z hududingiz bo'lishi kerak";
                Kingdom owner = await Db.Kingdoms.AsNoTracking().FirstAsync(k => k.Id == region.OwnerKingdomId);
                KingdomConflictEntity conflict = await ConflictAsync(me.WorldId, me.Id, owner.Id);
                if (owner.IsAi && !(conflict != null && conflict.Active))
                {
                    await SetConflictAsync(me.WorldId, me.Id, owner.Id, active: true, truceUntil: null);
                    await _kingdom.AdjustRelationAsync(me.WorldId, me.Id, owner.Id, -WarRules.ConflictRelationPenalty);
                }
                if (!owner.IsAi)
                {
                    await EnsureStateAsync(me);
                    await Db.WarStates.Where(s => s.KingdomId == me.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AcceptsPvp, true).SetProperty(x => x.ProtectionWaived, true));
                }
                seconds = WarRules.MarchSeconds(home.Latitude, home.Longitude, region.Latitude, region.Longitude);
                PendingSieges[me.Id] = (region.Id, Now, seconds + SiegeMinBattleSeconds);
                targetName = region.Name;
                Emit(me.WorldId, "WarNotice", owner.Id, me.Name + " qo'shini " + region.Name + " qasriga yurish boshladi!");
                return null;
            }, "Qo'shin qamalga yo'lga chiqdi");
            if (res.Success && !res.Duplicate && targetName.Length > 0)
                res.MessageUz = "Qo'shiningiz " + targetName + " qasriga yo'lga chiqdi — " + seconds + " soniyada yetib boradi";
            return res;
        }

        public async Task<ActionResultDto> SiegeFinishAsync(Kingdom me, SiegeFinishRequest req)
        {
            await TickKingdomAsync(me);
            string message = "";
            ActionResultDto res = await _online.RunCommandAsync(me, req?.CommandId, async () =>
            {
                if (!PendingSieges.TryGetValue(me.Id, out var pending) || pending.RegionId != req.RegionId) return "Bu qasrga qamal boshlanmagan";
                if (req.Won && (Now - pending.Started).TotalSeconds < pending.MinSeconds) return "Qamal hali tugamagan";
                PendingSieges.TryRemove(me.Id, out _);
                RegionEntity region = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.RegionId && r.WorldId == me.WorldId);
                if (region == null || region.OwnerKingdomId == me.Id || region.OwnerKingdomId == 0) { message = "Qasr egasi o'zgargan — natija hisobga olinmadi"; return null; }
                Kingdom def = await Db.Kingdoms.AsNoTracking().FirstAsync(k => k.Id == region.OwnerKingdomId);
                if (!req.Won)
                {
                    await _kingdom.AdjustRelationAsync(me.WorldId, me.Id, def.Id, -5);
                    message = region.Name + " qamali muvaffaqiyatsiz — qo'shin chekindi";
                    return null;
                }
                // Himoyachilar yo'qotishi: qamalda yiqilganlar ulushida saltanat qo'shinlari kamayadi
                float share = req.DefendersTotal > 0 ? Math.Clamp(req.DefendersDown / (float)req.DefendersTotal, 0f, 1f) : 0.5f;
                foreach (ArmyEntity a in await Db.Armies.AsNoTracking().Where(a => a.KingdomId == def.Id).ToListAsync())
                {
                    int st = await ArmyStrengthAsync(a);
                    if (st > 0) await ApplyLossesAsync(a.Id, (int)(st * share * 0.5f), st, 50);
                }
                if (await IsCapitalOrLastAsync(region))
                {
                    int defCoin = await Db.KingdomResources.Where(r => r.KingdomId == def.Id).Select(r => r.Coin).FirstOrDefaultAsync();
                    int plunder = Math.Min(WarRules.PlunderMax, defCoin * WarRules.PlunderPercent / 100);
                    if (plunder > 0)
                    {
                        int took = await Db.KingdomResources.Where(r => r.KingdomId == def.Id && r.Coin >= plunder).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - plunder));
                        if (took == 1) await Db.KingdomResources.Where(r => r.KingdomId == me.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin + plunder));
                        else plunder = 0;
                    }
                    message = region.Name + " poytaxti qamal qilindi: shoh yengildi, " + plunder + " tanga o'lja olindi. Poytaxt " + def.Name + " tarkibida qoladi.";
                }
                else
                {
                    bool moved = await TransferRegionAsync(me, region, def.Id);
                    message = moved ? region.Name + " endi " + me.Name + " tarkibida! Qasrga bayrog'ingiz tikildi." : region.Name + " egasi o'zgarmadi.";
                    if (moved)
                    {
                        Emit(me.WorldId, "TerritoryOwnershipUpdated", region.Id, me.Id, def.Id);
                        Emit(me.WorldId, "TerritoryTransferred", me.WorldId, region.Id, me.Id, def.Id, region.Name + " qamal natijasida " + me.Name + " tarkibiga o'tdi");
                    }
                }
                await _kingdom.AdjustRelationAsync(me.WorldId, me.Id, def.Id, -10);
                await Db.Kingdoms.Where(k => k.Id == me.Id || k.Id == def.Id).ExecuteUpdateAsync(s => s.SetProperty(k => k.StateVersion, k => k.StateVersion + 1));
                return null;
            }, "Qamal natijasi qabul qilindi");
            if (res.Success && !res.Duplicate && message.Length > 0) res.MessageUz = message;
            return res;
        }

        public async Task<ActionResultDto> MarchAsync(Kingdom me, MarchRequest req)
        {
            await TickKingdomAsync(me);
            int seconds = 0;
            string targetName = "", armyName = "";
            ActionResultDto res = await _online.RunCommandAsync(me, req?.CommandId, async () =>
            {
                RegionEntity region = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.RegionId && r.WorldId == me.WorldId);
                if (region == null) return "Qasr topilmadi";
                if (region.OwnerKingdomId == me.Id) return "Bu sizning qasringiz";
                if (region.OwnerKingdomId == 0) return "Mustaqil hudud — uni diplomatik yo'l bilan qo'shib oling (Saltanat → Hududlar)";
                Kingdom owner = await Db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(k => k.Id == region.OwnerKingdomId);
                if (owner == null) return "Qasr egasi topilmadi";
                ArmyEntity army = await Db.Armies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == req.ArmyId && a.KingdomId == me.Id && !a.IsGarrison);
                string block = await ArmyReadyBlockAsync(army);
                if (block != null) return block;
                KingdomConflictEntity conflict = await ConflictAsync(me.WorldId, me.Id, owner.Id);
                if (conflict != null && conflict.TruceUntil > Now) return "Sulh amalda — yurish mumkin emas";
                string pvp = await PvpTargetBlockAsync(owner);
                if (pvp != null) return pvp;
                int traveling = (int)ArmyStatus.Traveling;
                if (await Db.Armies.AnyAsync(a => a.KingdomId == me.Id && a.Status == traveling && a.TargetRegionId == region.Id))
                    return "Bu qasrga qo'shiningiz allaqachon yo'lda";

                RegionEntity home = await HomeRegionAsync(me, army);
                if (home == null) return "Avval o'z hududingiz bo'lishi kerak";
                seconds = WarRules.MarchSeconds(home.Latitude, home.Longitude, region.Latitude, region.Longitude);
                bool wasActive = conflict != null && conflict.Active;
                int cost = 0; // Yurish tekin (o'yinchi talabi) — ta'minot/konflikt uchun tanga olinmaydi
                if (cost > 0)
                {
                    int paid = await Db.KingdomResources.Where(r => r.KingdomId == me.Id && r.Coin >= cost).ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - cost));
                    if (paid == 0) return "Yurish ta'minoti uchun tanga yetarli emas (" + cost + ")";
                }
                if (owner.IsAi && !wasActive)
                {
                    await SetConflictAsync(me.WorldId, me.Id, owner.Id, active: true, truceUntil: null);
                    await _kingdom.AdjustRelationAsync(me.WorldId, me.Id, owner.Id, -WarRules.ConflictRelationPenalty);
                }
                if (!owner.IsAi)
                {
                    await EnsureStateAsync(me);
                    await Db.WarStates.Where(s => s.KingdomId == me.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AcceptsPvp, true).SetProperty(x => x.ProtectionWaived, true));
                }
                DateTime now = Now, arrive = Now.AddSeconds(seconds);
                int available = (int)ArmyStatus.Available;
                int homeId = home.Id, targetId = region.Id;
                int moved = await Db.Armies.Where(a => a.Id == army.Id && a.Status == available).ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, traveling).SetProperty(a => a.OriginRegionId, homeId).SetProperty(a => a.TargetRegionId, targetId)
                    .SetProperty(a => a.DepartAt, now).SetProperty(a => a.StatusUntil, arrive));
                if (moved == 0) return "Qo'shin holati o'zgardi — qayta urining";
                targetName = region.Name; armyName = army.Name;
                Emit(me.WorldId, "ArmyMarching", army.Id, me.Id, owner.Id, region.Name, seconds, me.Name);
                return null;
            }, "Qo'shin yurishga chiqdi");
            if (res.Success && !res.Duplicate && targetName.Length > 0)
                res.MessageUz = armyName + " " + targetName + " qasriga yo'lga chiqdi — " + seconds + " soniyada yetib boradi";
            return res;
        }

        /// <summary>Qasr garnizoni (militsiya): qasrda tayyor qo'shin bo'lmasa himoya qiladi. Aholiga bog'liq, cheklangan.</summary>
        private async Task<ArmyEntity> EnsureGarrisonAsync(Kingdom k, RegionEntity r)
        {
            ArmyEntity g = await Db.Armies.AsNoTracking().FirstOrDefaultAsync(a => a.KingdomId == k.Id && a.IsGarrison);
            if (g == null)
            {
                g = new ArmyEntity { KingdomId = k.Id, Name = "Qasr garnizoni", RegionId = r.Id, IsGarrison = true, Morale = 65, Readiness = 70 };
                Db.Armies.Add(g);
                await Db.SaveChangesAsync();
                Db.Entry(g).State = EntityState.Detached;
            }
            int pop = await Db.Cities.Where(c => c.RegionId == r.Id).SumAsync(c => (int?)c.Population) ?? 0;
            int size = WarRules.GarrisonSize(pop);
            int line = (int)UnitLine.Piyoda, gid = g.Id;
            int have = await Db.ArmyUnits.Where(u => u.ArmyId == gid && u.Line == line && u.Level == 1).Select(u => u.Count).FirstOrDefaultAsync();
            if (have < size) await AddUnitsAsync(g.Id, UnitLine.Piyoda, 1, size - have);
            return await Db.Armies.AsNoTracking().FirstAsync(a => a.Id == gid);
        }

        /// <summary>Qo'shin qasrga yetib keldi: himoyachi tanlanadi va jang boshlanadi (yoki sharoit o'zgargan bo'lsa qaytadi).</summary>
        private async Task ArriveAsync(ArmyEntity a, Kingdom attacker)
        {
            int traveling = (int)ArmyStatus.Traveling, available = (int)ArmyStatus.Available, inProgress = (int)BattleStatus.InProgress;
            RegionEntity r = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == a.TargetRegionId);
            if (r == null || r.OwnerKingdomId == attacker.Id || r.OwnerKingdomId == 0)
            {
                await Db.Armies.Where(x => x.Id == a.Id && x.Status == traveling).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, available));
                Emit(attacker.WorldId, "WarNotice", attacker.Id, a.Name + ": qasr egasi o'zgardi — qo'shin qaytdi");
                return;
            }
            Kingdom def = await Db.Kingdoms.AsNoTracking().FirstAsync(k => k.Id == r.OwnerKingdomId);
            if (await Db.Battles.AnyAsync(b => b.Status == inProgress && (b.AttackerKingdomId == attacker.Id || b.DefenderKingdomId == attacker.Id || b.AttackerKingdomId == def.Id || b.DefenderKingdomId == def.Id)))
            {
                DateTime later = Now.AddSeconds(10); // boshqa jang tugashini kutadi
                await Db.Armies.Where(x => x.Id == a.Id && x.Status == traveling).ExecuteUpdateAsync(s => s.SetProperty(x => x.StatusUntil, later));
                return;
            }
            if (!def.IsAi && await PvpTargetBlockAsync(def) != null)
            {
                await Db.Armies.Where(x => x.Id == a.Id && x.Status == traveling).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, available));
                Emit(attacker.WorldId, "WarNotice", attacker.Id, a.Name + ": " + def.Name + " endi PvP qabul qilmaydi — qo'shin qaytdi");
                return;
            }
            int inBattle = (int)ArmyStatus.InBattle;
            int claimed = await Db.Armies.Where(x => x.Id == a.Id && x.Status == traveling).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, inBattle));
            if (claimed == 0) return;

            await EnsureStateAsync(def);
            // Himoyachi: eng kuchli tayyor qo'shini, bo'lmasa — qasr garnizoni.
            ArmyEntity dArmy = null; int best = -1;
            foreach (ArmyEntity x in await Db.Armies.AsNoTracking().Where(x => x.KingdomId == def.Id && !x.IsGarrison && x.Status == available).ToListAsync())
            {
                int st = await ArmyStrengthAsync(x);
                if (st > best && WarRules.CombatUnitCount(Tuples(await GroupsAsync(x.Id))) > 0) { best = st; dArmy = x; }
            }
            if (dArmy == null) dArmy = await EnsureGarrisonAsync(def, r);
            if (def.IsAi) await AiReplenishAsync(def, dArmy);
            bool capital = await IsCapitalOrLastAsync(r);
            ArmyEntity attackerArmy = await Db.Armies.AsNoTracking().FirstAsync(x => x.Id == a.Id);
            BattleEntity b = await CreateBattleAsync(attacker.WorldId, isPvp: !def.IsAi, r.Id, territoryStake: !capital, attacker, attackerArmy, def, dArmy, WarRules.MarchCost);
            if (capital) await Db.Battles.Where(x => x.Id == b.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsCapitalSiege, true));
            if (def.IsAi) await AddAiDecisionAsync(b, def);
            Emit(attacker.WorldId, "BattleStarted", b.Id, attacker.Id, def.Id);
        }

        /// <summary>Server tiki (har 2 soniya, o'yinchilar offline bo'lsa ham): yetib kelgan yurishlar va faol janglar.</summary>
        public async Task TickWorldAsync(int worldId)
        {
            DateTime now = Now;
            int traveling = (int)ArmyStatus.Traveling;
            var kingdomIds = await Db.Kingdoms.Where(k => k.WorldId == worldId).Select(k => k.Id).ToListAsync();
            var arrived = await Db.Armies.AsNoTracking().Where(a => a.Status == traveling && a.StatusUntil <= now && kingdomIds.Contains(a.KingdomId)).ToListAsync();
            foreach (ArmyEntity a in arrived)
            {
                Kingdom k = await Db.Kingdoms.AsNoTracking().FirstAsync(x => x.Id == a.KingdomId);
                await using var tx = await Db.Database.BeginTransactionAsync();
                try
                {
                    await ArriveAsync(a, k);
                    await tx.CommitAsync();
                }
                catch (DbUpdateException) { await tx.RollbackAsync(); Events.Clear(); }
                Db.ChangeTracker.Clear();
            }
            int inProgress = (int)BattleStatus.InProgress;
            foreach (BattleEntity b in await Db.Battles.AsNoTracking().Where(b => b.WorldId == worldId && b.Status == inProgress).ToListAsync())
                await TickOneBattleAsync(b);
        }

        private async Task FillMapAsync(Kingdom me, WarOverviewDto dto)
        {
            var regions = await Db.Regions.AsNoTracking().Where(r => r.WorldId == me.WorldId).OrderBy(r => r.Id).ToListAsync();
            var kingdoms = await Db.Kingdoms.AsNoTracking().Where(k => k.WorldId == me.WorldId).ToDictionaryAsync(k => k.Id);
            var regionIds = regions.Select(r => r.Id).ToList();
            var cities = await Db.Cities.AsNoTracking().Where(c => regionIds.Contains(c.RegionId)).ToListAsync();
            var strength = new Dictionary<int, int>();
            async Task<int> StrengthOf(int kid)
            {
                if (strength.TryGetValue(kid, out int v)) return v;
                int best = 0;
                foreach (ArmyEntity a in await Db.Armies.AsNoTracking().Where(a => a.KingdomId == kid && !a.IsGarrison).ToListAsync())
                    best = Math.Max(best, await ArmyStrengthAsync(a));
                int def = await FacilityLevelAsync(kid, FacilityKind.Mudofaa);
                strength[kid] = v = (int)(best * (1 + def * 0.08));
                return v;
            }
            var ownerBlock = new Dictionary<int, string>();
            RegionEntity home = regions.FirstOrDefault(r => r.OwnerKingdomId == me.Id);
            bool haveReady = dto.Armies.Any(a => a.Status == ArmyStatus.Available && a.UnitCount >= WarRules.MinBattleArmy);
            foreach (RegionEntity r in regions)
            {
                kingdoms.TryGetValue(r.OwnerKingdomId, out Kingdom owner);
                var rc = cities.Where(c => c.RegionId == r.Id).ToList();
                int pop = rc.Sum(c => c.Population);
                bool capital = rc.Any(c => c.IsCapital) || (owner != null && regions.Count(x => x.OwnerKingdomId == owner.Id) <= 1);
                var m = new WarMapRegionDto
                {
                    RegionId = r.Id, Name = r.Name, CityName = rc.FirstOrDefault()?.Name ?? r.Name, OwnerKingdomId = r.OwnerKingdomId,
                    OwnerName = owner?.Name ?? "Mustaqil", OwnerIsAi = owner?.IsAi ?? true, IsMine = r.OwnerKingdomId == me.Id, IsCapital = capital,
                    Color = owner == null ? new[] { 150, 150, 150 } : new[] { owner.ColorR, owner.ColorG, owner.ColorB },
                    Flag = WorldSeeder.FlagOf(owner), Province = r.Province ?? "",
                    MapX = (float)r.Latitude, MapY = (float)r.Longitude, Population = pop
                };
                if (owner != null && !m.IsMine)
                {
                    int st = await StrengthOf(owner.Id);
                    m.DefenseEstimate = st > 0 ? st : WarRules.GarrisonSize(pop) * 10;
                }
                if (home != null) m.MarchSeconds = WarRules.MarchSeconds(home.Latitude, home.Longitude, r.Latitude, r.Longitude);
                string block = m.IsMine ? "Sizning qasringiz" : owner == null ? "Mustaqil hudud — diplomatik yo'l bilan qo'shib oling" : null;
                if (block == null)
                {
                    if (!ownerBlock.TryGetValue(owner.Id, out string ob))
                    {
                        KingdomConflictEntity c = await ConflictAsync(me.WorldId, me.Id, owner.Id);
                        ob = c != null && c.TruceUntil > Now ? "Sulh amalda" : await PvpTargetBlockAsync(owner);
                        ownerBlock[owner.Id] = ob;
                    }
                    block = ob;
                    if (block == null && !haveReady) block = "Tayyor qo'shin yo'q (kamida " + WarRules.MinBattleArmy + " birlik)";
                    if (block == null && dto.Marches.Any(x => x.IsMine && x.TargetRegionId == r.Id)) block = "Qo'shiningiz allaqachon yo'lda";
                }
                m.CanMarch = block == null;
                // Qamal (shahardagi jangchilar bilan) uchun server qo'shini shart emas
                m.CanSiege = owner != null && !m.IsMine && (block == null || block.StartsWith("Tayyor qo'shin yo'q") || block.StartsWith("Qo'shiningiz allaqachon"));
                m.RulerName = owner?.RulerName ?? "";
                m.BlockReasonUz = block ?? "";
                if (owner != null && !m.IsMine)
                {
                    int est = 0;
                    if (capital) est = Math.Min(WarRules.PlunderMax, (await Db.KingdomResources.Where(x => x.KingdomId == owner.Id).Select(x => x.Coin).FirstOrDefaultAsync()) * WarRules.PlunderPercent / 100);
                    m.OutcomeUz = capital
                        ? "Poytaxt: g'alabada xazinadan o'lja (~" + est + " tanga) olinadi, qasr egasida qoladi."
                        : "G'alabada " + r.Name + " qasri, shaharlari va aholisi saltanatingizga o'tadi.";
                    if (!owner.IsAi) m.OutcomeUz += " Real o'yinchi: u onlayn bo'lsa o'zi himoya qiladi, aks holda qasr sarkardasi.";
                }
                dto.MapRegions.Add(m);
            }
        }

        private async Task FillMarchesAsync(Kingdom me, WarOverviewDto dto)
        {
            int traveling = (int)ArmyStatus.Traveling;
            var kingdoms = await Db.Kingdoms.AsNoTracking().Where(k => k.WorldId == me.WorldId).ToDictionaryAsync(k => k.Id);
            var ids = kingdoms.Keys.ToList();
            var regionNames = await Db.Regions.AsNoTracking().Where(r => r.WorldId == me.WorldId).ToDictionaryAsync(r => r.Id, r => r);
            foreach (ArmyEntity a in await Db.Armies.AsNoTracking().Where(a => a.Status == traveling && ids.Contains(a.KingdomId)).ToListAsync())
            {
                Kingdom k = kingdoms[a.KingdomId];
                regionNames.TryGetValue(a.TargetRegionId, out RegionEntity t);
                var g = await GroupsAsync(a.Id);
                dto.Marches.Add(new MarchDto
                {
                    ArmyId = a.Id, ArmyName = a.Name, KingdomId = k.Id, KingdomName = k.Name, Color = new[] { k.ColorR, k.ColorG, k.ColorB }, Flag = WorldSeeder.FlagOf(k),
                    OriginRegionId = a.OriginRegionId, TargetRegionId = a.TargetRegionId, TargetName = t?.Name ?? "?",
                    TotalSeconds = Math.Max(1, (int)(a.StatusUntil - a.DepartAt).TotalSeconds), SecondsLeft = Math.Max(0, (int)(a.StatusUntil - Now).TotalSeconds),
                    UnitCount = g.Sum(x => x.Count), IsMine = k.Id == me.Id, TargetIsMine = t != null && t.OwnerKingdomId == me.Id
                });
            }
        }

        // ==================================================================
        // Tinchlik
        // ==================================================================
        public Task<ActionResultDto> ProposePeaceAsync(Kingdom me, PeaceRequest req) => _online.RunCommandAsync(me, req?.CommandId, async () =>
        {
            Kingdom other = await Db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(k => k.Id == req.KingdomId && k.WorldId == me.WorldId);
            if (other == null || other.Id == me.Id) return "Saltanat topilmadi";
            KingdomConflictEntity c = await ConflictAsync(me.WorldId, me.Id, other.Id);
            if (c == null || !c.Active) return "Bu saltanat bilan faol mojaro yo'q";
            int inProgress = (int)BattleStatus.InProgress;
            if (await Db.Battles.AnyAsync(b => b.Status == inProgress && ((b.AttackerKingdomId == me.Id && b.DefenderKingdomId == other.Id) || (b.AttackerKingdomId == other.Id && b.DefenderKingdomId == me.Id))))
                return "Jang davom etmoqda — jang ichida «Muzokara taklifi» ni tanlang";
            int relation = await _kingdom.RelationAsync(me.WorldId, me.Id, other.Id);
            if (other.IsAi)
            {
                await EnsureStateAsync(other);
                ArmyEntity aiArmy = await Db.Armies.AsNoTracking().Where(a => a.KingdomId == other.Id).OrderBy(a => a.Id).FirstOrDefaultAsync();
                ArmyEntity myArmy = await Db.Armies.AsNoTracking().Where(a => a.KingdomId == me.Id).OrderBy(a => a.Id).FirstOrDefaultAsync();
                int aiPower = aiArmy == null ? 0 : await ArmyStrengthAsync(aiArmy);
                int myPower = myArmy == null ? 0 : await ArmyStrengthAsync(myArmy);
                if (!WarRules.AiAcceptsPeace(other.AiStrategy, relation, aiPower, myPower))
                    return other.Name + " tinchlik taklifini rad etdi (munosabat juda past). Elchi yuborib munosabatni yaxshilang.";
            }
            await SetConflictAsync(me.WorldId, me.Id, other.Id, active: false, truceUntil: Now.AddHours(WarRules.TruceHours));
            await _kingdom.AdjustRelationAsync(me.WorldId, me.Id, other.Id, 15);
            Emit(me.WorldId, "PeaceAgreed", me.Id, other.Id);
            return null;
        }, "Tinchlik shartnomasi tuzildi: " + WarRules.TruceHours + " soatlik sulh, munosabat +15, savdo tiklandi");

        // ==================================================================
        // PvP (faqat ikki tomon rozi bo'lsa)
        // ==================================================================
        public Task<ActionResultDto> InviteAsync(Kingdom me, PvpInviteRequest req) => _online.RunCommandAsync(me, req?.CommandId, async () =>
        {
            await EnsureStateAsync(me);
            Kingdom to = await Db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(k => k.Id == req.ToKingdomId && k.WorldId == me.WorldId);
            if (to == null || to.Id == me.Id || to.IsAi) return "Real o'yinchi saltanatini tanlang";
            await EnsureStateAsync(to);
            KingdomWarStateEntity st = await Db.WarStates.AsNoTracking().FirstAsync(s => s.KingdomId == to.Id);
            if (!st.AcceptsPvp) return to.Name + " PvP takliflarini qabul qilmaydi";
            if (await IsProtectedAsync(to)) return to.Name + " yangi o'yinchi himoyasida";
            ArmyEntity army = await Db.Armies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == req.ArmyId && a.KingdomId == me.Id);
            string block = await ArmyReadyBlockAsync(army);
            if (block != null) return block;
            int pending = (int)InvitationStatus.Pending;
            DateTime now = Now;
            if (await Db.BattleInvitations.CountAsync(i => i.FromKingdomId == me.Id && i.Status == pending && i.ExpiresAt > now) >= WarRules.MaxPendingInvitations)
                return "Javob kutilayotgan takliflar juda ko'p (" + WarRules.MaxPendingInvitations + ")";
            if (await Db.BattleInvitations.AnyAsync(i => i.FromKingdomId == me.Id && i.ToKingdomId == to.Id && i.Status == pending && i.ExpiresAt > now))
                return "Bu o'yinchiga taklif allaqachon yuborilgan";
            if (req.StakeRegionId != 0)
            {
                RegionEntity stake = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.StakeRegionId && r.OwnerKingdomId == to.Id);
                if (stake == null) return "Garov hududi raqibga tegishli emas";
                if (await Db.Regions.CountAsync(r => r.WorldId == me.WorldId && r.OwnerKingdomId == to.Id) <= 1) return "Raqibning yagona hududini garovga qo'yib bo'lmaydi";
            }
            var inv = new BattleInvitationEntity { WorldId = me.WorldId, FromKingdomId = me.Id, FromArmyId = army.Id, ToKingdomId = to.Id, StakeRegionId = req.StakeRegionId, Status = pending, CreatedAt = now, ExpiresAt = now.AddMinutes(WarRules.InvitationMinutes) };
            Db.BattleInvitations.Add(inv);
            await Db.SaveChangesAsync();
            Emit(me.WorldId, "BattleInvitationCreated", inv.Id, me.Id, to.Id, me.Name);
            return null;
        }, "PvP jang taklifi yuborildi");

        public async Task<(ActionResultDto result, int battleId)> RespondAsync(Kingdom me, PvpRespondRequest req)
        {
            int battleId = 0;
            BattleInvitationEntity invSnapshot = null;
            ActionResultDto res = await _online.RunCommandAsync(me, req?.CommandId, async () =>
            {
                BattleInvitationEntity inv = await Db.BattleInvitations.AsNoTracking().FirstOrDefaultAsync(i => i.Id == req.InvitationId && i.ToKingdomId == me.Id);
                if (inv == null) return "Taklif topilmadi";
                if ((InvitationStatus)inv.Status != InvitationStatus.Pending || inv.ExpiresAt < Now) return "Taklif endi faol emas";
                invSnapshot = inv;
                int pending = (int)InvitationStatus.Pending;
                if (!req.Accept)
                {
                    int declined = (int)InvitationStatus.Declined;
                    await Db.BattleInvitations.Where(i => i.Id == inv.Id && i.Status == pending).ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, declined));
                    Emit(me.WorldId, "BattleInvitationDeclined", inv.Id, inv.FromKingdomId, me.Id);
                    return null;
                }
                Kingdom from = await Db.Kingdoms.AsNoTracking().FirstAsync(k => k.Id == inv.FromKingdomId);
                ArmyEntity fromArmy = await Db.Armies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == inv.FromArmyId);
                string fb = await ArmyReadyBlockAsync(fromArmy);
                if (fb != null) return "Taklif yuboruvchining qo'shini tayyor emas: " + fb;
                ArmyEntity myArmy = await Db.Armies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == req.ArmyId && a.KingdomId == me.Id);
                string mb = await ArmyReadyBlockAsync(myArmy);
                if (mb != null) return mb;
                int inProgress = (int)BattleStatus.InProgress;
                if (await Db.Battles.AnyAsync(b => b.Status == inProgress && (b.AttackerKingdomId == me.Id || b.DefenderKingdomId == me.Id || b.AttackerKingdomId == from.Id || b.DefenderKingdomId == from.Id)))
                    return "Ishtirokchilardan birida faol jang bor";
                int accepted = (int)InvitationStatus.Accepted;
                int took = await Db.BattleInvitations.Where(i => i.Id == inv.Id && i.Status == pending).ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, accepted));
                if (took == 0) return "Taklif allaqachon ko'rib chiqilgan";
                int region = inv.StakeRegionId != 0 ? inv.StakeRegionId : await Db.Regions.Where(r => r.OwnerKingdomId == me.Id && r.WorldId == me.WorldId).Select(r => r.Id).FirstOrDefaultAsync();
                BattleEntity b = await CreateBattleAsync(me.WorldId, isPvp: true, region, territoryStake: inv.StakeRegionId != 0, from, fromArmy, me, myArmy, 0);
                await Db.BattleInvitations.Where(i => i.Id == inv.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.BattleId, b.Id));
                battleId = b.Id;
                return null;
            }, req != null && req.Accept ? "PvP jang boshlandi" : "Taklif rad etildi");
            if (res.Success && !res.Duplicate && battleId > 0 && invSnapshot != null)
            {
                Emit(me.WorldId, "BattleInvitationAccepted", invSnapshot.Id, battleId, invSnapshot.FromKingdomId, me.Id);
                Emit(me.WorldId, "BattleStarted", battleId, invSnapshot.FromKingdomId, me.Id);
            }
            return (res, battleId);
        }
    }
}
