namespace AndijonSaltanatlar.Population
{
    public enum CitizenProfession
    {
        None,        // ishsiz
        Dehqon,      // dehqon xo'jaligi
        Bogbon,      // bog'
        Savdogar,    // bozor
        Hunarmand,   // ustaxona
        Quruvchi,    // qurilish
        Tabib,       // tabibxona
        Muallim,     // maktab / madrasa
        Soqchi,      // qarorgoh / darvoza
        Amaldor,     // boshqaruv qarorgohi
        Tashuvchi,   // karvonsaroy (ko'rinishda "Karvonchi")
        // --- kengaytma (saqlashda butun son — faqat oxiriga qo'shiladi) ---
        Chorvador,     // ferma: chorva, sut, go'sht, jun
        Konchi,        // kon: tosh va ma'dan
        Baliqchi,      // baliqxona: daryo va ariqlardan baliq
        Omborchi,      // karvonsaroy ombori: zaxira, isrofni kamaytiradi
        SoliqYiguvchi, // saroy devoni: soliq tushumi
        BozorEgasi,    // bozor: savdo infratuzilmasi
        Olim,          // madrasa: ilm-fan, texnologiya tezligi
        Memor          // saroy: loyiha, qurilish tezligi
    }

    public enum CitizenState
    {
        Idle, AtHome, GoingToWork, Working, GoingToMarket, Shopping, ReturningHome, Resting, SeekingHelp
    }

    /// <summary>
    /// Bitta fuqaro. Ko'rinadigan yuruvchi figura (CitizenUnit) emas — bu simulyatsiya
    /// ma'lumot modeli: kasb, ish-joy, ehtiyojlar, baxt. Katta shaharda ming-minglab shunday
    /// obyekt bo'lishi mumkin (aggregated), ekranda esa faqat bir qismi animatsiya bilan chiziladi.
    /// </summary>
    public sealed class Citizen
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; } = 20;

        public int HouseholdId { get; set; }
        public int? HomeBuildingId { get; set; }
        public int? WorkplaceBuildingId { get; set; }

        public CitizenProfession Profession { get; set; } = CitizenProfession.None;
        public CitizenState CurrentState { get; set; } = CitizenState.AtHome;

        public float Happiness { get; set; } = 70;
        public float Health { get; set; } = 100;
        public float Education { get; set; } = 0;

        public float Hunger { get; set; } = 0;
        public float Energy { get; set; } = 100;

        public float PersonalMoney { get; set; } = 10;

        public bool IsEmployed { get; set; }
    }
}
