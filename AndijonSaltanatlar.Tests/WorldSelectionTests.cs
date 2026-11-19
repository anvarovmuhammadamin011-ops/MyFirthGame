using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using AndijonSaltanatlar.Server.Authentication;
using AndijonSaltanatlar.Server.Data;
using AndijonSaltanatlar.Server.Services;
using AndijonSaltanatlar.Shared.DTOs;

namespace AndijonSaltanatlar.Tests
{
    /// <summary>PROMPT 26: DUNYONI TANLASH — dunyo ro'yxati, a'zolik va qaytgan o'yinchining saltanati.</summary>
    public class WorldSelectionTests : IDisposable
    {
        private readonly SqliteConnection _conn;
        private readonly OnlineGameService _svc;

        public WorldSelectionTests()
        {
            _conn = new SqliteConnection("Data Source=:memory:");
            _conn.Open();
            using GameDbContext db = NewDb();
            db.Database.EnsureCreated();
            _svc = new OnlineGameService(NewDb(), new SystemClock());
        }

        public void Dispose()
        {
            _svc.Db.Dispose();
            _conn.Dispose();
        }

        private GameDbContext NewDb() => new GameDbContext(new DbContextOptionsBuilder<GameDbContext>().UseSqlite(_conn).Options);

        private AppUser NewPlayer(string sub)
        {
            using GameDbContext db = NewDb();
            return GoogleLoginService.FindOrCreateUser(db, new GoogleIdentity
            {
                Sub = sub, Email = sub + "@gmail.com", Name = "Ali", EmailVerified = true
            });
        }

        [Fact]
        public async Task Worlds_ListsPlayersAndOwnKingdom_SameAccountSameKingdom()
        {
            AppUser a = NewPlayer("world-a");
            AppUser b = NewPlayer("world-b");
            Kingdom ka = await _svc.JoinAsync(a.Id, new JoinWorldRequest { WorldId = 1, ChooseStart = true });
            Kingdom kb = await _svc.JoinAsync(b.Id, new JoinWorldRequest { WorldId = 1, ChooseStart = true });

            WorldsResponse wa = await _svc.WorldsAsync(a.Id);
            Assert.NotNull(wa.MyKingdomId);
            Assert.Equal(ka.Id, wa.MyKingdomId);
            Assert.Equal(1, wa.MyWorldId);
            Assert.Single(wa.Worlds);
            Assert.True(wa.Worlds[0].IsMine);
            Assert.True(wa.Worlds[0].PlayerCount >= 2);
            Assert.True(wa.Worlds[0].FreeRegionCount > 0);

            // Boshqa qurilmada qayta kirish (yoki qayta so'rov) yangi saltanat yaratmaydi.
            Kingdom again = await _svc.JoinAsync(a.Id, new JoinWorldRequest { WorldId = 1, ChooseStart = true });
            Assert.Equal(ka.Id, again.Id);
            Assert.NotEqual(ka.Id, kb.Id);
            Assert.Equal(2, _svc.Db.Kingdoms.Count(k => !k.IsAi && k.OwnerUserId != null));
        }

        [Fact]
        public async Task NewPlayer_ChoosesWorldThenFreeRegion_AndCannotTakeItTwice()
        {
            AppUser a = NewPlayer("start-a");
            Kingdom ka = await _svc.JoinAsync(a.Id, new JoinWorldRequest { WorldId = 2, ChooseStart = true });
            Assert.Equal(2, ka.WorldId);
            Assert.True((await _svc.StateAsync(ka)).NeedsHome);

            StartRegionsDto starts = await _svc.StartRegionsAsync(ka);
            StartRegionDto free = starts.Regions.First(r => r.Free);
            Assert.Null(await _svc.ChooseStartAsync(ka, new ChooseStartRequest
            {
                RegionId = free.RegionId, KingdomName = "Asaka sultonligi"
            }));
            Assert.False((await _svc.StateAsync(ka)).NeedsHome);

            // Boshqa o'yinchi egallangan hududni ololmaydi.
            AppUser b = NewPlayer("start-b");
            Kingdom kb = await _svc.JoinAsync(b.Id, new JoinWorldRequest { WorldId = 2, ChooseStart = true });
            Assert.NotNull(await _svc.ChooseStartAsync(kb, new ChooseStartRequest { RegionId = free.RegionId }));

            WorldsResponse wb = await _svc.WorldsAsync(b.Id);
            Assert.Equal(2, wb.MyWorldId);
            Assert.Contains(wb.Worlds, w => w.Id == 2 && w.PlayerCount >= 2);
        }

        // --- Promt 3: AVTO JOY ---

