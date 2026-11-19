using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Shared.War;
using AndijonSaltanatlar.UI.Design;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.UI
{
    /// <summary>
    /// "XARITA" — Farg'ona vodiysi dunyo xaritasi: qasrlar (egasi rangidagi bayroq bilan, poytaxtlar kattaroq),
    /// yo'llar, yurayotgan qo'shinlar (kelib chiqish → nishon, qolgan vaqt) va faol janglar. Qasr tanlanganda
    /// ma'lumot paneli: egasi, himoya bahosi, g'alaba oqibati, qo'shin tanlash va "YURISH BOSHLASH".
    /// Barcha raqamlar serverdan; yurish va jang natijasini faqat server hisoblaydi.
    /// </summary>
    public sealed partial class OnlinePanel
    {
        private int _mapRegion = -1;
        private int _marchArmy;
        private WarOverviewDto _mapWarRef;
        private float _mapFetchTime;

        private UzMapCanvas _warCanvas;

        // --- QAMAL: shahardagi o'z jangchilaringiz bilan yurish (jang o'yinchi kompyuterida, real vaqtda) ---
        /// <summary>Shahardagi jangchilar soni (Game1 beradi).</summary>
        public Func<int> LocalArmyCount;
        /// <summary>Qo'shin qasrga yetib keldi — Game1 qamalni boshlaydi va buni null qiladi.</summary>
        public WarMapRegionDto SiegeArrived { get; set; }
        public bool SiegeMarching => _siegeTarget != null;
        /// <summary>Yurish boshlandi — Game1 shaharda qo'shin chiqib ketishini ko'rsatadi.</summary>
        public Action<WarMapRegionDto> SiegeDeparted;
        private WarMapRegionDto _siegeTarget;
        private int _siegeHome = -1;
        private int[] _siegeColor = { 70, 130, 220 };
        private string _siegeFlag = "";
        private DateTime _siegeArrive;
        private int _siegeTotal;

        /// <summary>Har kadr (xarita yopiq bo'lsa ham): yurish vaqti tugadimi.</summary>
        private void UpdateSiegeMarch()
        {
            if (_siegeTarget != null && DateTime.UtcNow >= _siegeArrive)
            {
                SiegeArrivedLocal = _siegeLocal;
                SiegeArrived = _siegeTarget;
                _siegeTarget = null;
            }
        }

        /// <summary>Yurish serversiz dunyoda boshlangan (natija qurilmada qo'llanadi).</summary>
        public bool SiegeArrivedLocal { get; private set; }
        private bool _siegeLocal;

        private void BeginSiegeMarch(WarMapRegionDto r, WarMapRegionDto home)
        {
            WarMapRegionDto target = r;
            bool local = LocalMode;
            Action<bool> go = ok =>
            {
                if (!ok) return;
                _siegeLocal = local;
                _siegeTarget = target;
                _siegeHome = home?.RegionId ?? -1;
                _siegeColor = home?.Color ?? _siegeColor;
                _siegeFlag = home?.Flag ?? "";
                _siegeTotal = Math.Max(5, target.MarchSeconds);
                _siegeArrive = DateTime.UtcNow.AddSeconds(_siegeTotal);
                SiegeDeparted?.Invoke(target);
            };
            if (local) go(true); else _client.SiegeStart(r.RegionId, go);
        }
        private readonly List<MapCell> _cellBuf = new List<MapCell>();

        private void DrawWarMap(Rectangle c, WarOverviewDto w)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            if (!ReferenceEquals(_mapWarRef, w)) { _mapWarRef = w; _mapFetchTime = ui.Time; }
            float since = ui.Time - _mapFetchTime;
            // Yurishlar davom etayotgan bo'lsa ma'lumot har 3 soniyada yangilanadi (yetib kelish/jang boshlanishi).
            if (!LocalMode && (w.Marches.Count > 0 || w.ActiveBattle != null) && since > 3f && !_client.WarBusy) { _mapFetchTime = ui.Time; _client.LoadWar(); }

            bool side = c.Width >= 860;
            int infoW = side ? Math.Min((int)(380 * Math.Max(1f, ui.Type.UiScale)), c.Width * 2 / 5) : c.Width;
            var map = side ? new Rectangle(c.X, c.Y, c.Width - infoW - ui.Gap, c.Height) : new Rectangle(c.X, c.Y, c.Width, c.Height * 58 / 100);
            var info = side ? new Rectangle(map.Right + ui.Gap, c.Y, infoW, c.Height) : new Rectangle(c.X, map.Bottom + ui.Gap, c.Width, c.Bottom - map.Bottom - ui.Gap);

            _warCanvas ??= new UzMapCanvas(ui);
            UzMapCanvas cv = _warCanvas;
            WarMapRegionDto home = w.MapRegions.Where(r => r.IsMine).OrderByDescending(r => r.IsCapital).FirstOrDefault();
            if (!cv.Focused && home != null) cv.FocusOn(home.MapX, home.MapY, 6f);
            cv.Begin(map);

            _cellBuf.Clear();
            foreach (WarMapRegionDto r in w.MapRegions) _cellBuf.Add(new MapCell(r.RegionId, r.MapX, r.MapY, r.OwnerKingdomId, Col(r.Color), r.Province, r.IsMine));
            cv.DrawBase(_cellBuf, _mapRegion, T.Focus);

            Vector2 Pos(int regionId)
            {
                WarMapRegionDto r = w.MapRegions.FirstOrDefault(x => x.RegionId == regionId);
                return r == null ? new Vector2(-9999) : cv.ToScreen(r.MapX, r.MapY);
            }

            // Faol jang — qasr atrofida qizil pulsatsiya
            if (w.ActiveBattle != null)
            {
                Vector2 bp = Pos(w.ActiveBattle.RegionId);
                int g = (int)(28 * cv.MarkerScale + 8 * MathF.Sin(ui.Time * 6));
                ui.Rounded(new Rectangle((int)bp.X - g, (int)bp.Y - g, 2 * g, 2 * g), T.Danger * 0.28f, g);
            }

            // Qasrlar (uzoqdagilari birinchi — yaqindagilari ustida)
            float s = cv.MarkerScale;
            foreach (WarMapRegionDto r in w.MapRegions.OrderBy(x => x.MapY))
            {
                Vector2 p = cv.ToScreen(r.MapX, r.MapY);
                if (!cv.InView(p, 60)) continue;
                DrawCastle(p, r, s, r.RegionId == _mapRegion, w.Marches.Any(m => m.TargetRegionId == r.RegionId && m.TargetIsMine));
            }
            // Yozuvlar: avval tanlangan, o'zimniki va poytaxtlar, keyin aholisi ko'plari — ustma-ust tushganlari chiqmaydi
            bool showAll = cv.Zoom >= 2.4f;
            foreach (WarMapRegionDto r in w.MapRegions.OrderByDescending(x => x.RegionId == _mapRegion).ThenByDescending(x => x.IsMine).ThenByDescending(x => x.IsCapital).ThenByDescending(x => x.Population))
            {
                bool important = r.RegionId == _mapRegion || r.IsMine || (r.IsCapital && r.OwnerKingdomId != 0);
                if (!showAll && !important) continue;
                Vector2 p = cv.ToScreen(r.MapX, r.MapY);
                if (!cv.InView(p)) continue;
                cv.Label(r.Name, p + new Vector2(0, 4), r.IsMine ? new Color(40, 70, 130) : Ink, r.IsMine ? new Color(30, 60, 120) : Ink, r.RegionId == _mapRegion);
            }

            // Yurayotgan qo'shinlar
            foreach (MarchDto m in w.Marches)
            {
                Vector2 a = Pos(m.OriginRegionId), b = Pos(m.TargetRegionId);
                if (a.X < -9000 || b.X < -9000) continue;
                Color kc = new Color(m.Color[0], m.Color[1], m.Color[2]);
                float left = Math.Max(0f, m.SecondsLeft - since);
                float t = MathHelper.Clamp(1f - left / Math.Max(1, m.TotalSeconds), 0f, 1f);
                cv.DashedLine(a, b, kc * 0.85f, 3, 12, 6);
                Vector2 p = Vector2.Lerp(a, b, t) + new Vector2(0, MathF.Sin(ui.Time * 8 + m.ArmyId) * 2);
                DrawArmyMarker(p, kc, s, m.UnitCount);
                FlagRenderer.DrawOnPole(ui, p + new Vector2(0, -11 * s), (int)(16 * s), (int)(16 * s), FlagRenderer.Spec(m.Flag, kc), ui.Time, m.ArmyId);
                string lbl = (m.IsMine ? "" : m.KingdomName + " · ") + (int)Math.Ceiling(left) + " s";
                Vector2 ls = ui.Type.Measure(lbl, TextStyle.Caption);
                var lr = new Rectangle((int)(p.X - ls.X / 2 - 6), (int)(p.Y + 12 * s), (int)ls.X + 12, (int)ui.Type.LineHeight(TextStyle.Caption) + 4);
                ui.Frame(lr, m.TargetIsMine ? new Color(150, 40, 30) : new Color(236, 220, 180), Ink, 1, false);
                ui.Type.DrawFitted(_sbOf(), lbl, lr, TextStyle.Caption, m.TargetIsMine ? new Color(250, 236, 210) : Ink, 1);
            }

            DrawEnvoysOnMap(cv, w, Pos, s);

            // Qamalga ketayotgan o'z qo'shinimiz (shahardagi jangchilar)
            if (_siegeTarget != null)
            {
                Vector2 a = Pos(_siegeHome), b = Pos(_siegeTarget.RegionId);
                if (a.X > -9000 && b.X > -9000)
                {
                    Color kc = Col(_siegeColor);
                    float left = (float)Math.Max(0, (_siegeArrive - DateTime.UtcNow).TotalSeconds);
                    float t = MathHelper.Clamp(1f - left / Math.Max(1, _siegeTotal), 0f, 1f);
                    cv.DashedLine(a, b, kc * 0.9f, 3, 12, 6);
                    Vector2 p = Vector2.Lerp(a, b, t) + new Vector2(0, MathF.Sin(ui.Time * 8) * 2);
                    DrawArmyMarker(p, kc, s, LocalArmyCount?.Invoke() ?? 0);
                    FlagRenderer.DrawOnPole(ui, p + new Vector2(0, -11 * s), (int)(16 * s), (int)(16 * s), FlagRenderer.Spec(_siegeFlag, kc), ui.Time, 7);
                    string lbl = "Qamalga: " + (int)Math.Ceiling(left) + " s";
                    Vector2 ls = ui.Type.Measure(lbl, TextStyle.Caption);
                    var lr = new Rectangle((int)(p.X - ls.X / 2 - 6), (int)(p.Y + 12 * s), (int)ls.X + 12, (int)ui.Type.LineHeight(TextStyle.Caption) + 4);
                    ui.Frame(lr, new Color(236, 220, 180), Ink, 1, false);
                    ui.Type.DrawFitted(_sbOf(), lbl, lr, TextStyle.Caption, Ink, 1);
                }
            }

            // Bosish: eng yaqin qasr (diplomatiya oynasi ochiq bo'lsa — xaritaga bosish o'tmaydi)
            if (_dipMode == 0)
            {
                int picked = cv.PickNearest(w.MapRegions.Select(r => (r.RegionId, cv.ToScreen(r.MapX, r.MapY) + new Vector2(0, -12 * s))), 34 * s + 14);
                if (picked >= 0) _mapRegion = picked;
            }

            cv.DrawOverlay("O'zbekiston");
            DrawMapInfo(info, w);
        }

        private static Color Col(int[] c) => c != null && c.Length >= 3 ? new Color(c[0], c[1], c[2]) : Color.Gray;

        /// <summary>Qadimiy xarita siyohi (to'q jigarrang).</summary>
        private static readonly Color Ink = new Color(70, 48, 28);

        private void DrawCastle(Vector2 p, WarMapRegionDto r, float s, bool selected, bool threatened)
        {
            UiKit ui = _ui;
            Color owner = Col(r.Color);
            bool neutral = r.OwnerKingdomId == 0;
            float k = r.IsCapital && !neutral ? 1.25f : neutral ? 0.7f : 1f;
            int w = (int)(40 * s * k), h = (int)(22 * s * k), tw = Math.Max(4, (int)(10 * s * k)), th = (int)(34 * s * k);
            if (selected) ui.Rounded(new Rectangle((int)p.X - w, (int)p.Y - h / 2, w * 2, h + 4), ui.T.Focus * 0.5f, h / 2);
            if (threatened) { int g = (int)(4 + 3 * MathF.Sin(ui.Time * 9)); ui.Rounded(new Rectangle((int)p.X - w - g, (int)p.Y - h / 2 - g, w * 2 + 2 * g, h + 4 + 2 * g), ui.T.Danger * 0.4f, h / 2 + g); }
            Color stone = new Color(232, 214, 172), dark = Ink;
            int baseY = (int)p.Y;
            if (neutral)
            {
                // Mustaqil qishloq: kichik uycha (bayroqsiz)
                ui.Frame(new Rectangle((int)p.X - w / 2, baseY - h, w, h), stone, dark, 1, false);
                int roof = Math.Max(2, h * 2 / 3);
                for (int row = 0; row < roof; row++)
                {
                    int half = (int)(w / 2f * row / (float)roof) + 1;
                    ui.Fill(new Rectangle((int)p.X - half, baseY - h - roof + row, half * 2, 1), new Color(150, 92, 60));
                }
                ui.Fill(new Rectangle((int)p.X - 2, baseY - h / 2, 4, h / 2), new Color(70, 50, 34));
                return;
            }
            // Devor (siyoh konturli)
            ui.Frame(new Rectangle((int)p.X - w / 2, baseY - h, w, h), stone, dark, 2, false);
            for (int i = 0; i < 5; i++) ui.Frame(new Rectangle((int)p.X - w / 2 + i * w / 5 + 1, baseY - h - (int)(5 * s), w / 10 + 2, (int)(5 * s) + 2), stone, dark, 1, false);
            // Minoralar (tomi egasi rangida)
            foreach (int sx in new[] { -1, 1 })
            {
                var tr = new Rectangle((int)p.X + sx * w / 2 - tw / 2, baseY - th, tw, th);
                ui.Frame(tr, stone, dark, 2, false);
                ui.Frame(new Rectangle(tr.X - 2, tr.Y - (int)(6 * s), tr.Width + 4, (int)(6 * s) + 2), owner, dark, 1, false);
            }
            // Darvoza
            ui.Rounded(new Rectangle((int)p.X - (int)(6 * s), baseY - (int)(13 * s * k), (int)(12 * s), (int)(13 * s * k)), new Color(70, 50, 34), (int)(5 * s));
            // Saltanat bayrog'i (hilpiraydi)
            FlagRenderer.DrawOnPole(ui, new Vector2(p.X, baseY - th + 2), (int)(22 * s * k), (int)(24 * s * k), FlagRenderer.Spec(r.Flag, owner), ui.Time, r.RegionId);
            if (r.IsCapital) ui.Fill(new Rectangle((int)p.X - 3, baseY - th - (int)(22 * s * k) - 7, 8, 5), new Color(236, 196, 90)); // toj belgisi
        }

        private void DrawArmyMarker(Vector2 p, Color kc, float s, int units)
        {
            UiKit ui = _ui;
            int r = (int)(11 * s);
            ui.Rounded(new Rectangle((int)p.X - r - 2, (int)p.Y - r - 2, r * 2 + 4, r * 2 + 4), new Color(40, 30, 20), r + 2);
            ui.Rounded(new Rectangle((int)p.X - r, (int)p.Y - r, r * 2, r * 2), kc, r);
            ui.Fill(new Rectangle((int)p.X - 1, (int)p.Y - r - (int)(12 * s), 2, (int)(12 * s)), new Color(60, 44, 30));
            ui.Fill(new Rectangle((int)p.X + 1, (int)p.Y - r - (int)(12 * s), (int)(9 * s), (int)(6 * s)), kc);
            string n = units.ToString();
            Vector2 m = ui.Type.Measure(n, TextStyle.Caption);
            ui.Text(n, new Vector2(p.X - m.X / 2, p.Y - m.Y / 2), TextStyle.Caption, Color.White);
        }

        private void DrawMapInfo(Rectangle info, WarOverviewDto w)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            int h = ui.ControlH, gap = ui.Gap;
            ui.Card(info);
            int x = info.X + 14, y = info.Y + 12, iw = info.Width - 28;
            float lh = ui.Type.LineHeight(TextStyle.Secondary);

            // Menga qarab kelayotgan qo'shinlar — eng yuqorida ogohlantirish
            foreach (MarchDto m in w.Marches.Where(m => m.TargetIsMine))
            {
                var wr = new Rectangle(x, y, iw, (int)(lh * 3 + 12));
                ui.Rounded(wr, T.Danger * 0.2f, 8);
                ui.Paragraph("DIQQAT: " + m.KingdomName + " (" + m.UnitCount + " birlik) " + m.TargetName + " qasringizga kelmoqda — " + m.SecondsLeft + " s", new Rectangle(wr.X + 8, wr.Y + 5, wr.Width - 16, wr.Height - 6), TextStyle.Secondary, T.TextPrimary, 3);
                y += wr.Height + 6;
            }

            WarMapRegionDto r = w.MapRegions.FirstOrDefault(m => m.RegionId == _mapRegion);
            if (r == null)
            {
                // Kimning yeri qayerda: har saltanat — rangi, qasrlari soni, poytaxti va qasrlari ro'yxati
                ui.TextFit("SALTANATLAR VA YERLARI", new Rectangle(x, y, iw, (int)ui.Type.LineHeight(TextStyle.PanelTitle)), TextStyle.PanelTitle, T.Accent);
                y += (int)ui.Type.LineHeight(TextStyle.PanelTitle) + 6;
                var groups = w.MapRegions.Where(m => m.OwnerKingdomId != 0)
                    .GroupBy(m => m.OwnerKingdomId)
                    .OrderByDescending(g => g.Any(m => m.IsMine)).ThenByDescending(g => g.Count());
                foreach (var g in groups)
                {
                    if (y + lh * 2 > info.Bottom - 10) break;
                    var first = g.First();
                    var col = first.Color != null && first.Color.Length >= 3 ? new Color(first.Color[0], first.Color[1], first.Color[2]) : Color.Gray;
                    ui.Fill(new Rectangle(x, y + 3, 14, (int)lh - 6), col);
                    string capital = g.FirstOrDefault(m => m.IsCapital)?.Name ?? first.Name;
                    string who = (first.IsMine ? "SIZ: " : "") + first.OwnerName + (first.OwnerIsAi ? "" : " (o'yinchi)");
                    ui.TextFit(who + " — " + g.Count() + " qasr", new Rectangle(x + 22, y, iw - 22, (int)lh), TextStyle.Secondary, first.IsMine ? T.Primary : T.TextPrimary);
                    y += (int)lh + 2;
                    string list = "Saroy: " + capital + (g.Count() > 1 ? "  ·  " + string.Join(", ", g.Where(m => !m.IsCapital).Select(m => m.Name).Take(6)) : "");
                    ui.TextFit(list, new Rectangle(x + 22, y, iw - 22, (int)lh), TextStyle.Caption, T.TextSecondary);
                    y += (int)lh + 8;
                }
                int free = w.MapRegions.Count(m => m.OwnerKingdomId == 0);
                if (free > 0 && y + lh < info.Bottom - 10) ui.TextFit("Egasiz hududlar: " + free, new Rectangle(x, y, iw, (int)lh), TextStyle.Caption, T.TextSecondary);
                return;
            }
            ui.TextFit(r.Name + (r.IsCapital ? " · poytaxt" : ""), new Rectangle(x, y, iw, (int)ui.Type.LineHeight(TextStyle.PanelTitle)), TextStyle.PanelTitle); y += (int)ui.Type.LineHeight(TextStyle.PanelTitle) + 4;
            void Row(string a, Color? c = null) { ui.TextFit(a, new Rectangle(x, y, iw, (int)lh), TextStyle.Secondary, c ?? T.TextPrimary); y += (int)lh + 4; }
            Row("Viloyat: " + AndijonSaltanatlar.Shared.Geo.UzMap.ProvinceName(r.Province), T.TextSecondary);
            Row("Egasi: " + r.OwnerName + (r.OwnerIsAi || r.IsMine || r.OwnerKingdomId == 0 ? "" : " (real o'yinchi)"));
            Row("Shahar: " + r.CityName + " · aholi " + r.Population);
            if (!r.IsMine && r.OwnerKingdomId != 0) Row("Himoya ≈ " + r.DefenseEstimate);
            if (!r.IsMine && r.MarchSeconds > 0) Row("Yo'l: " + r.MarchSeconds + " soniya · yurish tekin", T.TextSecondary);
            if (r.OutcomeUz.Length > 0) y += (int)ui.Paragraph(r.OutcomeUz, new Rectangle(x, y, iw, (int)(lh * 3 + 6)), TextStyle.Secondary, T.TextSecondary, 3) + 6;
            if (r.IsMine || r.OwnerKingdomId == 0) { if (r.BlockReasonUz.Length > 0) ui.Paragraph(r.BlockReasonUz, new Rectangle(x, y, iw, (int)(lh * 2 + 4)), TextStyle.Secondary, T.TextSecondary, 2); return; }

            // Promt 6: diplomatiya — boshqa saltanatga mehmon borish, sovg'a va xat (faqat onlayn)
            if (!LocalMode) y = DrawDiplomacyRow(x, y, iw, r);

            // Qamal: shahardagi o'z jangchilaringiz bilan (server qo'shini shart emas)
            int local = LocalArmyCount?.Invoke() ?? 0;
            Row("Shahringizdagi qo'shin: " + local + " jangchi", local >= WarRules.MinBattleArmy ? T.TextPrimary : T.Warning);
            if (!string.IsNullOrEmpty(r.RulerName)) Row("Saroyda: shoh " + r.RulerName + " va vaziri", T.TextSecondary);
            string why = !r.CanSiege ? r.BlockReasonUz
                : _siegeTarget != null ? "Qo'shiningiz yo'lda: " + _siegeTarget.Name + " (" + (int)Math.Max(0, (_siegeArrive - DateTime.UtcNow).TotalSeconds) + " s)"
                : local < WarRules.MinBattleArmy ? "Shahringizda kamida " + WarRules.MinBattleArmy + " jangchi bo'lsin (hozir " + local + "). DIAGNOSTIKA → QO'SHIN bo'limida yollang."
                : "";
            bool canGo = r.CanSiege && _siegeTarget == null && local >= WarRules.MinBattleArmy && (LocalMode || !_client.WarBusy);
            int by = info.Bottom - h - 12;
            if (why.Length > 0) ui.Paragraph(why, new Rectangle(x, by - (int)(lh * 2 + 8), iw, (int)(lh * 2 + 4)), TextStyle.Secondary, T.Warning, 2);
            if (ui.Button(new Rectangle(x, by, iw, h), canGo ? "YURISH BOSHLASH" : "Yurish mumkin emas", ButtonKind.Danger, canGo, "Qo'shin yo'lga chiqadi; yetib borgach shahar ichida jang, saroyda shoh va vazir"))
                BeginSiegeMarch(r, w.MapRegions.Where(m => m.IsMine).OrderByDescending(m => m.IsCapital).FirstOrDefault());
        }
    }
}
