using System;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.Core.Graphics
{
    /// <summary>
    /// Shahar muhiti bezaklari uchun placeholder sprite'lar: daraxtlar, butalar, rastalar,
    /// hovuz, quduq, tandir, so'ri va h.k.
    /// </summary>
    public static class DecorArt
    {
        /// <summary>
        /// Ekin (bug'doy) o'sish bosqichi, 3x aniqlikda (o'yinda 1/3 masshtabda chiziladi):
        /// 0 — urug' (tuproqda), 1 — nihol, 2 — yosh, 3 — yashil poya, 4 — pishgan oltin boshoq.
        /// </summary>
        /// <summary>Hosil qopi (yelkada), 3x aniqlik.</summary>
        public static Canvas Sack()
        {
            var c = new Canvas(42, 48);
            c.FillBlob(21, 28, 16, 17, new Color(196, 168, 112), 0.04f);
            c.FillBlob(17, 24, 9, 9, new Color(214, 188, 132), 0.03f);
            c.FillEllipse(21, 11, 6, 4, new Color(170, 140, 88));
            c.Line(new Vector2(14, 13), new Vector2(28, 13), new Color(120, 86, 48), 3);
            c.Line(new Vector2(10, 34), new Vector2(32, 34), new Color(176, 146, 92), 2);
            c.Outline(new Color(70, 50, 30), 0.5f);
            return c;
        }

        public static Canvas Crop(int stage)
        {
            const int W = 30, H = 66;
            var c = new Canvas(W, H);
            float cx = W / 2f, by = H - 3;
            if (stage == 0)
            {
                c.FillEllipse(cx, by - 2, 7, 3, new Color(92, 62, 38));
                c.FillEllipse(cx - 2, by - 3, 1.6f, 1.2f, new Color(214, 190, 120));
                c.FillEllipse(cx + 3, by - 2, 1.6f, 1.2f, new Color(214, 190, 120));
                return c;
            }
            float hgt = stage == 1 ? 16 : stage == 2 ? 32 : stage == 3 ? 50 : 58;
            Color stem = stage == 4 ? new Color(206, 168, 70) : new Color(72, 148, 62);
            Color leaf = stage == 4 ? new Color(228, 190, 88) : new Color(96, 178, 74);
            int blades = stage == 1 ? 2 : stage == 2 ? 3 : 5;
            for (int i = 0; i < blades; i++)
            {
                float t = blades == 1 ? 0 : i / (float)(blades - 1) - 0.5f;
                float topX = cx + t * (stage == 1 ? 10 : 16);
                float topY = by - hgt * (0.82f + 0.18f * (1 - Math.Abs(t) * 2));
                c.Line(new Vector2(cx + t * 3, by), new Vector2(topX, topY), i % 2 == 0 ? stem : leaf, stage >= 3 ? 2 : 2);
                if (stage == 4)
                {
                    // boshoq
                    for (int k = 0; k < 5; k++)
                        c.FillEllipse(topX + (k % 2 == 0 ? -1.5f : 1.5f), topY + k * 2.6f, 2.0f, 1.6f, new Color(238, 200, 96));
                }
                else if (stage == 3)
                    c.FillEllipse(topX, topY + 2, 1.8f, 3.2f, new Color(126, 196, 92));
            }
            return c;
        }

        private static readonly Color Outline = new Color(40, 28, 20);

        /// <summary>Berilgan tuvalga daraxt chizadi. (x, baseY) — tana tagi.</summary>
        /// <param name="kind">0 terak, 1 chinor, 2 gullagan o'rik, 3 archa</param>
        public static void DrawTree(Canvas c, float x, float baseY, int kind, float s = 1f)
        {
            c.FillEllipse(x + 2, baseY, 11 * s, 4.5f * s, new Color(0, 0, 0, 55));
            switch (kind)
            {
                case 0: // terak — baland va ingichka
                    c.FillRect((int)x - 1, (int)(baseY - 16 * s), 3, (int)(16 * s), Pal.Trunk);
                    c.FillBlob(x, baseY - 36 * s, 8.5f * s, 26 * s, Pal.LeafDark, 0.12f);
                    c.FillBlob(x - 1.5f * s, baseY - 40 * s, 6 * s, 20 * s, Pal.Leaf, 0.12f);
                    c.FillBlob(x - 2.5f * s, baseY - 46 * s, 3.5f * s, 11 * s, Pal.LeafLight, 0.10f);
                    break;
                case 1: // chinor — keng soyabon
                    c.FillRect((int)x - 2, (int)(baseY - 18 * s), 4, (int)(18 * s), Pal.Trunk);
                    c.FillBlob(x - 9 * s, baseY - 24 * s, 12 * s, 10 * s, Pal.LeafDark, 0.12f);
                    c.FillBlob(x + 10 * s, baseY - 25 * s, 12 * s, 10 * s, Pal.LeafDark, 0.12f);
                    c.FillBlob(x, baseY - 33 * s, 15 * s, 12 * s, Pal.Leaf, 0.12f);
                    c.FillBlob(x - 5 * s, baseY - 38 * s, 8 * s, 6 * s, Pal.LeafLight, 0.10f);
                    break;
                case 2: // o'rik — bahorgi oq-pushti gul
                    c.FillRect((int)x - 1, (int)(baseY - 13 * s), 3, (int)(13 * s), Pal.Trunk);
                    c.FillBlob(x - 6 * s, baseY - 19 * s, 9 * s, 8 * s, new Color(92, 140, 72), 0.10f);
                    c.FillBlob(x + 6 * s, baseY - 20 * s, 9 * s, 8 * s, new Color(92, 140, 72), 0.10f);
                    c.FillBlob(x, baseY - 26 * s, 11 * s, 9 * s, new Color(110, 156, 82), 0.10f);
                    for (int i = 0; i < 26; i++)
                    {
                        float a = Canvas.Hash(i * 7 + 3, 11) * MathHelper.TwoPi;
                        float r = Canvas.Hash(i * 5 + 1, 29);
                        float fx = x + MathF.Cos(a) * r * 13 * s;
                        float fy = baseY - 23 * s + MathF.Sin(a) * r * 9 * s;
                        Color f = i % 3 == 0 ? new Color(244, 190, 200) : new Color(250, 238, 232);
                        c.FillRect((int)fx, (int)fy, 2, 2, f);
                    }
                    break;
                default: // archa — to'q yashil konus
                    c.FillRect((int)x - 1, (int)(baseY - 8 * s), 2, (int)(8 * s), Pal.Trunk);
                    for (int t = 0; t < (int)(34 * s); t++)
                    {
                        float hw = 7.5f * s * (1f - t / (34f * s)) + 0.8f;
                        for (int dx = -(int)hw; dx <= (int)hw; dx++)
                            c.Blend((int)x + dx, (int)(baseY - 6 * s) - t, Canvas.Shade(new Color(44, 92, 58), 1.05f - 0.3f * (dx / (hw + 0.01f))), 0.12f);
                    }
                    break;
            }
        }

        public static Canvas Tree(int kind)
        {
            var ic = new IsoCanvas(1, kind == 0 ? 62 : 44);
            Vector2 p = ic.P(0.5f, 0.5f, 0);
            DrawTree(ic.C, p.X, p.Y, kind);
            ic.C.Outline(Outline, 0.4f);
            return ic.C;
        }

        public static Canvas Bush()
        {
            var ic = new IsoCanvas(1, 12);
            Vector2 p = ic.P(0.5f, 0.5f, 0);
            ic.C.FillEllipse(p.X + 1, p.Y + 2, 13, 5, new Color(0, 0, 0, 50));
            ic.C.FillBlob(p.X - 6, p.Y - 3, 8, 6, Pal.LeafDark, 0.14f);
            ic.C.FillBlob(p.X + 6, p.Y - 2, 8, 6, Pal.LeafDark, 0.14f);
            ic.C.FillBlob(p.X, p.Y - 6, 9, 7, Pal.Leaf, 0.14f);
            ic.C.Outline(Outline, 0.35f);
            return ic.C;
        }

        public static Canvas Flowers()
        {
            var ic = new IsoCanvas(1, 8);
            Color[] cols = { new Color(226, 70, 70), new Color(246, 214, 90), new Color(240, 240, 236), new Color(226, 120, 180) };
            for (int i = 0; i < 22; i++)
            {
                float u = 0.15f + Canvas.Hash(i, 3) * 0.7f, v = 0.15f + Canvas.Hash(i, 9) * 0.7f;
                Vector2 p = ic.P(u, v, 0);
                ic.C.FillRect((int)p.X, (int)p.Y - 3, 1, 3, Pal.LeafDark);
                ic.C.FillRect((int)p.X - 1, (int)p.Y - 5, 3, 2, cols[i % cols.Length]);
            }
            return ic.C;
        }

        /// <summary>Bozor rastasi: yog'och peshtaxta, mevalar va yo'l-yo'l soyabon.</summary>
        public static Canvas Stall(int variant)
        {
            var ic = new IsoCanvas(1, 40);
            Color[] cloth = { Pal.Red, new Color(44, 104, 170), new Color(56, 132, 84), new Color(214, 130, 44) };
            Color a = cloth[variant % cloth.Length], b = Pal.Cream;
            Vector2 g = ic.P(0.5f, 0.5f, 0);
            ic.C.FillEllipse(g.X, g.Y + 2, 24, 10, new Color(0, 0, 0, 40));
            // orqa ustunlar
            ic.Pole(0.1f, 0.1f, 0, 33, Pal.Wood);
            ic.Pole(0.9f, 0.1f, 0, 33, Pal.Wood);
            ic.Pole(0.1f, 0.9f, 0, 25, Pal.Wood);
            // peshtaxta
            ic.Box(0.18f, 0.28f, 0.84f, 0.74f, 0, 9, Pal.Wood);
            Color[] goods = { new Color(226, 190, 70), new Color(200, 60, 50), new Color(120, 170, 70), new Color(232, 140, 50), new Color(240, 226, 190) };
            for (int i = 0; i < 9; i++)
            {
                float u = 0.26f + (i % 3) * 0.22f, v = 0.36f + (i / 3) * 0.13f;
                Vector2 p = ic.P(u, v, 10);
                ic.C.FillBlob(p.X, p.Y - 1, 3.2f, 2.6f, goods[(i + variant * 2) % goods.Length], 0.05f);
            }
            // soyabon (5 ta yo'l)
            for (int i = 0; i < 5; i++)
            {
                float x0 = 0.02f + i * 0.192f, x1 = x0 + 0.192f;
                ic.C.FillPolygon(i % 2 == 0 ? a : b, 0.05f,
                    ic.P(x0, 0.04f, 34), ic.P(x1, 0.04f, 34), ic.P(x1, 0.98f, 25), ic.P(x0, 0.98f, 25));
            }
            ic.QuadSW(0.02f, 0.98f, 0.98f, 21, 25, Canvas.Shade(a, 0.8f));
            ic.QuadSE(0.98f, 0.04f, 0.98f, 21, 25, Canvas.Shade(a, 0.62f));
            ic.C.FillPolygon(Canvas.Shade(a, 0.62f), 0f, ic.P(0.98f, 0.04f, 34), ic.P(0.98f, 0.98f, 25), ic.P(0.98f, 0.98f, 21), ic.P(0.98f, 0.04f, 30));
            ic.Pole(0.9f, 0.9f, 0, 22, Pal.Wood);
            ic.C.Outline(Outline, 0.5f);
            return ic.C;
        }

        /// <summary>Maydon markazidagi hovuz (2x2) — favvorachasi bilan.</summary>
        public static Canvas Hovuz()
        {
            var ic = new IsoCanvas(2, 26);
            ic.Box(0.12f, 0.12f, 1.88f, 1.88f, 0, 7, Pal.Stone, 0, new Color(196, 190, 176));
            ic.Top(0.3f, 0.3f, 1.7f, 1.7f, 7, Canvas.Shade(Pal.Stone, 0.6f));
            ic.Top(0.34f, 0.34f, 1.7f, 1.7f, 5, Pal.Water, 0.06f);
            for (int i = 0; i < 7; i++)
            {
                Vector2 p = ic.P(0.5f + Canvas.Hash(i, 5) * 1.0f, 0.5f + Canvas.Hash(i, 8) * 1.0f, 5);
                ic.C.FillRect((int)p.X, (int)p.Y, 5, 1, new Color(170, 220, 236));
            }
            // feruza burchak toshlari
            foreach (var (x, y) in new[] { (0.12f, 0.12f), (1.7f, 0.12f), (0.12f, 1.7f), (1.7f, 1.7f) })
                ic.Top(x, y, x + 0.18f, y + 0.18f, 7.5f, Pal.Turquoise);
            // favvora
            ic.Cylinder(1f, 1f, 5, 13, 5, Pal.Stone);
            Vector2 f = ic.P(1f, 1f, 13);
            ic.C.FillBlob(f.X, f.Y - 4, 3, 5, new Color(200, 232, 244), 0.03f);
            ic.C.FillEllipse(f.X, f.Y + 6, 9, 3.5f, new Color(190, 228, 240, 150));
            ic.C.Outline(Outline, 0.45f);
            return ic.C;
        }

        public static Canvas Well()
        {
            var ic = new IsoCanvas(1, 30);
            ic.Cylinder(0.5f, 0.5f, 0, 10, 13, Pal.Stone, 4);
            Vector2 t = ic.P(0.5f, 0.5f, 10);
            ic.C.FillEllipse(t.X, t.Y, 9, 4.5f, new Color(36, 44, 56));
            ic.Pole(0.22f, 0.5f, 0, 24, Pal.Wood);
            ic.Pole(0.78f, 0.5f, 0, 24, Pal.Wood);
            ic.Box(0.14f, 0.36f, 0.86f, 0.64f, 24, 27, Pal.Wood);
            ic.C.Line(ic.P(0.5f, 0.5f, 24), ic.P(0.5f, 0.5f, 12), Pal.Ink);
            ic.C.Outline(Outline, 0.5f);
            return ic.C;
        }

        /// <summary>Qorovul mash'alasi.</summary>
        public static Canvas Brazier()
        {
            var ic = new IsoCanvas(1, 30);
            Vector2 g = ic.P(0.5f, 0.5f, 0);
            ic.C.FillEllipse(g.X, g.Y + 1, 8, 3.5f, new Color(0, 0, 0, 55));
            ic.C.FillEllipse(g.X, g.Y - 12, 18, 12, new Color(255, 190, 80, 34));
            ic.Pole(0.5f, 0.5f, 0, 15, Pal.Ink, 2);
            ic.C.FillPolygon(new Color(70, 60, 58), 0f, g + new Vector2(-6, -18), g + new Vector2(6, -18), g + new Vector2(3, -13), g + new Vector2(-3, -13));
            ic.C.FillBlob(g.X, g.Y - 22, 4.5f, 5.5f, new Color(232, 110, 36), 0.05f);
            ic.C.FillBlob(g.X, g.Y - 21, 2.6f, 3.6f, new Color(252, 214, 96), 0.03f);
            return ic.C;
        }

        public static Canvas Banner()
        {
            var ic = new IsoCanvas(1, 50);
            Vector2 g = ic.P(0.5f, 0.5f, 0);
            ic.C.FillEllipse(g.X, g.Y + 1, 6, 2.5f, new Color(0, 0, 0, 55));
            ic.Pole(0.5f, 0.5f, 0, 46, Pal.Wood, 2);
            ic.C.FillEllipse(g.X, g.Y - 47, 2, 2, Pal.Gold);
            ic.C.FillPolygon(Pal.Red, 0.05f, g + new Vector2(1, -45), g + new Vector2(15, -45), g + new Vector2(15, -26), g + new Vector2(8, -30), g + new Vector2(1, -26));
            ic.C.FillRect((int)g.X + 1, (int)g.Y - 45, 14, 2, Pal.Gold);
            ic.C.FillEllipse(g.X + 8, g.Y - 37, 2.5f, 2.5f, Pal.Gold);
            ic.C.Outline(Outline, 0.5f);
            return ic.C;
        }

        /// <summary>Shahar markazidagi ogohlantirish qo'ng'irog'i: yog'och ramkaga osilgan bronza jom.</summary>
        public static Canvas Bell()
        {
            var ic = new IsoCanvas(1, 50);
            Vector2 g = ic.P(0.5f, 0.5f, 0);
            ic.C.FillEllipse(g.X, g.Y + 1, 7, 3f, new Color(0, 0, 0, 55));
            ic.Pole(0.3f, 0.5f, 0, 40, Pal.Wood, 2);
            ic.Pole(0.7f, 0.5f, 0, 40, Pal.Wood, 2);
            Vector2 top = ic.P(0.5f, 0.5f, 40);
            ic.C.FillRect((int)g.X - 11, (int)top.Y - 2, 22, 3, Pal.Wood); // ko'ndalang
            Color bronze = new Color(200, 152, 72), bronzeD = new Color(150, 110, 48);
            int by = (int)top.Y + 3;
            ic.C.FillRect((int)g.X - 1, by - 2, 2, 3, bronzeD); // osma ilgak
            for (int i = 0; i < 7; i++) { int w = 3 + i; ic.C.FillRect((int)g.X - w, by + i * 2, w * 2, 2, (i & 1) == 0 ? bronze : bronzeD); }
            ic.C.FillRect((int)g.X - 10, by + 14, 20, 2, bronzeD); // labi
            ic.C.FillRect((int)g.X - 1, by + 16, 2, 3, bronzeD);   // tili
            ic.C.Outline(Outline, 0.5f);
            return ic.C;
        }

        public static Canvas Crates()
        {
            var ic = new IsoCanvas(1, 22);
            Vector2 g = ic.P(0.5f, 0.5f, 0);
            ic.C.FillEllipse(g.X, g.Y + 2, 22, 9, new Color(0, 0, 0, 40));
            ic.Box(0.12f, 0.15f, 0.52f, 0.55f, 0, 12, new Color(150, 106, 62), 4);
            ic.Box(0.2f, 0.22f, 0.48f, 0.5f, 12, 21, new Color(166, 120, 70), 4);
            ic.Box(0.56f, 0.3f, 0.9f, 0.64f, 0, 10, new Color(140, 98, 58), 4);
            Vector2 s = ic.P(0.4f, 0.78f, 0);
            ic.C.FillBlob(s.X, s.Y - 5, 6, 6, new Color(206, 190, 150), 0.08f);
            ic.C.FillRect((int)s.X - 1, (int)s.Y - 12, 3, 2, new Color(150, 130, 96));
            ic.C.Outline(Outline, 0.5f);
            return ic.C;
        }

        /// <summary>Mashq qo'g'irchog'i (qilichbozlik uchun).</summary>
        public static Canvas Dummy()
        {
            var ic = new IsoCanvas(1, 30);
            Vector2 g = ic.P(0.5f, 0.5f, 0);
            ic.C.FillEllipse(g.X, g.Y + 1, 7, 3, new Color(0, 0, 0, 55));
            ic.Pole(0.5f, 0.5f, 0, 24, Pal.Wood, 2);
            ic.C.FillRect((int)g.X - 8, (int)g.Y - 19, 16, 2, Pal.Wood);
            ic.C.FillBlob(g.X, g.Y - 15, 5, 6.5f, new Color(214, 186, 110), 0.14f);
            ic.C.FillBlob(g.X, g.Y - 25, 3.5f, 3.5f, new Color(214, 186, 110), 0.14f);
            ic.C.FillEllipse(g.X, g.Y - 15, 2, 2, Pal.Red);
            ic.C.Outline(Outline, 0.5f);
            return ic.C;
        }

        public static Canvas WeaponRack()
        {
            var ic = new IsoCanvas(1, 34);
            Vector2 g = ic.P(0.5f, 0.5f, 0);
            ic.C.FillEllipse(g.X, g.Y + 1, 18, 6, new Color(0, 0, 0, 45));
            ic.Pole(0.15f, 0.5f, 0, 18, Pal.Wood, 2);
            ic.Pole(0.85f, 0.5f, 0, 18, Pal.Wood, 2);
            ic.C.Line(ic.P(0.15f, 0.5f, 16), ic.P(0.85f, 0.5f, 16), Pal.Wood, 2);
            ic.C.Line(ic.P(0.15f, 0.5f, 7), ic.P(0.85f, 0.5f, 7), Pal.Wood, 2);
            for (int i = 0; i < 4; i++)
            {
                float u = 0.26f + i * 0.16f;
                Vector2 a = ic.P(u, 0.56f, 0), b = ic.P(u, 0.5f, 30);
                ic.C.Line(a, b, new Color(150, 110, 66));
                ic.C.FillPolygon(Pal.Steel, 0f, b + new Vector2(-2, 0), b + new Vector2(3, 0), b + new Vector2(0.5f, -6));
            }
            Vector2 sh = ic.P(0.5f, 0.75f, 6);
            ic.C.FillEllipse(sh.X, sh.Y, 6, 6, Pal.Gold);
            ic.C.FillEllipse(sh.X, sh.Y, 4.6f, 4.6f, Pal.Red);
            ic.C.Outline(Outline, 0.5f);
            return ic.C;
        }

        public static Canvas Tandir()
        {
            var ic = new IsoCanvas(1, 26);
            Vector2 g = ic.P(0.5f, 0.5f, 0);
            ic.C.FillEllipse(g.X, g.Y + 2, 20, 8, new Color(0, 0, 0, 45));
            ic.Box(0.2f, 0.2f, 0.8f, 0.8f, 0, 5, Pal.MudDark);
            ic.Dome(0.5f, 0.5f, 5, 14, 15, new Color(196, 150, 104), 0, false);
            Vector2 m = ic.P(0.62f, 0.62f, 12);
            ic.C.FillEllipse(m.X, m.Y, 5, 5.5f, new Color(40, 24, 18));
            ic.C.FillEllipse(m.X, m.Y + 2, 3, 2.5f, new Color(232, 120, 40));
            // non savati
            Vector2 n = ic.P(0.86f, 0.3f, 0);
            ic.C.FillBlob(n.X, n.Y - 3, 5, 3.2f, new Color(226, 180, 104), 0.05f);
            ic.C.Outline(Outline, 0.5f);
            return ic.C;
        }

        /// <summary>So'ri (choyxona taxti) — gilam to'shalgan yog'och supa.</summary>
        public static Canvas Sori()
        {
            var ic = new IsoCanvas(1, 22);
            Vector2 g = ic.P(0.5f, 0.5f, 0);
            ic.C.FillEllipse(g.X, g.Y + 2, 26, 10, new Color(0, 0, 0, 40));
            foreach (var (x, y) in new[] { (0.12f, 0.12f), (0.88f, 0.12f), (0.12f, 0.88f), (0.88f, 0.88f) })
                ic.Pole(x, y, 0, 6, Pal.Wood, 2);
            // orqa panjara
            ic.QuadSW(0.08f, 0.92f, 0.1f, 9, 18, Canvas.Shade(Pal.Wood, 1.1f));
            ic.QuadSE(0.1f, 0.08f, 0.92f, 9, 18, Canvas.Shade(Pal.Wood, 0.9f));
            ic.Box(0.08f, 0.08f, 0.92f, 0.92f, 5, 9, Pal.Wood, 0, Pal.Red);
            ic.Top(0.2f, 0.2f, 0.8f, 0.8f, 9, new Color(196, 70, 50));
            ic.Top(0.3f, 0.3f, 0.7f, 0.7f, 9, new Color(226, 190, 96));
            ic.Top(0.38f, 0.38f, 0.62f, 0.62f, 9, new Color(40, 104, 150));
            // choynak
            Vector2 t = ic.P(0.5f, 0.5f, 10);
            ic.C.FillBlob(t.X, t.Y - 2, 3, 3, new Color(240, 240, 244), 0.02f);
            ic.C.Outline(Outline, 0.5f);
            return ic.C;
        }

        // ---------- UI ikonkalari ----------

        public static Canvas IconCoin()
        {
            var c = new Canvas(22, 22);
            c.FillEllipse(11, 11, 9.5f, 9.5f, new Color(150, 104, 26));
            c.FillBlob(11, 11, 8.3f, 8.3f, Pal.Gold, 0.03f);
            c.FillEllipse(11, 11, 5.2f, 5.2f, new Color(190, 146, 40));
            c.FillBlob(11, 11, 4, 4, new Color(236, 204, 96), 0.02f);
            return c;
        }

        public static Canvas IconFood()
        {
            var c = new Canvas(22, 22);
            // non (patir)
            c.FillEllipse(11, 12, 10, 8, new Color(130, 84, 36));
            c.FillBlob(11, 12, 9, 7, new Color(226, 172, 92), 0.05f);
            c.FillEllipse(11, 12, 4, 3, new Color(196, 138, 66));
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * MathHelper.TwoPi;
                c.FillRect((int)(11 + MathF.Cos(a) * 6.2f), (int)(12 + MathF.Sin(a) * 4.6f), 1, 1, new Color(120, 76, 34));
            }
            return c;
        }

        public static Canvas IconPopulation()
        {
            var c = new Canvas(22, 22);
            c.FillPolygon(new Color(60, 120, 150), 0f, new Vector2(3, 21), new Vector2(5, 13), new Vector2(17, 13), new Vector2(19, 21));
            c.FillEllipse(11, 9, 4.6f, 4.8f, new Color(226, 184, 146));
            // do'ppi
            c.FillPolygon(new Color(30, 30, 36), 0f, new Vector2(6, 7), new Vector2(7, 3), new Vector2(15, 3), new Vector2(16, 7));
            c.FillRect(8, 4, 1, 1, Color.White); c.FillRect(11, 4, 1, 1, Color.White); c.FillRect(14, 4, 1, 1, Color.White);
            return c;
        }
    }
}
