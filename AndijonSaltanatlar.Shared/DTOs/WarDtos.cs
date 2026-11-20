using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Shared.War;

namespace AndijonSaltanatlar.Shared.DTOs
{
    // PROMPT 25 — urush tizimi DTO'lari. Barcha raqamlar serverdan; client hech narsani o'zi hisoblamaydi.

    public sealed class UnitGroupDto
    {
        public UnitLine Line { get; set; }
        public int Level { get; set; }
        public int Count { get; set; }
        public string NameUz { get; set; } = "";
    }

    public sealed class ArmyDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public ArmyStatus Status { get; set; }
        public string StatusUz { get; set; } = "";
        public int Morale { get; set; }
        public int Readiness { get; set; }
        public int Strength { get; set; }
        public int UnitCount { get; set; }
        public int Upkeep { get; set; }
        public int RegionId { get; set; }
        public string RegionName { get; set; } = "";
        public int CommanderId { get; set; }
        public string CommanderName { get; set; } = "";
        public int CommanderLevel { get; set; }
        public string ActiveTaskUz { get; set; } = "";
        public List<UnitGroupDto> Groups { get; set; } = new();
    }

    public sealed class CommanderDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public string RankUz { get; set; } = "";
        public int Experience { get; set; }
        public int NextLevelXp { get; set; }
        public int PowerBonusPct { get; set; }
        public int MoraleBonus { get; set; }
        public int AdministrationBonus { get; set; }
        public int AssignedArmyId { get; set; }
        public int Portrait { get; set; }
    }

    public sealed class FacilityDto
    {
        public FacilityKind Kind { get; set; }
        public string NameUz { get; set; } = "";
        public string EffectUz { get; set; } = "";
        public int Level { get; set; }
        public int MaxLevel { get; set; }
        public int UpgradeCost { get; set; }
        public int Upkeep { get; set; }
    }

    public sealed class RecruitOrderDto
    {
        public int Id { get; set; }
        public int ArmyId { get; set; }
        public string NameUz { get; set; } = "";
        public int Count { get; set; }
        public int SecondsLeft { get; set; }
    }

    /// <summary>Hujum qilish mumkin bo'lgan hudud (boshqa saltanatniki) — mojaro oqibatlari bilan.</summary>
    public sealed class WarTargetDto
    {
        public int RegionId { get; set; }
        public string RegionName { get; set; } = "";
        public int OwnerKingdomId { get; set; }
        public string OwnerName { get; set; } = "";
        public bool OwnerIsAi { get; set; }
        public int EstimatedDefense { get; set; }
        public int Relation { get; set; }
        public int RelationAfter { get; set; }
        public int Cost { get; set; }
        public int Population { get; set; }
        public bool CanAttack { get; set; }
        public string BlockReasonUz { get; set; } = "";
        public string ConsequencesUz { get; set; } = "";
        public float MapX { get; set; }
        public float MapY { get; set; }
    }

    /// <summary>PvP uchun boshqa real o'yinchi saltanati.</summary>
    public sealed class PvpOpponentDto
    {
        public int KingdomId { get; set; }
        public string Name { get; set; } = "";
        public string RulerName { get; set; } = "";
        public int Strength { get; set; }
        public bool Protected { get; set; }
        public bool AcceptsPvp { get; set; }
        public string NoteUz { get; set; } = "";
        public List<RegionStakeDto> StakeRegions { get; set; } = new();
    }

    public sealed class RegionStakeDto { public int RegionId { get; set; } public string Name { get; set; } = ""; }

    public sealed class BattleInvitationDto
    {
        public int Id { get; set; }
        public int FromKingdomId { get; set; }
        public string FromName { get; set; } = "";
        public int ToKingdomId { get; set; }
        public string ToName { get; set; } = "";
        public int StakeRegionId { get; set; }
        public string StakeRegionName { get; set; } = "";
        public InvitationStatus Status { get; set; }
        public bool Incoming { get; set; }
        public int SecondsLeft { get; set; }
        public string TermsUz { get; set; } = "";
    }

    public sealed class BattleRoundDto
    {
        public int Round { get; set; }
        public BattleDecision AttackerDecision { get; set; }
        public BattleDecision DefenderDecision { get; set; }
        public int AttackerLoss { get; set; }
        public int DefenderLoss { get; set; }
        public string SummaryUz { get; set; } = "";
    }

    public sealed class BattleDto
    {
        public int Id { get; set; }
        public BattleStatus Status { get; set; }
        public bool IsPvp { get; set; }
        public int Round { get; set; }
        public int MaxRounds { get; set; } = WarRules.MaxRounds;
        public int RegionId { get; set; }
        public string RegionName { get; set; } = "";
        public int AttackerKingdomId { get; set; }
        public string AttackerName { get; set; } = "";
        public int DefenderKingdomId { get; set; }
        public string DefenderName { get; set; } = "";
        public int[] AttackerColor { get; set; } = { 70, 130, 220 };
        public int[] DefenderColor { get; set; } = { 190, 80, 60 };
        /// <summary>Saltanat bayroqlari (FlagSpec matni).</summary>
        public string AttackerFlag { get; set; } = "";
        public string DefenderFlag { get; set; } = "";
        public string AttackerCommander { get; set; } = "";
        public string DefenderCommander { get; set; } = "";
        public int AttackerStartPower { get; set; }
        public int DefenderStartPower { get; set; }
        public int AttackerPower { get; set; }
        public int DefenderPower { get; set; }
        public int AttackerMorale { get; set; }
        public int DefenderMorale { get; set; }
        /// <summary>Joriy o'yinchi shu jangda hujumchimi (false — himoyachi yoki kuzatuvchi).</summary>
        public bool IAmAttacker { get; set; }
        public bool IAmParticipant { get; set; }
        /// <summary>Joriy raund uchun mening qarorim yuborilganmi.</summary>
        public bool MyDecisionSubmitted { get; set; }
        public bool OpponentDecisionSubmitted { get; set; }
        public int RoundSecondsLeft { get; set; }
        public int WinnerKingdomId { get; set; }
        public bool Peace { get; set; }
        public bool TerritoryTransferred { get; set; }
        public string ResultUz { get; set; } = "";
        public string DiplomacyUz { get; set; } = "";
        public int CostCoins { get; set; }
        public List<UnitGroupDto> AttackerGroups { get; set; } = new();
        public List<UnitGroupDto> DefenderGroups { get; set; } = new();
        public List<BattleRoundDto> Rounds { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Dunyo xaritasidagi qasr (hudud) — yurish uchun.</summary>
    public sealed class WarMapRegionDto
    {
        public int RegionId { get; set; }
        public string Name { get; set; } = "";
        public string CityName { get; set; } = "";
        public int OwnerKingdomId { get; set; }
        public string OwnerName { get; set; } = "";
        public bool OwnerIsAi { get; set; }
        public bool IsMine { get; set; }
        public bool IsCapital { get; set; }
        public int[] Color { get; set; } = { 128, 128, 128 };
        /// <summary>Egasining bayrog'i (FlagSpec matni; mustaqil hududda bo'sh).</summary>
        public string Flag { get; set; } = "";
        /// <summary>Viloyat kaliti (UzMap.Provinces).</summary>
        public string Province { get; set; } = "";
        public float MapX { get; set; }
        public float MapY { get; set; }
        public int Population { get; set; }
        public int DefenseEstimate { get; set; }
        public bool CanMarch { get; set; }
        /// <summary>Shahardagi o'z jangchilaringiz bilan qamal qilish mumkinmi (server qo'shini shart emas).</summary>
        public bool CanSiege { get; set; }
        /// <summary>Qasr himoya darajasi (0..WarRules.MaxFortify) — o'zingizniki uchun kuchaytirish holati.</summary>
        public int FortifyLevel { get; set; }
        /// <summary>Shu qasrni hozir kuchaytirish mumkin (o'ziniki, poytaxt emas, daraja to'lmagan).</summary>
        public bool CanFortify { get; set; }
        /// <summary>Qasr egasining hukmdori (qamalda saroyda o'ldiriladigan shoh).</summary>
        public string RulerName { get; set; } = "";
        public string BlockReasonUz { get; set; } = "";
        public string OutcomeUz { get; set; } = "";
        public int MarchSeconds { get; set; }
    }

    /// <summary>Xaritada yurayotgan qo'shin (barcha o'yinchilar ko'radi).</summary>
    public sealed class MarchDto
    {
        public int ArmyId { get; set; }
        public string ArmyName { get; set; } = "";
        public int KingdomId { get; set; }
        public string KingdomName { get; set; } = "";
        public int[] Color { get; set; } = { 128, 128, 128 };
        public string Flag { get; set; } = "";
        public int OriginRegionId { get; set; }
        public int TargetRegionId { get; set; }
        public string TargetName { get; set; } = "";
        public int TotalSeconds { get; set; }
        public int SecondsLeft { get; set; }
        public int UnitCount { get; set; }
        public bool IsMine { get; set; }
        public bool TargetIsMine { get; set; }
    }

    public sealed class WarOverviewDto
    {
        public List<WarMapRegionDto> MapRegions { get; set; } = new();
        public List<MarchDto> Marches { get; set; } = new();
        public int KingdomId { get; set; }
        public int Coin { get; set; }
        public int Capacity { get; set; }
        public int UnitsTotal { get; set; }
        public int MaxArmies { get; set; }
        public int DailyUpkeep { get; set; }
        public bool AcceptsPvp { get; set; }
        public bool Protected { get; set; }
        public List<ArmyDto> Armies { get; set; } = new();
        public List<CommanderDto> Commanders { get; set; } = new();
        public List<FacilityDto> Facilities { get; set; } = new();
        public List<RecruitOrderDto> Queue { get; set; } = new();
        public List<WarTargetDto> Targets { get; set; } = new();
        public List<PvpOpponentDto> Opponents { get; set; } = new();
        public List<BattleInvitationDto> Invitations { get; set; } = new();
        public BattleDto ActiveBattle { get; set; }
        public List<BattleDto> History { get; set; } = new();
        public List<string> AdviceUz { get; set; } = new();
        /// <summary>Faol mojarolar (tinchlik taklif qilish mumkin).</summary>
        public List<ConflictDto> Conflicts { get; set; } = new();
    }

    public sealed class ConflictDto
    {
        public int KingdomId { get; set; }
        public string KingdomName { get; set; } = "";
        public bool Active { get; set; }
        public int TruceMinutesLeft { get; set; }
        public int Relation { get; set; }
    }

    // ---- So'rovlar (har birida CommandId — idempotentlik) ----
    public sealed class RecruitRequest { public string CommandId { get; set; } = ""; public int ArmyId { get; set; } public UnitLine Line { get; set; } public int Count { get; set; } }
    public sealed class UpgradeUnitsRequest { public string CommandId { get; set; } = ""; public int ArmyId { get; set; } public UnitLine Line { get; set; } public int FromLevel { get; set; } public int Count { get; set; } }
    public sealed class FacilityRequest { public string CommandId { get; set; } = ""; public FacilityKind Kind { get; set; } }
    /// <summary>Egallangan qasrni bir daraja kuchaytirish (devor, darvoza, minoralar).</summary>
    public sealed class FortifyRequest { public string CommandId { get; set; } = ""; public int RegionId { get; set; } }
    public sealed class CommanderRequest { public string CommandId { get; set; } = ""; public int CommanderId { get; set; } public int ArmyId { get; set; } }
    public sealed class CreateArmyRequest { public string CommandId { get; set; } = ""; public string Name { get; set; } = ""; }
    /// <summary>Qamal: o'yinchi shahridagi jangchilar bilan qasrga yurish (jang o'yinchi kompyuterida, real vaqtda).</summary>
    public sealed class SiegeStartRequest { public string CommandId { get; set; } = ""; public int RegionId { get; set; } }
    /// <summary>Qamal natijasi: g'alaba (shoh va vazir o'ldirildi, saroy olindi) yoki mag'lubiyat/chekinish.</summary>
    public sealed class SiegeFinishRequest
    {
        public string CommandId { get; set; } = "";
        public int RegionId { get; set; }
        public bool Won { get; set; }
        public int AttackerFallen { get; set; }
        public int AttackerTotal { get; set; }
        public int DefendersDown { get; set; }
        public int DefendersTotal { get; set; }
    }
    public sealed class MarchRequest { public string CommandId { get; set; } = ""; public int ArmyId { get; set; } public int RegionId { get; set; } }
    public sealed class StartBattleRequest { public string CommandId { get; set; } = ""; public int ArmyId { get; set; } public int RegionId { get; set; } }
    public sealed class BattleDecisionRequest { public string CommandId { get; set; } = ""; public int BattleId { get; set; } public int Round { get; set; } public BattleDecision Decision { get; set; } }
    public sealed class PvpInviteRequest { public string CommandId { get; set; } = ""; public int ToKingdomId { get; set; } public int ArmyId { get; set; } public int StakeRegionId { get; set; } }
    public sealed class PvpRespondRequest { public string CommandId { get; set; } = ""; public int InvitationId { get; set; } public bool Accept { get; set; } public int ArmyId { get; set; } }
    public sealed class PeaceRequest { public string CommandId { get; set; } = ""; public int KingdomId { get; set; } }
    public sealed class PvpSettingRequest { public string CommandId { get; set; } = ""; public bool AcceptsPvp { get; set; } }

    public sealed class WarActionResultDto
    {
        public bool Success { get; set; }
        public bool Duplicate { get; set; }
        public string MessageUz { get; set; } = "";
        public int BattleId { get; set; }
        public WarOverviewDto Overview { get; set; }
    }
}
