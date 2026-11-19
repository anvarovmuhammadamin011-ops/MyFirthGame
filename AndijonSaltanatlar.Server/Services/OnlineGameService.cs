using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AndijonSaltanatlar.Shared.Heraldry;
using AndijonSaltanatlar.Server.Data;
using AndijonSaltanatlar.Server.Hubs;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Shared.Farming;

namespace AndijonSaltanatlar.Server.Services
{
    /// <summary>Server soati — testlarda vaqtni boshqarish uchun almashtiriladi.</summary>
    public interface IServerClock { DateTime UtcNow { get; } }

    public sealed class SystemClock : IServerClock { public DateTime UtcNow => DateTime.UtcNow; }

    /// <summary>
    /// PROMPT 19A/19B — server-authoritative ekin yetishtirish, ombor, Saltanatlar bozori va karvonlar.
    ///
    /// Barcha o'zgarishlar bitta DB tranzaksiyasida va SHARTLI yangilash (UPDATE ... WHERE shart) bilan:
    ///  - hosil faqat "Planted + yetilgan + versiya mos" bo'lsa yig'iladi → ikki marta yig'ib bo'lmaydi;
    ///  - taklif sotilishi "Status = Active" shartida o'zgaradi → bitta taklifni faqat bitta xaridor oladi;
    ///  - xaridor tangasi "Coin &gt;= narx" shartida yechiladi va karvon yetguncha REZERVDA turadi;
    ///  - karvon "Status = 0" shartida yetkaziladi → mol va pul faqat bir marta o'tadi;
    ///  - TradeTransactions.OfferId va Caravans.OfferId UNIQUE → DB darajasida ham takror savdo mumkin emas;
    ///  - CommandId (GameCommands PK) → bir xil so'rov qayta yuborilsa, qayta bajarilmaydi.
    /// </summary>
    public sealed class OnlineGameService
    {
        public const int WaterSourceCost = 150;
        public const string Rice = "Rice";

        private readonly GameDbContext _db;
        private readonly IServerClock _clock;

        public OnlineGameService(GameDbContext db, IServerClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public GameDbContext Db => _db;
        public IServerClock Clock => _clock;

        // ------------------------------------------------------------------
        // Dunyo, saltanat, boshlang'ich hudud
        // ------------------------------------------------------------------
        public async Task<World> EnsureWorldAsync(int worldId)
        {
            if (worldId <= 0) worldId = 1;
            World w = await _db.Worlds.FirstOrDefaultAsync(x => x.Id == worldId);
            if (w == null)
            {
                // Dunyo AYNAN so'ralgan Id bilan yaratiladi — aks holda bir xil WorldId so'ragan o'yinchilar turli dunyolarga tushib qoladi.
                w = new World { Id = worldId, Name = "Andijon - " + worldId + "-dunyo", EpochUtc = _clock.UtcNow };
                _db.Worlds.Add(w);
                try { await _db.SaveChangesAsync(); }
                catch (DbUpdateException)
                {
                    // Boshqa so'rov xuddi shu paytda yaratdi — o'shanisini olamiz.
                    _db.ChangeTracker.Clear();
                    w = await _db.Worlds.FirstAsync(x => x.Id == worldId);
                }
            }
            await WorldSeeder.SeedAsync(_db, w);
            return w;
        }

        /// <summary>Foydalanuvchini dunyoga qo'shadi: saltanati bo'lmasa — o'z hududi va poytaxt shahri bilan yaratadi.</summary>
        public async Task<Kingdom> JoinAsync(int userId, JoinWorldRequest req)
        {
            AppUser user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return null;
            if (user.KingdomId != null)
            {
                Kingdom existing = await _db.Kingdoms.FirstOrDefaultAsync(k => k.Id == user.KingdomId);
                if (existing != null)
                {
                    await EnsureWorldAsync(existing.WorldId);
                    await EnsureResourcesAsync(existing.Id);
                    // Yangi mijoz yurtni xaritadan o'zi tanlaydi; eski mijozga avtomatik joy beriladi.
                    if (req?.ChooseStart != true) await WorldSeeder.EnsurePlayerHomeAsync(_db, existing);
                    return existing;
                }
            }
            World world = await EnsureWorldAsync(req?.WorldId > 0 ? req.WorldId : 1);
            // Boshqa o'yinchilarga faqat taxallus ko'rinadi (Google'dagi haqiqiy ism yoki email emas)
            string ruler = string.IsNullOrWhiteSpace(user.DisplayName) || !user.ProfileCompleted ? user.Username : user.DisplayName;
            string name = string.IsNullOrWhiteSpace(req?.KingdomName) ? ruler + " saltanati" : req.KingdomName.Trim();
            if (name.Length > 40) name = name.Substring(0, 40);
            int n = await _db.Kingdoms.CountAsync(k => k.WorldId == world.Id && !k.IsAi);
            (int r, int g, int b) = WorldSeeder.PlayerColor(n);
            var kingdom = new Kingdom
            {
                WorldId = world.Id, Name = name, RulerName = ruler, IsAi = false, OwnerUserId = user.Id,
                ColorR = r, ColorG = g, ColorB = b
            };
            _db.Kingdoms.Add(kingdom);
            await _db.SaveChangesAsync();
            _db.KingdomResources.Add(new KingdomResource { KingdomId = kingdom.Id });
            kingdom.Flag = FlagSpec.ForKingdom(kingdom.Id, r, g, b).Encode();
            await _db.SaveChangesAsync();
            // Akkaunt ↔ saltanat bog'lanishi to'g'ridan-to'g'ri UPDATE bilan: dunyo yaratishdagi parallel to'qnashuvda
            // ChangeTracker tozalangan bo'lsa ham (user obyekti kuzatilmaydi) bog'lanish yo'qolmaydi.
            int uid = user.Id, kid = kingdom.Id;
            await _db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s.SetProperty(u => u.KingdomId, kid));
            user.KingdomId = kid;
            await EnsureResourcesAsync(kingdom.Id);
            if (req?.ChooseStart != true) await WorldSeeder.EnsurePlayerHomeAsync(_db, kingdom);
            return kingdom;
        }

