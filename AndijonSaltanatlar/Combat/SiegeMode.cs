using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Battles;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Military;
using AndijonSaltanatlar.Units;
using AndijonSaltanatlar.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AndijonSaltanatlar.Combat
{
    /// <summary>
    /// QAMAL — boshqa davlat hududiga yurish. Qo'shiningiz dushmanning O'Z SHAHRIGA boradi: shahar (binolar,
    /// ko'chalar, aholi, soqchilar) yaratiladi, jangchilaringiz janubiy darvoza oldida saf tortadi va saroy tomon
    /// yuradi. Dushman soqchilari yaqinlashganda jangga kiradi, aholi qo'ng'iroq bilan uylarga yashirinadi.
    ///  - Istalgan jangchingiz ichiga kirib (F) o'zingiz urishasiz.
    ///  - Dushman SHOHI saroy eshigida turadi — uni o'ldirsangiz, tirik qolgan dushman askarlari tomoningizga o'tadi.
    ///  - Shundan keyin SAROYGA KIRSANGIZ — hudud sizniki bo'ladi.
    ///  - Qo'shiningiz butunlay yiqilsa yoki Esc bilan chekinsangiz — mag'lubiyat.
    /// </summary>
    public sealed class SiegeMode
    {
        private enum Phase { Assault, ShahDown, Captured, Defeated }

        private readonly AssetManager _assets;
        private readonly GameStateManager _state;
        private readonly PixelFont _font;

        private MapManager _map;
        private BuildingManager _buildings;
        private UnitManager _units;
        private CombatSystem _combat;
        private WorldRenderer _renderer;
        private CameraManager _camera;

        private readonly List<Unit> _attackers = new List<Unit>();
        private readonly List<Unit> _defenders = new List<Unit>();
        private readonly List<Unit> _defectors = new List<Unit>();
        private ShahUnit _enemyShah;
        private Vector2 _palaceDoor;
        private Phase _phase;
        private float _endTimer;
        private float _time;
        private string _banner;
        private float _bannerTimer;
        private Unit _lastPossessed;

        private int _attackerPower, _defenderPower, _armyAtStart;
        private string _regionName = "", _foeName = "", _rulerName = "";

        public bool Active { get; private set; }
        public int RegionId { get; private set; } = -1;
        /// <summary>Sinov uchun: hozirgi bosqich nomi.</summary>
        public string PhaseName => _phase.ToString();
        public CombatSystem Combat => _combat;
        public ShahUnit EnemyShah => _enemyShah;
        public IReadOnlyList<Unit> Attackers => _attackers;
        public int AliveAttackers => _units?.Units.Count(u => u.Team == UnitTeam.Player && IsFighter(u) && !u.IsDown) ?? 0;
        public int AliveDefenders => _units?.Units.Count(u => u.Team == UnitTeam.Enemy && IsFighter(u) && !u.IsDown) ?? 0;

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

        /// <summary>Yurish yetib keldi: dushman shahrini yaratib, qamalni boshlaydi. Qo'shin bo'lmasa false.</summary>
        public bool Start(int regionId, Viewport viewport)
        {
            var W = _state.World;
            if (regionId < 0)
            {
                // Sinov: birinchi dushman hududi
                foreach (var rr in W.Regions.Regions)
                    if (rr.OwnerKingdomId != 0 && rr.OwnerKingdomId != W.Kingdoms.PlayerKingdomId) { regionId = rr.Id; break; }
            }
            var r = W.Regions.Get(regionId);
            if (r == null) return false;
            var def = W.Kingdoms.Get(r.OwnerKingdomId);
            if (def == null || def.Id == W.Kingdoms.PlayerKingdomId) return false;
            ArmyManager mil = _state.Military;
            if (mil.TotalUnits == 0) { _state.ShowMessage("Yurish uchun qo'shin yo'q — avval jangchi sotib oling", true); return false; }

            RegionId = regionId;
            _regionName = r.Name;
            _foeName = def.Name;
            _rulerName = string.IsNullOrWhiteSpace(def.RulerName) ? "Hukmdor" : def.RulerName;
            _attackerPower = Math.Max(20, mil.TotalStrength);
            _defenderPower = Math.Max(20, def.MilitaryStrength);
            _armyAtStart = mil.TotalUnits;

            // --- Dushman shahri (har hudud o'z urug'i bilan) ---
            _map = new MapManager(_assets, GameConfig.MapSize, GameConfig.MapSize);
            _buildings = new BuildingManager(_map, _assets);
            _units = new UnitManager(_assets);
            CityGenerator.Generate(_map, _buildings, _units, 7919 * (regionId + 1));
            CitizenSpawner.Spawn(_map, _units);
            PatrolController.RouteAll(_map, _units);
            _combat = new CombatSystem(_map, _units, _state) { Buildings = _buildings, SiegeMode = true };
            _renderer = new WorldRenderer(_assets, _font);

            Building palace = _buildings.Buildings.FirstOrDefault(b => b.Type == BuildingType.MainHall);
            Point door = palace != null ? palace.FrontEntranceTile : new Point(GameConfig.CityCenter, GameConfig.CityCenter - 6);
            _palaceDoor = new Vector2(door.X + 0.5f, door.Y + 0.6f);

            SetupDefenders(r.IsCapital);
            SetupAttackers(mil);

            _phase = Phase.Assault;
            _endTimer = 0f;
            _time = 0f;
            _lastPossessed = null;
            _combat.SiegeObjective = _enemyShah.Position;

            Vector2 start = _attackers.Count > 0 ? _attackers[0].Position : _palaceDoor;
            _camera = new CameraManager(viewport, IsoMath.TileToWorld(start) + new Vector2(0, -20), 2.0f);
            if (_attackers.Count > 0) _combat.TryPossess(_attackers[0]);
            Active = true;
            ShowBanner("QAMAL: " + _regionName.ToUpperInvariant() + "!  Dushman shohi " + _rulerName + " saroy eshigida", 5f);
            _state.ShowMessage("Qo'shiningiz " + _foeName + " shahri darvozasiga yetib keldi! Shoh " + _rulerName + "ni yenging va saroyga kiring.");
            return true;
        }

        private void SetupDefenders(bool capital)
        {
            _defenders.Clear();
            var rng = new Random(RegionId * 31 + 7);
            // Dushman kuchi: o'yinchi qo'shiniga nisbatan (juda kuchsiz yoki juda kuchli bo'lsa ham o'ynaladigan chegarada)
            float ratio = Math.Clamp(_defenderPower / (float)Math.Max(1, _attackerPower), 0.45f, 1.6f);
            int attackers = Math.Clamp(_armyAtStart, 1, 24);
            int want = Math.Clamp((int)MathF.Round(attackers * ratio * 0.9f) + (capital ? 2 : 0), 3, 16);

            var soldiers = _units.Units.Where(u => IsFighter(u)).ToList();
            // Saroyga eng yaqinlari qoladi (saroy soqchilari doim bor), qolganlardan tasodifiy
            soldiers.Sort((a, b) => Vector2.Distance(a.Position, _palaceDoor).CompareTo(Vector2.Distance(b.Position, _palaceDoor)));
            var keep = new List<Unit>();
            for (int i = 0; i < soldiers.Count && keep.Count < 2; i++) keep.Add(soldiers[i]);
            var rest = soldiers.Skip(keep.Count).OrderBy(_ => rng.Next()).ToList();
            foreach (Unit u in rest) if (keep.Count < want) keep.Add(u);
            foreach (Unit u in soldiers) if (!keep.Contains(u)) _units.Remove(u);

            foreach (Unit u in _units.Units) u.Team = UnitTeam.Enemy; // aholi ham — dushman shahri
            _defenders.AddRange(keep);

            // Dushman shohi saroy eshigi oldida
            int hp = 240 + (capital ? 120 : 0) + Math.Min(200, _defenderPower / 4);
            _enemyShah = _units.Add(new ShahUnit(_palaceDoor + new Vector2(0f, 0.6f), _rulerName, hp));
            _enemyShah.SetFacing(new Vector2(0, 1));
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
                var pos = new Vector2(O + 31.5f + col, O + 55.5f + row * 0.85f);
                pos = NearestOpen(pos);
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

        /// <param name="input">null — kiritish yo'q (panel ochiq).</param>
        public void Update(float dt, InputManager input, Viewport viewport)
        {
            if (!Active) return;
            _time += dt;
            if (_bannerTimer > 0f) _bannerTimer -= dt;

            if (input != null && _phase < Phase.Captured) HandleInput(input);

            _combat.SiegeObjective = _phase == Phase.Assault && !_enemyShah.IsDown ? _enemyShah.Position : _palaceDoor;
            _combat.Update(dt, _phase < Phase.Captured ? input : null, _camera);

            float wdt = dt * _combat.TimeScale;
            _units.Update(wdt);
            _units.ApplyPhysics(_map, _combat.Possessed, wdt);
            _renderer.Tick(dt);
            _map.Update(dt);

            // Kamera: ichiga kirganda jangchiga ergashadi, aks holda erkin (WASD/sichqoncha)
            Unit p = _combat.Possessed;
            if (p != null && _lastPossessed == null) _camera.SetTargetZoom(2.2f);
            _lastPossessed = p;
            _camera.KeyboardPanEnabled = p == null;
            if (input != null && p == null && (input.IsPanDragging || input.KeyDown(Keys.W) || input.KeyDown(Keys.A) || input.KeyDown(Keys.S) || input.KeyDown(Keys.D)
                || input.KeyDown(Keys.Up) || input.KeyDown(Keys.Down) || input.KeyDown(Keys.Left) || input.KeyDown(Keys.Right)))
                _manualCam = 4f; // o'yinchi kamerani o'zi surdi — bir oz ergashmaymiz
            else if (_manualCam > 0f) _manualCam -= dt;
            if (p != null) _camera.Follow(IsoMath.TileToWorld(p.Position) + new Vector2(0f, -20f), dt);
            else if (_manualCam <= 0f && ArmyFront(out Vector2 front)) _camera.Follow(IsoMath.TileToWorld(front) + new Vector2(0f, -20f), dt, 2.5f);
            if (input != null) _camera.Update(dt, input, viewport);

            UpdatePhase(dt);
        }

        private float _manualCam;

        /// <summary>Qo'shin o'rtasi (tirik jangchilar), kuzatuv kamerasi uchun.</summary>
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
            Unit p = _combat.Possessed;
            if (p == null)
            {
                if (input.KeyPressed(Keys.Escape)) { Retreat(); return; }
                // Jangchini bosib tanlash
                if (input.LeftPressed && !input.MouseOverUI)
                {
                    Vector2 world = _camera.ScreenToWorld(input.MouseVector);
                    if (_renderer.PickAt(world, _map, _buildings, _units) is Unit u && u.Team == UnitTeam.Player && !u.IsDown && IsFighter(u))
                        _state.Select(u);
                }
                if (input.KeyPressed(Keys.F) || input.KeyPressed(Keys.Space))
                {
                    Unit pick = _state.Selected as Unit;
                    if (pick == null || pick.IsDown || pick.Team != UnitTeam.Player || !_units.Units.Contains(pick)) pick = NearestAlly(_camera.Position);
                    if (pick != null) _combat.TryPossess(pick);
                }
            }
        }

        private Unit NearestAlly(Vector2 world)
        {
            Vector2 tile = IsoMath.WorldToTileF(world);
            Unit best = null;
            float bd = float.MaxValue;
            foreach (Unit u in _units.Units)
            {
                if (u.Team != UnitTeam.Player || u.IsDown || !IsFighter(u)) continue;
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
                    if (_enemyShah.IsDown) ShahFell();
                    else if (AliveAttackers == 0) Lose("Qo'shiningiz yengildi...");
                    break;
                case Phase.ShahDown:
                    if (AliveAttackers == 0) { Lose("Qo'shiningiz yengildi..."); break; }
                    foreach (Unit u in _units.Units)
                    {
                        if (u.Team != UnitTeam.Player || u.IsDown || !IsFighter(u)) continue;
                        if (Vector2.Distance(u.Position, _palaceDoor) <= 1.4f) { Capture(u); break; }
                    }
                    break;
                case Phase.Captured:
                case Phase.Defeated:
                    _endTimer -= dt;
                    if (_endTimer <= 0f) Finish(_phase == Phase.Captured, false);
                    break;
            }
        }

        /// <summary>Dushman shohi yiqildi: tirik dushman askarlari taslim bo'lib, tomonimizga o'tadi.</summary>
        private void ShahFell()
        {
            _phase = Phase.ShahDown;
            _defectors.Clear();
            foreach (Unit u in _units.Units.ToList())
            {
                if (u.Team != UnitTeam.Enemy || u is CitizenUnit || u is ShahUnit || u.IsDown) continue;
                u.Team = UnitTeam.Player;
                _defectors.Add(u);
                _combat.EnlistAttacker(u);
                u.Revive(1f); // to'liq sog'ayib safga qo'shiladi
            }
            string who = _defectors.Count > 0 ? " " + _defectors.Count + " ta dushman askari tomoningizga o'tdi!" : "";
            ShowBanner("SHOH " + _rulerName.ToUpperInvariant() + " HALOK BO'LDI!" + who + " Endi SAROYGA KIRING!", 6f);
            _state.ShowMessage("Dushman shohi yiqildi!" + who + " Saroy eshigiga boring.");
        }

        private void Capture(Unit by)
        {
            _phase = Phase.Captured;
            _endTimer = 3.5f;
            by.Hidden = true; // saroyga kirdi
            if (_combat.Possessed == by) _combat.Release(false);
            ShowBanner("SAROY OLINDI! " + _regionName.ToUpperInvariant() + " ENDI SIZNIKI!", 4f);
        }

        private void Lose(string why)
        {
            _phase = Phase.Defeated;
            _endTimer = 3f;
            if (_combat.Possessed != null) _combat.Release(false);
            ShowBanner(why.ToUpperInvariant() + "  Qamal muvaffaqiyatsiz.", 4f);
        }

        /// <summary>Sinov: dushman shohi darhol yiqiladi.</summary>
        public void TestKillShah()
        {
            if (!Active || _enemyShah == null) return;
            _enemyShah.HitPoints = 0;
            // Saroy eshigi yaqinidagi hujumchilar (kamera ko'rishi uchun)
            for (int i = 0; i < _attackers.Count; i++)
                _attackers[i].Position = NearestOpen(_palaceDoor + new Vector2(-2f + (i % 5), 4f + i / 5));
            ShahFell();
        }

        /// <summary>Esc (hech kimning ichida bo'lmaganda) — chekinish: mag'lubiyat sifatida hisoblanadi.</summary>
        public void Retreat()
        {
            if (!Active) return;
            Finish(false, true);
        }

        /// <summary>Natijani dunyoga qo'llaydi (hudud, xazina, qo'shin yo'qotishlari, o'tgan askarlar) va qamalni yopadi.</summary>
        private void Finish(bool win, bool retreat)
        {
            if (!Active) return;
            Active = false;
            ArmyManager mil = _state.Military;
            int spawned = Math.Max(1, _attackers.Count);
            int fallen = _attackers.Count(u => u.IsDown);
            float share = fallen / (float)spawned;
            int aLoss = (int)MathF.Round(_attackerPower * share);
            int defTotal = Math.Max(1, _defenders.Count);
            int defDown = _defenders.Count(u => u.IsDown) + (_enemyShah.IsDown ? 1 : 0);
            int dLoss = (int)MathF.Round(_defenderPower * Math.Min(1f, defDown / (float)defTotal));

            var outcome = win ? BattleOutcome.Galaba : BattleOutcome.Maglubiyat;
            var res = _state.World.PlayerCampaignResult(RegionId, outcome, _attackerPower, _defenderPower, aLoss, dLoss);
            if (res != null) _state.Coins += res.CoinChange;

            // Sahnadagi har bir jangchi qo'shinning (TotalUnits / sahnadagilar) qismini ifodalaydi
            int casualties = (int)MathF.Round(fallen * (_armyAtStart / (float)spawned));
            int dead = mil.RemoveCasualties(Math.Min(mil.TotalUnits - 1, casualties));
            int joined = 0;
            if (win)
                foreach (Unit d in _defectors)
                    if (!d.IsDown) joined += mil.Recruit(TypeOf(d), 1, 1);
            int up = mil.TrainAll(win ? 60 : 30);

            string text = (win ? "G'alaba! " : retreat ? "Chekindik. " : "Mag'lubiyat. ") + (res?.TerritoryResult ?? "")
                + " Halok bo'lganlar: " + dead + "." + (joined > 0 ? " Qo'shiningizga " + joined + " ta asir askar qo'shildi." : "")
                + (up > 0 ? " " + up + " ta jangchi darajasi oshdi." : "");
            _state.ShowMessage(text, !win);
            if (win) _state.ShowVizier("Hudud qo'shib olindi!", "Hazratim, " + _regionName + " endi saltanatimizniki! Shoh " + _rulerName + " yengildi" + (joined > 0 ? ", " + joined + " ta askari bizga xizmat qilishga ont ichdi." : "."));
            _state.Select(null);
            RegionId = -1;
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
            gd.Clear(new Color(26, 30, 22));
            Vector4 view = _camera.VisibleWorldRect;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, _camera.Transform);
            _map.Draw(sb, view);
            if (_phase == Phase.ShahDown)
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
            // Yuqori panel: hudud, kuchlar, vazifa
            string top = "QAMAL: " + _regionName + " (" + _foeName + ")   Qo'shiningiz: " + AliveAttackers + "   Dushman: " + AliveDefenders;
            string goal = _phase == Phase.Assault
                ? "VAZIFA: Shoh " + _rulerName + "ni yenging  (" + Math.Max(0, _enemyShah.HitPoints) + "/" + _enemyShah.MaxHitPoints + ")"
                : _phase == Phase.ShahDown ? "VAZIFA: Saroyga kiring! (oltin halqa — saroy eshigi)"
                : _phase == Phase.Captured ? "Saroy olindi!" : "Qamal muvaffaqiyatsiz";
            int s = 2 * k;
            int lh = PixelFont.GH * s + 6 * k;
            int w = Math.Min(vw - 16, (int)Math.Max(_font.Measure(top, s).X, _font.Measure(goal, s).X) + 28 * k);
            var r = new Rectangle(vw / 2 - w / 2, 6 * k, w, lh * 2 + 8 * k);
            sb.Draw(px, new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), new Color(216, 167, 83, 220));
            sb.Draw(px, r, new Color(24, 20, 18, 225));
            _font.DrawCentered(sb, top, new Vector2(vw / 2f, r.Y + 4 * k + lh / 2f), new Color(240, 226, 196), s);
            _font.DrawCentered(sb, goal, new Vector2(vw / 2f, r.Y + 4 * k + lh * 1.5f), _phase == Phase.ShahDown ? new Color(255, 214, 90) : new Color(255, 170, 140), s);

            // Katta banner (shoh o'ldi, saroy olindi, ...)
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

            // Xabar (o'yin xabarlari shu yerda ham ko'rinadi)
            if (_state.MessageTimer > 0f && !string.IsNullOrEmpty(_state.Message))
            {
                int ms = 2 * k;
                while (ms > k && _font.Measure(_state.Message, ms).X > vw - 40) ms--;
                Vector2 mm = _font.Measure(_state.Message, ms);
                var mr = new Rectangle((int)(vw / 2f - mm.X / 2 - 10 * k), r.Bottom + 8 * k, (int)mm.X + 20 * k, (int)mm.Y + 10 * k);
                sb.Draw(px, mr, new Color(16, 14, 12, 200));
                _font.DrawCentered(sb, _state.Message, mr.Center.ToVector2(), new Color(255, 236, 190), ms);
            }

            if (_combat.Possessed == null && _phase < Phase.Captured)
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
