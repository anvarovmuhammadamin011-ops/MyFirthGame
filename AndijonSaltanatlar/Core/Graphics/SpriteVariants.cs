using System;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.Core.Graphics
{
    /// <summary>Kiyimni qayta bo'yash usuli (bobur_hd.py dagi recolor() bilan bir xil).</summary>
    public enum RecolorMode { Shift, Dark, Gold, Grey }

    /// <summary>Qayta bo'yash retsepti: rang toni siljishi (gradus, ASL modelga nisbatan) va to'yinganlik/yorqinlik ko'paytmasi.</summary>
    public readonly struct Recolor
    {
        public readonly RecolorMode Mode;
        public readonly float Shift;
        public readonly float Sat;
        public readonly float Val;

        public Recolor(RecolorMode mode, float shift = 0f, float sat = 1f, float val = 1f)
        {
            Mode = mode; Shift = shift; Sat = sat; Val = val;
        }

        public static Recolor Hue(float shift, float sat = 1f, float val = 1f) => new Recolor(RecolorMode.Shift, shift, sat, val);
    }

    /// <summary>
    /// Yangi personaj oilasi = mavjud 3D-render oilasining kiyimi qayta bo'yalgan nusxasi. Asl spraytlar ham aynan shunday
    /// yasalgan (Blender: 4 ta model, qolganlari kiyim rangi bo'yicha variantlar), shuning uchun sifat va 31 ta animatsiya
    /// kadri to'liq saqlanadi, APK hajmi oshmaydi. Oila birinchi kerak bo'lganda (lazy) yasaladi.
    /// </summary>
    public sealed class VariantDef
    {
        /// <summary>Yangi oila kaliti (masalan "unit_citizen6", "unit_heavy").</summary>
        public string Key;
        /// <summary>Manba oila (masalan "unit_swordsman" — qo'riqchi modelining asl rangi).</summary>
        public string BaseKey;
        /// <summary>
        /// Manba o'zi bo'yalgan bo'lsa (fuqarolar): bir xil pozadagi boshqa bo'yalgan oila — ikkalasi faqat kiyim
        /// piksellarida farq qiladi, shundan kiyim niqobi aniq ajratiladi. null — manba asl rangda.
        /// </summary>
        public string DiffKey;
        /// <summary>Manba oilaga allaqachon qo'llangan rang siljishi (gradus).</summary>
        public float BaseShift;
        public Recolor Recipe;
    }

    public static class SpriteVariants
    {
        private const string Citizen0 = "unit_citizen0", Citizen1 = "unit_citizen1";
        private const float Citizen0Shift = 175f;   // bobur_hd.py: ("unit_citizen0", 175)

        private static VariantDef Cit(int kind, Recolor r) =>
            new VariantDef { Key = SpriteKeys.UnitCitizen + kind, BaseKey = Citizen0, DiffKey = Citizen1, BaseShift = Citizen0Shift, Recipe = r };

        private static VariantDef From(string key, string baseKey, Recolor r) =>
            new VariantDef { Key = key, BaseKey = baseKey, Recipe = r };

        /// <summary>
        /// Barcha hosila oilalar. Asl modellarda kiyim (to'n, qalpoq) ko'k ~225°; fuqarolar oilasi — kamonchi modeli, yoy va
        /// o'qdon yashirilgan. Ranglar mavjud fuqarolarnikidan (175, 140, 60, -100, 110, kulrang) ajralib turadigan tanlangan.
        /// </summary>
        public static readonly VariantDef[] All =
        {
            // ----- Fuqaro kasblari (CitizenArt 6..17) -----
            Cit(CitizenArt.Miner,      new Recolor(RecolorMode.Dark)),          // konchi: qorayib ketgan, chang kiyim
            Cit(CitizenArt.Fisher,     Recolor.Hue(-45f)),                     // baliqchi: feruza (suv rangi)
            Cit(CitizenArt.Storekeeper,Recolor.Hue(-170f, 1f, 0.8f)),           // omborchi: tuproq-sariq
            Cit(CitizenArt.TaxCollector,Recolor.Hue(25f, 1f, 0.62f)),          // soliqchi: to'q siyoh-ko'k
            Cit(CitizenArt.BazaarOwner,new Recolor(RecolorMode.Gold)),          // bozor egasi: zarbof to'n
            Cit(CitizenArt.Scholar,    Recolor.Hue(0f, 1f, 1.08f)),             // olim: Ulug'bek madrasasi ko'ki
            Cit(CitizenArt.Healer,     Recolor.Hue(-130f, 0.55f, 1.25f)),       // tabib: och yashil (dorivor o'tlar)
            Cit(CitizenArt.Architect,  Recolor.Hue(-180f, 0.35f, 1.3f)),        // me'mor: och qaymoq (oq to'n)
            Cit(CitizenArt.Builder,    Recolor.Hue(160f, 1f, 0.9f)),            // quruvchi: to'q sariq (g'isht)
            Cit(CitizenArt.Teacher,    Recolor.Hue(-75f, 0.8f, 0.7f)),          // ustoz: to'q zumrad
            Cit(CitizenArt.Official,   Recolor.Hue(130f, 1f, 0.7f)),            // amaldor: to'q qizil
            Cit(CitizenArt.Caravaneer, Recolor.Hue(155f, 0.8f, 0.6f)),          // karvonchi: yo'l changi, qo'ng'ir

            // ----- Yangi harbiy birliklar (asl modellardan) -----
            From("unit_archer",        SpriteKeys.UnitPatrol,    Recolor.Hue(-105f, 1f, 0.8f)),   // kamonchi: o'rmon yashili
            From("unit_herald",        SpriteKeys.UnitPatrol,    Recolor.Hue(185f, 1f, 1.1f)),    // xabarchi: yorqin sariq
            From("unit_marksman",      SpriteKeys.UnitPatrol,    Recolor.Hue(-45f, 1f, 0.95f)),   // mergan: temuriy feruza
            From("unit_musketeer",     SpriteKeys.UnitPatrol,    Recolor.Hue(130f, 1f, 0.75f)),   // tufangchi: to'q qizil
            From("unit_heavy",         SpriteKeys.UnitSwordsman, new Recolor(RecolorMode.Grey)),   // og'ir zirhli: po'lat-kulrang
            From("unit_watchman",      SpriteKeys.UnitSwordsman, Recolor.Hue(270f, 1f, 0.8f)),    // shahar qo'riqchisi: siyohrang
            From("unit_cavalry_elite", SpriteKeys.UnitCavalry,   new Recolor(RecolorMode.Gold)),   // elita otliq: oltin
            From("unit_scout",         SpriteKeys.UnitCavalry,   Recolor.Hue(120f, 0.7f, 0.85f)), // razvedkachi: dasht rangi
            new VariantDef { Key = "unit_engineer", BaseKey = Citizen0, DiffKey = Citizen1, BaseShift = Citizen0Shift,
                             Recipe = Recolor.Hue(150f, 0.6f, 0.5f) },                          // harbiy muhandis: zang-qo'ng'ir
        };

        public static VariantDef Find(string key)
        {
            if (key == null) return null;
            foreach (VariantDef v in All) if (v.Key == key) return v;
            return null;
        }

        /// <summary>
        /// Bir kadrni qayta bo'yaydi. Kiyim pikseli: diff bo'lsa — manba va diff farq qilgan joy; bo'lmasa (asl rangdagi manba) —
        /// to'yinganlik &gt; 0.22 va rang toni 10..50° dan tashqari (teri, charm, metall saqlanadi). Alfa o'zgarmaydi.
        /// </summary>
        public static void Apply(Color[] src, Color[] diff, Color[] dst, float baseShift, Recolor r)
        {
            bool useDiff = diff != null && diff.Length == src.Length;
            for (int i = 0; i < src.Length; i++)
            {
                Color c = src[i];
                if (c.A <= 2) { dst[i] = c; continue; }
                RgbToHsv(c, out float h, out float s, out float v);
                bool cloth;
                if (useDiff)
                {
                    Color d = diff[i];
                    cloth = Math.Abs(c.R - d.R) + Math.Abs(c.G - d.G) + Math.Abs(c.B - d.B) > 6;
                }
                else
                {
                    float hd = h * 360f;
                    cloth = s > 0.22f && !(hd > 10f && hd < 50f);
                }
                if (!cloth) { dst[i] = c; continue; }
                switch (r.Mode)
                {
                    case RecolorMode.Dark: h = 0f; s *= 0.45f; v *= 0.38f; break;
                    case RecolorMode.Gold: h = 0.12f; s = Math.Max(s, 0.7f); v = Math.Min(1f, v * 1.2f + 0.08f); break;
                    case RecolorMode.Grey: s *= 0.12f; v = 0.5f + v * 0.45f; break;
                    default:
                        h = (h + (r.Shift - baseShift) / 360f) % 1f;
                        if (h < 0f) h += 1f;
                        break;
                }
                s = Math.Clamp(s * r.Sat, 0f, 1f);
                v = Math.Clamp(v * r.Val, 0f, 1f);
                dst[i] = HsvToRgb(h, s, v, c.A);
            }
        }

        public static void RgbToHsv(Color c, out float h, out float s, out float v)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            float d = max - min;
            v = max;
            s = max <= 0f ? 0f : d / max;
            if (d <= 0f) { h = 0f; return; }
            if (max == r) h = (g - b) / d % 6f;
            else if (max == g) h = (b - r) / d + 2f;
            else h = (r - g) / d + 4f;
            h /= 6f;
            if (h < 0f) h += 1f;
        }

        public static Color HsvToRgb(float h, float s, float v, byte a)
        {
            float hh = (h % 1f + 1f) % 1f * 6f;
            int i = (int)MathF.Floor(hh) % 6;
            float f = hh - MathF.Floor(hh);
            float p = v * (1f - s), q = v * (1f - s * f), t = v * (1f - s * (1f - f));
            float r, g, b;
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return new Color((int)MathF.Round(r * 255f), (int)MathF.Round(g * 255f), (int)MathF.Round(b * 255f), (int)a);
        }
    }
}
