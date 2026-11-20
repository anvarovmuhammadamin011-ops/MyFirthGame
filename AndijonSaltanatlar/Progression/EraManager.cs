namespace AndijonSaltanatlar.Progression
{
    /// <summary>
    /// Tarixiy davrni ochilgan texnologiyalar soniga qarab belgilaydi. Chiziqli daraja emas —
    /// badiiy uslub Temuriylar/Bobur davridan ilhomlangan; keyingi davrlar kelajakda.
    /// </summary>
    public static class EraManager
    {
        public static string EraName(int researchedCount) => EraNameOf(EraIndex(researchedCount));

        /// <summary>Davr raqami: 0 ilk o'rta asrlar, 1 rivojlangan o'rta asrlar, 2 Temuriylar, 3 Bobur davri (XVI asr).</summary>
        public static int EraIndex(int researchedCount)
        {
            if (researchedCount < 3) return 0;
            if (researchedCount < 7) return 1;
            if (researchedCount < 12) return 2;
            return 3;
        }

        public static string EraNameOf(int era) => era switch
        {
            <= 0 => "Ilk o'rta asrlar",
            1 => "Rivojlangan o'rta asrlar",
            2 => "Temuriylar davri",
            _ => "Bobur davri (XVI asr)"
        };

        /// <summary>Shu davrga yetish uchun kerakli o'rganilgan texnologiyalar soni.</summary>
        public static int ResearchFor(int era) => era switch { <= 0 => 0, 1 => 3, 2 => 7, _ => 12 };
    }
}
