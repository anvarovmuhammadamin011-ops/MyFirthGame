using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.UI.Design;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using AndijonSaltanatlar.Core;

namespace AndijonSaltanatlar.UI
{
    /// <summary>
    /// Promt 6 — xaritadan boshqa o'yinchi saltanatiga diplomatiya: MEHMONGA BORISH (ochiq snapshot),
    /// SOVG'A (tanga/oziq-ovqat, kunlik limit) va XAT (≤300 belgi, filtr). Hamda kelgan xatlar qutisi.
    /// Barcha tekshiruvlar (limit, uzunlik, filtr, maxfiylik) serverda; bu yer faqat ko'rsatadi va yuboradi.
    /// </summary>
    public sealed partial class OnlinePanel
    {
        /// <summary>0 — yopiq, 1 — mehmon (snapshot), 2 — sovg'a, 3 — xat, 4 — xatlar qutisi.</summary>
        private int _dipMode;
        private int _dipKingdomId;
        private string _dipKingdomName = "";
        private string _giftKind = "coin";
        private int _giftAmount = 100;
        private string _letterDraft = "";

        private const int GiftMaxAmount = 1000;
        private const int LetterMaxLength = 300;

        private void OpenVisit(WarMapRegionDto r)
        {
            _dipMode = 1; _dipKingdomId = r.OwnerKingdomId; _dipKingdomName = r.OwnerName;
            _client.VisitCity(r.OwnerKingdomId);
        }
        private void OpenGift(WarMapRegionDto r)
        {
            _dipMode = 2; _dipKingdomId = r.OwnerKingdomId; _dipKingdomName = r.OwnerName;
            if (_giftAmount < 1) _giftAmount = 100;
        }
        private void OpenLetter(WarMapRegionDto r)
        {
            // Xat endi elchi orqali: elchi oynasi shu saltanat tanlangan holda ochiladi
            OpenEnvoy(r.OwnerKingdomId, r.OwnerName);
        }

        /// <summary>ELCHI oynasi: maktub yozish, qaysi shohga olib borishini tanlash, elchilar holati.</summary>
        public void OpenEnvoy(int kingdomId = 0, string kingdomName = "")
        {
            _dipMode = 6;
            if (kingdomId != 0) { _dipKingdomId = kingdomId; _dipKingdomName = kingdomName ?? ""; }
            _letterDraft = "";
            _client.LoadLetters(false);
        }
        private void OpenTrade(WarMapRegionDto r)
        {
            _dipMode = 5; _dipKingdomId = r.OwnerKingdomId; _dipKingdomName = r.OwnerName;
            _client.VisitCity(r.OwnerKingdomId); // nimalar bilan savdo qilishini ko'rsatish uchun snapshot
        }

        /// <summary>Qasr ma'lumot panelidagi tugmalar (faqat boshqa o'yinchi/AI saltanati uchun): mehmon, savdo, sovg'a, xat.</summary>
        private int DrawDiplomacyRow(int x, int y, int iw, WarMapRegionDto r)
        {
            UiKit ui = _ui; int h = ui.ControlH, gap = ui.Gap;
            int bw = (iw - 2 * gap) / 3;
            if (ui.Button(new Rectangle(x, y, bw, h), "MEHMON", ButtonKind.Secondary, !_client.VisitBusy, "Shaharni ochiq ko'rish")) OpenVisit(r);
            if (ui.Button(new Rectangle(x + bw + gap, y, bw, h), "SOVG'A", ButtonKind.Secondary, true, "Tanga yoki oziq-ovqat yuborish")) OpenGift(r);
            if (ui.Button(new Rectangle(x + 2 * (bw + gap), y, iw - 2 * (bw + gap), h), "XAT", ButtonKind.Secondary, true, "Xat yozish")) OpenLetter(r);
            // Savdo — alohida keng tugma (savdo xaritasi: davlat bilan savdo yo'li va taklif)
            if (ui.Button(new Rectangle(x, y + h + gap, iw, h), "SAVDO YO'LI / TAKLIF", ButtonKind.Primary, !_client.VisitBusy, "Bu davlat bilan savdo qilish")) OpenTrade(r);
            return y + 2 * (h + gap);
        }

