using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.World
{
    /// <summary>
    /// Dunyodagi barcha "tik" obyektlarni (bino, bezak, jangchi) chuqurlik bo'yicha saralab chizadi
    /// va sichqoncha ostidagi obyektni piksel aniqligida topadi.
    /// </summary>
    public sealed class WorldRenderer
    {
        private readonly List<IWorldObject> _buffer = new List<IWorldObject>(1024);
        private readonly AssetManager _assets;
        private readonly PixelFont _font;

        public int LastDrawnCount { get; private set; }

        private static readonly Vector2[] RimOffsets = { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) };

        public WorldRenderer(AssetManager assets, PixelFont font)
        {
            _assets = assets;
            _font = font;
        }

        private static int Compare(IWorldObject a, IWorldObject b)
        {
            int c = a.Depth.CompareTo(b.Depth);
            if (c != 0) return c;
            c = a.SortLayer.CompareTo(b.SortLayer);
            if (c != 0) return c;
            return a.DrawPosition.X.CompareTo(b.DrawPosition.X);
        }

        private static bool Visible(IWorldObject o, Vector4 view)
        {
            Vector2 p = o.DrawPosition;
            return p.X + o.Texture.Width >= view.X && p.X <= view.Z && p.Y + o.Texture.Height >= view.Y && p.Y <= view.W;
        }

        /// <param name="extra">Qo'shimcha obyektlar (masalan, yerdagi qushlar) — binolar bilan birga saralanadi.</param>
        public void Draw(SpriteBatch sb, Vector4 view, MapManager map, BuildingManager buildings, UnitManager units,
            IReadOnlyList<IWorldObject> extra = null)
        {
            _buffer.Clear();
            foreach (var b in buildings.Buildings) if (Visible(b, view)) _buffer.Add(b);
            foreach (var d in map.Decorations) if (Visible(d, view)) _buffer.Add(d);
            foreach (var u in units.Units) if (!u.Hidden && Visible(u, view)) _buffer.Add(u);
            if (extra != null) foreach (var e in extra) if (Visible(e, view)) _buffer.Add(e);
            _buffer.Sort(Compare);

            foreach (var o in _buffer)
            {
                Color tint = Color.White;
                Building bb = o as Building;
                if (bb != null && bb.BuildProgress < 1f)
                    tint = new Color(150, 180, 255) * (0.45f + 0.55f * bb.BuildProgress); // qurilayotgan bino
                if (o is Decoration dec && AndijonSaltanatlar.Environment.SeasonVisuals.IsFoliage(dec.Type))
                {
                    tint = AndijonSaltanatlar.Environment.SeasonVisuals.FoliageTint(dec.Type, dec.Position.X * 31 + dec.Position.Y);
                    // Shamolda tebranish: daraxt/buta tagidan (ildiz) ozgina egiladi
                    bool tree = dec.Type == DecorationType.TreePoplar || dec.Type == DecorationType.TreeChinor
                             || dec.Type == DecorationType.TreeApricot || dec.Type == DecorationType.TreeCypress;
                    float wind = AndijonSaltanatlar.Environment.SeasonVisuals.Wind;
                    float phase = dec.Position.X * 0.7f + dec.Position.Y * 1.3f;
                    float amp = tree ? 0.055f : 0.03f;
                    float sway = wind * (amp * MathF.Sin(_time * 1.9f + phase) + amp * 0.4f * MathF.Sin(_time * 3.3f + phase * 1.7f));
                    var origin = new Vector2(o.Texture.Width / 2f, o.Texture.Height);
                    Vector2 baseC = o.DrawPosition + origin;
                    SpriteEffects dfx = o.Flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                    sb.Draw(o.Texture, baseC, null, tint, sway, origin, 1f, dfx, 0f);
                    float sn = AndijonSaltanatlar.Environment.SeasonVisuals.Snow;
                    if (sn > 0f && _assets.FlashOf(o.Texture) is Texture2D dsil)
                        sb.Draw(dsil, baseC, null, new Color(246, 250, 255) * (sn * 0.55f), sway, origin, 1f, dfx, 0f);
                    continue;
                }

                if (o is Unit cu)
                {
                    // Fuqarolar havo hissini ko'rsatadi: sovuqda ko'kimtir, issiqda qizargan
                    if (cu is CitizenUnit) tint = AndijonSaltanatlar.Environment.SeasonVisuals.CitizenTint(AndijonSaltanatlar.Environment.SeasonVisuals.CurrentComfort);
                    // Zarba yegan: qizarish asta so'nadi
                    if (cu.HurtTimer > 0f) tint = Color.Lerp(tint, new Color(255, 116, 104), MathHelper.Clamp(cu.HurtTimer / 0.3f, 0f, 1f));
                    tint *= cu.FadeAlpha;
                    SpriteEffects fx = o.Flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                    Vector2 feet = IsoMath.TileToWorld(cu.Position);
                    // Yurganda tananing yengil ko'tarilib-tushishi (haqiqiy qadam hissi) — har kadr emas, qadam ritmida.
                    if (cu.State == AndijonSaltanatlar.Units.UnitState.Walking && !cu.IsDown)
                        feet.Y -= MathF.Abs(MathF.Sin(_time * 6.2f + cu.PerEntityPhaseOffset)) * 1.3f;
                    var origin = new Vector2(o.Texture.Width / 2f, o.Texture.Height - AssetManager.FootOf(o.Texture));
                    float k = AssetManager.ScaleOf(o.Texture) * AssetManager.UnitScale;
                    if (cu.IsDown)
                    {
                        // Yiqilish: tezlashib qulaydi, yerga tushib biroz sakraydi, keyin xira yotadi
                        float lie = MathHelper.Clamp(cu.DownTime / 0.45f, 0f, 1f);
                        Color down = Color.Lerp(tint, new Color(150, 140, 135) * cu.FadeAlpha, lie);
                        float ang = cu.FallAngle * (o.Flipped ? -1f : 1f);
                        Vector2 at = new Vector2(MathF.Round(feet.X), MathF.Round(feet.Y)) + cu.HitDirWorld * 4f * lie;
                        sb.Draw(o.Texture, at, null, down, ang, origin, k, fx, 0f);
                        if (cu.FlashTimer > 0f && _assets.FlashOf(o.Texture) is Texture2D wf)
                            sb.Draw(wf, at, null, Color.White * (cu.FlashTimer / Unit.FlashTime) * cu.FadeAlpha, ang, origin, k, fx, 0f);
                    }
                    else
                    {
                        // Zarba (kesish / chopish / sanchish / aylanma) va chayqalish: oyoq atrofida og'ish va siljish
                        cu.GetAttackPose(out Vector2 off, out float rot, out float sy, out bool spin);
                        cu.GetHitPose(out Vector2 hoff, out float hrot);
                        if (spin) fx = fx == SpriteEffects.None ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                        Vector2 at = new Vector2(MathF.Round(feet.X), MathF.Round(feet.Y)) + off + hoff;
                        int depth = map.WaterDepth((int)MathF.Floor(cu.Position.X), (int)MathF.Floor(cu.Position.Y));
                        float sinkNow = 0f;
                        if (depth > 0)
                        {
                            // Suvda: sayozda kechib (tizzagacha), chuqurda suzib (yelkagacha); tana suv sathida kesiladi
                            float bodyPx = (o.Texture.Height - AssetManager.FootOf(o.Texture)) * k;
                            float sink = bodyPx * (depth == 2 ? 0.62f : 0.3f) + (depth == 2 ? MathF.Sin(_time * 2.6f + cu.PerEntityPhaseOffset) * 1.2f : 0f);
                            sinkNow = sink;
                            int keepRows = (int)MathF.Max(1f, (o.Texture.Height - AssetManager.FootOf(o.Texture)) - sink / k);
                            var src = new Rectangle(0, 0, o.Texture.Width, keepRows);
                            var org = new Vector2(origin.X, keepRows);
                            Vector2 wat = at + new Vector2(0, -0f);
                            sb.Draw(o.Texture, wat + new Vector2(0, 0), src, tint, 0f, org, k, fx, 0f);
                            // to'lqin halqalari
                            float rip = (_time * 0.8f + cu.PerEntityPhaseOffset) % 1f;
                            DrawRing(sb, at, 9f + rip * 9f, new Color(220, 240, 255) * (0.55f * (1f - rip)));
                            DrawRing(sb, at, 7f, new Color(200, 230, 255) * 0.45f);
                        }
                        else
                        sb.Draw(o.Texture, at, null, tint, rot + hrot, origin, new Vector2(k, sy * k), fx, 0f);
                        // Yelkadagi narsa (hosil qopi)
                        if (cu.CarryKey != null && _assets.TryGet(cu.CarryKey) is Texture2D carry)
                        {
                            float ck = AssetManager.ScaleOf(carry) * AssetManager.UnitScale;
                            float side = o.Flipped ? 5f : -5f;
                            sb.Draw(carry, at + new Vector2(side, -30f * AssetManager.UnitScale + sinkNow), null, Color.White, 0f, new Vector2(carry.Width / 2f, carry.Height), ck, fx, 0f);
                        }
                        // Sug'orish: ekin ustiga suv tomchilari
                        if (cu.Watering)
                            for (int dI = 0; dI < 6; dI++)
                            {
                                float u2 = (_time * 1.6f + dI / 6f) % 1f;
                                float dxw = (o.Flipped ? -1f : 1f) * (8f + dI * 1.6f);
                                Vector2 dp = at + new Vector2(dxw + u2 * 2f, -14f + u2 * 16f);
                                sb.Draw(_assets.Pixel, new Rectangle((int)dp.X, (int)dp.Y, 1, 2), new Color(150, 200, 255) * (1f - u2 * 0.6f));
                            }
                        // Zarba tekkan lahza: oq chaqnash
                        if (cu.FlashTimer > 0f && _assets.FlashOf(o.Texture) is Texture2D wf)
                            sb.Draw(wf, at, null, Color.White * (cu.FlashTimer / Unit.FlashTime) * cu.FadeAlpha, rot + hrot, origin, new Vector2(k, sy * k), fx, 0f);
                    }
                    if (cu is CitizenUnit citizen && !cu.IsDown)
                    {
                        if (citizen.Kind != Core.Graphics.CitizenArt.Citizen) DrawCitizenRole(sb, o, citizen);   // oddiy aholi boshida yozuv yo'q
                        if (!string.IsNullOrEmpty(citizen.SpeechText)) DrawCitizenSpeech(sb, o, citizen);
                    }
                    if (cu.CanRank && cu.Rank > 0 && !cu.IsDown) DrawRankBadge(sb, o, cu);   // pagon
                    continue;
                }

                // Bino darajasi ko'rinishi (bor bo'lsa): 2/3-darajali uy, minora, devor, darvoza, saroy
                Texture2D tex = o.Texture;
                Vector2 pos = o.DrawPosition;
                if (bb != null && bb.Level >= 2 && bb.BuildProgress >= 1f)
                {
                    string lk = AssetManager.LevelKey(bb.SpriteKey, System.Math.Min(bb.Level, AssetManager.MaxArtLevel));
                    // Upgrade uslubi: har binoning tasodifiy Variant'i + joyi — bir xil binolar har xil bezaladi (gumbaz, minora, bayroq, chiroq).
                    int st = ((bb.Variant * 3 + AssetManager.StyleOf(bb.Position)) % AssetManager.Styles + AssetManager.Styles) % AssetManager.Styles;
                    Texture2D lt = (st > 0 ? _assets.TryGet(lk + "_s" + st) : null) ?? _assets.TryGet(lk);
                    if (lt != null) { tex = lt; pos = WorldObjectExtensions.AnchorBottomCenter(lt, bb.Position, bb.Size.X); }
                }

                // Yuqori darajali bino: shakli bo'ylab yupqa hoshiya (3-daraja — oltin, 2-daraja — kumush)
                if (bb != null && bb.Level >= 2 && bb.BuildProgress >= 1f && _assets.FlashOf(tex) is Texture2D sil)
                {
                    Color rim = bb.Level >= 3 ? new Color(236, 188, 82) * 0.6f : new Color(214, 218, 226) * 0.35f;
                    SpriteEffects fxb = o.Flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                    foreach (Vector2 d in RimOffsets)
                        sb.Draw(sil, pos + d, null, rim, 0f, Vector2.Zero, 1f, fxb, 0f);
                }

                if (bb != null && bb.Type == BuildingType.MainHall && bb.MaxHitPoints > 0 && bb.HitPoints < bb.MaxHitPoints)
                {
                    float dmg = 1f - bb.HitPoints / (float)bb.MaxHitPoints;
                    tint = Color.Lerp(tint, new Color(96, 82, 72), dmg * 0.9f);   // qoraygan, chang bosgan
                }
                sb.Draw(tex, pos, null, tint, 0f, Vector2.Zero, 1f,
                    o.Flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
                if (bb != null && bb.Type == BuildingType.MainHall && bb.HitPoints < bb.MaxHitPoints * 0.66f)
                    DrawSmoke(sb, pos + new Vector2(tex.Width / 2f, tex.Height * 0.25f), 1f - bb.HitPoints / (float)bb.MaxHitPoints, tex.Width);
                // Qish: tomlar va devorlar qor bilan oqaradi
                float bsnow = AndijonSaltanatlar.Environment.SeasonVisuals.Snow;
                if (bb != null && bsnow > 0f && _assets.FlashOf(tex) is Texture2D bsil)
                    sb.Draw(bsil, pos, null, new Color(244, 248, 255) * (bsnow * 0.3f), 0f, Vector2.Zero, 1f,
                        o.Flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);

                if (o is Farm farm && farm.BuildProgress >= 1f)
                {
                    DrawCrops(sb, farm);
                    DrawFarmStatus(sb, o, farm);
                }

                // Bino daraja nishoni (evolyutsiya belgisi) — BuildingEvolution
                if (o is Building b2 && b2.BuildProgress >= 1f)
                {
                    // (Bino daraja nuqtalari — "pips" — o'yinchi talabiga ko'ra olib tashlandi.)

                    // Kiriladigan bino ustida kichik eshik belgisi (Promt 7) — ichkariga kirish mumkinligини bildiradi.
                    if (AndijonSaltanatlar.Interiors.HouseInterior.IsEnterable(b2))
                    {
                        int dx = (int)(pos.X + tex.Width / 2f - 4f);
                        int dy = (int)(pos.Y - 13f);
                        sb.Draw(_assets.Pixel, new Rectangle(dx - 1, dy - 1, 10, 12), new Color(20, 20, 24, 200));   // ramka
                        sb.Draw(_assets.Pixel, new Rectangle(dx, dy, 8, 10), new Color(150, 96, 52));                 // eshik yog'ochi
                        sb.Draw(_assets.Pixel, new Rectangle(dx + 2, dy + 2, 4, 8), new Color(96, 60, 32));           // tabaqa
                        sb.Draw(_assets.Pixel, new Rectangle(dx + 5, dy + 5, 1, 2), new Color(222, 178, 92));         // tutqich
                    }
                }
            }
            LastDrawnCount = _buffer.Count;
        }

        private void DrawCitizenRole(SpriteBatch sb, IWorldObject unit, CitizenUnit citizen)
        {
            Vector2 size = _font.Measure(citizen.RoleLabel, 1, 0);
            int width = (int)size.X + 6;
            int height = (int)size.Y + 4;
            float headDrop = unit.Texture.Height * AssetManager.ScaleOf(unit.Texture) * (1f - AssetManager.UnitScale);
            int x = (int)MathF.Round(unit.DrawPosition.X + unit.Texture.Width * AssetManager.ScaleOf(unit.Texture) / 2f - width / 2f);
            int y = (int)MathF.Round(unit.DrawPosition.Y + headDrop - height - 2);
            sb.Draw(_assets.Pixel, new Rectangle(x, y, width, height), new Color(12, 35, 22, 220));
            _font.Draw(sb, citizen.RoleLabel, new Vector2(x + 3, y + 2), new Color(130, 245, 145), 1, 0);
        }

        /// <summary>Askar yelkasidagi pagon: unvon bo'yicha belgilar soni va rangi (kumush → oltin).
        /// Kichik zoomда faqat rangli nuqta.</summary>
        private void DrawRankBadge(SpriteBatch sb, IWorldObject unit, Unit u)
        {
            int pips = AndijonSaltanatlar.Military.MilitaryRank.Pips(u.Rank);
            if (pips <= 0 || unit.Texture == null) return;
            Color col = AndijonSaltanatlar.Military.MilitaryRank.PipColor(u.Rank);
            float scale = AssetManager.ScaleOf(unit.Texture);
            int w = (int)(unit.Texture.Width * scale);
            int h = (int)(unit.Texture.Height * scale);
            int cx = (int)MathF.Round(unit.DrawPosition.X + w * 0.60f);
            int cy = (int)MathF.Round(unit.DrawPosition.Y + h * 0.26f);
            if (w < 18)
            {
                sb.Draw(_assets.Pixel, new Rectangle(cx, cy, 3, 3), col); // uzoqdan — faqat nuqta
                return;
            }
            const int ps = 2, gap = 1;
            int bw = pips * (ps + gap) + 3;
            sb.Draw(_assets.Pixel, new Rectangle(cx - 2, cy - 2, bw, ps + 5), new Color(28, 22, 16, 205)); // pagon toni
            for (int i = 0; i < pips; i++)
                sb.Draw(_assets.Pixel, new Rectangle(cx + i * (ps + gap), cy, ps, ps + 2), col);
        }

        private void DrawCitizenSpeech(SpriteBatch sb, IWorldObject unit, CitizenUnit citizen)
        {
            Vector2 size = _font.Measure(citizen.SpeechText, 1, 0);
            int width = (int)size.X + 8;
            int height = (int)size.Y + 6;
            float headDrop = unit.Texture.Height * AssetManager.ScaleOf(unit.Texture) * (1f - AssetManager.UnitScale);
            int x = (int)MathF.Round(unit.DrawPosition.X + unit.Texture.Width * AssetManager.ScaleOf(unit.Texture) / 2f - width / 2f);
            int y = (int)MathF.Round(unit.DrawPosition.Y + headDrop - height - _font.Measure(citizen.RoleLabel, 1, 0).Y - 6);
            var box = new Rectangle(x, y, width, height);
            sb.Draw(_assets.Pixel, box, new Color(246, 230, 184));
            sb.Draw(_assets.Pixel, new Rectangle(x, y, width, 1), new Color(83, 54, 35));
            sb.Draw(_assets.Pixel, new Rectangle(x, y + height - 1, width, 1), new Color(83, 54, 35));
            _font.DrawCentered(sb, citizen.SpeechText, new Vector2(box.Center.X, box.Center.Y), new Color(60, 42, 30), 1, 0);
        }

        /// <summary>
        /// Dalada ekinlar: 5 qator x 9 tup. Ekilganda urug', keyin nihol -> yosh -> yashil -> pishgan oltin boshoq;
        /// o'rilganda dala yana bo'sh (haydalgan) qoladi. Yuqori darajali fermada poydevor balandligi hisobga olinadi.
        /// </summary>
        private void DrawCrops(SpriteBatch sb, Farm farm)
        {
            if (farm.CropStage == FarmCropStage.Empty) return;
            float g = farm.Growth;
            int stage = farm.CropStage == FarmCropStage.Mature ? 4 : g < 0.06f ? 0 : g < 0.3f ? 1 : g < 0.6f ? 2 : 3;
            Texture2D tex = _assets.TryGet("crop_" + stage);
            if (tex == null) return;
            float k = AssetManager.ScaleOf(tex);
            int podium = farm.Level >= 5 ? 10 : farm.Level >= 4 ? 7 : 0;
            // o'sish ichida biroz kattalashadi (bosqichlar orasida silliq)
            float grow = stage == 0 || stage == 4 ? 1f : 0.8f + 0.4f * ((g * 4f) % 1f) * 0.5f;
            var origin = new Vector2(tex.Width / 2f, tex.Height - 3 / k);
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 9; col++)
                {
                    float x = 0.56f + col * 0.36f, y = 0.46f + row * 0.62f;
                    Vector2 w = IsoMath.TileToWorld(new Vector2(farm.Position.X + x, farm.Position.Y + y)) + new Vector2(0, -7 - podium);
                    float sway = stage >= 2 ? 0.04f * MathF.Sin(_time * 1.7f + col * 0.7f + row) : 0f;
                    sb.Draw(tex, new Vector2(MathF.Round(w.X), MathF.Round(w.Y)), null, Color.White, sway, origin, k * grow, (col + row) % 2 == 0 ? SpriteEffects.None : SpriteEffects.FlipHorizontally, 0f);
                }
        }

        /// <summary>Shikastlangan saroydan tutun ko'tariladi (shikast qancha ko'p — shuncha quyuq).</summary>
        private void DrawSmoke(SpriteBatch sb, Vector2 top, float dmg, int width)
        {
            int n = 10 + (int)(dmg * 22);
            for (int i = 0; i < n; i++)
            {
                float u = (_time * 0.22f + i / (float)n) % 1f;
                float x = top.X + ((i * 37) % width - width / 2f) * 0.6f + MathF.Sin(_time * 0.8f + i) * 10f * u;
                float y = top.Y - u * 110f;
                int s = (int)(8 + u * 26 * (0.6f + dmg));
                float a = (1f - u) * (0.55f + dmg * 0.45f);
                sb.Draw(_assets.Pixel, new Rectangle((int)x - s / 2, (int)y - s / 2, s, s), new Color(52, 48, 46) * (a * 0.85f));
            }
            // Kuchli shikast — alanga tillari
            if (dmg > 0.66f)
                for (int i = 0; i < 8; i++)
                {
                    float u = (_time * 1.4f + i / 8f) % 1f;
                    float x = top.X + ((i * 53) % width - width / 2f) * 0.5f;
                    float y = top.Y + 20f - u * 26f;
                    int s = (int)(6 * (1f - u) + 2);
                    sb.Draw(_assets.Pixel, new Rectangle((int)x - s / 2, (int)y - s, s, s * 2), Color.Lerp(new Color(255, 210, 90), new Color(230, 70, 30), u) * (1f - u));
                }
        }

        private void DrawRing(SpriteBatch sb, Vector2 c, float r, Color col)
        {
            for (int i = 0; i < 20; i++)
            {
                float a = i / 20f * MathF.PI * 2f;
                var p = c + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r * 0.45f);
                sb.Draw(_assets.Pixel, new Rectangle((int)p.X, (int)p.Y, 2, 1), col);
            }
        }

        private float _time;
        /// <summary>Shamolda chayqalish uchun vaqt (Game1 har kadr beradi).</summary>
        public void Tick(float dt) => _time += dt;

        private void DrawFarmStatus(SpriteBatch sb, IWorldObject building, Farm farm)
        {
            string text = farm.CropStage == FarmCropStage.Empty
                ? (farm.StoredProduce > 0 ? "HOSIL O'RILDI" : "DALA: BO'SH")
                : farm.CropStage == FarmCropStage.Mature ? "HOSIL TAYYOR" : "EKIN O'SMOQDA " + (int)(farm.Growth * 100) + "%";
            Vector2 size = _font.Measure(text, 1, 0);
            int width = (int)size.X + 8;
            int x = (int)MathF.Round(building.DrawPosition.X + building.Texture.Width / 2f - width / 2f);
            int y = (int)MathF.Round(building.DrawPosition.Y - size.Y - 6);
            var box = new Rectangle(x, y, width, (int)size.Y + 4);
            sb.Draw(_assets.Pixel, box, new Color(18, 30, 21, 220));
            _font.DrawCentered(sb, text, new Vector2(box.Center.X, box.Center.Y), new Color(165, 238, 139), 1, 0);
        }

        /// <summary>World nuqtasi ostidagi eng oldingi obyekt (Unit, Building yoki Decoration).</summary>
        public object PickAt(Vector2 world, MapManager map, BuildingManager buildings, UnitManager units)
        {
            Unit unit = units.GetAt(world);
            if (unit != null) return unit;

            IWorldObject best = null;
            void Test(IWorldObject o)
            {
                Vector2 p = o.DrawPosition;
                int lx = (int)MathF.Floor(world.X - p.X), ly = (int)MathF.Floor(world.Y - p.Y);
                if (lx < 0 || ly < 0 || lx >= o.Texture.Width || ly >= o.Texture.Height) return;
                if (o.Flipped) lx = o.Texture.Width - 1 - lx;
                if (!_assets.IsOpaque(o.Texture, lx, ly)) return;
                if (best == null || Compare(o, best) > 0) best = o;
            }
            foreach (var b in buildings.Buildings) Test(b);
            foreach (var d in map.Decorations) Test(d);
            return best;
        }
    }
}
