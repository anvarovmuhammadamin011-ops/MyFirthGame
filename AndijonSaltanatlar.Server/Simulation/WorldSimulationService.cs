using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AndijonSaltanatlar.Server.Data;
using AndijonSaltanatlar.Server.Hubs;
using AndijonSaltanatlar.Server.Services;
using AndijonSaltanatlar.Shared;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Shared.Farming;

namespace AndijonSaltanatlar.Server.Simulation
{
    /// <summary>
    /// Doimiy dunyo simulyatsiyasi (o'yinchi offline bo'lsa ham ishlaydi):
    ///  - har 2 soniyada: muddati yetgan savdo karvonlarini yetkazadi va tegishli o'yinchilarga xabar beradi;
    ///  - har 10 soniyada: dunyo kuni, binolar qurilishi (tugaganda shahar statistikasi oshadi), shaharlarning
    ///    yengil statistik o'sishi va AI saltanatlarning bozordagi savdosi.
    /// DB ulanmagan bo'lsa server ishdan to'xtamaydi — ogohlantirish yoziladi.
    /// </summary>
    public class WorldSimulationService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<WorldSimulationService> _log;
        private readonly IHubContext<GameHub> _hub;
        private static readonly TimeSpan FastInterval = TimeSpan.FromSeconds(2);
        private const int SlowEvery = 5; // 5 * 2s = 10s
        private long _tick;

