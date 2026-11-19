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

namespace AndijonSaltanatlar.UI
{
    public enum BattleSceneAction { None, Close, Decide, OpenRegion, ManageRegion }

    /// <summary>
    /// PROMPT 25 — 2.5D izometrik jang sahnasi. Barcha raqamlar va natija — serverdan (BattleDto); sahna faqat ko'rsatadi.
    ///
    /// Animatsiyalar (grafik zo'ravonliksiz, stilizatsiyalangan):
    ///  - Birliklar: maydonga kirib kelish (March), Idle (har birining fazasi va tezligi har xil — bir xil takrorlanmaydi),
    ///    Ready, mavhum harakat (markazga qisqa intilish), Reaction (silkinish), Victory (sakrash), Withdraw (orqaga, xiralashib).
    ///  - Sarkardalar: Idle, CommandGesture (raund hal bo'lganda), Victory, Return.
    ///  - Muhit: hilpiragan bayroqlar, yengil chang, sahna kirish/chiqish o'tishlari.
    /// Sprite anchor (oyoq nuqtasi) o'zgarmaydi — faqat siljish beriladi; kadrlar almashinuvi sekin va silliq.
    /// Katta qo'shin minglab NPC emas: guruh ikonkalari (bitta figura ≈ bir necha birlik) bilan ko'rsatiladi.
    /// </summary>
    public sealed class BattleScene
    {
        private readonly AssetManager _assets;
        private int _battleId = -1;
        private int _seenRounds;
        private float _enterT;          // 0..1 kirish animatsiyasi
        private float _actionT = 1f;    // 0..1 — oxirgi raund harakati
        private float _sceneTime;
        private int _lastAttackerLoss, _lastDefenderLoss;
        private BattleDecision? _selected;
        private bool _showReport;
        private readonly List<Vector3> _dust = new List<Vector3>(); // x, y, yosh

        public BattleDecision SelectedDecision => _selected ?? BattleDecision.Muvozanatli;

        /// <summary>TABS uslubi: strategiya tugmalari ko'rsatilmaydi, jang o'zi kechadi; pastda qo'shin qo'shish tray'i (ArenaController chizadi).</summary>
        public bool AutoPlay;
        /// <summary>AutoPlay'da pastki tray uchun ajratilgan balandlik (ArenaController o'qiydi).</summary>
        public int TrayHeight { get; private set; }

        public BattleScene(AssetManager assets) { _assets = assets; }

        private struct Figure
        {
            public Vector2 Home;
            public string Sprite;
            public Color Tint;
            public float Phase, Speed, Scale;
            public bool Support;
            /// <summary>Masofaviy jangchi: yugurmaydi, orqadan o'q otadi.</summary>
            public bool Ranged;
            public int Col, Index;
        }

        // --- Raund jangi xoreografiyasi: uchqunlar, o'qlar, yiqilganlar, silkinish ---
        private struct Spark { public Vector2 Pos, Vel; public float Life, Max; public Color Color; }
        private struct Arrow { public Vector2 From, To; public float Start, Dur, Arc; }
        private struct Corpse { public Texture2D Tex; public Vector2 Pos; public float Scale, T; public bool Left; public Color Tint; }
        private readonly List<Spark> _sparks = new List<Spark>();
        private readonly List<Arrow> _arrows = new List<Arrow>();
        private readonly List<Corpse> _corpses = new List<Corpse>();
        private readonly HashSet<long> _strikes = new HashSet<long>();
        private readonly HashSet<long> _fallen = new HashSet<long>();
        private Vector2 _shake;
        private float _actionClock;
        private readonly Random _fx = new Random(7);
        /// <summary>Raund harakati davomiyligi (soniya): yugurish, to'qnashuv va qaytish.</summary>
        private const float ActionTime = 1.8f;

        public void Reset() { _battleId = -1; }

        /// <summary>Sinov (--replay-round): jang ochilganda oxirgi raund harakati qayta o'ynaladi.</summary>
        public static bool ReplayLastRound;

        public BattleSceneAction Draw(UiKit ui, SpriteBatch sb, BattleDto b, int vw, int vh, float dt, bool busy)
        {
            if (b == null) return BattleSceneAction.None;
            if (b.Id != _battleId)
            {
                _battleId = b.Id; _seenRounds = Math.Max(0, b.Rounds.Count - (ReplayLastRound ? 1 : 0)); _enterT = 0f; _battleEndT = null; _actionT = 1f; _selected = null; _showReport = false; _dust.Clear();
                _sparks.Clear(); _arrows.Clear(); _corpses.Clear(); _strikes.Clear(); _fallen.Clear();
            }
            _sceneTime += dt;
            _enterT = Math.Min(1f, _enterT + dt / 1.4f);
            if (b.Rounds.Count > _seenRounds)
            {
                // Yangi raund hal bo'ldi — harakat animatsiyasi va chang.
                _seenRounds = b.Rounds.Count;
                BattleRoundDto last = b.Rounds[b.Rounds.Count - 1];
                _lastAttackerLoss = last.AttackerLoss; _lastDefenderLoss = last.DefenderLoss;
                _actionT = 0f;
                _actionClock = 0f;
                _strikes.Clear(); _fallen.Clear(); _arrows.Clear();
                _selected = null;
                var rng = new Random(b.Id * 31 + _seenRounds);
                for (int i = 0; i < 26; i++) _dust.Add(new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), 0f));
            }
            _actionT = Math.Min(1f, _actionT + dt / ActionTime);
            if (_actionT < 1f) _actionClock += dt;
            // To'qnashuv lahzasida maydon silkinadi
            float shakeK = _actionT > 0.27f && _actionT < 0.45f ? 1f - (_actionT - 0.27f) / 0.18f : 0f;
            float sc0 = Math.Max(1f, vh / 720f);
            _shake = shakeK > 0f ? new Vector2(MathF.Sin(_sceneTime * 87f), MathF.Cos(_sceneTime * 63f)) * 3.2f * sc0 * shakeK : Vector2.Zero;
            UpdateSparks(dt);

