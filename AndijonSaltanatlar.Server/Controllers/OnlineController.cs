using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using AndijonSaltanatlar.Server.Data;
using AndijonSaltanatlar.Server.Hubs;
using AndijonSaltanatlar.Server.Services;
using AndijonSaltanatlar.Shared.DTOs;

namespace AndijonSaltanatlar.Server.Controllers
{
    /// <summary>
    /// PROMPT 19A online API: dunyoga qo'shilish, guruch yetishtirish va Saltanatlar bozori.
    /// Har bir so'rov JWT bilan; saltanat har doim tokendagi foydalanuvchidan aniqlanadi (clientdan olinmaydi).
    /// </summary>
    [ApiController]
    [Route("api/online")]
    [Authorize]
    public class OnlineController : ControllerBase
    {
        private readonly OnlineGameService _svc;
        private readonly IHubContext<GameHub> _hub;

        public OnlineController(OnlineGameService svc, IHubContext<GameHub> hub)
        {
            _svc = svc;
            _hub = hub;
        }

        private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int id) ? id : 0;

        private async Task<Kingdom> MyKingdom() => await _svc.KingdomOfUserAsync(UserId);

        [HttpPost("join")]
        public async Task<IActionResult> Join([FromBody] JoinWorldRequest req)
        {
            Kingdom k = await _svc.JoinAsync(UserId, req ?? new JoinWorldRequest());
            if (k == null) return Unauthorized(new { messageUz = "Foydalanuvchi topilmadi" });
            return Ok(await _svc.StateAsync(k));
        }

        /// <summary>DUNYONI TANLASH: mavjud dunyolar, o'yinchilar soni va o'z saltanati qaysi dunyoda ekani.</summary>
        [HttpGet("worlds")]
        public async Task<IActionResult> Worlds() => Ok(await _svc.WorldsAsync(UserId));