        // ------------------------------------------------------------------
        // Dunyolar ro'yxati (DUNYONI TANLASH) va a'zolik
        // ------------------------------------------------------------------
        /// <summary>
        /// Mavjud dunyolar + foydalanuvchining saltanati qaysi dunyoda ekani. Bir hisob bitta saltanatga ega;
        /// qaytgan o'yinchi o'z dunyosini va saltanatini shu yerdan ko'radi.
        /// </summary>
        public async Task<WorldsResponse> WorldsAsync(int userId)
        {
            if (!await _db.Worlds.AnyAsync()) await EnsureWorldAsync(1);

            AppUser user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            Kingdom mine = null;
            if (user?.KingdomId != null)
                mine = await _db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(k => k.Id == user.KingdomId);

            var worlds = await _db.Worlds.AsNoTracking().OrderBy(w => w.Id).Take(20).ToListAsync();
            var playerCounts = await _db.Kingdoms.AsNoTracking()
                .Where(k => !k.IsAi && k.OwnerUserId != null)
                .GroupBy(k => k.WorldId)
                .Select(g => new { WorldId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.WorldId, x => x.Count);
            var freeCounts = await _db.Regions.AsNoTracking()
                .Where(r => r.OwnerKingdomId == 0)
                .GroupBy(r => r.WorldId)
                .Select(g => new { WorldId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.WorldId, x => x.Count);

            var dto = new WorldsResponse
            {
                MyWorldId = mine?.WorldId,
                MyKingdomId = mine?.Id,
                MyKingdomName = mine?.Name ?? ""
            };
            foreach (World w in worlds)
            {
                dto.Worlds.Add(new WorldSummaryDto
                {
                    Id = w.Id,
                    Name = string.IsNullOrWhiteSpace(w.Name) ? "Andijon - " + w.Id + "-dunyo" : w.Name,
                    Day = w.Day,
                    PlayerCount = playerCounts.GetValueOrDefault(w.Id),
                    FreeRegionCount = freeCounts.GetValueOrDefault(w.Id),
                    IsMine = mine != null && mine.WorldId == w.Id
                });
            }
            return dto;
        }

        // ------------------------------------------------------------------
        // Boshlang'ich yurt (xaritadan tanlash) va bayroq
        // ------------------------------------------------------------------
        /// <summary>Butun O'zbekiston xaritasi: qaysi hududlar bo'sh (mustaqil), qaysilari band.</summary>
        public async Task<StartRegionsDto> StartRegionsAsync(Kingdom me)
        {
            await EnsureWorldAsync(me.WorldId);
            var regions = await _db.Regions.AsNoTracking().Where(r => r.WorldId == me.WorldId).OrderBy(r => r.Id).ToListAsync();
            var kingdoms = await _db.Kingdoms.AsNoTracking().Where(k => k.WorldId == me.WorldId).ToDictionaryAsync(k => k.Id);
            var pops = await _db.Cities.AsNoTracking().Where(c => regions.Select(r => r.Id).Contains(c.RegionId))
                .GroupBy(c => c.RegionId).Select(g => new { g.Key, Pop = g.Sum(c => c.Population) }).ToDictionaryAsync(x => x.Key, x => x.Pop);
            var dto = new StartRegionsDto
            {
                SuggestedName = me.Name, Flag = WorldSeeder.FlagOf(me), Color = new[] { me.ColorR, me.ColorG, me.ColorB }
            };
            var startable = new HashSet<int>((await WorldSeeder.StartableRegionsAsync(_db, me.WorldId)).Select(r => r.Id));
            foreach (RegionEntity r in regions)
            {
                kingdoms.TryGetValue(r.OwnerKingdomId, out Kingdom o);
                dto.Regions.Add(new StartRegionDto
                {
                    RegionId = r.Id, Name = r.Name, Province = r.Province, MapX = (float)r.Latitude, MapY = (float)r.Longitude,
                    Population = pops.GetValueOrDefault(r.Id), Free = startable.Contains(r.Id),
                    OwnerKingdomId = r.OwnerKingdomId, OwnerName = o?.Name ?? "Mustaqil", OwnerIsAi = o?.IsAi ?? true,
                    Color = o == null ? new[] { 150, 150, 150 } : new[] { o.ColorR, o.ColorG, o.ColorB }, Flag = WorldSeeder.FlagOf(o)
                });
            }
            return dto;
        }

        /// <summary>
        /// AVTO JOY: bo'sh hududlarni moslik bo'yicha tartiblaydi (eng yaxshisi birinchi).
        /// Ball: boshqa saltanatlardan uzoqlik (yangi o'yinchi darhol hujumga uchramasin) — asosiy;
        /// aholisi o'rtachaga yaqin bo'lsa afzal; rivojlangan (suv/yer sifati taxminiy) yerga oz bonus.
        /// Har pick hududни band qilgani uchun keyingi yangi o'yinchilar ham bir-biridan uzoqqa tushadi.
        /// </summary>
        public async Task<List<int>> PickAutoRegionsAsync(int worldId)
        {
            await EnsureWorldAsync(worldId);
            var regions = await _db.Regions.AsNoTracking().Where(r => r.WorldId == worldId).ToListAsync();
            var startable = new HashSet<int>((await WorldSeeder.StartableRegionsAsync(_db, worldId)).Select(r => r.Id));
            var free = regions.FindAll(r => startable.Contains(r.Id));
            if (free.Count == 0) return new List<int>();
            var occupied = regions.FindAll(r => !startable.Contains(r.Id));
            var ids = regions.ConvertAll(r => r.Id);
            var pops = await _db.Cities.AsNoTracking().Where(c => ids.Contains(c.RegionId))
                .GroupBy(c => c.RegionId).Select(g => new { g.Key, Pop = g.Sum(c => c.Population) })
                .ToDictionaryAsync(x => x.Key, x => x.Pop);
            var freePops = new List<int>();
            foreach (var r in free) freePops.Add(pops.GetValueOrDefault(r.Id));
            freePops.Sort();
            double median = freePops.Count == 0 ? 0 : freePops[freePops.Count / 2];

            // Har bo'sh hudud uchun eng yaqin band saltanatgacha masofa (band yo'q bo'lsa — hammasi teng).
            var minDist = new Dictionary<int, double>();
            double maxMinDist = 0.0001;
            foreach (var r in free)
            {
                double md = 1.0;
                if (occupied.Count > 0)
                {
                    md = double.MaxValue;
                    foreach (var o in occupied)
                    {
                        double dx = r.Latitude - o.Latitude, dy = r.Longitude - o.Longitude;
                        double d = System.Math.Sqrt(dx * dx + dy * dy);
                        if (d < md) md = d;
                    }
                }
                minDist[r.Id] = md;
                if (md > maxMinDist) maxMinDist = md;
            }
            int maxDev = 1;
            foreach (var r in free) if (r.Development > maxDev) maxDev = r.Development;

            // Ball: masofa ASOSIY (boshqalardan uzoq — xavfsiz), aholi o'rtachaga yaqinligi va yer sifati — qo'shimcha.
            var score = new Dictionary<int, double>();
            foreach (var r in free)
            {
                double distNorm = minDist[r.Id] / maxMinDist;               // 0..1 (uzoqroq = yaxshi)
                double pop = pops.GetValueOrDefault(r.Id);
                double popPenalty = median <= 0 ? 0 : System.Math.Abs(pop - median) / System.Math.Max(1.0, median);
                double devNorm = (double)r.Development / maxDev;            // 0..1 (suv/yer sifati taxminiy)
                score[r.Id] = distNorm * 1.0 - popPenalty * 0.3 + devNorm * 0.1;
            }

            free.Sort((a, b) =>
            {
                int c = score[b.Id].CompareTo(score[a.Id]);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            return free.ConvertAll(r => r.Id);
        }

        /// <summary>Yurt tanlash: saltanat nomi va bayrog'i ham shu yerda o'rnatiladi. Natija: null — muvaffaqiyat, aks holda sabab.</summary>
        public async Task<string> ChooseStartAsync(Kingdom me, ChooseStartRequest req)
        {
            if (req == null) return "So'rov bo'sh.";
            string name = (req.KingdomName ?? "").Trim();
            if (name.Length > 0 && (name.Length < 3 || name.Length > 40)) return "Saltanat nomi 3–40 harf bo'lsin.";
            if (!string.IsNullOrEmpty(req.Flag) && !FlagSpec.TryParse(req.Flag, out _)) return "Bayroq noto'g'ri.";
            if (req.Auto)
            {
                // AVTO JOY: eng mos bo'sh hududni server tanlaydi; band bo'lsa keyingi eng yaxshisini oladi.
                List<int> order = await PickAutoRegionsAsync(me.WorldId);
                string autoErr = "Bo'sh hudud qolmadi — birozdan so'ng urining.";
                bool placed = false;
                foreach (int rid in order)
                {
                    string e;
                    try { e = await WorldSeeder.ChooseStartAsync(_db, me, rid); }
                    catch (InvalidOperationException) { continue; } // shu zahoti band bo'ldi — keyingisi
                    if (e == null) { placed = true; break; }
                    if (!e.Contains("band")) return e; // masalan: yurti allaqachon bor
                }
                if (!placed) return autoErr;
            }
            else
            {
                string err;
                try { err = await WorldSeeder.ChooseStartAsync(_db, me, req.RegionId); }
                catch (InvalidOperationException) { err = "Bu hudud hozirgina band bo'ldi — boshqasini tanlang."; }
                if (err != null) return err;
            }
            await _db.Kingdoms.Where(k => k.Id == me.Id).ExecuteUpdateAsync(s => s
                .SetProperty(k => k.Name, k => name.Length > 0 ? name : k.Name)
                .SetProperty(k => k.Flag, k => !string.IsNullOrEmpty(req.Flag) ? req.Flag : k.Flag)
                .SetProperty(k => k.StateVersion, k => k.StateVersion + 1));
            if (name.Length > 0) me.Name = name;
            if (!string.IsNullOrEmpty(req.Flag)) me.Flag = req.Flag;
            return null;
        }

        /// <summary>Bayroqni o'zgartirish (istalgan payt). Natija: null — muvaffaqiyat.</summary>
        public async Task<string> SetFlagAsync(Kingdom me, SetFlagRequest req)
        {
            if (req == null || !FlagSpec.TryParse(req.Flag, out FlagSpec f)) return "Bayroq noto'g'ri.";
            string flag = f.Encode();
            await _db.Kingdoms.Where(k => k.Id == me.Id).ExecuteUpdateAsync(s => s.SetProperty(k => k.Flag, flag).SetProperty(k => k.StateVersion, k => k.StateVersion + 1));
            me.Flag = flag;
            return null;
        }

        // --- Promt 6: boshqa o'yinchi shahrini ko'rish, sovg'a va xat ---

        public const int GiftDailyLimit = 3;
        public const int GiftMaxAmount = 1000;
        public const int LetterMaxLength = 300;
        public const int LetterDailyLimit = 10;

        /// <summary>Yangi o'yinchi himoyasida (qalqon) — WarService bilan bir xil qoida.</summary>
        private async Task<bool> IsShieldedAsync(Kingdom k)
        {
            if (k.IsAi || k.OwnerUserId == null) return false;
            if (await _db.WarStates.Where(s => s.KingdomId == k.Id).Select(s => s.ProtectionWaived).FirstOrDefaultAsync()) return false;
            DateTime created = await _db.Users.Where(u => u.Id == k.OwnerUserId).Select(u => u.CreatedAt).FirstOrDefaultAsync();
            return _clock.UtcNow - created < TimeSpan.FromHours(AndijonSaltanatlar.Shared.War.WarRules.NewPlayerProtectionHours);
        }

        /// <summary>Boshqa saltanat shahrining OCHIQ ko'rinishi: xazina, ombor, qo'shin, email kabi maxfiy ma'lumot BERILMAYDI.</summary>
        public async Task<CitySnapshotDto> CitySnapshotAsync(Kingdom viewer, int targetKingdomId)
        {
            Kingdom k = await _db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == targetKingdomId && x.WorldId == viewer.WorldId);
            if (k == null) return null;
            int regionCount = await _db.Regions.CountAsync(r => r.OwnerKingdomId == k.Id);
            int population = await _db.Cities.Where(c => c.KingdomId == k.Id).Select(c => (int?)c.Population).SumAsync() ?? 0;
            var devs = await _db.Regions.Where(r => r.OwnerKingdomId == k.Id).Select(r => r.Development).ToListAsync();
            int development = devs.Count == 0 ? 0 : (int)devs.Average();
            var grouped = await _db.Buildings.AsNoTracking().Where(b => b.KingdomId == k.Id && b.BuildProgress >= 1f)
                .GroupBy(b => b.Kind)
                .Select(g => new { Kind = g.Key, Count = g.Count(), Level = g.Max(x => x.Level) })
                .ToListAsync();
            var dto = new CitySnapshotDto
            {
                KingdomId = k.Id, KingdomName = k.Name, RulerName = k.RulerName ?? "", Flag = k.Flag ?? "",
                Population = population, Development = development, RegionCount = regionCount,
                IsAi = k.IsAi, Shielded = await IsShieldedAsync(k)
            };
            foreach (var b in grouped)
                dto.Buildings.Add(new CityBuildingDto { Type = ((AndijonSaltanatlar.Shared.BuildingKind)b.Kind).ToString(), Level = b.Level, Count = b.Count });
            return dto;
        }

        /// <summary>Sovg'a: tanga yoki oziq-ovqat; kunlik limit va eng ko'p miqdor serverda tekshiriladi. Natija: null — muvaffaqiyat.</summary>
        public async Task<string> SendGiftAsync(Kingdom me, GiftRequest req)
        {
            if (req == null) return "So'rov bo'sh.";
            if (req.ToKingdomId == me.Id) return "O'zingizga sovg'a yubora olmaysiz.";
            string kind = (req.Kind ?? "coin").ToLowerInvariant();
            if (kind != "coin" && kind != "food") return "Sovg'a turi noto'g'ri.";
            if (req.Amount <= 0 || req.Amount > GiftMaxAmount) return "Miqdor 1 dan " + GiftMaxAmount + " gacha bo'lsin.";
            Kingdom to = await _db.Kingdoms.FirstOrDefaultAsync(x => x.Id == req.ToKingdomId && x.WorldId == me.WorldId);
            if (to == null) return "Saltanat topilmadi.";
            DateTime dayStart = _clock.UtcNow.Date;
            int todayCount = await _db.GiftLog.CountAsync(g => g.FromKingdomId == me.Id && g.SentAt >= dayStart);
            if (todayCount >= GiftDailyLimit) return "Bugungi sovg'a limiti tugadi (" + GiftDailyLimit + " ta).";
            KingdomResource mine = await _db.KingdomResources.FirstOrDefaultAsync(r => r.KingdomId == me.Id);
            if (mine == null) return "Resurs topilmadi.";
            KingdomResource their = await _db.KingdomResources.FirstOrDefaultAsync(r => r.KingdomId == to.Id);
            if (their == null) { their = new KingdomResource { KingdomId = to.Id }; _db.KingdomResources.Add(their); }
            if (kind == "coin")
            {
                if (mine.Coin < req.Amount) return "Tanga yetarli emas.";
                mine.Coin -= req.Amount; their.Coin += req.Amount;
            }
            else
            {
                if (mine.Food < req.Amount) return "Oziq-ovqat yetarli emas.";
                mine.Food -= req.Amount; their.Food += req.Amount;
            }
            _db.GiftLog.Add(new GiftLogEntity { WorldId = me.WorldId, FromKingdomId = me.Id, ToKingdomId = to.Id, Kind = kind, Amount = req.Amount, SentAt = _clock.UtcNow });
            await _db.SaveChangesAsync();
            return null;
        }

        /// <summary>Xat: matn uzunligi, nomaqbul so'z filtri va kunlik limit serverda tekshiriladi. Natija: null — muvaffaqiyat.</summary>
        public async Task<string> SendLetterAsync(Kingdom me, LetterRequest req)
        {
            if (req == null) return "So'rov bo'sh.";
            if (req.ToKingdomId == me.Id) return "O'zingizga xat yoza olmaysiz.";
            string text = (req.Text ?? "").Trim();
            if (text.Length == 0) return "Xat bo'sh.";
            if (text.Length > LetterMaxLength) return "Xat " + LetterMaxLength + " belgidan oshmasin.";
            if (ProfanityFilter.Contains(text)) return "Xatda nomaqbul so'zlar bor — iltimos, odob bilan yozing.";
            Kingdom to = await _db.Kingdoms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.ToKingdomId && x.WorldId == me.WorldId);
            if (to == null) return "Saltanat topilmadi.";
            DateTime dayStart = _clock.UtcNow.Date;
            int todayCount = await _db.Letters.CountAsync(l => l.FromKingdomId == me.Id && l.SentAt >= dayStart);
            if (todayCount >= LetterDailyLimit) return "Bugungi xat limiti tugadi (" + LetterDailyLimit + " ta).";
            var (fromRegion, toRegion, seconds) = await EnvoyRouteAsync(me.Id, to.Id);
            _db.Letters.Add(new PlayerLetterEntity
            {
                WorldId = me.WorldId, FromKingdomId = me.Id, ToKingdomId = to.Id, FromName = me.Name, ToName = to.Name, Text = text,
                SentAt = _clock.UtcNow, Read = false, DeliverAt = _clock.UtcNow.AddSeconds(seconds),
                FromRegionId = fromRegion, ToRegionId = toRegion, EnvoyName = EnvoyNameOf(me.Id)
            });
            await _db.SaveChangesAsync();
            return null;
        }

        // ==================================================================
        // ELCHI: har saltanatning (shohning) elchisi maktubni poytaxtdan poytaxtga olib boradi
        // ==================================================================
        private static readonly string[] EnvoyNames = { "Bahodir", "Qosim", "Sodiq", "Rustam", "Farhod", "Komil", "Jo'ra", "Nodir", "Sherzod", "Anvar" };

        public static string EnvoyNameOf(int kingdomId) => "Elchi " + EnvoyNames[Math.Abs(kingdomId) % EnvoyNames.Length];

        /// <summary>Elchi yo'li: jo'natuvchi poytaxti → qabul qiluvchi poytaxti; vaqt — qo'shin yurishidan tezroq (otliq elchi).</summary>
        private async Task<(int From, int To, int Seconds)> EnvoyRouteAsync(int fromKingdom, int toKingdom)
        {
            RegionEntity a = await CapitalRegionAsync(fromKingdom), b = await CapitalRegionAsync(toKingdom);
            if (a == null || b == null) return (a?.Id ?? 0, b?.Id ?? 0, 10);
            int s = AndijonSaltanatlar.Shared.War.WarRules.MarchSeconds(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
            return (a.Id, b.Id, Math.Clamp((int)(s * 0.6f), 8, 120));
        }

        private async Task<RegionEntity> CapitalRegionAsync(int kingdomId)
        {
            int rid = await _db.Cities.Where(c => c.KingdomId == kingdomId && c.IsCapital).Select(c => c.RegionId).FirstOrDefaultAsync();
            RegionEntity r = rid != 0 ? await _db.Regions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == rid && x.OwnerKingdomId == kingdomId) : null;
            return r ?? await _db.Regions.AsNoTracking().Where(x => x.OwnerKingdomId == kingdomId).OrderBy(x => x.Id).FirstOrDefaultAsync();
        }

        /// <summary>AI shoh maktubni o'qiydi va o'z elchisi bilan javob yuboradi (mazmuniga qarab).</summary>
        private async Task AiReadLettersAsync(Kingdom me)
        {
            DateTime now = _clock.UtcNow;
            var due = await _db.Letters.Where(l => l.FromKingdomId == me.Id && !l.Read && l.DeliverAt <= now).ToListAsync();
            if (due.Count == 0) return;
            var aiIds = await _db.Kingdoms.AsNoTracking().Where(k => k.IsAi).Select(k => k.Id).ToListAsync();
            foreach (PlayerLetterEntity l in due)
            {
                if (!aiIds.Contains(l.ToKingdomId)) continue;
                Kingdom ai = await _db.Kingdoms.AsNoTracking().FirstAsync(k => k.Id == l.ToKingdomId);
                l.Read = true;
                var (fromRegion, toRegion, seconds) = await EnvoyRouteAsync(ai.Id, me.Id);
                string ruler = string.IsNullOrWhiteSpace(ai.RulerName) ? ai.Name + " hukmdori" : ai.RulerName;
                _db.Letters.Add(new PlayerLetterEntity
                {
                    WorldId = me.WorldId, FromKingdomId = ai.Id, ToKingdomId = me.Id, FromName = ai.Name + " · shoh " + ruler, ToName = me.Name,
                    Text = AiReplyText(l.Text, ruler), SentAt = now, Read = false, DeliverAt = now.AddSeconds(seconds),
                    FromRegionId = fromRegion, ToRegionId = toRegion, EnvoyName = EnvoyNameOf(ai.Id)
                });
            }
            await _db.SaveChangesAsync();
        }

        public static string AiReplyText(string text, string ruler)
        {
            string t = (text ?? "").ToLowerInvariant();
            bool Has(params string[] w) => w.Any(x => t.Contains(x));
            if (Has("urush", "jang", "taslim", "bosib", "yo'q qil", "hujum"))
                return "Maktubingizni o'qidim. Tahdidingizdan qo'rqmaymiz — qasrlarimiz mustahkam, askarlarimiz tayyor! — " + ruler;
            if (Has("tinch", "sulh", "do'st", "ittifoq"))
                return "Tinchlik so'zingiz menga ma'qul. Biz ham urush istamaymiz — elchilarimiz bordi-keldi qilib tursin. — " + ruler;
            if (Has("savdo", "karvon", "bozor", "tovar"))
                return "Savdo taklifingizni o'qidim. Karvonlaringizga yo'limiz ochiq, adolatli narx bilan savdo qilamiz. — " + ruler;
            if (Has("sovg'a", "hadya"))
                return "Hadyangiz va maktubingiz uchun minnatdormiz. Do'stligimiz mustahkam bo'lsin. — " + ruler;
            return "Maktubingizni o'qidim. Elchingizni hurmat bilan kutib oldik. Saltanatingizga salom yo'llaymiz. — " + ruler;
        }

        /// <summary>Kelgan xatlar qutisi (eng yangi 50 ta); ko'rilgach o'qilgan deb belgilanadi.</summary>
        public async Task<LettersResponse> LettersAsync(Kingdom me, bool markRead = true)
        {
            await AiReadLettersAsync(me);
            DateTime now = _clock.UtcNow;
            // Faqat elchi yetkazgan maktublar ko'rinadi
            var letters = await _db.Letters.AsNoTracking().Where(l => l.ToKingdomId == me.Id && l.DeliverAt <= now)
                .OrderByDescending(l => l.SentAt).Take(50).ToListAsync();
            var resp = new LettersResponse { Unread = letters.Count(l => !l.Read), MyEnvoy = EnvoyNameOf(me.Id) };
            foreach (var l in letters)
                resp.Letters.Add(new LetterDto { Id = l.Id, FromName = l.FromName, Text = l.Text, SentAt = l.SentAt, Read = l.Read, EnvoyName = l.EnvoyName ?? "" });
            foreach (var l in await _db.Letters.AsNoTracking().Where(l => l.FromKingdomId == me.Id).OrderByDescending(l => l.SentAt).Take(8).ToListAsync())
            {
                int total = Math.Max(1, (int)(l.DeliverAt - l.SentAt).TotalSeconds);
                resp.Envoys.Add(new EnvoyDto
                {
                    LetterId = l.Id, EnvoyName = string.IsNullOrEmpty(l.EnvoyName) ? EnvoyNameOf(me.Id) : l.EnvoyName, ToName = l.ToName ?? "",
                    FromRegionId = l.FromRegionId, ToRegionId = l.ToRegionId, TotalSeconds = total,
                    SecondsLeft = Math.Max(0, (int)Math.Ceiling((l.DeliverAt - now).TotalSeconds)), Delivered = l.DeliverAt <= now, Read = l.Read,
                    Preview = l.Text.Length > 40 ? l.Text.Substring(0, 40) + "…" : l.Text
                });
            }
            var unreadIds = letters.Where(l => !l.Read).Select(l => l.Id).ToList();
            if (markRead && unreadIds.Count > 0)
                await _db.Letters.Where(l => unreadIds.Contains(l.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.Read, true));
            return resp;
        }

        private async Task EnsureResourcesAsync(int kingdomId)
        {
            if (!await _db.KingdomResources.AnyAsync(r => r.KingdomId == kingdomId))
                _db.KingdomResources.Add(new KingdomResource { KingdomId = kingdomId });
            var have = await _db.Inventory.Where(i => i.KingdomId == kingdomId).Select(i => i.Product).ToListAsync();
            foreach (CropDef c in CropCatalog.All)
                if (c.Type != CropType.Rice && !have.Contains(c.Key))
                    _db.Inventory.Add(new InventoryEntity { KingdomId = kingdomId, Product = c.Key });
            await _db.SaveChangesAsync();
        }

        public async Task<Kingdom> KingdomOfUserAsync(int userId)
        {
            AppUser user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.KingdomId == null) return null;
            return await _db.Kingdoms.FirstOrDefaultAsync(k => k.Id == user.KingdomId);
        }

        public async Task<long> GameMinuteAsync(int worldId)
        {
            World w = await _db.Worlds.AsNoTracking().FirstOrDefaultAsync(x => x.Id == worldId);
            return w == null ? 0 : RiceFarmingRules.GameMinutesSince(w.EpochUtc, _clock.UtcNow);
        }

        // ------------------------------------------------------------------
        // Ombor (guruch — KingdomResource.Rice; boshqalari — Inventory). Faqat tranzaksiya ichida chaqiriladi.
        // ------------------------------------------------------------------
        public static string NormalizeProduct(string product)
            => CropCatalog.TryParse(string.IsNullOrWhiteSpace(product) ? Rice : product, out CropType t) ? CropCatalog.Get(t).Key : null;

        private Task<int> AddStock(int kingdomId, string product, int qty)
            => product == Rice
                ? _db.KingdomResources.Where(r => r.KingdomId == kingdomId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Rice, r => r.Rice + qty))
                : _db.Inventory.Where(i => i.KingdomId == kingdomId && i.Product == product).ExecuteUpdateAsync(s => s.SetProperty(i => i.Quantity, i => i.Quantity + qty));