        private void DrawDiplomacyOverlay(int vw, int vh)
        {
            if (_dipMode == 0) return;
            switch (_dipMode)
            {
                case 1: DrawVisitCard(); break;
                case 2: DrawGiftModal(); break;
                case 3: DrawLetterModal(); break;
                case 4: DrawInboxModal(); break;
                case 5: DrawTradeModal(); break;
                case 6: DrawEnvoyModal(); break;
            }
        }

        // ------------------------------------------------------------------ MEHMONGA BORISH
        private void DrawVisitCard()
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            CitySnapshotDto v = _client.Visit;
            string title = (v != null ? v.KingdomName : _dipKingdomName) + " — mehmon";
            Rectangle m = ui.Modal(580, 560, title);
            int x = m.X + 20, w = m.Width - 40;
            if (ui.IconButton(new Rectangle(m.Right - ui.ControlH - 8, m.Y + 10, ui.ControlH, ui.ControlH), "×", "Yopish")) { CloseDip(); return; }
            int y = m.Y + 64;

            if (v == null || v.KingdomId != _dipKingdomId)
            {
                ui.Paragraph(_client.VisitBusy ? "Shahar yuklanmoqda…" : "Ma'lumot yo'q.", new Rectangle(x, y, w, 60), TextStyle.Body, T.TextSecondary);
                return;
            }

            // Bayroq + hukmdor
            var flag = new Rectangle(x, y, 84, 56);
            FlagRenderer.DrawFramed(ui, flag, FlagRenderer.Spec(v.Flag, T.Accent), Ink);
            int tx = flag.Right + 16;
            ui.TextFit(v.RulerName + (v.IsAi ? " (sun'iy)" : " (o'yinchi)"), new Rectangle(tx, y + 2, m.Right - 20 - tx, (int)ui.Type.LineHeight(TextStyle.PanelTitle)), TextStyle.PanelTitle);
            if (v.Shielded) ui.Badge(new Vector2(tx, y + 30), "Qalqon ostida", T.Secondary);
            y = flag.Bottom + 14;

            // Ko'rsatkichlar
            float lh = ui.Type.LineHeight(TextStyle.Secondary);
            void Row(string a, string b)
            {
                ui.TextFit(a, new Rectangle(x, y, w / 2, (int)lh), TextStyle.Secondary, T.TextSecondary);
                ui.Type.DrawRight(_sbOf(), b, new Vector2(x + w, y), TextStyle.Secondary, T.TextPrimary);
                y += (int)lh + 6;
            }
            Row("Aholi", v.Population.ToString());
            Row("Saltanat darajasi", v.Development.ToString());
            Row("Hududlar", v.RegionCount.ToString());
            y += 4;

            ui.TextFit("BINOLAR", new Rectangle(x, y, w, (int)ui.Type.LineHeight(TextStyle.PanelTitle)), TextStyle.PanelTitle, T.Accent);
            y += (int)ui.Type.LineHeight(TextStyle.PanelTitle) + 6;
            int by = m.Bottom - ui.ControlH - 18;
            var listView = new Rectangle(x, y, w, by - y - 10);
            if (v.Buildings.Count == 0)
                ui.Paragraph("Ochiq binolar ko'rinmadi.", listView, TextStyle.Secondary, T.TextSecondary);
            else
            {
                int rowH = (int)lh + 10;
                int scroll = ui.Scroll("visit-bld", listView, v.Buildings.Count * rowH);
                for (int i = 0; i < v.Buildings.Count; i++)
                {
                    CityBuildingDto b = v.Buildings[i];
                    var rr = new Rectangle(listView.X, listView.Y + i * rowH - scroll, listView.Width, rowH - 4);
                    if (!UiKit.InView(rr, listView)) continue;
                    ui.TextFit(BuildingKindName(b.Type) + (b.Count > 1 ? " ×" + b.Count : ""), new Rectangle(rr.X + 6, rr.Y, rr.Width - 120, rr.Height), TextStyle.Secondary);
                    ui.Type.DrawRight(_sbOf(), "daraja " + b.Level, new Vector2(rr.Right - 6, rr.Y + 2), TextStyle.Caption, T.TextSecondary);
                }
            }

