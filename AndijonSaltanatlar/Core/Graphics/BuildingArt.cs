using System;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.Core.Graphics
{
    /// <summary>
    /// Binolar uchun placeholder sprite'lar — kod bilan chizilgan sharqona me'morchilik:
    /// loy-g'isht uylar, feruza gumbazlar, peshtoqlar, minoralar, kungurador devorlar.
    /// Har bir funksiya bitta sprite qaytaradi; Content/Sprites/&lt;kalit&gt;.png mavjud bo'lsa,
    /// AssetManager shu funksiya o'rniga PNG faylni ishlatadi.
    /// </summary>
    public static class BuildingArt
    {
        private static readonly Color Outline = new Color(44, 28, 20);

        // =====================================================================
        //  UY (2x2) — hovlili loy-g'isht uy, 4 xil ko'rinish
        // =====================================================================
        /// <summary>
        /// Uy. Daraja ko'rinishi: 2 — koshin belbog', ko'k bo'yalgan ayvon ustunlari, oltin hoshiya, gul tuvaklar;
        /// 3 — bularning ustiga ikkinchi qavat (bolaxona), feruza qirrali gumbaz, bayroq va koshinli darvoza peshtoqi.
        /// </summary>
        public static Canvas House(int v, int level = 1)
        {
            var ic = new IsoCanvas(2, level >= 5 ? 108 : level >= 4 ? 94 : level >= 3 ? 78 : 56);
            Color wall = v switch { 0 => Pal.Mud, 1 => Pal.BrickPale, 2 => Pal.Plaster, _ => Pal.MudDark };
            Color roof = Canvas.Shade(wall, 0.92f);
            float mh = v == 1 ? 32 : (v == 3 ? 26 : 29);
            // 4-5 daraja: uy balandlashadi (kattalashadi) — minora emas, uyga xos qavatlar
            if (level >= 4) mh += 9;
            if (level >= 5) mh += 8;
            // Har bir uy turi o'z ranglari va shakliga ega — yuqori darajada ham bir xil bo'lib qolmaydi
            Color band = v switch { 0 => Pal.Turquoise, 1 => Pal.Blue, 2 => new Color(70, 150, 96), _ => Pal.Red };
            Color dome = v switch { 0 => Pal.Turquoise, 1 => Pal.Blue, 2 => new Color(70, 150, 96), _ => Pal.Gold };
            Color flagC = v switch { 0 => Pal.Red, 1 => Pal.Gold, 2 => Pal.Blue, _ => new Color(70, 150, 96) };
            bool rightLoft = v % 2 == 1;                       // bolaxona o'ngda (toq turlar) yoki chapda
            float lx0 = rightLoft ? 1.0f : 0.2f, lx1 = rightLoft ? 1.8f : 1.0f, lc = (lx0 + lx1) / 2f;
            float domeX = rightLoft ? 0.6f : 1.4f;

            // hovli
            ic.Top(0.08f, 0.08f, 1.92f, 1.92f, 0, new Color(204, 176, 130));
            ic.Box(0.08f, 0.08f, 1.92f, 0.2f, 0, 10, wall);
            ic.Box(0.08f, 0.2f, 0.2f, 1.92f, 0, 10, wall);

            // asosiy xona (orqada)
            ic.Box(0.2f, 0.2f, 1.8f, 1.0f, 0, mh, wall, 7, roof);
            ic.Parapet(0.2f, 0.2f, 1.8f, 1.0f, mh, roof);
            // to'sin uchlari (vassa)
            for (float x = 0.32f; x < 1.75f; x += 0.2f)
                ic.QuadSW(x, x + 0.05f, 1.0f, mh - 5, mh - 3, Pal.Wood);
            // derazalar
            ic.Arch(true, 1.25f, 1.0f, 0.09f, mh - 17, mh - 7, Pal.Door);
            ic.Arch(true, 1.58f, 1.0f, 0.09f, mh - 17, mh - 7, Pal.Door);
            ic.QuadSE(1.8f, 0.45f, 0.75f, mh - 16, mh - 8, Canvas.Shade(Pal.Door, 0.9f));
            if (level >= 2)
            {
                // feruza koshin belbog' va oltin hoshiya
                ic.QuadSW(0.2f, 1.8f, 1.0f, mh - 5, mh - 2, band);
                ic.QuadSE(1.8f, 0.2f, 1.0f, mh - 5, mh - 2, Canvas.Shade(band, 0.74f));
                ic.QuadSW(0.2f, 1.8f, 1.0f, mh - 2, mh - 1, Pal.Gold);
            }

            if (v == 1 || v == 2 || level >= 3)
            {
                // bolaxona (ikkinchi qavat)
                ic.Box(lx0, 0.2f, lx1, 0.9f, mh, mh + 15, wall, 7, roof);
                ic.Parapet(lx0, 0.2f, lx1, 0.9f, mh + 15, roof, 0.07f);
                if (level >= 3)
                {
                    ic.FramedArch(true, lc, 0.9f, 0.13f, mh + 2, mh + 13, band, Pal.Door);
                    ic.QuadSW(lx0, lx1, 0.9f, mh + 13, mh + 15, Pal.Gold);
                    ic.Pole(lx0 + 0.25f, 0.5f, mh + 15, mh + 29, Pal.Wood, 1);
                    ic.Flag(lx0 + 0.25f, 0.5f, mh + 29, flagC, 10, 7);
                }
                else ic.Arch(true, lc, 0.9f, 0.1f, mh + 3, mh + 12, Pal.Door);
            }
            if (level >= 3)
            {
                // feruza qirrali gumbaz (oltin uchli)
                ic.Cylinder(domeX, 0.6f, mh, mh + 4, 10, Pal.Plaster);
                ic.Dome(domeX, 0.6f, mh + 4, 10, v == 2 ? 15 : 12, dome, v == 3 ? 0 : 6, true);
            }
            else if (v == 3)
            {
                // kichik gumbazcha
                ic.Dome(1.3f, 0.6f, mh, 12, 11, Pal.BrickPale, 0, false);
            }
            if (level >= 4)
            {
                // Ikkinchi gumbaz (qarama-qarshi tomonda) va oltin belbog' — uy hashamatlashadi
                float dX2 = rightLoft ? 1.45f : 0.55f;
                ic.Cylinder(dX2, 0.62f, mh, mh + 3, 8, Pal.Plaster);
                ic.Dome(dX2, 0.62f, mh + 3, 8, 11, Canvas.Shade(dome, 0.92f), 6, true);
                ic.QuadSW(0.2f, 1.8f, 1.0f, mh - 1, mh, Pal.Gold);
            }
            if (level >= 5)
            {
                // Oliy daraja: yiriklashgan oltin gumbaz uchi va oltin bayroq (uyga xos toj, minora emas)
                ic.Dome(domeX, 0.6f, mh + 4, 12, 15, Pal.Gold, 6, true);
                ic.Pole(1.0f, 0.95f, mh + 15, mh + 30, Pal.Wood, 1);
                ic.Flag(1.0f, 0.95f, mh + 30, Pal.Gold, 11, 7);
            }

            // yon xona (chap-old)
            ic.Box(0.2f, 1.0f, 0.95f, 1.8f, 0, 21, wall, 7, roof);
            ic.Parapet(0.2f, 1.0f, 0.95f, 1.8f, 21, roof, 0.07f);
            ic.Arch(true, 0.58f, 1.8f, 0.14f, 0, 15, Pal.Door);
            ic.QuadSW(0.47f, 0.69f, 1.8f, 15, 17, Pal.Wood);
            ic.QuadSE(0.95f, 1.25f, 1.5f, 8, 15, Canvas.Shade(Pal.Door, 0.9f));

            // ayvon: yog'och ustunlar va tom (yuqori darajada — o'ymakor, ko'k bo'yalgan, oltin hoshiyali)
            Color post = level >= 2 ? (v switch { 0 => Pal.Blue, 1 => Pal.Turquoise, 2 => Pal.Red, _ => Pal.Gold }) : Pal.Wood;
            ic.Pole(1.1f, 1.38f, 0, 19, post);
            ic.Pole(1.72f, 1.38f, 0, 19, post);
            ic.Box(1.0f, 1.0f, 1.8f, 1.42f, 19, 22, Pal.Wood, 0, Canvas.Shade(roof, 0.95f));
            if (level >= 2) ic.QuadSW(1.0f, 1.8f, 1.42f, 19, 20, Pal.Gold);

            // hovlidagi daraxt
            Vector2 t = ic.P(1.42f, 1.62f, 0);
            DecorArt.DrawTree(ic.C, t.X, t.Y, v % 2 == 0 ? 2 : 1, 0.55f);
            if (level >= 2)
            {
                // gul tuvaklar
                ic.Box(1.12f, 1.62f, 1.22f, 1.72f, 0, 4, Pal.Red);
                ic.Box(1.64f, 1.58f, 1.74f, 1.68f, 0, 4, Pal.Red);
                ic.Box(1.13f, 1.63f, 1.21f, 1.71f, 4, 6, Pal.LeafLight);
                ic.Box(1.65f, 1.59f, 1.73f, 1.67f, 4, 6, Pal.LeafLight);
            }

            // old hovli devori va darvozacha
            ic.Box(1.8f, 0.2f, 1.92f, 1.8f, 0, 10, wall);
            ic.Box(0.95f, 1.8f, 1.3f, 1.92f, 0, 10, wall);
            ic.Box(1.55f, 1.8f, 1.92f, 1.92f, 0, 10, wall);
            ic.QuadSW(1.3f, 1.55f, 1.86f, 0, 9, Pal.Wood);
            if (level >= 3)
            {
                // koshinli darvoza peshtoqi
                ic.Box(1.24f, 1.8f, 1.31f, 1.92f, 0, 15, Pal.Plaster, 0, Pal.Turquoise);
                ic.Box(1.54f, 1.8f, 1.61f, 1.92f, 0, 15, Pal.Plaster, 0, Pal.Turquoise);
                ic.Box(1.24f, 1.8f, 1.61f, 1.92f, 12, 16, Pal.Plaster, 0, Pal.Turquoise);
                ic.QuadSW(1.27f, 1.58f, 1.92f, 13, 15, band);
            }

            ic.C.Outline(Outline);
            return ic.C;
        }

        // =====================================================================
        //  QO'RIQLASH MINORASI (2x2)
        // =====================================================================
        /// <summary>Qal'a minorasi. Daraja: 2 — ikkinchi koshin belbog'; 3 — oltin gumbaz va katta bayroq.</summary>
        public static Canvas Tower(int level = 1)
        {
            var ic = new IsoCanvas(2, 104);
            Color w = Pal.Brick;
            // poydevor
            ic.Box(0.1f, 0.1f, 1.9f, 1.9f, 0, 8, Canvas.Shade(w, 0.85f), 4);
            // tana
            ic.Box(0.2f, 0.2f, 1.8f, 1.8f, 8, 60, w, 5);
            ic.Arch(true, 1.0f, 1.8f, 0.2f, 8, 30, Pal.Door);
            ic.QuadSW(0.94f, 1.06f, 1.8f, 38, 50, Pal.Door);
            ic.QuadSE(1.8f, 0.94f, 1.06f, 38, 50, Canvas.Shade(Pal.Door, 0.8f));
            ic.QuadSE(1.8f, 0.94f, 1.06f, 16, 28, Canvas.Shade(Pal.Door, 0.8f));
            // feruza koshin belbog'
            ic.QuadSW(0.2f, 1.8f, 1.8f, 53, 57, Pal.Turquoise);
            ic.QuadSE(1.8f, 0.2f, 1.8f, 53, 57, Canvas.Shade(Pal.Turquoise, 0.74f));
            if (level >= 2)
            {
                ic.QuadSW(0.2f, 1.8f, 1.8f, 32, 35, Pal.Turquoise);
                ic.QuadSE(1.8f, 0.2f, 1.8f, 32, 35, Canvas.Shade(Pal.Turquoise, 0.74f));
                ic.QuadSW(0.2f, 1.8f, 1.8f, 57, 58, Pal.Gold);
            }
            // kengaygan maydoncha
            ic.Box(0.08f, 0.08f, 1.92f, 1.92f, 60, 68, w, 4, Pal.BrickPale);

            // kunguralar: orqadagilar -> gumbaz -> oldingilar
            float[] a = { 0.08f, 0.8f, 1.52f };
            const float s = 0.4f;
            foreach (float mx in a)
                foreach (float my in a)
                {
                    bool edge = mx != 0.8f || my != 0.8f;
                    if (edge && mx + my < 1.7f) ic.Merlon(mx, my, s, 68, 77, w);
                }
            ic.Cylinder(1f, 1f, 68, 74, 13, Pal.Plaster);
            ic.Dome(1f, 1f, 74, 13, 14, level >= 3 ? Pal.Gold : Pal.Turquoise, 8);
            ic.Pole(1f, 1f, 90, 102, Pal.Wood, 1);
            if (level >= 3) ic.Flag(1f, 1f, 102, Pal.Red, 16, 10); else ic.Flag(1f, 1f, 102, Pal.Red);
            foreach (float mx in a)
                foreach (float my in a)
                {
                    bool edge = mx != 0.8f || my != 0.8f;
                    if (edge && mx + my >= 1.7f) ic.Merlon(mx, my, s, 68, 77, w);
                }

            ic.C.Outline(Outline);
            return ic.C;
        }

        // =====================================================================
        //  DEVOR BO'LAGI (1x1) — istalgan yo'nalishda ulanadi
        // =====================================================================
        /// <summary>Devor bo'lagi. Daraja: 2 — feruza koshin belbog'; 3 — oltin hoshiya ham.</summary>
        public static Canvas Wall(int level = 1)
        {
            var ic = new IsoCanvas(1, 42);
            Color w = Pal.Brick;
            ic.Box(0, 0, 1, 1, 0, 30, w, 5, Pal.BrickPale);
            ic.QuadSW(0, 1, 1, 24, 26, level >= 2 ? Pal.Turquoise : Canvas.Shade(w, 0.74f));
            ic.QuadSE(1, 0, 1, 24, 26, level >= 2 ? Canvas.Shade(Pal.Turquoise, 0.74f) : Canvas.Shade(w, 0.56f));
            if (level >= 3) ic.QuadSW(0, 1, 1, 26, 27, Pal.Gold);
            const float s = 0.3f;
            ic.Merlon(0, 0, s, 30, 38, w);
            ic.Merlon(1 - s, 0, s, 30, 38, w);
            ic.Merlon(0, 1 - s, s, 30, 38, w);
            ic.Merlon(1 - s, 1 - s, s, 30, 38, w);
            ic.C.Outline(Outline, 0.4f);
            return ic.C;
        }

        // =====================================================================
        //  DARVOZA RAVOG'I (1x1) — devor X o'qi bo'ylab, o'tish Y bo'ylab.
        //  Boshqa yo'nalish uchun sprite gorizontal aylantiriladi.
        //  O'rtasi shaffof: jangchi ravoq ostidan o'tayotgani ko'rinadi.
        // =====================================================================
        /// <summary>Darvoza. Daraja: 2 — koshinli ustunlar; 3 — tepasida feruza gumbazcha.</summary>
        public static Canvas Gate(int level = 1)
        {
            var ic = new IsoCanvas(1, level >= 3 ? 82 : 66);
            Color w = Pal.BrickPale;
            // ustunlar
            ic.Box(0f, 0f, 0.16f, 1f, 0, 40, w, 5);
            ic.Box(0.84f, 0f, 1f, 1f, 0, 40, w, 5);
            if (level >= 2)
            {
                ic.QuadSW(0.03f, 0.13f, 1f, 6, 36, Pal.Turquoise);
                ic.QuadSW(0.87f, 0.97f, 1f, 6, 36, Pal.Turquoise);
            }
            // ravoq burchaklari
            ic.C.FillPolygon(Canvas.Shade(w, 0.93f), 0.04f, ic.P(0.16f, 1, 40), ic.P(0.16f, 1, 24), ic.P(0.3f, 1, 34), ic.P(0.5f, 1, 40));
            ic.C.FillPolygon(Canvas.Shade(w, 0.93f), 0.04f, ic.P(0.84f, 1, 40), ic.P(0.84f, 1, 24), ic.P(0.7f, 1, 34), ic.P(0.5f, 1, 40));
            // peshtoq (tepa qism)
            ic.Box(0f, 0f, 1f, 1f, 40, 54, w, 5, Pal.Plaster);
            ic.QuadSW(0f, 1f, 1f, 44, 50, Pal.Blue);
            ic.QuadSW(0.1f, 0.9f, 1f, 46, 48, Pal.Gold);
            ic.QuadSE(1f, 0f, 1f, 44, 50, Canvas.Shade(Pal.Blue, 0.7f));
            const float s = 0.3f;
            ic.Merlon(0, 0, s, 54, 62, w);
            ic.Merlon(1 - s, 0, s, 54, 62, w);
            ic.Merlon(0, 1 - s, s, 54, 62, w);
            ic.Merlon(1 - s, 1 - s, s, 54, 62, w);
            if (level >= 3) ic.Dome(0.5f, 0.5f, 54, 8, 10, Pal.Turquoise, 4, true);
            ic.C.Outline(Outline, 0.45f);
            return ic.C;
        }

        // =====================================================================
        //  BOZOR (4x4) — gumbazli savdo toqi, soyabonlar va mollar
        // =====================================================================
        public static Canvas Market()
        {
            var ic = new IsoCanvas(4, 92);
            Color w = Pal.BrickPale;
            ic.Top(0.05f, 0.05f, 3.95f, 3.95f, 0, Pal.Paving);
            ic.Box(0.6f, 0.6f, 3.4f, 3.4f, 0, 30, w, 5, Pal.Plaster);
            ic.Parapet(0.6f, 0.6f, 3.4f, 3.4f, 30, Pal.Plaster, 0.12f);

            // ravoqlar
            ic.FramedArch(true, 2.0f, 3.4f, 0.42f, 0, 27, Pal.Turquoise, Pal.Door);
            ic.FramedArch(true, 1.05f, 3.4f, 0.26f, 0, 21, Pal.Brick, Pal.Door);
            ic.FramedArch(true, 2.95f, 3.4f, 0.26f, 0, 21, Pal.Brick, Pal.Door);
            ic.FramedArch(false, 2.0f, 3.4f, 0.42f, 0, 27, Pal.Turquoise, Pal.Door);
            ic.FramedArch(false, 1.05f, 3.4f, 0.26f, 0, 21, Pal.Brick, Pal.Door);
            ic.FramedArch(false, 2.95f, 3.4f, 0.26f, 0, 21, Pal.Brick, Pal.Door);

            // gumbazlar (orqadan oldinga)
            Color dome = new Color(214, 170, 112);
            ic.Dome(1.1f, 1.1f, 30, 15, 13, dome);
            ic.Dome(2.9f, 1.1f, 30, 15, 13, dome);
            ic.Dome(1.1f, 2.9f, 30, 15, 13, dome);
            ic.Cylinder(2f, 2f, 30, 38, 36, Pal.Plaster);
            ic.Band(2f, 2f, 32, 36, 36, Pal.Turquoise);
            ic.Dome(2f, 2f, 38, 37, 36, dome, 12);
            ic.Dome(2.9f, 2.9f, 30, 15, 13, dome);

            // soyabonlar
            Awning(ic, true, 0.65f, 1.5f, 3.4f, Pal.Red);
            Awning(ic, true, 2.5f, 3.35f, 3.4f, new Color(44, 104, 170));
            Awning(ic, false, 0.65f, 1.5f, 3.4f, new Color(214, 130, 44));
            Awning(ic, false, 2.5f, 3.35f, 3.4f, new Color(56, 132, 84));

            // mollar: qutilar, qoplar, qovunlar
            Color[] goods = { new Color(226, 190, 70), new Color(200, 60, 50), new Color(120, 170, 70), new Color(232, 140, 50) };
            for (int i = 0; i < 4; i++)
            {
                float x = 0.72f + i * 0.2f + (i >= 2 ? 1.5f : 0f);
                ic.Box(x, 3.62f, x + 0.16f, 3.8f, 0, 6, Pal.Wood);
                Vector2 p = ic.P(x + 0.08f, 3.71f, 7);
                ic.C.FillBlob(p.X, p.Y, 3.4f, 2.6f, goods[i], 0.04f);
                float y = 0.72f + i * 0.2f + (i >= 2 ? 1.5f : 0f);
                ic.Box(3.62f, y, 3.8f, y + 0.16f, 0, 6, Pal.Wood);
                p = ic.P(3.71f, y + 0.08f, 7);
                ic.C.FillBlob(p.X, p.Y, 3.4f, 2.6f, goods[(i + 2) % 4], 0.04f);
            }

            ic.C.Outline(Outline);
            return ic.C;
        }

        private static void Awning(IsoCanvas ic, bool sw, float a0, float a1, float plane, Color col)
        {
            const int n = 5;
            float step = (a1 - a0) / n;
            for (int i = 0; i < n; i++)
            {
                float u0 = a0 + i * step, u1 = u0 + step;
                Color c = i % 2 == 0 ? col : Pal.Cream;
                if (sw)
                    ic.C.FillPolygon(c, 0.05f, ic.P(u0, plane, 24), ic.P(u1, plane, 24), ic.P(u1, plane + 0.5f, 15), ic.P(u0, plane + 0.5f, 15));
                else
                    ic.C.FillPolygon(Canvas.Shade(c, 0.8f), 0.05f, ic.P(plane, u0, 24), ic.P(plane, u1, 24), ic.P(plane + 0.5f, u1, 15), ic.P(plane + 0.5f, u0, 15));
            }
            if (sw)
            {
                ic.Pole(a0 + 0.03f, plane + 0.47f, 0, 15, Pal.Wood, 1);
                ic.Pole(a1 - 0.03f, plane + 0.47f, 0, 15, Pal.Wood, 1);
            }
            else
            {
                ic.Pole(plane + 0.47f, a0 + 0.03f, 0, 15, Pal.Wood, 1);
                ic.Pole(plane + 0.47f, a1 - 0.03f, 0, 15, Pal.Wood, 1);
            }
        }

        // =====================================================================
        //  SAROY / O'RDA (6x6) — markaziy boshqaruv binosi
        // =====================================================================
        /// <summary>Shoh saroyi. 3-daraja — bosh gumbaz zarhal (oltin).</summary>
        public static Canvas MainHall(int level = 1)
        {
            var ic = new IsoCanvas(6, 150);
            Color w = Pal.Plaster, pw = Pal.Brick;

            ic.Top(0.15f, 0.15f, 5.85f, 5.85f, 0, Pal.Paving);
            // hovli naqshi
            ic.Top(2.4f, 4.0f, 3.6f, 5.5f, 0, new Color(196, 156, 112));
            ic.Top(2.8f, 4.0f, 3.2f, 5.5f, 0, Canvas.Shade(Pal.Turquoise, 0.9f));

            // orqa va chap qo'rg'on devori
            ic.Box(0.15f, 0.15f, 5.85f, 0.42f, 0, 15, pw, 5);
            ic.Box(0.15f, 0.42f, 0.42f, 5.85f, 0, 15, pw, 5);
            Turret(ic, 0.3f, 0.3f);
            Turret(ic, 5.7f, 0.3f);

            // asosiy bino
            ic.Box(0.9f, 0.8f, 5.1f, 3.3f, 0, 48, w, 6);
            ic.Parapet(0.9f, 0.8f, 5.1f, 3.3f, 48, w, 0.14f);
            // koshin belbog'
            ic.QuadSW(0.9f, 5.1f, 3.3f, 39, 44, Pal.Turquoise);
            ic.QuadSW(0.9f, 5.1f, 3.3f, 41, 42, Pal.Gold);
            ic.QuadSE(5.1f, 0.8f, 3.3f, 39, 44, Canvas.Shade(Pal.Turquoise, 0.72f));
            // ravoqli tokchalar
            foreach (float x in new[] { 1.3f, 1.85f, 4.15f, 4.7f })
                ic.FramedArch(true, x, 3.3f, 0.2f, 4, 34, Pal.Blue, Pal.DeepBlue);
            foreach (float y in new[] { 1.25f, 2.05f, 2.85f })
                ic.FramedArch(false, y, 5.1f, 0.24f, 4, 34, Pal.Blue, Pal.DeepBlue);

            // tom gumbazchalari (orqa)
            ic.Dome(1.5f, 1.3f, 48, 14, 13, Pal.Turquoise, 6);
            ic.Dome(4.5f, 1.3f, 48, 14, 13, Pal.Turquoise, 6);
            // bosh gumbaz
            ic.Cylinder(3f, 2.05f, 48, 66, 40, Pal.Plaster);
            ic.Band(3f, 2.05f, 54, 62, 40, Pal.Blue);
            ic.Band(3f, 2.05f, 57, 59, 40, Pal.Gold);
            ic.Dome(3f, 2.05f, 66, 42, 56, level >= 3 ? Pal.Gold : Pal.Turquoise, 14);
            ic.Pole(3f, 2.05f, 128, 142, Pal.Wood, 1);
            ic.Flag(3f, 2.05f, 142, new Color(40, 130, 80), 14, 9);
            // tom gumbazchalari (old)
            ic.Dome(1.5f, 2.8f, 48, 13, 12, Pal.Turquoise, 6);
            ic.Dome(4.5f, 2.8f, 48, 13, 12, Pal.Turquoise, 6);

            // chap minora -> peshtoq -> o'ng minora
            Minaret(ic, 2.05f, 3.62f, 98, 8);
            ic.Box(2.3f, 3.3f, 3.7f, 3.95f, 0, 76, pw, 6, Pal.BrickPale);
            ic.QuadSW(2.3f, 3.7f, 3.95f, 66, 72, Pal.Blue);
            ic.QuadSW(2.4f, 3.6f, 3.95f, 68, 70, Pal.Gold);
            ic.Arch(true, 3.0f, 3.95f, 0.56f, 0, 62, Pal.Turquoise);
            ic.Arch(true, 3.0f, 3.95f, 0.47f, 0, 57, Pal.Blue);
            ic.Arch(true, 3.0f, 3.95f, 0.38f, 0, 52, new Color(34, 26, 40));
            ic.Arch(true, 3.0f, 3.95f, 0.2f, 0, 26, Pal.Wood);
            ic.QuadSW(2.99f, 3.01f, 3.95f, 0, 24, Pal.Door);
            ic.QuadSE(3.7f, 3.3f, 3.95f, 66, 72, Canvas.Shade(Pal.Blue, 0.7f));
            Minaret(ic, 3.95f, 3.62f, 98, 8);

            // hovlidagi archalar
            foreach (var (x, y) in new[] { (1.3f, 4.5f), (1.9f, 5.0f), (4.2f, 5.0f), (4.8f, 4.5f) })
            {
                Vector2 p = ic.P(x, y, 0);
                DecorArt.DrawTree(ic.C, p.X, p.Y, 3, 0.85f);
            }

            // o'ng va old qo'rg'on devori, darvoza
            ic.Box(5.58f, 0.42f, 5.85f, 5.58f, 0, 15, pw, 5);
            ic.Box(0.42f, 5.58f, 2.45f, 5.85f, 0, 15, pw, 5);
            ic.Box(3.55f, 5.58f, 5.85f, 5.85f, 0, 15, pw, 5);
            ic.Box(2.25f, 5.5f, 2.5f, 5.92f, 0, 24, Pal.BrickPale, 5, Pal.Turquoise);
            ic.Box(3.5f, 5.5f, 3.75f, 5.92f, 0, 24, Pal.BrickPale, 5, Pal.Turquoise);
            Turret(ic, 0.3f, 5.7f);
            Turret(ic, 5.7f, 5.7f);

            ic.C.Outline(Outline);
            return ic.C;
        }

        private static void Turret(IsoCanvas ic, float x, float y)
        {
            ic.Cylinder(x, y, 0, 24, 9, Pal.Brick, 5);
            ic.Band(x, y, 18, 21, 9, Pal.Turquoise);
            ic.Dome(x, y, 24, 9, 9, Pal.Turquoise, 0, false);
        }

        private static void Minaret(IsoCanvas ic, float x, float y, float h, float r)
        {
            ic.Cylinder(x, y, 0, h - 14, r, Pal.BrickPale, 6);
            ic.Band(x, y, h * 0.3f, h * 0.3f + 4, r, Pal.Turquoise);
            ic.Band(x, y, h * 0.6f, h * 0.6f + 4, r, Pal.Blue);
            ic.Cylinder(x, y, h - 14, h - 10, r + 3, Pal.Plaster);
            ic.Cylinder(x, y, h - 10, h, r - 2, Pal.Plaster);
            ic.Dome(x, y, h, r - 1, r + 3, Pal.Turquoise, 0, true);
        }

        // =====================================================================
        //  QO'SHINXONA / KAZARMA (3x3)
        // =====================================================================
        public static Canvas Barracks()
        {
            var ic = new IsoCanvas(3, 70);
            Color w = Pal.Brick;
            ic.Top(0.08f, 0.08f, 2.92f, 2.92f, 0, new Color(196, 168, 124));
            ic.Box(0.08f, 0.08f, 2.92f, 0.22f, 0, 12, w, 4);
            ic.Box(0.08f, 0.22f, 0.22f, 2.92f, 0, 12, w, 4);

            // asosiy zal
            ic.Box(0.22f, 0.22f, 2.78f, 1.7f, 0, 30, w, 5, Pal.BrickPale);
            for (float x = 0.22f; x < 2.6f; x += 0.42f) ic.Merlon(x, 1.48f, 0.22f, 30, 37, w);
            ic.FramedArch(true, 1.2f, 1.7f, 0.26f, 0, 22, Pal.BrickPale, Pal.Door);
            // devorga osilgan qalqonlar
            foreach (float x in new[] { 0.6f, 1.85f, 2.3f })
            {
                Vector2 p = ic.P(x, 1.7f, 17);
                ic.C.FillEllipse(p.X, p.Y, 4.5f, 5, Pal.Gold);
                ic.C.FillEllipse(p.X, p.Y, 3.2f, 3.7f, x > 1.5f ? Pal.Red : Pal.Blue);
            }
            ic.QuadSE(2.78f, 0.7f, 0.85f, 12, 22, Canvas.Shade(Pal.Door, 0.8f));
            ic.QuadSE(2.78f, 1.15f, 1.3f, 12, 22, Canvas.Shade(Pal.Door, 0.8f));

            // burchak minorasi
            ic.Box(0.22f, 0.22f, 1.0f, 1.0f, 30, 50, w, 5, Pal.BrickPale);
            ic.QuadSW(0.22f, 1.0f, 1.0f, 43, 46, Pal.Turquoise);
            ic.QuadSE(1.0f, 0.22f, 1.0f, 43, 46, Canvas.Shade(Pal.Turquoise, 0.72f));
            ic.Merlon(0.22f, 0.22f, 0.24f, 50, 57, w);
            ic.Merlon(0.76f, 0.22f, 0.24f, 50, 57, w);
            ic.Pole(0.6f, 0.6f, 50, 66, Pal.Wood, 1);
            ic.Flag(0.6f, 0.6f, 66, Pal.Red);
            ic.Merlon(0.22f, 0.76f, 0.24f, 50, 57, w);
            ic.Merlon(0.76f, 0.76f, 0.24f, 50, 57, w);

            // mashq maydonchasi: nayzalar tokchasi
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = ic.P(0.5f + i * 0.12f, 2.4f, 0), b = ic.P(0.5f + i * 0.12f, 2.3f, 24);
                ic.C.Line(a, b, new Color(150, 110, 66));
                ic.C.FillRect((int)b.X - 1, (int)b.Y - 3, 3, 4, Pal.Steel);
            }
            ic.Box(2.0f, 2.1f, 2.4f, 2.5f, 0, 9, new Color(150, 106, 62), 4);

            ic.Box(2.78f, 0.22f, 2.92f, 2.78f, 0, 12, w, 4);
            ic.Box(0.22f, 2.78f, 1.2f, 2.92f, 0, 12, w, 4);
            ic.Box(1.8f, 2.78f, 2.92f, 2.92f, 0, 12, w, 4);
            ic.C.Outline(Outline);
            return ic.C;
        }

        // =====================================================================
        //  BOG' (3x3) — chorbog': hovuzcha, mevali daraxtlar, gullar
        // =====================================================================
        public static Canvas Garden()
        {
            var ic = new IsoCanvas(3, 62);
            Color w = Pal.Mud;
            ic.Top(0.08f, 0.08f, 2.92f, 2.92f, 0, new Color(118, 162, 84), 0.10f);
            // yo'lakchalar (chorbog' xochi)
            ic.Top(1.38f, 0.2f, 1.62f, 2.8f, 0, Pal.Paving);
            ic.Top(0.2f, 1.38f, 2.8f, 1.62f, 0, Pal.Paving);
            ic.Box(0.08f, 0.08f, 2.92f, 0.2f, 0, 9, w);
            ic.Box(0.08f, 0.2f, 0.2f, 2.92f, 0, 9, w);

            // gullar
            Color[] fl = { new Color(226, 70, 70), new Color(246, 214, 90), new Color(240, 240, 236), new Color(226, 120, 180) };
            for (int i = 0; i < 46; i++)
            {
                float u = 0.3f + Canvas.Hash(i, 41) * 2.4f, v = 0.3f + Canvas.Hash(i, 77) * 2.4f;
                if (MathF.Abs(u - 1.5f) < 0.2f || MathF.Abs(v - 1.5f) < 0.2f) continue;
                Vector2 p = ic.P(u, v, 0);
                ic.C.FillRect((int)p.X, (int)p.Y - 2, 2, 2, fl[i % fl.Length]);
            }

            void T(float x, float y, int kind, float s)
            {
                Vector2 p = ic.P(x, y, 0);
                DecorArt.DrawTree(ic.C, p.X, p.Y, kind, s);
            }
            T(0.7f, 0.7f, 0, 0.9f);
            T(2.3f, 0.7f, 2, 0.95f);
            T(0.7f, 2.3f, 2, 0.95f);

            // hovuzcha
            ic.Box(1.15f, 1.15f, 1.85f, 1.85f, 0, 4, Pal.Stone);
            ic.Top(1.24f, 1.24f, 1.76f, 1.76f, 4, Pal.Water);
            ic.Top(1.24f, 1.24f, 1.5f, 1.4f, 4, new Color(140, 204, 226));

            T(2.3f, 2.3f, 1, 0.9f);
            T(1.0f, 2.62f, 3, 0.6f);
            T(2.62f, 1.0f, 3, 0.6f);

            ic.Box(2.8f, 0.2f, 2.92f, 2.8f, 0, 9, w);
            ic.Box(0.2f, 2.8f, 1.3f, 2.92f, 0, 9, w);
            ic.Box(1.7f, 2.8f, 2.92f, 2.92f, 0, 9, w);
            ic.C.Outline(Outline, 0.45f);
            return ic.C;
        }

        public static Canvas Farm()
        {
            var ic = new IsoCanvas(4, 58);
            Color soil = new Color(112, 78, 47);
            ic.Top(0.06f, 0.06f, 3.94f, 3.94f, 0, new Color(116, 145, 70), 0.05f);
            ic.Top(0.34f, 0.34f, 3.66f, 3.66f, 0, soil);
            for (int row = 0; row < 5; row++)
            {
                float y = 0.52f + row * 0.62f;
                // Haydalgan jo'yak (ekinlar alohida chiziladi va dehqon parvarishida o'sadi)
                ic.Top(0.45f, y - 0.12f, 3.55f, y, 7, new Color(96, 66, 40));
                ic.QuadSW(0.45f, 3.55f, y, 4, 7, new Color(82, 57, 36));
            }
            ic.Box(3.28f, 0.24f, 3.78f, 0.38f, 0, 12, Pal.Wood, 3);
            ic.Box(3.72f, 0.24f, 3.86f, 3.72f, 0, 12, Pal.Wood, 3);
            ic.C.Outline(Outline, 0.45f);
            return ic.C;
        }

        public static Canvas GladiatorArena()
        {
            var ic = new IsoCanvas(5, 104);
            Color stone = new Color(173, 132, 84);
            ic.Top(0.08f, 0.08f, 4.92f, 4.92f, 0, new Color(174, 123, 75));
            ic.Top(0.65f, 0.65f, 4.35f, 4.35f, 0, new Color(196, 159, 108));
            ic.Top(1.05f, 1.05f, 3.95f, 3.95f, 0, new Color(143, 94, 58));
            ic.Box(0.18f, 0.18f, 4.82f, 0.44f, 0, 43, stone, 5, Pal.Plaster);
            ic.Box(0.18f, 0.18f, 0.44f, 4.82f, 0, 43, stone, 5, Pal.Plaster);
            ic.Box(4.56f, 0.18f, 4.82f, 4.82f, 0, 43, stone, 5, Pal.Plaster);
            ic.Box(0.18f, 4.56f, 2.1f, 4.82f, 0, 43, stone, 5, Pal.Plaster);
            ic.Box(3.0f, 4.56f, 4.82f, 4.82f, 0, 43, stone, 5, Pal.Plaster);
            ic.QuadSW(1.45f, 3.55f, 3.05f, 24, 28, Pal.Red);
            ic.QuadSE(3.55f, 1.45f, 3.05f, 24, 28, Canvas.Shade(Pal.Red, 0.7f));
            ic.C.Outline(Outline, 0.45f);
            return ic.C;
        }

        // =====================================================================
        //  MADRASA (4x4) — ichki hovli, peshtoq, burchak gumbazlari
        // =====================================================================
        public static Canvas Madrasa()
        {
            var ic = new IsoCanvas(4, 96);
            Color w = Pal.BrickPale;
            const float z = 34;
            ic.Top(0.05f, 0.05f, 3.95f, 3.95f, 0, Pal.Paving);

            // orqa va chap qanot
            ic.Box(0.2f, 0.2f, 3.8f, 1.0f, 0, z, w, 6, Pal.Plaster);
            ic.Box(0.2f, 1.0f, 1.0f, 3.0f, 0, z, w, 6, Pal.Plaster);
            foreach (float x in new[] { 1.4f, 2.0f, 2.6f })
            {
                ic.FramedArch(true, x, 1.0f, 0.2f, 2, 15, Pal.Turquoise, Pal.Door);
                ic.FramedArch(true, x, 1.0f, 0.2f, 18, 31, Pal.Turquoise, Pal.Door);
            }
            foreach (float y in new[] { 1.4f, 2.0f, 2.6f })
            {
                ic.FramedArch(false, y, 1.0f, 0.2f, 2, 15, Pal.Turquoise, Pal.Door);
                ic.FramedArch(false, y, 1.0f, 0.2f, 18, 31, Pal.Turquoise, Pal.Door);
            }
            // orqa burchak gumbazlari
            ic.Cylinder(0.6f, 0.6f, z, z + 6, 17, Pal.Plaster);
            ic.Dome(0.6f, 0.6f, z + 6, 18, 22, Pal.Turquoise, 8);
            ic.Cylinder(3.4f, 0.6f, z, z + 6, 17, Pal.Plaster);
            ic.Dome(3.4f, 0.6f, z + 6, 18, 22, Pal.Turquoise, 8);

            // ichki hovli
            ic.Top(1.0f, 1.0f, 3.0f, 3.0f, 0, new Color(214, 188, 146));
            Vector2 t = ic.P(2.0f, 2.0f, 0);
            DecorArt.DrawTree(ic.C, t.X, t.Y, 1, 0.75f);

            // o'ng va old qanot
            ic.Box(3.0f, 1.0f, 3.8f, 3.0f, 0, z, w, 6, Pal.Plaster);
            ic.Box(0.2f, 3.0f, 3.8f, 3.8f, 0, z, w, 6, Pal.Plaster);
            foreach (float x in new[] { 0.62f, 1.1f, 2.9f, 3.38f })
            {
                ic.FramedArch(true, x, 3.8f, 0.17f, 2, 15, Pal.Turquoise, Pal.Door);
                ic.FramedArch(true, x, 3.8f, 0.17f, 18, 31, Pal.Turquoise, Pal.Door);
            }
            foreach (float y in new[] { 0.6f, 1.15f, 1.7f, 2.25f, 2.8f, 3.35f })
            {
                ic.FramedArch(false, y, 3.8f, 0.17f, 2, 15, Pal.Turquoise, Pal.Door);
                ic.FramedArch(false, y, 3.8f, 0.17f, 18, 31, Pal.Turquoise, Pal.Door);
            }

            // guldasta -> peshtoq -> guldasta
            Minaret(ic, 0.34f, 3.7f, 58, 7);
            ic.Box(1.4f, 3.6f, 2.6f, 3.97f, 0, 58, Pal.Brick, 6, Pal.BrickPale);
            ic.QuadSW(1.4f, 2.6f, 3.97f, 50, 55, Pal.Blue);
            ic.QuadSW(1.5f, 2.5f, 3.97f, 52, 53, Pal.Gold);
            ic.Arch(true, 2.0f, 3.97f, 0.48f, 0, 47, Pal.Turquoise);
            ic.Arch(true, 2.0f, 3.97f, 0.39f, 0, 42, Pal.Blue);
            ic.Arch(true, 2.0f, 3.97f, 0.3f, 0, 37, new Color(34, 26, 40));
            ic.Arch(true, 2.0f, 3.97f, 0.16f, 0, 20, Pal.Wood);
            Minaret(ic, 3.68f, 3.7f, 58, 7);

            ic.C.Outline(Outline);
            return ic.C;
        }

        // =====================================================================
        //  JOME MASJIDI (4x4) — ko'k gumbaz, ayvon, baland minora
        // =====================================================================
        public static Canvas Mosque()
        {
            var ic = new IsoCanvas(4, 132);
            Color w = Pal.Plaster;
            ic.Top(0.05f, 0.05f, 3.95f, 3.95f, 0, Pal.Paving);
            ic.Box(0.05f, 0.05f, 3.95f, 0.18f, 0, 9, Pal.Mud);
            ic.Box(0.05f, 0.18f, 0.18f, 3.95f, 0, 9, Pal.Mud);

            // xonaqoh
            ic.Box(0.4f, 0.4f, 3.0f, 2.4f, 0, 38, w, 6);
            ic.Parapet(0.4f, 0.4f, 3.0f, 2.4f, 38, w, 0.12f);
            ic.QuadSW(0.4f, 3.0f, 2.4f, 31, 35, Pal.Blue);
            ic.QuadSE(3.0f, 0.4f, 2.4f, 31, 35, Canvas.Shade(Pal.Blue, 0.72f));
            foreach (float x in new[] { 0.9f, 1.7f, 2.5f })
                ic.FramedArch(true, x, 2.4f, 0.26f, 0, 27, Pal.Turquoise, Pal.Door);
            foreach (float y in new[] { 0.9f, 1.75f })
                ic.FramedArch(false, y, 3.0f, 0.26f, 0, 27, Pal.Turquoise, Pal.Door);

            ic.Cylinder(1.7f, 1.4f, 38, 50, 30, Pal.Plaster);
            ic.Band(1.7f, 1.4f, 42, 47, 30, Pal.Turquoise);
            ic.Dome(1.7f, 1.4f, 50, 32, 44, Pal.Blue, 12);

            // ayvon: o'yma ustunlar va tekis tom
            for (float x = 0.5f; x <= 2.95f; x += 0.6f)
                ic.Pole(x, 3.1f, 0, 25, Pal.Wood, 3);
            ic.Box(0.4f, 2.4f, 3.0f, 3.2f, 25, 29, Pal.Wood, 0, new Color(196, 164, 120));
            ic.QuadSW(0.4f, 3.0f, 3.2f, 25, 26, Pal.Gold);

            // daraxt
            Vector2 t = ic.P(0.7f, 3.6f, 0);
            DecorArt.DrawTree(ic.C, t.X, t.Y, 2, 0.7f);

            // minora
            const float mx = 3.42f, my = 3.42f;
            ic.Box(3.1f, 3.1f, 3.74f, 3.74f, 0, 8, Pal.Brick, 4);
            ic.Cylinder(mx, my, 8, 88, 10, Pal.BrickPale, 6);
            ic.Band(mx, my, 30, 35, 10, Pal.Turquoise);
            ic.Band(mx, my, 56, 61, 10, Pal.Blue);
            ic.Band(mx, my, 78, 82, 10, Pal.Turquoise);
            ic.Cylinder(mx, my, 88, 92, 14, Pal.Plaster);
            ic.Cylinder(mx, my, 92, 104, 8, Pal.Plaster);
            Vector2 l = ic.P(mx, my, 96);
            ic.C.FillRect((int)l.X - 5, (int)l.Y - 4, 2, 6, Pal.Door);
            ic.C.FillRect((int)l.X + 1, (int)l.Y - 3, 2, 6, Pal.Door);
            ic.Dome(mx, my, 104, 9, 13, Pal.Turquoise, 0, true);

            ic.Box(3.82f, 0.18f, 3.95f, 3.0f, 0, 9, Pal.Mud);
            ic.Box(0.18f, 3.82f, 3.0f, 3.95f, 0, 9, Pal.Mud);
            ic.C.Outline(Outline);
            return ic.C;
        }

        // =====================================================================
        //  KARVONSAROY (4x4) — qal'asimon, ichki hovlili
        // =====================================================================
        public static Canvas Caravanserai()
        {
            var ic = new IsoCanvas(4, 62);
            Color w = Pal.MudDark;
            const float z = 27;
            ic.Top(0.05f, 0.05f, 3.95f, 3.95f, 0, new Color(200, 172, 128));

            ic.Box(0.2f, 0.2f, 3.8f, 0.95f, 0, z, w, 6, Pal.Mud);
            ic.Box(0.2f, 0.95f, 0.95f, 3.05f, 0, z, w, 6, Pal.Mud);
            foreach (float x in new[] { 1.35f, 2.0f, 2.65f })
                ic.FramedArch(true, x, 0.95f, 0.22f, 0, 20, Pal.BrickPale, Pal.Door);
            foreach (float y in new[] { 1.35f, 2.0f, 2.65f })
                ic.FramedArch(false, y, 0.95f, 0.22f, 0, 20, Pal.BrickPale, Pal.Door);
            Corner(ic, 0.2f, 0.2f, z, w);
            Corner(ic, 3.35f, 0.2f, z, w);

            // hovli: quduq va yuklar
            ic.Top(0.95f, 0.95f, 3.05f, 3.05f, 0, new Color(212, 186, 142));
            ic.Cylinder(2.0f, 1.9f, 0, 6, 8, Pal.Stone);
            Vector2 q = ic.P(2.0f, 1.9f, 6);
            ic.C.FillEllipse(q.X, q.Y, 5.5f, 2.8f, new Color(40, 50, 62));
            ic.Box(1.3f, 2.3f, 1.6f, 2.6f, 0, 8, new Color(150, 106, 62), 4);
            ic.Box(2.5f, 2.5f, 2.75f, 2.75f, 0, 7, new Color(166, 120, 70), 4);

            ic.Box(3.05f, 0.95f, 3.8f, 3.05f, 0, z, w, 6, Pal.Mud);
            ic.Box(0.2f, 3.05f, 3.8f, 3.8f, 0, z, w, 6, Pal.Mud);
            // katta darvoza (SE tomonda)
            ic.Box(3.7f, 1.5f, 3.95f, 2.5f, 0, z + 12, Pal.Brick, 6, Pal.BrickPale);
            ic.Arch(false, 2.0f, 3.95f, 0.36f, 0, 30, Canvas.Shade(Pal.Turquoise, 0.8f));
            ic.Arch(false, 2.0f, 3.95f, 0.27f, 0, 25, Pal.Door);
            ic.QuadSE(3.95f, 1.55f, 2.45f, 33, 36, Canvas.Shade(Pal.Blue, 0.75f));
            // SW devoridagi tor tuynuklar
            foreach (float x in new[] { 0.8f, 1.4f, 2.0f, 2.6f, 3.2f })
                ic.QuadSW(x - 0.03f, x + 0.03f, 3.8f, 13, 21, Pal.Door);
            Corner(ic, 0.2f, 3.35f, z, w);
            Corner(ic, 3.35f, 3.35f, z, w);
            ic.Pole(3.57f, 3.57f, z + 8, z + 26, Pal.Wood, 1);
            ic.Flag(3.57f, 3.57f, z + 26, new Color(214, 130, 44));

            ic.C.Outline(Outline);
            return ic.C;
        }

        private static void Corner(IsoCanvas ic, float x, float y, float z, Color w)
        {
            ic.Box(x - 0.05f, y - 0.05f, x + 0.5f, y + 0.5f, 0, z + 8, w, 6, Pal.Mud);
            ic.QuadSW(x - 0.05f, x + 0.5f, y + 0.5f, z + 1, z + 4, Pal.Turquoise);
            ic.QuadSE(x + 0.5f, y - 0.05f, y + 0.5f, z + 1, z + 4, Canvas.Shade(Pal.Turquoise, 0.72f));
        }
    }
}
