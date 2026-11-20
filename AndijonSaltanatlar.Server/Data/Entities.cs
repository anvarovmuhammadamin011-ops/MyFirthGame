using System;

namespace AndijonSaltanatlar.Server.Data
{
    // Foydalanuvchi akkaunti (parol xeshlangan holda saqlanadi).
    public class AppUser
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }
        public int? KingdomId { get; set; }
        public string PreferredLanguage { get; set; } = "uz";
        /// <summary>Google hisobining doimiy identifikatori ("sub"). Kirish faqat Google orqali.</summary>
        public string GoogleSub { get; set; }
        /// <summary>O'yindagi taxallus (yangi o'yinchi o'zi tanlaydi; Google'dagi ism majburan ko'rsatilmaydi).</summary>
        public string DisplayName { get; set; }
        /// <summary>O'yinchi taxallus va avatarni tanlaganmi (YANGI HUKMDOR ekrani o'tilgan).</summary>
        public bool ProfileCompleted { get; set; }
        public int AvatarId { get; set; }
        /// <summary>VIP hisob: barcha imkoniyatlar ochilgan (serverdagi Vip:Accounts ro'yxati bo'yicha).</summary>
        public bool IsVip { get; set; }
    }

    /// <summary>Qurilma sessiyasi: yangilash tokeni (faqat xesh), muddati, bekor qilinganligi.</summary>
    public class PlayerSessionEntity
    {
        public string Id { get; set; }
        public int UserId { get; set; }
        public string RefreshTokenHash { get; set; }
        public string PreviousTokenHash { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string DeviceLabel { get; set; }
    }

    /// <summary>
    /// O'yinchining shahar holati (bulutdagi saqlash). Har foydalanuvchida bitta qator — o'yin istalgan
    /// qurilmada (Windows yoki Android) shu yerdan davom etadi. Json — client SaveGameData'si.
    /// </summary>
    public class CloudSaveEntity
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Json { get; set; } = "";
        /// <summary>Har saqlashda oshadi (eski qurilma yangi saqlash ustidan yozmasligi uchun).</summary>
        public long Version { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string Device { get; set; } = "";
    }

    public class World
    {
        public int Id { get; set; }
        public string Name { get; set; } = "Andijon — 1-dunyo";
        public int Day { get; set; } = 1;
        public long StateVersion { get; set; }
        public DateTime LastTickAt { get; set; } = DateTime.UtcNow;
        /// <summary>Dunyo o'yin vaqtining boshlanishi (server o'yin soati shundan hisoblanadi).</summary>
        public DateTime EpochUtc { get; set; } = DateTime.UtcNow;
        /// <summary>Xarita versiyasi (UzMap.Version): eskirgan bo'lsa dunyo yangi xaritaga ko'chiriladi.</summary>
        public int MapVersion { get; set; }
    }

    public class Kingdom
    {
        public int Id { get; set; }
        public int WorldId { get; set; }
        public string Name { get; set; }
        public string RulerName { get; set; }
        public bool IsAi { get; set; }
        public int? OwnerUserId { get; set; }   // null = AI
        public int ColorR { get; set; } = 120;
        public int ColorG { get; set; } = 120;
        public int ColorB { get; set; } = 130;
        public int Population { get; set; }
        public long StateVersion { get; set; }
        public string AiStrategy { get; set; } = "balanced";
        public DateTime NextDecisionAt { get; set; } = DateTime.UtcNow;
        /// <summary>Saltanat bayrog'i (FlagSpec matni). Bo'sh bo'lsa — rangi asosidagi standart bayroq.</summary>
        public string Flag { get; set; } = "";
    }

    public class KingdomResource
    {
        public int Id { get; set; }
        public int KingdomId { get; set; }
        public int Coin { get; set; } = 500;
        public int Food { get; set; } = 200;
        /// <summary>PROMPT 20: boshlang'ich zaxira; keyin shaharlar ishlab chiqaradi (server simulyatsiyasi).</summary>
        public int Wood { get; set; } = 120;
        public int Stone { get; set; } = 60;
        // --- PROMPT 19A ---
        /// <summary>Ombordagi (sotish mumkin bo'lgan) guruch.</summary>
        public int Rice { get; set; } = 0;
        /// <summary>Bozorga qo'yilgan, rezervdagi guruch (boshqa taklifga qo'yib bo'lmaydi).</summary>
        public int RiceReserved { get; set; } = 0;
        /// <summary>Suv manbalari (ariq) soni — nechta maydonni sug'orish mumkinligini belgilaydi.</summary>
        public int WaterSources { get; set; } = 1;
    }

    /// <summary>Guruch maydoni (PROMPT 19A). Saqlangan holat + ekilgan server o'yin vaqti; joriy holat hisoblanadi.</summary>
    public class RiceFieldEntity
    {
        public int Id { get; set; }
        public int KingdomId { get; set; }
        public int Slot { get; set; }
        /// <summary>PROMPT 19B: ekin turi (Shared.Farming.CropType; 0 = guruch).</summary>
        public int CropType { get; set; }
        public int State { get; set; }               // Shared.Farming.RiceFieldState (Empty/Planted/Harvested saqlanadi)
        public long PlantedAtGameMinute { get; set; }
        public int HarvestCount { get; set; }
        /// <summary>Optimistik versiya — bir hosil ikki marta yig'ilmasligi uchun shartli yangilashda ishlatiladi.</summary>
        public int Version { get; set; }
    }

    public static class OfferStatus
    {
        public const int Active = 0, Sold = 1, Cancelled = 2;
    }

    /// <summary>Saltanatlar bozoridagi o'yinchi taklifi (PROMPT 19A).</summary>
    public class MarketOfferEntity
    {
        public int Id { get; set; }
        public int WorldId { get; set; }
        public int SellerKingdomId { get; set; }
        public string Product { get; set; } = "Rice";
        public int Quantity { get; set; }
        public int TotalPrice { get; set; }
        public int Status { get; set; }              // OfferStatus
        /// <summary>PROMPT 20: 0 = umumiy bozor; aks holda faqat shu ittifoq a'zolari ko'radi va sotib oladi.</summary>
        public int AllianceId { get; set; }
        public int? BuyerKingdomId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ClosedAt { get; set; }
    }

    /// <summary>Yakunlangan savdo. OfferId UNIQUE — bitta taklif ma'lumotlar bazasi darajasida ham faqat bir marta sotiladi.</summary>
    public class TradeTransactionEntity
    {
        public int Id { get; set; }
        public int WorldId { get; set; }
        public int OfferId { get; set; }
        public int SellerKingdomId { get; set; }
        public int BuyerKingdomId { get; set; }
        public string Product { get; set; } = "Rice";
        public int Quantity { get; set; }
        public int TotalPrice { get; set; }
        public string CommandId { get; set; } = "";
        public DateTime At { get; set; } = DateTime.UtcNow;
        /// <summary>0 = karvon yo'lda (xaridor tangasi rezervda), 1 = yetkazildi va savdo yakunlandi.</summary>
        public int DeliveryStatus { get; set; }
    }

    public class RegionEntity
    {
        public int Id { get; set; }
        public int WorldId { get; set; }
        public string Name { get; set; }
        public int OwnerKingdomId { get; set; }  // 0 = neytral
        /// <summary>Viloyat kaliti (UzMap.Provinces: "an", "fg", "tv", ...).</summary>
        public string Province { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int Development { get; set; } = 30;
        /// <summary>Eski maydon (ishlatilmaydi). Hudud aholisi — shaharlari aholisi yig'indisi sifatida hisoblanadi.</summary>
        public int Population { get; set; }
        /// <summary>PROMPT 19B: barqarorlik (0..100).</summary>
        public int Stability { get; set; } = 60;
        /// <summary>Qasr himoya darajasi (0..WarRules.MaxFortify): egasi shaharga kirib kuchaytiradi. Egasi almashsa 0.</summary>
        public int Fortify { get; set; }
    }

    /// <summary>
    /// Shahar (PROMPT 19B: multi-city). Aholi SHAHARDA saqlanadi va hudud egasi o'zgarganda shahar bilan birga
    /// yangi saltanatga o'tadi — aholi alohida "resurs" sifatida ko'chirilmaydi va ikki marta sanalmaydi.
    /// </summary>
    public class CityEntity
    {
        public int Id { get; set; }
        public int KingdomId { get; set; }
        public string Name { get; set; }
        public int Level { get; set; } = 1;
        public int RegionId { get; set; }
        public bool IsCapital { get; set; }
        public int Population { get; set; }
        public int Households { get; set; }
        public int HousingCapacity { get; set; }
        public int Jobs { get; set; }
        public int Happiness { get; set; } = 65;
        /// <summary>Hukmdorga siyosiy ishonch (0..100) — yangi boshqaruvga o'tganda pasayadi va asta tiklanadi.</summary>
        public int Trust { get; set; } = 60;
        public int GovernorMode { get; set; }            // Shared.DTOs.GovernorMode
        public string GovernorName { get; set; } = "";
        public int GovernorSpendLimit { get; set; } = 300;
    }

    /// <summary>Saltanat ombori (guruchdan boshqa mahsulotlar; guruch — KingdomResource.Rice, 19A bilan mos).</summary>
    public class InventoryEntity
    {
        public int Id { get; set; }
        public int KingdomId { get; set; }
        public string Product { get; set; } = "";
        public int Quantity { get; set; }
        public int Reserved { get; set; }
    }

    /// <summary>Savdo karvoni: sotuvchi ombori → yo'l → xaridor shahri. Offline bo'lsa ham server yetkazadi.</summary>
    public class CaravanEntity
    {
        public int Id { get; set; }
        public int WorldId { get; set; }
        public int OfferId { get; set; }
        public int SellerKingdomId { get; set; }
        public int BuyerKingdomId { get; set; }
        public int FromCityId { get; set; }
        public int ToCityId { get; set; }
        public string Product { get; set; } = "Rice";
        public int Quantity { get; set; }
        public int TotalPrice { get; set; }
        public DateTime DepartAt { get; set; }
        public DateTime ArriveAt { get; set; }
        public int Status { get; set; }                  // 0 = yo'lda, 1 = yetkazildi
        public DateTime? DeliveredAt { get; set; }
    }

    /// <summary>Hokim qurilish rejasi — faqat o'yinchi tasdiqlagach bajariladi.</summary>
    public class GovernorPlanEntity
    {
        public int Id { get; set; }
        public int CityId { get; set; }
        public int KingdomId { get; set; }
        public string TitleUz { get; set; } = "";
        public string ReasonUz { get; set; } = "";
        public int Houses { get; set; }
        public int Markets { get; set; }
        public int Gardens { get; set; }
        public int Cost { get; set; }
        public int Status { get; set; }                  // 0 taklif, 1 bajarilmoqda/bajarildi, 2 rad etildi
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class BuildingEntity
    {
        public int Id { get; set; }
        public int KingdomId { get; set; }
        public int CityId { get; set; }
        public int Kind { get; set; }        // Shared.BuildingKind
        public int X { get; set; }
        public int Y { get; set; }
        public int Level { get; set; } = 1;
        public float BuildProgress { get; set; } = 1f;
    }

    public class LandParcelEntity
    {
        public int Id { get; set; }
        public int CityId { get; set; }
        public string Name { get; set; }
        public int State { get; set; }       // 0=Open,1=Locked,...
        public int UnlockCost { get; set; }
    }

    public class ResearchProgressEntity
    {
        public int Id { get; set; }
        public int KingdomId { get; set; }
        public string TechId { get; set; }
        public int Progress { get; set; }
        public bool Done { get; set; }
    }

    public class DiplomaticRelationEntity
    {
        public int Id { get; set; }
        public int WorldId { get; set; }
        public int KingdomA { get; set; }
        public int KingdomB { get; set; }
        /// <summary>Ishonch / munosabat (-100..100).</summary>
        public int Value { get; set; }
        // PROMPT 20
        public int TradeVolume { get; set; }
        public string LastEvent { get; set; } = "";
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TradeOfferEntity
    {
        public int Id { get; set; }
        public int WorldId { get; set; }
        public int FromKingdomId { get; set; }
        public int ToKingdomId { get; set; }
        public string Resource { get; set; } = "Food";
        public int Quantity { get; set; }
        public int Price { get; set; }
        public int Status { get; set; }      // Shared.TradeStatus
    }

    // Idempotentlik: bajarilgan buyruqlar (takror bajarilmasligi uchun).
    public class GameCommandEntity
    {
        public string CommandId { get; set; }  // PK
        public int KingdomId { get; set; }
        public int WorldId { get; set; }
        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    }

    public class PlayerNotificationEntity
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string TitleUz { get; set; }
        public string BodyUz { get; set; }
        public bool Read { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
