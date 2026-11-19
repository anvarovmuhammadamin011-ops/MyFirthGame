using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Military;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.World
{
    /// <summary>
    /// QO'SHIN YO'LGA CHIQADI: boshqa davlatga yurish boshlanganda o'yinchining o'z shahrida jangchilar qo'shinxonadan
    /// (bo'lmasa saroydan) qismlar bo'yicha — otliqlar, piyodalar, nayzachilar, kamonchilar — ketma-ket chiqib,
    /// ko'cha bo'ylab janubiy darvozadan shahar tashqarisiga yurib ketadi. Kamera ularga ergashadi.
    /// Bu faqat ko'rinish: haqiqiy qo'shin (ArmyManager) o'zgarmaydi, figuralar darvozadan chiqqach yo'qoladi.
    /// </summary>
    public sealed class MarchOutParade
    {
        private sealed class Marcher
        {
            public Unit Unit;
            public List<Vector2> Path;
            public int Index;
            public float Delay;
            public float Speed;
            public string Group;
            public bool Done;
        }

        private static readonly (string Key, string Label, string One)[] Groups =
            { ("otliq", "OTLIQLAR", "otliq"), ("piyoda", "PIYODALAR", "piyoda"), ("nayza", "NAYZACHILAR", "nayzachi"), ("kamon", "KAMONCHILAR", "kamonchi") };

        private readonly List<Marcher> _marchers = new List<Marcher>();
        private UnitManager _units;
        private float _time;

        public bool Active { get; private set; }
        /// <summary>Yuqorida ko'rinadigan matn: kim, qayerdan, qayerga.</summary>
        public string Summary { get; private set; } = "";
        /// <summary>O'yinchi kamerani o'zi surdi — ergashish to'xtaydi.</summary>
        public bool CameraReleased { get; set; }
        private string _title = "", _detail = "";

        public static string GroupOf(UnitType t) => t switch
        {
            UnitType.Otliq or UnitType.ElitaOtliq => "otliq",
            UnitType.Kamonchi => "kamon",
            UnitType.Nayzador or UnitType.SaroyQoriqchisi => "nayza",
            _ => "piyoda"
        };

        /// <summary>Paradni boshlaydi. Natija: boshlanish nuqtasi (kamera uchun) yoki null.</summary>
        public Vector2? Start(MapManager map, BuildingManager buildings, UnitManager units, ArmyManager mil, string targetName)
        {
            Clear();
            if (mil == null || mil.TotalUnits == 0) return null;
            _units = units;
            Building from = buildings.Buildings.FirstOrDefault(b => b.Type == BuildingType.Barracks && b.BuildProgress >= 1f)
                         ?? buildings.Buildings.FirstOrDefault(b => b.Type == BuildingType.MainHall);
            if (from == null) return null;
            Point? start = Entrance(map, from);
            int O = GameConfig.CityOffset;
            if (!start.HasValue) return null;
            // Janubiy darvoza tashqarisi (ko'prikdan o'tib bo'lmasa — darvoza oldi)
            List<Point> path = null;
            foreach (int gy in new[] { 58, 52, 50 })
            {
                Point? goal = NearestWalkable(map, new Point(O + 31, O + gy));
                if (!goal.HasValue) continue;
                path = Pathfinder.FindPath(map, start.Value, goal.Value);
                if (path != null && path.Count > 0) break;
            }
            if (path == null || path.Count == 0) return null;
            var pts = path.Select(p => new Vector2(p.X + 0.5f, p.Y + 0.5f)).ToList();

            // Har qismdan proporsional vakillar (jami 18 tagacha) — qismlar ketma-ket chiqadi
            var counts = Groups.ToDictionary(g => g.Key, g => mil.Units.Count(u => GroupOf(u.UnitType) == g.Key));
            int total = counts.Values.Sum();
            int cap = Math.Min(18, total);
            float delay = 0f;
            foreach (var g in Groups)
            {
                int n = counts[g.Key];
                if (n == 0) continue;
                int show = Math.Max(1, (int)Math.Round(n * cap / (float)total));
                for (int i = 0; i < show; i++)
                {
                    Unit u = Create(g.Key, pts[0]);
                    u.Team = UnitTeam.Player;
                    u.ExternallyControlled = true;
                    u.Hidden = true; // navbati kelguncha binoning ichida
                    units.Add(u);
                    _marchers.Add(new Marcher { Unit = u, Path = pts, Delay = delay, Speed = g.Key == "otliq" ? 2.3f : 1.7f, Group = g.Key });
                    delay += g.Key == "otliq" ? 0.45f : 0.6f;
                }
                delay += 1.2f; // qismlar orasida tanaffus
            }
            string parts = string.Join(" · ", Groups.Where(g => counts[g.Key] > 0).Select(g => counts[g.Key] + " " + g.One));
            Summary = "Qo'shin " + targetName + " tomon yo'lga chiqdi: " + parts + "  (" + from.Name + "dan janubiy darvoza orqali)";
            _title = "QO'SHIN " + targetName.ToUpperInvariant() + " TOMON YO'LGA CHIQDI";
            _detail = parts + "  ·  " + from.Name + "dan janubiy darvoza orqali";
            _time = 0f;
            CameraReleased = false;
            Active = _marchers.Count > 0;
            return Active ? pts[0] : (Vector2?)null;
        }

        private static Unit Create(string group, Vector2 pos)
        {
            Vector2[] here = { pos };
            switch (group)
            {
                case "otliq": return new CavalryUnit(here);
                case "nayza": return new SpearmanUnit(pos, Direction.South);
                case "kamon": { var a = new PatrolUnit(here); a.MakeArcher(); return a; }
                default: return new SwordsmanUnit(here);
            }
        }

        public void Update(float dt)
        {
            if (!Active || dt <= 0f) return;
            _time += dt;
            foreach (Marcher m in _marchers)
            {
                if (m.Done) continue;
                if (m.Delay > 0f) { m.Delay -= dt; continue; }
                m.Unit.Hidden = false;
                float step = m.Speed * dt;
                while (step > 0f && m.Index < m.Path.Count)
                {
                    Vector2 to = m.Path[m.Index] - m.Unit.Position;
                    float d = to.Length();
                    if (d <= step) { m.Unit.Position = m.Path[m.Index]; m.Index++; step -= d; continue; }
                    Vector2 dir = to / d;
                    m.Unit.Position += dir * step;
                    m.Unit.DriveMove(dir, m.Speed);
                    step = 0f;
                }
                if (m.Index >= m.Path.Count)
                {
                    m.Done = true;
                    _units?.Remove(m.Unit); // darvozadan chiqib ketdi
                }
            }
            if (_marchers.All(m => m.Done) || _time > 90f) Clear();
        }

        /// <summary>Yurayotgan figuralar o'rtasi (kamera ergashishi uchun).</summary>
        public bool Focus(out Vector2 at)
        {
            at = Vector2.Zero;
            int n = 0;
            foreach (Marcher m in _marchers)
                if (!m.Done && !m.Unit.Hidden) { at += m.Unit.Position; n++; }
            if (n == 0) return false;
            at /= n;
            return true;
        }

        public void Clear()
        {
            foreach (Marcher m in _marchers) if (!m.Done) _units?.Remove(m.Unit);
            _marchers.Clear();
            Active = false;
        }

        /// <summary>Ekran qatlami: tepada yo'nalish matni, har qism boshlovchisi ustida qism nomi.</summary>
        public void DrawHud(SpriteBatch sb, Texture2D px, PixelFont font, CameraManager cam, int vw, int vh, int k)
        {
            if (!Active) return;
            // Tor ekranda shrift kichraytirilmaydi — qatorlarga bo'linadi
            int ts = AndijonSaltanatlar.UI.HudText.Scale(font, _title, 3 * k, 2 * k, vw - 60);
            var tl = AndijonSaltanatlar.UI.HudText.Wrap(font, _title, ts, vw - 60, 2);
            var dl = AndijonSaltanatlar.UI.HudText.Wrap(font, _detail, 2 * k, vw - 60, 3);
            var lines = new List<string>(tl);
            lines.AddRange(dl);
            Rectangle r = AndijonSaltanatlar.UI.HudText.Box(sb, px, font, tl, ts, vw / 2f, 54 * k, new Color(24, 20, 18, 230), new Color(255, 214, 90), 16 * k, 6 * k, new Color(216, 167, 83, 230));
            Rectangle r2 = AndijonSaltanatlar.UI.HudText.Box(sb, px, font, dl, 2 * k, vw / 2f, r.Bottom, new Color(24, 20, 18, 230), new Color(236, 224, 200), 16 * k, 5 * k);
            int bannerBottom = Math.Max(r.Bottom, r2.Bottom);

            foreach (var g in Groups)
            {
                Marcher lead = _marchers.FirstOrDefault(x => x.Group == g.Key && !x.Done && !x.Unit.Hidden);
                if (lead == null) continue;
                Vector2 sp = cam.WorldToScreen(IsoMath.TileToWorld(lead.Unit.Position) + new Vector2(0, -62));
                if (sp.Y < bannerBottom + 12) continue; // yozuv tepadagi yo'nalish qutisiga tushmasin
                Vector2 lm = font.Measure(g.Label, 2 * k);
                var lr = new Rectangle((int)(sp.X - lm.X / 2 - 6 * k), (int)(sp.Y - lm.Y / 2 - 3 * k), (int)lm.X + 12 * k, (int)lm.Y + 6 * k);
                sb.Draw(px, lr, new Color(24, 20, 18, 210));
                font.DrawCentered(sb, g.Label, lr.Center.ToVector2(), new Color(255, 214, 90), 2 * k);
            }
        }

        private static Point? Entrance(MapManager map, Building b)
        {
            Point e = b.FrontEntranceTile;
            if (map.IsWalkable(e.X, e.Y)) return e;
            for (int r = 1; r <= 3; r++)
                for (int y = b.Position.Y - r; y < b.Position.Y + b.Size.Y + r; y++)
                    for (int x = b.Position.X - r; x < b.Position.X + b.Size.X + r; x++)
                    {
                        bool inside = x >= b.Position.X && x < b.Position.X + b.Size.X && y >= b.Position.Y && y < b.Position.Y + b.Size.Y;
                        if (!inside && map.IsWalkable(x, y)) return new Point(x, y);
                    }
            return null;
        }

        private static Point? NearestWalkable(MapManager map, Point p)
        {
            for (int r = 0; r <= 5; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                        if (map.IsWalkable(p.X + dx, p.Y + dy)) return new Point(p.X + dx, p.Y + dy);
            return null;
        }
    }
}
