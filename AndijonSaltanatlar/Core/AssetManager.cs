using System;
using System.Collections.Generic;
using System.IO;
using AndijonSaltanatlar.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.Core
{
    /// <summary>Barcha sprite kalitlari. Fayl nomi = kalit + ".png".</summary>
    public static class SpriteKeys
    {
        public const string Tiles = "tiles";

        public const string House = "building_house";       // + _0.._3
        public const int HouseVariants = 4;
        public const string Tower = "building_tower";
        public const string Wall = "building_wall";
        public const string Gate = "building_gate";
        public const string Market = "building_market";
        public const string MainHall = "building_mainhall";
        public const string Barracks = "building_barracks";
        public const string Garden = "building_garden";
        public const string Farm = "building_farm";
        public const string GladiatorArena = "building_gladiator_arena";
        public const string Madrasa = "building_madrasa";
        public const string Mosque = "building_mosque";
        public const string Caravanserai = "building_caravanserai";

        public const string TreePoplar = "decor_tree_poplar";
        public const string TreeChinor = "decor_tree_chinor";
        public const string TreeApricot = "decor_tree_apricot";
        public const string TreeCypress = "decor_tree_cypress";
        public const string Bush = "decor_bush";
        public const string Flowers = "decor_flowers";
        public const string Stall = "decor_stall";           // + _0.._3
        public const int StallVariants = 4;
        public const string Hovuz = "decor_hovuz";
        public const string Well = "decor_well";
        public const string Brazier = "decor_brazier";
        public const string Banner = "decor_banner";
        public const string Crates = "decor_crates";
        public const string Dummy = "decor_dummy";
        public const string WeaponRack = "decor_weaponrack";
        public const string Tandir = "decor_tandir";
        public const string Sori = "decor_sori";

        public const string UnitSwordsman = "unit_swordsman"; // + _0.._2 (kadrlar)
        public const string UnitSpearman = "unit_spearman";
        public const string UnitShield = "unit_shield";
        public const string UnitPatrol = "unit_patrol";
        public const string UnitRaider = "unit_raider";           // bosqinchi
        public const string UnitRaiderChief = "unit_raider_chief"; // bosqinchilar sardori
        public const string UnitShah = "unit_shah";                // shoh (tojli)
        public const string UnitElite = "unit_elite";
        public const string UnitCavalry = "unit_cavalry";          // otliq (Bobur bobo modellaridan)              // xos navkar (VIP)
        public const string UnitCitizen = "unit_citizen"; // + <0..5> + _0.._2 (fuqaro turi + kadr)
        public const int CitizenKinds = CitizenArt.Kinds;
        public const int UnitFrames = 3;

        public const string IconCoin = "icon_coin";
        public const string IconFood = "icon_food";
        public const string IconPopulation = "icon_population";
    }

    /// <summary>
    /// Sprite'larni boshqaradi. Asosiy g'oya — "almashtiriladigan placeholder":
    ///   1) Content/Sprites/&lt;kalit&gt;.png bor bo'lsa — o'sha haqiqiy rasm yuklanadi;
    ///   2) bo'lmasa — kod bilan chizilgan placeholder ishlatiladi.
    /// Shunday qilib rassom tayyorlagan PNG'ni papkaga tashlash kifoya, kodga tegilmaydi.
    /// (Sprite langari: rasmning pastki-o'rta nuqtasi = obyekt izining janubiy burchagi.)
    /// </summary>
    public sealed class AssetManager
    {
        private readonly GraphicsDevice _gd;
        private readonly string _spriteDir;
        private readonly string _exportDir;
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<Texture2D, byte[]> _alpha = new Dictionary<Texture2D, byte[]>();

        /// <summary>1x1 oq piksel — UI to'rtburchaklari uchun.</summary>
        public Texture2D Pixel { get; private set; }
        /// <summary>GraphicsDevice — placeholder Animatsiya tizimida ToTexture chaqiruvi uchun.</summary>
        public GraphicsDevice GraphicsDevice => _gd;
        public string ContentDir { get; }

        public AssetManager(GraphicsDevice gd, string contentDir, string exportDir = null)
        {
            _gd = gd;
            ContentDir = contentDir;
            _spriteDir = Path.Combine(contentDir, "Sprites");
            _exportDir = exportDir;
            if (_exportDir != null) Directory.CreateDirectory(_exportDir);
        }

        public void LoadAll()
        {
            Pixel = new Texture2D(_gd, 1, 1);
            Pixel.SetData(new[] { Color.White });

            Register(SpriteKeys.Tiles, TileArt.BuildAtlas);

            for (int i = 0; i < SpriteKeys.HouseVariants; i++)
            {
                int v = i;
                Register(SpriteKeys.House + "_" + i, () => BuildingArt.House(v));
                // Darajali ko'rinishlar: key_l2..key_l5 — har daraja uyga xos (minora qo'shmaydi), uy kattalashadi
                RegisterLevels(SpriteKeys.House + "_" + i, 2, l => BuildingArt.House(v, l), 5);
            }
            Register(SpriteKeys.Tower, () => BuildingArt.Tower());
            Register(SpriteKeys.Wall, () => BuildingArt.Wall());
            Register(SpriteKeys.Gate, () => BuildingArt.Gate());
            RegisterLevels(SpriteKeys.Tower, 2, l => BuildingArt.Tower(l), 3);
            RegisterLevels(SpriteKeys.Wall, 1, l => BuildingArt.Wall(l), 3);
            RegisterLevels(SpriteKeys.Gate, 1, l => BuildingArt.Gate(l), 3);
            // Saroy: 3-daraja — maxsus rasm; 2, 4, 5 — umumiy daraja bezaklari
            Register(LevelKey(SpriteKeys.MainHall, 2), () => LevelArt.Compose(BuildingArt.MainHall(1), 6, 2));
            Register(LevelKey(SpriteKeys.MainHall, 3), () => BuildingArt.MainHall(3));
            Register(LevelKey(SpriteKeys.MainHall, 4), () => LevelArt.Compose(BuildingArt.MainHall(3), 6, 4));
            Register(LevelKey(SpriteKeys.MainHall, 5), () => LevelArt.Compose(BuildingArt.MainHall(3), 6, 5));
            Register(SpriteKeys.Market, BuildingArt.Market);
            Register(SpriteKeys.MainHall, () => BuildingArt.MainHall());
            Register(SpriteKeys.Barracks, BuildingArt.Barracks);
            Register(SpriteKeys.Garden, () => BuildingArt.Garden());
            Register(SpriteKeys.Farm, () => BuildingArt.Farm());
            Register(SpriteKeys.GladiatorArena, BuildingArt.GladiatorArena);
            Register(SpriteKeys.Madrasa, BuildingArt.Madrasa);
            Register(SpriteKeys.Mosque, BuildingArt.Mosque);
            Register(SpriteKeys.Caravanserai, BuildingArt.Caravanserai);
            // Qolgan barcha binolar ham 5-darajagacha kattalashadi
            RegisterLevels(SpriteKeys.Market, 4, _ => BuildingArt.Market());
            RegisterLevels(SpriteKeys.Barracks, 3, _ => BuildingArt.Barracks());
            RegisterLevels(SpriteKeys.Garden, 3, l => BuildingArt.Garden(l), 5); // tabiatga mos daraja (minora emas)
            RegisterLevels(SpriteKeys.Farm, 4, l => BuildingArt.Farm(l), 5);     // tabiatga mos daraja (minora emas)
            RegisterLevels(SpriteKeys.GladiatorArena, 5, _ => BuildingArt.GladiatorArena());
            RegisterLevels(SpriteKeys.Madrasa, 4, _ => BuildingArt.Madrasa());
            RegisterLevels(SpriteKeys.Mosque, 4, _ => BuildingArt.Mosque());
            RegisterLevels(SpriteKeys.Caravanserai, 4, _ => BuildingArt.Caravanserai());

            Register(SpriteKeys.TreePoplar, () => DecorArt.Tree(0));
            Register(SpriteKeys.TreeChinor, () => DecorArt.Tree(1));
            Register(SpriteKeys.TreeApricot, () => DecorArt.Tree(2));
            Register(SpriteKeys.TreeCypress, () => DecorArt.Tree(3));
            Register(SpriteKeys.Bush, DecorArt.Bush);
            Register(SpriteKeys.Flowers, DecorArt.Flowers);
            for (int i = 0; i < SpriteKeys.StallVariants; i++)
            {
                int v = i;
                Register(SpriteKeys.Stall + "_" + i, () => DecorArt.Stall(v));
            }
            Register(SpriteKeys.Hovuz, DecorArt.Hovuz);
            Register(SpriteKeys.Well, DecorArt.Well);
            Register(SpriteKeys.Brazier, DecorArt.Brazier);
            Register(SpriteKeys.Banner, DecorArt.Banner);
            Register(SpriteKeys.Crates, DecorArt.Crates);
            Register(SpriteKeys.Dummy, DecorArt.Dummy);
            Register(SpriteKeys.WeaponRack, DecorArt.WeaponRack);
            Register(SpriteKeys.Tandir, DecorArt.Tandir);
            Register(SpriteKeys.Sori, DecorArt.Sori);

            RegisterUnit(SpriteKeys.UnitSwordsman, UnitArt.Swordsman);
            RegisterUnit(SpriteKeys.UnitSpearman, UnitArt.Spearman);
            RegisterUnit(SpriteKeys.UnitShield, UnitArt.Shield);
            RegisterUnit(SpriteKeys.UnitPatrol, UnitArt.Patrol);
            // Otliq: Content/Sprites/unit_cavalry_*.png (3D modeldan); bo'lmasa patrul rasmi
            for (int f = 0; f < SpriteKeys.UnitFrames; f++)
            {
                int frame = f;
                Register(SpriteKeys.UnitCavalry + "_" + f, () => UnitArt.Frame(UnitArt.Patrol, frame));
                Register(SpriteKeys.UnitCavalry + "_b" + f, () => UnitArt.Frame(UnitArt.Patrol, frame));
            }
            for (int a = 0; a < UnitArt.AttackKinds; a++)
                for (int ph = 0; ph < UnitArt.AttackPhases; ph++)
                {
                    int aa = a, pp = ph;
                    Register(AttackKey(SpriteKeys.UnitCavalry, aa, pp), () => UnitArt.AttackFrame(UnitArt.Patrol, aa, pp));
                }
            RegisterWalk(SpriteKeys.UnitCavalry, fr => UnitArt.Frame(UnitArt.Patrol, fr));
            RegisterUnit(SpriteKeys.UnitRaider, UnitArt.Raider);
            RegisterUnit(SpriteKeys.UnitRaiderChief, UnitArt.RaiderChief);
            RegisterUnit(SpriteKeys.UnitShah, UnitArt.Shah);
            RegisterUnit(SpriteKeys.UnitElite, UnitArt.Elite);

            // Fuqarolar (har turi 3 kadr)
            for (int k = 0; k < SpriteKeys.CitizenKinds; k++)
                for (int f = 0; f < SpriteKeys.UnitFrames; f++)
                {
                    int kk = k, ff = f;
                    Register(SpriteKeys.UnitCitizen + kk + "_" + ff, () => CitizenArt.Frame(kk, ff));
                    if (ff == 0) RegisterWalk(SpriteKeys.UnitCitizen + kk, fr => CitizenArt.Frame(kk, fr));
                    string bk = SpriteKeys.UnitCitizen + kk + "_b" + ff;
                    if (File.Exists(Path.Combine(_spriteDir, bk + ".png")) || Bundled.Contains(bk))
                        Register(bk, () => CitizenArt.Frame(kk, ff));
                }

            Register("carry_sack", DecorArt.Sack);
            // Ekin bosqichlari (dehqon parvarishida o'sadi)
            for (int st = 0; st < 5; st++)
            {
                int s = st;
                Register("crop_" + s, () => DecorArt.Crop(s));
            }
            // Dehqonning ketmon chopishi: key_h0..h3 (orqa: key_bh0..bh3) — 3D modeldan, fayl bo'lsa
            for (int k = 0; k < SpriteKeys.CitizenKinds; k++)
                for (int i = 0; i < 4; i++)
                    foreach (string p in new[] { "_h", "_bh" })
                    {
                        string hk = SpriteKeys.UnitCitizen + k + p + i;
                        if (File.Exists(Path.Combine(_spriteDir, hk + ".png")) || Bundled.Contains(hk))
                            Register(hk, () => new Canvas(48, 60));
                    }
            Register(SpriteKeys.IconCoin, DecorArt.IconCoin);
            Register(SpriteKeys.IconFood, DecorArt.IconFood);
            Register(SpriteKeys.IconPopulation, DecorArt.IconPopulation);
        }

        private void RegisterUnit(string key, int kind)
        {
            RegisterWalk(key, fr => UnitArt.Frame(kind, fr));
            for (int f = 0; f < SpriteKeys.UnitFrames; f++)
            {
                int frame = f;
                Register(key + "_" + f, () => UnitArt.Frame(kind, frame));
                // Orqa ko'rinish: Content/Sprites/key_b{f}.png (3D modeldan); bo'lmasa — old rasm
                if (File.Exists(Path.Combine(_spriteDir, key + "_b" + f + ".png")) || Bundled.Contains(key + "_b" + f))
                    Register(key + "_b" + f, () => UnitArt.Frame(kind, frame));
            }
            if (kind == UnitArt.Shah)
            {
                // Shoh (Bobur) ham qilich bilan jang qiladi — 3D modeldan zarba kadrlari bo'lsa
                for (int a = 0; a < UnitArt.AttackKinds; a++)
                    for (int ph = 0; ph < UnitArt.AttackPhases; ph++)
                    {
                        int aa = a, pp = ph;
                        string k2 = AttackKey(key, aa, pp);
                        if (File.Exists(Path.Combine(_spriteDir, k2 + ".png")) || Bundled.Contains(k2))
                            Register(k2, () => UnitArt.AttackFrame(UnitArt.Swordsman, aa, pp));
                    }
                return;
            }
            // Zarba kadrlari: key_a{tur}_{bosqich}
            for (int a = 0; a < UnitArt.AttackKinds; a++)
                for (int ph = 0; ph < UnitArt.AttackPhases; ph++)
                {
                    int aa = a, pp = ph;
                    Register(AttackKey(key, aa, pp), () => UnitArt.AttackFrame(kind, aa, pp));
                }
        }

        /// <summary>Bino ko'rinishi uchun eng yuqori daraja.</summary>
        public const int MaxArtLevel = 5;
        /// <summary>Yuqori darajadagi bezak uslublari soni (bir xil binolar har xil ko'rinadi).</summary>
        public const int Styles = 4;

        /// <summary>Bino joyiga qarab doimiy uslub (0..Styles-1).</summary>
        public static int StyleOf(Point pos) => ((pos.X * 7 + pos.Y * 13) % Styles + Styles) % Styles;

        /// <summary>
        /// key_l2..key_l5: bespokeMax gacha — binoning o'z rasmi (baseOf(daraja)), undan yuqori — o'sha rasm
        /// ustiga umumiy daraja bezaklari (LevelArt: poydevor, minoralar, bayroqlar, chiroqlar).
        /// </summary>
        private void RegisterLevels(string key, int size, Func<int, Canvas> baseOf, int bespokeMax = 1)
        {
            for (int lv = 2; lv <= MaxArtLevel; lv++)
            {
                int l = lv;
                Register(LevelKey(key, l), () => l <= bespokeMax ? baseOf(l) : LevelArt.Compose(baseOf(bespokeMax), size, l, 0));
                if (l > bespokeMax)
                    for (int st = 1; st < Styles; st++)
                    {
                        int s = st;
                        Register(LevelKey(key, l) + "_s" + s, () => LevelArt.Compose(baseOf(bespokeMax), size, l, s));
                    }
            }
        }

        /// <summary>Bino darajasi ko'rinishi kaliti (masalan "house_2_l3").</summary>
        public static string LevelKey(string key, int level) => key + "_l" + level;

        /// <summary>Zarba kadri kaliti (masalan "unit_swordsman_a1_2").</summary>
        public static string AttackKey(string key, int attack, int phase) => key + "_a" + attack + "_" + phase;

        private readonly Dictionary<Texture2D, Texture2D> _flash = new Dictionary<Texture2D, Texture2D>();

        /// <summary>
        /// Sprite'ning oq silueti (zarba tekkan lahzadagi chaqnash uchun). Alfa niqobidan bir marta yasaladi —
        /// GetData kerak emas (telefonda ham ishlaydi).
        /// </summary>
        /// <summary>Yuqori aniqlikdagi rasmlar ko'paytmasi (o'yin pikseliga nisbatan).</summary>
        public const int HR = 3;
        private static readonly HashSet<Texture2D> HiRes = new HashSet<Texture2D>();

        /// <summary>Rasmni chizish masshtabi: oddiy — 1, 3D modeldan olingan aniq rasm — 1/HR.</summary>
        public static float ScaleOf(Texture2D t) => t != null && HiRes.Contains(t) ? 1f / HR : 1f;

        /// <summary>Odamlar (fuqaro, askar, shoh) chizilish masshtabi — real odamlarga nisbatan biroz kichikroq.</summary>
        public const float UnitScale = 0.88f;

        /// <summary>Oyoq nuqtasi rasm pastidan necha piksel yuqorida (rasm o'z piksellarida).</summary>
        public static float FootOf(Texture2D t) => UnitArt.FootOffset / ScaleOf(t);

        public Texture2D FlashOf(Texture2D tex)
        {
            if (tex == null) return null;
            if (_flash.TryGetValue(tex, out var w)) return w;
            if (!_alpha.TryGetValue(tex, out byte[] mask) || mask.Length != tex.Width * tex.Height) return null;
            var data = new Color[mask.Length];
            for (int i = 0; i < mask.Length; i++) { byte a = mask[i]; data[i] = new Color(a, a, a, a); }
            w = new Texture2D(_gd, tex.Width, tex.Height, false, SurfaceFormat.Color);
            w.SetData(data);
            if (HiRes.Contains(tex)) HiRes.Add(w);
            _flash[tex] = w;
            return w;
        }

        /// <summary>Sprite bo'lsa qaytaradi, bo'lmasa null (istisno tashlamaydi).</summary>
        public Texture2D TryGet(string key) => key != null && _textures.TryGetValue(key, out var tex) ? tex : null;

        private HashSet<string> _bundled;
        /// <summary>APK ichidagi spraytlar ro'yxati (Content/Sprites/sprites.txt); kompyuterda fayldan o'qiladi.</summary>
        private HashSet<string> Bundled
        {
            get
            {
                if (_bundled != null) return _bundled;
                _bundled = new HashSet<string>();
                if (File.Exists(Path.Combine(_spriteDir, "sprites.txt"))) return _bundled; // kompyuter: to'g'ridan-to'g'ri fayl
                try
                {
                    using var s = Microsoft.Xna.Framework.TitleContainer.OpenStream("Content/Sprites/sprites.txt");
                    using var r = new StreamReader(s);
                    string line;
                    while ((line = r.ReadLine()) != null) if (line.Trim().Length > 0) _bundled.Add(line.Trim());
                }
                catch (Exception) { }
                return _bundled;
            }
        }

        /// <summary>key_w0..w7 va key_bw0..bw7 (3D modeldan yurish sikli) — fayl bor bo'lsagina.</summary>
        private void RegisterWalk(string key, Func<int, Canvas> fallback)
        {
            for (int i = 0; i < 8; i++)
            {
                int ii = i;
                foreach (string p in new[] { "_w", "_bw" })
                {
                    string k = key + p + i;
                    if (File.Exists(Path.Combine(_spriteDir, k + ".png")) || Bundled.Contains(k))
                        Register(k, () => fallback(1 + ii % 2));
                }
            }
        }

        private void Register(string key, Func<Canvas> placeholder)
        {
            string path = Path.Combine(_spriteDir, key + ".png");
            Canvas canvas = null;
            if (File.Exists(path))
            {
                try { canvas = Canvas.FromPng(_gd, path); }
                catch (Exception) { canvas = null; } // buzuq fayl — placeholderga qaytamiz
            }
            else if (Bundled.Contains(key))
            {
                // Telefon: PNG APK ichida (TitleContainer); o'qib bo'lmasa — chizilgan rasm
                try
                {
                    using var s = Microsoft.Xna.Framework.TitleContainer.OpenStream("Content/Sprites/" + key + ".png");
#if ANDROID
                    // Android: PNG tizimning o'z dekoderi bilan o'qiladi (Texture2D.GetData'ga tayanmaydi)
                    using var bmp = Android.Graphics.BitmapFactory.DecodeStream(s);
                    int bw = bmp.Width, bh = bmp.Height;
                    var argb = new int[bw * bh];
                    bmp.GetPixels(argb, 0, bw, 0, 0, bw, bh);
                    var c = new Canvas(bw, bh);
                    for (int i = 0; i < argb.Length; i++)
                    {
                        int v = argb[i];
                        c.Pixels[i] = new Color((v >> 16) & 255, (v >> 8) & 255, v & 255, (v >> 24) & 255);
                    }
#else
                    using var btex = Texture2D.FromStream(_gd, s);
                    var c = new Canvas(btex.Width, btex.Height);
                    btex.GetData(c.Pixels);
#endif
                    canvas = c;
                }
                catch (Exception) { canvas = null; }
            }
            if (canvas == null)
            {
                canvas = placeholder();
                if (_exportDir != null) canvas.SavePng(_gd, Path.Combine(_exportDir, key + ".png"));
            }

            // 3D modeldan olingan yuqori aniqlikdagi odam rasmi (HR marta katta) — mipmapli, chizishda 1/HR masshtab
            bool hiRes = (key.StartsWith("unit_") && canvas.Height >= 120) || key.StartsWith("crop_") || key.StartsWith("carry_");
            Texture2D tex = hiRes ? canvas.ToTextureMipmapped(_gd) : canvas.ToTexture(_gd);
            if (hiRes) HiRes.Add(tex);
            tex.Name = key;
            _textures[key] = tex;

            var mask = new byte[canvas.Pixels.Length];
            for (int i = 0; i < mask.Length; i++) mask[i] = canvas.Pixels[i].A;
            _alpha[tex] = mask;
        }

        public Texture2D Get(string key)
        {
            if (_textures.TryGetValue(key, out var tex)) return tex;
            throw new KeyNotFoundException("Sprite topilmadi: " + key);
        }

        /// <summary>Sprite'ning berilgan pikseli shaffof emasmi (aniq tanlash uchun).</summary>
        public bool IsOpaque(Texture2D tex, int x, int y)
        {
            if (tex == null || (uint)x >= (uint)tex.Width || (uint)y >= (uint)tex.Height) return false;
            return _alpha.TryGetValue(tex, out var mask) && mask[y * tex.Width + x] > 40;
        }
    }
}
