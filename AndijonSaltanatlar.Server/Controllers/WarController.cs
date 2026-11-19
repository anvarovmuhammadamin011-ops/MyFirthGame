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
    /// PROMPT 25 — Harbiy boshqaruv API. Saltanat har doim JWT dagi foydalanuvchidan aniqlanadi (client boshqa
    /// saltanat nomidan buyruq bera olmaydi). Har bir o'zgarish — CommandId bilan (takror yuborilsa qayta bajarilmaydi).
    /// Real vaqt xabarlari (SignalR) faqat tranzaksiya muvaffaqiyatli yakunlangandan keyin yuboriladi.
    /// </summary>
    [ApiController]
    [Route("api/war")]
    [Authorize]
    public class WarController : ControllerBase
    {
        private readonly OnlineGameService _online;
        private readonly WarService _war;
        private readonly IHubContext<GameHub> _hub;

        public WarController(OnlineGameService online, WarService war, IHubContext<GameHub> hub)
        {
            _online = online; _war = war; _hub = hub;
        }

        private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int id) ? id : 0;
        private async Task<Kingdom> Me() => await _online.KingdomOfUserAsync(UserId);

        [HttpGet("overview")]
        public async Task<IActionResult> Overview()
        {
            Kingdom k = await Me();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            WarOverviewDto dto = await _war.OverviewAsync(k);
            await FlushAsync();
            return Ok(dto);
        }

        [HttpGet("battle/{id:int}")]
        public async Task<IActionResult> Battle(int id)
        {
            Kingdom k = await Me();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            BattleDto b = await _war.BattleAsync(k, id);
            await FlushAsync();
            return b == null ? NotFound(new { messageUz = "Jang topilmadi" }) : Ok(b);
        }

        [HttpPost("recruit")] public Task<IActionResult> Recruit([FromBody] RecruitRequest r) => Act(k => _war.RecruitAsync(k, r));
        [HttpPost("upgrade")] public Task<IActionResult> Upgrade([FromBody] UpgradeUnitsRequest r) => Act(k => _war.UpgradeAsync(k, r));
        [HttpPost("facility")] public Task<IActionResult> Facility([FromBody] FacilityRequest r) => Act(k => _war.BuildFacilityAsync(k, r));
        [HttpPost("commander/hire")] public Task<IActionResult> Hire([FromBody] CommanderRequest r) => Act(k => _war.HireCommanderAsync(k, r));
        [HttpPost("commander/assign")] public Task<IActionResult> Assign([FromBody] CommanderRequest r) => Act(k => _war.AssignCommanderAsync(k, r));
        [HttpPost("army/create")] public Task<IActionResult> CreateArmy([FromBody] CreateArmyRequest r) => Act(k => _war.CreateArmyAsync(k, r));
        [HttpPost("decision")] public Task<IActionResult> Decision([FromBody] BattleDecisionRequest r) => Act(k => _war.SubmitDecisionAsync(k, r), r?.BattleId ?? 0);
        [HttpPost("march")] public Task<IActionResult> March([FromBody] MarchRequest r) => Act(k => _war.MarchAsync(k, r));
        [HttpPost("siege/start")] public Task<IActionResult> SiegeStart([FromBody] SiegeStartRequest r) => Act(k => _war.SiegeStartAsync(k, r));
        [HttpPost("siege/finish")] public Task<IActionResult> SiegeFinish([FromBody] SiegeFinishRequest r) => Act(k => _war.SiegeFinishAsync(k, r));
        [HttpPost("peace")] public Task<IActionResult> Peace([FromBody] PeaceRequest r) => Act(k => _war.ProposePeaceAsync(k, r));
        [HttpPost("pvp/invite")] public Task<IActionResult> Invite([FromBody] PvpInviteRequest r) => Act(k => _war.InviteAsync(k, r));
        [HttpPost("pvp/setting")] public Task<IActionResult> PvpSetting([FromBody] PvpSettingRequest r) => Act(k => _war.SetPvpAsync(k, r));

        [HttpPost("battle/start")]
        public async Task<IActionResult> Start([FromBody] StartBattleRequest r)
        {
            Kingdom k = await Me();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            var (res, battleId) = await _war.StartAiBattleAsync(k, r);
            return await Reply(k, res, battleId);
        }

        [HttpPost("pvp/respond")]
        public async Task<IActionResult> Respond([FromBody] PvpRespondRequest r)
        {
            Kingdom k = await Me();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            var (res, battleId) = await _war.RespondAsync(k, r);
            return await Reply(k, res, battleId);
        }

        private async Task<IActionResult> Act(System.Func<Kingdom, Task<ActionResultDto>> action, int battleId = 0)
        {
            Kingdom k = await Me();
            if (k == null) return NotFound(new { messageUz = "Avval dunyoga qo'shiling" });
            ActionResultDto res = await action(k);
            return await Reply(k, res, battleId);
        }

        private async Task<IActionResult> Reply(Kingdom k, ActionResultDto res, int battleId)
        {
            if (res.Success) await FlushAsync(); else _war.Events.Clear();
            WarOverviewDto overview = await _war.OverviewAsync(k);
            await FlushAsync();
            return Ok(new WarActionResultDto { Success = res.Success, Duplicate = res.Duplicate, MessageUz = res.MessageUz, BattleId = battleId, Overview = overview });
        }

        private async Task FlushAsync()
        {
            foreach (WarEvent e in _war.Events)
                await _hub.Clients.Group(GameHub.WorldGroup(e.WorldId)).SendCoreAsync(e.Name, e.Args);
            _war.Events.Clear();
        }
    }
}
