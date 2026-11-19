using System;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.Core.Graphics
{
    /// <summary>
    /// Har qanday binoning yuqori daraja ko'rinishi (asosiy rasm ustiga qo'shiladi). Bino 5-darajagacha kattalashadi,
    /// va bir xil binolar bir xil ko'rinmasligi uchun 4 xil USLUB bor (bino joyiga qarab tanlanadi):
    ///   0 — egizak minoralar (feruza), 1 — burchaklarda gumbazli shiyponlar (ko'k),
    ///   2 — bitta baland qorovul minorasi va bayroq (g'isht/oltin), 3 — koshinli ingichka minoralar (yashil).
    /// Darajalar: 2 — chiroqlar; 3 — bayroqlar/tug'lar; 4 — poydevor va uslub inshootlari; 5 — oltin bezaklar.
    /// </summary>
    public static class LevelArt
    {
        private static readonly Color Outline = new Color(44, 28, 20);
        private static readonly Color Green = new Color(70, 150, 96);

        private static readonly Color[] Trims = { Pal.Turquoise, Pal.Blue, Pal.Gold, Green };
        private static readonly Color[] Stones = { Pal.Stone, Pal.BrickPale, Pal.Plaster, Pal.Mud };
        private static readonly Color[] Flags = { Pal.Red, Pal.Blue, Pal.Gold, Green };
        private static readonly Color[] Lamps = { new Color(255, 214, 120), new Color(255, 180, 100), new Color(255, 236, 170), new Color(255, 200, 140) };

        public static Canvas Compose(Canvas baseArt, int size, int level, int style = 0)
        {
            if (level <= 1 || baseArt == null) return baseArt;
            style = ((style % 4) + 4) % 4;
            Color trim = Trims[style], flag = Flags[style];
            int tileH = size * GameConfig.TileHeight;
            int baseH = Math.Max(0, baseArt.Height - tileH);
            bool big = size >= 2;
            // Poydevor har darajada o'sadi — bino har upgrade'da ko'rinarli kattalashadi (2-darajadan boshlab).
            int podium = big ? (level >= 5 ? 12 : level >= 4 ? 9 : level >= 3 ? 6 : level >= 2 ? 3 : 0) : 0;
            float towerH = big && level >= 4 ? Math.Max(baseH * (style == 2 ? 1.05f : 0.85f), 46f) + (level >= 5 ? 16f : 6f) : 0f;
            if (style == 1) towerH *= 0.55f;   // shiyponlar pastroq
            float top = Math.Max(baseH, towerH + (level >= 5 ? 26f : 12f)) + podium;
            int H = (int)Math.Ceiling(top) + (level >= 3 ? 24 : 10);
            int dy = H - podium - baseH;

            // 1) Poydevor (4-5 daraja) — uslubga qarab tosh/g'isht/marmar/loy
            var back = new IsoCanvas(size, H);
            if (podium > 0)
            {
                Color stone = level >= 5 ? Canvas.Mix(Stones[style], Pal.Plaster, 0.5f) : Stones[style];
                Color edge = level >= 5 ? Pal.Gold : trim;
                back.Box(0.02f, 0.02f, size - 0.02f, size - 0.02f, 0, podium, stone, style == 1 ? 3 : 0, Canvas.Shade(stone, 1.06f));
                back.QuadSW(0.02f, size - 0.02f, size - 0.02f, podium - 2, podium, edge);
                back.QuadSE(size - 0.02f, 0.02f, size - 0.02f, podium - 2, podium, Canvas.Shade(edge, 0.78f));
                back.C.Outline(Outline);
            }
            Canvas result = back.C;
            Blit(result, baseArt, 0, dy);

            // 2) Old bezaklar
            var front = new IsoCanvas(size, H);
            float z0 = podium;
            if (!big)
            {
                if (level >= 4)
                {
                    front.Pole(0.5f, 0.5f, baseH - 6, baseH + 12, Pal.Wood, 1);
                    front.Flag(0.5f, 0.5f, baseH + 12, level >= 5 ? Pal.Gold : flag, 8, 5);
                }
                else if (level >= 2)
                    front.Box(0.42f, 0.42f, 0.58f, 0.58f, baseH - 4, baseH, level >= 3 ? Pal.Gold : trim);
            }
            else
            {
                float e = 0.17f;
                Vector2 L = new Vector2(e, size - e), R = new Vector2(size - e, e);
                if (level >= 4)
                {
                    float t = z0 + towerH;
                    switch (style)
                    {
                        case 0: // egizak minoralar
                            foreach (Vector2 c in new[] { L, R }) Minaret(front, c, z0, t, size >= 4 ? 7f : 5f, Pal.BrickPale, trim, level, false);
                            break;
                        case 1: // gumbazli shiyponlar (to'rt ustunli ayvoncha)
                            foreach (Vector2 c in new[] { L, R }) Kiosk(front, c, z0, t, size >= 4 ? 0.42f : 0.3f, trim, level);
                            break;
                        case 2: // bitta baland qorovul minorasi (o'ngda), chapda tug'
                            Minaret(front, R, z0, t, size >= 4 ? 8f : 6f, Pal.Brick, Pal.Gold, level, true);
                            front.Pole(L.X, L.Y, z0, z0 + 34, Pal.Wood, 1);
                            front.Flag(L.X, L.Y, z0 + 34, flag, 12, 8);
                            break;
                        default: // koshinli ingichka minoralar
                            foreach (Vector2 c in new[] { L, R }) Minaret(front, c, z0, t, size >= 4 ? 5.5f : 4f, Pal.Plaster, trim, level, false, tiled: true);
                            break;
                    }
                }
                else if (level >= 3)
                {
                    // Bayroqlar yoki tug'lar — uslubga qarab rang va joy
                    var poles = style == 2 ? new[] { L } : style == 3 ? new[] { R } : new[] { L, R };
                    foreach (Vector2 c in poles)
                    {
                        front.Pole(c.X, c.Y, z0, z0 + 30, Pal.Wood, 1);
                        front.Flag(c.X, c.Y, z0 + 30, flag, 10, 7);
                    }
                }
                // Chiroqlar (2+): old tomonda; uslubga qarab soni va rangi
                var lamps = style == 1
                    ? new[] { new Vector2(size * 0.5f, size - 0.07f), new Vector2(size - 0.07f, size * 0.5f) }
                    : style == 3
                        ? new[] { new Vector2(size * 0.25f, size - 0.07f), new Vector2(size * 0.6f, size - 0.07f), new Vector2(size - 0.07f, size * 0.4f) }
                        : new[] { new Vector2(size * 0.3f, size - 0.07f), new Vector2(size - 0.07f, size * 0.3f) };
                foreach (Vector2 c in lamps)
                {
                    front.Pole(c.X, c.Y, z0, z0 + 12, Pal.Wood, 1);
                    front.Box(c.X - 0.07f, c.Y - 0.07f, c.X + 0.07f, c.Y + 0.07f, z0 + 12, z0 + 18, Lamps[style], 0, level >= 5 ? Pal.Gold : trim, 0f);
                }
            }
            front.C.Outline(Outline);
            Blit(result, front.C, 0, 0);
            return result;
        }

        private static void Minaret(IsoCanvas ic, Vector2 c, float z0, float t, float r, Color body, Color trim, int level, bool flag, bool tiled = false)
        {
            ic.Cylinder(c.X, c.Y, z0, t - 10, r, level >= 5 && !tiled ? Pal.Plaster : body, tiled ? 0 : 2);
            if (tiled)
                for (float z = z0 + 8; z < t - 14; z += 9) ic.Band(c.X, c.Y, z, z + 3, r + 0.3f, trim);
            else
                ic.Band(c.X, c.Y, z0 + (t - z0) * 0.45f, z0 + (t - z0) * 0.45f + 3, r + 0.5f, trim);
            ic.Band(c.X, c.Y, t - 14, t - 10, r + 1.5f, level >= 5 ? Pal.Gold : trim);
            if (level >= 5) ic.Dome(c.X, c.Y, t - 10, r + 1f, 11, Pal.Gold, 0, true);
            else ic.Cone(c.X, c.Y, t - 10, r + 1f, 11, trim);
            if (flag || level >= 5)
            {
                ic.Pole(c.X, c.Y, t + 1, t + 16, Pal.Wood, 1);
                ic.Flag(c.X, c.Y, t + 16, level >= 5 ? Pal.Gold : Pal.Red, 11, 7);
            }
        }

        private static void Kiosk(IsoCanvas ic, Vector2 c, float z0, float t, float half, Color trim, int level)
        {
            float x0 = c.X - half, y0 = c.Y - half, x1 = c.X + half, y1 = c.Y + half;
            foreach (var p in new[] { new Vector2(x0, y1), new Vector2(x1, y0), new Vector2(x1, y1) })
                ic.Pole(p.X, p.Y, z0, t - 6, Pal.Wood, 2);
            ic.Box(x0, y0, x1, y1, t - 6, t, Pal.Plaster, 0, trim);
            ic.Dome(c.X, c.Y, t, half * 28f, 12, level >= 5 ? Pal.Gold : trim, 4, true);
        }

        /// <summary>src ni dst ga (ox, oy) nuqtadan alfa bilan qo'yadi.</summary>
        public static void Blit(Canvas dst, Canvas src, int ox, int oy)
        {
            for (int y = 0; y < src.Height; y++)
            {
                int ty = y + oy;
                if (ty < 0 || ty >= dst.Height) continue;
                for (int x = 0; x < src.Width; x++)
                {
                    int tx = x + ox;
                    if (tx < 0 || tx >= dst.Width) continue;
                    Color c = src.Pixels[y * src.Width + x];
                    if (c.A == 0) continue;
                    dst.Blend(tx, ty, c);
                }
            }
        }
    }
}
