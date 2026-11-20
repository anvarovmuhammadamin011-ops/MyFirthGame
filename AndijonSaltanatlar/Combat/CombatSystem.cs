using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Units;
using AndijonSaltanatlar.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AndijonSaltanatlar.Combat
{
    /// <summary>
    /// Jang tizimi: "ICHIGA KIRISH" (istalgan jangchi yoki fuqaroni o'yinchi o'zi boshqaradi) va
    /// "BOSQIN" (shaharga tashqaridan dushman hujumi, himoyachilarning avtomatik jangi).
    ///
    /// Asosiy qoidalar:
    ///  - Unit boshqarilayotganda (o'yinchi yoki AI) uning o'z xatti-harakati (patrul/post) to'xtaydi
    ///    va jang tugagach o'z joyiga yo'l topib (A*) qaytadi — patrul yo'nalishlari buzilmaydi.
    ///  - Shahar himoyachilari va aholi o'lmaydi: HP tugasa yiqiladi, bosqin tugagach o'rnidan turadi.
    ///    (Fuqaro figuralari PopulationManager'dagi haqiqiy fuqarolarga bog'langan — ro'yxatdan o'chirilmaydi.)
    ///  - Bosqinchilar vaqtinchalik: yengilganda dunyodan olib tashlanadi, Save'ga yozilmaydi.
    ///  - Jang real vaqtda kechadi (o'yin tezligi x2/x4 ga bog'liq emas), pauzada to'xtaydi.
    /// </summary>
    public sealed class CombatSystem
    {
        // --- Sozlamalar ---
        private const float PlayerWalkSpeed = 2.5f;     // tile/s
        private const float PlayerSprintSpeed = 3.7f;
        private const float PlayerAttackCooldown = 0.42f;
        private const float PlayerDamageMultiplier = 1.6f;
        private const float DefenderSpeed = 1.8f;
        private const float DisengageRange = 15f;       // bosqindan tashqari (masalan, o'yinchi bosqinchi ichida) jang masofasi
        private const float RaiderAggro = 6f;
        private const float RaiderPossessedAggro = 9f;
        private static readonly Vector2 CityCenter = new Vector2(31.5f, 31.5f);

        private readonly MapManager _map;
        private readonly UnitManager _units;
        private readonly GameStateManager _state;
        private readonly Random _rng = new Random();
        public Action<CitizenUnit> OnCitizenWorkRequested { get; set; }

        private enum AgentMode { Possessed, Fighting, Raiding, Returning, Escort, Fleeing, Hiding }

        /// <summary>Fuqarolar qochib yashirinadigan uylarni topish uchun (Game1 o'rnatadi).</summary>
        public AndijonSaltanatlar.Buildings.BuildingManager Buildings { get; set; }

        private const float CitizenPanicRange = 10f;   // dushman shu masofada ko'rinsa fuqaro qochadi
        private const float FleeSpeed = 2.7f;
        private float _panicThink;

        private sealed class Agent
        {
            public Unit Unit;
            public AgentMode Mode;
            public Vector2 Home;
            public Unit Target;
            public float ThinkTimer;
            public float LootTimer;
            public List<Point> Path;
            public int PathIndex;
            public Point PathGoal;
            public float RepathTimer;
            public Vector2 EscortOffset;
            public Vector2 FleeTo;
        }

        // ==================================================================
        // SHOH SHAHARDA (saroydan chiqish, soqchilar hamrohligi)
        // ==================================================================

        private ShahUnit _shah;
        /// <summary>Soliq g'alayoni davom etyapti — shohga ko'proq qo'riqchi hamroh bo'ladi.</summary>
        public bool UnrestHigh { get; set; }

        /// <summary>Hozir shohga hamroh bo'lib yurgan qo'riqchilar soni.</summary>
        public int EscortCount
        {
            get
            {
                int n = 0;
                foreach (var kv in _agents) if (kv.Value.Mode == AgentMode.Escort && !kv.Key.IsDown) n++;
                return n;
            }
        }

        /// <summary>Shoh hozir shaharda (saroydan tashqarida) yuribdimi.</summary>
        public bool ShahInWorld => _shah != null;

        /// <summary>
        /// Shohni saroy eshigi oldida dunyoga chiqaradi va o'yinchi uni boshqaradi.
        /// Saroy yaqinidagi ikki soqchi shohga hamroh bo'ladi.
        /// </summary>
        public ShahUnit SpawnShahAndPossess(Vector2 palaceDoor)
        {
            if (_shah == null)
            {
                _shah = _units.Add(new ShahUnit(palaceDoor));
                _agents[_shah] = new Agent { Unit = _shah, Mode = AgentMode.Possessed, Home = palaceDoor };

                // Eng yaqin ikki soqchi (avval nayzalilar) — hamrohlar
                var guards = new List<Unit>();
                foreach (Unit u in _units.Units)
                    if (IsSoldier(u) && !u.IsDown && u != Possessed && Vector2.Distance(u.Position, palaceDoor) < 30f) guards.Add(u);
                guards.Sort((a, b) =>
                {
                    int pa = a is SpearmanUnit ? 0 : 1, pb = b is SpearmanUnit ? 0 : 1;
                    return pa != pb ? pa.CompareTo(pb) : Vector2.Distance(a.Position, palaceDoor).CompareTo(Vector2.Distance(b.Position, palaceDoor));
                });
                // Qo'riqchilar shoh atrofida halqa bo'lib yuradi: odatda 4 ta, g'alayonda 8 ta
                int escorts = UnrestHigh ? 8 : 4;
                var offsets = new Vector2[escorts];
                for (int k = 0; k < escorts; k++)
                {
                    float ang = k / (float)escorts * MathF.PI * 2f + 0.4f;
                    float rad = k % 2 == 0 ? 1.0f : 1.35f;
                    offsets[k] = new Vector2(MathF.Cos(ang) * rad, MathF.Sin(ang) * rad);
                }
                for (int i = 0; i < guards.Count && i < escorts; i++)
                {
                    Unit g = guards[i];
                    if (!_agents.TryGetValue(g, out Agent ga))
                    {
                        ga = new Agent { Unit = g, Home = g.Position, Mode = AgentMode.Escort };
                        _agents[g] = ga;
                    }
                    g.ExternallyControlled = true;
                    ga.Mode = AgentMode.Escort;
                    ga.Target = _shah;
                    ga.EscortOffset = offsets[i];
                    ga.Path = null;
                }
            }
            TryPossess(_shah);
            _state.ShowMessage("Shoh " + ShahUnit.ShahName + " hazratlari shahar sayriga chiqdilar! Soqchilar hamroh.");
            return _shah;
        }

        private void UpdateEscort(Agent a, float dt)
        {
            Unit lord = a.Target;
            if (lord == null || lord.IsDown || !_units.Units.Contains(lord)) { BeginReturn(a); return; }
            Vector2 goal = lord.Position + a.EscortOffset;
            if (!Walk(goal)) goal = lord.Position;
            float dist = Vector2.Distance(a.Unit.Position, goal);
            float speed = dist > 3f ? 4.0f : dist > 1.2f ? 3.0f : 1.6f; // orqada qolsa yuguradi
            if (MoveAgent(a, goal, speed, dt, 0.35f)) a.Unit.SetFacing(lord.FacingTile);
        }

        private readonly Dictionary<Unit, Agent> _agents = new Dictionary<Unit, Agent>();
        private readonly List<RaiderUnit> _raiders = new List<RaiderUnit>();
        private readonly Dictionary<Unit, float> _corpseTimers = new Dictionary<Unit, float>();
        private readonly Dictionary<Unit, float> _reviveTimers = new Dictionary<Unit, float>();

        private struct FloatText { public Vector2 World; public string Text; public Color Color; public float T; public bool Big; public float Vx; }
        private readonly List<FloatText> _floats = new List<FloatText>();

        // --- Zarba hissi: kechiktirilgan zarar, zarrachalar, to'xtash (hit-stop), silkinish ---
        /// <summary>Zarar qurol tekkan lahzada beriladi (silkinish boshlanganda emas).</summary>
        private struct PendingHit { public Unit A, V; public int Dmg, Kind; public float Delay, Knock, Reach; public bool Heavy; }
        private readonly List<PendingHit> _pending = new List<PendingHit>();

        private const byte PSpark = 0, PDust = 1, PRing = 2;
        private struct Particle { public Vector2 World, Vel; public float Life, Max, Size; public Color Color; public byte Kind; }
        private readonly List<Particle> _particles = new List<Particle>();

        private CameraManager _cam;
        private float _hitStop;

        /// <summary>Zarba tekkan lahzadagi qisqa to'xtash (o'yin "og'irlik" hissi): unitlar shu koeffitsient bilan yangilanadi.</summary>
        public float TimeScale => _hitStop > 0f ? 0.06f : 1f;

        // --- Bosqin holati ---
        public bool RaidActive { get; private set; }
        public bool RaidIncoming => _warnTimer > 0f;
        private float _warnTimer;
        private int _raidNumber;
        private int _raidTotal;
        private int _lootStolen;
        private int _gateIndex;
        private string _resultText;
        private bool _resultVictory;
        private float _resultTimer;
        private float _defenderThink;

        private static readonly string[] GateNames = { "Shimoliy", "Janubiy", "G'arbiy", "Sharqiy" };

        private bool _justPossessed;

        /// <summary>O'yinchi hozir boshqarayotgan unit (yoki null).</summary>
        public Unit Possessed { get; private set; }

        /// <summary>Telefon (sensorli ekran): ko'rsatmalar ekrandagi tugmalarga mos, boshqaruv paneli ixcham va tepada.</summary>
        public bool TouchUi { get; set; }

        // --- QAMAL (dushman shahriga hujum) ---
        /// <summary>Qamal rejimi: o'yinchi qo'shini (Player) dushman shahrida; dushman askarlari (Enemy) himoyalanadi.</summary>
        public bool SiegeMode { get; set; }
        /// <summary>Hujumchilar dushman yo'q paytda shu nuqtaga (shoh / saroy eshigi) yuradi.</summary>
        public Vector2 SiegeObjective { get; set; }
        private float _siegeThink;
        /// <summary>Qamal: himoyachilar hushyor (birinchi qism hujumga yuborilgach). Saf tortish paytida jim turadi.</summary>
        public bool SiegeAlert { get; set; }
        private struct Arrow { public Vector2 From, To; public float T; }
        private readonly List<Arrow> _arrows = new List<Arrow>();
        private const float SiegeAttackerAggro = 9f;
        private const float SiegeDefenderAggro = 9f;

        /// <summary>Qamal: unitni hujumchi qo'shin safiga qo'shadi (AI saroy tomon yuradi, yo'lda dushman bilan jang qiladi).</summary>
        public void EnlistAttacker(Unit u)
        {
            if (u == null) return;
            if (!_agents.TryGetValue(u, out Agent a))
            {
                a = new Agent { Unit = u, Home = u.Position };
                _agents[u] = a;
            }
            u.ExternallyControlled = true;
            a.Mode = u == Possessed ? AgentMode.Possessed : AgentMode.Fighting;
            a.Target = null;
            a.Path = null;
            a.EscortOffset = new Vector2(Rand(-1.8f, 1.8f), Rand(-1.8f, 1.8f));
            a.ThinkTimer = Rand(0f, 0.4f);
        }

        /// <summary>Qamal: jangchi saroy ichiga kirdi — tashqaridagi AI va boshqaruvdan olinadi, ko'rinmaydi.</summary>
        public void Withdraw(Unit u)
        {
            if (u == null) return;
            if (Possessed == u) Possessed = null;
            _agents.Remove(u);
            u.ExternallyControlled = true;
            u.Hidden = true;
            u.DriveIdle();
            if (_state.Selected == u) _state.Select(null);
        }

        /// <summary>Qamal: dushman askari hujumchini yaqin masofada sezsa jangga kiradi (aks holda o'z postida/patrulida).</summary>
        private void UpdateSiegeAwareness(float dt)
        {
            if (!SiegeAlert) return;
            _siegeThink -= dt;
            if (_siegeThink > 0f) return;
            _siegeThink = 0.35f;
            foreach (Unit u in _units.Units)
            {
                if (u.Team != UnitTeam.Enemy || u is CitizenUnit || u.IsDown || u == Possessed) continue;
                if (_agents.TryGetValue(u, out Agent ex) && ex.Mode == AgentMode.Fighting) continue;
                Unit foe = NearestEnemy(u, u is ShahUnit ? 5f : SiegeDefenderAggro);
                if (foe == null) continue;
                if (ex == null)
                {
                    ex = new Agent { Unit = u, Home = u.Position };
                    _agents[u] = ex;
                }
                u.ExternallyControlled = true;
                ex.Mode = AgentMode.Fighting;
                ex.Target = foe;
                ex.Path = null;
            }
        }

        private void UpdateSiegeAttacker(Agent a, float dt)
        {
            Unit u = a.Unit;
            a.ThinkTimer -= dt;
            if (a.Target == null || a.Target.IsDown || a.ThinkTimer <= 0f)
            {
                a.ThinkTimer = 0.45f;
                a.Target = NearestEnemy(u, SiegeAttackerAggro);
            }
            float speed = u is CavalryUnit ? 2.7f : DefenderSpeed;
            if (a.Target == null && u.IsArcher) a.Target = NearestEnemy(u, u.AttackRange + 2.5f);
            if (a.Target != null) { ChaseAndStrike(a, a.Target, speed, dt); return; }
            Vector2 goal = SiegeObjective + a.EscortOffset;
            if (!Walk(goal)) goal = SiegeObjective;
            MoveAgent(a, goal, speed, dt, 0.5f);
        }

        public CombatSystem(MapManager map, UnitManager units, GameStateManager state)
        {
            _map = map;
            _units = units;
            _state = state;
        }

        // ==================================================================
        // ICHIGA KIRISH
        // ==================================================================

        public bool TryPossess(Unit u)
        {
            if (u == null)
            {
                _state.ShowMessage("Avval jangchi yoki fuqaroni tanlang (ustiga bosing), keyin F", true);
                return false;
            }
            if (u.IsDown)
            {
                _state.ShowMessage(u.Name + " yiqilgan — boshqasini tanlang", true);
                return false;
            }
            if (Possessed != null) Release(false);

            Possessed = u;
            _justPossessed = true; // shu kadrdagi F/Q/E bosilishi darhol chiqarib yubormasin
            u.ExternallyControlled = true;
            u.Hidden = false;
            u.DriveIdle();
            if (_agents.TryGetValue(u, out Agent a)) a.Mode = AgentMode.Possessed; // uy joyi saqlanadi
            else _agents[u] = new Agent { Unit = u, Mode = AgentMode.Possessed, Home = u.Position };
            _state.Select(u);
            string controls = TouchUi
                ? (u is CitizenUnit ? "Joystik — yurish, BOSHQA — ish, CHIQ — chiqish" : "Joystik — yurish, ZARBA — urish, CHIQ — chiqish")
                : u is CitizenUnit
                ? "WASD — yurish, E — ish, F — chiqish"
                : "WASD — yurish, Space/sichqoncha — zarba, F — chiqish";
            _state.ShowMessage("Siz endi: " + u.Name + ".  " + controls);
            return true;
        }

        /// <summary>Boshqaruvni AI'ga qaytaradi: bosqin paytida jangga, aks holda o'z joyiga.</summary>
        public void Release(bool announce = true)
        {
            Unit u = Possessed;
            if (u == null) return;
            Possessed = null;
            if (_agents.TryGetValue(u, out Agent a))
            {
                if (u is RaiderUnit) a.Mode = AgentMode.Raiding;
                else if ((RaidActive || SiegeMode) && IsSoldier(u)) { a.Mode = AgentMode.Fighting; a.Target = null; }
                else BeginReturn(a);
            }
            u.DriveIdle();
            if (u is ShahUnit) _state.ShowMessage("Shoh hazratlari saroyga qaytmoqdalar...");
            else if (announce) _state.ShowMessage("Kuzatuv rejimi. Jangchini tanlab F bosing — yana ichiga kirasiz");
        }

        private void CyclePossession(int dir)
        {
            if (Possessed == null) return;
            var list = new List<Unit>();
            foreach (Unit u in _units.Units)
            {
                if (u.IsDown || u.Team != Possessed.Team) continue;
                if (u != Possessed && u is CitizenUnit) continue; // almashtirishda faqat jangchilar
                list.Add(u);
            }
            if (list.Count <= 1) { _state.ShowMessage("Boshqa jangchi yo'q", true); return; }
            int i = list.IndexOf(Possessed);
            int next = ((i + dir) % list.Count + list.Count) % list.Count;
            TryPossess(list[next]);
        }

        private void UpdatePossessed(float dt, InputManager input, CameraManager camera)
        {
            Unit p = Possessed;
            if (p == null) return;
            if (p.IsDown)
            {
                _state.ShowMessage(p.Name + " yiqildi! Boshqa jangchini tanlab F bosing", true);
                Release(false);
                return;
            }
            if (input == null || _justPossessed) { _justPossessed = false; p.DriveIdle(); return; }

            if (input.KeyPressed(Keys.F) || input.KeyPressed(Keys.Escape)) { Release(); return; }
            if (input.KeyPressed(Keys.E))
            {
                if (p is CitizenUnit citizen) OnCitizenWorkRequested?.Invoke(citizen);
                else CyclePossession(1);
                return;
            }
            if (input.KeyPressed(Keys.Q)) { CyclePossession(-1); return; }

            // Ekran yo'nalishidagi harakat (W — ekranda yuqoriga) -> tile yo'nalishi
            Vector2 screen = Vector2.Zero;
            if (input.KeyDown(Keys.A) || input.KeyDown(Keys.Left)) screen.X -= 1;
            if (input.KeyDown(Keys.D) || input.KeyDown(Keys.Right)) screen.X += 1;
            if (input.KeyDown(Keys.W) || input.KeyDown(Keys.Up)) screen.Y -= 1;
            if (input.KeyDown(Keys.S) || input.KeyDown(Keys.Down)) screen.Y += 1;

            if (screen != Vector2.Zero && dt > 0f)
            {
                screen.Normalize();
                Vector2 tileDir = IsoMath.WorldToTileF(screen * 32f);
                tileDir.Normalize();
                float speed = input.KeyDown(Keys.LeftShift) || input.KeyDown(Keys.RightShift) ? PlayerSprintSpeed : PlayerWalkSpeed;
                p.DriveMove(tileDir, speed);
                MoveWithCollision(p, tileDir * speed * dt);
            }
            else p.DriveIdle();

            if (_comboWindow > 0f) _comboWindow -= dt;

            bool mouseAttack = input.LeftPressed && !input.MouseOverUI;
            bool attack = input.KeyPressed(Keys.Space) || mouseAttack;
            bool spin = input.KeyPressed(Keys.R) || (input.RightClicked && !input.MouseOverUI);
            if ((attack || spin) && p.AttackCooldown <= 0f && dt > 0f)
            {
                if ((mouseAttack || input.RightClicked) && camera != null)
                {
                    // Sichqoncha tomonga qarab uradi
                    Vector2 aim = IsoMath.WorldToTileF(camera.ScreenToWorld(input.MouseVector)) - p.Position;
                    p.SetFacing(aim);
                }
                int kind;
                if (spin) kind = Unit.AttackSpin;
                else
                {
                    // Kombo: tez-tez bosilsa KESISH -> CHOPISH -> SANCHISH navbatma-navbat
                    _comboStep = _comboWindow > 0f ? (_comboStep + 1) % 3 : 0;
                    kind = _comboStep;
                }
                PlayerSwing(p, kind);
                _comboWindow = 0.85f;
            }
        }

        private int _comboStep;
        private float _comboWindow;

        private static readonly string[] AttackNames = { "KESISH", "CHOPISH", "SANCHISH!", "AYLANMA!" };

        private void PlayerSwing(Unit p, int kind)
        {
            p.AttackKind = kind;
            p.AttackTimer = Unit.SwingTime;
            // Har bir zarba: [kutish, zarar koeffitsienti, qo'shimcha masofa, nishonlar soni, orqaga uloqtirish]
            float cooldown, mult, extra, knock;
            int maxTargets;
            switch (kind)
            {
                case Unit.AttackOverhead: cooldown = 0.42f; mult = 1.3f; extra = 0.35f; maxTargets = 2; knock = 0.2f; break;
                case Unit.AttackThrust: cooldown = 0.5f; mult = 1.7f; extra = 0.8f; maxTargets = 1; knock = 0.5f; break;
                case Unit.AttackSpin: cooldown = 1.0f; mult = 0.95f; extra = 0.55f; maxTargets = 8; knock = 0.35f; break;
                default: cooldown = PlayerAttackCooldown * 0.8f; mult = 1f; extra = 0.35f; maxTargets = 2; knock = 0.08f; break;
            }
            p.AttackCooldown = cooldown;
            if (kind >= Unit.AttackOverhead)
                AddFloat(p.Position + new Vector2(-0.2f, -0.2f), AttackNames[kind], new Color(255, 214, 90));

            int baseDmg = Math.Max(4, p.Attack);
            float reach = p.AttackRange + extra;
            Vector2 facing = p.FacingTile;
            facing.Normalize();

            // Nishonlar: aylanmada — atrofdagi hammasi, boshqalarida — oldidagi eng yaqinlari
            var targets = new List<(Unit u, float d)>();
            foreach (Unit u in _units.Units)
            {
                if (u.Team == p.Team || u.IsDown || u.Hidden) continue;
                if (SiegeMode && u is CitizenUnit) continue; // tinch aholiga tegilmaydi
                Vector2 to = u.Position - p.Position;
                float d = to.Length();
                if (d > reach) continue;
                if (kind != Unit.AttackSpin && d > 0.3f && Vector2.Dot(to / d, facing) < -0.1f) continue; // orqadagilarga tegmaydi
                targets.Add((u, d));
            }
            targets.Sort((a, b) => a.d.CompareTo(b.d));
            for (int i = 0; i < targets.Count && i < maxTargets; i++)
            {
                Unit t = targets[i].u;
                // Ob-havo jangga ta'sir qiladi (bo'ron/qor zarbani pasaytiradi)
                int dmg = (int)(baseDmg * PlayerDamageMultiplier * mult * _state.Weather.BattleModifier()) + _rng.Next(0, 5);
                ScheduleHit(p, t, dmg, kind, knock, reach, i);
            }
        }

        // ==================================================================
        // BOSQIN
        // ==================================================================

        /// <summary>
        /// Bosqinni boshlaydi (ogohlantirish bilan). manual=true — B tugmasi. attacker — hujum qilgan AI davlat nomi
        /// (serversiz dunyoda AI qo'shini Asakaga yetib kelganda); null — oddiy bosqinchilar.
        /// </summary>
        public void TriggerRaid(bool manual, string attacker = null)
        {
            if (RaidActive || RaidIncoming)
            {
                if (manual) _state.ShowMessage("Bosqin allaqachon davom etmoqda!", true);
                return;
            }
            _gateIndex = _rng.Next(4);
            _warnTimer = manual ? 3f : 6f;
            string who = string.IsNullOrEmpty(attacker) ? "dushman" : attacker + " qo'shini";
            _state.ShowMessage("DIQQAT! " + GateNames[_gateIndex] + " darvoza tomondan " + who + " yaqinlashmoqda!", true);
            _state.ShowVizier(string.IsNullOrEmpty(attacker) ? "Dushman yaqinlashmoqda!" : "Sizga hujum boshlandi!",
                "Hazratim, " + GateNames[_gateIndex] + " darvoza tomondan " + who + " ko'rindi! Askarlarni himoyaga chorlang va xalqni uylarga kiriting.");
        }

        /// <summary>Sinov/ishga tushirish parametri uchun: ogohlantirishsiz darhol bosqin.</summary>
        public void StartRaidNow(int gate)
        {
            if (RaidActive) return;
            _gateIndex = ((gate % 4) + 4) % 4;
            _warnTimer = 0f;
            SpawnRaid();
        }

        /// <summary>Berilgan tile'ga eng yaqin (yiqilmagan) shahar jangchisi ichiga kiradi.</summary>
        public bool PossessNearestSoldier(Vector2 tile)
        {
            Unit best = null;
            float bestD = float.MaxValue;
            foreach (Unit u in _units.Units)
            {
                if (!IsSoldier(u) || u.IsDown) continue;
                float d = Vector2.Distance(u.Position, tile);
                if (d < bestD) { bestD = d; best = u; }
            }
            return TryPossess(best);
        }

        private void SpawnRaid()
        {
            _raidNumber++;
            int count = Math.Min(14, 4 + 2 * _raidNumber);
            _raidTotal = count;
            _lootStolen = 0;
            RaidActive = true;
            for (int i = 0; i < count; i++)
            {
                Vector2 pos = SpawnPosition(_gateIndex, i);
                bool chief = i == 0;
                var r = _units.Add(new RaiderUnit(pos, chief));
                _raiders.Add(r);
                _agents[r] = new Agent { Unit = r, Mode = AgentMode.Raiding, Home = pos, ThinkTimer = (float)_rng.NextDouble() * 0.4f };
            }
            _state.ShowMessage("BOSQIN! " + count + " ta dushman! Barcha askarlar himoyaga otlandi, xalq uylarga yashirinsin!", true);
            _defenderThink = 0f; // jangovar hayajon darhol
        }

        /// <summary>Bosqinchilar bosh ko'cha (31-32 qatorlar) bo'ylab darvoza tashqarisida saf tortadi.</summary>
        private static Vector2 SpawnPosition(int gate, int i)
        {
            float lane = i % 2;          // ko'chaning ikki qatori
            float row = (i / 2) * 0.9f;  // orqaga saf
            switch (gate)
            {
                case 0: return new Vector2(31.5f + lane, 2.5f + row);   // shimol
                case 1: return new Vector2(31.5f + lane, 61.5f - row);  // janub
                case 2: return new Vector2(2.5f + row, 31.5f + lane);   // g'arb
                default: return new Vector2(61.5f - row, 31.5f + lane); // sharq
            }
        }

        private void EndRaid(bool victory)
        {
            RaidActive = false;
            if (Possessed is RaiderUnit) Release(false);

            if (victory)
            {
                int reward = 30 + 12 * _raidTotal;
                _state.Coins += reward;
                _resultText = "G'ALABA! Bosqin qaytarildi.  +" + reward + " tanga";
            }
            else
            {
                int loss = _state.Coins / 5;
                _state.Coins -= loss;
                _resultText = "MAG'LUBIYAT... Bosqinchilar " + (loss + _lootStolen) + " tanga talab ketdi";
            }
            _resultVictory = victory;
            _resultTimer = 6f;
            _state.ShowMessage(_resultText, !victory);

            // Bosqinchilarni olib tashlash
            foreach (RaiderUnit r in _raiders) RemoveUnit(r);
            _raiders.Clear();
            _corpseTimers.Clear();

            // Himoyachilar o'rnidan turadi va o'z joyiga qaytadi
            foreach (Unit u in _units.Units)
                if (u.Team == UnitTeam.Player && u.IsDown) u.Revive(victory ? 0.6f : 0.35f);
            foreach (Agent a in _agents.Values)
            {
                if (a.Mode == AgentMode.Fleeing || a.Mode == AgentMode.Hiding)
                {
                    // Yashiringan xalq uylardan chiqib, o'z ishiga qaytadi
                    a.Unit.Hidden = false;
                    if (victory && a.Unit is CitizenUnit cz && _rng.Next(3) == 0) cz.Say("Xayriyat! Dushman qaytarildi!", 3f);
                    BeginReturn(a);
                }
                else if (a.Mode == AgentMode.Fighting) BeginReturn(a);
            }
        }

        // ==================================================================
        // XALQ QOCHADI: dushmanni ko'rgan fuqaro eng yaqin xavfsiz uyga yugurib yashirinadi
        // ==================================================================

        private static readonly string[] PanicLines =
        {
            "Voy! Dushman! Qochinglar!", "Bosqinchilar! Uyga kiringlar!", "Yordam bering! Askarlar!", "Bolalarni olib qoching!"
        };

        private void UpdateCitizenPanic(float dt)
        {
            _panicThink -= dt;
            if (_panicThink > 0f) return;
            _panicThink = 0.4f;
            foreach (Unit u in _units.Units)
            {
                if (!(u is CitizenUnit c) || c.IsDown || c.Hidden || c == Possessed) continue;
                if (_agents.TryGetValue(c, out Agent existing) && existing.Mode != AgentMode.Returning) continue;
                if (existing == null && c.ExternallyControlled) continue; // boshqa tizim band qilgan (masalan suhbat)
                // Alarm chalinganda (bosqin yaqinlashganda yoki boshlanganda) — BARCHA fuqaro uyiga qochadi.
                // Aks holda faqat dushmanni yaqinda ko'rgan fuqaro qochadi.
                bool alarm = RaidActive || RaidIncoming || SiegeMode;
                if (!alarm && NearestEnemy(c, CitizenPanicRange) == null) continue;

                Agent a = existing ?? new Agent { Unit = c, Home = c.Position, Mode = AgentMode.Fleeing };
                _agents[c] = a;
                c.ExternallyControlled = true;
                a.Mode = AgentMode.Fleeing;
                a.Path = null;
                a.FleeTo = FindHideout(c.Position);
                c.Say(PanicLines[_rng.Next(PanicLines.Length)], 2.5f);
                AddFloat(c.Position, "!", new Color(255, 230, 120));
            }
        }

        /// <summary>Dushmanlardan uzoqroq, yaqin uy darvozasi. Topilmasa — dushmandan teskari tomonga.</summary>
        private Vector2 FindHideout(Vector2 from)
        {
            Vector2 best = Vector2.Zero;
            float bestScore = float.MaxValue;
            if (Buildings != null)
            {
                foreach (var b in Buildings.Buildings)
                {
                    if (b.Type != AndijonSaltanatlar.Buildings.BuildingType.House || b.BuildProgress < 1f) continue;
                    var door = new Vector2(b.Position.X + 1.45f, b.Position.Y + 2.5f);
                    if (!Walk(door)) continue;
                    float danger = float.MaxValue;
                    foreach (RaiderUnit r in _raiders) if (!r.IsDown) danger = MathF.Min(danger, Vector2.Distance(r.Position, door));
                    if (danger < 5f) continue; // dushman yonidagi uyga qochmaydi
                    float score = Vector2.Distance(from, door) - MathF.Min(danger, 14f) * 0.3f;
                    if (score < bestScore) { bestScore = score; best = door; }
                }
            }
            if (bestScore < float.MaxValue) return best;
            Unit near = null;
            foreach (RaiderUnit r in _raiders) if (!r.IsDown && (near == null || Vector2.Distance(r.Position, from) < Vector2.Distance(near.Position, from))) near = r;
            Vector2 away = near != null ? from - near.Position : new Vector2(1, 1);
            if (away.LengthSquared() < 0.01f) away = new Vector2(1, 0);
            away.Normalize();
            return Clamp(from + away * 8f);
        }

        private void UpdateFleeing(Agent a, float dt)
        {
            if (MoveAgent(a, a.FleeTo, FleeSpeed, dt, 0.35f))
            {
                a.Mode = AgentMode.Hiding;
                a.Unit.Hidden = true; // uyga kirib yashirindi
                a.Unit.DriveIdle();
                if (_state.Selected == a.Unit) _state.Select(null);
            }
        }

        // ==================================================================
        // UPDATE
        // ==================================================================

        /// <param name="dt">Real vaqt (pauzada 0).</param>
        /// <param name="input">Boshqaruv kiritishi (panel/dialog ochiq bo'lsa null).</param>
        public void Update(float dt, InputManager input, CameraManager camera)
        {
            _cam = camera;
            // Floating matnlar va natija banneri real vaqtda so'nadi
            for (int i = _floats.Count - 1; i >= 0; i--)
            {
                FloatText f = _floats[i];
                f.T += dt;
                if (f.T > 1f) _floats.RemoveAt(i); else _floats[i] = f;
            }
            if (_resultTimer > 0f) _resultTimer -= dt;

            // Zarba tekkan lahza: dunyo bir zumga deyarli to'xtaydi
            if (_hitStop > 0f && dt > 0f) { _hitStop -= dt; dt *= TimeScale; }
            UpdateParticles(dt);
            for (int i = _arrows.Count - 1; i >= 0; i--)
            {
                Arrow ar = _arrows[i];
                ar.T += dt / 0.28f;
                if (ar.T >= 1f) _arrows.RemoveAt(i); else _arrows[i] = ar;
            }

            UpdatePossessed(dt, input, camera);
            if (dt <= 0f) return;
            UpdatePendingHits(dt);
            UpdateKnockback(dt);

            // Avtomatik bosqinlar o'chirilgan (o'yinchi talabi) — bosqin faqat B tugmasi bilan chaqiriladi.
            if (RaidIncoming)
            {
                _warnTimer -= dt;
                if (_warnTimer <= 0f) { _warnTimer = 0f; SpawnRaid(); }
            }

            if (RaidActive)
            {
                UpdateDefenderAwareness(dt);
                UpdateCitizenPanic(dt);
            }
            else if (RaidIncoming)
            {
                UpdateCitizenPanic(dt); // alarm chalindi — xalq dushman kelishidan oldin uyiga qochadi
            }
            if (SiegeMode)
            {
                UpdateSiegeAwareness(dt);
                UpdateCitizenPanic(dt); // qamal: qo'ng'iroq chalindi, dushman shahri aholisi uylarga kiradi
            }

            // Agentlar (AI boshqaruvidagi unitlar)
            var snapshot = new List<Agent>(_agents.Values);
            foreach (Agent a in snapshot)
            {
                Unit u = a.Unit;
                if (u == Possessed) continue;
                if (u.IsDown) { u.DriveIdle(); continue; }
                switch (a.Mode)
                {
                    case AgentMode.Fighting: UpdateFighter(a, dt); break;
                    case AgentMode.Raiding: UpdateRaider(a, dt); break;
                    case AgentMode.Returning: UpdateReturning(a, dt); break;
                    case AgentMode.Escort: UpdateEscort(a, dt); break;
                    case AgentMode.Fleeing: UpdateFleeing(a, dt); break;
                    case AgentMode.Hiding: u.DriveIdle(); break;
                }
            }
            Separate();

            // Yiqilgan bosqinchilar biroz yotib, keyin yo'qoladi
            if (_corpseTimers.Count > 0)
            {
                var dead = new List<Unit>();
                foreach (var kv in _corpseTimers) if (kv.Value - dt <= 0f) dead.Add(kv.Key);
                foreach (Unit d in new List<Unit>(_corpseTimers.Keys)) { _corpseTimers[d] -= dt; d.FadeAlpha = Math.Clamp(_corpseTimers[d] / 0.8f, 0f, 1f); }
                foreach (Unit d in dead) { _corpseTimers.Remove(d); _raiders.Remove(d as RaiderUnit); RemoveUnit(d); }
            }

            // Bosqin natijasi
            if (RaidActive)
            {
                int aliveRaiders = 0;
                foreach (RaiderUnit r in _raiders) if (!r.IsDown) aliveRaiders++;
                int aliveDefenders = 0;
                foreach (Unit u in _units.Units) if (IsSoldier(u) && !u.IsDown) aliveDefenders++;
                if (aliveRaiders == 0) EndRaid(true);
                else if (aliveDefenders == 0) EndRaid(false);
            }
            else if (!SiegeMode)
            {
                // Bosqindan tashqari (masalan bosqinchi ichida bo'lganda) yiqilganlar tez tiklanadi
                foreach (Unit u in _units.Units)
                {
                    if (u.Team != UnitTeam.Player || !u.IsDown) continue;
                    _reviveTimers.TryGetValue(u, out float t);
                    t += dt;
                    if (t >= 4f) { u.Revive(0.5f); _reviveTimers.Remove(u); }
                    else _reviveTimers[u] = t;
                }
            }
        }

        /// <summary>Shahar jangchilari yaqin dushmanni sezsa jangga kiradi.</summary>
        private void UpdateDefenderAwareness(float dt)
        {
            _defenderThink -= dt;
            if (_defenderThink > 0f) return;
            _defenderThink = 0.35f;
            foreach (Unit u in _units.Units)
            {
                if (!IsSoldier(u) || u.IsDown || u == Possessed) continue;
                // Jangovar hayajon: bosqin paytida shahardagi BARCHA askarlar eng yaqin dushmanga yuguradi
                Unit enemy = NearestEnemy(u, float.MaxValue);
                if (enemy == null) continue;
                if (!_agents.TryGetValue(u, out Agent a))
                {
                    // Muhim: Mode aniq beriladi (enum standart qiymati Possessed — aks holda askar "o'yinchiniki" bo'lib qotib qoladi)
                    a = new Agent { Unit = u, Home = u.Position, Mode = AgentMode.Fighting };
                    _agents[u] = a;
                    u.ExternallyControlled = true;
                }
                // u != Possessed yuqorida tekshirilgan — hamrohlar ham, qaytayotganlar ham jangga kiradi
                a.Mode = AgentMode.Fighting;
                if (a.Target == null || a.Target.IsDown) a.Target = enemy;
            }
            if (RaidActive) SoundAlarm();
        }

        /// <summary>
        /// Jang signali: bosqin paytida har bir bosqinchiga eng yaqin ikki himoyachi (postdan uzoq bo'lsa ham) yuboriladi —
        /// aks holda shahar markazini talayotgan bosqinchilarga hech kim chiqmasdi.
        /// </summary>
        private void SoundAlarm()
        {
            var assigned = new Dictionary<Unit, int>();
            foreach (Agent ag in _agents.Values)
                if (ag.Mode == AgentMode.Fighting && ag.Target != null && !ag.Target.IsDown)
                    assigned[ag.Target] = assigned.GetValueOrDefault(ag.Target) + 1;
            foreach (RaiderUnit r in _raiders)
            {
                if (r.IsDown) continue;
                int have = assigned.GetValueOrDefault(r);
                while (have < 2)
                {
                    Unit best = null;
                    float bd = float.MaxValue;
                    foreach (Unit u in _units.Units)
                    {
                        if (!IsSoldier(u) || u.IsDown || u == Possessed) continue;
                        if (_agents.TryGetValue(u, out Agent ex) && ex.Mode != AgentMode.Returning) continue; // jangda yoki soqchilikda
                        float d = Vector2.DistanceSquared(u.Position, r.Position);
                        if (d < bd) { bd = d; best = u; }
                    }
                    if (best == null) return;
                    if (!_agents.TryGetValue(best, out Agent a))
                    {
                        a = new Agent { Unit = best, Home = best.Position };
                        _agents[best] = a;
                        best.ExternallyControlled = true;
                    }
                    a.Mode = AgentMode.Fighting;
                    a.Target = r;
                    a.Path = null;
                    have++;
                }
            }
        }

        private void UpdateFighter(Agent a, float dt)
        {
            Unit u = a.Unit;
            if (SiegeMode && u.Team == UnitTeam.Player) { UpdateSiegeAttacker(a, dt); return; }
            a.ThinkTimer -= dt;
            if (a.Target == null || a.Target.IsDown || a.ThinkTimer <= 0f)
            {
                a.ThinkTimer = 0.5f;
                // Bosqin paytida signal bilan kelgan himoyachi uzoqdagi dushmanni ham ta'qib qiladi
                a.Target = NearestEnemy(u, RaidActive ? 90f : SiegeMode ? 12f : DisengageRange);
            }
            if (a.Target == null) { BeginReturn(a); return; }
            ChaseAndStrike(a, a.Target, DefenderSpeed, dt);
        }

        private void UpdateRaider(Agent a, float dt)
        {
            Unit u = a.Unit;
            a.ThinkTimer -= dt;
            if (a.Target == null || a.Target.IsDown || a.ThinkTimer <= 0f)
            {
                a.ThinkTimer = 0.4f;
                Unit t = null;
                float best = RaiderAggro;
                foreach (Unit o in _units.Units)
                {
                    if (o.Team == UnitTeam.Enemy || o.IsDown) continue;
                    bool possessedTarget = o == Possessed;
                    if (!possessedTarget && !IsSoldier(o)) continue; // tinch aholiga tegmaydi
                    float range = possessedTarget ? RaiderPossessedAggro : RaiderAggro;
                    float d = Vector2.Distance(o.Position, u.Position);
                    if (d <= range && (t == null || d < best)) { best = d; t = o; }
                }
                a.Target = t;
            }

            float speed = ((RaiderUnit)u).MoveSpeed;
            if (a.Target != null) { ChaseAndStrike(a, a.Target, speed, dt); return; }

            // Shahar markaziga yurish, yetgach — talash
            if (MoveAgent(a, CityCenter, speed, dt, 2.2f))
            {
                a.LootTimer -= dt;
                if (a.LootTimer <= 0f)
                {
                    a.LootTimer = 1.2f;
                    int take = Math.Min(_state.Coins, ((RaiderUnit)u).IsChief ? 6 : 3);
                    if (take > 0)
                    {
                        _state.Coins -= take;
                        _lootStolen += take;
                        AddFloat(u.Position, "-" + take, new Color(255, 210, 90));
                    }
                }
            }
        }

        private void UpdateReturning(Agent a, float dt)
        {
            Unit u = a.Unit;
            if (MoveAgent(a, a.Home, u is ShahUnit ? 1.8f : 1.4f, dt, 0.06f))
            {
                if (u is ShahUnit && u.Team == UnitTeam.Player && !SiegeMode)
                {
                    // Shoh saroyga kirib, yana taxtga o'tiradi
                    RemoveUnit(u);
                    _shah = null;
                    _state.ShowMessage("Shoh hazratlari saroyga qaytdilar");
                    return;
                }
                u.Position = a.Home;
                u.ExternallyControlled = false;
                u.DriveIdle();
                _agents.Remove(u);
            }
        }

        private void BeginReturn(Agent a)
        {
            a.Mode = AgentMode.Returning;
            a.Target = null;
            a.Path = null;
        }

        /// <summary>Nishonga yaqinlashadi; masofaga kirsa to'xtab uradi.</summary>
        private void ChaseAndStrike(Agent a, Unit target, float speed, float dt)
        {
            Unit u = a.Unit;
            float dist = Vector2.Distance(u.Position, target.Position);
            if (dist <= u.AttackRange)
            {
                u.DriveIdle();
                u.SetFacing(target.Position - u.Position);
                if (u.AttackCooldown <= 0f)
                {
                    // AI ham har xil uradi: nayzali — sanchadi, sardor — tepadan chopadi, qolganlar aralash
                    u.AttackKind = u is SpearmanUnit ? Unit.AttackThrust
                        : u is RaiderUnit rc && rc.IsChief ? (_rng.Next(3) == 0 ? Unit.AttackSlash : Unit.AttackOverhead)
                        : _rng.Next(3);
                    u.AttackTimer = Unit.SwingTime;
                    u.AttackCooldown = 1.0f + (float)_rng.NextDouble() * 0.35f;
                    int dmg = Math.Max(2, u.Attack + _rng.Next(-2, 3));
                    if (u.IsArcher) _arrows.Add(new Arrow { From = u.Position, To = target.Position });
                    ScheduleHit(u, target, dmg, u.AttackKind, 0.12f, u.AttackRange, 0);
                }
                return;
            }
            MoveAgent(a, target.Position, speed, dt, u.AttackRange * 0.85f);
        }

        /// <summary>Zarbani rejalashtiradi: zarar qurol nishonga tekkan lahzada (zarba turiga qarab) beriladi.</summary>
        private void ScheduleHit(Unit a, Unit v, int dmg, int kind, float knock, float reach, int order)
        {
            float frac = kind switch
            {
                Unit.AttackOverhead => 0.5f,
                Unit.AttackThrust => 0.36f,
                Unit.AttackSpin => 0.28f + 0.07f * Math.Min(order, 6),
                _ => 0.42f
            };
            _pending.Add(new PendingHit
            {
                A = a, V = v, Dmg = dmg, Kind = kind, Delay = Unit.SwingTime * frac, Knock = knock, Reach = reach + 0.75f,
                Heavy = kind == Unit.AttackOverhead || kind == Unit.AttackThrust
            });
        }

        private void UpdatePendingHits(float dt)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingHit h = _pending[i];
                h.Delay -= dt;
                if (h.Delay > 0f) { _pending[i] = h; continue; }
                _pending.RemoveAt(i);
                if (h.A.IsDown || h.V.IsDown || !_units.Units.Contains(h.V)) continue;
                Vector2 away = h.V.Position - h.A.Position;
                if (away.Length() > h.Reach) continue; // nishon chetga chiqib ulgurdi — zarba havoga ketdi
                Hit(h.A, h.V, h.Dmg, h.Heavy);
                if (away.LengthSquared() > 0.0001f)
                {
                    away.Normalize();
                    float push = h.V.IsDown ? Math.Max(h.Knock, 0.25f) : h.Knock;
                    if (push > 0f) h.V.KnockVelocity += away * push * 9f;
                }
            }
        }

        /// <summary>Orqaga uloqtirish silliq: tezlik asta so'nadi (bir zumda "sakrab" ketmaydi).</summary>
        private void UpdateKnockback(float dt)
        {
            foreach (Unit u in _units.Units)
            {
                Vector2 v = u.KnockVelocity;
                if (v.LengthSquared() < 0.0004f) { if (v != Vector2.Zero) u.KnockVelocity = Vector2.Zero; continue; }
                MoveWithCollision(u, v * dt);
                u.KnockVelocity = v * MathF.Exp(-12f * dt);
            }
        }

        // ---------------------------------------------------------------- Zarrachalar (uchqun, chang, zarba halqasi)
        private float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private void Impact(Unit attacker, Unit victim, bool heavy, bool playerInvolved)
        {
            Vector2 feet = IsoMath.TileToWorld(victim.Position);
            Vector2 chest = feet + new Vector2(0, -21);
            Vector2 dir = feet - IsoMath.TileToWorld(attacker.Position);
            if (dir.LengthSquared() < 0.0001f) dir = new Vector2(attacker.Flipped ? -1 : 1, 0);
            dir.Normalize();
            float baseAng = MathF.Atan2(dir.Y, dir.X);
            int sparks = heavy ? 14 : 9;
            for (int i = 0; i < sparks; i++)
            {
                float ang = baseAng + Rand(-0.95f, 0.95f);
                float sp = Rand(70f, 170f) * (heavy ? 1.3f : 1f);
                float life = Rand(0.16f, 0.32f);
                _particles.Add(new Particle
                {
                    World = chest + new Vector2(Rand(-2f, 2f), Rand(-3f, 3f)),
                    Vel = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * sp + new Vector2(0, -40f),
                    Life = life, Max = life, Size = Rand(1f, 2f), Kind = PSpark,
                    Color = Color.Lerp(new Color(255, 248, 214), new Color(255, 176, 64), Rand(0f, 1f))
                });
            }
            for (int i = 0; i < (heavy ? 6 : 4); i++)
            {
                float life = Rand(0.35f, 0.55f);
                _particles.Add(new Particle
                {
                    World = feet + new Vector2(Rand(-6f, 6f), Rand(-2f, 2f)),
                    Vel = new Vector2(dir.X * Rand(10f, 30f) + Rand(-14f, 14f), Rand(-16f, -4f)),
                    Life = life, Max = life, Size = Rand(2.5f, 4.5f), Kind = PDust, Color = new Color(198, 172, 128)
                });
            }
            _particles.Add(new Particle { World = chest, Life = 0.2f, Max = 0.2f, Size = heavy ? 22f : 15f, Kind = PRing, Color = Color.White });
            if (victim.IsDown)
            {
                // Yiqilish changi
                for (int i = 0; i < 8; i++)
                {
                    float life = Rand(0.45f, 0.75f);
                    _particles.Add(new Particle
                    {
                        World = feet + new Vector2(Rand(-10f, 10f), Rand(-3f, 3f)),
                        Vel = new Vector2(Rand(-26f, 26f), Rand(-12f, -2f)),
                        Life = life, Max = life, Size = Rand(3f, 5.5f), Kind = PDust, Color = new Color(186, 160, 118)
                    });
                }
            }
            if (playerInvolved)
            {
                _hitStop = Math.Max(_hitStop, heavy || victim.IsDown ? 0.085f : 0.05f);
                _cam?.Shake(heavy || victim.IsDown ? 4.5f : 2.5f, heavy ? 0.22f : 0.14f);
            }
            if (_particles.Count > 600) _particles.RemoveRange(0, _particles.Count - 600);
        }

        private void UpdateParticles(float dt)
        {
            if (dt <= 0f) return;
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                Particle p = _particles[i];
                p.Life -= dt;
                if (p.Life <= 0f) { _particles.RemoveAt(i); continue; }
                p.World += p.Vel * dt;
                if (p.Kind == PSpark) { p.Vel.Y += 420f * dt; p.Vel *= MathF.Exp(-2.5f * dt); }
                else if (p.Kind == PDust) p.Vel *= MathF.Exp(-4f * dt);
                _particles[i] = p;
            }
        }

        private void DrawParticles(SpriteBatch sb, Texture2D px, CameraManager cam)
        {
            float z = cam.Zoom;
            foreach (Particle p in _particles)
            {
                float t = p.Life / p.Max;       // 1 -> 0
                Vector2 s = cam.WorldToScreen(p.World);
                switch (p.Kind)
                {
                    case PSpark:
                    {
                        float len = Math.Max(2f, p.Vel.Length() * 0.03f * z);
                        float ang = MathF.Atan2(p.Vel.Y, p.Vel.X);
                        sb.Draw(px, s, null, p.Color * Math.Min(1f, t * 1.6f), ang + MathF.PI, new Vector2(0f, 0.5f), new Vector2(len, Math.Max(1f, p.Size * z * 0.8f)), SpriteEffects.None, 0f);
                        break;
                    }
                    case PDust:
                    {
                        float sz = p.Size * (1.7f - 0.7f * t) * z;
                        sb.Draw(px, new Rectangle((int)(s.X - sz / 2), (int)(s.Y - sz / 2), (int)sz, (int)sz), p.Color * (0.5f * t));
                        break;
                    }
                    case PRing:
                    {
                        float r = MathHelper.Lerp(p.Size, 3f, t) * z;
                        int dot = Math.Max(1, (int)(2 * z * t + 1));
                        for (int k = 0; k < 14; k++)
                        {
                            float a = k / 14f * MathF.PI * 2f;
                            sb.Draw(px, new Rectangle((int)(s.X + MathF.Cos(a) * r), (int)(s.Y + MathF.Sin(a) * r * 0.6f), dot, dot), p.Color * (0.85f * t));
                        }
                        break;
                    }
                }
            }
        }

        private void Hit(Unit attacker, Unit victim, int dmg, bool heavy = false)
        {
            if (victim.IsDown) return;
            victim.HitPoints = Math.Max(0, victim.HitPoints - dmg);
            bool byPlayer = attacker == Possessed;
            victim.TakeHitVisual(IsoMath.TileToWorld(victim.Position) - IsoMath.TileToWorld(attacker.Position));
            AddFloat(victim.Position, dmg.ToString(), byPlayer ? new Color(255, 236, 120) : victim.Team == UnitTeam.Enemy ? Color.White : new Color(255, 120, 110), byPlayer && heavy);
            Impact(attacker, victim, heavy, byPlayer || victim == Possessed);
            if (SiegeMode && !victim.IsDown && victim != Possessed && !(victim is CitizenUnit))
            {
                // Qamal: zarba yegan jangchi javob qaytaradi
                if (!_agents.TryGetValue(victim, out Agent va))
                {
                    va = new Agent { Unit = victim, Home = victim.Position, Mode = AgentMode.Fighting };
                    _agents[victim] = va;
                    victim.ExternallyControlled = true;
                }
                if (va.Mode == AgentMode.Returning) va.Mode = AgentMode.Fighting;
                if (va.Mode == AgentMode.Fighting && (va.Target == null || va.Target.IsDown)) va.Target = attacker;
            }
            if (!victim.IsDown) return;

            if (victim is RaiderUnit rv)
            {
                _corpseTimers[victim] = 2.2f;
                if (_agents.TryGetValue(victim, out _)) _agents.Remove(victim);
                if (rv.IsChief) _state.ShowMessage("Bosqinchilar sardori yiqildi!" + (byPlayer ? " Ofarin, botir!" : ""));
                else if (byPlayer) AddFloat(victim.Position + new Vector2(-0.3f, -0.3f), "YIQITDINGIZ!", new Color(255, 214, 90));
            }
            else
            {
                AddFloat(victim.Position, victim.Name + " yiqildi", new Color(255, 140, 120));
            }
        }

        // ==================================================================
        // HARAKAT VA YO'L TOPISH
        // ==================================================================

        private bool Walk(Vector2 p) => _map.IsPassable((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));

        /// <summary>To'siqqa tegsa devor bo'ylab sirpanadi. Unit to'siq ichida qolib ketgan bo'lsa — chiqib ketishiga ruxsat.</summary>
        private bool MoveWithCollision(Unit u, Vector2 delta)
        {
            Vector2 p = u.Position;
            Vector2 np = Clamp(p + delta);
            if (!Walk(p) || Walk(np)) { u.Position = np; return true; }
            Vector2 nx = Clamp(new Vector2(p.X + delta.X, p.Y));
            if (MathF.Abs(delta.X) > 0.0001f && Walk(nx)) { u.Position = nx; return true; }
            Vector2 ny = Clamp(new Vector2(p.X, p.Y + delta.Y));
            if (MathF.Abs(delta.Y) > 0.0001f && Walk(ny)) { u.Position = ny; return true; }
            return false;
        }

        private Vector2 Clamp(Vector2 p) => new Vector2(
            MathHelper.Clamp(p.X, 0.3f, _map.Width - 0.3f), MathHelper.Clamp(p.Y, 0.3f, _map.Height - 0.3f));

        /// <summary>a dan b gacha to'g'ri chiziq to'siqsizmi (oxirgi qismi tekshirilmaydi — nishon to'siq yonida turishi mumkin).</summary>
        private bool Clear(Vector2 a, Vector2 b)
        {
            float d = Vector2.Distance(a, b);
            if (d < 0.6f) return true;
            int n = (int)((d - 0.5f) / 0.25f) + 1;
            for (int i = 1; i <= n; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i * 0.25f / d);
                if (!Walk(p)) return false;
            }
            return true;
        }

        private Point NearestWalkable(Point t)
        {
            if (_map.IsWalkable(t.X, t.Y)) return t;
            for (int r = 1; r <= 3; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                        if (_map.IsWalkable(t.X + dx, t.Y + dy)) return new Point(t.X + dx, t.Y + dy);
            return t;
        }

        /// <summary>Agentni maqsadga olib boradi (to'g'ri yo'l bo'lsa — to'g'ri, aks holda A*). Yetib kelganda true.</summary>
        private bool MoveAgent(Agent a, Vector2 goal, float speed, float dt, float stopDist)
        {
            Unit u = a.Unit;
            Vector2 pos = u.Position;
            if (Vector2.Distance(pos, goal) <= stopDist) { u.DriveIdle(); return true; }

            Vector2 next = goal;
            if (!Clear(pos, goal))
            {
                Point goalTile = NearestWalkable(new Point((int)MathF.Floor(goal.X), (int)MathF.Floor(goal.Y)));
                a.RepathTimer -= dt;
                if (a.Path == null || a.RepathTimer <= 0f || a.PathGoal != goalTile)
                {
                    Point start = NearestWalkable(new Point((int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y)));
                    a.Path = Pathfinder.FindPath(_map, start, goalTile, 4000);
                    a.PathGoal = goalTile;
                    a.PathIndex = a.Path != null && a.Path.Count > 1 ? 1 : 0;
                    a.RepathTimer = 0.8f + (float)_rng.NextDouble() * 0.3f;
                }
                if (a.Path != null && a.PathIndex < a.Path.Count)
                {
                    Point wp = a.Path[a.PathIndex];
                    next = new Vector2(wp.X + 0.5f, wp.Y + 0.5f);
                    if (Vector2.Distance(pos, next) < 0.2f && a.PathIndex < a.Path.Count - 1)
                    {
                        a.PathIndex++;
                        wp = a.Path[a.PathIndex];
                        next = new Vector2(wp.X + 0.5f, wp.Y + 0.5f);
                    }
                }
            }
            else a.Path = null;

            Vector2 dir = next - pos;
            float len = dir.Length();
            if (len < 0.0001f) { u.DriveIdle(); return false; }
            dir /= len;
            u.DriveMove(dir, speed);
            if (!MoveWithCollision(u, dir * MathF.Min(speed * dt, len))) a.RepathTimer = 0f; // tiqilib qoldi — yo'lni qayta hisoblash
            return false;
        }

        /// <summary>Jangdagi unitlar bir nuqtaga to'planib qolmasligi uchun yengil itarilish.</summary>
        private void Separate()
        {
            var list = new List<Unit>();
            foreach (Agent a in _agents.Values)
                if (!a.Unit.IsDown && a.Mode != AgentMode.Returning && a.Mode != AgentMode.Hiding) list.Add(a.Unit);
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    Vector2 d = list[j].Position - list[i].Position;
                    float l = d.Length();
                    if (l >= 0.45f) continue;
                    if (l < 0.001f) { d = new Vector2(0.01f * (j - i), 0.01f); l = d.Length(); }
                    Vector2 push = d / l * (0.45f - l) * 0.5f;
                    if (list[i] != Possessed) MoveWithCollision(list[i], -push);
                    if (list[j] != Possessed) MoveWithCollision(list[j], push);
                }
        }

        // ==================================================================
        // YORDAMCHI
        // ==================================================================

        private static bool IsSoldier(Unit u) => u.Team == UnitTeam.Player && !(u is CitizenUnit) && !(u is ShahUnit);

        private Unit NearestEnemy(Unit from, float range)
        {
            Unit best = null;
            float bestD = range;
            foreach (Unit o in _units.Units)
            {
                if (o.Team == from.Team || o.IsDown || o.Hidden || o is CitizenUnit) continue;
                float d = Vector2.Distance(o.Position, from.Position);
                if (d <= bestD) { bestD = d; best = o; }
            }
            return best;
        }

        private void RemoveUnit(Unit u)
        {
            if (u == null) return;
            if (Possessed == u) Release(false);
            if (u == _shah) _shah = null;
            _agents.Remove(u);
            _units.Remove(u);
            if (_state.Selected == u) _state.Select(null);
        }

        private void AddFloat(Vector2 tile, string text, Color c, bool big = false)
        {
            _floats.Add(new FloatText { World = IsoMath.TileToWorld(tile) + new Vector2(_rng.Next(-6, 7), -46), Text = text, Color = c, Big = big, Vx = _rng.Next(-14, 15) });
        }

        // ==================================================================
        // CHIZISH
        // ==================================================================

        /// <summary>Oyoq ostidagi halqalar (world batch, binolardan oldin).</summary>
        public void DrawGroundMarkers(SpriteBatch sb)
        {
            foreach (RaiderUnit r in _raiders)
                if (!r.IsDown) _map.DrawMarker(sb, IsoMath.TileToWorld(r.Position), new Color(220, 50, 40, 170), 0.42f);
            if (SiegeMode)
            {
                // Qamal: dushman — qizil, o'zimiznikilar — ko'k halqa; dushman shohi — katta oltin-qizil
                foreach (Unit u in _units.Units)
                {
                    if (u.IsDown || u.Hidden || u is CitizenUnit || u == Possessed) continue;
                    if (u is ShahUnit && u.Team == UnitTeam.Enemy)
                        _map.DrawMarker(sb, IsoMath.TileToWorld(u.Position), new Color(255, 170, 40, 220), 0.6f);
                    else
                        _map.DrawMarker(sb, IsoMath.TileToWorld(u.Position), u.Team == UnitTeam.Enemy ? new Color(220, 50, 40, 150) : new Color(70, 140, 235, 130), 0.4f);
                }
            }
            if (Possessed != null)
            {
                float pulse = 0.5f + 0.06f * MathF.Sin((float)DateTime.Now.TimeOfDay.TotalSeconds * 6f);
                _map.DrawMarker(sb, IsoMath.TileToWorld(Possessed.Position), new Color(255, 214, 90, 240), pulse);
            }
        }

        /// <summary>Ekran qatlami: HP chiziqlari, zarba raqamlari, bosqin banneri, boshqaruv paneli.</summary>
        /// <summary>HUD masshtabi (telefonda 2: eski UI bilan bir xil o'lcham).</summary>
        private int _k = 1;

        public void DrawHud(SpriteBatch sb, Texture2D px, PixelFont font, CameraManager cam, int vw, int vh, int uiScale = 1)
        {
            _k = Math.Max(1, uiScale);
            // --- HP chiziqlari (jangdagi yoki jarohatlangan unitlar) ---
            foreach (Unit u in _units.Units)
            {
                if (u.Hidden) continue;
                bool show = u.Team == UnitTeam.Enemy || u == Possessed || (u.HitPoints < u.MaxHitPoints && u.MaxHitPoints > 0)
                    || ((RaidActive || SiegeMode) && IsSoldier(u) && _agents.ContainsKey(u));
                if (!show || u.IsDown) continue;
                Vector2 s = cam.WorldToScreen(IsoMath.TileToWorld(u.Position) + new Vector2(0, -52));
                if (s.X < -40 || s.Y < -20 || s.X > vw + 40 || s.Y > vh + 20) continue;
                int w = 30, h = 4;
                var r = new Rectangle((int)s.X - w / 2, (int)s.Y, w, h);
                sb.Draw(px, new Rectangle(r.X - 1, r.Y - 1, w + 2, h + 2), new Color(10, 10, 12, 200));
                float f = u.MaxHitPoints > 0 ? (float)u.HitPoints / u.MaxHitPoints : 0f;
                Color c = u.Team == UnitTeam.Enemy ? new Color(220, 60, 50) : u == Possessed ? new Color(255, 210, 80) : new Color(90, 200, 110);
                sb.Draw(px, new Rectangle(r.X, r.Y, (int)(w * f), h), c);
            }

            // --- Qilich izlari (har bir zarba turi o'z shaklida) ---
            foreach (Unit u in _units.Units)
                if (u.AttackTimer > 0f && !u.IsDown) DrawSlash(sb, px, cam, u);

            // --- Zarba raqamlari ---
            // --- Uchqun, chang va zarba halqalari ---
            DrawParticles(sb, px, cam);

            // --- Kamon o'qlari (yoysimon uchadi) ---
            foreach (Arrow ar in _arrows)
            {
                Vector2 a0 = cam.WorldToScreen(IsoMath.TileToWorld(ar.From) + new Vector2(0, -24));
                Vector2 a1 = cam.WorldToScreen(IsoMath.TileToWorld(ar.To) + new Vector2(0, -20));
                float hgt = Vector2.Distance(a0, a1) * 0.25f;
                Vector2 Pt(float t) => Vector2.Lerp(a0, a1, t) + new Vector2(0, -4f * hgt * t * (1f - t));
                Vector2 head = Pt(ar.T), tail = Pt(Math.Max(0f, ar.T - 0.12f));
                Vector2 d = head - tail;
                float len = Math.Max(2f, d.Length());
                sb.Draw(px, tail, null, new Color(60, 40, 24), MathF.Atan2(d.Y, d.X), new Vector2(0f, 0.5f), new Vector2(len, Math.Max(1f, cam.Zoom)), SpriteEffects.None, 0f);
                sb.Draw(px, new Rectangle((int)head.X - 1, (int)head.Y - 1, 3, 3), new Color(220, 220, 230));
            }

            foreach (FloatText f in _floats)
            {
                float rise = 30f * (1f - (1f - f.T) * (1f - f.T));
                Vector2 s = cam.WorldToScreen(f.World + new Vector2(f.Vx * f.T, -rise));
                float alpha = f.T < 0.7f ? 1f : 1f - (f.T - 0.7f) / 0.3f;
                int scale = (f.Big ? 3 : 2) * _k + (f.T < 0.09f ? _k : 0); // paydo bo'lganda "otilib" kattaroq
                Vector2 size = font.Measure(f.Text, scale);
                Vector2 p = new Vector2(MathF.Round(s.X - size.X / 2), MathF.Round(s.Y));
                font.Draw(sb, f.Text, p + new Vector2(_k, _k), Color.Black * (0.75f * alpha), scale);
                font.Draw(sb, f.Text, p, f.Color * alpha, scale);
            }

            // --- ALARM (urush qo'ng'irog'i): qizil chaqnagan chekkalar va chalinayotgan qo'ng'iroq ---
            if (RaidIncoming || RaidActive)
            {
                float pulse = (MathF.Sin(System.Environment.TickCount / 150f) + 1f) / 2f;
                Color edge = new Color(205, 32, 24) * (0.16f + 0.30f * pulse);
                int th = 10 * _k;
                sb.Draw(px, new Rectangle(0, 0, vw, th), edge);
                sb.Draw(px, new Rectangle(0, vh - th, vw, th), edge);
                sb.Draw(px, new Rectangle(0, 0, th, vh), edge);
                sb.Draw(px, new Rectangle(vw - th, 0, th, vh), edge);
                DrawAlarmBell(sb, px, 16 * _k, 14 * _k, _k, pulse);
            }

            // --- Bosqin banneri ---
            if (RaidIncoming)
            {
                string t = "DUSHMAN YAQINLASHMOQDA!  " + GateNames[_gateIndex].ToUpperInvariant() + " DARVOZA  " + (int)MathF.Ceiling(_warnTimer);
                Banner(sb, px, font, vw, t, new Color(150, 30, 26, 230), Color.White, _k);
            }
            else if (RaidActive)
            {
                int alive = 0;
                foreach (RaiderUnit r in _raiders) if (!r.IsDown) alive++;
                int def = 0;
                foreach (Unit u in _units.Units) if (IsSoldier(u) && !u.IsDown) def++;
                string t = "BOSQIN!  Dushman: " + alive + "/" + _raidTotal + "   Himoyachilar: " + def + (_lootStolen > 0 ? "   Talangan: " + _lootStolen : "");
                Banner(sb, px, font, vw, t, new Color(110, 24, 22, 220), new Color(255, 230, 200), _k);
                if (Possessed == null)
                {
                    // Ko'rsatma xarita ustida o'qilishi uchun qora fon bilan
                    string hint = "Jangchini tanlang va F bosing - o'zingiz jangga kiring!";
                    int hs = FitScale(font, hint, vw - 24, 2 * _k);
                    Vector2 hm = font.Measure(hint, hs);
                    float hy = (44 + 30) * _k + 8 + hm.Y / 2f;
                    sb.Draw(px, new Rectangle((int)(vw / 2f - hm.X / 2 - 8), (int)(hy - hm.Y / 2 - 5), (int)hm.X + 16, (int)hm.Y + 10), new Color(20, 12, 10, 190));
                    font.DrawCentered(sb, hint, new Vector2(vw / 2f, hy), new Color(255, 220, 150), hs);
                }
            }
            else if (_resultTimer > 0f && _resultText != null)
            {
                Banner(sb, px, font, vw, _resultText, _resultVictory ? new Color(30, 100, 50, 225) : new Color(90, 30, 30, 225), Color.White, _k);
            }

            // --- Boshqaruv paneli ---
            if (Possessed != null) DrawPossessionPanel(sb, px, font, vw, vh);
        }

        /// <summary>
        /// Qilich izi: KESISH — gorizontal yoy, CHOPISH — tepadan pastga yoy, SANCHISH — oldinga chiziq,
        /// AYLANMA — to'liq aylana. Iz zarba jarayoniga qarab chiziladi va so'nib boradi.
        /// </summary>
        private void DrawSlash(SpriteBatch sb, Texture2D px, CameraManager cam, Unit u)
        {
            float p = u.AttackProgress;
            float z = cam.Zoom;
            Vector2 center = cam.WorldToScreen(IsoMath.TileToWorld(u.Position) + new Vector2(0, -22));
            Vector2 f = u.FacingWorld;
            float a0 = MathF.Atan2(f.Y, f.X);
            Color col = u == Possessed ? new Color(255, 244, 200) : u.Team == UnitTeam.Enemy ? new Color(255, 170, 160) : new Color(225, 230, 240);
            const int n = 18;
            bool havePrev = false;
            Vector2 prev = Vector2.Zero;

            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                if (t > p) break;
                float alpha = MathHelper.Clamp(1f - (p - t) * 2.6f, 0f, 1f);
                Vector2 pt;
                switch (u.AttackKind)
                {
                    case Unit.AttackOverhead:
                    {
                        float s = f.X >= 0 ? 1f : -1f;
                        float ang = -MathF.PI / 2f + s * t * 2.3f;
                        pt = center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * 22f * z + f * 4f * z;
                        break;
                    }
                    case Unit.AttackThrust:
                        pt = center + f * (6f + 30f * t) * z + new Vector2(0, 6f * z);
                        break;
                    case Unit.AttackSpin:
                    {
                        float ang = a0 + t * MathF.PI * 2f;
                        pt = center + new Vector2(MathF.Cos(ang), MathF.Sin(ang) * 0.55f) * 26f * z + new Vector2(0, 10f * z);
                        break;
                    }
                    default:
                    {
                        float ang = a0 - 1.25f + 2.5f * t;
                        pt = center + new Vector2(MathF.Cos(ang), MathF.Sin(ang) * 0.6f) * 20f * z + new Vector2(0, 6f * z);
                        break;
                    }
                }
                // Yaxlit yoy: oldingi nuqtadan shu nuqtagacha qalinligi so'nishga mos chiziq (boshi qalin, dumi ingichka)
                if (havePrev && alpha > 0.02f)
                {
                    Vector2 d = pt - prev;
                    float len = d.Length();
                    if (len > 0.01f)
                    {
                        float thick = (1.2f + 3.2f * alpha) * z;
                        float ang = MathF.Atan2(d.Y, d.X);
                        sb.Draw(px, prev, null, Color.Black * (0.35f * alpha), ang, new Vector2(0f, 0.5f), new Vector2(len + 1f, thick + 2f), SpriteEffects.None, 0f);
                        sb.Draw(px, prev, null, col * (0.95f * alpha), ang, new Vector2(0f, 0.5f), new Vector2(len + 1f, thick), SpriteEffects.None, 0f);
                    }
                }
                prev = pt;
                havePrev = true;
            }
        }

        /// <summary>Matn kenglikka sig'adigan eng katta piksel masshtabi (kamida 1).</summary>
        private static int FitScale(PixelFont font, string text, int maxW, int want)
        {
            for (int s = Math.Max(1, want); s > 1; s--)
                if (font.Measure(text, s).X <= maxW) return s;
            return 1;
        }

        private static void Banner(SpriteBatch sb, Texture2D px, PixelFont font, int vw, string text, Color bg, Color fg, int k = 1)
        {
            int sc = FitScale(font, text, vw - 60, 2 * k);
            Vector2 size = font.Measure(text, sc);
            int w = (int)size.X + 40 * k, h = (int)size.Y + 16 * k;
            var r = new Rectangle(vw / 2 - w / 2, 44 * k, w, h);
            sb.Draw(px, new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), new Color(20, 12, 10, 220));
            sb.Draw(px, r, bg);
            font.DrawCentered(sb, text, r.Center.ToVector2(), fg, sc);
        }

        /// <summary>Chalinayotgan qo'ng'iroq belgisi (ovoz tizimi yo'q — vizual alarm). Yon tebranadi va tovush to'lqinlari chiqaradi.</summary>
        private static void DrawAlarmBell(SpriteBatch sb, Texture2D px, int x, int y, int k, float pulse)
        {
            int sway = (int)(MathF.Sin(System.Environment.TickCount / 85f) * 2.5f * k);
            Color gold = new Color(240, 200, 96), dark = new Color(60, 44, 20);
            int cx = x + sway;
            // dastak
            sb.Draw(px, new Rectangle(cx + 6 * k, y, 2 * k, 2 * k), dark);
            // tanasi (yuqoridan pastga kengayadi)
            sb.Draw(px, new Rectangle(cx + 5 * k, y + 2 * k, 4 * k, 3 * k), gold);
            sb.Draw(px, new Rectangle(cx + 3 * k, y + 5 * k, 8 * k, 4 * k), gold);
            sb.Draw(px, new Rectangle(cx + 1 * k, y + 9 * k, 12 * k, 4 * k), gold);
            // labi va tili
            sb.Draw(px, new Rectangle(cx, y + 13 * k, 14 * k, 2 * k), dark);
            sb.Draw(px, new Rectangle(cx + 6 * k, y + 14 * k, 2 * k, 3 * k), dark);
            // tovush to'lqinlari (pulse bilan)
            if (pulse > 0.45f)
            {
                Color wave = new Color(255, 232, 150) * ((pulse - 0.45f) * 1.8f);
                sb.Draw(px, new Rectangle(x - 4 * k, y + 5 * k, 2 * k, k), wave);
                sb.Draw(px, new Rectangle(x - 7 * k, y + 8 * k, 3 * k, k), wave);
                sb.Draw(px, new Rectangle(x + 16 * k, y + 5 * k, 2 * k, k), wave);
                sb.Draw(px, new Rectangle(x + 17 * k, y + 8 * k, 3 * k, k), wave);
            }
        }

        private void DrawPossessionPanel(SpriteBatch sb, Texture2D px, PixelFont font, int vw, int vh)
        {
            Unit p = Possessed;
            int k = _k;
            if (TouchUi)
            {
                // Telefon: tepada ixcham — ism va jon chizig'i (pastki burchaklar joystik va tugmalarga band)
                int tw = Math.Min(vw - 40 * k, 420 * k), th = 40 * k;
                var tr = new Rectangle(vw / 2 - tw / 2, 58 * k, tw, th);
                sb.Draw(px, new Rectangle(tr.X - 2, tr.Y - 2, tr.Width + 4, tr.Height + 4), new Color(216, 167, 83, 220));
                sb.Draw(px, tr, new Color(24, 20, 18, 225));
                font.Draw(sb, "SIZ: " + p.Name.ToUpperInvariant(), new Vector2(tr.X + 10 * k, tr.Y + 5 * k), new Color(255, 214, 90), 2 * k);
                int hbw = tw - 20 * k, hbh = 8 * k;
                float fr = p.MaxHitPoints > 0 ? (float)p.HitPoints / p.MaxHitPoints : 0f;
                var hb = new Rectangle(tr.X + 10 * k, tr.Bottom - hbh - 6 * k, hbw, hbh);
                sb.Draw(px, new Rectangle(hb.X - 1, hb.Y - 1, hb.Width + 2, hb.Height + 2), Color.Black);
                sb.Draw(px, new Rectangle(hb.X, hb.Y, (int)(hbw * fr), hbh), fr > 0.35f ? new Color(90, 200, 110) : new Color(220, 70, 50));
                return;
            }
            // Boshqaruv yo'riqnomasi ikki qatorda va o'qiladigan o'lchamda (oldin bir qatorda mayda bo'lib, panelga sig'masdi)
            string[] help =
            {
                "WASD yurish | SHIFT yugurish | SPACE kombo | R aylanma",
                SiegeMode ? "Q/E boshqa jangchi | F chiqish (qo'shin o'zi jang qiladi)" : "ENTER binoga kirish | Q/E boshqasi | F chiqish"
            };
            int w = Math.Min(vw - 32, 640 * k);
            int hs = 2 * k;
            foreach (string line in help) hs = Math.Min(hs, FitScale(font, line, w - 28 * k, 2 * k));
            int helpLine = PixelFont.GH * hs + 5 * k;
            int h = 10 * k + 20 * k + 20 * k + help.Length * helpLine + 6 * k;
            var r = new Rectangle(vw / 2 - w / 2, vh - 44 * k - h - 10 * k, w, h);
            sb.Draw(px, new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), new Color(216, 167, 83, 230));
            sb.Draw(px, r, new Color(24, 20, 18, 235));

            int x = r.X + 14 * k, y = r.Y + 10 * k;
            string atk = "Hujum " + Math.Max(4, p.Attack);
            int ts = FitScale(font, "SIZ: " + p.Name.ToUpperInvariant() + "    " + atk, w - 28 * k, 2 * k);
            font.Draw(sb, "SIZ: " + p.Name.ToUpperInvariant(), new Vector2(x, y), new Color(255, 214, 90), ts);
            font.DrawRight(sb, atk, new Vector2(r.Right - 14 * k, y), new Color(220, 200, 170), ts);
            y += 20 * k;

            // HP
            int bw = w - 28 * k, bh = 10 * k;
            sb.Draw(px, new Rectangle(x - 1, y - 1, bw + 2, bh + 2), Color.Black);
            float f = p.MaxHitPoints > 0 ? (float)p.HitPoints / p.MaxHitPoints : 0f;
            sb.Draw(px, new Rectangle(x, y, (int)(bw * f), bh), f > 0.35f ? new Color(90, 200, 110) : new Color(220, 70, 50));
            font.DrawCentered(sb, p.HitPoints + " / " + p.MaxHitPoints, new Vector2(x + bw / 2f, y + bh / 2f + 1), Color.White, k);
            if (p.AttackCooldown > 0f)
                sb.Draw(px, new Rectangle(x, y + bh + 2 * k, (int)(bw * (p.AttackCooldown / PlayerAttackCooldown)), 2 * k), new Color(255, 214, 90, 180));
            y += 20 * k;

            foreach (string line in help)
            {
                font.Draw(sb, line, new Vector2(x, y), new Color(200, 190, 168), hs);
                y += helpLine;
            }
        }
    }
}
