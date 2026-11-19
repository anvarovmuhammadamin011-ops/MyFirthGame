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
        private enum Phase { Assault, Captured, Defeated }

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

            _phase = Phase.Assault;
            _endTimer = 0f;
            _time = 0f;
            _lastPossessed = null;
            _inside = null;
            _wasInside = false;
            _combat.SiegeObjective = _palaceDoor;

            Vector2 start = _attackers.Count > 0 ? _attackers[0].Position : _palaceDoor;
            _camera = new CameraManager(viewport, IsoMath.TileToWorld(start) + new Vector2(0, -20), 2.0f);
            if (_attackers.Count > 0) _combat.TryPossess(_attackers[0]);
            Active = true;
            ShowBanner("QAMAL: " + t.RegionName.ToUpperInvariant() + "!  Saroyga kirib shoh " + t.RulerName + " va vazirni yenging", 5f);
            _state.ShowMessage("Qo'shiningiz " + t.FoeName + " qasri darvozasiga yetib keldi! Saroyga boring.");
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
            var chosen = mil.Units.OrderByDescending(u => u.Strength).Take(24).ToList();
            int O = GameConfig.CityOffset;
            for (int i = 0; i < chosen.Count; i++)
            {
                // Bosh (janubiy) darvoza tashqarisida, ko'prik va yo'l bo'ylab ikki qator saf
                int col = i % 2, row = i / 2;
                var pos = NearestOpen(new Vector2(O + 31.5f + col, O + 55.5f + row * 0.85f));
                Unit u = Create(chosen[i].UnitType, pos);
                u.Team = UnitTeam.Player;
                u.SetFacing(new Vector2(0, -1));
                _units.Add(u);
                _attackers.Add(u);
                _combat.EnlistAttacker(u);
            }
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
            else if (input != null && _phase == Phase.Assault) HandleInput(input);
            if (_wasInside && !_interior.Active) LeftPalace();
            _wasInside = _interior.Active;

            _combat.SiegeObjective = _palaceDoor;
            _combat.Update(dt, _phase == Phase.Assault && !_interior.Active ? input : null, _camera);

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

        private void HandleInput(InputManager input)
        {
            if (_combat.Possessed != null) return;
            if (input.KeyPressed(Keys.Escape)) { Retreat(); return; }
            if (input.LeftPressed && !input.MouseOverUI)
            {
                Vector2 world = _camera.ScreenToWorld(input.MouseVector);
                if (_renderer.PickAt(world, _map, _buildings, _units) is Unit u && u.Team == UnitTeam.Player && !u.IsDown && IsFighter(u))
                    _state.Select(u);
            }
            if (input.KeyPressed(Keys.F) || input.KeyPressed(Keys.Space))
            {
                Unit pick = _state.Selected as Unit;
                if (pick == null || pick.IsDown || pick.Team != UnitTeam.Player || !_units.Units.Contains(pick)) pick = NearestAlly(IsoMath.WorldToTileF(_camera.Position));
                if (pick != null) _combat.TryPossess(pick);
            }
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
            if (_phase == Phase.Assault)
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

        private void DrawSiegeHud(SpriteBatch sb, int vw, int vh, int k)
        {
            Texture2D px = _assets.Pixel;
            string top = "QAMAL: " + _t.RegionName + " (" + _t.FoeName + ")   Qo'shiningiz: " + AliveAttackers + "   Dushman: " + AliveDefenders;
            string goal = _phase == Phase.Assault
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
                Color bg = _phase == Phase.Defeated ? new Color(90, 26, 24) : _phase == Phase.Assault ? new Color(60, 34, 20) : new Color(28, 90, 46);
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

            if (_combat.Possessed == null && _phase == Phase.Assault)
            {
                string hint = "Jangchini bosing va F - ichiga kirib o'zingiz urishing  |  Esc - chekinish";
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