            // Pastki tugmalar
            int bw = (w - ui.Gap * 2) / 3;
            if (ui.Button(new Rectangle(x, by, bw, ui.ControlH), "SOVG'A", ButtonKind.Primary)) _dipMode = 2;
            if (ui.Button(new Rectangle(x + bw + ui.Gap, by, bw, ui.ControlH), "XAT", ButtonKind.Secondary)) { _dipMode = 3; _letterDraft = ""; }
            if (ui.Button(new Rectangle(x + 2 * (bw + ui.Gap), by, w - 2 * (bw + ui.Gap), ui.ControlH), "Yopish", ButtonKind.Ghost)) CloseDip();
        }

        // ------------------------------------------------------------------ SOVG'A
        private void DrawGiftModal()
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            Rectangle m = ui.Modal(520, 360, "Sovg'a — " + _dipKingdomName);
            int x = m.X + 20, w = m.Width - 40, y = m.Y + 64;
            if (ui.IconButton(new Rectangle(m.Right - ui.ControlH - 8, m.Y + 10, ui.ControlH, ui.ControlH), "×", "Yopish")) { CloseDip(); return; }

            int h = ui.ControlH;
            int cw = (w - ui.Gap) / 2;
            if (ui.Chip(new Rectangle(x, y, cw, h - 6), "Tanga", _giftKind == "coin", T.Accent)) _giftKind = "coin";
            if (ui.Chip(new Rectangle(x + cw + ui.Gap, y, cw, h - 6), "Oziq-ovqat", _giftKind == "food", T.Success)) _giftKind = "food";
            y += h + ui.Gap;

            y = Stepper(x, y, "Miqdor", ref _giftAmount, new[] { -100, -10, 10, 100 }, 1, GiftMaxAmount, _giftKind == "coin" ? " tanga" : " oziq");
            _giftAmount = Math.Clamp(_giftAmount, 1, GiftMaxAmount);
            ui.Paragraph("Kuniga 3 tagacha sovg'a yuborish mumkin, har biri " + GiftMaxAmount + " dan oshmasin.",
                new Rectangle(x, y, w, (int)(ui.Type.LineHeight(TextStyle.Secondary) * 2 + 4)), TextStyle.Secondary, T.TextSecondary, 2);