        /// <summary>Boshlang'ich yurt tanlash xaritasi: barcha hududlar, bo'sh/band holati bilan.</summary>
        [HttpGet("start-regions")]
        public async Task<IActionResult> StartRegions()
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            return Ok(await _svc.StartRegionsAsync(k));
        }

        /// <summary>Yurtni tanlash (faqat mustaqil hudud, faqat bir marta) + saltanat nomi va bayrog'i.</summary>
        [HttpPost("start")]
        public async Task<IActionResult> ChooseStart([FromBody] ChooseStartRequest req)
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            string err = await _svc.ChooseStartAsync(k, req);
            if (err != null) return Conflict(new { messageUz = err });
            await _hub.Clients.Group(GameHub.WorldGroup(k.WorldId)).SendAsync("PlayersChanged", k.WorldId);
            return Ok(await _svc.StateAsync(k));
        }

        /// <summary>Saltanat bayrog'ini o'zgartirish.</summary>
        [HttpPost("flag")]
        public async Task<IActionResult> SetFlag([FromBody] SetFlagRequest req)
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            string err = await _svc.SetFlagAsync(k, req);
            if (err != null) return BadRequest(new { messageUz = err });
            await _hub.Clients.Group(GameHub.WorldGroup(k.WorldId)).SendAsync("PlayersChanged", k.WorldId);
            return Ok(await _svc.StateAsync(k));
        }

        // --- Promt 6: mehmon, sovg'a, xat ---

        /// <summary>Boshqa o'yinchi shahrining ochiq ko'rinishi (maxfiy ma'lumotsiz).</summary>
        [HttpGet("city/{kingdomId:int}")]
        public async Task<IActionResult> City(int kingdomId)
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            CitySnapshotDto dto = await _svc.CitySnapshotAsync(k, kingdomId);
            if (dto == null) return NotFound(new { messageUz = "Saltanat topilmadi" });
            return Ok(dto);
        }

        /// <summary>Boshqa saltanatga sovg'a (tanga/oziq-ovqat) — kunlik limit bilan.</summary>
        [HttpPost("gift")]
        public async Task<IActionResult> Gift([FromBody] GiftRequest req)
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            string err = await _svc.SendGiftAsync(k, req);
            if (err != null) return BadRequest(new { messageUz = err });
            return Ok(new { ok = true });
        }

        /// <summary>Boshqa saltanatga xat — uzunlik, filtr va kunlik limit bilan.</summary>
        [HttpPost("letter")]
        public async Task<IActionResult> Letter([FromBody] LetterRequest req)
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            string err = await _svc.SendLetterAsync(k, req);
            if (err != null) return BadRequest(new { messageUz = err });
            return Ok(new { ok = true });
        }

        /// <summary>Kelgan xatlar qutisi.</summary>
        [HttpGet("letters")]
        public async Task<IActionResult> Letters([FromQuery] bool read = true)
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            return Ok(await _svc.LettersAsync(k, read));
        }

        [HttpGet("state")]
        public async Task<IActionResult> State()
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            await DeliverDue(k.WorldId);
            return Ok(await _svc.StateAsync(k));
        }

        /// <summary>Muddati yetgan karvonlar fon xizmatini kutmasdan ham yetkaziladi (har biri faqat bir marta).</summary>
        private async Task DeliverDue(int worldId)
        {
            foreach (CaravanEntity c in await _svc.DeliverDueAsync(worldId))
                await Simulation.WorldSimulationService.NotifyDeliveryAsync(_svc, c, _hub);
        }

        [HttpPost("fields/plant-crop")]
        public Task<IActionResult> PlantCrop([FromBody] PlantCropRequest req) => Act(k => _svc.PlantCropAsync(k, req?.CommandId, req?.FieldId ?? 0, req?.Crop));

        [HttpGet("caravans")]
        public async Task<IActionResult> Caravans()
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            await DeliverDue(k.WorldId);
            return Ok(await _svc.CaravansAsync(k));
        }

        // ---------------- Guruch ----------------
        [HttpPost("fields/build")]
        public Task<IActionResult> BuildField([FromBody] FieldActionRequest req) => Act(k => _svc.BuildFieldAsync(k, req?.CommandId));

        [HttpPost("water/build")]
        public Task<IActionResult> BuildWater([FromBody] FieldActionRequest req) => Act(k => _svc.BuildWaterAsync(k, req?.CommandId));

        [HttpPost("fields/plant")]
        public Task<IActionResult> Plant([FromBody] FieldActionRequest req) => Act(k => _svc.PlantAsync(k, req?.CommandId, req?.FieldId ?? 0));

        [HttpPost("fields/harvest")]
        public Task<IActionResult> Harvest([FromBody] FieldActionRequest req) => Act(k => _svc.HarvestAsync(k, req?.CommandId, req?.FieldId ?? 0));

        // ---------------- Bozor ----------------
        [HttpGet("market")]
        public async Task<IActionResult> Market()
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            await DeliverDue(k.WorldId);
            return Ok(await _svc.OffersAsync(k));
        }

        [HttpGet("market/history")]
        public async Task<IActionResult> History()
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            return Ok(await _svc.HistoryAsync(k));
        }

        [HttpPost("market/offers")]
        public async Task<IActionResult> CreateOffer([FromBody] CreateOfferRequest req)
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            if (req == null) return BadRequest(ActionResultDto.Fail("So'rov bo'sh"));
            ActionResultDto r = await _svc.CreateOfferAsync(k, req);
            if (r.Success && !r.Duplicate) await _hub.Clients.Group(GameHub.WorldGroup(k.WorldId)).SendAsync("MarketChanged", k.WorldId);
            return Ok(r);
        }

        [HttpPost("market/cancel")]
        public async Task<IActionResult> Cancel([FromBody] BuyOfferRequest req)
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            ActionResultDto r = await _svc.CancelOfferAsync(k, req?.CommandId, req?.OfferId ?? 0);
            if (r.Success && !r.Duplicate) await _hub.Clients.Group(GameHub.WorldGroup(k.WorldId)).SendAsync("MarketChanged", k.WorldId);
            return Ok(r);
        }

        [HttpPost("market/buy")]
        public async Task<IActionResult> Buy([FromBody] BuyOfferRequest req)
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            if (req == null) return BadRequest(ActionResultDto.Fail("So'rov bo'sh"));
            MarketOfferEntity sold = null;
            ActionResultDto r = await _svc.BuyAsync(k, req, o => sold = o);
            if (sold != null)
            {
                // Ikkala client ham yangilanadi: bozor ro'yxati (hamma) va sotuvchining holati (shaxsan).
                await _hub.Clients.Group(GameHub.WorldGroup(k.WorldId)).SendAsync("MarketChanged", k.WorldId);
                int? sellerUser = await _svc.OwnerUserIdAsync(sold.SellerKingdomId);
                if (sellerUser != null)
                    await _hub.Clients.User(sellerUser.Value.ToString()).SendAsync("StateChanged", sold.SellerKingdomId,
                        "Mahsulotingiz sotildi: " + sold.Quantity + " " + AndijonSaltanatlar.Shared.Farming.CropCatalog.NameOf(sold.Product).ToLowerInvariant() + " — karvon yo'lga chiqdi, " + sold.TotalPrice + " tanga yetkazilganda tushadi");
            }
            return Ok(r);
        }

        private async Task<IActionResult> Act(System.Func<Kingdom, Task<ActionResultDto>> action)
        {
            Kingdom k = await MyKingdom();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            return Ok(await action(k));
        }
    }
}