        public WorldSimulationService(IServiceScopeFactory scopes, ILogger<WorldSimulationService> log, IHubContext<GameHub> hub)
        {
            _scopes = scopes; _log = log; _hub = hub;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DeliverCaravansAsync();
                    await TickWarAsync();
                    if (++_tick % SlowEvery == 0) await SlowTickAsync();
                }
                catch (Exception ex) { _log.LogWarning(ex, "Simulyatsiya tikida xatolik (DB ulanmagan bo'lishi mumkin)."); }
                await Task.Delay(FastInterval, stoppingToken);
            }
        }

        /// <summary>PROMPT 25: yetib kelgan yurishlar jangga aylanadi; javobsiz raundlar sarkarda qarori bilan davom etadi.</summary>
        private async Task TickWarAsync()
        {
            using IServiceScope scope = _scopes.CreateScope();
            var war = scope.ServiceProvider.GetRequiredService<WarService>();
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            if (!await db.Database.CanConnectAsync()) return;
            foreach (int worldId in await db.Worlds.Select(w => w.Id).ToListAsync())
            {
                await war.TickWorldAsync(worldId);
                if (_tick % SlowEvery == 0) await war.AiAggressionAsync(worldId);   // AI davlatlar o'yindagi o'yinchilarga hujum qiladi
                foreach (WarEvent e in war.Events)
                    await _hub.Clients.Group(GameHub.WorldGroup(e.WorldId)).SendCoreAsync(e.Name, e.Args);
                war.Events.Clear();
            }
        }

        private async Task DeliverCaravansAsync()
        {
            using IServiceScope scope = _scopes.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<OnlineGameService>();
            if (!await svc.Db.Database.CanConnectAsync()) return;
            foreach (CaravanEntity c in await svc.DeliverDueAsync())
                await NotifyDeliveryAsync(svc, c, _hub);
        }

        public static async Task NotifyDeliveryAsync(OnlineGameService svc, CaravanEntity c, IHubContext<GameHub> hub)
        {
            string what = c.Quantity + " " + CropCatalog.NameOf(c.Product).ToLowerInvariant();
            int? buyer = await svc.OwnerUserIdAsync(c.BuyerKingdomId);
            int? seller = await svc.OwnerUserIdAsync(c.SellerKingdomId);
            if (buyer != null) await hub.Clients.User(buyer.Value.ToString()).SendAsync("StateChanged", c.BuyerKingdomId, "Karvon yetib keldi: +" + what + " omborga tushirildi");
            if (seller != null) await hub.Clients.User(seller.Value.ToString()).SendAsync("StateChanged", c.SellerKingdomId, "Karvon manzilga yetdi, savdo yakunlandi: +" + c.TotalPrice + " tanga");
            await hub.Clients.Group(GameHub.WorldGroup(c.WorldId)).SendAsync("MarketChanged", c.WorldId);
        }

        private async Task SlowTickAsync()
        {
            using IServiceScope scope = _scopes.CreateScope();
            GameDbContext db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            if (!await db.Database.CanConnectAsync()) return;

            foreach (World world in await db.Worlds.ToListAsync())
            {
                world.Day++;
                world.StateVersion++;
                world.LastTickAt = DateTime.UtcNow;
            }

            // Qurilish progressi; tugagan bino o'z shahrining statistikasini bir marta oshiradi.
            var cities = await db.Cities.ToDictionaryAsync(c => c.Id);
            foreach (BuildingEntity b in await db.Buildings.Where(b => b.BuildProgress < 1f).ToListAsync())
            {
                b.BuildProgress = Math.Min(1f, b.BuildProgress + 0.5f);
                if (b.BuildProgress >= 1f && cities.TryGetValue(b.CityId, out CityEntity city))
                    KingdomService.ApplyBuildingEffect(city, (BuildingKind)b.Kind);
            }
            // Barcha shaharlar — yengil statistik simulyatsiya.
            foreach (CityEntity c in cities.Values) KingdomService.GrowCity(c);

            // PROMPT 20: har bir shahar yog'och va tosh ishlab chiqaradi (qo'shma loyihalar uchun) — chegaralangan, cheksiz emas.
            foreach (var g in cities.Values.Where(c => c.KingdomId != 0).GroupBy(c => c.KingdomId))
            {
                int n = g.Count(), kid = g.Key;
                await db.KingdomResources.Where(r => r.KingdomId == kid)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Wood, r => r.Wood + 3 * n > 5000 ? 5000 : r.Wood + 3 * n)
                                              .SetProperty(r => r.Stone, r => r.Stone + 2 * n > 5000 ? 5000 : r.Stone + 2 * n));
            }

            // AI saltanatlar: daromad (chegaralangan).
            foreach (Kingdom k in await db.Kingdoms.Where(k => k.IsAi).ToListAsync())
            {
                KingdomResource res = await db.KingdomResources.FirstOrDefaultAsync(r => r.KingdomId == k.Id);
                if (res != null) { res.Coin = Math.Min(20000, res.Coin + 10); res.Food = Math.Min(20000, res.Food + 5); }
            }
            await db.SaveChangesAsync();

            var kingdom = scope.ServiceProvider.GetRequiredService<KingdomService>();
            foreach (int wid in await db.Worlds.Select(w => w.Id).ToListAsync())
            {
                await kingdom.RefreshPopulationAsync(wid);
                await AiMarketAsync(scope.ServiceProvider.GetRequiredService<OnlineGameService>(), wid);
                try { await scope.ServiceProvider.GetRequiredService<DiplomacyService>().AiDiplomacyAsync(wid); } catch { }
            }
        }

        /// <summary>
        /// AI saltanatlar bozorda xuddi o'yinchilar kabi qatnashadi (bir xil server qoidalari, bir xil ombor):
        /// omborida ko'p bo'lgan molni sotuvga qo'yadi va 2 daqiqadan ortiq turgan arzon takliflarni sotib oladi.
        /// </summary>
        private async Task AiMarketAsync(OnlineGameService svc, int worldId)
        {
            var rnd = new Random((int)(_tick * 7919 + worldId));
            var ais = await svc.Db.Kingdoms.AsNoTracking().Where(k => k.WorldId == worldId && k.IsAi).ToListAsync();
            foreach (Kingdom ai in ais)
            {
                int active = await svc.Db.MarketOffers.CountAsync(o => o.SellerKingdomId == ai.Id && o.Status == OfferStatus.Active);
                if (active < 2 && rnd.NextDouble() < 0.25)
                {
                    InventoryEntity inv = await svc.Db.Inventory.AsNoTracking().Where(i => i.KingdomId == ai.Id && i.Quantity >= 100)
                        .OrderByDescending(i => i.Quantity).FirstOrDefaultAsync();
                    if (inv != null && CropCatalog.TryParse(inv.Product, out CropType t))
                        await svc.CreateOfferAsync(ai, new CreateOfferRequest { CommandId = "ai-sell:" + ai.Id + ":" + _tick, Product = inv.Product, Quantity = 100, TotalPrice = CropCatalog.Get(t).ReferencePrice * 100 });
                }

                if (rnd.NextDouble() < 0.2)
                {
                    DateTime old = svc.Clock.UtcNow.AddMinutes(-2);
                    var offers = await svc.Db.MarketOffers.AsNoTracking()
                        .Where(o => o.WorldId == worldId && o.Status == OfferStatus.Active && o.SellerKingdomId != ai.Id && o.CreatedAt <= old).ToListAsync();
                    MarketOfferEntity cheap = offers.Where(o => CropCatalog.TryParse(o.Product, out CropType t2)
                                                               && o.TotalPrice <= CropCatalog.Get(t2).ReferencePrice * o.Quantity * 1.25)
                                                    .OrderBy(o => o.TotalPrice / (double)o.Quantity).FirstOrDefault();
                    if (cheap != null)
                    {
                        MarketOfferEntity sold = null;
                        await svc.BuyAsync(ai, new BuyOfferRequest { CommandId = "ai-buy:" + ai.Id + ":" + _tick, OfferId = cheap.Id }, o => sold = o);
                        if (sold != null)
                        {
                            int? seller = await svc.OwnerUserIdAsync(sold.SellerKingdomId);
                            if (seller != null) await _hub.Clients.User(seller.Value.ToString()).SendAsync("StateChanged", sold.SellerKingdomId, ai.Name + " mahsulotingizni sotib oldi — karvon yo'lda");
                            await _hub.Clients.Group(GameHub.WorldGroup(worldId)).SendAsync("MarketChanged", worldId);
                        }
                    }
                }
            }
        }
    }
}
