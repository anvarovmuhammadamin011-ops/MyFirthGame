using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Shared.War;
using AndijonSaltanatlar.UI.Design;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AndijonSaltanatlar.UI
{
    /// <summary>
    /// TABS uslubidagi REAL VAQT jang (fantastikasiz, tarixiy jangchilar):
    ///  1) JOYLASHTIRISH — o'z qo'shiningiz chap tomonda saf tortadi; pastdagi tugmalardan tur tanlab, maydonning
    ///     chap qismiga bosib qo'shimcha yollanma jangchi qo'yasiz (tanga).
    ///  2) JANG — har bir jangchi alohida: eng yaqin dushmanga yuguradi, qilich/nayza bilan uradi, kamonchi o'q,
    ///     katapulta tosh otadi (tosh tushgan joydagi hamma zarar ko'radi). Nayzachi otliqqa qarshi kuchli,
    ///     otliq birinchi zarbada bostirib kiradi.
    ///  3) ICHIGA KIRISH — F: sichqoncha yonidagi jangchingizga kirasiz; WASD yurish, Probel/sichqoncha — zarba, Esc — chiqish.
    ///  4) NATIJA — bir tomon qirilsa (yoki vaqt tugasa) tugaydi; omon qolgan kuch ulushi ArenaController'ga beriladi.
    /// </summary>
    public sealed class TabsBattle
    {
        public enum Phase { Deploy, Fight, Done }
        public enum Action { None, FightStarted, Close }
        public enum Kind { Piyoda, Nayzachi, Kamonchi, Otliq, Katapulta }

        private sealed class Fighter
        {
            public bool Left;
            public Kind K;
            public int Level;
            public string Key;
            public Vector2 P;
            public float Hp, MaxHp, Dmg, Range, Speed, Cd, CdT;
            public Fighter Target;
            public float Retarget;
            public bool Dead;
            public float DeadT;
            public float Anim, Lunge, Hurt;
            public bool FaceLeft, Charged, Merc;
            public Color Tint;
            public bool Ranged => K == Kind.Kamonchi || K == Kind.Katapulta;
        }

        private sealed class Shot
        {
            public Vector2 From, To;
            public float T, Dur, Arc, Dmg;
            public bool Stone, Left;
            public Fighter Target;
        }

        private sealed class Puff { public Vector2 P, V; public float Life, Max; public Color C; }

        private const int N = 16;                  // maydon N×N izometrik katak
        private const float TimeLimit = 150f;
        private const int MaxFiguresPerSide = 36;

        private readonly AssetManager _assets;
        private readonly GameStateManager _state;
        private readonly List<Fighter> _all = new List<Fighter>();
        private readonly List<Shot> _shots = new List<Shot>();
        private readonly List<Puff> _puffs = new List<Puff>();
        private readonly Random _rng = new Random();
        private Texture2D _catReady, _catFired, _dot;

        private Color _mineColor, _foeColor;
        private string _mineName = "", _foeName = "";
        private float _foeHpBonus;
        private float _time, _doneT;
        private Kind _deployKind = Kind.Piyoda;
        private Fighter _possessed;

        // Ekran → maydon o'tkazish parametrlari (har kadr hisoblanadi)
        private Vector2 _c;
        private float _t = 1f;
        private Rectangle _field;

        public Phase Stage { get; private set; } = Phase.Deploy;
        public bool Finished => Stage == Phase.Done;
        /// <summary>1 — o'yinchi, 2 — raqib, 0 — durang.</summary>
        public int Winner { get; private set; }
        public float MineSurvival => Survival(true);
        public float FoeSurvival => Survival(false);

        private static readonly (Kind K, string Name, int Cost)[] Tray =
        {
            (Kind.Piyoda, "Piyoda", 15), (Kind.Nayzachi, "Nayzachi", 20), (Kind.Kamonchi, "Kamonchi", 25),
            (Kind.Otliq, "Otliq", 45), (Kind.Katapulta, "Katapulta", 120),
        };

        public TabsBattle(AssetManager assets, GameStateManager state)
        {
            _assets = assets;
            _state = state;
        }

        // ================================================================== Sozlash
        public void Setup(List<UnitGroupDto> mine, List<UnitGroupDto> foe, Color mineColor, Color foeColor, string mineName, string foeName, float foeHpBonus)
        {
            _all.Clear(); _shots.Clear(); _puffs.Clear();
            _possessed = null;
            _mineColor = mineColor; _foeColor = foeColor;
            _mineName = mineName ?? ""; _foeName = foeName ?? "";
            _foeHpBonus = Math.Max(0f, foeHpBonus);
            _time = 0f; _doneT = 0f; Winner = 0;
            Stage = Phase.Deploy;
            EnsureArt();
            Deploy(mine, true);
            Deploy(foe, false);
        }

        private void EnsureArt()
        {
            GraphicsDevice gd = _assets.GraphicsDevice;
            _catReady ??= CatapultArt(false).ToTexture(gd);
            _catFired ??= CatapultArt(true).ToTexture(gd);
            if (_dot == null)
            {
                var c = new Canvas(24, 12);
                c.FillEllipse(12, 6, 11.5f, 5.5f, Color.White);
                _dot = c.ToTexture(gd);
            }
        }

        private static Kind KindOf(UnitLine line, int level)
        {
            switch (line)
            {
                case UnitLine.Otliq: return Kind.Otliq;
                case UnitLine.Masofaviy: return Kind.Kamonchi;
                case UnitLine.Aravasi: return Kind.Katapulta;
                default: return level <= 1 ? Kind.Nayzachi : Kind.Piyoda;
            }
        }

        /// <summary>Qo'shin guruhlarini safga tizadi: oldinda yaqin jang, orqada kamonchi, eng orqada katapulta.</summary>
        private void Deploy(List<UnitGroupDto> groups, bool left)
        {
            var kinds = new List<(Kind k, int lvl)>();
            int total = groups.Where(g => g.Line != UnitLine.Lager).Sum(g => Math.Max(0, g.Count));
            int per = Math.Max(1, (int)Math.Ceiling(total / (double)MaxFiguresPerSide));
            foreach (UnitGroupDto g in groups)
            {
                if (g.Line == UnitLine.Lager || g.Count <= 0) continue;
                int figs = Math.Max(1, (int)Math.Round(g.Count / (double)per));
                for (int i = 0; i < figs; i++) kinds.Add((KindOf(g.Line, g.Level), Math.Clamp(g.Level, 1, 3)));
            }
            int Order(Kind k) => k == Kind.Otliq || k == Kind.Nayzachi || k == Kind.Piyoda ? 0 : k == Kind.Kamonchi ? 1 : 2;
            var rows = kinds.GroupBy(x => Order(x.k)).OrderBy(g => g.Key).ToList();
            float a = 4.6f;
            foreach (var row in rows)
            {
                var list = row.ToList();
                for (int start = 0; start < list.Count; start += 10)
                {
                    var line = list.Skip(start).Take(10).ToList();
                    float aa = left ? -a : a;
                    float lo = Math.Max(1f - aa, 1f + aa) + 0.6f, hi = Math.Min(2 * N - 1f + aa, 2 * N - 1f - aa) - 0.6f;
                    float mid = N, spread = Math.Min((hi - lo) / 2f, 7.5f);
                    for (int i = 0; i < line.Count; i++)
                    {
                        float u = line.Count == 1 ? 0.5f : i / (float)(line.Count - 1);
                        float b = mid - spread + u * spread * 2f + ((float)_rng.NextDouble() - 0.5f) * 0.3f;
                        Add(line[i].k, line[i].lvl, left, new Vector2((aa + b) / 2f, (b - aa) / 2f), false);
                    }
                    a += 1.25f;
                }
                a += 0.6f;
            }
        }

        private Fighter Add(Kind k, int lvl, bool left, Vector2 p, bool merc)
        {
            float m = 1f + 0.2f * (lvl - 1);
            var f = new Fighter { K = k, Level = lvl, Left = left, P = Clamp(p), Merc = merc, CdT = (float)_rng.NextDouble() * 0.8f };
            switch (k)
            {
                case Kind.Piyoda: f.MaxHp = 60; f.Dmg = 9; f.Range = 0.9f; f.Speed = 1.6f; f.Cd = 1.0f; f.Key = lvl >= 3 ? SpriteKeys.UnitShield : SpriteKeys.UnitSwordsman; break;
                case Kind.Nayzachi: f.MaxHp = 70; f.Dmg = 11; f.Range = 1.25f; f.Speed = 1.45f; f.Cd = 1.15f; f.Key = SpriteKeys.UnitSpearman; break;
                case Kind.Kamonchi: f.MaxHp = 40; f.Dmg = 8; f.Range = 6.5f; f.Speed = 1.4f; f.Cd = 1.6f; f.Key = SpriteKeys.UnitPatrol; break;
                case Kind.Otliq: f.MaxHp = 120; f.Dmg = 14; f.Range = 1.0f; f.Speed = 3.0f; f.Cd = 1.1f; f.Key = SpriteKeys.UnitCavalry; break;
                default: f.MaxHp = 110; f.Dmg = 26; f.Range = 10.5f; f.Speed = 0.6f; f.Cd = 4.0f; f.Key = null; break;
            }
            f.MaxHp *= m * (left ? 1f : 1f + _foeHpBonus);
            f.Dmg *= m;
            f.Hp = f.MaxHp;
            f.FaceLeft = !left;
            Color team = left ? _mineColor : _foeColor;
            Color baseC = k == Kind.Otliq ? new Color(255, 236, 200) : k == Kind.Kamonchi ? new Color(210, 240, 210) : Color.White;
            f.Tint = Color.Lerp(baseC, team, 0.22f);
            _all.Add(f);
            return f;
        }

        private static Vector2 Clamp(Vector2 p) => new Vector2(Math.Clamp(p.X, 0.4f, N - 0.4f), Math.Clamp(p.Y, 0.4f, N - 0.4f));

        private float Survival(bool left)
        {
            float hp = 0f, max = 0f;
            foreach (Fighter f in _all) if (f.Left == left) { max += f.MaxHp; if (!f.Dead) hp += Math.Max(0f, f.Hp); }
            return max <= 0f ? 0f : hp / max;
        }

        private int Alive(bool left) { int n = 0; foreach (Fighter f in _all) if (f.Left == left && !f.Dead) n++; return n; }

        /// <summary>Jangni boshlaydi (sinov uchun ham). possess — birinchi tirik jangchingiz ichiga kirish.</summary>
        public void BeginFight(bool possess)
        {
            if (Stage != Phase.Deploy) return;
            Stage = Phase.Fight;
            _time = 0f;
            if (possess) _possessed = _all.FirstOrDefault(f => f.Left && !f.Dead && f.K != Kind.Katapulta);
        }

        /// <summary>Esc: jangchi ichida bo'lsa — chiqadi (true). Aks holda false (ArenaController sahnani yopadi).</summary>
        public bool ReleasePossession()
        {
            if (_possessed == null) return false;
            _possessed = null;
            return true;
        }

        // ================================================================== Izometrik o'tkazish
        private Vector2 G(Vector2 p) => _c + new Vector2((p.X - p.Y) * 32f * _t, (p.X + p.Y + 1 - N) * 16f * _t);

        private Vector2 FromScreen(Point m)
        {
            Vector2 d = new Vector2(m.X, m.Y) - _c;
            float a = d.X / (32f * _t), b = d.Y / (16f * _t) - 1 + N;
            return new Vector2((a + b) / 2f, (b - a) / 2f);
        }

        private static bool InField(Vector2 p) => p.X >= 0.3f && p.Y >= 0.3f && p.X <= N - 0.3f && p.Y <= N - 0.3f;
        private static bool InMyZone(Vector2 p) => InField(p) && p.X - p.Y <= -3f;

        // ================================================================== Simulyatsiya
        private void Step(float dt, InputManager input)
        {
            _time += dt;
            foreach (Fighter f in _all)
            {
                if (f.Dead) { f.DeadT += dt; continue; }
                f.CdT -= dt;
                f.Hurt = Math.Max(0f, f.Hurt - dt);
                f.Lunge = Math.Max(0f, f.Lunge - dt);
                if (f == _possessed) { StepPossessed(f, dt, input); continue; }
                StepAi(f, dt);
            }
            StepShots(dt);
            StepPuffs(dt);

            if (Stage == Phase.Fight)
            {
                int ml = Alive(true), fl = Alive(false);
                bool over = ml == 0 || fl == 0 || _time >= TimeLimit;
                if (over)
                {
                    _doneT += dt;
                    if (_doneT > 1.4f || _time >= TimeLimit)
                    {
                        if (fl == 0 && ml > 0) Winner = 1;
                        else if (ml == 0 && fl > 0) Winner = 2;
                        else { float d = MineSurvival - FoeSurvival; Winner = d > 0.1f ? 1 : d < -0.1f ? 2 : 0; }
                        Stage = Phase.Done;
                        _possessed = null;
                    }
                }
            }
        }

        private Fighter NearestEnemy(Fighter f, float maxDist = float.MaxValue)
        {
            Fighter best = null; float bd = maxDist * maxDist;
            foreach (Fighter o in _all)
            {
                if (o.Dead || o.Left == f.Left) continue;
                float d = Vector2.DistanceSquared(o.P, f.P);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        private void StepAi(Fighter f, float dt)
        {
            f.Retarget -= dt;
            if (f.Target == null || f.Target.Dead || f.Retarget <= 0f) { f.Target = NearestEnemy(f); f.Retarget = 0.5f + (float)_rng.NextDouble() * 0.3f; }
            Fighter t = f.Target;
            if (t == null) return;
            Vector2 to = t.P - f.P;
            float d = to.Length();
            Face(f, to);
            if (d <= f.Range)
            {
                f.Anim = 0f;
                if (f.CdT <= 0f) Attack(f, t, 1f);
                return;
            }
            Vector2 dir = d > 0.001f ? to / d : Vector2.Zero;
            Move(f, dir, f.Speed, dt);
        }

        private void StepPossessed(Fighter f, float dt, InputManager input)
        {
            Vector2 screen = Vector2.Zero;
            if (input != null)
            {
                if (input.KeyDown(Keys.A) || input.KeyDown(Keys.Left)) screen.X -= 1;
                if (input.KeyDown(Keys.D) || input.KeyDown(Keys.Right)) screen.X += 1;
                if (input.KeyDown(Keys.W) || input.KeyDown(Keys.Up)) screen.Y -= 1;
                if (input.KeyDown(Keys.S) || input.KeyDown(Keys.Down)) screen.Y += 1;
            }
            if (screen != Vector2.Zero)
            {
                // ekran yo'nalishi → maydon yo'nalishi
                var dir = new Vector2(screen.X / 32f + screen.Y / 16f, screen.Y / 16f - screen.X / 32f);
                dir.Normalize();
                Move(f, dir, f.Speed * 1.15f, dt);
                Face(f, dir);
            }
            else f.Anim = 0f;
            if (input != null && input.KeyPressed(Keys.Space)) ManualAttack(f);
        }

        private void ManualAttack(Fighter f)
        {
            if (f.CdT > 0f) return;
            Fighter t = NearestEnemy(f, f.Range + (f.Ranged ? 0f : 0.45f));
            if (t == null) { f.Lunge = 0.18f; f.CdT = f.Cd * 0.5f; return; } // havoga siltash
            Face(f, t.P - f.P);
            Attack(f, t, 1.5f);   // qahramon zarbasi
        }

        private void Face(Fighter f, Vector2 dirField)
        {
            float sx = dirField.X - dirField.Y;
            if (MathF.Abs(sx) > 0.02f) f.FaceLeft = sx < 0;
        }

        private void Move(Fighter f, Vector2 dir, float speed, float dt)
        {
            // Ittifoqchilardan ajralish (to'da bo'lib bir nuqtaga yig'ilmasin)
            Vector2 sep = Vector2.Zero;
            foreach (Fighter o in _all)
            {
                if (o == f || o.Dead) continue;
                Vector2 dd = f.P - o.P;
                float l = dd.Length();
                float min = o.Left == f.Left ? 0.5f : 0.38f;
                if (l > 0.0001f && l < min) sep += dd / l * (min - l);
            }
            f.P = Clamp(f.P + dir * speed * dt + sep * 0.8f);
            f.Anim += dt * speed * 3.2f;
        }

        private void Attack(Fighter f, Fighter t, float mult)
        {
            f.CdT = f.Cd * (0.85f + (float)_rng.NextDouble() * 0.3f);
            f.Lunge = 0.22f;
            if (f.K == Kind.Kamonchi || f.K == Kind.Katapulta)
            {
                bool stone = f.K == Kind.Katapulta;
                float dist = Vector2.Distance(f.P, t.P);
                _shots.Add(new Shot
                {
                    From = f.P, To = t.P + (stone ? new Vector2((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f) * 0.8f : Vector2.Zero),
                    Dur = stone ? 1.1f + dist * 0.05f : 0.35f + dist * 0.045f, Arc = stone ? 3.4f : 1.1f + dist * 0.12f,
                    Dmg = f.Dmg * mult, Stone = stone, Left = f.Left, Target = t,
                });
                return;
            }
            float dmg = f.Dmg * mult * (0.8f + (float)_rng.NextDouble() * 0.4f);
            if (f.K == Kind.Nayzachi && t.K == Kind.Otliq) dmg *= 1.8f;             // nayza otliqni to'xtatadi
            if (f.K == Kind.Otliq && !f.Charged) { dmg *= 2f; f.Charged = true; }    // bostirib kirish
            Hit(t, dmg, f.P);
        }

        private void Hit(Fighter t, float dmg, Vector2 from)
        {
            if (t.Dead) return;
            t.Hp -= dmg;
            t.Hurt = 0.25f;
            Vector2 push = t.P - from;
            if (push.LengthSquared() > 0.0001f) t.P = Clamp(t.P + Vector2.Normalize(push) * 0.12f);
            for (int i = 0; i < 3; i++) Spawn(t.P, new Color(214, 196, 160), 0.35f);
            if (t.Hp <= 0f)
            {
                t.Dead = true; t.DeadT = 0f;
                for (int i = 0; i < 6; i++) Spawn(t.P, new Color(190, 170, 130), 0.6f);
                if (t == _possessed) _possessed = null;
            }
        }

        private void StepShots(float dt)
        {
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                Shot s = _shots[i];
                s.T += dt / s.Dur;
                if (s.T < 1f) continue;
                _shots.RemoveAt(i);
                if (s.Stone)
                {
                    foreach (Fighter f in _all)
                        if (!f.Dead && f.Left != s.Left && Vector2.Distance(f.P, s.To) < 1.3f)
                            Hit(f, s.Dmg * (1f - Vector2.Distance(f.P, s.To) / 2.2f), s.To);
                    for (int k = 0; k < 14; k++) Spawn(s.To, new Color(170, 150, 120), 0.8f);
                }
                else
                {
                    Fighter t = s.Target != null && !s.Target.Dead && Vector2.Distance(s.Target.P, s.To) < 1.2f ? s.Target : null;
                    if (t == null)
                        foreach (Fighter f in _all)
                            if (!f.Dead && f.Left != s.Left && Vector2.Distance(f.P, s.To) < 0.6f) { t = f; break; }
                    if (t != null) Hit(t, s.Dmg, s.From);
                }
            }
        }

        private void Spawn(Vector2 p, Color c, float life)
        {
            if (_puffs.Count > 260) return;
            var v = new Vector2((float)_rng.NextDouble() - 0.5f, -(float)_rng.NextDouble()) * 40f;
            _puffs.Add(new Puff { P = G(p) + new Vector2(0, -6f * _t), V = v, Life = life, Max = life, C = c });
        }

        private void StepPuffs(float dt)
        {
            for (int i = _puffs.Count - 1; i >= 0; i--)
            {
                Puff p = _puffs[i];
                p.Life -= dt;
                if (p.Life <= 0f) { _puffs.RemoveAt(i); continue; }
                p.P += p.V * dt;
                p.V.Y += 60f * dt;
            }
        }

        // ================================================================== Chizish + kiritish
        public Action Draw(UiKit ui, SpriteBatch sb, InputManager input, int vw, int vh, float dt, string resultText)
        {
            Action act = Action.None;
            ThemeTokens T = ui.T;
            int topH = (int)(ui.Type.LineHeight(TextStyle.PanelTitle) * 2f + 22);
            int botH = (int)(ui.ControlH + 22);
            _field = new Rectangle(0, topH, vw, vh - topH - botH);
            _t = Math.Min(vw * 0.94f / (64f * N), _field.Height * 0.86f / (32f * N));
            _c = new Vector2(vw / 2f, _field.Y + _field.Height * 0.56f - 16f * _t);

            // --- Kiritish va simulyatsiya ---
            if (Stage == Phase.Fight)
            {
                if (input != null && input.KeyPressed(Keys.F)) PossessNearMouse(input.MousePosition);
                float rem = Math.Min(dt, 0.1f);
                while (rem > 0f) { float s = Math.Min(rem, 0.033f); Step(s, input); rem -= s; }
            }
            else if (Stage == Phase.Done) StepPuffs(dt);

            // --- Fon ---
            DrawBackdrop(ui, vw, vh);
            DrawGround(sb);
            if (Stage == Phase.Deploy) DrawZones(sb);

            // --- Jangchilar (avval yiqilganlar, keyin chuqurlik bo'yicha) ---
            foreach (Fighter f in _all.Where(x => x.Dead).OrderBy(x => x.P.X + x.P.Y)) DrawFighter(sb, f);
            foreach (Fighter f in _all.Where(x => !x.Dead).OrderBy(x => x.P.X + x.P.Y)) DrawFighter(sb, f);
            DrawShots(sb);
            foreach (Puff p in _puffs)
                sb.Draw(_assets.Pixel, new Rectangle((int)p.P.X, (int)p.P.Y, Math.Max(2, (int)(3 * _t)), Math.Max(2, (int)(3 * _t))), p.C * (p.Life / p.Max));

            // --- Joylashtirish: bosilgan joyga jangchi qo'yish ---
            if (Stage == Phase.Deploy)
            {
                Vector2 hp = FromScreen(input.MousePosition);
                bool okZone = InMyZone(hp) && _field.Contains(input.MousePosition);
                if (_field.Contains(input.MousePosition))
                    sb.Draw(_dot, G(hp), null, (okZone ? new Color(120, 230, 120) : new Color(230, 90, 80)) * 0.55f, 0f, new Vector2(12, 6), _t * 1.2f, SpriteEffects.None, 0f);
                if (ui.Clicked(_field) && okZone) PlaceMerc(hp);
            }

            // --- UI ---
            DrawHeader(ui, vw, topH);
            if (Stage == Phase.Deploy) act = DrawDeployBar(ui, vw, vh, botH);
            else if (Stage == Phase.Fight) act = DrawFightBar(ui, vw, vh, botH);
            else act = DrawResult(ui, vw, vh, resultText);
            return act;
        }

        private void PlaceMerc(Vector2 p)
        {
            var t = Tray.First(x => x.K == _deployKind);
            if (_state.Coins < t.Cost) { _state.ShowMessage("Yollash uchun tanga yetarli emas", true); return; }
            if (_all.Count(f => f.Left) >= MaxFiguresPerSide + 24) { _state.ShowMessage("Maydon to'ldi", true); return; }
            _state.Coins -= t.Cost;
            Add(_deployKind, 1, true, p, true);
        }

        private void PossessNearMouse(Point m)
        {
            Vector2 mp = FromScreen(m);
            Fighter best = null; float bd = float.MaxValue;
            foreach (Fighter f in _all)
            {
                if (f.Dead || !f.Left) continue;
                float d = Vector2.DistanceSquared(f.P, mp);
                if (d < bd) { bd = d; best = f; }
            }
            if (best == null) return;
            _possessed = best == _possessed ? null : best;
        }

        private void DrawBackdrop(UiKit ui, int vw, int vh)
        {
            int h = _field.Y + _field.Height;
            for (int i = 0; i < 12; i++)
            {
                float u = i / 11f;
                Color c = Color.Lerp(new Color(52, 66, 104), new Color(214, 150, 104), u);
                ui.Fill(new Rectangle(0, _field.Y + (int)(u * _field.Height * 0.45f), vw, (int)(_field.Height * 0.45f / 11f) + 2), c);
            }
            ui.Fill(new Rectangle(0, _field.Y + (int)(_field.Height * 0.45f), vw, h - _field.Y - (int)(_field.Height * 0.45f)), new Color(78, 104, 64));
        }

        private void DrawGround(SpriteBatch sb)
        {
            Texture2D atlas = _assets.Get(SpriteKeys.Tiles);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int row = ((x * 7 + y * 13) % 6 == 0) ? 0 : 3;
                    var pos = _c + new Vector2((x - y) * 32f * _t + TileArt.DrawOffsetX * _t, (x + y + 1 - N) * 16f * _t + TileArt.DrawOffsetY * _t);
                    sb.Draw(atlas, pos, TileArt.Cell(row, (x + y) % TileArt.Columns), Color.White, 0f, Vector2.Zero, _t, SpriteEffects.None, 0f);
                }
        }

        private void DrawZones(SpriteBatch sb)
        {
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float a = x - y;
                    Color c = a <= -3 ? new Color(90, 150, 255) : a >= 3 ? new Color(230, 90, 70) : Color.Transparent;
                    if (c == Color.Transparent) continue;
                    sb.Draw(_dot, G(new Vector2(x + 0.5f, y + 0.5f)), null, c * 0.16f, 0f, new Vector2(12, 6), _t * 2.6f, SpriteEffects.None, 0f);
                }
        }

        private void DrawFighter(SpriteBatch sb, Fighter f)
        {
            Vector2 g = G(f.P);
            Color team = f.Left ? _mineColor : _foeColor;
            float alpha = f.Dead ? Math.Max(0.35f, 1f - f.DeadT / 8f) : 1f;
            if (!f.Dead) sb.Draw(_dot, g, null, team * 0.55f, 0f, new Vector2(12, 6), _t * 0.75f, SpriteEffects.None, 0f);
            if (f == _possessed)
                sb.Draw(_dot, g, null, new Color(255, 214, 90) * (0.6f + 0.3f * MathF.Sin(_time * 8f)), 0f, new Vector2(12, 6), _t * 1.4f, SpriteEffects.None, 0f);

            // Zarba paytida dushman tomon intilish
            Vector2 lunge = Vector2.Zero;
            if (f.Lunge > 0f && f.Target != null)
            {
                Vector2 dd = G(f.Target.P) - g;
                if (dd.LengthSquared() > 1f) lunge = Vector2.Normalize(dd) * (f.Lunge / 0.22f) * 5f * _t;
            }
            SpriteEffects fx = f.FaceLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Color tint = f.Hurt > 0f ? Color.Lerp(f.Tint, new Color(255, 110, 100), f.Hurt / 0.25f) : f.Tint;
            if (f.Dead) tint = Color.Lerp(tint, new Color(120, 112, 104), 0.5f);
            float rot = f.Dead ? Math.Min(1f, f.DeadT / 0.35f) * (f.FaceLeft ? 1.45f : -1.45f) : 0f;

            if (f.K == Kind.Katapulta)
            {
                Texture2D ct = f.CdT > f.Cd - 0.6f ? _catFired : _catReady;
                sb.Draw(ct, g + lunge * 0.3f, null, tint * alpha, rot * 0.3f, new Vector2(ct.Width / 2f, ct.Height - 3), _t * 1.05f, fx, 0f);
            }
            else
            {
                int frame = !f.Dead && f.Anim > 0f ? 1 + ((int)f.Anim % 2) : 0;
                Texture2D tex = _assets.TryGet(f.Key + "_" + frame) ?? _assets.TryGet(f.Key + "_0");
                if (tex == null) return;
                float k = AssetManager.ScaleOf(tex) * 0.95f * _t;
                var origin = new Vector2(tex.Width / 2f, tex.Height - AssetManager.FootOf(tex));
                sb.Draw(tex, g + lunge, null, tint * alpha, rot, origin, k, fx, 0f);
            }

            if (!f.Dead && f.Hp < f.MaxHp)
            {
                int w = (int)(18 * _t), y = (int)(g.Y - 46 * _t);
                var bg = new Rectangle((int)g.X - w / 2, y, w, Math.Max(2, (int)(3 * _t)));
                sb.Draw(_assets.Pixel, bg, new Color(20, 16, 12, 200));
                sb.Draw(_assets.Pixel, new Rectangle(bg.X, bg.Y, (int)(w * Math.Max(0f, f.Hp / f.MaxHp)), bg.Height), team);
            }
            f.Anim = f.Dead ? 0f : f.Anim;
        }

        private void DrawShots(SpriteBatch sb)
        {
            foreach (Shot s in _shots)
            {
                float u = Math.Clamp(s.T, 0f, 1f);
                Vector2 a = G(s.From), b = G(s.To);
                Vector2 pos = Vector2.Lerp(a, b, u) + new Vector2(0, -(4f * u * (1f - u)) * s.Arc * 16f * _t - 12f * _t);
                if (s.Stone)
                {
                    int r = Math.Max(3, (int)(4 * _t));
                    sb.Draw(_dot, Vector2.Lerp(a, b, u), null, Color.Black * 0.25f, 0f, new Vector2(12, 6), _t * 0.5f, SpriteEffects.None, 0f);
                    sb.Draw(_assets.Pixel, new Rectangle((int)pos.X - r / 2, (int)pos.Y - r / 2, r, r), new Color(96, 90, 84));
                }
                else
                {
                    float u2 = Math.Min(1f, u + 0.04f);
                    Vector2 next = Vector2.Lerp(a, b, u2) + new Vector2(0, -(4f * u2 * (1f - u2)) * s.Arc * 16f * _t - 12f * _t);
                    Vector2 d = next - pos;
                    float ang = MathF.Atan2(d.Y, d.X);
                    sb.Draw(_assets.Pixel, pos, null, new Color(70, 52, 34), ang, new Vector2(0, 0.5f), new Vector2(8f * _t, Math.Max(1f, _t)), SpriteEffects.None, 0f);
                }
            }
        }

        private void DrawHeader(UiKit ui, int vw, int topH)
        {
            ThemeTokens T = ui.T;
            ui.Fill(new Rectangle(0, 0, vw, topH), new Color(24, 20, 16, 235));
            ui.Fill(new Rectangle(0, topH - 2, vw, 2), T.Border);
            float lh = ui.Type.LineHeight(TextStyle.PanelTitle);
            ui.Text(_mineName + " (SIZ)", new Vector2(16, 8), TextStyle.PanelTitle, _mineColor);
            ui.Text("Jangchilar: " + Alive(true) + "   Kuch: " + (int)(MineSurvival * 100) + "%", new Vector2(16, 12 + lh), TextStyle.Secondary, T.TextSecondary);
            string fn = _foeName;
            ui.Type.DrawRight(ui.Batch, fn, new Vector2(vw - 16, 8), TextStyle.PanelTitle, _foeColor);
            ui.Type.DrawRight(ui.Batch, "Jangchilar: " + Alive(false) + "   Kuch: " + (int)(FoeSurvival * 100) + "%", new Vector2(vw - 16, 12 + lh), TextStyle.Secondary, T.TextSecondary);
            string mid = Stage == Phase.Deploy ? "JOYLASHTIRISH" : Stage == Phase.Fight ? "JANG  " + (int)Math.Max(0, TimeLimit - _time) + " s" : "JANG TUGADI";
            Vector2 m = ui.Type.Measure(mid, TextStyle.PanelTitle);
            ui.Text(mid, new Vector2(vw / 2f - m.X / 2f, 10), TextStyle.PanelTitle, T.Accent);
        }

        private Action DrawDeployBar(UiKit ui, int vw, int vh, int botH)
        {
            ThemeTokens T = ui.T;
            var bar = new Rectangle(0, vh - botH, vw, botH);
            ui.Fill(bar, new Color(24, 20, 16, 240));
            int pad = 8, gap = ui.Gap, h = ui.ControlH;
            int goW = Math.Max(150, (int)ui.Type.Measure("JANG BOSHLASH!", TextStyle.Button).X + 40);
            int n = Tray.Length;
            int bw = (vw - pad * 2 - goW - gap * n) / n;
            int y = bar.Y + (botH - h) / 2;
            for (int i = 0; i < n; i++)
            {
                var t = Tray[i];
                var r = new Rectangle(pad + i * (bw + gap), y, bw, h);
                bool sel = _deployKind == t.K;
                bool afford = _state.Coins >= t.Cost;
                if (ui.Button(r, t.Name + " " + t.Cost, sel ? ButtonKind.Primary : ButtonKind.Secondary, afford || sel,
                    "Tanlang, so'ng maydonning ko'k (chap) qismiga bosing — yollanma jangchi qo'yiladi"))
                    _deployKind = t.K;
            }
            if (ui.Button(new Rectangle(vw - pad - goW, y, goW, h), "JANG BOSHLASH!", ButtonKind.Danger, Alive(true) > 0, "Jangni boshlash"))
            {
                Stage = Phase.Fight;
                _time = 0f;
                return Action.FightStarted;
            }
            return Action.None;
        }

        private Action DrawFightBar(UiKit ui, int vw, int vh, int botH)
        {
            ThemeTokens T = ui.T;
            var bar = new Rectangle(0, vh - botH, vw, botH);
            ui.Fill(bar, new Color(24, 20, 16, 240));
            int h = ui.ControlH;
            int bw = (int)ui.Type.Measure("CHEKINISH", TextStyle.Button).X + 40;
            string hint = _possessed != null
                ? "SIZ jangdasiz: WASD — yurish, PROBEL — zarba, F — boshqa jangchi, Esc — chiqish"
                : "F — sichqoncha yonidagi jangchingiz ichiga kirib, o'zingiz urishing";
            ui.TextFit(hint, new Rectangle(16, bar.Y + (botH - (int)ui.Type.LineHeight(TextStyle.Body)) / 2, vw - bw - 48, (int)ui.Type.LineHeight(TextStyle.Body)), TextStyle.Body,
                _possessed != null ? T.Accent : T.TextPrimary);
            if (ui.Button(new Rectangle(vw - bw - 8, bar.Y + (botH - h) / 2, bw, h), "CHEKINISH", ButtonKind.Secondary, true, "Jangdan chekinish"))
                return Action.Close;
            return Action.None;
        }

        private Action DrawResult(UiKit ui, int vw, int vh, string resultText)
        {
            ThemeTokens T = ui.T;
            int w = Math.Min(vw - 40, 620), h = 250;
            var r = new Rectangle((vw - w) / 2, (vh - h) / 2, w, h);
            ui.Panel(r);
            string title = Winner == 1 ? "G'ALABA!" : Winner == 2 ? "MAG'LUBIYAT" : "DURANG";
            Color tc = Winner == 1 ? T.Success : Winner == 2 ? T.Danger : T.Accent;
            Vector2 m = ui.Type.Measure(title, TextStyle.ScreenTitle);
            ui.Text(title, new Vector2(r.Center.X - m.X / 2f, r.Y + 16), TextStyle.ScreenTitle, tc);
            int ty = r.Y + 24 + (int)m.Y;
            ui.Paragraph("Omon qolgan kuch — siz: " + (int)(MineSurvival * 100) + "%, raqib: " + (int)(FoeSurvival * 100) + "%",
                new Rectangle(r.X + 20, ty, w - 40, 24), TextStyle.Secondary, T.TextSecondary);
            if (!string.IsNullOrEmpty(resultText))
                ui.Paragraph(resultText, new Rectangle(r.X + 20, ty + 28, w - 40, h - (ty + 28 - r.Y) - ui.ControlH - 24), TextStyle.Body, T.TextPrimary, 4);
            if (ui.Button(new Rectangle(r.X + 20, r.Bottom - ui.ControlH - 14, w - 40, ui.ControlH), "DAVOM ETISH", ButtonKind.Primary))
                return Action.Close;
            return Action.None;
        }

        /// <summary>Yog'och katapulta (o'ngga qarab): g'ildirakli asos, A-ramka, tosh solingan richag.</summary>
        private static Canvas CatapultArt(bool fired)
        {
            var c = new Canvas(40, 34);
            Color wood = new Color(146, 100, 58), dark = new Color(96, 64, 36), ink = new Color(36, 28, 24);
            c.FillEllipse(20, 31.5f, 16, 2.2f, new Color(0, 0, 0, 60));
            c.FillRect(5, 22, 30, 5, wood);
            c.FillRect(5, 26, 30, 1, dark);
            c.FillEllipse(10, 28, 3.6f, 3.6f, dark); c.FillEllipse(30, 28, 3.6f, 3.6f, dark);
            c.FillEllipse(10, 28, 1.2f, 1.2f, ink); c.FillEllipse(30, 28, 1.2f, 1.2f, ink);
            c.Line(new Vector2(12, 22), new Vector2(20, 11), dark, 2);
            c.Line(new Vector2(28, 22), new Vector2(20, 11), dark, 2);
            Vector2 pivot = new Vector2(20, 12);
            Vector2 end = fired ? new Vector2(31, 2) : new Vector2(6, 17);
            c.Line(pivot + (pivot - end) * 0.3f, end, wood, 2);
            c.FillEllipse(end.X, end.Y, 3f, 2.2f, dark);
            if (!fired) c.FillEllipse(end.X, end.Y - 1.5f, 2.2f, 2.2f, new Color(120, 114, 106));
            c.FillRect((int)(pivot.X + (pivot.X - end.X) * 0.3f) - 2, (int)(pivot.Y + (pivot.Y - end.Y) * 0.3f) - 1, 4, 4, new Color(90, 86, 80)); // qarshi yuk
            c.Outline(ink, 0.4f);
            return c;
        }
    }
}
