using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AndijonSaltanatlar.Server.Data;
using AndijonSaltanatlar.Shared;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Shared.Farming;

namespace AndijonSaltanatlar.Server.Services
{
    /// <summary>
    /// PROMPT 19B — KINGDOM EXPANSION (shahar ichidagi yer ochishdan ALOHIDA), ko'p shaharli boshqaruv va hokimlar.
    ///
    /// Hudud transferi atomik: <c>UPDATE Regions SET Owner=@new WHERE Id=@r AND Owner=@old</c> — bitta hudud bir vaqtda ikki
    /// davlatga o'tib keta olmaydi. Shaharlar (aholisi, oilalari, binolari bilan) shu tranzaksiyada yangi saltanatga
    /// biriktiriladi. Aholi shaharda saqlanadi — saltanat aholisi har doim shaharlar yig'indisidan hisoblanadi
    /// (alohida hisoblagich yo'q, shuning uchun fuqaro ikki marta sanalmaydi).
    /// </summary>
    public sealed class KingdomService
    {
        public const int HouseCost = 50, MarketCost = 200, GardenCost = 90;

        private readonly OnlineGameService _online;
        private GameDbContext Db => _online.Db;

        public KingdomService(OnlineGameService online) => _online = online;

        // ------------------------------------------------------------------
        // Diplomatik munosabat (juftlik bo'yicha bitta qator, A < B)
        // ------------------------------------------------------------------
        public static (int a, int b) Pair(int x, int y) => x < y ? (x, y) : (y, x);

        public async Task<int> RelationAsync(int worldId, int k1, int k2)
        {
            (int a, int b) = Pair(k1, k2);
            return await Db.DiplomaticRelations.AsNoTracking()
                .Where(r => r.WorldId == worldId && r.KingdomA == a && r.KingdomB == b).Select(r => r.Value).FirstOrDefaultAsync();
        }

