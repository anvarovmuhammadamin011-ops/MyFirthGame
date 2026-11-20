namespace AndijonSaltanatlar.Core.Graphics
{
    /// <summary>
    /// Fuqaro turlari. 0..5 — Bobur bobo 3D modellaridan (Content/Sprites/unit_citizen*.png). 6.. — kasblar: o'sha
    /// renderlardan yuklash paytida kiyimi qayta bo'yaladi (SpriteVariants) — sifat bir xil, APK hajmi oshmaydi.
    /// Saqlashda tur raqami ishlatilmaydi (ko'rinadigan figura har ochilishda kasbdan tanlanadi).
    /// </summary>
    public static class CitizenArt
    {
        public const int Citizen = 0, Merchant = 1, Craftsman = 2, Farmer = 3, Buyer = 4, Shepherd = 5;
        /// <summary>PNG fayli bor (3D render) turlar soni.</summary>
        public const int RenderedKinds = 6;

        // --- Kasblar (qayta bo'yalgan) ---
        public const int Miner = 6, Fisher = 7, Storekeeper = 8, TaxCollector = 9, BazaarOwner = 10, Scholar = 11,
            Healer = 12, Architect = 13, Builder = 14, Teacher = 15, Official = 16, Caravaneer = 17;

        public const int Kinds = 18;

        /// <summary>Bo'sh (shaffof) kadr — haqiqiy rasm PNG'dan yuklanadi.</summary>
        public static Canvas Frame(int kind, int frame) => new Canvas(48, 60);
    }
}