            int by = m.Bottom - h - 18, bw = (w - ui.Gap) / 2;
            if (ui.Button(new Rectangle(x, by, bw, h), "Bekor qilish", ButtonKind.Secondary)) CloseDip();
            if (ui.Button(new Rectangle(x + bw + ui.Gap, by, bw, h), "Yuborish", ButtonKind.Primary, !_client.Busy))
            { _client.SendGift(_dipKingdomId, _giftKind, _giftAmount); CloseDip(); }
        }

        // ------------------------------------------------------------------ XAT
        private void DrawLetterModal()
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            Rectangle m = ui.Modal(560, 420, "Xat — " + _dipKingdomName);
            int x = m.X + 20, w = m.Width - 40, y = m.Y + 64;
            if (ui.IconButton(new Rectangle(m.Right - ui.ControlH - 8, m.Y + 10, ui.ControlH, ui.ControlH), "×", "Yopish")) { CloseDip(); return; }
            int h = ui.ControlH;

            _letterDraft = ui.TextInput(new Rectangle(x, y, w, h), "letter", _letterDraft, "Xat matnini kiriting…", false,
                () => AndroidKeyboard("letter", "Xat matni", _letterDraft, false), LetterMaxLength);
            y += h + 8;
            // To'liq matn (o'ralgan) va belgilar hisobi
            int previewH = (int)(ui.Type.LineHeight(TextStyle.Secondary) * 4 + 8);
            ui.Frame(new Rectangle(x, y, w, previewH), T.Surface, T.Border, 1, false);
            if (!string.IsNullOrEmpty(_letterDraft))
                ui.Paragraph(_letterDraft, new Rectangle(x + 8, y + 6, w - 16, previewH - 12), TextStyle.Secondary, T.TextPrimary, 4);
            y += previewH + 6;
            ui.Type.DrawRight(_sbOf(), _letterDraft.Length + " / " + LetterMaxLength, new Vector2(x + w, y), TextStyle.Caption,
                _letterDraft.Length >= LetterMaxLength ? T.Warning : T.TextSecondary);

            int by = m.Bottom - h - 18, bw = (w - ui.Gap) / 2;
            if (ui.Button(new Rectangle(x, by, bw, h), "Bekor qilish", ButtonKind.Secondary)) CloseDip();
            if (ui.Button(new Rectangle(x + bw + ui.Gap, by, bw, h), "Yuborish", ButtonKind.Primary, !_client.Busy && _letterDraft.Trim().Length > 0))
            { _client.SendLetter(_dipKingdomId, _letterDraft.Trim()); CloseDip(); }
        }

        // ------------------------------------------------------------------ ELCHI
        private void DrawEnvoyModal()
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            LettersResponse inbox = _client.Inbox;
            string envoy = string.IsNullOrEmpty(inbox?.MyEnvoy) ? "Elchi" : inbox.MyEnvoy;
            Rectangle m = ui.Modal(660, 600, envoy.ToUpperInvariant() + " — saltanat elchisi");
            if (ui.IconButton(new Rectangle(m.Right - ui.ControlH - 8, m.Y + 10, ui.ControlH, ui.ControlH), "×", "Yopish")) { CloseDip(); return; }
            int x = m.X + 20, w = m.Width - 40, y = m.Y + 62, h = ui.ControlH;
            float lh = ui.Type.LineHeight(TextStyle.Secondary);

            // Elchi qiyofasi va so'zi
            var face = new Rectangle(x, y, 64, 72);
            ui.Frame(face, new Color(48, 40, 30), T.Border, 1, false);
            Texture2D tex = _assets.TryGet(SpriteKeys.UnitCitizen + AndijonSaltanatlar.Core.Graphics.CitizenArt.Merchant + "_0");
            if (tex != null)
            {
                float k = Math.Min(60f / tex.Width, 68f / tex.Height) * 1.6f;
                _sbOf().Draw(tex, new Vector2(face.Center.X, face.Bottom - 2), null, Color.White, 0f, new Vector2(tex.Width / 2f, tex.Height), Math.Min(k, 68f / tex.Height), SpriteEffects.None, 0f);
            }
            ui.Paragraph("\"Hazratim, maktubingizni yozing va qaysi shohga olib borishimni ayting — o'zim yetkazaman.\"",
                new Rectangle(face.Right + 12, y + 4, w - 76, (int)(lh * 3 + 4)), TextStyle.Secondary, T.TextPrimary, 3);
            y += face.Height + 10;

            // Qayerga: saltanatlar (o'zimizdan va egasiz hududlardan boshqa)
            ui.Text("Qayerga olib borsin?", new Vector2(x, y), TextStyle.Secondary, T.TextSecondary);
            y += (int)lh + 4;
            var kingdoms = (_client.War?.MapRegions ?? new List<WarMapRegionDto>())
                .Where(r => !r.IsMine && r.OwnerKingdomId != 0).GroupBy(r => r.OwnerKingdomId)
                .Select(g => (Id: g.Key, Name: g.First().OwnerName, Ruler: g.First().RulerName)).OrderBy(k => k.Name).ToList();
            int cw = Math.Max(120, (w - ui.Gap * 2) / 3), col = 0;
            foreach (var k in kingdoms.Take(9))
            {
                var cr = new Rectangle(x + col * (cw + ui.Gap), y, cw, h - 6);
                string label = k.Name + (string.IsNullOrEmpty(k.Ruler) ? "" : " · " + k.Ruler);
                if (ui.Chip(cr, label, _dipKingdomId == k.Id)) { _dipKingdomId = k.Id; _dipKingdomName = k.Name; }
                if (++col == 3) { col = 0; y += h; }
            }
            if (col != 0) y += h;
            if (kingdoms.Count == 0) { ui.Text("Xarita yuklanmoqda…", new Vector2(x, y), TextStyle.Caption, T.TextSecondary); y += (int)lh + 4; }
            y += 4;

            // Maktub
            _letterDraft = ui.TextInput(new Rectangle(x, y, w, h), "letter", _letterDraft, "Maktub matni (masalan: Salom, savdo qilaylik)…", false,
                () => AndroidKeyboard("letter", "Maktub", _letterDraft, false), LetterMaxLength);
            y += h + 4;
            ui.Type.DrawRight(_sbOf(), _letterDraft.Length + " / " + LetterMaxLength, new Vector2(x + w, y), TextStyle.Caption, T.TextSecondary);
            bool ready = _dipKingdomId != 0 && _letterDraft.Trim().Length > 0 && !_client.Busy;
            string go = _dipKingdomId != 0 ? "ELCHIGA TOPSHIRISH — " + _dipKingdomName : "ELCHIGA TOPSHIRISH";
            if (ui.Button(new Rectangle(x, y + (int)lh + 6, w, h), go, ButtonKind.Primary, ready, "Elchi maktubni o'sha shohga olib boradi"))
            { _client.SendLetter(_dipKingdomId, _letterDraft.Trim()); _letterDraft = ""; }
            y += (int)lh + 6 + h + 12;

            // Elchilar holati
            ui.Text("Elchilaringiz:", new Vector2(x, y), TextStyle.Secondary, T.TextSecondary);
            y += (int)lh + 4;
            var envoys = inbox?.Envoys ?? new List<EnvoyDto>();
            if (envoys.Count == 0) ui.Text("Hali maktub yuborilmagan.", new Vector2(x, y), TextStyle.Caption, T.TextSecondary);
            float since = (float)(DateTime.UtcNow - _client.InboxLoadedAt).TotalSeconds;
            foreach (EnvoyDto e in envoys)
            {
                if (y + lh > m.Bottom - 12) break;
                int left = Math.Max(0, e.SecondsLeft - (int)since);
                string st = !e.Delivered && left > 0 ? "yo'lda — " + left + " s" : e.Read ? "yetkazdi, shoh O'QIDI" : "yetkazdi";
                ui.TextFit(e.EnvoyName + " → " + e.ToName + ": " + st + "   \"" + e.Preview + "\"", new Rectangle(x, y, w, (int)lh), TextStyle.Caption,
                    e.Read ? T.Primary : !e.Delivered ? T.Accent : T.TextPrimary);
                y += (int)lh + 2;
            }
        }

        /// <summary>Xaritada yo'ldagi elchilar: kichik otliq-chopar belgisi va qolgan vaqt.</summary>
        private void DrawEnvoysOnMap(UzMapCanvas cv, WarOverviewDto w, Func<int, Vector2> pos, float s)
        {
            LettersResponse inbox = _client.Inbox;
            if (inbox?.Envoys == null) return;
            float since = (float)(DateTime.UtcNow - _client.InboxLoadedAt).TotalSeconds;
            bool travelling = false;
            foreach (EnvoyDto e in inbox.Envoys)
            {
                float left = e.SecondsLeft - since;
                if (e.Delivered || left <= 0f) continue;
                travelling = true;
                Vector2 a = pos(e.FromRegionId), b = pos(e.ToRegionId);
                if (a.X < -9000 || b.X < -9000) continue;
                float t = MathHelper.Clamp(1f - left / Math.Max(1, e.TotalSeconds), 0f, 1f);
                cv.DashedLine(a, b, new Color(120, 90, 40) * 0.7f, 2, 6, 6);
                Vector2 p = Vector2.Lerp(a, b, t) + new Vector2(0, MathF.Sin(_ui.Time * 10) * 1.5f);
                int r = (int)(8 * s);
                _ui.Rounded(new Rectangle((int)p.X - r - 2, (int)p.Y - r - 2, 2 * r + 4, 2 * r + 4), new Color(60, 44, 30), r + 2);
                _ui.Rounded(new Rectangle((int)p.X - r, (int)p.Y - r, 2 * r, 2 * r), new Color(240, 226, 190), r);
                _ui.Fill(new Rectangle((int)p.X - r / 2, (int)p.Y - r / 3, r, (int)(r * 0.7f)), new Color(200, 160, 90)); // maktub o'rami
                _ui.Fill(new Rectangle((int)p.X - 1, (int)p.Y - r / 3, 2, (int)(r * 0.7f)), new Color(150, 40, 30));     // muhr
                string lbl = e.EnvoyName + " · " + (int)Math.Ceiling(left) + " s";
                Vector2 ls = _ui.Type.Measure(lbl, TextStyle.Caption);
                var lr = new Rectangle((int)(p.X - ls.X / 2 - 6), (int)(p.Y + r + 4), (int)ls.X + 12, (int)_ui.Type.LineHeight(TextStyle.Caption) + 4);
                _ui.Frame(lr, new Color(250, 240, 214), Ink, 1, false);
                _ui.Type.DrawFitted(_sbOf(), lbl, lr, TextStyle.Caption, Ink, 1);
            }
            // Elchi yo'lda bo'lsa holat tez-tez yangilanadi (yetib bordimi, javob keldimi)
            if (travelling && (DateTime.UtcNow - _client.InboxLoadedAt).TotalSeconds > 4) _client.LoadLetters(false);
        }

        // ------------------------------------------------------------------ XATLAR QUTISI
        private void DrawInboxModal()
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            LettersResponse inbox = _client.Inbox;
            int unread = inbox?.Unread ?? 0;
            Rectangle m = ui.Modal(600, 560, "Xatlar qutisi" + (unread > 0 ? " · " + unread + " yangi" : ""));
            int x = m.X + 20, w = m.Width - 40, y = m.Y + 64;
            if (ui.IconButton(new Rectangle(m.Right - ui.ControlH - 8, m.Y + 10, ui.ControlH, ui.ControlH), "×", "Yopish")) { CloseDip(); return; }

            var view = new Rectangle(x, y, w, m.Bottom - y - 18);
            if (inbox == null || inbox.Letters.Count == 0)
            {
                ui.Paragraph("Hozircha xat yo'q. Xaritada boshqa o'yinchiga xat yuborishingiz mumkin.", view, TextStyle.Body, T.TextSecondary);
                return;
            }
            float lh = ui.Type.LineHeight(TextStyle.Secondary);
            int gap = 8;
            // Matn uzunligidan taxminiy qator soni (bitta qatordagi kenglikka bo'lib) — karta balandligi uchun
            int Lines(LetterDto l) => Math.Clamp((int)Math.Ceiling(ui.Type.Measure(l.Text ?? "", TextStyle.Secondary).X / Math.Max(1, w - 28)), 1, 6);
            int Height(LetterDto l) => (int)(lh + 6) + (int)(Lines(l) * lh) + 16;
            int total = inbox.Letters.Sum(l => Height(l) + gap);
            int scroll = ui.Scroll("inbox", view, total);
            int cy = view.Y - scroll;
            foreach (LetterDto l in inbox.Letters)
            {
                int ch = Height(l);
                var r = new Rectangle(view.X, cy, view.Width, ch);
                cy += ch + gap;
                if (r.Bottom < view.Y || r.Y > view.Bottom) continue;
                ui.Card(r, !l.Read);
                if (!l.Read) ui.Fill(new Rectangle(r.X + 6, r.Y + 8, 4, ch - 16), T.Accent);
                ui.TextFit(l.FromName + (string.IsNullOrEmpty(l.EnvoyName) ? "" : "  ·  " + l.EnvoyName + " olib keldi"), new Rectangle(r.X + 16, r.Y + 8, r.Width - 150, (int)lh), TextStyle.PanelTitle, l.Read ? T.TextPrimary : T.Accent);
                ui.Type.DrawRight(_sbOf(), l.SentAt.ToLocalTime().ToString("dd.MM HH:mm"), new Vector2(r.Right - 12, r.Y + 10), TextStyle.Caption, T.TextSecondary);
                ui.Paragraph(l.Text, new Rectangle(r.X + 16, r.Y + (int)lh + 10, r.Width - 28, ch - (int)lh - 18), TextStyle.Secondary, T.TextPrimary, 6);
            }
        }

        // ------------------------------------------------------------------ SAVDO (savdo yo'li va taklif)
        private void DrawTradeModal()
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            CitySnapshotDto v = _client.Visit;
            Rectangle m = ui.Modal(560, 520, "Savdo — " + _dipKingdomName);
            int x = m.X + 20, w = m.Width - 40, y = m.Y + 64, h = ui.ControlH;
            if (ui.IconButton(new Rectangle(m.Right - h - 8, m.Y + 10, h, h), "×", "Yopish")) { CloseDip(); return; }

            if (v == null || v.KingdomId != _dipKingdomId)
            {
                ui.Paragraph(_client.VisitBusy ? "Savdo ma'lumoti yuklanmoqda…" : "Ma'lumot yo'q.", new Rectangle(x, y, w, 60), TextStyle.Body, T.TextSecondary);
                return;
            }
            int lh = (int)ui.Type.LineHeight(TextStyle.Secondary);
            ui.TextFit(v.RulerName + (v.IsAi ? " (sun'iy davlat)" : " (o'yinchi)"), new Rectangle(x, y, w, (int)ui.Type.LineHeight(TextStyle.PanelTitle)), TextStyle.PanelTitle);
            y += (int)ui.Type.LineHeight(TextStyle.PanelTitle) + 8;

            ui.TextFit("Bu saltanat nimalar bilan savdo qiladi:", new Rectangle(x, y, w, lh), TextStyle.Secondary, T.Accent);
            y += lh + 4;
            foreach (string g in TradeGoods(v)) { ui.TextFit("•  " + g, new Rectangle(x + 6, y, w - 6, lh), TextStyle.Secondary, T.TextPrimary); y += lh + 2; }
            y += 10;

            ui.Paragraph("Savdo taklifi yuboring — qabul qilsa, «Xatlar» orqali javob yozadi:", new Rectangle(x, y, w, lh * 2), TextStyle.Secondary, T.TextSecondary, 2);
            y += lh * 2 + 4;
            string[] proposals =
            {
                "Men tanga beraman — siz oziq-ovqat bering",
                "Men oziq-ovqat beraman — siz tanga bering",
                "Doimiy savdo yo'li ochaylik — o'zaro mahsulot almashaylik",
            };
            int by = m.Bottom - h - 16;
            foreach (string p in proposals)
            {
                if (y + h > by) break;
                if (ui.Button(new Rectangle(x, y, w, h), p, ButtonKind.Secondary, !_client.Busy))
                {
                    _client.SendLetter(_dipKingdomId, "SAVDO TAKLIFI: " + p + ". Rozimisiz?");
                    CloseDip();
                    return;
                }
                y += h + 6;
            }
            if (ui.Button(new Rectangle(x, by, w, h), "Yopish", ButtonKind.Ghost)) CloseDip();
        }

        /// <summary>Snapshot binolaridan saltanatning savdo mahsulotlari ro'yxati.</summary>
        private static List<string> TradeGoods(CitySnapshotDto v)
        {
            var goods = new List<string>();
            void Add(string s) { if (!goods.Contains(s)) goods.Add(s); }
            foreach (CityBuildingDto b in v.Buildings)
                switch (b.Type)
                {
                    case "Farm": Add("Guruch va oziq-ovqat"); break;
                    case "Garden": Add("Meva va sabzavot"); break;
                    case "Market": Add("Bozor mahsulotlari"); break;
                    case "Caravanserai": Add("Karvon savdosi (uzoq yo'l)"); break;
                    case "Madrasa": Add("Ilm va kitoblar"); break;
                    case "Barracks": Add("Qurol-yarog'"); break;
                }
            if (goods.Count == 0) Add("Asosiy mahsulotlar");
            return goods;
        }

        private void CloseDip() { _dipMode = 0; _client.CloseVisit(); _ui.FocusId = null; }

        private static string BuildingKindName(string kind)
        {
            switch (kind)
            {
                case "House": return "Uy";
                case "Market": return "Bozor";
                case "Tower": return "Minora";
                case "Wall": return "Devor";
                case "Gate": return "Darvoza";
                case "MainHall": return "Saroy";
                case "Garden": return "Bog'";
                case "Barracks": return "Kazarma";
                case "Madrasa": return "Madrasa";
                case "Mosque": return "Masjid";
                case "Caravanserai": return "Karvonsaroy";
                default: return kind;
            }
        }
    }
}