        private static double Dist(RegionEntity a, RegionEntity b)
        {
            double dx = a.Latitude - b.Latitude, dy = a.Longitude - b.Longitude;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        [Fact]
        public async Task AutoStart_PicksFreeRegion()
        {
            AppUser a = NewPlayer("auto-a");
            Kingdom ka = await _svc.JoinAsync(a.Id, new JoinWorldRequest { WorldId = 3, ChooseStart = true });
            Assert.True((await _svc.StateAsync(ka)).NeedsHome);
            Assert.Null(await _svc.ChooseStartAsync(ka, new ChooseStartRequest { Auto = true, KingdomName = "Avto saltanat" }));
            Assert.False((await _svc.StateAsync(ka)).NeedsHome);
            using GameDbContext db = NewDb();
            Assert.Equal(1, db.Regions.Count(r => r.OwnerKingdomId == ka.Id));
        }

        [Fact]
        public async Task AutoStart_TwoPlayers_DifferentRegions()
        {
            AppUser a = NewPlayer("auto2-a");
            AppUser b = NewPlayer("auto2-b");
            Kingdom ka = await _svc.JoinAsync(a.Id, new JoinWorldRequest { WorldId = 4, ChooseStart = true });
            Kingdom kb = await _svc.JoinAsync(b.Id, new JoinWorldRequest { WorldId = 4, ChooseStart = true });
            Assert.Null(await _svc.ChooseStartAsync(ka, new ChooseStartRequest { Auto = true }));
            Assert.Null(await _svc.ChooseStartAsync(kb, new ChooseStartRequest { Auto = true }));
            using GameDbContext db = NewDb();
            int ra = db.Regions.Where(r => r.OwnerKingdomId == ka.Id).Select(r => r.Id).Single();
            int rb = db.Regions.Where(r => r.OwnerKingdomId == kb.Id).Select(r => r.Id).Single();
            Assert.NotEqual(ra, rb); // bir-birining yoniga emas, har xil hududga tushadi
        }

        [Fact]
        public async Task PickAutoRegions_OnlyFree_AndExcludesTaken()
        {
            AppUser a = NewPlayer("auto3-a");
            Kingdom ka = await _svc.JoinAsync(a.Id, new JoinWorldRequest { WorldId = 5, ChooseStart = true });
            await _svc.ChooseStartAsync(ka, new ChooseStartRequest { Auto = true });
            using GameDbContext db = NewDb();
            int taken = db.Regions.Where(r => r.OwnerKingdomId == ka.Id).Select(r => r.Id).Single();
            var order = await _svc.PickAutoRegionsAsync(5);
            Assert.DoesNotContain(taken, order); // band hudud qayta taklif qilinmaydi
            var freeIds = db.Regions.Where(r => r.WorldId == 5 && r.OwnerKingdomId == 0).Select(r => r.Id).ToHashSet();
            Assert.All(order, id => Assert.Contains(id, freeIds));
        }

        [Fact]
        public async Task AutoRecommend_PrefersFarFromExistingPlayer()
        {
            AppUser a = NewPlayer("far-a");
            Kingdom ka = await _svc.JoinAsync(a.Id, new JoinWorldRequest { WorldId = 6, ChooseStart = true });
            StartRegionsDto starts = await _svc.StartRegionsAsync(ka);
            StartRegionDto first = starts.Regions.First(r => r.Free);
            await _svc.ChooseStartAsync(ka, new ChooseStartRequest { RegionId = first.RegionId });

            var order = await _svc.PickAutoRegionsAsync(6);
            Assert.NotEmpty(order);
            using GameDbContext db = NewDb();
            // Band = AI saltanatlar + o'yinchi a. Scoring barcha band hududlardan uzoqlikka qaraydi.
            var occupied = db.Regions.Where(r => r.WorldId == 6 && r.OwnerKingdomId != 0).ToList();
            var frees = db.Regions.Where(r => r.WorldId == 6 && r.OwnerKingdomId == 0).ToList();
            Assert.Contains(occupied, r => r.OwnerKingdomId == ka.Id); // o'yinchi hududi band ro'yxatida
            double MinDistToOcc(RegionEntity r) => occupied.Count == 0 ? 1.0 : occupied.Min(o => Dist(r, o));
            RegionEntity top = frees.First(r => r.Id == order[0]);
            double avg = frees.Average(MinDistToOcc);
            Assert.True(MinDistToOcc(top) >= avg - 1e-9); // tavsiya etilgan hudud band hududlardan o'rtachadan uzoq
        }

        // --- Promt 6: mehmon, sovg'a, xat ---

        private async Task<(Kingdom a, Kingdom b)> TwoPlayersAsync(int worldId, string tag)
        {
            Kingdom ka = await _svc.JoinAsync(NewPlayer(tag + "-a").Id, new JoinWorldRequest { WorldId = worldId, ChooseStart = true });
            Kingdom kb = await _svc.JoinAsync(NewPlayer(tag + "-b").Id, new JoinWorldRequest { WorldId = worldId, ChooseStart = true });
            await _svc.ChooseStartAsync(ka, new ChooseStartRequest { Auto = true });
            await _svc.ChooseStartAsync(kb, new ChooseStartRequest { Auto = true });
            return (ka, kb);
        }

        [Fact]
        public async Task Gift_Transfers_AndEnforcesLimits()
        {
            var (ka, kb) = await TwoPlayersAsync(10, "gift");
            Assert.Null(await _svc.SendGiftAsync(ka, new GiftRequest { ToKingdomId = kb.Id, Kind = "coin", Amount = 100 }));
            using (GameDbContext db = NewDb())
            {
                Assert.Equal(400, db.KingdomResources.Single(r => r.KingdomId == ka.Id).Coin); // 500-100
                Assert.Equal(600, db.KingdomResources.Single(r => r.KingdomId == kb.Id).Coin); // 500+100
            }
            // Kunlik limit: yana 2 ta (jami 3) — so'ng 4-si rad etiladi.
            Assert.Null(await _svc.SendGiftAsync(ka, new GiftRequest { ToKingdomId = kb.Id, Kind = "coin", Amount = 10 }));
            Assert.Null(await _svc.SendGiftAsync(ka, new GiftRequest { ToKingdomId = kb.Id, Kind = "coin", Amount = 10 }));
            Assert.NotNull(await _svc.SendGiftAsync(ka, new GiftRequest { ToKingdomId = kb.Id, Kind = "coin", Amount = 10 }));
            // Eng ko'p miqdordan oshsa — rad.
            Assert.NotNull(await _svc.SendGiftAsync(kb, new GiftRequest { ToKingdomId = ka.Id, Kind = "coin", Amount = 2000 }));
        }

        [Fact]
        public async Task Letter_LengthFilterAndInbox()
        {
            var (ka, kb) = await TwoPlayersAsync(11, "lett");
            Assert.NotNull(await _svc.SendLetterAsync(ka, new LetterRequest { ToKingdomId = kb.Id, Text = new string('a', 301) })); // uzun
            Assert.NotNull(await _svc.SendLetterAsync(ka, new LetterRequest { ToKingdomId = kb.Id, Text = "sen ahmoq" }));          // filtr
            Assert.Null(await _svc.SendLetterAsync(ka, new LetterRequest { ToKingdomId = kb.Id, Text = "Salom, savdo qilaylikmi?" }));

            // Elchi hali yo'lda — qabul qiluvchi maktubni ko'rmaydi, jo'natuvchi esa elchisini kuzatadi
            LettersResponse early = await _svc.LettersAsync(kb);
            Assert.DoesNotContain(early.Letters, l => l.Text.Contains("savdo"));
            LettersResponse mine = await _svc.LettersAsync(ka);
            Assert.Contains(mine.Envoys, e => !e.Delivered && e.SecondsLeft > 0 && e.EnvoyName.StartsWith("Elchi"));
            // Elchi yetib bordi
            using (var db = NewDb()) await db.Letters.ExecuteUpdateAsync(s => s.SetProperty(l => l.DeliverAt, System.DateTime.UtcNow.AddSeconds(-1)));
            LettersResponse inbox = await _svc.LettersAsync(kb);
            Assert.Contains(inbox.Letters, l => l.Text.Contains("savdo") && l.EnvoyName.StartsWith("Elchi"));
            Assert.Equal(1, inbox.Unread);
            // Ikkinchi o'qishda o'qilgan deb belgilangan.
            LettersResponse again = await _svc.LettersAsync(kb);
            Assert.Equal(0, again.Unread);
        }

        [Fact]
        public async Task CitySnapshot_ReturnsPublicInfo()
        {
            var (ka, kb) = await TwoPlayersAsync(12, "snap");
            CitySnapshotDto snap = await _svc.CitySnapshotAsync(ka, kb.Id);
            Assert.NotNull(snap);
            Assert.Equal(kb.Id, snap.KingdomId);
            Assert.NotNull(snap.Buildings);
            Assert.True(snap.RegionCount >= 1);
            // DTO maxfiy maydonlarni (xazina/ombor/qo'shin/email) umuman o'z ichiga olmaydi — bu strukturaga ko'ra.
        }
    }
}
