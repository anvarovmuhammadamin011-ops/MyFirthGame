using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AndijonSaltanatlar.Server.Data;
using AndijonSaltanatlar.Server.Hubs;
using AndijonSaltanatlar.Shared;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Shared.Farming;

namespace AndijonSaltanatlar.Server.Services
{
    /// <summary>
    /// PROMPT 20 — Ittifoqlar, qo'shma loyihalar, diplomatik kelishuvlar, hudud bo'yicha kelishuvlar,
    /// dunyo yangiliklari va saltanat boshqaruvi. Barcha operatsiyalar server-authoritative, idempotent.
    /// </summary>
    public sealed class DiplomacyService
    {
        private readonly OnlineGameService _online;
        private readonly IHubContext<GameHub> _hub;
        private GameDbContext Db => _online.Db;

        public DiplomacyService(OnlineGameService online, IHubContext<GameHub> hub)
        {
            _online = online;
            _hub = hub;
        }

        // ==================================================================
        //  YORDAMCHI NOMLAR
        // ==================================================================

        // ------------------------------------------------------------------
        // Xabarlar faqat tranzaksiya MUVAFFAQIYATLI tasdiqlangandan keyin yuboriladi: amal bekor bo'lsa,
        // clientlar "ittifoq tuzildi" kabi yolg'on xabar olmaydi (PROMPT 20, 17-bo'lim).
        // ------------------------------------------------------------------
        private bool _inCommand;
        private readonly List<Func<Task>> _afterCommit = new List<Func<Task>>();

        private Task Later(Func<Task> send)
        {
            if (_inCommand) { _afterCommit.Add(send); return Task.CompletedTask; }
            return send();
        }

        private async Task<ActionResultDto> Run(Kingdom me, string commandId, Func<Task<string>> body, string okMessage)
        {
            _afterCommit.Clear();
            _inCommand = true;
            ActionResultDto r;
            try { r = await _online.RunCommandAsync(me, commandId, body, okMessage); }
            finally { _inCommand = false; }
            Func<Task>[] pending = _afterCommit.ToArray();
            _afterCommit.Clear();
            if (r.Success && !r.Duplicate)
                foreach (Func<Task> send in pending)
                {
                    try { await send(); } catch { /* client uzilgan bo'lsa — holat baribir serverda saqlangan */ }
                }
            return r;
        }

