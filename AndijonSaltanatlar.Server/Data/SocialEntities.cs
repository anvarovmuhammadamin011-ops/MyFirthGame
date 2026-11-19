using System;

namespace AndijonSaltanatlar.Server.Data
{
    /// <summary>O'yinchidan o'yinchiga xat (diplomatik aloqa). Matn serverda filtrlanadi va cheklanadi.</summary>
    public class PlayerLetterEntity
    {
        public int Id { get; set; }
        public int WorldId { get; set; }
        public int FromKingdomId { get; set; }
        public int ToKingdomId { get; set; }
        public string FromName { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime SentAt { get; set; }
        public bool Read { get; set; }
        // --- ELCHI: maktubni elchi olib boradi (yetib borguncha qabul qiluvchi ko'rmaydi) ---
        /// <summary>Elchi yetib boradigan vaqt (eski xatlar uchun 0001-01-01 — allaqachon yetkazilgan).</summary>
        public DateTime DeliverAt { get; set; }
        public int FromRegionId { get; set; }
        public int ToRegionId { get; set; }
        public string EnvoyName { get; set; } = "";
        public string ToName { get; set; } = "";
    }

    /// <summary>Sovg'a jurnali: kunlik limitni tekshirish va tarix uchun.</summary>
    public class GiftLogEntity
    {
        public int Id { get; set; }
        public int WorldId { get; set; }
        public int FromKingdomId { get; set; }
        public int ToKingdomId { get; set; }
        /// <summary>"coin" yoki "food".</summary>
        public string Kind { get; set; } = "coin";
        public int Amount { get; set; }
        public DateTime SentAt { get; set; }
    }
}
