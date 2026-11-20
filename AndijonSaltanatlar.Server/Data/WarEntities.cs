using System;

namespace AndijonSaltanatlar.Server.Data
{
    // PROMPT 25 — urush tizimi jadvallari. Barcha holat serverda: server qayta ishga tushsa ham
    // qo'shinlar, faol janglar, qarorlar va hudud egaligi saqlanadi.

    /// <summary>Saltanat harbiy holati (bitta qator): PvP sozlamasi va saqlash xarajati hisobi.</summary>
    public class KingdomWarStateEntity
    {
        public int Id { get; set; }
        public int KingdomId { get; set; }
        public bool AcceptsPvp { get; set; } = true;
        /// <summary>O'yinchi "PvP ni yoqish" ni o'zi tanladi — yangi o'yinchi himoyasidan ixtiyoriy voz kechdi.</summary>
        public bool ProtectionWaived { get; set; }
        /// <summary>Saqlash xarajati oxirgi marta yechilgan o'yin kuni.</summary>
        public long LastUpkeepDay { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        /// <summary>Keyingi AI hujumi vaqti (faqat o'yinchilar uchun). Bo'sh (0001) — hali rejalashtirilmagan.</summary>
        public DateTime NextAiAttackAt { get; set; }
    }

    public class ArmyEntity
    {
        public int Id { get; set; }
        public int KingdomId { get; set; }
        public string Name { get; set; } = "";
        public int RegionId { get; set; }
        public int Status { get; set; }              // Shared.War.ArmyStatus
        public int Morale { get; set; } = 70;
        public int Readiness { get; set; } = 70;
        public int CommanderId { get; set; }
        public DateTime StatusUntil { get; set; } = DateTime.UtcNow;
        /// <summary>Xaritadagi yurish: qayerdan → qaysi qasrga, qachon chiqdi (yetib borish vaqti — StatusUntil).</summary>
        public int OriginRegionId { get; set; }
        public int TargetRegionId { get; set; }
        public DateTime DepartAt { get; set; } = DateTime.UtcNow;
        /// <summary>Qasr garnizoni (shahar militsiyasi) — qo'shin yo'q bo'lsa qasrni himoya qiladi; yurishga chiqmaydi.</summary>
        public bool IsGarrison { get; set; }
    }

    /// <summary>Qo'shindagi birlik guruhi: (yo'nalish, daraja) bo'yicha bitta qator.</summary>
    public class ArmyUnitEntity
    {
        public int Id { get; set; }
        public int ArmyId { get; set; }
        public int Line { get; set; }               // Shared.War.UnitLine
        public int Level { get; set; } = 1;
        public int Count { get; set; }
    }

    public class CommanderEntity
    {
        public int Id { get; set; }
        public int KingdomId { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; } = 1;
        public int Experience { get; set; }
        public int AdministrationBonus { get; set; }
        public int ArmyId { get; set; }
        public int Portrait { get; set; }
    }

    public class MilitaryFacilityEntity
    {
        public int Id { get; set; }
        public int KingdomId { get; set; }
        public int Kind { get; set; }               // Shared.War.FacilityKind
        public int Level { get; set; }
    }

    /// <summary>Yollash navbati: tanga buyurtma paytida BIR MARTA yechiladi, birliklar ReadyAt da qo'shinga qo'shiladi.</summary>
    public class RecruitOrderEntity
    {
        public int Id { get; set; }
        public int KingdomId { get; set; }
        public int ArmyId { get; set; }
        public int Line { get; set; }
        public int Count { get; set; }
        public DateTime ReadyAt { get; set; }
        public bool Delivered { get; set; }
    }

    /// <summary>Strategik jang (AI yoki PvP). Status shartli yangilanadi — jang faqat bir marta yakunlanadi.</summary>
    public class BattleEntity
    {
        public int Id { get; set; }
        public int WorldId { get; set; }
        public bool IsPvp { get; set; }
        public int Status { get; set; }             // Shared.War.BattleStatus
        public int Round { get; set; } = 1;
        public int Seed { get; set; }
        public int RegionId { get; set; }
        public bool TerritoryStake { get; set; } = true;
        public int AttackerKingdomId { get; set; }
        public int DefenderKingdomId { get; set; }
        public int AttackerArmyId { get; set; }
        public int DefenderArmyId { get; set; }
        public int AttackerStartPower { get; set; }
        public int DefenderStartPower { get; set; }
        public int AttackerPower { get; set; }
        public int DefenderPower { get; set; }
        public int AttackerMorale { get; set; }
        public int DefenderMorale { get; set; }
        public int AttackerResiliencePct { get; set; }
        public int DefenderResiliencePct { get; set; }
        public int DefenseBonusPct { get; set; }
        public int WinnerKingdomId { get; set; }
        public bool Peace { get; set; }
        public bool TerritoryTransferred { get; set; }
        public int CostCoins { get; set; }
        /// <summary>Poytaxt qamali: g'alabada hudud emas, xazinadan o'lja olinadi (saltanat yo'q bo'lib ketmaydi).</summary>
        public bool IsCapitalSiege { get; set; }
        public int PlunderCoins { get; set; }
        public string ResultUz { get; set; } = "";
        public string DiplomacyUz { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime RoundStartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }

    /// <summary>Raund qarori. (BattleId, Round, KingdomId) UNIQUE — bir raundda bitta qaror, takror yuborib bo'lmaydi.</summary>
    public class BattleDecisionEntity
    {
        public int Id { get; set; }
        public int BattleId { get; set; }
        public int Round { get; set; }
        public int KingdomId { get; set; }
        public int Decision { get; set; }
        public bool Automatic { get; set; }
        public DateTime At { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Hisoblangan raund natijasi (jang hisoboti uchun).</summary>
    public class BattleRoundEntity
    {
        public int Id { get; set; }
        public int BattleId { get; set; }
        public int Round { get; set; }
        public int AttackerDecision { get; set; }
        public int DefenderDecision { get; set; }
        public int AttackerLoss { get; set; }
        public int DefenderLoss { get; set; }
        public string SummaryUz { get; set; } = "";
    }

    public class BattleInvitationEntity
    {
        public int Id { get; set; }
        public int WorldId { get; set; }
        public int FromKingdomId { get; set; }
        public int FromArmyId { get; set; }
        public int ToKingdomId { get; set; }
        public int StakeRegionId { get; set; }
        public int Status { get; set; }             // Shared.War.InvitationStatus
        public int BattleId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>Ikki saltanat o'rtasidagi mojaro / sulh holati. KingdomA &lt; KingdomB.</summary>
    public class KingdomConflictEntity
    {
        public int Id { get; set; }
        public int WorldId { get; set; }
        public int KingdomA { get; set; }
        public int KingdomB { get; set; }
        public bool Active { get; set; }
        public DateTime TruceUntil { get; set; } = DateTime.MinValue;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
