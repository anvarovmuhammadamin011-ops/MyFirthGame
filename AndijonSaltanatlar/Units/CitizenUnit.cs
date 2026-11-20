using System.Collections.Generic;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.Units
{
    public enum CitizenActivityIcon { None, Visiting, Talking, Trading, Complaining, Revolting, Sitting, Eating, Sleeping }

    /// <summary>
    /// Shahar fuqarosi: belgilangan yo'nalish bo'ylab (A* yo'li bilan) yuradi.
    /// Qurolsiz, tinch aholi — shaharni "jonli" ko'rsatadi. PatrolUnit xatti-harakatini meros oladi.
    ///
    /// PROMPT 17: ko'rinadigan figura PopulationManager'dagi HAQIQIY fuqaroga bog'lanadi (CitizenId).
    /// Ism, kasb, oila va ehtiyojlar o'sha fuqarodan olinadi — bu yerda nusxasi saqlanmaydi.
    /// </summary>
    public sealed class CitizenUnit : PatrolUnit
    {
        private static readonly string[] Names =
        {
            "Fuqaro", "Savdogar", "Hunarmand", "Dehqon", "Xaridor", "Chopon",
            "Konchi", "Baliqchi", "Omborchi", "Soliqchi", "Bozor egasi", "Olim",
            "Tabib", "Me'mor", "Quruvchi", "Ustoz", "Amaldor", "Karvonchi"
        };
        private static readonly string[] Descs =
        {
            "Shahar aholisi. Kundalik yumushlari bilan band.",
            "Ipak va matolar savdogari. Bozor sari ketmoqda.",
            "Kulol-hunarmand. Ustaxonasiga qaytmoqda.",
            "Dehqon. Dalalardan shaharga kelmoqda.",
            "Bozor xaridori. Narx-navo bilan qiziqadi.",
            "Chopon. Shahar chetidagi yaylovni aylanib yuradi.",
            "Konchi. Tog' konidan tosh va ma'dan qazib oladi.",
            "Baliqchi. Ariq va daryodan baliq tutadi.",
            "Omborchi. Don, mato va qurol zaxirasini hisoblaydi.",
            "Soliq yig'uvchi. Xazinaga tushumni daftarga yozadi.",
            "Bozor egasi. Rastalar va do'konlar tartibini boshqaradi.",
            "Olim. Madrasada falakiyot va riyoziyot bilan shug'ullanadi.",
            "Tabib. Bemorlarni dorivor o'tlar bilan davolaydi.",
            "Me'mor. Yangi bino va gumbazlarni loyihalaydi.",
            "Quruvchi. G'isht teradi, devor va uylar quradi.",
            "Ustoz. Madrasada shogirdlarga ta'lim beradi.",
            "Amaldor. Saroy devonida davlat ishlarini yuritadi.",
            "Karvonchi. Karvonsaroydan yuk va tuyalarni boshqaradi."
        };
        private static readonly string[] RoleLabels =
        {
            "ODDIY ODAM", "SAVDOGAR", "HUNARMAND", "DEHQON", "XARIDOR", "CHOPON",
            "KONCHI", "BALIQCHI", "OMBORCHI", "SOLIQCHI", "BOZOR EGASI", "OLIM",
            "TABIB", "ME'MOR", "QURUVCHI", "USTOZ", "AMALDOR", "KARVONCHI"
        };

        public int Kind { get; }
        public string RoleLabel => RoleLabels[Kind];
        public string SpeechText { get; private set; } = "";
        private float _speechTimer;
        private int _lastWorkDay = -1;
        public bool IsInsideBuilding { get; set; }
        public CitizenActivityIcon ActivityIcon { get; private set; }
        /// <summary>Bog'langan haqiqiy fuqaro (PopulationManager). 0 = bog'lanmagan (faqat bezak).</summary>
        public int CitizenId { get; private set; }
        public bool IsBound => CitizenId > 0;

        public CitizenUnit(IEnumerable<Vector2> route, int kind, float speed = 0.8f) : base(route, speed)
        {
            int k = ((kind % CitizenArt.Kinds) + CitizenArt.Kinds) % CitizenArt.Kinds;
            Kind = k;
            Name = Names[k];
            Description = Descs[k];
            SpriteKey = SpriteKeys.UnitCitizen + k;
            SetHitPoints(30);
            PauseTime = 0.8f + (float)Rng.NextDouble() * 1.6f;
            GaitScale = 0.7f; // oddiy fuqaro — eng sekin, bemalol yuradi
            CanRank = false;  // fuqaro — harbiy unvon olmaydi
        }

        /// <summary>Figurani haqiqiy fuqaroga bog'laydi va ko'rsatiladigan ism/tavsifni yangilaydi.</summary>
        public void BindCitizen(int citizenId, string displayName, string description)
        {
            CitizenId = citizenId;
            if (!string.IsNullOrEmpty(displayName)) Name = displayName;
            if (!string.IsNullOrEmpty(description)) Description = description;
        }

        public void Say(string text, float duration = 3f)
        {
            SpeechText = text ?? "";
            _speechTimer = duration;
        }

        public void SetActivity(CitizenActivityIcon icon) => ActivityIcon = icon;

        public void UpdateSpeech(float dt)
        {
            if (_speechTimer <= 0f) return;
            _speechTimer -= dt;
            if (_speechTimer <= 0f) SpeechText = "";
        }

        public bool TryBeginWork(int day)
        {
            if (_lastWorkDay == day) return false;
            _lastWorkDay = day;
            return true;
        }
    }
}
