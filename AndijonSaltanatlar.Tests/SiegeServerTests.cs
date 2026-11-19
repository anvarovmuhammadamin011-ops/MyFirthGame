using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using AndijonSaltanatlar.Server.Data;
using AndijonSaltanatlar.Server.Services;
using AndijonSaltanatlar.Shared.DTOs;

namespace AndijonSaltanatlar.Tests
{
    /// <summary>
    /// Qamal (M xarita → shahardagi jangchilar bilan yurish): server qamalni ro'yxatga oladi, natijani faqat
    /// yurish + jang vaqti o'tgach qabul qiladi, g'alabada qasr (poytaxt bo'lmasa) o'yinchiga o'tadi — bayrog'i o'zgaradi.
    /// </summary>
    public class SiegeServerTests : IDisposable
    {
        private sealed class FakeClock : IServerClock
        {
            public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
            public void Advance(double seconds) => UtcNow = UtcNow.AddSeconds(seconds);
        }

        private readonly SqliteConnection _conn;
        private readonly FakeClock _clock = new FakeClock();

        public SiegeServerTests()
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

        private async Task<Kingdom> Player(string name)
        {
            using GameDbContext db = NewDb();
            var u = new AppUser { Username = name, Email = name + "@t", PasswordHash = "", CreatedAt = _clock.UtcNow.AddDays(-3) };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            return await new OnlineGameService(db, _clock).JoinAsync(u.Id, new JoinWorldRequest { WorldId = 1 });
        }

        private async Task<WarOverviewDto> Overview(Kingdom k) { using GameDbContext db = NewDb(); return await War(db).OverviewAsync(k); }

        private async Task<int> OwnerOf(int regionId) { using GameDbContext db = NewDb(); return await db.Regions.Where(r => r.Id == regionId).Select(r => r.OwnerKingdomId).FirstAsync(); }

        private async Task<WarMapRegionDto> AiTarget(Kingdom me) =>
            (await Overview(me)).MapRegions.First(r => !r.IsMine && r.OwnerKingdomId != 0 && r.OwnerIsAi && !r.IsCapital);

        [Fact]
        public async Task MapOffersSiegeAgainstAiCastle_WithRulerName()
        {
            Kingdom me = await Player("andijon");
            WarMapRegionDto t = await AiTarget(me);
            Assert.True(t.CanSiege, t.BlockReasonUz);
            Assert.False(string.IsNullOrEmpty(t.RulerName));
            Assert.All((await Overview(me)).MapRegions.Where(r => r.IsMine), r => Assert.False(r.CanSiege));
        }

        [Fact]
        public async Task WonSiege_TransfersCastleToPlayer_OnlyAfterMarchAndBattleTime()
        {
            Kingdom me = await Player("andijon");
            WarMapRegionDto t = await AiTarget(me);
            int oldOwner = t.OwnerKingdomId;

            using (GameDbContext db = NewDb())
                Assert.True((await War(db).SiegeStartAsync(me, new SiegeStartRequest { CommandId = Id(), RegionId = t.RegionId })).Success);

            // Darhol "g'alaba" — qabul qilinmaydi (yurish va jang vaqti o'tmagan)
            var win = new SiegeFinishRequest { RegionId = t.RegionId, Won = true, AttackerFallen = 2, AttackerTotal = 10, DefendersDown = 6, DefendersTotal = 8 };
            using (GameDbContext db = NewDb())
            {
                win.CommandId = Id();
                ActionResultDto early = await War(db).SiegeFinishAsync(me, win);
                Assert.False(early.Success);
            }
            Assert.Equal(oldOwner, await OwnerOf(t.RegionId));

            _clock.Advance(t.MarchSeconds + WarService.SiegeMinBattleSeconds + 1);
            using (GameDbContext db = NewDb())
            {
                win.CommandId = Id();
                ActionResultDto ok = await War(db).SiegeFinishAsync(me, win);
                Assert.True(ok.Success, ok.MessageUz);
                Assert.Contains("bayrog", ok.MessageUz);
            }
            Assert.Equal(me.Id, await OwnerOf(t.RegionId));
            WarMapRegionDto after = (await Overview(me)).MapRegions.First(r => r.RegionId == t.RegionId);
            Assert.True(after.IsMine);

            // Bir qamal — bir natija: takror yuborilgan natija qabul qilinmaydi
            using (GameDbContext db = NewDb())
            {
                win.CommandId = Id();
                Assert.False((await War(db).SiegeFinishAsync(me, win)).Success);
            }
        }

        [Fact]
        public async Task LostSiege_KeepsOwner_AndFinishWithoutStartIsRejected()
        {
            Kingdom me = await Player("andijon");
            WarMapRegionDto t = await AiTarget(me);
            using (GameDbContext db = NewDb())
                Assert.False((await War(db).SiegeFinishAsync(me, new SiegeFinishRequest { CommandId = Id(), RegionId = t.RegionId, Won = true })).Success);

            using (GameDbContext db = NewDb())
                Assert.True((await War(db).SiegeStartAsync(me, new SiegeStartRequest { CommandId = Id(), RegionId = t.RegionId })).Success);
            using (GameDbContext db = NewDb())
                Assert.True((await War(db).SiegeFinishAsync(me, new SiegeFinishRequest { CommandId = Id(), RegionId = t.RegionId, Won = false })).Success);
            Assert.Equal(t.OwnerKingdomId, await OwnerOf(t.RegionId));
        }

        [Fact]
        public async Task CannotSiegeOwnCastle()
        {
            Kingdom me = await Player("andijon");
            WarMapRegionDto mine = (await Overview(me)).MapRegions.First(r => r.IsMine);
            using GameDbContext db = NewDb();
            Assert.False((await War(db).SiegeStartAsync(me, new SiegeStartRequest { CommandId = Id(), RegionId = mine.RegionId })).Success);
        }
    }
}