        /// <summary>Ombordan rezervga (taklif uchun). Yetmasa 0.</summary>
        private Task<int> ReserveStock(int kingdomId, string product, int qty)
            => product == Rice
                ? _db.KingdomResources.Where(r => r.KingdomId == kingdomId && r.Rice >= qty)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Rice, r => r.Rice - qty).SetProperty(r => r.RiceReserved, r => r.RiceReserved + qty))
                : _db.Inventory.Where(i => i.KingdomId == kingdomId && i.Product == product && i.Quantity >= qty)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.Quantity, i => i.Quantity - qty).SetProperty(i => i.Reserved, i => i.Reserved + qty));

        /// <summary>Rezervni bekor qilib omborga qaytarish.</summary>
        private Task<int> UnreserveStock(int kingdomId, string product, int qty)
            => product == Rice
                ? _db.KingdomResources.Where(r => r.KingdomId == kingdomId && r.RiceReserved >= qty)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Rice, r => r.Rice + qty).SetProperty(r => r.RiceReserved, r => r.RiceReserved - qty))
                : _db.Inventory.Where(i => i.KingdomId == kingdomId && i.Product == product && i.Reserved >= qty)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.Quantity, i => i.Quantity + qty).SetProperty(i => i.Reserved, i => i.Reserved - qty));

        /// <summary>Rezervdagi molni karvonga yuklash (ombordan butunlay chiqadi).</summary>
        private Task<int> LoadReserved(int kingdomId, string product, int qty)
            => product == Rice
                ? _db.KingdomResources.Where(r => r.KingdomId == kingdomId && r.RiceReserved >= qty)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.RiceReserved, r => r.RiceReserved - qty))
                : _db.Inventory.Where(i => i.KingdomId == kingdomId && i.Product == product && i.Reserved >= qty)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.Reserved, i => i.Reserved - qty));

        // ------------------------------------------------------------------
        // Holat
        // ------------------------------------------------------------------
        public async Task<OnlineStateDto> StateAsync(Kingdom k)
        {
            World w = await _db.Worlds.AsNoTracking().FirstAsync(x => x.Id == k.WorldId);
            long now = RiceFarmingRules.GameMinutesSince(w.EpochUtc, _clock.UtcNow);
            KingdomResource res = await _db.KingdomResources.AsNoTracking().FirstAsync(r => r.KingdomId == k.Id);
            AppUser owner = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == k.OwnerUserId);
            long version = await _db.Kingdoms.AsNoTracking().Where(x => x.Id == k.Id).Select(x => x.StateVersion).FirstAsync();
            var cities = await _db.Cities.AsNoTracking().Where(c => c.KingdomId == k.Id).ToListAsync();
            var dto = new OnlineStateDto
            {
                WorldId = w.Id, WorldName = w.Name, KingdomId = k.Id, KingdomName = k.Name, Username = owner?.Username ?? "",
                Coin = res.Coin, Rice = res.Rice, RiceReserved = res.RiceReserved, WaterSources = res.WaterSources,
                ServerGameMinute = now, Day = (int)(now / (24 * 60)) + 1, StateVersion = version,
                PlayersOnline = GameHub.OnlinePlayers(w.Id),
                CityCount = cities.Count, Population = cities.Sum(c => c.Population),
                NeedsHome = cities.Count == 0, Flag = WorldSeeder.FlagOf(k), Color = new[] { k.ColorR, k.ColorG, k.ColorB }
            };
            dto.Inventory[Rice] = res.Rice;
            dto.Reserved[Rice] = res.RiceReserved;
            // PROMPT 20: qo'shma loyihalar uchun qurilish resurslari
            dto.Inventory["Wood"] = res.Wood;
            dto.Inventory["Stone"] = res.Stone;
            foreach (InventoryEntity i in await _db.Inventory.AsNoTracking().Where(i => i.KingdomId == k.Id).ToListAsync())
            {
                dto.Inventory[i.Product] = i.Quantity;
                dto.Reserved[i.Product] = i.Reserved;
            }
            foreach (RiceFieldEntity f in await _db.RiceFields.AsNoTracking().Where(f => f.KingdomId == k.Id).OrderBy(f => f.Slot).ToListAsync())
            {
                var stored = (RiceFieldState)f.State;
                var crop = (CropType)f.CropType;
                dto.Fields.Add(new RiceFieldDto
                {
                    Id = f.Id, Slot = f.Slot, PlantedAtGameMinute = f.PlantedAtGameMinute, Crop = CropCatalog.Get(crop).Key,
                    State = CropCatalog.Evaluate(crop, stored, f.PlantedAtGameMinute, now),
                    Progress = CropCatalog.Progress(crop, stored, f.PlantedAtGameMinute, now),
                    SecondsToMature = stored == RiceFieldState.Planted ? CropCatalog.SecondsToMature(crop, f.PlantedAtGameMinute, now) : 0
                });
            }
            dto.Caravans = await CaravansAsync(k);
            return dto;
        }

        public async Task<List<CaravanDto>> CaravansAsync(Kingdom k)
        {
            DateTime now = _clock.UtcNow;
            var list = await _db.Caravans.AsNoTracking()
                .Where(c => c.SellerKingdomId == k.Id || c.BuyerKingdomId == k.Id)
                .OrderByDescending(c => c.Id).Take(12).ToListAsync();
            var cityIds = list.SelectMany(c => new[] { c.FromCityId, c.ToCityId }).Distinct().ToList();
            var names = await _db.Cities.AsNoTracking().Where(c => cityIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
            return list.Select(c =>
            {
                double total = Math.Max(1, (c.ArriveAt - c.DepartAt).TotalSeconds);
                double done = (now - c.DepartAt).TotalSeconds;
                return new CaravanDto
                {
                    Id = c.Id, OfferId = c.OfferId, Product = c.Product, Quantity = c.Quantity,
                    FromName = names.GetValueOrDefault(c.FromCityId, "?"), ToName = names.GetValueOrDefault(c.ToCityId, "?"),
                    Outgoing = c.SellerKingdomId == k.Id, Status = c.Status,
                    Progress = c.Status == 1 ? 1f : (float)Math.Clamp(done / total, 0, 1),
                    SecondsLeft = c.Status == 1 ? 0 : Math.Max(0, (int)Math.Ceiling((c.ArriveAt - now).TotalSeconds))
                };
            }).ToList();
        }

        // ------------------------------------------------------------------
        // Idempotent buyruq qobig'i
        // ------------------------------------------------------------------
        public async Task<ActionResultDto> RunCommandAsync(Kingdom k, string commandId, Func<Task<string>> body, string okMessage)
        {
            if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 80) return ActionResultDto.Fail("CommandId noto'g'ri");
            if (await _db.GameCommands.AsNoTracking().AnyAsync(c => c.CommandId == commandId))
                return new ActionResultDto { Success = true, Duplicate = true, MessageUz = "Buyruq allaqachon bajarilgan", State = await StateAsync(k) };

            await using var tx = await _db.Database.BeginTransactionAsync();
            string error;
            try
            {
                error = await body();
                if (error == null)
                {
                    _db.GameCommands.Add(new GameCommandEntity { CommandId = commandId, KingdomId = k.Id, WorldId = k.WorldId });
                    await _db.Kingdoms.Where(x => x.Id == k.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.StateVersion, x => x.StateVersion + 1));
                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                else
                {
                    await tx.RollbackAsync();
                }
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync();
                _db.ChangeTracker.Clear();
                bool dup = await _db.GameCommands.AsNoTracking().AnyAsync(c => c.CommandId == commandId);
                return new ActionResultDto { Success = dup, Duplicate = dup, MessageUz = dup ? "Buyruq allaqachon bajarilgan" : "Amal bajarilmadi (parallel o'zgarish)", State = await StateAsync(k) };
            }
            _db.ChangeTracker.Clear();
            return new ActionResultDto { Success = error == null, MessageUz = error ?? okMessage, State = await StateAsync(k) };
        }

        // ------------------------------------------------------------------
        // Ekinlar (CropSystem)
        // ------------------------------------------------------------------
        public Task<ActionResultDto> BuildFieldAsync(Kingdom k, string commandId) => RunCommandAsync(k, commandId, async () =>
        {
            int count = await _db.RiceFields.CountAsync(f => f.KingdomId == k.Id);
            if (count >= RiceFarmingRules.MaxFieldsPerKingdom) return "Ekin maydonlari soni chegarasiga yetdi";
            int paid = await _db.KingdomResources.Where(r => r.KingdomId == k.Id && r.Coin >= RiceFarmingRules.FieldBuildCost)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - RiceFarmingRules.FieldBuildCost));
            if (paid == 0) return "Tanga yetarli emas (" + RiceFarmingRules.FieldBuildCost + ")";
            _db.RiceFields.Add(new RiceFieldEntity { KingdomId = k.Id, Slot = count, State = (int)RiceFieldState.Empty });
            return null;
        }, "Ekin maydoni qurildi (-" + RiceFarmingRules.FieldBuildCost + " tanga)");

        public Task<ActionResultDto> BuildWaterAsync(Kingdom k, string commandId) => RunCommandAsync(k, commandId, async () =>
        {
            int paid = await _db.KingdomResources.Where(r => r.KingdomId == k.Id && r.Coin >= WaterSourceCost)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - WaterSourceCost)
                                          .SetProperty(r => r.WaterSources, r => r.WaterSources + 1));
            return paid == 0 ? "Tanga yetarli emas (" + WaterSourceCost + ")" : null;
        }, "Yangi ariq qazildi (-" + WaterSourceCost + " tanga)");

        /// <summary>19A bilan mos: guruch ekish.</summary>
        public Task<ActionResultDto> PlantAsync(Kingdom k, string commandId, int fieldId) => PlantCropAsync(k, commandId, fieldId, Rice);

        public Task<ActionResultDto> PlantCropAsync(Kingdom k, string commandId, int fieldId, string cropKey)
        {
            if (!CropCatalog.TryParse(string.IsNullOrEmpty(cropKey) ? Rice : cropKey, out CropType crop))
                return Task.FromResult(ActionResultDto.Fail("Noma'lum ekin"));
            CropDef def = CropCatalog.Get(crop);
            return RunCommandAsync(k, commandId, async () =>
            {
                RiceFieldEntity f = await _db.RiceFields.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fieldId && x.KingdomId == k.Id);
                if (f == null) return "Maydon topilmadi";
                long now = await GameMinuteAsync(k.WorldId);
                if (!RiceFarmingRules.CanPlant(CropCatalog.Evaluate((CropType)f.CropType, (RiceFieldState)f.State, f.PlantedAtGameMinute, now))) return "Bu maydonga hozir ekib bo'lmaydi";

                KingdomResource res = await _db.KingdomResources.AsNoTracking().FirstAsync(r => r.KingdomId == k.Id);
                int active = await _db.RiceFields.CountAsync(x => x.KingdomId == k.Id && x.State == (int)RiceFieldState.Planted);
                if (!RiceFarmingRules.HasWaterFor(active, res.WaterSources)) return "Suv yetarli emas: yangi ariq qazing";

                int paid = await _db.KingdomResources.Where(r => r.KingdomId == k.Id && r.Coin >= def.SeedCost)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - def.SeedCost));
                if (paid == 0) return "Urug' uchun tanga yetarli emas (" + def.SeedCost + ")";

                int changed = await _db.RiceFields.Where(x => x.Id == f.Id && x.Version == f.Version && x.State == f.State)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, (int)RiceFieldState.Planted)
                                              .SetProperty(x => x.CropType, (int)crop)
                                              .SetProperty(x => x.PlantedAtGameMinute, now)
                                              .SetProperty(x => x.Version, x => x.Version + 1));
                return changed == 0 ? "Maydon holati o'zgarib ketdi, qayta urining" : null;
            }, (crop == CropType.Rice ? "Guruch" : def.NameUz) + " ekildi (-" + def.SeedCost + " tanga)");
        }

        public async Task<ActionResultDto> HarvestAsync(Kingdom k, string commandId, int fieldId)
        {
            string harvested = "";
            ActionResultDto r = await RunCommandAsync(k, commandId, async () =>
            {
                RiceFieldEntity f = await _db.RiceFields.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fieldId && x.KingdomId == k.Id);
                if (f == null) return "Maydon topilmadi";
                var crop = (CropType)f.CropType;
                CropDef def = CropCatalog.Get(crop);
                long now = await GameMinuteAsync(k.WorldId);
                RiceFieldState evaluated = CropCatalog.Evaluate(crop, (RiceFieldState)f.State, f.PlantedAtGameMinute, now);
                if (evaluated == RiceFieldState.Harvested) return "Bu hosil allaqachon yig'ib olingan";
                if (!RiceFarmingRules.CanHarvest(evaluated)) return def.NameUz + " hali yetilmagan";

                // Faqat aynan shu (versiyadagi) ekilgan hosil yig'iladi — ikkinchi so'rov 0 qator o'zgartiradi.
                int changed = await _db.RiceFields.Where(x => x.Id == f.Id && x.Version == f.Version && x.State == (int)RiceFieldState.Planted)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, (int)RiceFieldState.Harvested)
                                              .SetProperty(x => x.HarvestCount, x => x.HarvestCount + 1)
                                              .SetProperty(x => x.Version, x => x.Version + 1));
                if (changed == 0) return "Bu hosil allaqachon yig'ib olingan";
                await AddStock(k.Id, def.Key, def.Yield);
                harvested = def.Yield + " " + (crop == CropType.Rice ? "guruch" : def.NameUz.ToLowerInvariant());
                return null;
            }, "Hosil yig'ildi");
            if (r.Success && !r.Duplicate) r.MessageUz = "Hosil yig'ildi: +" + harvested + " omborga qo'shildi";
            return r;
        }

        // ------------------------------------------------------------------
        // Saltanatlar bozori
        // ------------------------------------------------------------------
        public async Task<List<MarketOfferDto>> OffersAsync(Kingdom k, int allianceId = 0)
        {
            var offers = await _db.MarketOffers.AsNoTracking()
                .Where(o => o.WorldId == k.WorldId && o.Status == OfferStatus.Active && o.AllianceId == allianceId)
                .OrderBy(o => o.Id).Take(100).ToListAsync();
            var sellerIds = offers.Select(o => o.SellerKingdomId).Distinct().ToList();
            var names = await _db.Kingdoms.AsNoTracking().Where(x => sellerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
            return offers.Select(o => new MarketOfferDto
            {
                Id = o.Id, SellerKingdomId = o.SellerKingdomId, SellerName = names.GetValueOrDefault(o.SellerKingdomId, "?"),
                Product = o.Product, Quantity = o.Quantity, TotalPrice = o.TotalPrice, IsMine = o.SellerKingdomId == k.Id
            }).ToList();
        }

        public async Task<List<TradeRecordDto>> HistoryAsync(Kingdom k)
        {
            var list = await _db.TradeTransactions.AsNoTracking()
                .Where(t => t.WorldId == k.WorldId && (t.SellerKingdomId == k.Id || t.BuyerKingdomId == k.Id))
                .OrderByDescending(t => t.Id).Take(20).ToListAsync();
            var ids = list.SelectMany(t => new[] { t.SellerKingdomId, t.BuyerKingdomId }).Distinct().ToList();
            var names = await _db.Kingdoms.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
            return list.Select(t => new TradeRecordDto
            {
                Id = t.Id, OfferId = t.OfferId, Quantity = t.Quantity, TotalPrice = t.TotalPrice, Product = t.Product,
                DeliveryStatus = t.DeliveryStatus,
                SellerName = names.GetValueOrDefault(t.SellerKingdomId, "?"), BuyerName = names.GetValueOrDefault(t.BuyerKingdomId, "?"),
                AtUtc = t.At.ToString("u")
            }).ToList();
        }

        /// <summary>Taklif yaratish: mahsulot sotuvchi omboridan REZERVga o'tadi (boshqa taklifga qo'yib bo'lmaydi).</summary>
        public Task<ActionResultDto> CreateOfferAsync(Kingdom k, CreateOfferRequest req)
        {
            string product = NormalizeProduct(req?.Product);
            string label = product == null ? "" : CropCatalog.NameOf(product).ToLowerInvariant();
            return RunCommandAsync(k, req?.CommandId, async () =>
            {
                if (product == null) return "Noma'lum mahsulot";
                if (req.AllianceId > 0)
                {
                    bool member = await _db.AllianceMembers.AsNoTracking().AnyAsync(m => m.AllianceId == req.AllianceId && m.KingdomId == k.Id);
                    if (!member) return "Siz bu ittifoq a'zosi emassiz";
                }
                string invalid = MarketRules.Validate(req.Quantity, req.TotalPrice);
                if (invalid != null) return invalid;
                int active = await _db.MarketOffers.CountAsync(o => o.SellerKingdomId == k.Id && o.Status == OfferStatus.Active);
                if (active >= MarketRules.MaxActiveOffersPerKingdom) return "Faol takliflar soni chegarasiga yetdi";
                if (await ReserveStock(k.Id, product, req.Quantity) == 0) return "Omborda yetarli " + label + " yo'q";
                _db.MarketOffers.Add(new MarketOfferEntity
                {
                    WorldId = k.WorldId, SellerKingdomId = k.Id, Product = product, Quantity = req.Quantity, TotalPrice = req.TotalPrice,
                    AllianceId = req.AllianceId, Status = OfferStatus.Active, CreatedAt = _clock.UtcNow
                });
                return null;
            }, "Sotuvga qo'yildi: " + (req?.Quantity ?? 0) + " " + label + ", " + (req?.TotalPrice ?? 0) + " tanga");
        }

        public Task<ActionResultDto> CancelOfferAsync(Kingdom k, string commandId, int offerId) => RunCommandAsync(k, commandId, async () =>
        {
            MarketOfferEntity o = await _db.MarketOffers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == offerId && x.SellerKingdomId == k.Id);
            if (o == null) return "Taklif topilmadi";
            int changed = await _db.MarketOffers.Where(x => x.Id == offerId && x.Status == OfferStatus.Active)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OfferStatus.Cancelled).SetProperty(x => x.ClosedAt, _clock.UtcNow));
            if (changed == 0) return "Taklif allaqachon yopilgan";
            await UnreserveStock(k.Id, o.Product, o.Quantity);
            return null;
        }, "Taklif bekor qilindi, mahsulot omborga qaytdi");

        /// <summary>
        /// Sotib olish (PROMPT 19B: karvon bilan) — bitta tranzaksiya:
        ///   taklifni egallash (Active→Sold) → xaridor tangasini REZERVga olish → sotuvchi rezervidan molni karvonga yuklash →
        ///   savdo yozuvi (yetkazilmoqda) + karvon. Mol va pul karvon yetib kelganda <see cref="DeliverDueAsync"/> da o'tadi.
        /// </summary>
        public async Task<ActionResultDto> BuyAsync(Kingdom buyer, BuyOfferRequest req, Action<MarketOfferEntity> onSold = null)
        {
            MarketOfferEntity sold = null;
            int travel = 0;
            ActionResultDto result = await RunCommandAsync(buyer, req?.CommandId, async () =>
            {
                MarketOfferEntity o = await _db.MarketOffers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.OfferId);
                if (o == null || o.WorldId != buyer.WorldId) return "Taklif topilmadi";
                if (o.SellerKingdomId == buyer.Id) return "O'z taklifingizni sotib ololmaysiz";
                // Ittifoq bozori: faqat shu ittifoq a'zolari sotib oladi (ID bilan to'g'ridan-to'g'ri so'rov ham rad etiladi).
                if (o.AllianceId > 0 && !await _db.AllianceMembers.AsNoTracking().AnyAsync(m => m.AllianceId == o.AllianceId && m.KingdomId == buyer.Id))
                    return "Bu taklif faqat ittifoq a'zolari uchun";
                if (o.Status != OfferStatus.Active) return "Bu taklif allaqachon sotilgan";

                int claimed = await _db.MarketOffers.Where(x => x.Id == o.Id && x.Status == OfferStatus.Active)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OfferStatus.Sold)
                                              .SetProperty(x => x.BuyerKingdomId, buyer.Id)
                                              .SetProperty(x => x.ClosedAt, _clock.UtcNow));
                if (claimed == 0) return "Bu taklif allaqachon sotilgan";

                int price = o.TotalPrice, qty = o.Quantity;
                int paid = await _db.KingdomResources.Where(r => r.KingdomId == buyer.Id && r.Coin >= price)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin - price));
                if (paid == 0) return "Tanga yetarli emas (" + price + ")"; // tranzaksiya bekor — taklif Active bo'lib qoladi

                if (await LoadReserved(o.SellerKingdomId, o.Product, qty) == 0) return "Sotuvchi rezervi topilmadi";

                CityEntity from = await WorldSeeder.CapitalOfAsync(_db, o.SellerKingdomId);
                CityEntity to = await WorldSeeder.CapitalOfAsync(_db, buyer.Id);
                travel = CaravanRules.TravelSeconds(await WorldSeeder.DistanceAsync(_db, from, to));
                DateTime now = _clock.UtcNow;
                _db.TradeTransactions.Add(new TradeTransactionEntity
                {
                    WorldId = o.WorldId, OfferId = o.Id, SellerKingdomId = o.SellerKingdomId, BuyerKingdomId = buyer.Id,
                    Product = o.Product, Quantity = qty, TotalPrice = price, CommandId = req.CommandId, At = now, DeliveryStatus = 0
                });
                _db.Caravans.Add(new CaravanEntity
                {
                    WorldId = o.WorldId, OfferId = o.Id, SellerKingdomId = o.SellerKingdomId, BuyerKingdomId = buyer.Id,
                    FromCityId = from?.Id ?? 0, ToCityId = to?.Id ?? 0, Product = o.Product, Quantity = qty, TotalPrice = price,
                    DepartAt = now, ArriveAt = now.AddSeconds(travel), Status = 0
                });
                await _db.Kingdoms.Where(x => x.Id == o.SellerKingdomId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.StateVersion, x => x.StateVersion + 1));
                sold = o;
                return null;
            }, "Sotib olindi");
            if (result.Success && !result.Duplicate && sold != null)
            {
                result.MessageUz = "Sotib olindi: " + sold.Quantity + " " + CropCatalog.NameOf(sold.Product).ToLowerInvariant()
                                   + " (-" + sold.TotalPrice + " tanga). Karvon yo'lga chiqdi (~" + travel + " s)";
                onSold?.Invoke(sold);
            }
            return result;
        }

        /// <summary>
        /// Muddati yetgan karvonlarni yetkazadi (o'yinchilar offline bo'lsa ham — fon xizmati chaqiradi).
        /// Har bir karvon "Status = 0" shartida bir marta yopiladi: xaridorga mol, sotuvchiga tanga.
        /// </summary>
        public async Task<List<CaravanEntity>> DeliverDueAsync(int? worldId = null)
        {
            DateTime now = _clock.UtcNow;
            var due = await _db.Caravans.AsNoTracking()
                .Where(c => c.Status == 0 && c.ArriveAt <= now && (worldId == null || c.WorldId == worldId))
                .OrderBy(c => c.Id).Take(50).ToListAsync();
            var delivered = new List<CaravanEntity>();
            foreach (CaravanEntity c in due)
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                int closed = await _db.Caravans.Where(x => x.Id == c.Id && x.Status == 0)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, 1).SetProperty(x => x.DeliveredAt, now));
                if (closed == 0) { await tx.RollbackAsync(); continue; }
                await AddStock(c.BuyerKingdomId, c.Product, c.Quantity);
                await _db.KingdomResources.Where(r => r.KingdomId == c.SellerKingdomId)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Coin, r => r.Coin + c.TotalPrice));
                await _db.TradeTransactions.Where(t => t.OfferId == c.OfferId)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.DeliveryStatus, 1));
                await _db.Kingdoms.Where(x => x.Id == c.SellerKingdomId || x.Id == c.BuyerKingdomId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.StateVersion, x => x.StateVersion + 1));
                await tx.CommitAsync();
                delivered.Add(c);
            }
            return delivered;
        }

        public async Task<int?> OwnerUserIdAsync(int kingdomId)
            => await _db.Kingdoms.AsNoTracking().Where(k => k.Id == kingdomId).Select(k => k.OwnerUserId).FirstOrDefaultAsync();
    }
}
