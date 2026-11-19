using System;
using AndijonSaltanatlar.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.Environment
{
    /// <summary>Fuqarolarning havo hissi.</summary>
    public enum Comfort { Qulay, Sovuq, JudaSovuq, Issiq }

    /// <summary>
    /// Fasl ko'rinishi: yer va o't rangi, daraxt barglari, qor qatlami va havo hissi.
    /// Bahor — yam-yashil va yomg'irli, yoz — quyoshli, kuz — sariq barglar to'kiladi, qish — hamma joy oppoq.
    /// Faqat ko'rinish (iqtisod haqiqiy faslga bog'liq). Game1 har kadr Current va Weather ni o'rnatadi.
    /// </summary>
    public static class SeasonVisuals
    {
        public static Season Current = Season.Bahor;
        public static Weather Weather = Weather.Clear;
        /// <summary>Ko'rinadigan havo hissi (fuqarolar rangi va titrashi uchun) — Game1 har kadr o'rnatadi.</summary>
        public static Comfort CurrentComfort = Comfort.Qulay;

        /// <summary>Shamol kuchi (0..1): daraxt va butalar shu darajada tebranib egiladi. Game1 har kadr o'rnatadi.</summary>
        public static float Wind = 0.25f;

        /// <summary>Qor qatlami kuchi (0..1): qishda yer va tomlar oqaradi.</summary>
        public static float Snow => Current == Season.Qish ? (Weather == Weather.Snowy ? 0.82f : 0.68f) : 0f;

        /// <summary>Yer kataklari uchun rang (multiply tint).</summary>
        public static Color TileTint(TileType t)
        {
            switch (Current)
            {
                case Season.Bahor:
                    // Bahor: yam-yashil, nam o't
                    return t == TileType.Grass ? new Color(150, 216, 128) : Color.White;
                case Season.Yoz:
                    // Yoz: to'q, quyoshda pishgan yashil (sarg'ish emas), tuproq biroz iliq
                    return t == TileType.Grass ? new Color(170, 205, 120) : t == TileType.Ground ? new Color(246, 230, 196) : Color.White;
                case Season.Kuz:
                    // Kuz: oltin-sarg'ish, qurigan o't
                    return t == TileType.Grass ? new Color(224, 190, 112) : t == TileType.Ground ? new Color(234, 210, 168) : Color.White;
                default:
                    // Qish: hamma joy oppoq-ko'kimtir, muz
                    return t == TileType.Water ? new Color(196, 222, 246) : new Color(226, 234, 248);
            }
        }

        /// <summary>Qor kataklarga qancha tushadi (yo'l va maydonda kamroq — odamlar yuradi).</summary>
        public static float TileSnow(TileType t)
        {
            float s = Snow;
            if (s <= 0f) return 0f;
            switch (t)
            {
                case TileType.Water: return s * 0.35f;  // muz
                case TileType.Road: return s * 0.55f;
                case TileType.Plaza: return s * 0.6f;
                default: return s;
            }
        }

        public static bool IsFoliage(DecorationType t) =>
            t == DecorationType.TreePoplar || t == DecorationType.TreeChinor || t == DecorationType.TreeApricot ||
            t == DecorationType.TreeCypress || t == DecorationType.Bush || t == DecorationType.Flowers;

        /// <summary>Daraxt/buta rangi: bahor yashil, yoz to'q, kuz sariq-qizg'ish, qish kulrang-oq.</summary>
        public static Color FoliageTint(DecorationType t, int seed)
        {
            switch (Current)
            {
                case Season.Bahor:
                    return t == DecorationType.TreeApricot ? new Color(255, 214, 230) : new Color(188, 240, 170);
                case Season.Yoz:
                    // Yoz: to'q, quyuq yashil barglar
                    return new Color(150, 198, 120);
                case Season.Kuz:
                    // har daraxt biroz boshqacha: sariq, to'q sariq, qizg'ish
                    int k = (seed * 7919) & 3;
                    if (t == DecorationType.TreeCypress) return new Color(214, 214, 150); // sarv doim yashil, biroz so'lg'in
                    return k == 0 ? new Color(255, 214, 90) : k == 1 ? new Color(255, 170, 70) : k == 2 ? new Color(236, 140, 70) : new Color(255, 196, 110);
                default:
                    return new Color(214, 222, 236);
            }
        }

        /// <summary>Havo hissi: fasl, ob-havo va kun vaqtiga bog'liq.</summary>
        public static Comfort ComfortOf(Season s, Weather w, float dayFraction)
        {
            bool night = dayFraction < 0.2f || dayFraction > 0.8f;
            if (s == Season.Qish) return w == Weather.Snowy || night ? Comfort.JudaSovuq : Comfort.Sovuq;
            if (s == Season.Yoz) return !night && w == Weather.Clear && dayFraction > 0.4f && dayFraction < 0.7f ? Comfort.Issiq : Comfort.Qulay;
            if (s == Season.Kuz && night && w == Weather.Rainy) return Comfort.Sovuq;
            return Comfort.Qulay;
        }

        public static string ComfortName(Comfort c)
        {
            switch (c)
            {
                case Comfort.Sovuq: return "Sovuq";
                case Comfort.JudaSovuq: return "Juda sovuq";
                case Comfort.Issiq: return "Issiq";
                default: return "Qulay";
            }
        }

        /// <summary>Kunlik baxt ta'siri (havo hissidan).</summary>
        public static float HappinessDelta(Comfort c)
        {
            switch (c)
            {
                case Comfort.Sovuq: return -0.6f;
                case Comfort.JudaSovuq: return -1.5f;
                case Comfort.Issiq: return -0.5f;
                default: return 0.3f;
            }
        }

        /// <summary>Fuqaro rangi (sovuqda ko'kimtir, issiqda qizarib ketgan).</summary>
        public static Color CitizenTint(Comfort c)
        {
            switch (c)
            {
                case Comfort.JudaSovuq: return new Color(200, 220, 255);
                case Comfort.Sovuq: return new Color(226, 236, 255);
                case Comfort.Issiq: return new Color(255, 226, 210);
                default: return Color.White;
            }
        }

        // ------------------------------------------------------------------ zarralar (ekran fazosi)
        /// <summary>Faslga xos zarralar: kuzda barglar, qishda qor, bahorda gulbarglar, yozda quyosh nuri.</summary>
        public static void DrawParticles(SpriteBatch sb, Texture2D px, int vw, int vh, float time, int count, float night)
        {
            switch (Current)
            {
                case Season.Kuz: DrawLeaves(sb, px, vw, vh, time, Math.Max(12, count / 3)); break;
                case Season.Qish:
                    if (Weather != Weather.Snowy) DrawFlurries(sb, px, vw, vh, time, Math.Max(12, count / 3));
                    sb.Draw(px, new Rectangle(0, 0, vw, vh), new Color(220, 232, 255) * 0.07f);
                    break;
                case Season.Bahor: DrawPetals(sb, px, vw, vh, time, Math.Max(6, count / 8)); break;
                case Season.Yoz:
                    if (night < 0.3f)
                    {
                        // Yozgi quyosh: iliq nur va havoda chang zarralari
                        float a = 0.09f * (1f - night / 0.3f);
                        sb.Draw(px, new Rectangle(0, 0, vw, vh), new Color(255, 214, 120) * a);
                        DrawMotes(sb, px, vw, vh, time, Math.Max(8, count / 6));
                    }
                    break;
            }
        }

        private static void DrawLeaves(SpriteBatch sb, Texture2D px, int vw, int vh, float time, int n)
        {
            Color[] cols = { new Color(232, 168, 52), new Color(214, 110, 40), new Color(180, 82, 36), new Color(246, 204, 80) };
            for (int i = 0; i < n; i++)
            {
                float sx = Hash(i * 3 + 7) * vw;
                float speed = 38f + Hash(i * 11) * 30f;
                float y = (time * speed + Hash(i) * (vh + 40)) % (vh + 40) - 20;
                float sway = MathF.Sin(time * 1.6f + i) * 26f;
                float x = (sx + sway + y * 0.18f + vw) % vw;
                float rot = MathF.Sin(time * 3f + i * 1.7f) * 1.2f;
                int s = 3 + (int)(Hash(i * 5) * 3);
                sb.Draw(px, new Vector2(x, y), null, cols[i & 3] * 0.95f, rot, new Vector2(0.5f, 0.5f), new Vector2(s + 1, s - 1), SpriteEffects.None, 0f);
            }
        }

        private static void DrawFlurries(SpriteBatch sb, Texture2D px, int vw, int vh, float time, int n)
        {
            var col = new Color(240, 244, 252, 170);
            for (int i = 0; i < n; i++)
            {
                float sx = Hash(i * 13 + 3) * vw;
                float y = (time * 40f + Hash(i * 2) * vh) % (vh + 20) - 10;
                float x = (sx + MathF.Sin((time + i) * 0.7f) * 18f + vw) % vw;
                sb.Draw(px, new Rectangle((int)x, (int)y, 2, 2), col);
            }
        }

        private static void DrawPetals(SpriteBatch sb, Texture2D px, int vw, int vh, float time, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float sx = Hash(i * 17 + 5) * vw;
                float y = (time * 26f + Hash(i * 4) * vh) % (vh + 20) - 10;
                float x = (sx + MathF.Sin(time * 1.2f + i) * 30f + vw) % vw;
                Color c = (i & 1) == 0 ? new Color(255, 214, 230) : new Color(255, 246, 250);
                sb.Draw(px, new Rectangle((int)x, (int)y, 3, 2), c * 0.9f);
            }
        }

        private static void DrawMotes(SpriteBatch sb, Texture2D px, int vw, int vh, float time, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float x = (Hash(i * 19 + 1) * vw + MathF.Sin(time * 0.4f + i) * 40f + vw) % vw;
                float y = (Hash(i * 23 + 9) * vh - time * 6f % vh + vh) % vh;
                float a = 0.25f + 0.25f * MathF.Sin(time * 2f + i);
                sb.Draw(px, new Rectangle((int)x, (int)y, 2, 2), new Color(255, 236, 170) * a);
            }
        }

        private static float Hash(int n)
        {
            unchecked
            {
                uint h = (uint)(n * 374761393 + 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65536f;
            }
        }
    }
}