        public async Task AdjustRelationAsync(int worldId, int k1, int k2, int delta)
        {
            (int a, int b) = Pair(k1, k2);
            int changed = await Db.DiplomaticRelations.Where(r => r.WorldId == worldId && r.KingdomA == a && r.KingdomB == b)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Value, r => r.Value + delta > 100 ? 100 : (r.Value + delta < -100 ? -100 : r.Value + delta)));
            if (changed == 0)
            {
                Db.DiplomaticRelations.Add(new DiplomaticRelationEntity { WorldId = worldId, KingdomA = a, KingdomB = b, Value = Math.Clamp(delta, -100, 100) });
                await Db.SaveChangesAsync();
            }
        }

        // ------------------------------------------------------------------
        // Hududlar va xarita
        // ------------------------------------------------------------------
        public async Task<List<RegionDto>> RegionsAsync(Kingdom me)
        {
            var regions = await Db.Regions.AsNoTracking().Where(r => r.WorldId == me.WorldId).OrderBy(r => r.Id).ToListAsync();
            var kingdoms = await Db.Kingdoms.AsNoTracking().Where(k => k.WorldId == me.WorldId).ToDictionaryAsync(k => k.Id);
            var cities = await Db.Cities.AsNoTracking().Where(c => regions.Select(r => r.Id).Contains(c.RegionId)).ToListAsync();
            var relations = await Db.DiplomaticRelations.AsNoTracking().Where(r => r.WorldId == me.WorldId && (r.KingdomA == me.Id || r.KingdomB == me.Id)).ToListAsync();
            var regionCount = regions.GroupBy(r => r.OwnerKingdomId).ToDictionary(g => g.Key, g => g.Count());
            int coin = await Db.KingdomResources.AsNoTracking().Where(r => r.KingdomId == me.Id).Select(r => r.Coin).FirstOrDefaultAsync();

            var list = new List<RegionDto>();
            foreach (RegionEntity r in regions)
            {
                kingdoms.TryGetValue(r.OwnerKingdomId, out Kingdom owner);
                var rc = cities.Where(c => c.RegionId == r.Id).ToList();
                int pop = rc.Sum(c => c.Population);
                int rel = owner == null ? 0 : relations.Where(x => x.KingdomA == owner.Id || x.KingdomB == owner.Id).Select(x => x.Value).FirstOrDefault();
                var dto = new RegionDto
                {
                    Id = r.Id, Name = r.Name, OwnerKingdomId = r.OwnerKingdomId, OwnerName = owner?.Name ?? "Mustaqil hudud",
                    ColorR = owner?.ColorR ?? 150, ColorG = owner?.ColorG ?? 150, ColorB = owner?.ColorB ?? 140,
                    Flag = WorldSeeder.FlagOf(owner), Province = r.Province ?? "",
                    MapX = (float)r.Latitude, MapY = (float)r.Longitude, Population = pop, Development = r.Development, Stability = r.Stability,
                    Cities = rc.Select(c => c.Name).ToList(), IsMine = r.OwnerKingdomId == me.Id, Relation = rel,
                    AnnexCost = TerritoryRules.AnnexCost(pop, r.Development)
                };
                dto.AnnexBlockReasonUz = AnnexBlock(r, owner, rel, regionCount.GetValueOrDefault(r.OwnerKingdomId), dto.AnnexCost, coin, me.Id);
                dto.CanAnnex = dto.AnnexBlockReasonUz.Length == 0;
                list.Add(dto);
            }
            return list;
        }

        private static string AnnexBlock(RegionEntity r, Kingdom owner, int relation, int ownerRegions, int cost, int coin, int meId)
        {
            if (r.OwnerKingdomId == meId) return "Bu hudud allaqachon sizniki";
            if (owner != null && !owner.IsAi) return "O'yinchi hududi — o'yinchilar o'rtasidagi kelishuvlar keyingi bosqichda (PROMPT 20)";
            if (owner != null && TerritoryRules.ProtectLastRegion && ownerRegions <= 1) return "Bu " + owner.Name + "ning so'nggi hududi — birlashtirib bo'lmaydi";
            if (owner != null && relation < TerritoryRules.RequiredRelation) return "Munosabat yetarli emas: " + relation + " / " + TerritoryRules.RequiredRelation + " (elchi yuboring)";
            if (coin < cost) return "Tanga yetarli emas: " + cost;
            return "";
        }

        /// <summary>Elchi yuborish: munosabatni oshiradi (abstrakt diplomatiya).</summary>
        public Task<ActionResultDto> SendEnvoyAsync(Kingdom me, string commandId, int regionId) => _online.RunCommandAsync(me, commandId, async () =>
        {
            RegionEntity r = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == regionId && x.WorldId == me.WorldId);
            if (r == null) return "Hudud topilmadi";
            if (r.OwnerKingdomId == 0) return "Mustaqil hududga elchi shart emas — to'g'ridan-to'g'ri birlashish mumkin";
            if (r.OwnerKingdomId == me.Id) return "Bu sizning hududingiz";
            int paid = await Db.KingdomResources.Where(x => x.KingdomId == me.Id && x.Coin >= TerritoryRules.EnvoyCost)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Coin, x => x.Coin - TerritoryRules.EnvoyCost));
            if (paid == 0) return "Tanga yetarli emas (" + TerritoryRules.EnvoyCost + ")";
            await AdjustRelationAsync(me.WorldId, me.Id, r.OwnerKingdomId, TerritoryRules.EnvoyRelationGain);
            return null;
        }, "Elchi yuborildi: munosabat +" + TerritoryRules.EnvoyRelationGain + " (-" + TerritoryRules.EnvoyCost + " tanga)");

        /// <summary>
        /// Diplomatik birlashish — hudud egasi o'zgaradi. Bitta tranzaksiyada:
        ///  1) Region.Owner (shartli: eski ega o'zgarmagan bo'lsa); 2) tanga; 3) shaharlar → yangi saltanat (aholi, oilalar,
        ///  ish joylari o'z joyida qoladi); 4) binolar → yangi saltanat; 5) siyosiy ishonch yangi boshqaruvga moslashadi;
        ///  6) ikki saltanat StateVersion++. Xato bo'lsa hammasi bekor — eski ma'lumot buzilmaydi.
        /// </summary>
        public async Task<(ActionResultDto result, RegionEntity region, int oldOwner)> AnnexAsync(Kingdom me, string commandId, int regionId)
        {
            RegionEntity annexed = null;
            int oldOwner = -1;
            ActionResultDto res = await _online.RunCommandAsync(me, commandId, async () =>
            {
                RegionEntity r = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == regionId && x.WorldId == me.WorldId);
                if (r == null) return "Hudud topilmadi";
                Kingdom owner = r.OwnerKingdomId == 0 ? null : await Db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(k => k.Id == r.OwnerKingdomId);
                int ownerRegions = await Db.Regions.CountAsync(x => x.WorldId == me.WorldId && x.OwnerKingdomId == r.OwnerKingdomId);
                int relation = owner == null ? 0 : await RelationAsync(me.WorldId, me.Id, owner.Id);
                int pop = await Db.Cities.Where(c => c.RegionId == r.Id).SumAsync(c => c.Population);
                int cost = TerritoryRules.AnnexCost(pop, r.Development);
                int coin = await Db.KingdomResources.AsNoTracking().Where(x => x.KingdomId == me.Id).Select(x => x.Coin).FirstAsync();
                string block = AnnexBlock(r, owner, relation, ownerRegions, cost, coin, me.Id);
                if (block.Length > 0) return block;

                int moved = await Db.Regions.Where(x => x.Id == r.Id && x.OwnerKingdomId == r.OwnerKingdomId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerKingdomId, me.Id).SetProperty(x => x.Stability, x => x.Stability > 15 ? x.Stability - 15 : 0).SetProperty(x => x.Fortify, 0));
                if (moved == 0) return "Hudud egasi hozirgina o'zgardi — qayta urining";
                int paid = await Db.KingdomResources.Where(x => x.KingdomId == me.Id && x.Coin >= cost)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Coin, x => x.Coin - cost));
                if (paid == 0) return "Tanga yetarli emas: " + cost;

                var cityIds = await Db.Cities.Where(c => c.RegionId == r.Id).Select(c => c.Id).ToListAsync();
                // Shaharlar aholisi bilan birga o'tadi; yangi boshqaruvga ishonch biroz pasayadi va vaqt bilan tiklanadi.
                await Db.Cities.Where(c => c.RegionId == r.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.KingdomId, me.Id).SetProperty(c => c.IsCapital, false)
                                              .SetProperty(c => c.Trust, c => c.Trust > 45 ? c.Trust - 15 : 30)
                                              .SetProperty(c => c.GovernorMode, (int)GovernorMode.Manual));
                await Db.Buildings.Where(b => cityIds.Contains(b.CityId)).ExecuteUpdateAsync(s => s.SetProperty(b => b.KingdomId, me.Id));
                await Db.GovernorPlans.Where(p => cityIds.Contains(p.CityId) && p.Status == 0).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, 2));
                if (owner != null)
                {
                    await AdjustRelationAsync(me.WorldId, me.Id, owner.Id, -10);
                    await Db.Kingdoms.Where(x => x.Id == owner.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.StateVersion, x => x.StateVersion + 1));
                }
                annexed = r;
                oldOwner = r.OwnerKingdomId;
                return null;
            }, "Hudud saltanatga qo'shildi");
            if (res.Success && !res.Duplicate && annexed != null)
                res.MessageUz = annexed.Name + " saltanatga qo'shildi! Shahar va aholi endi sizning boshqaruvingizda.";
            await RefreshPopulationAsync(me.WorldId);
            return (res, annexed, oldOwner);
        }

        /// <summary>Kingdom.Population — faqat ko'rsatish keshi; har doim shaharlardan qayta hisoblanadi.</summary>
        public async Task RefreshPopulationAsync(int worldId)
        {
            var sums = await Db.Cities.Where(c => c.KingdomId != 0).GroupBy(c => c.KingdomId).Select(g => new { g.Key, Pop = g.Sum(c => c.Population) }).ToListAsync();
            foreach (var kv in sums)
                await Db.Kingdoms.Where(k => k.Id == kv.Key && k.WorldId == worldId).ExecuteUpdateAsync(s => s.SetProperty(k => k.Population, kv.Pop));
        }

        // ------------------------------------------------------------------
        // Shaharlar va umumiy ko'rinish
        // ------------------------------------------------------------------
        public async Task<KingdomOverviewDto> OverviewAsync(Kingdom me)
        {
            var cities = await Db.Cities.AsNoTracking().Where(c => c.KingdomId == me.Id).OrderByDescending(c => c.IsCapital).ThenBy(c => c.Id).ToListAsync();
            var regionNames = await Db.Regions.AsNoTracking().Where(r => r.WorldId == me.WorldId).ToDictionaryAsync(r => r.Id, r => r.Name);
            var pending = await Db.GovernorPlans.AsNoTracking().Where(p => p.KingdomId == me.Id && p.Status == 0).ToListAsync();
            var buildingCounts = await Db.Buildings.AsNoTracking().Where(b => b.KingdomId == me.Id).GroupBy(b => b.CityId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            OnlineStateDto st = await _online.StateAsync(me);

            var dto = new KingdomOverviewDto
            {
                KingdomId = me.Id, Name = me.Name, Ruler = me.RulerName, Coin = st.Coin,
                Flag = WorldSeeder.FlagOf(me), Color = new[] { me.ColorR, me.ColorG, me.ColorB },
                Capital = cities.FirstOrDefault(c => c.IsCapital)?.Name ?? cities.FirstOrDefault()?.Name ?? "",
                Population = cities.Sum(c => c.Population), CityCount = cities.Count, Inventory = st.Inventory
            };
            foreach (CityEntity c in cities)
                dto.Cities.Add(ToCityDto(c, regionNames.GetValueOrDefault(c.RegionId, ""), pending.FirstOrDefault(p => p.CityId == c.Id), buildingCounts.GetValueOrDefault(c.Id)));
            dto.Regions = await RegionsAsync(me);
            dto.RegionCount = dto.Regions.Count(r => r.IsMine);
            dto.DevelopmentScore = dto.Regions.Where(r => r.IsMine).Sum(r => r.Development) + dto.Population / 20;

            foreach (Kingdom k in await Db.Kingdoms.AsNoTracking().Where(k => k.WorldId == me.WorldId && k.Id != me.Id).ToListAsync())
                dto.Relations.Add(new KingdomRelationDto
                {
                    KingdomId = k.Id, Name = k.Name, IsAi = k.IsAi, Value = await RelationAsync(me.WorldId, me.Id, k.Id),
                    Regions = dto.Regions.Count(r => r.OwnerKingdomId == k.Id)
                });
            dto.AdvisorNotes = AdvisorNotes(dto, st);
            return dto;
        }

        public static CityDto ToCityDto(CityEntity c, string regionName, GovernorPlanEntity plan, int buildings)
        {
            var dto = new CityDto
            {
                Id = c.Id, Name = c.Name, RegionId = c.RegionId, RegionName = regionName, IsCapital = c.IsCapital,
                Population = c.Population, Households = c.Households, HousingCapacity = c.HousingCapacity, Jobs = c.Jobs,
                Level = c.Level, Happiness = c.Happiness, Trust = c.Trust, Buildings = buildings,
                // Shahar daromadi: aholi solig'i bazasi (2 tanga/kishi * 10%) + ish o'rinlari; xarajat: xizmatlar.
                DailyIncome = c.Population / 5 + Math.Min(c.Jobs, c.Population) / 10,
                DailyExpense = c.Population / 8 + 2,
                GovernorMode = (GovernorMode)c.GovernorMode, GovernorName = c.GovernorName, GovernorSpendLimit = c.GovernorSpendLimit
            };
            dto.Problems = CityProblems(c);
            if (plan != null) dto.PendingPlan = ToPlanDto(plan);
            return dto;
        }

        public static List<string> CityProblems(CityEntity c)
        {
            var p = new List<string>();
            if (c.Population >= c.HousingCapacity * 0.9) p.Add("Uy-joy yetishmaydi (" + c.Population + "/" + c.HousingCapacity + ")");
            if (c.Jobs < c.Population * 0.55) p.Add("Ish o'rinlari kam (" + c.Jobs + " ta)");
            if (c.Happiness < 55) p.Add("Aholi mamnunligi past (" + c.Happiness + ")");
            if (c.Trust < 50) p.Add("Yangi boshqaruvga ishonch past (" + c.Trust + ")");
            return p;
        }

        private static GovernorPlanDto ToPlanDto(GovernorPlanEntity p) => new GovernorPlanDto
        {
            Id = p.Id, CityId = p.CityId, TitleUz = p.TitleUz, ReasonUz = p.ReasonUz, Houses = p.Houses, Markets = p.Markets,
            Gardens = p.Gardens, Cost = p.Cost, Status = p.Status == 0 ? "Tasdiq kutilmoqda" : p.Status == 1 ? "Bajarilmoqda" : "Rad etildi"
        };

        /// <summary>Donishmand Vazir eslatmalari — faqat haqiqiy holatdan.</summary>
        private static List<string> AdvisorNotes(KingdomOverviewDto k, OnlineStateDto st)
        {
            var notes = new List<string>();
            foreach (CityDto c in k.Cities.Where(c => !c.IsCapital && c.Trust < 50))
                notes.Add("Hazratim, saltanatimizga " + c.Name + " qo'shildi. U yerda uy-joy va savdo infratuzilmasini rivojlantirishni tavsiya qilaman.|city:" + c.Id);
            foreach (CityDto c in k.Cities.Where(c => c.PendingPlan != null))
                notes.Add(c.Name + " hokimi qurilish rejasini tayyorladi (" + c.PendingPlan.Cost + " tanga) — tasdig'ingizni kutmoqda.|city:" + c.Id);
            int moving = st.Caravans.Count(c => c.Status == 0);
            if (moving > 0) notes.Add("Yo'lda " + moving + " ta savdo karvoni bor.|market");
            foreach (RegionDto r in k.Regions.Where(r => r.CanAnnex).Take(1))
                notes.Add(r.Name + " hududini saltanatga qo'shish mumkin (" + r.AnnexCost + " tanga, aholi " + r.Population + ").|region:" + r.Id);
            if (notes.Count == 0) notes.Add("Hazratim, saltanatda ishlar joyida. Shaharlar barqaror.|");
            return notes;
        }

        // ------------------------------------------------------------------
        // Hokimlar
        // ------------------------------------------------------------------
        private async Task<CityEntity> MyCityAsync(Kingdom me, int cityId)
            => await Db.Cities.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cityId && c.KingdomId == me.Id);

        public Task<ActionResultDto> SetGovernorModeAsync(Kingdom me, GovernorRequest req) => _online.RunCommandAsync(me, req?.CommandId, async () =>
        {
            if (!Enum.IsDefined(typeof(GovernorMode), req.Mode)) return "Noma'lum rejim";
            CityEntity c = await MyCityAsync(me, req.CityId);
            if (c == null) return "Shahar sizga tegishli emas";
            await Db.Cities.Where(x => x.Id == c.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.GovernorMode, (int)req.Mode));
            return null;
        }, "Hokim boshqaruv rejimi o'zgartirildi");

        /// <summary>Hokim shaharni tahlil qilib reja tuzadi (faqat taklif — hech narsa qurilmaydi, tanga yechilmaydi).</summary>
        public Task<ActionResultDto> GovernorAnalyzeAsync(Kingdom me, GovernorRequest req) => _online.RunCommandAsync(me, req?.CommandId, async () =>
        {
            CityEntity c = await MyCityAsync(me, req.CityId);
            if (c == null) return "Shahar sizga tegishli emas";
            if (c.GovernorMode == (int)GovernorMode.Manual) return "Hokim 'Qo'lda boshqaruv' rejimida — avval Maslahat yoki Tasdiqlangan avtomatika rejimini tanlang";
            int houses = 0, markets = 0, gardens = 0;
            var why = new List<string>();
            if (c.Population >= c.HousingCapacity * 0.85) { houses = Math.Clamp((c.Population - c.HousingCapacity) / 5 + 3, 2, 6); why.Add("uy-joy to'lib qolgan"); }
            if (c.Jobs < c.Population * 0.55) { markets = c.Population > 250 ? 2 : 1; why.Add("ish o'rinlari kam"); }
            if (c.Happiness < 60 || c.Trust < 55) { gardens = 1; why.Add("aholi mamnunligi va ishonchini oshirish kerak"); }
            if (houses + markets + gardens == 0) return "Hokim: shahar barqaror, hozircha qurilish shart emas";
            await Db.GovernorPlans.Where(p => p.CityId == c.Id && p.Status == 0).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, 2));
            Db.GovernorPlans.Add(new GovernorPlanEntity
            {
                CityId = c.Id, KingdomId = me.Id, TitleUz = c.Name + " rivojlanish rejasi",
                ReasonUz = "Sabab: " + string.Join(", ", why) + ".", Houses = houses, Markets = markets, Gardens = gardens,
                Cost = houses * HouseCost + markets * MarketCost + gardens * GardenCost, Status = 0
            });
            return null;
        }, "Hokim yangi qurilish rejasini tayyorladi — tasdig'ingizni kutmoqda");

        /// <summary>
        /// Rejani tasdiqlash (faqat "Tasdiqlangan avtomatika" rejimida). Xarajat xazinadan bir marta yechiladi;
        /// binolar shu shaharga qurilish holatida qo'shiladi va server simulyatsiyasi ularni tugatgach shahar statistikasi oshadi.
        /// Hokimning o'zi limitdan katta xarajatni ruxsatsiz qilmaydi — har bir reja o'yinchi tasdig'idan o'tadi.
        /// </summary>
        public Task<ActionResultDto> ApprovePlanAsync(Kingdom me, GovernorRequest req) => _online.RunCommandAsync(me, req?.CommandId, async () =>
        {
            GovernorPlanEntity p = await Db.GovernorPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.PlanId && x.KingdomId == me.Id);
            if (p == null) return "Reja topilmadi";
            CityEntity c = await MyCityAsync(me, p.CityId);
            if (c == null) return "Shahar endi sizga tegishli emas";
            if (c.GovernorMode != (int)GovernorMode.ApprovedAutomation) return "Rejani bajarish uchun hokimni 'Tasdiqlangan avtomatika' rejimiga o'tkazing";
            int taken = await Db.GovernorPlans.Where(x => x.Id == p.Id && x.Status == 0).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, 1));
            if (taken == 0) return "Reja allaqachon ko'rib chiqilgan";
            int paid = await Db.KingdomResources.Where(r => r.KingdomId == me.Id && r.Coin >= p.Cost)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - p.Cost));
            if (paid == 0) return "Xazinada tanga yetarli emas (" + p.Cost + ")";
            int slot = await Db.Buildings.CountAsync(b => b.CityId == c.Id);
            void Add(BuildingKind kind, int n) { for (int i = 0; i < n; i++, slot++) Db.Buildings.Add(new BuildingEntity { KingdomId = me.Id, CityId = c.Id, Kind = (int)kind, X = slot % 16, Y = slot / 16, BuildProgress = 0f }); }
            Add(BuildingKind.House, p.Houses);
            Add(BuildingKind.Market, p.Markets);
            Add(BuildingKind.Garden, p.Gardens);
            return null;
        }, "Reja tasdiqlandi — qurilish boshlandi");

        public Task<ActionResultDto> RejectPlanAsync(Kingdom me, GovernorRequest req) => _online.RunCommandAsync(me, req?.CommandId, async () =>
        {
            int n = await Db.GovernorPlans.Where(x => x.Id == req.PlanId && x.KingdomId == me.Id && x.Status == 0).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, 2));
            return n == 0 ? "Reja topilmadi" : null;
        }, "Reja rad etildi");

        /// <summary>
        /// Shaharlarning yengil statistik simulyatsiyasi (server fon xizmati): tugagan binolar effekti, aholi o'sishi,
        /// ishonch va mamnunlik tiklanishi. Faol (o'yinchi ko'rayotgan) shahar ham shu qoidalarga bo'ysunadi.
        /// </summary>
        public static int ApplyBuildingEffect(CityEntity c, BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.House: c.HousingCapacity += 5; return 5;
                case BuildingKind.Market: c.Jobs += 4; c.Happiness = Math.Min(100, c.Happiness + 2); return 4;
                case BuildingKind.Garden: c.Jobs += 5; c.Happiness = Math.Min(100, c.Happiness + 3); return 5;
                default: return 0;
            }
        }

        public static void GrowCity(CityEntity c)
        {
            int room = c.HousingCapacity - c.Population;
            if (room > 0 && c.Happiness >= 55) c.Population += Math.Max(1, room / 20);
            else if (room < 0) c.Population += room / 10; // sig'imdan ortiq — ayrimlar ko'chib ketadi
            if (c.Population < 0) c.Population = 0;
            c.Households = (int)Math.Ceiling(c.Population / 4.5);
            if (c.Trust < 70) c.Trust++;
            int targetHappy = 50 + (c.Jobs >= c.Population * 0.55 ? 10 : -5) + (c.Population <= c.HousingCapacity ? 8 : -10);
            c.Happiness += Math.Sign(targetHappy - c.Happiness);
            c.Level = 1 + c.Population / 200;
        }
    }
}