        /// <summary>
        /// Asoschi a'zoga rol beradi: Vakil (taklif yuboradi, loyiha taklif qiladi) yoki A'zo.
        /// Asoschilikni berish — asoschi o'zi Vakilga aylanadi (ittifoqda doim bitta asoschi).
        /// </summary>
        public Task<ActionResultDto> SetRoleAsync(Kingdom me, SetAllianceRoleRequest req) => Run(me, req?.CommandId, async () =>
        {
            int? aid = await AllianceOfAsync(me.Id);
            if (aid == null) return "Siz ittifoqda emassiz";
            if (await MyRoleAsync(me.Id, aid.Value) != AllianceRole.Founder) return "Rollarni faqat Asoschi belgilaydi";
            if (req.KingdomId == me.Id) return "O'z rolingizni o'zgartira olmaysiz";
            if (req.Role != AllianceRole.Envoy && req.Role != AllianceRole.Member && req.Role != AllianceRole.Founder) return "Noma'lum rol";
            int changed = await Db.AllianceMembers.Where(m => m.AllianceId == aid.Value && m.KingdomId == req.KingdomId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, req.Role));
            if (changed == 0) return "Bu saltanat ittifoq a'zosi emas";
            if (req.Role == AllianceRole.Founder)
            {
                await Db.AllianceMembers.Where(m => m.AllianceId == aid.Value && m.KingdomId == me.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, AllianceRole.Envoy));
                await Db.Alliances.Where(a => a.Id == aid.Value).ExecuteUpdateAsync(s => s.SetProperty(a => a.FounderKingdomId, req.KingdomId));
            }
            int? user = await _online.OwnerUserIdAsync(req.KingdomId);
            if (user != null && _hub != null) await Later(() => _hub.Clients.User(user.Value.ToString()).SendAsync("AllianceMemberJoined", aid.Value, req.KingdomId, "Rolingiz: " + RoleNameUz(req.Role)));
            return null;
        }, "Rol o'zgartirildi");

        public static string RoleNameUz(int role) => role == 0 ? "Asoschi" : role == 1 ? "Vakil" : "A'zo";
        public static string ProjectNameUz(int type) => type == 0 ? "Savdo yo'li" : type == 1 ? "Karvonsaroy" : "Madaniyat markazi";
        public static string ProjectStatusUz(int status) => status == 0 ? "Yig'ilmoqda" : "Yakunlandi";
        public static string AgreementNameUz(int type) => type == 0 ? "Savdo kelishuvi" : type == 1 ? "Do'stlik kelishuvi" : type == 2 ? "Hududni topshirish" : "Savdo yo'lidan foydalanish";
        public static string AgreementStatusUz(int status) => status == 0 ? "Taklif qilingan" : status == 1 ? "Faol" : status == 2 ? "Rad etilgan" : status == 3 ? "Bekor qilingan" : status == 4 ? "Muddati tugagan" : "Yakunlangan";
        public static string InviteStatusUz(int status) => status == 0 ? "Kutilmoqda" : status == 1 ? "Qabul qilindi" : status == 2 ? "Rad etildi" : "Bekor qilindi";

        public static (int coin, int wood, int stone) ProjectCost(int type)
        {
            switch (type)
            {
                case 0: return (200, 50, 0);
                case 1: return (300, 150, 100);
                default: return (400, 100, 50);
            }
        }

        private async Task<Kingdom> KingdomByIdAsync(int id) => await Db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id);

        private async Task<string> KingdomNameAsync(int id)
        {
            Kingdom k = await KingdomByIdAsync(id);
            return k?.Name ?? "?";
        }

        private async Task<int?> AllianceOfAsync(int kingdomId)
            => await Db.AllianceMembers.AsNoTracking().Where(m => m.KingdomId == kingdomId).Select(m => (int?)m.AllianceId).FirstOrDefaultAsync();

        private async Task<int> MyRoleAsync(int kingdomId, int allianceId)
            => await Db.AllianceMembers.AsNoTracking().Where(m => m.KingdomId == kingdomId && m.AllianceId == allianceId).Select(m => m.Role).FirstOrDefaultAsync();

        private async Task PublishNewsAsync(int worldId, string type, int kA, int kB, int regionId, string text, int gameDay)
        {
            Db.WorldNews.Add(new WorldNewsEntity { WorldId = worldId, Type = type, KingdomAId = kA, KingdomBId = kB, RegionId = regionId, TextUz = text, GameDay = gameDay, At = DateTime.UtcNow });
            await Db.SaveChangesAsync();
            if (_hub != null) await Later(() => _hub.Clients.Group(GameHub.WorldGroup(worldId)).SendAsync("WorldNewsPublished", worldId, type, text));
        }

        // ==================================================================
        //  ALLIANCE — YARATISH / TAKLIF / A'ZOLIK
        // ==================================================================

        public Task<ActionResultDto> CreateAllianceAsync(Kingdom me, CreateAllianceRequest req) => Run(me, req?.CommandId, async () =>
        {
            if (string.IsNullOrWhiteSpace(req?.Name)) return "Ittifoq nomi bo'sh";
            string name = req.Name.Trim();
            if (name.Length > 40) name = name.Substring(0, 40);
            if (await AllianceOfAsync(me.Id) != null) return "Siz allaqachon ittifoqdasiz";
            if (await Db.Alliances.AnyAsync(a => a.WorldId == me.WorldId && a.Name == name)) return "Bu nomli ittifoq allaqachon mavjud";

            var alliance = new AllianceEntity { WorldId = me.WorldId, Name = name, Emblem = req.Emblem, FounderKingdomId = me.Id, CreatedAt = DateTime.UtcNow };
            Db.Alliances.Add(alliance);
            await Db.SaveChangesAsync();
            Db.AllianceMembers.Add(new AllianceMemberEntity { AllianceId = alliance.Id, KingdomId = me.Id, Role = AllianceRole.Founder, JoinedAt = DateTime.UtcNow });
            await Db.SaveChangesAsync();
            await PublishNewsAsync(me.WorldId, "alliance_created", me.Id, 0, 0, me.Name + " saltanati \"" + name + "\" ittifoqini tuzdi.", await GameDayAsync(me.WorldId));
            return null;
        }, "Ittifoq yaratildi");

        public Task<ActionResultDto> InviteAsync(Kingdom me, InviteToAllianceRequest req) => Run(me, req?.CommandId, async () =>
        {
            int? allianceId = await AllianceOfAsync(me.Id);
            if (allianceId == null) return "Siz ittifoqda emassiz";
            int role = await MyRoleAsync(me.Id, allianceId.Value);
            if (role != AllianceRole.Founder && role != AllianceRole.Envoy) return "Taklif yuborish uchun Asoschi yoki Vakil bo'lish kerak";
            if (req.ToKingdomId == me.Id) return "O'zingizni taklif qila olmaysiz";
            Kingdom target = await KingdomByIdAsync(req.ToKingdomId);
            if (target == null || target.WorldId != me.WorldId) return "Saltanat topilmadi";
            if (await AllianceOfAsync(target.Id) != null) return target.Name + " allaqachon ittifoqda";
            // pending duplicate
            if (await Db.AllianceInvitations.AnyAsync(i => i.AllianceId == allianceId.Value && i.ToKingdomId == target.Id && i.Status == InviteStatus.Pending))
                return "Bu saltanatga taklif allaqachon yuborilgan";
            Db.AllianceInvitations.Add(new AllianceInvitationEntity { WorldId = me.WorldId, AllianceId = allianceId.Value, FromKingdomId = me.Id, ToKingdomId = target.Id, Status = InviteStatus.Pending, CreatedAt = DateTime.UtcNow });
            await Db.SaveChangesAsync();
            int? targetUser = await _online.OwnerUserIdAsync(target.Id);
            if (targetUser != null && _hub != null) await Later(() => _hub.Clients.User(targetUser.Value.ToString()).SendAsync("AllianceInvitationReceived", allianceId.Value, me.Name, target.Id));
            if (_hub != null) await Later(() => _hub.Clients.Group(GameHub.WorldGroup(me.WorldId)).SendAsync("AllianceInvitationCreated", me.WorldId));
            return null;
        }, "Ittifoqqa taklif yuborildi");

        public Task<ActionResultDto> RespondInvitationAsync(Kingdom me, RespondInvitationRequest req) => Run(me, req?.CommandId, async () =>
        {
            AllianceInvitationEntity inv = await Db.AllianceInvitations.FirstOrDefaultAsync(i => i.Id == req.InvitationId && i.ToKingdomId == me.Id);
            if (inv == null) return "Taklif topilmadi";
            if (inv.Status != InviteStatus.Pending) return "Taklif allaqachon ko'rib chiqilgan";
            if (await AllianceOfAsync(me.Id) != null) return "Siz allaqachon ittifoqdasiz";
            if (!req.Accept)
            {
                inv.Status = InviteStatus.Rejected; inv.RespondedAt = DateTime.UtcNow; await Db.SaveChangesAsync();
                return null;
            }
            // accept
            inv.Status = InviteStatus.Accepted; inv.RespondedAt = DateTime.UtcNow;
            Db.AllianceMembers.Add(new AllianceMemberEntity { AllianceId = inv.AllianceId, KingdomId = me.Id, Role = AllianceRole.Member, JoinedAt = DateTime.UtcNow });
            await Db.SaveChangesAsync();
            AllianceEntity a = await Db.Alliances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == inv.AllianceId);
            await PublishNewsAsync(me.WorldId, "alliance_joined", inv.FromKingdomId, me.Id, 0, me.Name + " saltanati \"" + (a?.Name ?? "") + "\" ittifoqiga qo'shildi.", await GameDayAsync(me.WorldId));
            int? founderUser = await _online.OwnerUserIdAsync(inv.FromKingdomId);
            if (founderUser != null && _hub != null) await Later(() => _hub.Clients.User(founderUser.Value.ToString()).SendAsync("AllianceMemberJoined", inv.AllianceId, me.Id, me.Name));
            if (_hub != null) await Later(() => _hub.Clients.Group(GameHub.WorldGroup(me.WorldId)).SendAsync("AllianceInvitationAccepted", me.WorldId, inv.AllianceId, me.Id));
            return null;
        }, req != null && req.Accept ? "Ittifoqqa qo'shildingiz" : "Taklif rad etildi");

        public Task<ActionResultDto> LeaveAllianceAsync(Kingdom me, LeaveAllianceRequest req) => Run(me, req?.CommandId, async () =>
        {
            int? allianceId = await AllianceOfAsync(me.Id);
            if (allianceId == null) return "Siz ittifoqda emassiz";
            int role = await MyRoleAsync(me.Id, allianceId.Value);
            if (role == AllianceRole.Founder)
            {
                // founder leaves: dissolve? We'll just remove founder and promote earliest member if any
                var members = await Db.AllianceMembers.Where(m => m.AllianceId == allianceId.Value).ToListAsync();
                if (members.Count <= 1)
                {
                    // O'YIN QOIDASI: ittifoq tarqatilganda xazina so'nggi a'zoga (asoschiga) qaytariladi — resurs yo'qolmaydi.
                    // A'zo ittifoqdan chiqqanda esa uning avvalgi hissalari ittifoqda qoladi (ixtiyoriy hadya edi).
                    AllianceEntity dis = await Db.Alliances.AsNoTracking().FirstOrDefaultAsync(a => a.Id == allianceId.Value);
                    if (dis != null)
                        await Db.KingdomResources.Where(r => r.KingdomId == me.Id).ExecuteUpdateAsync(s => s
                            .SetProperty(r => r.Coin, r => r.Coin + dis.TreasuryCoin)
                            .SetProperty(r => r.Wood, r => r.Wood + dis.TreasuryWood)
                            .SetProperty(r => r.Stone, r => r.Stone + dis.TreasuryStone));
                    await Db.AllianceMembers.Where(m => m.AllianceId == allianceId.Value).ExecuteDeleteAsync();
                    await Db.AllianceInvitations.Where(i => i.AllianceId == allianceId.Value).ExecuteDeleteAsync();
                    await Db.AllianceProjects.Where(p => p.AllianceId == allianceId.Value).ExecuteDeleteAsync();
                    await Db.Alliances.Where(a => a.Id == allianceId.Value).ExecuteDeleteAsync();
                    await PublishNewsAsync(me.WorldId, "alliance_left", me.Id, 0, 0, me.Name + " saltanati ittifoqni tarqatdi.", await GameDayAsync(me.WorldId));
                    return null;
                }
                members.Remove(members.First(m => m.KingdomId == me.Id));
                var next = members.OrderBy(m => m.JoinedAt).First();
                next.Role = AllianceRole.Founder;
                await Db.AllianceMembers.Where(m => m.KingdomId == me.Id && m.AllianceId == allianceId.Value).ExecuteDeleteAsync();
                await Db.Alliances.Where(a => a.Id == allianceId.Value).ExecuteUpdateAsync(s => s.SetProperty(a => a.FounderKingdomId, next.KingdomId));
                await PublishNewsAsync(me.WorldId, "alliance_left", me.Id, 0, 0, me.Name + " saltanati ittifoqdan chiqdi.", await GameDayAsync(me.WorldId));
                return null;
            }
            await Db.AllianceMembers.Where(m => m.KingdomId == me.Id && m.AllianceId == allianceId.Value).ExecuteDeleteAsync();
            await PublishNewsAsync(me.WorldId, "alliance_left", me.Id, 0, 0, me.Name + " saltanati ittifoqdan chiqdi.", await GameDayAsync(me.WorldId));
            return null;
        }, "Ittifoqdan chiqildi");

        // ==================================================================
        //  ALLIANCE — O'QISH
        // ==================================================================

        public async Task<AllianceDto> MyAllianceAsync(Kingdom me)
        {
            int? aid = await AllianceOfAsync(me.Id);
            if (aid == null) return null;
            AllianceEntity a = await Db.Alliances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == aid.Value);
            if (a == null) return null;
            var members = await Db.AllianceMembers.AsNoTracking().Where(m => m.AllianceId == a.Id).ToListAsync();
            var memberDtos = new List<AllianceMemberDto>();
            foreach (AllianceMemberEntity m in members)
            {
                Kingdom k = await KingdomByIdAsync(m.KingdomId);
                memberDtos.Add(new AllianceMemberDto { KingdomId = m.KingdomId, KingdomName = k?.Name ?? "?", Role = m.Role, RoleNameUz = RoleNameUz(m.Role) });
            }
            var projects = await ProjectsInternalAsync(a.Id);
            return new AllianceDto
            {
                Id = a.Id, Name = a.Name, Emblem = a.Emblem, FounderKingdomId = a.FounderKingdomId,
                TreasuryCoin = a.TreasuryCoin, TreasuryWood = a.TreasuryWood, TreasuryStone = a.TreasuryStone,
                Members = memberDtos, MyRole = await MyRoleAsync(me.Id, a.Id), Projects = projects
            };
        }

        public async Task<List<AllianceInvitationDto>> InvitationsAsync(Kingdom me)
        {
            var list = await Db.AllianceInvitations.AsNoTracking()
                .Where(i => i.ToKingdomId == me.Id && i.Status == InviteStatus.Pending)
                .OrderByDescending(i => i.Id).Take(50).ToListAsync();
            var result = new List<AllianceInvitationDto>();
            foreach (AllianceInvitationEntity i in list)
            {
                AllianceEntity a = await Db.Alliances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == i.AllianceId);
                result.Add(new AllianceInvitationDto
                {
                    Id = i.Id, AllianceId = i.AllianceId, AllianceName = a?.Name ?? "?",
                    FromKingdomId = i.FromKingdomId, FromKingdomName = await KingdomNameAsync(i.FromKingdomId),
                    ToKingdomId = i.ToKingdomId, ToKingdomName = await KingdomNameAsync(i.ToKingdomId),
                    Status = i.Status, StatusUz = InviteStatusUz(i.Status), CreatedAt = i.CreatedAt.ToString("u")
                });
            }
            return result;
        }

        public async Task<List<AllianceDto>> AlliancesInWorldAsync(int worldId, Kingdom me)
        {
            var alliances = await Db.Alliances.AsNoTracking().Where(a => a.WorldId == worldId).OrderBy(a => a.Id).Take(50).ToListAsync();
            var result = new List<AllianceDto>();
            foreach (AllianceEntity a in alliances)
            {
                int members = await Db.AllianceMembers.CountAsync(m => m.AllianceId == a.Id);
                result.Add(new AllianceDto { Id = a.Id, Name = a.Name, Emblem = a.Emblem, FounderKingdomId = a.FounderKingdomId, TreasuryCoin = a.TreasuryCoin, TreasuryWood = a.TreasuryWood, TreasuryStone = a.TreasuryStone, Members = new List<AllianceMemberDto>() });
            }
            return result;
        }

        // ==================================================================
        //  QO'SHMA LOYIHALAR VA HISSALAR
        // ==================================================================

        public Task<ActionResultDto> CreateProjectAsync(Kingdom me, CreateAllianceProjectRequest req) => Run(me, req?.CommandId, async () =>
        {
            int? aid = await AllianceOfAsync(me.Id);
            if (aid == null || aid.Value != req.AllianceId) return "Siz bu ittifoqda emassiz";
            int role = await MyRoleAsync(me.Id, aid.Value);
            if (role != AllianceRole.Founder && role != AllianceRole.Envoy) return "Loyiha taklif qilish uchun Asoschi yoki Vakil bo'lish kerak";
            if (req.Type < 0 || req.Type > 2) return "Noma'lum loyiha turi";
            (int c, int w, int s) = ProjectCost(req.Type);
            Db.AllianceProjects.Add(new AllianceProjectEntity
            {
                AllianceId = aid.Value, Type = req.Type, ReqCoin = c, ReqWood = w, ReqStone = s,
                GotCoin = 0, GotWood = 0, GotStone = 0, Status = ProjectStatus.Collecting,
                CreatedByKingdomId = me.Id, CreatedAt = DateTime.UtcNow
            });
            await Db.SaveChangesAsync();
            await PublishNewsAsync(me.WorldId, "project_new", me.Id, 0, 0, "\"" + ProjectNameUz(req.Type) + "\" qo'shma loyihasi taklif qilindi.", await GameDayAsync(me.WorldId));
            if (_hub != null) await Later(() => _hub.Clients.Group(GameHub.WorldGroup(me.WorldId)).SendAsync("AllianceProjectUpdated", me.WorldId, aid.Value));
            return null;
        }, "Qo'shma loyiha yaratildi");

        public Task<ActionResultDto> ContributeAsync(Kingdom me, ContributeAllianceRequest req) => Run(me, req?.CommandId, async () =>
        {
            int? aid = await AllianceOfAsync(me.Id);
            if (aid == null || aid.Value != req.AllianceId) return "Siz bu ittifoqda emassiz";
            if (req.Amount <= 0) return "Miqdor musbat bo'lishi kerak";
            string res = (req.Resource ?? "Coin").Trim();
            if (res != "Coin" && res != "Wood" && res != "Stone") return "Noma'lum resurs";

            // Idempotency: command already applied? RunCommandAsync handles command dedup, but contribution CommandId unique too.
            if (req.ProjectId > 0)
            {
                AllianceProjectEntity p = await Db.AllianceProjects.FirstOrDefaultAsync(x => x.Id == req.ProjectId && x.AllianceId == aid.Value);
                if (p == null) return "Loyiha topilmadi";
                if (p.Status != ProjectStatus.Collecting) return "Loyiha allaqachon yakunlangan";

                // Faqat kerakli qismgacha qabul qilinadi (ortiqcha resurs o'yinchida qoladi).
                int need = res == "Coin" ? p.ReqCoin - p.GotCoin : res == "Wood" ? p.ReqWood - p.GotWood : p.ReqStone - p.GotStone;
                if (need <= 0) return "Bu loyihaga " + ResourceNameUz(res) + " allaqachon yetarli";
                int amount = Math.Min(req.Amount, need);
                if (!await DeductAsync(me.Id, res, amount)) return "Resurs yetarli emas";
                Db.AllianceContributions.Add(new AllianceContributionEntity { AllianceId = aid.Value, ProjectId = p.Id, KingdomId = me.Id, Resource = res, Amount = amount, CommandId = req.CommandId, At = DateTime.UtcNow });
                await Db.SaveChangesAsync();
                // Atomik oshirish (bir vaqtdagi ikki hissa bir-birini yo'qotmaydi).
                int pid = p.Id;
                if (res == "Coin") await Db.AllianceProjects.Where(x => x.Id == pid).ExecuteUpdateAsync(s => s.SetProperty(x => x.GotCoin, x => x.GotCoin + amount));
                else if (res == "Wood") await Db.AllianceProjects.Where(x => x.Id == pid).ExecuteUpdateAsync(s => s.SetProperty(x => x.GotWood, x => x.GotWood + amount));
                else await Db.AllianceProjects.Where(x => x.Id == pid).ExecuteUpdateAsync(s => s.SetProperty(x => x.GotStone, x => x.GotStone + amount));

                // Yakunlanish faqat BIR marta (shartli UPDATE): yangilik ham bir marta.
                int completed = await Db.AllianceProjects
                    .Where(x => x.Id == pid && x.Status == ProjectStatus.Collecting && x.GotCoin >= x.ReqCoin && x.GotWood >= x.ReqWood && x.GotStone >= x.ReqStone)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ProjectStatus.Completed).SetProperty(x => x.CompletedAt, DateTime.UtcNow));
                if (completed == 1)
                {
                    await ApplyProjectBenefitAsync(aid.Value, p.Type);
                    await PublishNewsAsync(me.WorldId, "project_done", me.Id, 0, 0, "\"" + ProjectNameUz(p.Type) + "\" qo'shma loyihasi yakunlandi!", await GameDayAsync(me.WorldId));
                    if (_hub != null) await Later(() => _hub.Clients.Group(GameHub.WorldGroup(me.WorldId)).SendAsync("AllianceProjectUpdated", me.WorldId, aid.Value));
                }
                return null;
            }
            else
            {
                // treasury contribution
                if (!await DeductAsync(me.Id, res, req.Amount)) return "Resurs yetarli emas";
                Db.AllianceContributions.Add(new AllianceContributionEntity { AllianceId = aid.Value, ProjectId = 0, KingdomId = me.Id, Resource = res, Amount = req.Amount, CommandId = req.CommandId, At = DateTime.UtcNow });
                await Db.SaveChangesAsync();
                int amt = req.Amount, alId = aid.Value;
                if (res == "Coin") await Db.Alliances.Where(x => x.Id == alId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TreasuryCoin, x => x.TreasuryCoin + amt));
                else if (res == "Wood") await Db.Alliances.Where(x => x.Id == alId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TreasuryWood, x => x.TreasuryWood + amt));
                else await Db.Alliances.Where(x => x.Id == alId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TreasuryStone, x => x.TreasuryStone + amt));
                return null;
            }
        }, "Hissa qabul qilindi");

        public static string ResourceNameUz(string res) => res == "Wood" ? "yog'och" : res == "Stone" ? "tosh" : "tanga";

        /// <summary>
        /// Yakunlangan loyiha foydasi (aniq va server tomonidan): savdo yo'li — a'zolar o'rtasidagi munosabat +15;
        /// karvonsaroy — ittifoq xazinasiga 150 tanga; madaniyat markazi — a'zolar shaharlarida mamnunlik +5.
        /// </summary>
        private async Task ApplyProjectBenefitAsync(int allianceId, int type)
        {
            var members = await Db.AllianceMembers.AsNoTracking().Where(m => m.AllianceId == allianceId).Select(m => m.KingdomId).ToListAsync();
            if (type == 0)
            {
                int worldId = await Db.Alliances.AsNoTracking().Where(a => a.Id == allianceId).Select(a => a.WorldId).FirstAsync();
                for (int i = 0; i < members.Count; i++)
                    for (int j = i + 1; j < members.Count; j++)
                    {
                        int a = Math.Min(members[i], members[j]), b = Math.Max(members[i], members[j]);
                        int n = await Db.DiplomaticRelations.Where(r => r.WorldId == worldId && r.KingdomA == a && r.KingdomB == b)
                            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Value, r => r.Value + 15 > 100 ? 100 : r.Value + 15).SetProperty(r => r.LastEvent, "Savdo yo'li qurildi"));
                        if (n == 0) Db.DiplomaticRelations.Add(new DiplomaticRelationEntity { WorldId = worldId, KingdomA = a, KingdomB = b, Value = 15, LastEvent = "Savdo yo'li qurildi" });
                    }
                await Db.SaveChangesAsync();
            }
            else if (type == 1)
                await Db.Alliances.Where(x => x.Id == allianceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TreasuryCoin, x => x.TreasuryCoin + 150));
            else
                await Db.Cities.Where(c => members.Contains(c.KingdomId)).ExecuteUpdateAsync(s => s.SetProperty(c => c.Happiness, c => c.Happiness + 5 > 100 ? 100 : c.Happiness + 5));
        }

        private async Task<bool> DeductAsync(int kingdomId, string res, int amount)
        {
            if (res == "Coin")
            {
                int n = await Db.KingdomResources.Where(r => r.KingdomId == kingdomId && r.Coin >= amount)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - amount));
                return n > 0;
            }
            if (res == "Wood" || res == "Stone")
            {
                // wood/stone stored in Inventory as product; also KingdomResource has Wood/Stone columns
                if (res == "Wood")
                {
                    int n = await Db.KingdomResources.Where(r => r.KingdomId == kingdomId && r.Wood >= amount)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.Wood, r => r.Wood - amount));
                    return n > 0;
                }
                else
                {
                    int n = await Db.KingdomResources.Where(r => r.KingdomId == kingdomId && r.Stone >= amount)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.Stone, r => r.Stone - amount));
                    return n > 0;
                }
            }
            return false;
        }

        private async Task<List<AllianceProjectDto>> ProjectsInternalAsync(int allianceId)
        {
            var list = await Db.AllianceProjects.AsNoTracking().Where(p => p.AllianceId == allianceId).OrderByDescending(p => p.Id).ToListAsync();
            var result = new List<AllianceProjectDto>();
            foreach (AllianceProjectEntity p in list)
            {
                int total = p.ReqCoin + p.ReqWood + p.ReqStone;
                int got = p.GotCoin + p.GotWood + p.GotStone;
                Kingdom creator = await KingdomByIdAsync(p.CreatedByKingdomId);
                result.Add(new AllianceProjectDto
                {
                    Id = p.Id, AllianceId = p.AllianceId, Type = p.Type, TypeNameUz = ProjectNameUz(p.Type),
                    ReqCoin = p.ReqCoin, ReqWood = p.ReqWood, ReqStone = p.ReqStone,
                    GotCoin = p.GotCoin, GotWood = p.GotWood, GotStone = p.GotStone,
                    Status = p.Status, StatusUz = ProjectStatusUz(p.Status),
                    CreatedByKingdomId = p.CreatedByKingdomId, CreatedByName = creator?.Name ?? "?",
                    Progress = total <= 0 ? 1f : Math.Min(1f, got / (float)total)
                });
            }
            return result;
        }

        public async Task<List<AllianceProjectDto>> ProjectsAsync(Kingdom me, int allianceId)
        {
            int? aid = await AllianceOfAsync(me.Id);
            if (aid == null || aid.Value != allianceId) return new List<AllianceProjectDto>();
            return await ProjectsInternalAsync(allianceId);
        }

        // ==================================================================
        //  DIPLOMATIK KELISHUVLAR
        // ==================================================================

        public Task<ActionResultDto> ProposeAgreementAsync(Kingdom me, ProposeAgreementRequest req) => Run(me, req?.CommandId, async () =>
        {
            if (req.ToKingdomId == me.Id) return "O'zingizga kelishuv taklif qila olmaysiz";
            Kingdom target = await KingdomByIdAsync(req.ToKingdomId);
            if (target == null || target.WorldId != me.WorldId) return "Saltanat topilmadi";
            if (req.Type < 0 || req.Type > 3) return "Noma'lum kelishuv turi";
            if (req.Type == 2)
            {
                // territory transfer: region must be owned by me
                RegionEntity region = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.RegionId && r.WorldId == me.WorldId);
                if (region == null) return "Hudud topilmadi";
                if (region.OwnerKingdomId != me.Id) return "Bu hudud sizga tegishli emas";
                if (req.Price < 0) return "Narx manfiy bo'lmasin";
            }
            Db.DiplomaticAgreements.Add(new DiplomaticAgreementEntity
            {
                WorldId = me.WorldId, Type = req.Type, FromKingdomId = me.Id, ToKingdomId = target.Id,
                RegionId = req.RegionId, Price = req.Price, Status = AgreementStatus.Proposed,
                CreatedAt = DateTime.UtcNow
            });
            await Db.SaveChangesAsync();
            int? targetUser = await _online.OwnerUserIdAsync(target.Id);
            if (targetUser != null && _hub != null) await Later(() => _hub.Clients.User(targetUser.Value.ToString()).SendAsync("DiplomaticAgreementCreated", me.WorldId, me.Name, target.Id));
            await PublishNewsAsync(me.WorldId, "agreement", me.Id, target.Id, req.RegionId, me.Name + " → " + target.Name + ": " + AgreementNameUz(req.Type) + " taklifi.", await GameDayAsync(me.WorldId));
            return null;
        }, "Kelishuv taklifi yuborildi");

        public Task<ActionResultDto> RespondAgreementAsync(Kingdom me, RespondAgreementRequest req) => Run(me, req?.CommandId, async () =>
        {
            DiplomaticAgreementEntity a = await Db.DiplomaticAgreements.FirstOrDefaultAsync(x => x.Id == req.AgreementId && x.ToKingdomId == me.Id);
            if (a == null) return "Kelishuv topilmadi";
            if (a.Status != AgreementStatus.Proposed) return "Kelishuv allaqachon ko'rib chiqilgan";
            if (!req.Accept)
            {
                a.Status = AgreementStatus.Rejected; a.RespondedAt = DateTime.UtcNow; await Db.SaveChangesAsync();
                return null;
            }
            if (a.Type == 2)
            {
                // territory transfer: validate region still owned by from
                RegionEntity region = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == a.RegionId && r.WorldId == me.WorldId);
                if (region == null || region.OwnerKingdomId != a.FromKingdomId) return "Hudud endi topshirishga yaroqsiz";
                if (await Db.Cities.AnyAsync(c => c.RegionId == region.Id && c.IsCapital && c.KingdomId == a.FromKingdomId)) return "Poytaxt hududini topshirib bo'lmaydi";
                if (await Db.Regions.CountAsync(r => r.WorldId == me.WorldId && r.OwnerKingdomId == a.FromKingdomId) <= 1) return "Saltanatning yagona hududini topshirib bo'lmaydi";
                // pay price from buyer (me) to seller (from)
                if (a.Price > 0)
                {
                    int paid = await Db.KingdomResources.Where(r => r.KingdomId == me.Id && r.Coin >= a.Price)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - a.Price));
                    if (paid == 0) return "Tanga yetarli emas (" + a.Price + ")";
                    await Db.KingdomResources.Where(r => r.KingdomId == a.FromKingdomId)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin + a.Price));
                }
                // transfer region + cities + buildings — egalik SHARTLI o'zgaradi: parallel ikkinchi kelishuv 0 qator o'zgartiradi.
                int moved = await Db.Regions.Where(x => x.Id == region.Id && x.OwnerKingdomId == a.FromKingdomId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerKingdomId, me.Id).SetProperty(x => x.Fortify, 0));
                if (moved == 0) return "Hudud egasi hozirgina o'zgardi — kelishuv bekor";
                var cityIds = await Db.Cities.Where(c => c.RegionId == region.Id).Select(c => c.Id).ToListAsync();
                await Db.Cities.Where(c => c.RegionId == region.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.KingdomId, me.Id).SetProperty(c => c.IsCapital, false)
                    .SetProperty(c => c.Trust, c => c.Trust > 45 ? c.Trust - 10 : 35)
                    .SetProperty(c => c.GovernorMode, (int)GovernorMode.Manual));
                await Db.GovernorPlans.Where(gp => cityIds.Contains(gp.CityId) && gp.Status == 0).ExecuteUpdateAsync(s => s.SetProperty(gp => gp.Status, 2));
                if (cityIds.Count > 0) await Db.Buildings.Where(b => cityIds.Contains(b.CityId)).ExecuteUpdateAsync(s => s.SetProperty(b => b.KingdomId, me.Id));
                await new KingdomService(_online).RefreshPopulationAsync(me.WorldId);
                await Db.Kingdoms.Where(x => x.Id == a.FromKingdomId || x.Id == me.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.StateVersion, x => x.StateVersion + 1));
            }
            a.Status = AgreementStatus.Active; a.RespondedAt = DateTime.UtcNow;
            if (a.Type == 2) a.Status = AgreementStatus.Completed;
            await Db.SaveChangesAsync();
            await PublishNewsAsync(me.WorldId, "agreement", a.FromKingdomId, me.Id, a.RegionId,
                AgreementNameUz(a.Type) + " tasdiqlandi: " + (await KingdomNameAsync(a.FromKingdomId)) + " ↔ " + (await KingdomNameAsync(me.Id)) + ".", await GameDayAsync(me.WorldId));
            int? fromUser = await _online.OwnerUserIdAsync(a.FromKingdomId);
            if (fromUser != null && _hub != null) await Later(() => _hub.Clients.User(fromUser.Value.ToString()).SendAsync("DiplomaticAgreementAccepted", me.WorldId, a.Id));
            // Xarita yangilanishi faqat haqiqiy hudud transferida (savdo/do'stlik kelishuvida emas).
            if (a.Type == 2 && _hub != null)
            {
                string regionName = await Db.Regions.AsNoTracking().Where(r => r.Id == a.RegionId).Select(r => r.Name).FirstOrDefaultAsync() ?? "Hudud";
                int regionId = a.RegionId, fromId = a.FromKingdomId;
                await Later(() => _hub.Clients.Group(GameHub.WorldGroup(me.WorldId)).SendAsync("TerritoryTransferred", me.WorldId, regionId, me.Id, fromId, regionName + " kelishuv orqali " + me.Name + " tarkibiga o'tdi"));
            }
            return null;
        }, req != null && req.Accept ? "Kelishuv tasdiqlandi" : "Kelishuv rad etildi");

        public async Task<List<DiplomaticAgreementDto>> AgreementsAsync(Kingdom me)
        {
            var list = await Db.DiplomaticAgreements.AsNoTracking()
                .Where(a => a.WorldId == me.WorldId && (a.FromKingdomId == me.Id || a.ToKingdomId == me.Id))
                .OrderByDescending(a => a.Id).Take(50).ToListAsync();
            var result = new List<DiplomaticAgreementDto>();
            foreach (DiplomaticAgreementEntity a in list)
            {
                string regionName = "";
                if (a.RegionId > 0)
                {
                    RegionEntity r = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == a.RegionId);
                    regionName = r?.Name ?? "";
                }
                result.Add(new DiplomaticAgreementDto
                {
                    Id = a.Id, Type = a.Type, TypeNameUz = AgreementNameUz(a.Type),
                    FromKingdomId = a.FromKingdomId, FromKingdomName = await KingdomNameAsync(a.FromKingdomId),
                    ToKingdomId = a.ToKingdomId, ToKingdomName = await KingdomNameAsync(a.ToKingdomId),
                    RegionId = a.RegionId, RegionName = regionName, Price = a.Price,
                    Status = a.Status, StatusUz = AgreementStatusUz(a.Status), CreatedAt = a.CreatedAt.ToString("u")
                });
            }
            return result;
        }

        // ==================================================================
        //  DUNYO YANGILIKLARI
        // ==================================================================

        public async Task<List<WorldNewsDto>> NewsAsync(int worldId, int take = 30)
        {
            var list = await Db.WorldNews.AsNoTracking().Where(n => n.WorldId == worldId).OrderByDescending(n => n.Id).Take(take).ToListAsync();
            var result = new List<WorldNewsDto>();
            foreach (WorldNewsEntity n in list)
            {
                string regionName = "";
                if (n.RegionId > 0)
                {
                    RegionEntity r = await Db.Regions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == n.RegionId);
                    regionName = r?.Name ?? "";
                }
                result.Add(new WorldNewsDto
                {
                    Id = n.Id, Type = n.Type, TextUz = n.TextUz,
                    KingdomAId = n.KingdomAId, KingdomAName = await KingdomNameAsync(n.KingdomAId),
                    KingdomBId = n.KingdomBId, KingdomBName = n.KingdomBId > 0 ? await KingdomNameAsync(n.KingdomBId) : "",
                    RegionId = n.RegionId, RegionName = regionName, GameDay = n.GameDay, AtUtc = n.At.ToString("u")
                });
            }
            return result;
        }

        private async Task<int> GameDayAsync(int worldId)
        {
            World w = await Db.Worlds.AsNoTracking().FirstOrDefaultAsync(x => x.Id == worldId);
            return w?.Day ?? 1;
        }

        // ==================================================================
        //  AI WORLD POLITICS (lightweight)
        // ==================================================================

        public async Task AiDiplomacyAsync(int worldId)
        {
            var ais = await Db.Kingdoms.AsNoTracking().Where(k => k.WorldId == worldId && k.IsAi).ToListAsync();
            if (ais.Count < 2) return;
            var rnd = new Random((int)(DateTime.UtcNow.Ticks + worldId));
            if (rnd.NextDouble() > 0.15) return;
            var a = ais[rnd.Next(ais.Count)];
            var b = ais[rnd.Next(ais.Count)];
            if (a.Id == b.Id) return;
            // create friendship agreement if none exists
            bool exists = await Db.DiplomaticAgreements.AnyAsync(x => x.WorldId == worldId && ((x.FromKingdomId == a.Id && x.ToKingdomId == b.Id) || (x.FromKingdomId == b.Id && x.ToKingdomId == a.Id)));
            if (exists) return;
            Db.DiplomaticAgreements.Add(new DiplomaticAgreementEntity { WorldId = worldId, Type = 1, FromKingdomId = a.Id, ToKingdomId = b.Id, Status = AgreementStatus.Active, CreatedAt = DateTime.UtcNow });
            await Db.SaveChangesAsync();
            await PublishNewsAsync(worldId, "agreement", a.Id, b.Id, 0, a.Name + " va " + b.Name + " do'stlik kelishuvi imzoladi.", await GameDayAsync(worldId));
        }
    }
}
