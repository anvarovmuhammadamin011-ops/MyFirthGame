using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Battles;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Interiors;
using AndijonSaltanatlar.Military;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Units;
using AndijonSaltanatlar.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AndijonSaltanatlar.Combat
{
    /// <summary>
    /// QAMAL — boshqa davlat qasriga yurish. Qo'shiningiz dushmanning O'Z SHAHRIGA boradi: shahar (binolar,
    /// ko'chalar, aholi, soqchilar) yaratiladi, jangchilaringiz janubiy darvoza oldida saf tortadi va saroy
    /// tomon yuradi. Dushman soqchilari yaqinlashganda jangga kiradi, aholi qo'ng'iroq bilan uylarga yashirinadi.
    ///  - Istalgan jangchingiz ichiga kirib (F) o'zingiz urishasiz.
    ///  - SAROY ESHIGIGA yetgan jangchingiz bilan saroy ICHIGA kirasiz: taxt zalida shoh, vazir va soqchilar.
    ///  - Shoh va vazirni o'ldirib saroydan chiqsangiz — tirik dushman askarlari tomoningizga o'tadi, qasr sizniki.
    ///  - Qo'shiningiz butunlay yiqilsa yoki Esc bilan chekinsangiz — mag'lubiyat.
    /// Onlayn dunyo (M xarita) qasrida natija serverga yoziladi: g'alabada qasrga bayrog'ingiz tikiladi.
    /// </summary>
    public sealed class SiegeMode
    {
        private enum Phase { Deploy, Assault, Captured, Defeated }

        // --- SAF: qo'shin darvoza oldida qismlarga bo'linib turadi, o'yinchi qaysi qism birinchi borishini tanlaydi ---
        private static readonly (string Key, string Label)[] GroupDefs =
            { ("otliq", "OTLIQLAR"), ("piyoda", "PIYODALAR"), ("nayza", "NAYZACHILAR"), ("kamon", "KAMONCHILAR") };
        private readonly Dictionary<string, List<Unit>> _groups = new Dictionary<string, List<Unit>>();
        private readonly HashSet<string> _sent = new HashSet<string>();
        private readonly List<(Rectangle Rect, string Key)> _groupButtons = new List<(Rectangle, string)>();
        private Vector2 _formationCenter;

        private static string GroupOf(UnitType t) => t switch
        {
            UnitType.Otliq or UnitType.ElitaOtliq => "otliq",
            UnitType.Kamonchi => "kamon",
            UnitType.Nayzador or UnitType.SaroyQoriqchisi => "nayza",
            _ => "piyoda"
        };

        /// <summary>Qamal nishoni (mahalliy saltanat xaritasidan yoki onlayn dunyo xaritasidan).</summary>
        private sealed class Target
        {
            public int RegionId;
            public string RegionName = "", FoeName = "", RulerName = "";
            public int AttackerPower, DefenderPower;
            public bool Capital, Online;
        }

        private readonly AssetManager _assets;
        private readonly GameStateManager _state;
        private readonly PixelFont _font;

        private MapManager _map;
        private BuildingManager _buildings;
        private UnitManager _units;
        private CombatSystem _combat;
        private WorldRenderer _renderer;
        private CameraManager _camera;
        private HouseInterior _interior;
        private Building _palace;

        private readonly List<Unit> _attackers = new List<Unit>();
        private readonly List<Unit> _defenders = new List<Unit>();
        private readonly List<Unit> _defectors = new List<Unit>();
        private readonly List<SiegeFoe> _roster = new List<SiegeFoe>();
        private SiegeFoe _shahFoe, _vizierFoe;
        private Unit _inside;           // saroy ichidagi jangchi
        private bool _wasInside;
        private Vector2 _palaceDoor;
        private Phase _phase;
        private float _endTimer;
        private float _time;
        private string _banner;
        private float _bannerTimer;
        private Unit _lastPossessed;
        private float _manualCam;
        private Target _t;
        private int _armyAtStart;

        public bool Active { get; private set; }
        public int RegionId => _t?.RegionId ?? -1;
        public string PhaseName => _phase.ToString();
        public CombatSystem Combat => _combat;
        public bool InPalace => _interior != null && _interior.Active;
        public IReadOnlyList<Unit> Attackers => _attackers;
        public int AliveAttackers => _units?.Units.Count(u => u.Team == UnitTeam.Player && IsFighter(u) && !u.IsDown) ?? 0;
        public int AliveDefenders => _units?.Units.Count(u => u.Team == UnitTeam.Enemy && IsFighter(u) && !u.IsDown) ?? 0;

        /// <summary>Onlayn qamal tugadi: natija serverga yuboriladi (Game1 o'rnatadi).</summary>
        public Action<SiegeFinishRequest> OnlineFinished;

        public SiegeMode(AssetManager assets, GameStateManager state, PixelFont font)
        {
            _assets = assets;
            _state = state;
            _font = font;
        }

        private static bool IsFighter(Unit u) => !(u is CitizenUnit) && !(u is ShahUnit);

        // ==================================================================
        // BOSHLASH
        // ==================================================================

        /// <summary>Mahalliy saltanat xaritasidan yurish yetib keldi.</summary>
        public bool Start(int regionId, Viewport viewport)
        {
            var W = _state.World;
            if (regionId < 0)
                foreach (var rr in W.Regions.Regions)
                    if (rr.OwnerKingdomId != 0 && rr.OwnerKingdomId != W.Kingdoms.PlayerKingdomId) { regionId = rr.Id; break; }
            var r = W.Regions.Get(regionId);
            if (r == null) return false;
            var def = W.Kingdoms.Get(r.OwnerKingdomId);
            if (def == null || def.Id == W.Kingdoms.PlayerKingdomId) return false;
            return Begin(new Target
            {
                RegionId = regionId, RegionName = r.Name, FoeName = def.Name,
                RulerName = string.IsNullOrWhiteSpace(def.RulerName) ? "Hukmdor" : def.RulerName,
                DefenderPower = Math.Max(20, def.MilitaryStrength), Capital = r.IsCapital, Online = false
            }, viewport);
        }

        /// <summary>Onlayn dunyo xaritasidan (M) qamal: qasr serverdagi saltanatniki.</summary>
        public bool StartOnline(WarMapRegionDto r, Viewport viewport)
        {
            if (r == null) return false;
            string ruler = string.IsNullOrWhiteSpace(r.RulerName) ? r.OwnerName + " hukmdori" : r.RulerName;
            return Begin(new Target
            {
                RegionId = r.RegionId, RegionName = r.Name, FoeName = r.OwnerName, RulerName = ruler,
                DefenderPower = Math.Max(20, r.DefenseEstimate), Capital = r.IsCapital, Online = true
            }, viewport);
        }

        private bool Begin(Target t, Viewport viewport)
        {
            ArmyManager mil = _state.Military;
            if (mil.TotalUnits == 0) { _state.ShowMessage("Yurish uchun qo'shin yo'q — avval jangchi yollang", true); return false; }
            _t = t;
            t.AttackerPower = Math.Max(20, mil.TotalStrength);
            _armyAtStart = mil.TotalUnits;

            // --- Dushman shahri (har qasr o'z urug'i bilan) ---
            _map = new MapManager(_assets, GameConfig.MapSize, GameConfig.MapSize);
            _buildings = new BuildingManager(_map, _assets);
            _units = new UnitManager(_assets);
            CityGenerator.Generate(_map, _buildings, _units, 7919 * (Math.Abs(t.RegionId) + 1));
            CitizenSpawner.Spawn(_map, _units);
            PatrolController.RouteAll(_map, _units);
            _combat = new CombatSystem(_map, _units, _state) { Buildings = _buildings, SiegeMode = true };
            _renderer = new WorldRenderer(_assets, _font);
            _interior = new HouseInterior(_assets, _map, _buildings, _state);

            _palace = _buildings.Buildings.FirstOrDefault(b => b.Type == BuildingType.MainHall);
            Point door = _palace != null ? _palace.FrontEntranceTile : new Point(GameConfig.CityCenter, GameConfig.CityCenter - 6);
            _palaceDoor = new Vector2(door.X + 0.5f, door.Y + 0.6f);

            SetupDefenders();
            SetupRoster();
            SetupAttackers(mil);

            _phase = Phase.Deploy;
            _sent.Clear();
            _combat.SiegeAlert = false;
            _endTimer = 0f;
            _time = 0f;
            _lastPossessed = null;
            _inside = null;
            _wasInside = false;
            _combat.SiegeObjective = _palaceDoor;

            _camera = new CameraManager(viewport, IsoMath.TileToWorld(_formationCenter) + new Vector2(0, -60), 1.6f);
            _manualCam = 0f;
            Active = true;
            ShowBanner("QAMAL: " + t.RegionName.ToUpperInvariant() + "!  Qo'shin darvoza oldida saf tortdi", 5f);
            _state.ShowMessage("Qaysi qism birinchi hujumga borsin? Pastdagi tugmalar yoki 1-4, hammasi — 5");
            return true;
        }

        private void SetupDefenders()
        {
            _defenders.Clear();
            var rng = new Random(Math.Abs(_t.RegionId) * 31 + 7);
            // Dushman kuchi: o'yinchi qo'shiniga nisbatan (juda kuchsiz yoki juda kuchli bo'lsa ham o'ynaladigan chegarada)
            float ratio = Math.Clamp(_t.DefenderPower / (float)Math.Max(1, _t.AttackerPower), 0.45f, 1.6f);
            int attackers = Math.Clamp(_armyAtStart, 1, 24);
            int want = Math.Clamp((int)MathF.Round(attackers * ratio * 0.9f) + (_t.Capital ? 2 : 0), 3, 16);

            var soldiers = _units.Units.Where(u => IsFighter(u)).ToList();
            // Saroyga eng yaqinlari qoladi (saroy soqchilari doim bor), qolganlardan tasodifiy
            soldiers.Sort((a, b) => Vector2.Distance(a.Position, _palaceDoor).CompareTo(Vector2.Distance(b.Position, _palaceDoor)));
            var keep = new List<Unit>();
            for (int i = 0; i < soldiers.Count && keep.Count < 2; i++) keep.Add(soldiers[i]);
            foreach (Unit u in soldiers.Skip(keep.Count).OrderBy(_ => rng.Next())) if (keep.Count < want) keep.Add(u);
            foreach (Unit u in soldiers) if (!keep.Contains(u)) _units.Remove(u);

            foreach (Unit u in _units.Units) u.Team = UnitTeam.Enemy; // aholi ham — dushman shahri
            _defenders.AddRange(keep);
        }

        /// <summary>Saroy ichidagilar: shoh, vazir va ikki saroy soqchisi.</summary>
        private void SetupRoster()
        {
            _roster.Clear();
            int bonus = Math.Min(160, _t.DefenderPower / 5) + (_t.Capital ? 80 : 0);
            _shahFoe = new SiegeFoe { Name = "Shoh " + _t.RulerName, Short = "SHOH", IsShah = true, MaxHp = 220 + bonus, Attack = 13 };
            _vizierFoe = new SiegeFoe { Name = "Vazir", Short = "VAZIR", IsVizier = true, MaxHp = 110 + bonus / 3, Attack = 7 };
            _roster.Add(_shahFoe);
            _roster.Add(_vizierFoe);
            for (int i = 0; i < 2; i++) _roster.Add(new SiegeFoe { Name = "Saroy soqchisi", Short = "Soqchi", MaxHp = 120 + bonus / 3, Attack = 9 });
            foreach (SiegeFoe f in _roster) f.Hp = f.MaxHp;
        }

        private void SetupAttackers(ArmyManager mil)
        {
            _attackers.Clear();
            _defectors.Clear();
            // Qo'shindan eng kuchli 24 tagacha jangchi sahnaga chiqadi (qolganlari zaxirada)
            _groups.Clear();
            var chosen = mil.Units.OrderByDescending(u => u.Strength).Take(24).ToList();
            int O = GameConfig.CityOffset;
            // Qismlar: otliqlar, piyodalar, nayzachilar, kamonchilar — har biri darvoza oldida alohida saf (blok)
            var byGroup = GroupDefs.Select(g => (g.Key, Units: chosen.Where(c => GroupOf(c.UnitType) == g.Key).ToList())).Where(g => g.Units.Count > 0).ToList();
            // Darvozadan janubga ketgan yo'l bo'ylab: birinchi qism oldinda, qolganlari ortida (har qism — 3 qatorli blok)
            const int Cols = 3;
            const float Step = 0.9f, BlockGap = 1.3f;
            float frontY = O + 56.5f, y = frontY;
            var used = new HashSet<Point>();
            foreach (var g in byGroup)
            {
                var list = new List<Unit>();
                for (int i = 0; i < g.Units.Count; i++)
                {
                    var want = new Vector2(O + 31.1f + (i % Cols) * Step, y + (i / Cols) * Step);
                    Vector2 pos = NearestFree(want, used);
                    Unit u = Create(g.Units[i].UnitType, pos);
                    if (g.Key == "kamon") u.MakeArcher();
                    u.Team = UnitTeam.Player;
                    u.ExternallyControlled = true; // buyruq kutib saf turadi
                    u.SetFacing(new Vector2(0, -1));
                    _units.Add(u);
                    _attackers.Add(u);
                    list.Add(u);
                }
                _groups[g.Key] = list;
                y += ((g.Units.Count + Cols - 1) / Cols) * Step + BlockGap;
            }
            float midY = (frontY + y) / 2f;
            _formationCenter = new Vector2(O + 32f, midY);
        }

        private Vector2 NearestFree(Vector2 want, HashSet<Point> used)
        {
            Vector2 p = NearestOpen(want);
            for (int tries = 0; tries < 30; tries++)
            {
                var key = new Point((int)MathF.Round(p.X * 2f), (int)MathF.Round(p.Y * 2f));
                if (used.Add(key)) return p;
                p = NearestOpen(want + new Vector2((tries % 5 - 2) * 0.5f, 0.5f + tries / 5 * 0.5f));
            }
            return p;
        }

        /// <summary>Qismni hujumga yuboradi (birinchi buyruq — himoyachilar ham hushyor bo'ladi).</summary>
        public void SendGroup(string key)
        {
            if (!_groups.TryGetValue(key, out List<Unit> list) || _sent.Contains(key)) return;
            _sent.Add(key);
            foreach (Unit u in list) if (!u.IsDown) _combat.EnlistAttacker(u);
            _combat.SiegeAlert = true;
            if (_phase == Phase.Deploy) _phase = Phase.Assault;
            string label = GroupDefs.First(g => g.Key == key).Label;
            ShowBanner(label + " HUJUMGA!", 2.5f);
        }

        public void SendAll()
        {
            foreach (var g in GroupDefs) SendGroup(g.Key);
            ShowBanner("BUTUN QO'SHIN HUJUMGA!", 2.5f);
        }

        private Vector2 NearestOpen(Vector2 p)
        {
            for (int r = 0; r <= 6; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = (int)MathF.Floor(p.X) + dx, y = (int)MathF.Floor(p.Y) + dy;
                        if (_map.IsWalkable(x, y)) return new Vector2(x + (p.X - MathF.Floor(p.X)), y + (p.Y - MathF.Floor(p.Y)));
                    }
            return p;
        }

        /// <summary>Qo'shin turidan sahnadagi jangchi.</summary>
        private static Unit Create(UnitType type, Vector2 pos)
        {
            Vector2[] here = { pos };
            switch (type)
            {
                case UnitType.Otliq:
                case UnitType.ElitaOtliq: return new CavalryUnit(here);
                case UnitType.Nayzador:
                case UnitType.SaroyQoriqchisi: return new SpearmanUnit(pos, Direction.North);
                case UnitType.Qalqonchi: return new GuardUnit(pos, Direction.North);
                case UnitType.Qilichboz: return new SwordsmanUnit(here);
                default: return new PatrolUnit(here);
            }
        }

        private static UnitType TypeOf(Unit u) => u switch
        {
            CavalryUnit _ => UnitType.Otliq,
            Unit a when a.IsArcher => UnitType.Kamonchi,
            SpearmanUnit _ => UnitType.Nayzador,
            GuardUnit _ => UnitType.Qalqonchi,
            SwordsmanUnit _ => UnitType.Qilichboz,
            _ => UnitType.Piyoda
        };

        // ==================================================================
        // YANGILASH
        // ==================================================================

        public void Update(float dt, InputManager input, Viewport viewport)
        {
            if (!Active) return;
            _time += dt;
            if (_bannerTimer > 0f) _bannerTimer -= dt;

            bool inside = _interior.Active;
            if (inside) _interior.Update(dt, input);
            else if (input != null && _phase < Phase.Captured) HandleInput(input);
            if (_wasInside && !_interior.Active) LeftPalace();
            _wasInside = _interior.Active;

            _combat.SiegeObjective = _palaceDoor;
            _combat.Update(dt, _phase < Phase.Captured && !_interior.Active ? input : null, _camera);

            float wdt = dt * _combat.TimeScale;
            _units.Update(wdt);
            _units.ApplyPhysics(_map, _combat.Possessed, wdt);
            _renderer.Tick(dt);
            _map.Update(dt);

            // Kamera: ichiga kirganda jangchiga ergashadi, aks holda qo'shinga (o'yinchi o'zi surmasa)
            Unit p = _combat.Possessed;
            if (p != null && _lastPossessed == null) _camera.SetTargetZoom(2.2f);
            _lastPossessed = p;
            _camera.KeyboardPanEnabled = p == null;
            if (input != null && p == null && (input.IsPanDragging || input.KeyDown(Keys.W) || input.KeyDown(Keys.A) || input.KeyDown(Keys.S) || input.KeyDown(Keys.D)
                || input.KeyDown(Keys.Up) || input.KeyDown(Keys.Down) || input.KeyDown(Keys.Left) || input.KeyDown(Keys.Right)))
                _manualCam = 4f;
            else if (_manualCam > 0f) _manualCam -= dt;
            if (p != null) _camera.Follow(IsoMath.TileToWorld(p.Position) + new Vector2(0f, -20f), dt);
            else if (_manualCam <= 0f && ArmyFront(out Vector2 front)) _camera.Follow(IsoMath.TileToWorld(front) + new Vector2(0f, -20f), dt, 2.5f);
            if (input != null && !_interior.Active) _camera.Update(dt, input, viewport);

            UpdatePhase(dt);
        }

        private bool ArmyFront(out Vector2 at)
        {
            Vector2 sum = Vector2.Zero;
            int n = 0;
            foreach (Unit u in _units.Units)
                if (u.Team == UnitTeam.Player && !u.IsDown && !u.Hidden && IsFighter(u)) { sum += u.Position; n++; }
            at = n > 0 ? sum / n : Vector2.Zero;
            return n > 0;
        }

        private bool _lastTouch;

        private void HandleInput(InputManager input)
        {
            _lastTouch = input.IsTouch;
            // Qismlarni hujumga yuborish: tugmalar (sichqoncha) yoki 1-4, 5 — hammasi
            bool overPanel = false;
            foreach (var b in _groupButtons)
            {
                if (!b.Rect.Contains(input.MouseVector.ToPoint())) continue;
                overPanel = true;
                if (input.LeftPressed)
                {
                    if (b.Key == "all") SendAll();
                    else if (b.Key == "possess") PossessPick(input);
                    else SendGroup(b.Key);
                }
            }
            if (overPanel) input.MouseOverUI = true;
            Keys[] num = { Keys.D1, Keys.D2, Keys.D3, Keys.D4 };
            var present = GroupDefs.Where(g => _groups.ContainsKey(g.Key)).ToList();
            for (int i = 0; i < present.Count && i < num.Length; i++)
                if (input.KeyPressed(num[i]) || input.KeyPressed(Keys.NumPad1 + i)) SendGroup(present[i].Key);
            if (input.KeyPressed(Keys.D5) || input.KeyPressed(Keys.NumPad5)) SendAll();
            if (overPanel) return;

            if (_combat.Possessed != null) return;
            if (input.KeyPressed(Keys.Escape)) { Retreat(); return; }
            if (input.LeftPressed && !input.MouseOverUI)
            {
                Vector2 world = _camera.ScreenToWorld(input.MouseVector);
                if (_renderer.PickAt(world, _map, _buildings, _units) is Unit u && u.Team == UnitTeam.Player && !u.IsDown && IsFighter(u))
                    _state.Select(u);
            }
            if (input.KeyPressed(Keys.F) || input.KeyPressed(Keys.Space)) PossessPick(input);
        }

        private void PossessPick(InputManager input)
        {
            if (_combat.Possessed != null) return;
            Unit pick = _state.Selected as Unit;
            if (pick == null || pick.IsDown || pick.Team != UnitTeam.Player || !_units.Units.Contains(pick)) pick = NearestAlly(IsoMath.WorldToTileF(_camera.Position));
            if (pick != null) _combat.TryPossess(pick);
        }

        private Unit NearestAlly(Vector2 tile)
        {
            Unit best = null;
            float bd = float.MaxValue;
            foreach (Unit u in _units.Units)
            {
                if (u.Team != UnitTeam.Player || u.IsDown || u.Hidden || !IsFighter(u)) continue;
                float d = Vector2.DistanceSquared(u.Position, tile);
                if (d < bd) { bd = d; best = u; }
            }
            return best;
        }

        private void UpdatePhase(float dt)
        {
            switch (_phase)
            {
                case Phase.Deploy:
                case Phase.Assault:
                    if (_interior.Active) break;
                    if (AliveAttackers == 0) { Lose("Qo'shiningiz yengildi..."); break; }
                    // Saroy eshigiga yetgan jangchi bilan ichkariga: o'zingiz boshqarayotgan bo'lsa — u, aks holda eshikdagisi
                    Unit p = _combat.Possessed;
                    if (p != null)
                    {
                        if (!p.IsDown && Vector2.Distance(p.Position, _palaceDoor) <= 1.3f) EnterPalace(p);
                    }
                    else
                    {
                        foreach (Unit u in _units.Units)
                            if (u.Team == UnitTeam.Player && !u.IsDown && !u.Hidden && IsFighter(u) && Vector2.Distance(u.Position, _palaceDoor) <= 1.3f) { EnterPalace(u); break; }
                    }
                    break;
                case Phase.Captured:
                case Phase.Defeated:
                    _endTimer -= dt;
                    if (_endTimer <= 0f) Finish(_phase == Phase.Captured, false);
                    break;
            }
        }

        private void EnterPalace(Unit u)
        {
            if (_palace == null) return;
            _inside = u;
            _combat.Withdraw(u);       // tashqaridagi AI/boshqaruvdan olinadi (saroy ichida o'yinchi boshqaradi)
            _interior.EnterSiege(_palace, u, _roster, _t.RulerName);
            _wasInside = true;
        }

        /// <summary>Saroydan chiqildi: shoh va vazir yengilgan bo'lsa — qasr olindi.</summary>
        private void LeftPalace()
        {
            Unit u = _inside;
            _inside = null;
            if (u == null) return;
            u.Hidden = false;
            bool won = _shahFoe.Down && _vizierFoe.Down;
            if (won)
            {
                ShahFell();
                Capture(u);
                _interior.EndSiege();
                return;
            }
            if (u.IsDown)
            {
                ShowBanner(u.Name.ToUpperInvariant() + " SAROYDA YIQILDI!  Boshqa jangchi bilan qayta kiring", 4f);
                return;
            }
            _combat.EnlistAttacker(u);
            _combat.TryPossess(u);
            _state.ShowMessage("Saroydan chiqdingiz — shoh va vazir hali tirik!", true);
        }

        /// <summary>Dushman shohi yiqildi: tirik dushman askarlari taslim bo'lib, tomonimizga o'tadi.</summary>
        private void ShahFell()
        {
            _defectors.Clear();
            foreach (Unit u in _units.Units.ToList())
            {
                if (u.Team != UnitTeam.Enemy || u is CitizenUnit || u.IsDown) continue;
                u.Team = UnitTeam.Player;
                _defectors.Add(u);
                _combat.EnlistAttacker(u);
                u.Revive(1f);
            }
        }

        private void Capture(Unit by)
        {
            _phase = Phase.Captured;
            _endTimer = 4f;
            string who = _defectors.Count > 0 ? "  " + _defectors.Count + " ta dushman askari tomoningizga o'tdi!" : "";
            ShowBanner("SHOH VA VAZIR YENGILDI! " + _t.RegionName.ToUpperInvariant() + " QASRI OLINDI!" + who, 4f);
            _state.ShowMessage("Saroy olindi! Qasrga bayrog'ingiz tikilmoqda...");
            by.SetFacing(new Vector2(0, 1));
            _combat.EnlistAttacker(by);
        }

        private void Lose(string why)
        {
            _phase = Phase.Defeated;
            _endTimer = 3f;
            if (_combat.Possessed != null) _combat.Release(false);
            ShowBanner(why.ToUpperInvariant() + "  Qamal muvaffaqiyatsiz.", 4f);
        }

        /// <summary>Sinov: shoh va vazir darhol yiqiladi, qasr olinadi.</summary>
        public void TestKillShah()
        {
            if (!Active) return;
            _shahFoe.Hp = 0;
            _vizierFoe.Hp = 0;
            for (int i = 0; i < _attackers.Count; i++)
                _attackers[i].Position = NearestOpen(_palaceDoor + new Vector2(-2f + (i % 5), 3f + i / 5));
            ShahFell();
            Capture(_attackers[0]);
        }

        /// <summary>Sinov: birinchi jangchi bilan darhol saroy ichiga kirish.</summary>
        public void TestEnterPalace()
        {
            if (!Active || _attackers.Count == 0) return;
            Unit u = _attackers[0];
            u.Position = _palaceDoor;
            EnterPalace(u);
        }

        /// <summary>Esc (hech kimning ichida bo'lmaganda) — chekinish: mag'lubiyat sifatida hisoblanadi.</summary>
        public void Retreat()
        {
            if (!Active) return;
            Finish(false, true);
        }

        /// <summary>Natijani qo'llaydi (hudud, xazina, qo'shin yo'qotishlari, o'tgan askarlar) va qamalni yopadi.</summary>
        private void Finish(bool win, bool retreat)
        {
            if (!Active) return;
            Active = false;
            if (_interior.Active) _interior.Exit();
            _interior.EndSiege();
            ArmyManager mil = _state.Military;
            int spawned = Math.Max(1, _attackers.Count);
            int fallen = _attackers.Count(u => u.IsDown);
            int defTotal = _defenders.Count + _roster.Count;
            int defDown = _defenders.Count(u => u.IsDown) + _roster.Count(f => f.Down);

            // Sahnadagi har bir jangchi qo'shinning (TotalUnits / sahnadagilar) qismini ifodalaydi
            int casualties = (int)MathF.Round(fallen * (_armyAtStart / (float)spawned));
            int dead = mil.RemoveCasualties(Math.Min(mil.TotalUnits - 1, casualties));
            int joined = 0;
            if (win)
                foreach (Unit d in _defectors)
                    if (!d.IsDown) joined += mil.Recruit(TypeOf(d), 1, 1);
            int up = mil.TrainAll(win ? 60 : 30);
            string army = " Halok bo'lganlar: " + dead + "." + (joined > 0 ? " Qo'shiningizga " + joined + " ta asir askar qo'shildi." : "")
                + (up > 0 ? " " + up + " ta jangchi darajasi oshdi." : "");

            if (_t.Online)
            {
                OnlineFinished?.Invoke(new SiegeFinishRequest
                {
                    RegionId = _t.RegionId, Won = win, AttackerFallen = fallen, AttackerTotal = spawned,
                    DefendersDown = defDown, DefendersTotal = Math.Max(1, defTotal)
                });
                _state.ShowMessage((win ? "G'alaba! " + _t.RegionName + " qasri olindi." : retreat ? "Chekindik." : "Mag'lubiyat.") + army, !win);
            }
            else
            {
                int aLoss = (int)MathF.Round(_t.AttackerPower * (fallen / (float)spawned));
                int dLoss = (int)MathF.Round(_t.DefenderPower * Math.Min(1f, defDown / (float)Math.Max(1, defTotal)));
                var res = _state.World.PlayerCampaignResult(_t.RegionId, win ? BattleOutcome.Galaba : BattleOutcome.Maglubiyat, _t.AttackerPower, _t.DefenderPower, aLoss, dLoss);
                if (res != null) _state.Coins += res.CoinChange;
                _state.ShowMessage((win ? "G'alaba! " : retreat ? "Chekindik. " : "Mag'lubiyat. ") + (res?.TerritoryResult ?? "") + army, !win);
            }
            if (win) _state.ShowVizier("Qasr olindi!", "Hazratim, " + _t.RegionName + " endi saltanatimizniki! Shoh " + _t.RulerName + " va vaziri yengildi" + (joined > 0 ? ", " + joined + " ta askari bizga xizmat qilishga ont ichdi." : "."));
            _state.Select(null);
        }

        private void ShowBanner(string text, float seconds)
        {
            _banner = text;
            _bannerTimer = seconds;
        }

        // ==================================================================
        // CHIZISH
        // ==================================================================

        public void Draw(SpriteBatch sb, GraphicsDevice gd, int vw, int vh, float hudScale)
        {
            if (_map == null) return;
            if (_interior.Active)
            {
                gd.Clear(new Color(20, 16, 14));
                _interior.Draw(sb, _assets.Pixel, _font, vw, vh, 0f);
                return;
            }
            gd.Clear(new Color(26, 30, 22));
            Vector4 view = _camera.VisibleWorldRect;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, _camera.Transform);
            _map.Draw(sb, view);
            if (_phase < Phase.Captured)
            {
                // Saroy eshigi — maqsad (miltillovchi oltin halqa)
                float pulse = 0.55f + 0.12f * MathF.Sin(_time * 5f);
                _map.DrawMarker(sb, IsoMath.TileToWorld(_palaceDoor), new Color(255, 214, 90, 230), pulse);
            }
            _combat.DrawGroundMarkers(sb);
            if (_state.Selected is Unit sel && _units.Units.Contains(sel) && sel != _combat.Possessed)
                _map.DrawMarker(sb, IsoMath.TileToWorld(sel.Position), new Color(255, 215, 120, 220));
            _renderer.Draw(sb, view, _map, _buildings, _units);
            sb.End();

            int k = Math.Max(1, (int)Math.Round(hudScale));
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            _combat.DrawHud(sb, _assets.Pixel, _font, _camera, vw, vh, k);
            DrawSiegeHud(sb, vw, vh, k);
            sb.End();
        }

        /// <summary>Pastdagi buyruq paneli: har qism tugmasi (soni bilan) va "HAMMASI". Yuborilganlar — "JANGDA".</summary>
        private void DrawDeployPanel(SpriteBatch sb, int vw, int vh, int k)
        {
            _groupButtons.Clear();
            if (_phase >= Phase.Captured || _groups.Keys.All(_sent.Contains)) return;
            Texture2D px = _assets.Pixel;
            var present = GroupDefs.Where(g => _groups.ContainsKey(g.Key)).ToList();
            int s = 2 * k, bh = 34 * k, gap = 8 * k;
            string title = _phase == Phase.Deploy ? "QAYSI QISM BIRINCHI HUJUMGA BORSIN?" : "KEYINGI QISMNI YUBORING";
            var labels = present.Select((g, i) => (g.Key, Text: (i + 1) + ". " + g.Label + " (" + _groups[g.Key].Count(u => !u.IsDown) + ")" + (_sent.Contains(g.Key) ? " - JANGDA" : ""))).ToList();
            labels.Add(("all", "5. HAMMASI BIRGA"));
            int bs = s;
            int Width(string t, int sc) => (int)_font.Measure(t, sc).X + 24 * k;
            while (bs > k && labels.Sum(l => Width(l.Text, bs)) + gap * (labels.Count - 1) > vw - 40) bs--;
            int total = labels.Sum(l => Width(l.Text, bs)) + gap * (labels.Count - 1);
            int py = vh - bh - 18 * k - (_combat.Possessed != null ? 150 * k : 0);
            int th = PixelFont.GH * s + 10 * k;
            var panel = new Rectangle(vw / 2 - total / 2 - 14 * k, py - th - 10 * k, total + 28 * k, bh + th + 20 * k);
            sb.Draw(px, new Rectangle(panel.X - 2, panel.Y - 2, panel.Width + 4, panel.Height + 4), new Color(216, 167, 83, 230));
            sb.Draw(px, panel, new Color(24, 20, 18, 235));
            _font.DrawCentered(sb, title, new Vector2(vw / 2f, panel.Y + 6 * k + th / 2f), new Color(255, 214, 90), s);
            int x = vw / 2 - total / 2;
            foreach (var l in labels)
            {
                int w = Width(l.Text, bs);
                var r = new Rectangle(x, py, w, bh);
                bool sent = l.Key != "all" && _sent.Contains(l.Key);
                Color fill = sent ? new Color(60, 56, 50) : l.Key == "all" ? new Color(150, 40, 30) : new Color(110, 70, 30);
                sb.Draw(px, new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), sent ? new Color(90, 86, 80) : new Color(236, 196, 110));
                sb.Draw(px, r, fill);
                _font.DrawCentered(sb, l.Text, r.Center.ToVector2(), sent ? new Color(170, 165, 155) : Color.White, bs);
                if (!sent) _groupButtons.Add((r, l.Key));
                x += w + gap;
            }
        }

        private void DrawSiegeHud(SpriteBatch sb, int vw, int vh, int k)
        {
            Texture2D px = _assets.Pixel;
            // Saf turgan qismlar ustida nomi (hali yuborilmaganlar)
            int gi = 0;
            foreach (var g in GroupDefs)
            {
                if (!_groups.TryGetValue(g.Key, out List<Unit> list)) continue;
                gi++;
                if (_sent.Contains(g.Key) || _phase >= Phase.Captured) continue;
                var alive = list.Where(u => !u.IsDown && !u.Hidden).ToList();
                if (alive.Count == 0) continue;
                Vector2 c = Vector2.Zero;
                foreach (Unit u in alive) c += u.Position;
                c /= alive.Count;
                Vector2 sp = _camera.WorldToScreen(IsoMath.TileToWorld(c) + new Vector2(0, -70));
                string lbl = gi + ". " + g.Label;
                Vector2 m = _font.Measure(lbl, 2 * k);
                var lr = new Rectangle((int)(sp.X - m.X / 2 - 6 * k), (int)(sp.Y - m.Y / 2 - 3 * k), (int)m.X + 12 * k, (int)m.Y + 6 * k);
                sb.Draw(px, lr, new Color(24, 20, 18, 210));
                _font.DrawCentered(sb, lbl, lr.Center.ToVector2(), new Color(255, 214, 90), 2 * k);
            }
            string top = "QAMAL: " + _t.RegionName + " (" + _t.FoeName + ")   Qo'shiningiz: " + AliveAttackers + "   Dushman: " + AliveDefenders;
            string goal = _phase < Phase.Captured
                ? "VAZIFA: saroy eshigiga boring (oltin halqa), ichkarida shoh va vazirni yenging  [shoh " + Math.Max(0, _shahFoe.Hp) + "/" + _shahFoe.MaxHp + "]"
                : _phase == Phase.Captured ? "Saroy olindi!" : "Qamal muvaffaqiyatsiz";
            int s = 2 * k;
            int lh = PixelFont.GH * s + 6 * k;
            int wTop = Math.Min(vw - 16, (int)Math.Max(_font.Measure(top, s).X, _font.Measure(goal, s).X) + 28 * k);
            int gs = s;
            while (gs > k && _font.Measure(goal, gs).X > wTop - 20) gs--;
            var r = new Rectangle(vw / 2 - wTop / 2, 6 * k, wTop, lh * 2 + 8 * k);
            sb.Draw(px, new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), new Color(216, 167, 83, 220));
            sb.Draw(px, r, new Color(24, 20, 18, 225));
            _font.DrawCentered(sb, top, new Vector2(vw / 2f, r.Y + 4 * k + lh / 2f), new Color(240, 226, 196), s);
            _font.DrawCentered(sb, goal, new Vector2(vw / 2f, r.Y + 4 * k + lh * 1.5f), new Color(255, 214, 90), gs);

            if (_bannerTimer > 0f && _banner != null)
            {
                float a = Math.Min(1f, _bannerTimer / 0.6f);
                int bs = 3 * k;
                while (bs > k && _font.Measure(_banner, bs).X > vw - 40) bs--;
                Vector2 m = _font.Measure(_banner, bs);
                var br = new Rectangle((int)(vw / 2f - m.X / 2 - 18 * k), (int)(vh * 0.30f), (int)m.X + 36 * k, (int)m.Y + 20 * k);
                Color bg = _phase == Phase.Defeated ? new Color(90, 26, 24) : _phase < Phase.Captured ? new Color(60, 34, 20) : new Color(28, 90, 46);
                sb.Draw(px, new Rectangle(br.X - 2, br.Y - 2, br.Width + 4, br.Height + 4), new Color(240, 200, 110) * a);
                sb.Draw(px, br, bg * (0.92f * a));
                _font.DrawCentered(sb, _banner, br.Center.ToVector2(), Color.White * a, bs);
            }

            if (_state.MessageTimer > 0f && !string.IsNullOrEmpty(_state.Message))
            {
                int ms = 2 * k;
                while (ms > k && _font.Measure(_state.Message, ms).X > vw - 40) ms--;
                Vector2 mm = _font.Measure(_state.Message, ms);
                var mr = new Rectangle((int)(vw / 2f - mm.X / 2 - 10 * k), r.Bottom + 8 * k, (int)mm.X + 20 * k, (int)mm.Y + 10 * k);
                sb.Draw(px, mr, new Color(16, 14, 12, 200));
                _font.DrawCentered(sb, _state.Message, mr.Center.ToVector2(), new Color(255, 236, 190), ms);
            }

            DrawDeployPanel(sb, vw, vh, k);
            // Telefon: "ICHIGA KIRISH" tugmasi (F klavishi yo'q)
            if (_lastTouch && _combat.Possessed == null && _phase < Phase.Captured)
            {
                string pl = "ICHIGA KIRISH";
                int ps = 2 * k;
                Vector2 pm = _font.Measure(pl, ps);
                var pr = new Rectangle(vw - (int)pm.X - 40 * k, vh - (int)pm.Y - 150 * k, (int)pm.X + 28 * k, (int)pm.Y + 24 * k);
                sb.Draw(px, new Rectangle(pr.X - 2, pr.Y - 2, pr.Width + 4, pr.Height + 4), new Color(236, 196, 110));
                sb.Draw(px, pr, new Color(150, 40, 30));
                _font.DrawCentered(sb, pl, pr.Center.ToVector2(), Color.White, ps);
                _groupButtons.Add((pr, "possess"));
            }
            if (_combat.Possessed == null && _phase < Phase.Captured && _groups.Keys.All(_sent.Contains))
            {
                string hint = _lastTouch ? "Jangchiga teging va ICHIGA KIRISH - o'zingiz urishing  |  Orqaga - chekinish"
                    : "Jangchini bosing va F - ichiga kirib o'zingiz urishing  |  Esc - chekinish";
                int hs = 2 * k;
                while (hs > k && _font.Measure(hint, hs).X > vw - 30) hs--;
                Vector2 hm = _font.Measure(hint, hs);
                var hr = new Rectangle((int)(vw / 2f - hm.X / 2 - 10 * k), vh - (int)hm.Y - 26 * k, (int)hm.X + 20 * k, (int)hm.Y + 12 * k);
                sb.Draw(px, hr, new Color(20, 12, 10, 200));
                _font.DrawCentered(sb, hint, hr.Center.ToVector2(), new Color(255, 220, 150), hs);
            }
        }
    }
}