            ThemeTokens T = ui.T;
            BattleSceneAction act = BattleSceneAction.None;
            float s = Math.Max(1f, vh / 720f);         // sahna masshtabi (telefon/PC)
            int topH = (int)(ui.Type.LineHeight(TextStyle.PanelTitle) * 2.6f + 24);
            int panelH = b.Status == BattleStatus.Completed ? 0
                : AutoPlay ? (int)(ui.ControlH + 18)
                : (int)(ui.ControlH * 2.7f + ui.Gap * 3 + ui.Type.LineHeight(TextStyle.Secondary) + 10);
            TrayHeight = AutoPlay ? panelH : 0;
            var field = new Rectangle(0, topH, vw, vh - topH - panelH);

            // ---- Fon: osmon, uzoq tog'lar, shahar siluetlari ----
            DrawBackdrop(ui, field, vw);

            // ---- Izometrik jang maydoni va qo'shinlar (piksel-art: PointClamp) ----
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            DrawField(sb, field, s);
            DrawArmies(sb, b, field, s);
            DrawFlags(ui, sb, b, field, s);
            DrawDust(ui, field, s, dt);
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

            // ---- Yuqori panel: saltanatlar, kuch va ruh, raund ----
            DrawHeader(ui, b, new Rectangle(0, 0, vw, topH), b.Status != BattleStatus.Completed ? ui.ControlH + 16 : 0);

            // ---- Pastki qaror paneli yoki natija oynasi ----
            if (b.Status != BattleStatus.Completed)
            {
                // AutoPlay (TABS): strategiya tugmalari yo'q — jang o'zi kechadi, pastda ArenaController tray chizadi.
                if (!AutoPlay) act = DrawDecisionPanel(ui, b, new Rectangle(0, vh - panelH, vw, panelH), busy);
            }
            else
                act = DrawResult(ui, b, vw, vh);

