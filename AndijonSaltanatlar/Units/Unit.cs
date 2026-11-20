using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.Units
{
    public enum UnitState
    {
        Idle,     // bo'sh turibdi
        Walking,  // patrul nuqtasiga yuryapti
        Waiting,  // patrul nuqtasida to'xtab turibdi
        Guarding  // postda qo'riqlayapti
    }

    /// <summary>
    /// Tile o'qlari bo'yicha yo'nalish.
    /// Ekranda: North = o'ng-yuqori, East = o'ng-past, South = chap-past, West = chap-yuqori.
    /// </summary>
    public enum Direction { North, East, South, West }

    /// <summary>Unit qaysi tomonda: shahar himoyachilari/aholisi yoki bosqinchilar.</summary>
    public enum UnitTeam { Player, Enemy }

    /// <summary>
    /// Barcha jangchilar uchun asos klass. Position — uzluksiz tile koordinatasi (tile markazi = +0.5).
    /// Prompt 16 (Transaction 11, 13, 15): frame-based animatsiya, to'g'ri yo'nalish,
    /// spring-based cheklov bilan yo'nalish yangilanishi va chekloviy "robotga o'xshab yurish"
    /// muammosini bartaraf etish.
    /// </summary>
    public abstract class Unit : IWorldObject
    {
        protected static readonly Random Rng = new Random(GameConfig.WorldSeed);

        // --- Animatsiya holati ---
        private float _walkDist;
        private Vector2 _animLastPos = new Vector2(float.NaN, float.NaN);
        /// <summary>Oxirgi haqiqiy siljishdan beri o'tgan vaqt (soniya).</summary>
        private float _sinceStep = 1f;
        /// <summary>Shuncha vaqt siljimasa — "yurish" holatida ham turish kadri (oyoq qadam o'rtasida qotmaydi).</summary>
        private const float StepGrace = 0.15f;

        /// <summary>Yurish sikli ichidagi joy (1.0 = to'liq sikl, ikki qadam). Oyoq kadrlari va tana ko'tarilishi shundan olinadi.</summary>
        public float WalkPhase => _walkDist / MathF.Max(0.1f, WalkCycleTiles) + PerEntityPhaseOffset / (MathF.PI * 2f);

        /// <summary>Unit hozir haqiqatan qadam tashlayapti (yurish holatida va oxirgi lahzada siljigan).</summary>
        public bool IsStepping => State == UnitState.Walking && _sinceStep < StepGrace;

        /// <summary>
        /// To'liq yurish sikli (ikki qadam) necha tile masofada — kadrlar bosib o'tilgan yo'lga bog'langan,
        /// shuning uchun oyoq yerda sirpanmaydi va qadam tezligi yurish tezligiga mos keladi.
        /// </summary>
        public float WalkCycleTiles { get; set; } = 0.85f;

        /// <summary>Ish kadrlari (masalan ketmon chopish: ko'tarish, tushirish, urish, qaytish); orqa tomoni ham.</summary>
        public Texture2D[] WorkFrames { get; set; }
        public Texture2D[] BackWorkFrames { get; set; }
        /// <summary>Unit joyida ishlayapti (dehqon yer chopyapti) — ish kadrlari aylanadi.</summary>
        public bool Working { get; set; }
        /// <summary>
        /// Kasbga xos ish harakati (Population.WorkStyle raqami; 0 — yo'q). Ish kadrlari bo'lmagan kasblar uchun renderer
        /// tana pozasini shu bo'yicha qimirlatadi: bolg'a urish, qazish, yozish, savdo, baliq tutish, egilish.
        /// </summary>
        public int WorkStyle { get; set; }
        /// <summary>Ish harakati vaqti (soniya) — poza ritmi uchun.</summary>
        public float WorkTime { get; set; }

        // --- Hayot pozalari (ish, bayram, buyruq, mag'lubiyat) ---
        public const int ActionNone = 0, ActionCelebrate = 1, ActionSalute = 2, ActionDefeat = 3;
        /// <summary>Hozirgi hayot harakati (ActionCelebrate/Salute/Defeat) va qolgan vaqti.</summary>
        public int LifeAction { get; private set; }
        public float LifeActionTime { get; private set; }
        private float _lifeActionTotal;

        /// <summary>Qisqa harakatni boshlaydi: bayram (sakrash), buyruq olish (ta'zim), mag'lubiyat (bosh egib turish).</summary>
        public void PlayAction(int action, float seconds)
        {
            LifeAction = action;
            LifeActionTime = _lifeActionTotal = MathF.Max(0.1f, seconds);
        }

        /// <summary>Ko'rinish (0..1): binoga kirishda asta yo'qoladi, chiqishda asta paydo bo'ladi.</summary>
        public float Presence { get; set; } = 1f;

        /// <summary>Haqiqiy harakat tezligi (tile/s, silliqlangan) — yugurish pozasi uchun.</summary>
        public float MoveSpeed { get; private set; }
        /// <summary>Yugurayapti (tez yurish): tana oldinga engashadi, qadam kattaroq.</summary>
        public bool IsRunning => IsStepping && MoveSpeed > 2.1f;

        /// <summary>
        /// Hayot pozasi: kasbga xos ish harakati (to'xtab turganda) va bayram/buyruq/mag'lubiyat. Zarba paytida — yo'q
        /// (zarba pozasi ustun). Renderer zarba pozasiga qo'shadi: siljish, og'ish va vertikal masshtab.
        /// </summary>
        public void GetLifePose(out Vector2 offset, out float rotation, out float scaleY)
        {
            offset = Vector2.Zero; rotation = 0f; scaleY = 1f;
            if (AttackTimer > 0f || IsDown) return;
            float side = Flipped ? -1f : 1f;
            if (LifeAction != ActionNone && LifeActionTime > 0f)
            {
                float u = 1f - LifeActionTime / _lifeActionTotal;   // 0..1
                switch (LifeAction)
                {
                    case ActionCelebrate:   // sakrab-sakrab xursandchilik
                        offset = new Vector2(0, -MathF.Abs(MathF.Sin(u * _lifeActionTotal * 7f + PerEntityPhaseOffset)) * 5f);
                        rotation = 0.06f * MathF.Sin(u * _lifeActionTotal * 9f) * side;
                        break;
                    case ActionSalute:      // buyruqni qabul qilish: qisqa ta'zim
                    {
                        float k = MathF.Sin(MathF.Min(1f, u * 1.4f) * MathF.PI);
                        rotation = 0.16f * k * side;
                        offset = new Vector2(0, 1.5f * k);
                        break;
                    }
                    case ActionDefeat:      // mag'lubiyat: bosh egilgan, cho'kkan
                        rotation = 0.2f * side;
                        scaleY = 0.88f;
                        offset = new Vector2(0, 2.5f);
                        break;
                }
                offset = new Vector2(MathF.Round(offset.X), MathF.Round(offset.Y));
                return;
            }
            if (IsRunning)
            {
                rotation = 0.07f * side;   // yugurishda oldinga engashadi
                return;
            }
            if (WorkStyle == 0 || IsStepping) return;
            float t = WorkTime + PerEntityPhaseOffset;
            switch (WorkStyle)
            {
                case 1: // bolg'a: ko'tarib, keskin urish
                {
                    float p = t / 0.7f % 1f;
                    if (p < 0.6f) { float k = p / 0.6f; offset = new Vector2(0, -1.2f * k); rotation = -0.06f * k * side; }
                    else { float k = (p - 0.6f) / 0.4f; offset = FacingWorld * 2f * (1f - k) + new Vector2(0, 1f); rotation = 0.1f * (1f - k) * side; scaleY = 0.96f; }
                    break;
                }
                case 2: // qazish: chuqur egilib cho'kich urish
                {
                    float p = t / 1.1f % 1f;
                    float k = p < 0.55f ? p / 0.55f : 1f - (p - 0.55f) / 0.45f;
                    rotation = 0.2f * k * side;
                    scaleY = 1f - 0.1f * k;
                    offset = new Vector2(0, 2.5f * k);
                    break;
                }
                case 3: // yozish/o'qish: sekin bosh qimirlatish
                    rotation = 0.035f * MathF.Sin(t * 2.6f) * side + 0.04f * side;
                    scaleY = 1f - 0.015f * (1f + MathF.Sin(t * 2.6f));
                    break;
                case 4: // savdo: gaplashib, yonga chayqalish
                    rotation = 0.05f * MathF.Sin(t * 3.9f);
                    offset = new Vector2(MathF.Sin(t * 3.9f) * 1f, 0);
                    break;
                case 5: // baliq: suvga engashgan, ba'zan qarmoqni tortadi
                {
                    float p = t / 3.2f % 1f;
                    rotation = (p > 0.85f ? -0.06f : 0.12f) * side;
                    offset = new Vector2(0, p > 0.85f ? -1.5f : 0.5f);
                    break;
                }
                case 6: // egilish (o't terish, hayvon parvarishi)
                    rotation = 0.12f * side;
                    scaleY = 0.9f + 0.03f * MathF.Sin(t * 2f);
                    offset = new Vector2(0, 2f);
                    break;
            }
            offset = new Vector2(MathF.Round(offset.X), MathF.Round(offset.Y));
        }
        /// <summary>Yelkada ko'tarib yurgan narsa rasmi kaliti (masalan "carry_sack" — hosil qopi). null — hech narsa.</summary>
        public string CarryKey { get; set; }
        /// <summary>Sug'oryapti — ekin ustiga suv tomchilari tushadi.</summary>
        public bool Watering { get; set; }

        /// <summary>Bir ish harakati (ketmonni ko'tarib urish) davomiyligi, soniya.</summary>
        public float WorkCycle { get; set; } = 0.95f;
        private float _workTime;
        private float _moveNoise;

        // yo'nalish cheklovlari
        /// <summary>Burilish gisterezisi (radian, ~15°): yangi burchak joriy sektor chegarasidan shuncha o'tmaguncha
        /// yo'nalish o'zgarmaydi — jangda chegara atrofida chap-o'ng "lip-lip" titrash bo'lmaydi.</summary>
        private const float FacingHysteresis = 0.26f;
        private const float DirectionSpringK = 18f;     // yo'nalish spring kuchi
        private const float DirectionDamping = 5f;       // yo'nalish dumpingsi
        private float _facingAngle;                     // tekislikdagi yo'nalish burchagi
        private float _facingVelocity;                  // burchak tezligi

        public int Id { get; set; }
        public string Name { get; protected set; }
        public string Description { get; protected set; } = "";
        public Vector2 Position { get; set; }
        public Direction Direction { get; protected set; } = Direction.South;
        /// <summary>Tezlik: soniyasiga necha tile.</summary>
        public float Speed { get; protected set; }
        /// <summary>Tezlik koeffitsienti (kun-tun ritmi: tunda fuqarolar sekinlashadi).</summary>
        public float SpeedScale { get; set; } = 1f;
        /// <summary>Yurish qadami (tur bo'yicha): fuqaro 0.7, askar 0.85, shoh 0.8. Animatsiya masofaga
        /// bog'liq bo'lgani uchun bu tezlikni kamaytirsa ham odam sirpanmasdan sekinroq yuradi.</summary>
        public float GaitScale { get; set; } = 1f;

        // --- Harbiy unvon (faqat askarlar) ---
        /// <summary>Bu birlik unvon ola oladimi (askarlar — ha, fuqarolar — yo'q).</summary>
        public bool CanRank { get; set; }
        public int Experience { get; private set; }
        /// <summary>Joriy unvon (0 — Oddiy askar ... 5 — Sarkarda).</summary>
        public int Rank { get; private set; }
        /// <summary>Unvon endigina oshdi — bildirishnoma uchun (Game1 o'qib, tozalaydi).</summary>
        public bool RankUpPending { get; set; }

        /// <summary>XP qo'shadi; chegaradan oshsa unvon ko'tariladi (kazarma darajasi maxRank bilan cheklaydi).</summary>
        public void AddExperience(int xp, int maxRank)
        {
            if (!CanRank || xp <= 0) return;
            Experience += xp;
            int nr = AndijonSaltanatlar.Military.MilitaryRank.RankForXp(Experience, maxRank);
            if (nr > Rank) { ApplyRankBonus(Rank, nr); Rank = nr; RankUpPending = true; }
        }

        /// <summary>Saqlashdan tiklash: XP va unvonni bonuslari bilan qayta o'rnatadi.</summary>
        public void RestoreRank(int xp, int rank)
        {
            if (!CanRank) return;
            Experience = xp < 0 ? 0 : xp;
            int target = rank < 0 ? 0 : (rank > AndijonSaltanatlar.Military.MilitaryRank.Max ? AndijonSaltanatlar.Military.MilitaryRank.Max : rank);
            if (target > Rank) { ApplyRankBonus(Rank, target); Rank = target; }
        }

        private void ApplyRankBonus(int from, int to)
        {
            var (atk, hp, spd) = AndijonSaltanatlar.Military.MilitaryRank.BonusDelta(from, to);
            Attack += atk;
            MaxHitPoints += hp;
            HitPoints = MaxHitPoints;
            Speed += spd;
        }
        public UnitState State { get; protected set; } = UnitState.Idle;
        /// <summary>Hozir ko'rsatilayotgan kadr.</summary>
        public Texture2D Texture { get; private set; }
        /// <summary>Kadrlar: 0 — turish, 1..2 — yurish.</summary>
        public Texture2D[] Frames { get; private set; }
        public List<Vector2> PatrolPoints { get; } = new List<Vector2>();
        public string SpriteKey { get; protected set; }
        public int HitPoints { get; set; }
        public int MaxHitPoints { get; protected set; }
        public int Attack { get; protected set; }

        // --- Jang / boshqaruv (Combat tizimi) ---
        public UnitTeam Team { get; set; } = UnitTeam.Player;

        /// <summary>Chizish masshtabi (X — tana kengligi, Y — bo'y). Shoh qiyofasi sozlanganda o'zgaradi.</summary>
        public Vector2 BodyScale { get; set; } = Vector2.One;
        /// <summary>Unit yiqilgan (HP tugagan). Himoyachilar o'lmaydi — bosqin tugagach o'rnidan turadi.</summary>
        public bool IsDown => HitPoints <= 0;
        /// <summary>
        /// true bo'lsa unitning o'z xatti-harakati (patrul, post) to'xtaydi — uni tashqi tizim boshqaradi:
        /// o'yinchi (ichiga kirish) yoki jang AI'si. Harakat DriveMove/DriveIdle orqali beriladi.
        /// </summary>
        public bool ExternallyControlled { get; set; }
        /// <summary>Unit uyga yashiringan (bosqin paytida qochgan fuqaro) — chizilmaydi va tanlanmaydi.</summary>
        public bool Hidden { get; set; }
        /// <summary>Hujum masofasi (tile). Nayzalilar uzunroq uradi.</summary>
        public float AttackRange { get; protected set; } = 0.95f;

        /// <summary>Zirh (0..60): jangda kelgan zarbaning shuncha foizini yutadi (og'ir zirhli, qalqonchi, saroy qo'riqchisi).</summary>
        public int Armor { get; set; }

        /// <summary>Qo'shin katalogidagi turi (sahnaga qo'shindan chiqarilgan bo'lsa) — asir/yo'qotish hisobida ishlatiladi.</summary>
        public AndijonSaltanatlar.Military.UnitType? LineType { get; set; }

        /// <summary>Kamonchi: uzoqdan o'q otadi (qamalda "Kamonchilar" qismi).</summary>
        public bool IsArcher { get; private set; }

        /// <summary>Jangchini kamonchiga aylantiradi: masofa uzoq, zarba kuchsizroq.</summary>
        public void MakeArcher(float reach = 4.5f)
        {
            IsArcher = true;
            AttackRange = reach;
            Attack = System.Math.Max(4, Attack * 3 / 4);
        }
        /// <summary>Zarba animatsiyasi (oldinga intilish) qolgan vaqti.</summary>
        public float AttackTimer { get; set; }
        /// <summary>Jarohat (qizarish) animatsiyasi qolgan vaqti.</summary>
        public float HurtTimer { get; set; }
        /// <summary>Keyingi zarbagacha qolgan vaqt.</summary>
        public float AttackCooldown { get; set; }

        // --- Zarba va jarohat animatsiyasi ---
        /// <summary>Zarba kadrlari: [tur * 3 + bosqich] (kesish/chopish/sanchish × ko'tarish/urish/davomi). null — oddiy kadr.</summary>
        public Texture2D[] AttackFrames { get; set; }
        /// <summary>Zarba tekkan lahzadagi oq chaqnash (soniya).</summary>
        public float FlashTimer { get; set; }
        public const float FlashTime = 0.08f;
        /// <summary>Zarbadan chayqalish (orqaga og'ib, qaytish).</summary>
        public float HitReactTimer { get; set; }
        public const float HitReactTime = 0.28f;
        /// <summary>Zarba kelgan yo'nalish (world, birlik vektor) — unit shu tomonga chayqaladi.</summary>
        public Vector2 HitDirWorld { get; set; }
        /// <summary>Orqaga uloqtirish tezligi (tile/s). Jang tizimi har kadr qo'llaydi va so'ndiradi.</summary>
        public Vector2 KnockVelocity { get; set; }
        /// <summary>Yiqilganiga qancha bo'ldi (silliq yiqilish animatsiyasi uchun).</summary>
        public float DownTime { get; private set; }
        /// <summary>Ko'rinish (1 — to'liq). Yiqilgan bosqinchi yo'qolishdan oldin asta so'nadi.</summary>
        public float FadeAlpha { get; set; } = 1f;

        /// <summary>Zarba yeganda: chaqnash, chayqalish yo'nalishi va qizarish.</summary>
        public void TakeHitVisual(Vector2 fromWorldDir)
        {
            FlashTimer = FlashTime;
            HitReactTimer = HitReactTime;
            HurtTimer = 0.3f;
            if (fromWorldDir.LengthSquared() > 0.0001f) { fromWorldDir.Normalize(); HitDirWorld = fromWorldDir; }
        }

        /// <summary>Chayqalish holati: tez orqaga og'ish va sekin qaytish.</summary>
        public void GetHitPose(out Vector2 offset, out float rotation)
        {
            offset = Vector2.Zero; rotation = 0f;
            if (HitReactTimer <= 0f) return;
            float t = 1f - HitReactTimer / HitReactTime;
            float k = t < 0.25f ? t / 0.25f : 1f - (t - 0.25f) / 0.75f;
            k = k * k * (3f - 2f * k);
            offset = HitDirWorld * 6f * k + new Vector2(0, -1.5f * k);
            offset = new Vector2(MathF.Round(offset.X), MathF.Round(offset.Y));
            rotation = 0.24f * k * (HitDirWorld.X >= 0f ? 1f : -1f);
        }

        /// <summary>Yiqilish burchagi (0..90°): tezlashib qulaydi va yerga tushganda biroz sakraydi.</summary>
        public float FallAngle
        {
            get
            {
                float t = Math.Clamp(DownTime / 0.45f, 0f, 1f);
                float e = t * t * (2.2f - 1.2f * t);
                float bounce = t > 0.75f ? MathF.Sin((t - 0.75f) / 0.25f * MathF.PI) * 0.13f : 0f;
                return MathHelper.PiOver2 * Math.Clamp(e - bounce, 0f, 1f);
            }
        }
        private float _driveSpeed;

        /// <summary>Tashqi boshqaruvda bir qadam: yo'nalishni darhol burish va yurish animatsiyasi.</summary>
        public void DriveMove(Vector2 tileDelta, float speed)
        {
            SetFacing(tileDelta);
            _driveSpeed = speed;
            State = UnitState.Walking;
        }

        /// <summary>Tashqi boshqaruvda joyida turish.</summary>
        public void DriveIdle()
        {
            _driveSpeed = 0f;
            State = UnitState.Idle;
        }

        /// <summary>
        /// Yo'nalishni berilgan tomonga buradi. Gisterezis bilan: burchak ikki yo'nalish chegarasi atrofida tebransa
        /// (jangda nishonga qarash, itarilish) sprayt har kadr chap-o'ng ag'darilmaydi. Keskin burilish (90°+) darhol.
        /// </summary>
        public void SetFacing(Vector2 tileDelta)
        {
            if (tileDelta.LengthSquared() < 0.0001f) return;
            float angle = MathF.Atan2(tileDelta.Y, tileDelta.X);
            Direction next = AngleToDirection(angle);
            if (next == Direction) return;
            // Joriy sektor markazidan og'ish: chegaradan (45°) kamida FacingHysteresis o'tgandagina buriladi
            float off = MathF.Abs(WrapAngle(angle - DirectionToAngle(Direction)));
            if (off < MathF.PI / 4f + FacingHysteresis) return;
            Direction = next;
        }

        /// <summary>Hp'ni to'liq yoki qisman tiklash (bosqindan keyin).</summary>
        public void Revive(float fraction)
        {
            HitPoints = Math.Max(1, (int)(MaxHitPoints * Math.Clamp(fraction, 0.05f, 1f)));
            HurtTimer = 0f;
        }

        // --- Zarba turlari ---
        public const int AttackSlash = 0;     // KESISH: yonlama qilich silkinishi
        public const int AttackOverhead = 1;  // CHOPISH: qilichni ko'tarib, tepadan urish
        public const int AttackThrust = 2;    // SANCHISH: oldinga keskin hamla
        public const int AttackSpin = 3;      // AYLANMA: atrofga aylanib urish
        /// <summary>Bitta zarba animatsiyasining davomiyligi (soniya).</summary>
        public const float SwingTime = 0.3f;

        /// <summary>Hozirgi (oxirgi) zarba turi — animatsiya va qilich izi shunga qarab chiziladi.</summary>
        public int AttackKind { get; set; }

        /// <summary>Unit qarab turgan tomonning world (ekran) yo'nalishi, birlik vektor.</summary>
        public Vector2 FacingWorld
        {
            get
            {
                Vector2 d = Direction switch
                {
                    Direction.North => new Vector2(0, -1),
                    Direction.East => new Vector2(1, 0),
                    Direction.South => new Vector2(0, 1),
                    _ => new Vector2(-1, 0)
                };
                Vector2 w = IsoMath.TileToWorld(d);
                if (w.LengthSquared() > 0f) w.Normalize();
                return w;
            }
        }

        /// <summary>Unit qarab turgan tomon (tile yo'nalishi).</summary>
        public Vector2 FacingTile => Direction switch
        {
            Direction.North => new Vector2(0, -1),
            Direction.East => new Vector2(1, 0),
            Direction.South => new Vector2(0, 1),
            _ => new Vector2(-1, 0)
        };

        /// <summary>Zarba jarayoni 0..1 (0 — boshlanishi, 1 — tugashi).</summary>
        public float AttackProgress => AttackTimer <= 0f ? 1f : 1f - Math.Clamp(AttackTimer / SwingTime, 0f, 1f);

        /// <summary>
        /// Zarba paytidagi tana holati: siljish (world piksel), og'ish burchagi, vertikal masshtab va
        /// aylanma zarbada sprite'ni teskari aylantirish. Har bir zarba turi o'z harakatiga ega.
        /// </summary>
        public void GetAttackPose(out Vector2 offset, out float rotation, out float scaleY, out bool spinFlip)
        {
            offset = Vector2.Zero; rotation = 0f; scaleY = 1f; spinFlip = false;
            if (AttackTimer <= 0f) return;
            float p = AttackProgress;
            float side = Flipped ? -1f : 1f;      // oldinga og'ish soat mili (o'ngga qarasa) yoki teskari
            Vector2 f = FacingWorld;
            switch (AttackKind)
            {
                case AttackOverhead:
                    if (p < 0.4f)
                    {   // qilichni ko'tarish: orqaga og'ib, cho'zilib
                        float k = p / 0.4f;
                        offset = new Vector2(0, -4f * k) - f * 2f * k;
                        rotation = -0.1f * k * side;
                        scaleY = 1f + 0.05f * k;
                    }
                    else
                    {   // tepadan urish: oldinga engashib, yerga bosilib
                        float k = (p - 0.4f) / 0.6f;
                        float hit = MathF.Sin(MathF.Min(1f, k * 1.6f) * MathF.PI * 0.5f);
                        offset = f * 7f * (1f - k * 0.6f) + new Vector2(0, 2f * hit);
                        rotation = (0.14f * (1f - k) + 0.03f) * side;
                        scaleY = 1f - 0.06f * (1f - k);
                    }
                    break;
                case AttackThrust:
                {   // oldinga keskin hamla, keyin orqaga qaytish
                    float k = p < 0.35f ? p / 0.35f : 1f - (p - 0.35f) / 0.65f;
                    offset = f * 11f * k;
                    rotation = 0.06f * k * side;
                    scaleY = 1f - 0.05f * k;
                    break;
                }
                case AttackSpin:
                    // aylanib urish: kichik sakrash va sprite chap-o'ngga aylanadi
                    offset = new Vector2(0, -4f * MathF.Sin(p * MathF.PI));
                    spinFlip = ((int)(p * 6f)) % 2 == 1;
                    rotation = 0.1f * MathF.Sin(p * MathF.PI * 6f);
                    break;
                default:
                {   // KESISH: yonlama silkinish — orqaga burilib, oldinga qarsillatib
                    float k = MathF.Sin(p * MathF.PI);
                    offset = f * 5f * k;
                    rotation = MathHelper.Lerp(-0.08f, 0.12f, p) * side;
                    break;
                }
            }
            offset = new Vector2(MathF.Round(offset.X), MathF.Round(offset.Y));
        }

        /// <summary> Placeholder yoki real per-entity animatsiya renderlovchi konteksti.
        /// Hozircha cheklovli cheklovlarni boshqarish uchun fase g'oya qo'llaniladi. </summary>
        public float PerEntityPhaseOffset { get; } = (float)(Rng.NextDouble() * MathF.PI * 2);
        public float MoveNoise { get => _moveNoise; set => _moveNoise = Math.Clamp(value, -1f, 1f); }

        // --- IWorldObject ---
        public float Depth => Position.X + Position.Y;
        public int SortLayer => 1;
        public bool Flipped => Direction == Direction.South || Direction == Direction.West;
        public Vector2 DrawPosition
        {
            get
            {
                Vector2 w = IsoMath.TileToWorld(Position);
                float k = AssetManager.ScaleOf(Texture);
                return new Vector2(MathF.Round(w.X - Texture.Width * k / 2f), MathF.Round(w.Y - Texture.Height * k + UnitArt.FootOffset));
            }
        }

        protected Unit(Vector2 position)
        {
            Position = position;
        }

        protected void SetHitPoints(int hp)
        {
            MaxHitPoints = hp;
            HitPoints = hp;
        }

        /// <summary>Xos navkar (VIP) — eng kuchli jangchi.</summary>
        public bool IsElite { get; private set; }

        /// <summary>Jangchini xos navkarga aylantiradi: oltin sovutli ko'rinish, eng yuqori kuch va mustahkamlik.</summary>
        public void MakeElite(Texture2D[] frames, Texture2D[] attackFrames)
        {
            if (frames == null || frames.Length == 0 || frames[0] == null) return;
            SpriteKey = SpriteKeys.UnitElite;
            SetFrames(frames);
            AttackFrames = attackFrames;
            Name = "Xos navkar";
            Description = "Saltanatning eng kuchli, oltin sovutli xos navkari.";
            Attack = Math.Max(Attack, 32);
            float hp = MaxHitPoints > 0 ? HitPoints / (float)MaxHitPoints : 1f;
            MaxHitPoints = Math.Max(MaxHitPoints, 360);
            HitPoints = Math.Max(1, (int)(MaxHitPoints * Math.Max(hp, 0.05f)));
            if (HitPoints <= 0) HitPoints = MaxHitPoints;
            IsElite = true;
        }

        /// <summary>Orqa tomon kadrlari (yuqoriga — shimol/g'arbga yurganda ko'rinadi). null — old kadrlar ishlatiladi.</summary>
        public Texture2D[] BackFrames { get; set; }

        /// <summary>Unit kameradan uzoqqa (ekranda yuqoriga) qarab turibdi.</summary>
        public bool FacingAway => Direction == Direction.North || Direction == Direction.West;

        /// <summary>Joriy yo'nalishga mos kadrlar to'plami.</summary>
        private Texture2D[] Dir => FacingAway && BackFrames != null && BackFrames.Length == Frames.Length ? BackFrames : Frames;

        /// <summary>
        /// Ko'rinishni (sprayt oilasini) belgilaydi — UnitManager.Add dan OLDIN chaqiriladi. Yangi kasb va birlik turlari
        /// (masalan "unit_heavy", "unit_citizen9") shu orqali o'z kiyimida chiziladi.
        /// </summary>
        public void SetLook(string spriteKey)
        {
            if (!string.IsNullOrEmpty(spriteKey)) SpriteKey = spriteKey;
        }

        public void SetFrames(Texture2D[] frames)
        {
            Frames = frames;
            Texture = frames[0];
        }

        /// <summary>
        /// PROMPT 17: suhbat paytida unit joyida turadi (Idle kadri). Suhbat tugagach false qilinadi va
        /// unit kundalik yo'nalishini davom ettiradi.
        /// </summary>
        public bool Hold { get; set; }

        public void Update(float dt)
        {
            if (AttackTimer > 0f) AttackTimer = MathF.Max(0f, AttackTimer - dt);
            if (HurtTimer > 0f) HurtTimer = MathF.Max(0f, HurtTimer - dt);
            if (AttackCooldown > 0f) AttackCooldown = MathF.Max(0f, AttackCooldown - dt);
            if (FlashTimer > 0f) FlashTimer = MathF.Max(0f, FlashTimer - dt);
            if (HitReactTimer > 0f) HitReactTimer = MathF.Max(0f, HitReactTimer - dt);
            if (IsDown) DownTime += dt; else { DownTime = 0f; FadeAlpha = 1f; }
            if (WorkStyle != 0) WorkTime += dt;
            if (LifeActionTime > 0f) { LifeActionTime = MathF.Max(0f, LifeActionTime - dt); if (LifeActionTime <= 0f) LifeAction = ActionNone; }

            // 1) Xatti-harakatni yangilash (suhbatda — joyida turadi; yiqilgan — qimirlamaydi;
            //    tashqi boshqaruvda — harakatni Combat tizimi beradi)
            if (IsDown) State = UnitState.Idle;
            else if (ExternallyControlled) { /* DriveMove / DriveIdle orqali */ }
            else if (Hold) State = UnitState.Idle;
            else UpdateBehavior(dt);

            // 2) Yo'nalishni spring-based usulda yangilash (keskin burilishning oldini oladi)
            UpdateFacing(dt);

            // 3) Animatsiya kadrini yangilash (turish/yurish), zarba paytida — zarba kadri
            UpdateAnimation(dt);
            ApplyAttackFrame();
        }

        /// <summary>
        /// Zarba paytida haqiqiy zarba kadri: ko'tarish (0..30%), urish (30..62%), davomi (62..100%).
        /// Aylanma zarbada kesish kadrlari aylana bo'ylab almashinadi.
        /// </summary>
        private void ApplyAttackFrame()
        {
            if (AttackTimer <= 0f || IsDown || AttackFrames == null) return;
            // Zarba kadrlari faqat old tomondan chizilgan: orqasi o'girilgan jangchi kameraga birdan o'girilib qolmasin —
            // orqa kadr qoladi, zarba harakatini GetAttackPose (engashish, hamla, og'ish) beradi.
            if (FacingAway && BackFrames != null) return;
            float p = AttackProgress;
            int kind, phase;
            if (AttackKind == AttackSpin) { kind = AttackSlash; phase = ((int)(p * 7f)) % 3; }
            else
            {
                kind = Math.Clamp(AttackKind, 0, 2);
                phase = p < 0.3f ? 0 : p < 0.62f ? 1 : 2;
            }
            Texture2D t = AttackFrames[kind * 3 + phase];
            if (t != null) Texture = t;
        }

        /// <summary>Har bir unit turi o'z xatti-harakatini shu yerda belgilaydi.</summary>
        protected abstract void UpdateBehavior(float dt);

        /// <summary>Yo'nalishni spring-based usulda yangilash — keskin burilishning oldini oladi.</summary>
        private void UpdateFacing(float dt)
        {
            // Hozirgi yo'nalishni burchakka o'girish
            float targetAngle = DirectionToAngle(Direction);
            float diff = WrapAngle(targetAngle - _facingAngle);
            _facingVelocity += diff * DirectionSpringK * dt;
            _facingVelocity *= (1f - DirectionDamping * dt);
            _facingAngle += _facingVelocity * dt;

            // Cheklov: chekloviy tebranishlarni oldini olish
            _facingAngle = WrapAngle(_facingAngle);
            _facingVelocity = Math.Clamp(_facingVelocity, -3f, 3f);
        }

        /// <summary>Burchakni [-PI..PI] chegarasiga olish.</summary>
        private static float WrapAngle(float a)
        {
            while (a > MathF.PI) a -= 2f * MathF.PI;
            while (a < -MathF.PI) a += 2f * MathF.PI;
            return a;
        }

        /// <summary>Yo'nalishni burchakka o'girish.</summary>
        private static float DirectionToAngle(Direction d)
        {
            return d switch
            {
                Direction.North => -MathF.PI / 2,   // o'ng-yuqori
                Direction.East => 0,                // o'ng-past
                Direction.South => MathF.PI / 2,     // chap-past
                Direction.West => MathF.PI,          // chap-yuqori
                _ => 0
            };
        }

        /// <summary>Holat va bosib o'tilgan yo'lga mos kadrni tanlash: turish, yurish (masofaga bog'liq), ish.</summary>
        protected void UpdateAnimation(float dt)
        {
            // Bosib o'tilgan masofa (teleport yoki qayta tug'ilish sakrashlari hisobga olinmaydi) — kadrlar
            // bo'lmasa ham kuzatiladi (WalkPhase/IsStepping renderer va testlarda ishlatiladi)
            float moved = float.IsNaN(_animLastPos.X) ? 0f : Vector2.Distance(Position, _animLastPos);
            _animLastPos = Position;
            if (moved < 1f) _walkDist += moved;
            if (moved > 0.0005f && moved < 1f) _sinceStep = 0f;
            else _sinceStep += dt;
            if (dt > 0f && moved < 1f) MoveSpeed += (moved / dt - MoveSpeed) * MathF.Min(1f, dt * 8f);   // silliqlangan tezlik

            if (Frames == null || Frames.Length == 0)
            {
                Texture = null;
                return;
            }

            Texture2D[] fr = Dir;
            Texture = fr[0]; // turish

            // Ish: dehqon yer chopyapti (joyida turgan paytda ish kadrlari aylanadi)
            if (Working && WorkFrames != null && WorkFrames.Length > 0 && State != UnitState.Walking)
            {
                _workTime += dt;
                Texture2D[] wf = FacingAway && BackWorkFrames != null && BackWorkFrames.Length == WorkFrames.Length ? BackWorkFrames : WorkFrames;
                int wi = (int)(_workTime / MathF.Max(0.2f, WorkCycle) * wf.Length + PerEntityPhaseOffset) % wf.Length;
                Texture = wf[wi];
                return;
            }

            // Yurish: kadr = sikl ichidagi joy (masofa / sikl uzunligi) — oyoq yerda sirpanmaydi. 3D modeldan
            // 8 kadrli silliq sikl. "Yurish" holatida, lekin joyida tursa (yo'l to'silgan, tiqilib qolgan) —
            // oyoq qadam o'rtasida qotib qolmasin: turish kadri ko'rsatiladi.
            if (State == UnitState.Walking && Frames.Length > 1 && _sinceStep < StepGrace)
            {
                int walkFrames = Frames.Length - 1;
                int idx = 1 + (int)(WalkPhase * walkFrames) % walkFrames;
                Texture = fr[idx];
            }
        }

        /// <summary>Yo'nalishni tabiiy ravishda yangilash (FaceTowards — spring-based).</summary>
        public void FaceTowards(Vector2 delta, float dt)
        {
            if (delta.LengthSquared() < 0.0001f) return;

            // Hozirgi yo'nalishni spring-based usulda yangilash (keskin burilishning oldini oladi)
            float targetAngle = MathF.Atan2(delta.Y, delta.X);
            // Izometrik tuzilishga moslashtirish
            float angleDelta = WrapAngle(targetAngle - _facingAngle);
            _facingVelocity += angleDelta * DirectionSpringK * dt;
            _facingVelocity *= (1f - DirectionDamping * dt);

            _facingAngle += _facingVelocity * dt;
            float wrapped = WrapAngle(_facingAngle);
            _facingVelocity = Math.Clamp(_facingVelocity, -3f, 3f);

            // Yangi yo'nalishni aniqlash
            Direction = AngleToDirection(wrapped);
        }

        /// <summary>Burchakdan yo'nalishga o'girish.</summary>
        private static Direction AngleToDirection(float angle)
        {
            // Izometrik tuzilish: North = o'ng-yuqori, East = o'ng-past, South = chap-past, West = chap-yuqori.
            // Burchaklar: -PI/2 (N), 0 (E), PI/2 (S), PI (W)
            float a = WrapAngle(angle);
            if (a < -MathF.PI / 4) return Direction.North;
            if (a < MathF.PI / 4) return Direction.East;
            if (a < 3 * MathF.PI / 4) return Direction.South;
            return Direction.West;
        }

        public static string StateName(UnitState s)
        {
            switch (s)
            {
                case UnitState.Walking: return "Patrulda yuribdi";
                case UnitState.Waiting: return "Atrofni kuzatmoqda";
                case UnitState.Guarding: return "Postda qo‘riqlamoqda";
                default: return "Dam olmoqda";
            }
        }
    }
}
