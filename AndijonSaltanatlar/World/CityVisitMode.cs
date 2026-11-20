using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Online;
using AndijonSaltanatlar.UI;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AndijonSaltanatlar.World
{
    /// <summary>
    /// EGALLANGAN SHAHARGA KIRISH: M xaritada o'z qasringizni tanlab "SHAHARGA KIRISH" — qamal qilgan o'sha shahar
    /// (bir xil urug') ochiladi, endi tinch: aholi yuradi, dushman yo'q. "KUCHAYTIRISH" tanga evaziga devor, darvoza va
    /// minoralarni bir pog'ona ko'taradi (ko'rinishi ham o'zgaradi), saroy ham bezaladi. Daraja LocalWorld'da saqlanadi
    /// va AI davlat hujum qilganda qasr himoyasini oshiradi. Esc / M / "XARITAGA QAYTISH" — xaritaga qaytish.
    /// </summary>
    public sealed class CityVisitMode
    {
        private readonly AssetManager _assets;
        private readonly GameStateManager _state;
        private readonly PixelFont _font;

        private MapManager _map;
        private BuildingManager _buildings;
        private UnitManager _units;
        private WorldRenderer _renderer;
        private CameraManager _camera;
        private float _time;
        private string _banner;
        private float _bannerTimer;
        private bool _lastTouch;
        private readonly List<(Rectangle Rect, string Key)> _buttons = new List<(Rectangle, string)>();

        public bool Active { get; private set; }
        public int RegionId { get; private set; }
        public string RegionName { get; private set; } = "";
        /// <summary>Joriy himoya darajasi (0..5).</summary>
        public int Level { get; private set; }

        /// <summary>Kuchaytirishni saqlaydi (Game1 → LocalWorld.Fortify): yangi daraja yoki -1.</summary>
        public Func<int, int> FortifyCommit;
        /// <summary>Shu shaharga kelayotgan dushman haqida matn (yo'q bo'lsa null) — HUD'da qizil ogohlantirish.</summary>
        public Func<string> ThreatText;

        public CityVisitMode(AssetManager assets, GameStateManager state, PixelFont font)
        {
            _assets = assets;
            _state = state;
            _font = font;
        }

        // ==================================================================
        // HIMOYA DARAJASI → BINOLAR
        // ==================================================================

        /// <summary>Himoya binosi: devor, darvoza, minora (mudofaa) va saroy.</summary>
        public static bool IsFortification(Building b) => b.Category == BuildingCategory.Defense || b.Type == BuildingType.MainHall;

        /// <summary>
        /// Himoya darajasiga mos bino darajasi: devor/darvoza/minora — daraja+1 (har kuchaytirish ko'rinib turadi),
        /// saroy — daraja (kamida 1). Eng ko'pi 5.
        /// </summary>
        public static int TargetLevel(BuildingType t, int fortify) =>
            t == BuildingType.MainHall ? Math.Clamp(fortify, 1, 5) : Math.Clamp(fortify + 1, 1, 5);

        /// <summary>Himoya binolarini darajaga ko'taradi (Building.Upgrade — mustahkamlik ham oshadi). Boshqa binolarga tegmaydi.</summary>
        public static void ApplyFortify(IEnumerable<Building> buildings, int fortify)
        {
            foreach (Building b in buildings)
            {
                if (!IsFortification(b)) continue;
                int target = TargetLevel(b.Type, fortify);
                while (b.Level < target) b.Upgrade();
            }
        }

        // ==================================================================
        // OCHISH / YOPISH
        // ==================================================================

        public bool Start(int regionId, string regionName, int fortifyLevel, Viewport viewport)
        {
            RegionCity.Build(_assets, regionId, out _map, out _buildings, out _units);
            _renderer = new WorldRenderer(_assets, _font);
            RegionId = regionId;
            RegionName = regionName ?? "";
            Level = Math.Clamp(fortifyLevel, 0, LocalWorld.MaxFortify);
            ApplyFortify(_buildings.Buildings, Level);

            // Kamera: ekranda eng pastdagi darvoza (devor, minora va darvoza birga ko'rinadi — kuchaytirish natijasi darhol seziladi),
            // biroz shahar ichkarisiga siljitilgan; darvoza bo'lmasa — shahar markazi
            var center = new Vector2(GameConfig.CityCenter, GameConfig.CityCenter);
            Building gate = _buildings.Buildings.Where(b => b.Type == BuildingType.Gate).OrderByDescending(b => b.Position.X + b.Position.Y).FirstOrDefault();
            Vector2 look = center;
            if (gate != null)
            {
                var g = new Vector2(gate.Position.X + 0.5f, gate.Position.Y + 0.5f);
                look = g + (center - g) * 0.12f;
            }
            _camera = new CameraManager(viewport, IsoMath.TileToWorld(look), 1.3f);
            _time = 0f;
            _buttons.Clear();
            Active = true;
            ShowBanner(RegionName.ToUpperInvariant() + " SHAHRIGA XUSH KELIBSIZ!", 3.5f);
            return true;
        }

        /// <summary>Xaritaga qaytish: vaqtinchalik shahar xotiradan bo'shatiladi (RegionId Game1 uchun qoladi).</summary>
        public void Close()
        {
            Active = false;
            _buttons.Clear();
            _map = null;
            _buildings = null;
            _units = null;
            _renderer = null;
            _camera = null;
        }

        // ==================================================================
        // YANGILASH
        // ==================================================================

        public void Update(float dt, InputManager input, Viewport viewport)
        {
            if (!Active) return;
            _time += dt;
            if (_bannerTimer > 0f) _bannerTimer -= dt;
            if (input != null)
            {
                _lastTouch = input.IsTouch;
                // Xarita oynasidan qolgan "UI ustida" bayrog'ini tozalaymiz — aks holda g'ildirak/ikki barmoq bilan zoom ishlamaydi
                input.MouseOverUI = false;
                Point mp = input.MouseVector.ToPoint();
                foreach (var b in _buttons)
                {
                    if (!b.Rect.Contains(mp)) continue;
                    input.MouseOverUI = true;
                    if (!input.LeftPressed) continue;
                    if (b.Key == "fortify") TryFortify();
                    else if (b.Key == "back") { Close(); return; }
                }
                if (input.KeyPressed(Keys.Escape) || input.KeyPressed(Keys.M)) { Close(); return; }
                if (input.LeftPressed && !input.MouseOverUI) TapWorld(input);
            }

            _units.Update(dt);
            _units.ApplyPhysics(_map, null, dt);
            _renderer.Tick(dt);
            _map.Update(dt);
            if (input != null) _camera.Update(dt, input, viewport);
        }

        /// <summary>Shaharda bosish: fuqaro salomlashadi, bino nomi va darajasini aytadi.</summary>
        private void TapWorld(InputManager input)
        {
            Vector2 world = _camera.ScreenToWorld(input.MouseVector);
            object hit = _renderer.PickAt(world, _map, _buildings, _units);
            if (hit is CitizenUnit c && !c.IsDown) c.Say("Xush kelibsiz, hukmdorim!", 3f);
            else if (hit is Building b)
                _state.ShowMessage(b.Name + " - " + b.Level + "-daraja" + (IsFortification(b) ? " (himoya)" : ""));
        }

        /// <summary>Himoyani bir daraja oshiradi: avval saqlanadi, keyin tanga yechiladi (saqlash rad etilsa — tanga yo'qolmaydi).</summary>
        public void TryFortify()
        {
            if (!Active) return;
            if (Level >= LocalWorld.MaxFortify) { _state.ShowMessage("Qal'a eng yuqori darajada (" + LocalWorld.MaxFortify + "/" + LocalWorld.MaxFortify + ")"); return; }
            int cost = LocalWorld.FortifyCost(Level);
            if (_state.Coins < cost) { _state.ShowMessage("Kuchaytirish uchun " + cost + " tanga kerak (sizda " + _state.Coins + ")", true); return; }
            int nl = FortifyCommit?.Invoke(RegionId) ?? -1;
            if (nl <= Level) { _state.ShowMessage("Bu qasrni hozir kuchaytirib bo'lmaydi", true); return; }
            _state.Coins -= cost;
            Level = nl;
            ApplyFortify(_buildings.Buildings, Level);
            ShowBanner("HIMOYA KUCHAYDI!  Daraja " + Level + "/" + LocalWorld.MaxFortify, 2.5f);
            _state.ShowMessage(RegionName + ": devor, darvoza va minoralar mustahkamlandi (-" + cost + " tanga)");
            _camera?.Shake(2f, 0.25f);
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
            if (!Active || _map == null) return;
            gd.Clear(new Color(26, 30, 22));
            Vector4 view = _camera.VisibleWorldRect;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, _camera.Transform);
            _map.Draw(sb, view);
            _renderer.Draw(sb, view, _map, _buildings, _units);
            sb.End();

            int k = Math.Max(1, (int)Math.Round(hudScale));
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            DrawHud(sb, vw, vh, k);
            sb.End();
        }

        private void DrawHud(SpriteBatch sb, int vw, int vh, int k)
        {
            Texture2D px = _assets.Pixel;
            int s = 2 * k, maxW = vw - 32 * k;
            int lh = PixelFont.GH * s + 4 * k;
            int max = LocalWorld.MaxFortify;

            // --- Yuqori: shahar, himoya darajasi, xazina ---
            string top = "SHAHAR: " + RegionName + " | Himoya darajasi: " + Level + "/" + max + " | Xazina: " + _state.Coins + " tanga";
            string sub = Level >= max
                ? "Qal'a eng mustahkam holatda - dushman hujumini doim qaytaradi"
                : "Devor, darvoza va minoralarni kuchaytiring - dushman hujumida himoya oshadi";
            var topLines = HudText.Wrap(_font, top, s, maxW, 2);
            var subLines = HudText.Wrap(_font, sub, s, maxW, 2);
            float wTop = Math.Max(topLines.Max(l => _font.Measure(l, s).X), subLines.Max(l => _font.Measure(l, s).X));
            var r = new Rectangle((int)(vw / 2f - wTop / 2 - 12 * k), 6 * k, (int)wTop + 24 * k, lh * (topLines.Count + subLines.Count) + 8 * k);
            sb.Draw(px, new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), new Color(216, 167, 83, 220));
            sb.Draw(px, r, new Color(24, 20, 18, 225));
            int ly = r.Y + 4 * k;
            foreach (string l in topLines) { _font.DrawCentered(sb, l, new Vector2(vw / 2f, ly + PixelFont.GH * s / 2f), new Color(240, 226, 196), s); ly += lh; }
            foreach (string l in subLines) { _font.DrawCentered(sb, l, new Vector2(vw / 2f, ly + PixelFont.GH * s / 2f), new Color(255, 214, 90), s); ly += lh; }
            int below = r.Bottom + 8 * k;

            // --- Shu shaharga dushman kelmoqda: miltillovchi qizil ogohlantirish ---
            string threat = ThreatText?.Invoke();
            if (!string.IsNullOrEmpty(threat))
            {
                float pulse = 0.8f + 0.2f * MathF.Sin(_time * 6f);
                Rectangle tr = HudText.Box(sb, px, _font, HudText.Wrap(_font, threat, s, maxW, 2), s, vw / 2f, below,
                    new Color(150, 30, 24) * pulse, Color.White, 12 * k, 6 * k, new Color(255, 200, 120));
                below = tr.Bottom + 8 * k;
            }

            // --- Banner va xabar ---
            if (_bannerTimer > 0f && _banner != null)
            {
                float a = Math.Min(1f, _bannerTimer / 0.6f);
                int bs = HudText.Scale(_font, _banner, 3 * k, 2 * k, maxW);
                HudText.Box(sb, px, _font, HudText.Wrap(_font, _banner, bs, maxW, 3), bs, vw / 2f, (int)(vh * 0.30f), new Color(28, 90, 46) * (0.92f * a), Color.White * a,
                    18 * k, 10 * k, new Color(240, 200, 110) * a);
            }
            if (_state.MessageTimer > 0f && !string.IsNullOrEmpty(_state.Message))
                HudText.Box(sb, px, _font, HudText.Wrap(_font, _state.Message, s, maxW, 3), s, vw / 2f, below,
                    _state.MessageIsError ? new Color(90, 26, 24, 220) : new Color(16, 14, 12, 200), new Color(255, 236, 190), 10 * k, 5 * k);

            // --- Pastki tugmalar: KUCHAYTIRISH va XARITAGA QAYTISH (tor ekranda ustma-ust) ---
            _buttons.Clear();
            bool full = Level >= max;
            int cost = LocalWorld.FortifyCost(Level);
            bool afford = _state.Coins >= cost;
            string fl = full ? "HIMOYA TO'LIQ (" + max + "/" + max + ")" : "KUCHAYTIRISH (" + cost + " tanga)";
            const string bl = "XARITAGA QAYTISH";
            int bh = 34 * k, gap = 10 * k;
            int W(string t) => (int)_font.Measure(t, s).X + 28 * k;
            int wf = W(fl), wb = W(bl);
            Rectangle rf, rb;
            if (wf + gap + wb <= maxW)
            {
                int x = vw / 2 - (wf + gap + wb) / 2, y = vh - bh - 14 * k;
                rf = new Rectangle(x, y, wf, bh);
                rb = new Rectangle(x + wf + gap, y, wb, bh);
            }
            else
            {
                int w = Math.Max(wf, wb), y = vh - 2 * bh - gap - 14 * k;
                rf = new Rectangle(vw / 2 - w / 2, y, w, bh);
                rb = new Rectangle(vw / 2 - w / 2, y + bh + gap, w, bh);
            }
            Color fillF = full ? new Color(60, 56, 50) : afford ? new Color(40, 110, 50) : new Color(110, 70, 30);
            DrawButton(sb, px, rf, fl, fillF, full ? new Color(170, 165, 155) : Color.White, full ? new Color(90, 86, 80) : new Color(236, 196, 110), s);
            if (!full) _buttons.Add((rf, "fortify"));
            DrawButton(sb, px, rb, bl, new Color(110, 70, 30), Color.White, new Color(236, 196, 110), s);
            _buttons.Add((rb, "back"));

            // Maslahat qatori (tugmalar ustida)
            string hint = _lastTouch
                ? "Barmoq bilan suring, ikki barmoq - yaqinlashtirish. Odam yoki binoga bosing"
                : "Sichqoncha bilan suring, g'ildirak - yaqinlashtirish, Esc - xaritaga qaytish";
            var hl = HudText.Wrap(_font, hint, s, maxW, 2);
            int hy = Math.Min(rf.Y, rb.Y) - (PixelFont.GH * s + 4) * hl.Count - 12 * k;
            HudText.Box(sb, px, _font, hl, s, vw / 2f, hy, new Color(16, 14, 12, 170), new Color(230, 220, 200), 8 * k, 3 * k);
        }

        private void DrawButton(SpriteBatch sb, Texture2D px, Rectangle r, string label, Color fill, Color text, Color border, int s)
        {
            sb.Draw(px, new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), border);
            sb.Draw(px, r, fill);
            _font.DrawCentered(sb, label, r.Center.ToVector2(), text, s);
        }
    }
}