            // Yopish (sahnadan chiqish — jang serverda davom etadi, qayta ochish mumkin)
            int h = ui.ControlH;
            if (b.Status != BattleStatus.Completed && ui.IconButton(new Rectangle(vw - h - 8, 8, h, h), "×", "Sahnani yopish (jang serverda davom etadi)"))
                act = BattleSceneAction.Close;
            return act;
        }

        // ------------------------------------------------------------------ Fon
        private static void DrawBackdrop(UiKit ui, Rectangle f, int vw)
        {
            // Osmon gradienti (oqshom ranglari)
            int bands = 12;
            for (int i = 0; i < bands; i++)
            {
                Color c = Color.Lerp(new Color(44, 58, 92), new Color(214, 150, 98), i / (float)(bands - 1));
                int y0 = f.Y + f.Height * i / (bands * 2), y1 = f.Y + f.Height * (i + 1) / (bands * 2);
                ui.Fill(new Rectangle(0, y0, vw, y1 - y0 + 1), c);
            }
            // Uzoq tog'lar (Farg'ona vodiysi tizmalari)
            int horizon = f.Y + f.Height / 2;
            for (int x = 0; x < vw; x += 6)
            {
                float m = 0.5f + 0.5f * MathF.Sin(x * 0.008f) * MathF.Cos(x * 0.0031f + 1.3f);
                int top = horizon - (int)(f.Height * (0.08f + 0.14f * m));
                ui.Fill(new Rectangle(x, top, 6, horizon - top), new Color(70, 78, 104));
            }
            // Shahar siluetlari: gumbaz va minoralar (ikki tomonda)
            Color sil = new Color(52, 56, 78);
            for (int side = 0; side < 2; side++)
            {
                int cx = side == 0 ? vw / 8 : vw * 7 / 8;
                int baseY = horizon;
                ui.Fill(new Rectangle(cx - 70, baseY - 26, 140, 26), sil);
                ui.Rounded(new Rectangle(cx - 22, baseY - 58, 44, 44), sil, 22);
                ui.Fill(new Rectangle(cx - 52, baseY - 70, 9, 70), sil);
                ui.Fill(new Rectangle(cx + 44, baseY - 64, 9, 64), sil);
            }
            ui.Fill(new Rectangle(0, horizon, vw, f.Bottom - horizon), new Color(86, 104, 66));
        }

        private void DrawField(SpriteBatch sb, Rectangle f, float s)
        {
            Texture2D atlas = _assets.Get(SpriteKeys.Tiles);
            float tile = 1.15f * s;
            int n = 9;
            Vector2 c = new Vector2(f.Center.X, f.Y + f.Height * 0.52f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float wx = (x - y) * 32 * tile, wy = (x + y - n + 1) * 16 * tile;
                    int row = ((x * 7 + y * 13) % 5 == 0) ? 0 : 3; // asosan o't, orasida tuproq
                    var pos = c + new Vector2(wx + TileArt.DrawOffsetX * tile, wy + TileArt.DrawOffsetY * tile);
                    sb.Draw(atlas, pos, TileArt.Cell(row, (x + y) % TileArt.Columns), Color.White, 0f, Vector2.Zero, tile, SpriteEffects.None, 0f);
                }
        }

        // ------------------------------------------------------------------ Qo'shinlar
        private static string SpriteFor(UnitLine line, int level)
        {
            switch (line)
            {
                case UnitLine.Otliq: return SpriteKeys.UnitCavalry;
                case UnitLine.Masofaviy: return SpriteKeys.UnitPatrol;   // kamonchi
                case UnitLine.Aravasi:
                case UnitLine.Lager: return SpriteKeys.UnitCitizen + "2";
                default: return level >= 3 ? SpriteKeys.UnitShield : level == 2 ? SpriteKeys.UnitSwordsman : SpriteKeys.UnitSpearman;
            }
        }

        private static Color TintFor(UnitLine line, int level, Color kingdom)
        {
            Color baseC = line == UnitLine.Otliq ? new Color(255, 236, 200) : line == UnitLine.Masofaviy ? new Color(210, 240, 210) : Color.White;
            if (level >= 3) baseC = Color.Lerp(baseC, new Color(255, 220, 140), 0.35f);
            return Color.Lerp(baseC, kingdom, 0.18f);
        }

        private static List<Figure> Formation(List<UnitGroupDto> groups, bool left, Vector2 anchor, float s, int seed, Color kingdom)
        {
            var figs = new List<Figure>();
            int total = Math.Max(1, groups.Sum(g => g.Count));
            int maxFigs = 22;
            float per = Math.Max(1f, total / (float)maxFigs);
            var rng = new Random(seed);
            int i = 0;
            foreach (UnitGroupDto g in groups.OrderBy(g => g.Line == UnitLine.Aravasi || g.Line == UnitLine.Lager ? 1 : 0).ThenByDescending(g => g.Line == UnitLine.Otliq ? 0 : 1))
            {
                int n = Math.Max(g.Count > 0 ? 1 : 0, (int)Math.Round(g.Count / per));
                for (int k = 0; k < n && figs.Count < maxFigs + 4; k++, i++)
                {
                    int col = i / 5, row = i % 5;
                    float dx = (left ? -1 : 1) * col * 30 * s + (row % 2) * 8 * s * (left ? -1 : 1);
                    float dy = (row - 2) * 15 * s + col * 7.5f * s;
                    figs.Add(new Figure
                    {
                        Home = anchor + new Vector2(dx + (left ? -1 : 1) * col * 0f, dy),
                        Sprite = SpriteFor(g.Line, g.Level),
                        Tint = TintFor(g.Line, g.Level, kingdom),
                        Phase = (float)rng.NextDouble() * MathHelper.TwoPi,
                        Speed = 1.6f + (float)rng.NextDouble() * 1.4f,
                        Scale = (g.Line == UnitLine.Otliq ? 0.95f : 1.1f) * s,
                        Support = g.Line == UnitLine.Aravasi || g.Line == UnitLine.Lager,
                        Ranged = g.Line == UnitLine.Masofaviy,
                        Col = col, Index = i
                    });
                }
            }
            figs.Sort((a, b) => a.Home.Y.CompareTo(b.Home.Y)); // chuqurlik bo'yicha chizish
            return figs;
        }

        private void DrawArmies(SpriteBatch sb, BattleDto b, Rectangle f, float s)
        {
            Color ac = ToColor(b.AttackerColor), dc = ToColor(b.DefenderColor);
            float midY = f.Y + f.Height * 0.56f;
            var aAnchor = new Vector2(f.Center.X - f.Width * 0.09f, midY);
            var dAnchor = new Vector2(f.Center.X + f.Width * 0.09f, midY);
            bool done = b.Status == BattleStatus.Completed;
            int aWin = done && b.WinnerKingdomId == b.AttackerKingdomId ? 1 : done && b.WinnerKingdomId != 0 ? -1 : 0;
            DrawCorpses(sb);
            var aFigs = Formation(b.AttackerGroups, true, aAnchor, s, b.Id * 7 + 1, ac);
            var dFigs = Formation(b.DefenderGroups, false, dAnchor, s, b.Id * 7 + 2, dc);
            float aPer = Math.Max(1f, Math.Max(1, b.AttackerGroups.Sum(g => g.Count)) / 22f), dPer = Math.Max(1f, Math.Max(1, b.DefenderGroups.Sum(g => g.Count)) / 22f);
            DrawSide(sb, aFigs, true, s, f, aWin, _lastAttackerLoss, aPer, done && b.Peace);
            DrawSide(sb, dFigs, false, s, f, -aWin, _lastDefenderLoss, dPer, done && b.Peace);
            DrawArrows(sb, s);
            DrawSparks(sb, s);
            // Sarkardalar (qo'shin orqasida, kattaroq) — CommandGesture raund hal bo'lganda.
            DrawCommander(sb, aAnchor + new Vector2(-150 * s, -10 * s), true, s, ac, aWin);
            DrawCommander(sb, dAnchor + new Vector2(150 * s, -10 * s), false, s, dc, -aWin);
        }

        /// <summary>
        /// Bir tomon qo'shini. Raund hal bo'lganda xoreografiya (ActionTime): 0..28% — old qatorlar raqib tomon yuguradi,
        /// 28..78% — to'qnashuv: haqiqiy zarba kadrlari (ko'tarish → urish → davomi), tegishda uchqun, yo'qotgan tomon
        /// orqaga chayqaladi va old qatordan askarlar yiqiladi; 78..100% — o'z joyiga qaytish. Orqa qator (aravalar) o'q otadi.
        /// </summary>
        private void DrawSide(SpriteBatch sb, List<Figure> figs, bool left, float s, Rectangle f, int outcome, int loss, float perFigure, bool peace)
        {
            float dir = left ? 1f : -1f;
            float t = _sceneTime;
            float u = _actionT;
            bool acting = u < 1f && !peace;
            bool hit = loss > 0;
            float centerX = f.Center.X;
            int fallCount = hit ? Math.Clamp((int)MathF.Ceiling(loss / perFigure), 1, 4) : 0;
            var frontIdx = figs.Where(x => x.Col == 0 && !x.Support && !x.Ranged).Select(x => x.Index).OrderBy(x => x).Take(fallCount).ToList();

            for (int i = 0; i < figs.Count; i++)
            {
                Figure g = figs[i];
                float delay = (i % 7) * 0.05f;
                float e = MathHelper.Clamp((_enterT - delay) / (1f - 0.35f), 0f, 1f);
                e = 1f - (1f - e) * (1f - e) * (1f - e);
                Vector2 pos = Vector2.Lerp(g.Home - new Vector2(dir * f.Width * 0.45f, 0), g.Home, e);
                float bob = MathF.Sin(t * g.Speed + g.Phase) * 1.4f * s;
                float alpha = 1f;
                float rot = 0f;
                string frameKey = null;
                long key = ((long)(left ? 1 : 2) << 40) | ((long)g.Index << 8);

                if (acting && !g.Support && !g.Ranged)
                {
                    // Yugurish manzili: old qator raqib bilan to'qnashadi, orqadagilar zichlashib kelib turadi
                    float targetX = centerX - dir * (13f * s + g.Col * 19f * s);
                    float adv = targetX - g.Home.X;
                    float k;
                    if (u < 0.28f) { float c = u / 0.28f; k = c * c * (3f - 2f * c); }
                    else if (u < 0.78f) k = 1f;
                    else { float c = (u - 0.78f) / 0.22f; k = 1f - c * c * (3f - 2f * c); }
                    pos.X += adv * k;
                    if (u < 0.28f) bob -= MathF.Abs(MathF.Sin(u / 0.28f * MathF.PI * 3f)) * 3f * s; // yugurish sakrashi

                    if (u >= 0.28f && u < 0.78f && g.Col <= 1)
                    {
                        // To'qnashuv: ikki marta zarba (har askar o'z fazasida)
                        float lt = (u - 0.28f) / 0.5f + (g.Phase / MathHelper.TwoPi) * 0.22f;
                        float cyc = lt * 2f;
                        int ci = (int)cyc;
                        float fr = cyc - ci;
                        if (ci < 2)
                        {
                            int atk = (g.Index + ci) % 3;
                            int ph = fr < 0.33f ? 0 : fr < 0.6f ? 1 : 2;
                            frameKey = AssetManager.AttackKey(g.Sprite, atk, ph);
                            if (ph == 1) pos.X += dir * 4f * s; // urishda oldinga intiladi
                            // Tegish lahzasi: uchqun (bir marta)
                            long sk = key | (uint)ci;
                            if (g.Col == 0 && fr >= 0.42f && _strikes.Add(sk))
                                SpawnSparks(new Vector2(pos.X + dir * 15f * s, pos.Y - 22f * s), dir, s, atk == 1 ? 12 : 8);
                        }
                        // Yo'qotgan tomon: zarbalardan orqaga chayqaladi
                        if (hit) pos.X -= dir * MathF.Abs(MathF.Sin(cyc * MathF.PI)) * 4f * s;
                    }

                    // Old qatordan yiqilganlar (yo'qotishga mutanosib): yiqilish o'rni maydonda qoladi
                    if (hit && g.Col == 0 && frontIdx.Contains(g.Index) && u >= 0.56f)
                    {
                        if (_fallen.Add(key))
                        {
                            Texture2D ct = _assets.TryGet(g.Sprite + "_0");
                            if (ct != null)
                                _corpses.Add(new Corpse { Tex = ct, Pos = pos + new Vector2(0, bob), Scale = g.Scale, Left = left, Tint = g.Tint, T = 0f });
                            if (_corpses.Count > 40) _corpses.RemoveAt(0);
                            for (int d = 0; d < 6; d++)
                                _dust.Add(new Vector3((pos.X - f.Center.X) / (f.Width * 0.3f) + 0.5f, (float)_fx.NextDouble() * 0.4f + 0.3f, 0.6f));
                        }
                        continue; // yiqilgan askar safdan chiqadi (o'rnini orqadagisi egallaydi)
                    }
                }
                else if (acting && g.Ranged && u > 0.08f && u < 0.45f)
                {
                    // Masofaviy jangchilar orqadan o'q otadi (har raundda bir martadan)
                    long ak = key | 0xAAL;
                    float when = 0.1f + (g.Phase / MathHelper.TwoPi) * 0.3f;
                    if (u >= when && _strikes.Add(ak))
                    {
                        Vector2 from = pos + new Vector2(dir * 6f * s, -26f * s);
                        Vector2 to = new Vector2(centerX + dir * (14f + (float)_fx.NextDouble() * 50f) * s, pos.Y + ((float)_fx.NextDouble() - 0.5f) * 40f * s - 10f * s);
                        _arrows.Add(new Arrow { From = from, To = to, Start = _actionClock, Dur = 0.55f + (float)_fx.NextDouble() * 0.2f, Arc = (40f + (float)_fx.NextDouble() * 30f) * s });
                    }
                }

                // Yakun: g'olib sakraydi, mag'lub orqaga chekinadi; tinchlikda — joyida turadi.
                if (outcome > 0 && !peace) bob -= MathF.Max(0f, MathF.Sin(t * 5f + g.Phase)) * 6f * s;
                if (outcome < 0 && !peace)
                {
                    float w = MathHelper.Clamp((t - (_battleEndT ?? t)) / 2.5f, 0f, 1f);
                    pos.X -= dir * w * 120 * s;
                    alpha = 1f - 0.6f * w;
                }
                pos.Y += bob;
                pos += _shake;

                Texture2D tex = frameKey != null ? _assets.TryGet(frameKey) : null;
                if (tex == null)
                {
                    int frame = acting && !g.Support && !g.Ranged && u < 0.28f ? ((int)(u * 30f) + g.Index) % 2 + 1 // yugurish kadrlari
                        : ((int)(t * 2.2f + g.Phase * 3) % 3 + 3) % 3;
                    tex = _assets.TryGet(g.Sprite + "_" + frame) ?? _assets.TryGet(g.Sprite + "_0");
                }
                if (tex == null) continue;
                var origin = new Vector2(tex.Width / 2f, tex.Height - 2 / AssetManager.ScaleOf(tex));
                Color col = g.Tint * alpha;
                if (hit && acting && u > 0.3f && u < 0.75f)
                {
                    float flash = MathF.Max(0f, MathF.Sin((u - 0.3f) / 0.45f * MathF.PI * 4f));
                    col = Color.Lerp(col, new Color(255, 196, 186) * alpha, 0.55f * flash);
                }
                sb.Draw(_assets.Pixel, new Rectangle((int)(pos.X - 9 * s), (int)(pos.Y - 2 * s), (int)(18 * s), (int)(5 * s)), new Color(0, 0, 0, 60) * alpha); // soya
                sb.Draw(tex, pos, null, col, rot, origin, g.Scale * AssetManager.ScaleOf(tex), left ? SpriteEffects.None : SpriteEffects.FlipHorizontally, 0f);
            }
        }

        // ------------------------------------------------------------------ Effektlar
        private void SpawnSparks(Vector2 at, float dir, float s, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float ang = (dir > 0 ? 0f : MathF.PI) + ((float)_fx.NextDouble() - 0.5f) * 2.2f;
                float sp = (90f + (float)_fx.NextDouble() * 140f) * s;
                float life = 0.18f + (float)_fx.NextDouble() * 0.18f;
                _sparks.Add(new Spark
                {
                    Pos = at, Vel = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * sp - new Vector2(0, 50f * s),
                    Life = life, Max = life, Color = Color.Lerp(new Color(255, 250, 220), new Color(255, 170, 60), (float)_fx.NextDouble())
                });
            }
            if (_sparks.Count > 400) _sparks.RemoveRange(0, _sparks.Count - 400);
        }

        private void UpdateSparks(float dt)
        {
            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                Spark p = _sparks[i];
                p.Life -= dt;
                if (p.Life <= 0f) { _sparks.RemoveAt(i); continue; }
                p.Pos += p.Vel * dt;
                p.Vel.Y += 520f * dt;
                _sparks[i] = p;
            }
            for (int i = _corpses.Count - 1; i >= 0; i--) { Corpse c = _corpses[i]; c.T += dt; _corpses[i] = c; }
        }

        private void DrawSparks(SpriteBatch sb, float s)
        {
            foreach (Spark p in _sparks)
            {
                float k = p.Life / p.Max;
                float len = Math.Max(2f, p.Vel.Length() * 0.025f);
                sb.Draw(_assets.Pixel, p.Pos + _shake, null, p.Color * Math.Min(1f, k * 1.6f), MathF.Atan2(p.Vel.Y, p.Vel.X) + MathF.PI, new Vector2(0f, 0.5f), new Vector2(len, Math.Max(1f, 2f * s)), SpriteEffects.None, 0f);
            }
        }

        private void DrawArrows(SpriteBatch sb, float s)
        {
            for (int i = _arrows.Count - 1; i >= 0; i--)
            {
                Arrow a = _arrows[i];
                float k = (_actionClock - a.Start) / a.Dur;
                if (k < 0f) continue;
                if (k > 1f)
                {
                    if (k > 1.15f) { _arrows.RemoveAt(i); continue; }
                    // yerga sanchilgan o'q
                    sb.Draw(_assets.Pixel, a.To + _shake, null, new Color(90, 66, 40), -0.9f, new Vector2(0f, 0.5f), new Vector2(9f * s, Math.Max(1f, 1.5f * s)), SpriteEffects.None, 0f);
                    continue;
                }
                Vector2 p = Vector2.Lerp(a.From, a.To, k) + new Vector2(0, -a.Arc * 4f * k * (1f - k));
                Vector2 d = (a.To - a.From) + new Vector2(0, -a.Arc * 4f * (1f - 2f * k));
                float ang = MathF.Atan2(d.Y, d.X);
                sb.Draw(_assets.Pixel, p + _shake, null, new Color(70, 52, 34), ang + MathF.PI, new Vector2(0f, 0.5f), new Vector2(12f * s, Math.Max(1f, 1.5f * s)), SpriteEffects.None, 0f);
                sb.Draw(_assets.Pixel, p + _shake, null, new Color(210, 214, 222), ang, new Vector2(0f, 0.5f), new Vector2(3f * s, Math.Max(1f, 2f * s)), SpriteEffects.None, 0f);
            }
        }

        /// <summary>Yiqilganlar: qulab tushadi (sakrab), keyin maydonda xira yotib qoladi.</summary>
        private void DrawCorpses(SpriteBatch sb)
        {
            foreach (Corpse c in _corpses)
            {
                float k = Math.Clamp(c.T / 0.42f, 0f, 1f);
                float e = k * k * (2.2f - 1.2f * k);
                float bounce = k > 0.75f ? MathF.Sin((k - 0.75f) / 0.25f * MathF.PI) * 0.12f : 0f;
                float ang = MathHelper.PiOver2 * Math.Clamp(e - bounce, 0f, 1f) * (c.Left ? -1f : 1f);
                float fade = c.T < 3f ? 1f : Math.Max(0.45f, 1f - (c.T - 3f) / 4f);
                Color col = Color.Lerp(c.Tint, new Color(120, 112, 104), 0.5f * k) * fade;
                var origin = new Vector2(c.Tex.Width / 2f, c.Tex.Height - 2);
                sb.Draw(c.Tex, c.Pos + _shake, null, col, ang, origin, c.Scale, c.Left ? SpriteEffects.None : SpriteEffects.FlipHorizontally, 0f);
            }
        }

        private float? _battleEndT;

        private void DrawCommander(SpriteBatch sb, Vector2 pos, bool left, float s, Color kingdom, int outcome)
        {
            float t = _sceneTime;
            float gesture = _actionT < 1f ? MathF.Sin(_actionT * MathF.PI) : 0f;
            pos.Y += MathF.Sin(t * 1.3f + (left ? 0 : 1.7f)) * 1.2f * s - gesture * 5 * s;
            if (outcome > 0) pos.Y -= MathF.Max(0f, MathF.Sin(t * 4f)) * 5 * s;
            Texture2D tex = _assets.TryGet(SpriteKeys.UnitShield + "_" + ((int)(t * 1.5f) % 3)) ?? _assets.TryGet(SpriteKeys.UnitShield + "_0");
            if (tex == null) return;
            var origin = new Vector2(tex.Width / 2f, tex.Height - 2 / AssetManager.ScaleOf(tex));
            sb.Draw(_assets.Pixel, new Rectangle((int)(pos.X - 12 * s), (int)(pos.Y - 2 * s), (int)(24 * s), (int)(6 * s)), new Color(0, 0, 0, 70));
            sb.Draw(tex, pos, null, Color.Lerp(Color.White, kingdom, 0.25f), 0f, origin, 1.6f * s * AssetManager.ScaleOf(tex), left ? SpriteEffects.None : SpriteEffects.FlipHorizontally, 0f);
            // Sarkarda tug'i (CommandGesture paytida ko'tariladi)
            float poleX = pos.X + (left ? -14 : 14) * s, top = pos.Y - (58 + gesture * 8) * s;
            sb.Draw(_assets.Pixel, new Rectangle((int)poleX, (int)top, Math.Max(1, (int)(2 * s)), (int)(44 * s)), new Color(70, 52, 32));
            for (int i = 0; i < 6; i++)
            {
                float wave = MathF.Sin(t * 5f - i * 0.8f) * 2f * s;
                sb.Draw(_assets.Pixel, new Rectangle((int)(poleX + (left ? 2 : -2 - 16 * s) + (left ? i : -i) * 0f), (int)(top + i * 3 * s + wave), (int)(16 * s), (int)(3 * s) + 1), kingdom);
            }
        }

        private void DrawFlags(UiKit ui, SpriteBatch sb, BattleDto b, Rectangle f, float s)
        {
            float t = _sceneTime;
            for (int side = 0; side < 2; side++)
            {
                Color c = ToColor(side == 0 ? b.AttackerColor : b.DefenderColor);
                float x = side == 0 ? f.X + f.Width * 0.12f : f.Right - f.Width * 0.12f;
                float baseY = f.Y + f.Height * 0.62f;
                int poleH = (int)(f.Height * 0.42f);
                sb.Draw(_assets.Pixel, new Rectangle((int)x, (int)(baseY - poleH), Math.Max(2, (int)(3 * s)), poleH), new Color(92, 70, 44));
                sb.Draw(_assets.Pixel, new Rectangle((int)x - 3, (int)(baseY - poleH) - 4, 9, 6), new Color(230, 190, 90));
                // Saltanat bayrog'i (o'yinchi o'zi yaratgan): hilpiraydi, o'ng tomondagisi dastaga ko'zgu tarzida bog'langan
                int clothW = (int)(72 * s), clothH = (int)(clothW / Shared.Heraldry.FlagSpec.Aspect);
                var spec = FlagRenderer.Spec(side == 0 ? b.AttackerFlag : b.DefenderFlag, c);
                var cloth = side == 0 ? new Rectangle((int)x + 3, (int)(baseY - poleH) + 2, clothW, clothH) : new Rectangle((int)x - clothW, (int)(baseY - poleH) + 2, clothW, clothH);
                FlagRenderer.Draw(ui, cloth, spec, 18, 4f * s, t * 4.2f + side, 1f, side == 1);
            }
        }

        private void DrawDust(UiKit ui, Rectangle f, float s, float dt)
        {
            if (_dust.Count == 0) return;
            for (int i = _dust.Count - 1; i >= 0; i--)
            {
                Vector3 d = _dust[i];
                d.Z += dt;
                if (d.Z > 1.6f) { _dust.RemoveAt(i); continue; }
                _dust[i] = d;
                float x = f.Center.X + (d.X - 0.5f) * f.Width * 0.3f + d.Z * 18 * (d.X - 0.5f) * s;
                float y = f.Y + f.Height * (0.5f + d.Y * 0.14f) - d.Z * 22 * s;
                float r = (6 + 10 * d.Z) * s;
                float alpha = 0.35f * (1f - d.Z / 1.6f);
                ui.Rounded(new Rectangle((int)(x - r), (int)(y - r / 2), (int)(r * 2), (int)r), new Color(214, 196, 160) * alpha, (int)(r / 2));
            }
        }

        // ------------------------------------------------------------------ Yuqori panel
        private static void DrawHeader(UiKit ui, BattleDto b, Rectangle r, int rightInset)
        {
            ThemeTokens T = ui.T;
            ui.Fill(r, T.Surface * 0.94f);
            ui.Fill(new Rectangle(r.X, r.Bottom - 1, r.Width, 1), T.Border);
            int pad = 16, mid = r.Center.X;
            float th = ui.Type.LineHeight(TextStyle.PanelTitle);
            string round = b.Status == BattleStatus.Completed ? "JANG YAKUNLANDI" : b.Round + "-RAUND / " + b.MaxRounds;
            ui.Type.DrawCentered(ui.Batch, round, new Rectangle(mid - 160, r.Y + 6, 320, (int)th), TextStyle.PanelTitle, T.Accent);
            ui.Type.DrawCentered(ui.Batch, ui.Type.Fit(b.RegionName + " uchun" + (b.IsPvp ? " · PvP" : ""), 300, TextStyle.Secondary), new Rectangle(mid - 160, r.Y + 8 + (int)th, 320, 22), TextStyle.Secondary, T.TextSecondary);

            int sideW = Math.Max(160, mid - 180 - pad - rightInset);
            Side(ui, new Rectangle(r.X + pad, r.Y + 8, sideW, r.Height - 16), b.AttackerName, b.AttackerCommander, b.AttackerPower, b.AttackerStartPower, b.AttackerMorale, ToColor(b.AttackerColor), true, b.IAmAttacker && b.IAmParticipant);
            Side(ui, new Rectangle(r.Right - pad - rightInset - sideW, r.Y + 8, sideW, r.Height - 16), b.DefenderName, b.DefenderCommander, b.DefenderPower, b.DefenderStartPower, b.DefenderMorale, ToColor(b.DefenderColor), false, !b.IAmAttacker && b.IAmParticipant);
        }

        private static void Side(UiKit ui, Rectangle r, string name, string commander, int power, int start, int morale, Color c, bool left, bool mine)
        {
            ThemeTokens T = ui.T;
            float lh = ui.Type.LineHeight(TextStyle.Button), sh = ui.Type.LineHeight(TextStyle.Caption);
            ui.Rounded(new Rectangle(left ? r.X : r.Right - 6, r.Y + 2, 6, r.Height - 4), c, 3);
            int x = left ? r.X + 14 : r.X, w = r.Width - 14;
            string title = name + (mine ? " (siz)" : "");
            var tr = new Rectangle(x, r.Y, w, (int)lh);
            if (left) ui.TextFit(title, tr, TextStyle.Button); else ui.Type.DrawRight(ui.Batch, ui.Type.Fit(title, w, TextStyle.Button), new Vector2(r.Right - 14, r.Y), TextStyle.Button, T.TextPrimary);
            int y = r.Y + (int)lh + 2;
            string cmd = string.IsNullOrEmpty(commander) ? "" : commander;
            if (cmd.Length > 0)
            {
                if (left) ui.TextFit(cmd, new Rectangle(x, y, w, (int)sh), TextStyle.Caption, T.TextSecondary);
                else ui.Type.DrawRight(ui.Batch, ui.Type.Fit(cmd, w, TextStyle.Caption), new Vector2(r.Right - 14, y), TextStyle.Caption, T.TextSecondary);
                y += (int)sh + 3;
            }
            int barH = Math.Max(8, (int)(sh * 0.7f));
            Bar(ui, new Rectangle(x, y, w, barH), power / (float)Math.Max(1, start), c, "Kuch " + power, left);
            y += barH + (int)sh + 4;
            Bar(ui, new Rectangle(x, y, w, barH), morale / 100f, new Color(240, 200, 110), "Ruh " + morale, left);
        }

        private static void Bar(UiKit ui, Rectangle r, float v, Color c, string label, bool left)
        {
            ui.Rounded(r, ui.T.Disabled, r.Height / 2);
            int w = (int)(r.Width * MathHelper.Clamp(v, 0f, 1f));
            if (w > 0) ui.Rounded(left ? new Rectangle(r.X, r.Y, w, r.Height) : new Rectangle(r.Right - w, r.Y, w, r.Height), c, r.Height / 2);
            var lr = new Rectangle(r.X, r.Bottom + 1, r.Width, (int)ui.Type.LineHeight(TextStyle.Caption));
            if (left) ui.Text(label, new Vector2(lr.X, lr.Y), TextStyle.Caption, ui.T.TextSecondary);
            else ui.Type.DrawRight(ui.Batch, label, new Vector2(lr.Right, lr.Y), TextStyle.Caption, ui.T.TextSecondary);
        }

        // ------------------------------------------------------------------ Qaror paneli
        private BattleSceneAction DrawDecisionPanel(UiKit ui, BattleDto b, Rectangle r, bool busy)
        {
            ThemeTokens T = ui.T;
            ui.Fill(r, T.Surface * 0.96f);
            ui.Fill(new Rectangle(r.X, r.Y, r.Width, 1), T.Border);
            int pad = 14, gap = ui.Gap, h = ui.ControlH;
            int x = r.X + pad, w = r.Width - 2 * pad, y = r.Y + 8;
            string status;
            if (!b.IAmParticipant) status = "Siz bu jangni kuzatyapsiz.";
            else if (b.MyDecisionSubmitted) status = b.IsPvp ? "Qaroringiz yuborildi. Raqib qarori kutilmoqda" + (b.RoundSecondsLeft > 0 ? " (" + b.RoundSecondsLeft + " s)" : "") + "…" : "Server raundni hisoblamoqda…";
            else status = b.Rounds.Count > 0 ? b.Rounds[b.Rounds.Count - 1].SummaryUz + "  Keyingi qaroringiz:" : "Raund uchun strategik yondashuvni tanlang:";
            ui.TextFit(status, new Rectangle(x, y, w, (int)ui.Type.LineHeight(TextStyle.Secondary)), TextStyle.Secondary, b.MyDecisionSubmitted ? T.Accent : T.TextSecondary);
            y += (int)ui.Type.LineHeight(TextStyle.Secondary) + 6;
            if (!b.IAmParticipant) return BattleSceneAction.None;

            var decisions = (BattleDecision[])Enum.GetValues(typeof(BattleDecision));
            int confirmW = Math.Min(260, w / 4);
            int bw = (w - confirmW - gap - gap * (decisions.Length - 1)) / decisions.Length;
            bool locked = b.MyDecisionSubmitted || busy;
            for (int i = 0; i < decisions.Length; i++)
            {
                BattleDecision d = decisions[i];
                var br = new Rectangle(x + i * (bw + gap), y, bw, (int)(h * 2f));
                bool sel = _selected == d;
                if (ui.Card(br, sel, !locked) && !locked) _selected = d;
                // Nom sig'masa qisqa shakli (MUVOZANATLI, FAOL...), izoh ikki qatorga o'raladi — matn kesilmaydi
                string dn = WarRules.DecisionName(d);
                if (ui.Type.Measure(dn, TextStyle.Button).X > br.Width - 12) dn = d == BattleDecision.QaytaTashkil ? "Qayta tashkil" : dn.Split(' ')[0];
                ui.Type.DrawCentered(ui.Batch, ui.Type.Fit(dn, br.Width - 12, TextStyle.Button), new Rectangle(br.X, br.Y + 4, br.Width, (int)(br.Height * 0.4f)), TextStyle.Button, sel ? T.Primary : T.TextPrimary);
                ui.Paragraph(WarRules.DecisionHint(d), new Rectangle(br.X + 8, br.Y + (int)(br.Height * 0.44f), br.Width - 16, (int)(br.Height * 0.54f)), TextStyle.Caption, T.TextSecondary, 2);
            }
            var cr = new Rectangle(x + w - confirmW, y, confirmW, (int)(h * 2f));
            if (ui.Button(cr, locked ? "Kutilmoqda…" : "TASDIQLASH", ButtonKind.Primary, !locked && _selected != null, "Qarorni serverga yuborish"))
                return BattleSceneAction.Decide;
            return BattleSceneAction.None;
        }

        // ------------------------------------------------------------------ Natija oynasi
        private BattleSceneAction DrawResult(UiKit ui, BattleDto b, int vw, int vh)
        {
            if (_battleEndT == null) _battleEndT = _sceneTime;
            ThemeTokens T = ui.T;
            float appear = MathHelper.Clamp((_sceneTime - _battleEndT.Value - 0.9f) / 0.5f, 0f, 1f);
            if (appear <= 0f) return BattleSceneAction.None;
            ui.Fill(new Rectangle(0, 0, vw, vh), T.Scrim * (0.6f * appear));
            int h = ui.ControlH, gap = ui.Gap;
            // Telefonda shrift kattaroq (UiScale) — oyna ham mos ravishda kengayadi, tugma matnlari qisqarmaydi.
            string[] labels = { "XARITAGA QAYTISH", "HUDUDNI BOSHQARISH", _showReport ? "XULOSA" : "JANG HISOBOTI" };
            int needBtns = 0;
            foreach (string l in labels) needBtns += (int)ui.Type.Measure(l, TextStyle.Button).X + 36;
            int w = Math.Min(vw - 32, Math.Max((int)(640 * Math.Max(1f, ui.Type.UiScale)), needBtns + 2 * gap + 48));
            float lh = ui.Type.LineHeight(TextStyle.Secondary);
            int lines = _showReport ? Math.Max(5, b.Rounds.Count + 2) : 7;
            int mh = Math.Min(vh - 24, (int)(ui.Type.LineHeight(TextStyle.ScreenTitle) + ui.Type.LineHeight(TextStyle.PanelTitle) + lines * (lh + 6) + h * 2 + gap * 4 + 50));
            var m = new Rectangle((vw - w) / 2, (vh - mh) / 2 + (int)((1f - appear) * 30), w, mh);
            ui.Panel(m);
            ui.Rounded(new Rectangle(m.X, m.Y, m.Width, 6), T.Accent, 3);
            int x = m.X + 24, y = m.Y + 18, cw = m.Width - 48;
            ui.Text("JANG YAKUNLANDI", new Vector2(x, y), TextStyle.ScreenTitle, T.Accent);
            y += (int)ui.Type.LineHeight(TextStyle.ScreenTitle) + 4;
            int me = b.IAmAttacker ? b.AttackerKingdomId : b.DefenderKingdomId;
            string outcome = b.Peace ? "Tinchlik" : !b.IAmParticipant ? (b.WinnerKingdomId == b.AttackerKingdomId ? b.AttackerName : b.DefenderName) + " g'alabasi" : b.WinnerKingdomId == me ? "G'alaba" : "Mag'lubiyat";
            Color oc = b.Peace ? T.Secondary : b.WinnerKingdomId == me ? T.Success : T.Danger;
            ui.Text("Natija: " + outcome, new Vector2(x, y), TextStyle.PanelTitle, oc);
            y += (int)ui.Type.LineHeight(TextStyle.PanelTitle) + 8;

            void Line(string a) { ui.TextFit(a, new Rectangle(x, y, cw, (int)lh), TextStyle.Secondary, T.TextPrimary); y += (int)lh + 6; }
            if (!_showReport)
            {
                Line("Hudud: " + b.RegionName + (b.TerritoryTransferred ? " — saltanatga qo'shildi" : ""));
                y += (int)ui.Paragraph("Ishtirokchilar: " + b.AttackerName + " va " + b.DefenderName, new Rectangle(x, y, cw, (int)(lh * 2 + 6)), TextStyle.Secondary, T.TextPrimary, 2) + 6;
                Line("Jang raundlari: " + b.Rounds.Count);
                Line("Yakuniy kuch: " + b.AttackerPower + " / " + b.AttackerStartPower + "  —  " + b.DefenderPower + " / " + b.DefenderStartPower);
                Line("Resurs xarajati: " + b.CostCoins + " tanga");
                y += (int)ui.Paragraph("Diplomatiya: " + b.DiplomacyUz, new Rectangle(x, y, cw, (int)(lh * 2 + 6)), TextStyle.Secondary, T.TextSecondary, 2) + 4;
            }
            else
            {
                foreach (BattleRoundDto r in b.Rounds)
                    Line(r.Round + ". " + WarRules.DecisionName(r.AttackerDecision) + " / " + WarRules.DecisionName(r.DefenderDecision) + " → −" + r.AttackerLoss + " / −" + r.DefenderLoss);
                y += (int)ui.Paragraph(b.ResultUz, new Rectangle(x, y, cw, (int)(lh * 2 + 6)), TextStyle.Secondary, T.TextSecondary, 2) + 4;
            }

            int by = m.Bottom - 16 - h;
            // Tugmalar eni yorliq uzunligiga mutanosib — hech biri qisqarib ketmaydi.
            float k = Math.Min(1f, (cw - 2 * gap) / (float)Math.Max(1, needBtns));
            int extra = Math.Max(0, (cw - 2 * gap - needBtns) / 3);
            int[] bws = new int[3];
            for (int i = 0; i < 3; i++) bws[i] = (int)((ui.Type.Measure(labels[i], TextStyle.Button).X + 36) * k) + extra;
            BattleSceneAction act = BattleSceneAction.None;
            int bx0 = x;
            if (ui.Button(new Rectangle(bx0, by, bws[0], h), labels[0], ButtonKind.Primary, true, "Dunyo xaritasiga qaytish")) act = BattleSceneAction.OpenRegion;
            bx0 += bws[0] + gap;
            if (ui.Button(new Rectangle(bx0, by, bws[1], h), labels[1], ButtonKind.Secondary, b.TerritoryTransferred && b.WinnerKingdomId == me)) act = BattleSceneAction.ManageRegion;
            bx0 += bws[1] + gap;
            if (ui.Button(new Rectangle(bx0, by, bws[2], h), labels[2], ButtonKind.Ghost)) _showReport = !_showReport;
            if (act != BattleSceneAction.None) { _battleEndT = null; }
            return act;
        }

        // ------------------------------------------------------------------ yordamchilar
        private static SpriteBatch _sbStatic;
        private static SpriteBatch SbOf(UiKit ui) => _sbStatic;
        public static void Bind(SpriteBatch sb) => _sbStatic = sb;

        private static Color ToColor(int[] c) => c != null && c.Length >= 3 ? new Color(c[0], c[1], c[2]) : Color.Gray;
    }
}
