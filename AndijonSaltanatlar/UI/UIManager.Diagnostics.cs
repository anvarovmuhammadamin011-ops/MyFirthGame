using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Defense;
using AndijonSaltanatlar.Environment;
using AndijonSaltanatlar.Government;
using AndijonSaltanatlar.Military;
using AndijonSaltanatlar.Population;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.UI
{
    /// <summary>
    /// DIAGNOSTIKA — shahar haqidagi barcha ma'lumot bitta oynada: SHAHAR / AHOLI / QO'SHIN / DAVLAT.
    /// QO'SHIN bo'limi tepasida SOTIB OLISH (10 xil jangchi katalogi), MASHG'ULOT MAYDONI va JANG MAYDONI.
    /// Hamma qiymatlar o'yinning haqiqiy holatidan olinadi.
    /// </summary>
    public sealed partial class UIManager
    {
        public enum DiagTab { City, Population, Army, State }

        private DiagTab _diagTab = DiagTab.City;
        private bool _buyOpen;
        private readonly int[] _buyQty = NewQty(UnitCatalog.All.Count);
        private long _lastBuyTick;

        /// <summary>Har jangchi turi uchun xarid soni (boshida 1) — katalog o'lchamiga moslashadi.</summary>
        private static int[] NewQty(int n)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = 1;
            return a;
        }

        private Rectangle _diagClose, _diagContent, _armyBuyBtn, _armyTrainBtn, _armyArenaBtn, _buyBackBtn, _speedRow, _pagePrev, _pageNext;
        private int _catPage, _catPages = 1;
        private readonly Rectangle[] _diagTabRects = new Rectangle[4];
        private readonly Rectangle[] _cardRects = new Rectangle[UnitCatalog.All.Count];
        private readonly Rectangle[] _cardMinus = new Rectangle[UnitCatalog.All.Count];
        private readonly Rectangle[] _cardPlus = new Rectangle[UnitCatalog.All.Count];
        private readonly Rectangle[] _cardBuy = new Rectangle[UnitCatalog.All.Count];

        /// <summary>JANG MAYDONI tugmasi bosildi (Game1 mashq jangini ochadi).</summary>
        public bool OpenArenaRequested { get; set; }
        /// <summary>DUNYO tugmasi bosildi (Game1 saltanat xaritasini ochadi).</summary>
        public bool OpenWorldRequested { get; set; }

        private static readonly string[] DiagNames = { "SHAHAR", "AHOLI", "QO'SHIN", "DAVLAT" };

        public void OpenDiagnostics(DiagTab tab, bool buy = false)
        {
            _tab = Tab.Diag;
            _diagTab = tab;
            _buyOpen = buy && tab == DiagTab.Army;
        }

        // ------------------------------------------------------------------ joylashuv
        private void LayoutDiagnostics(int vw, int vh)
        {
            int pw = Math.Min(vw - 24, 1240);
            int ph = Math.Min(vh - TopH - BottomH - 20, 640);
            _panelRect = new Rectangle((vw - pw) / 2, TopH + (vh - TopH - BottomH - ph) / 2, pw, ph);
            int bh = Touch ? 36 : 30;
            _diagClose = new Rectangle(_panelRect.Right - 12 - bh, _panelRect.Y + 10, bh, bh);
            int tabsY = _panelRect.Y + 50, gap = 6;
            int tw = (pw - 32 - gap * 3) / 4;
            for (int i = 0; i < 4; i++) _diagTabRects[i] = new Rectangle(_panelRect.X + 16 + i * (tw + gap), tabsY, tw, bh);
            _diagContent = new Rectangle(_panelRect.X + 16, tabsY + bh + 12, pw - 32, _panelRect.Bottom - 12 - (tabsY + bh + 12));

            if (_diagTab == DiagTab.Army)
            {
                var c = _diagContent;
                if (!_buyOpen)
                {
                    int w3 = (c.Width - 2 * 8) / 3;
                    _armyBuyBtn = new Rectangle(c.X, c.Y, w3, bh + 4);
                    _armyTrainBtn = new Rectangle(c.X + w3 + 8, c.Y, w3, bh + 4);
                    _armyArenaBtn = new Rectangle(c.X + 2 * (w3 + 8), c.Y, w3, bh + 4);
                }
                else
                {
                    _buyBackBtn = new Rectangle(c.Right - 150, c.Y, 150, bh);
                    int top = c.Y + bh + 10, g = 8;
                    int cols = Math.Clamp(c.Width / 220, 2, 5);
                    int count = UnitCatalog.All.Count;
                    // Karta matni sig'ishi uchun kerakli balandlik; ekran past bo'lsa (telefon) — sahifalarga bo'linadi
                    int need = Touch ? 234 : 226;
                    int rowsAll = (count + cols - 1) / cols;
                    int rows = Math.Clamp((c.Bottom - top + g) / (need + g), 1, rowsAll);
                    int perPage = cols * rows;
                    _catPages = (count + perPage - 1) / perPage;
                    _catPage = Math.Clamp(_catPage, 0, _catPages - 1);
                    _pagePrev = _pageNext = Rectangle.Empty;
                    if (_catPages > 1)
                    {
                        _pageNext = new Rectangle(_buyBackBtn.X - 10 - bh - 12, c.Y, bh + 12, bh);
                        _pagePrev = new Rectangle(_pageNext.X - 70 - bh - 12, c.Y, bh + 12, bh);
                    }
                    int cw = (c.Width - g * (cols - 1)) / cols;
                    int ch = (c.Bottom - top - g * (rows - 1)) / rows;
                    int sb = Touch ? 28 : 24;
                    for (int i = 0; i < UnitCatalog.All.Count; i++)
                    {
                        int k = i - _catPage * perPage;
                        if (k < 0 || k >= perPage)
                        {
                            _cardRects[i] = _cardMinus[i] = _cardPlus[i] = _cardBuy[i] = Rectangle.Empty;
                            continue;
                        }
                        var r = new Rectangle(c.X + (k % cols) * (cw + g), top + (k / cols) * (ch + g), cw, ch);
                        _cardRects[i] = r;
                        int by = r.Bottom - 8 - sb;
                        _cardMinus[i] = new Rectangle(r.X + 8, by, sb, sb);
                        _cardPlus[i] = new Rectangle(r.X + 8 + sb + 30, by, sb, sb);
                        _cardBuy[i] = new Rectangle(_cardPlus[i].Right + 8, by, r.Right - 8 - (_cardPlus[i].Right + 8), sb);
                    }
                }
            }
            else if (_diagTab == DiagTab.State)
            {
                int tx = _diagContent.X + _diagContent.Width / 2 + 10, ty = _diagContent.Y + 34;
                int sb2 = SmallBtn, step = Touch ? 40 : 32;
                for (int i = 0; i < 3; i++)
                {
                    _taxMinus[i] = new Rectangle(tx + 190, ty + i * step, sb2, sb2);
                    _taxPlus[i] = new Rectangle(tx + 190 + sb2 + 60, ty + i * step, sb2, sb2);
                }
                int sy = ty + 3 * step + 40;
                _speedRow = new Rectangle(tx, sy, 0, 0);
                for (int i = 0; i < 3; i++) _speedBtn[i] = new Rectangle(tx + 190 + i * 56, sy - 4, 50, SmallBtn + 4);
                _petitionViewBtn = new Rectangle(tx, sy + 50, Math.Min(360, _diagContent.Right - tx), Touch ? 36 : 30);
            }
        }

        // ------------------------------------------------------------------ bosishlar
        private bool ClickDiagnostics(Point m)
        {
            if (_diagClose.Contains(m)) { _tab = Tab.None; _buyOpen = false; return true; }
            for (int i = 0; i < 4; i++)
                if (_diagTabRects[i].Contains(m)) { _diagTab = (DiagTab)i; _buyOpen = false; return true; }

            switch (_diagTab)
            {
                case DiagTab.Army when !_buyOpen:
                    if (_armyBuyBtn.Contains(m)) { _buyOpen = true; return true; }
                    if (_armyTrainBtn.Contains(m)) { _state.StartTrainingSession(_buildings); return true; }
                    if (_armyArenaBtn.Contains(m)) { OpenArenaRequested = true; return true; }
                    break;
                case DiagTab.Army:
                    if (_buyBackBtn.Contains(m)) { _buyOpen = false; return true; }
                    if (_pagePrev.Contains(m)) { _catPage = Math.Max(0, _catPage - 1); return true; }
                    if (_pageNext.Contains(m)) { _catPage = Math.Min(_catPages - 1, _catPage + 1); return true; }
                    for (int i = 0; i < UnitCatalog.All.Count; i++)
                    {
                        if (_cardMinus[i].Contains(m)) { _buyQty[i] = Math.Max(1, _buyQty[i] - 1); return true; }
                        if (_cardPlus[i].Contains(m)) { _buyQty[i] = Math.Min(10, _buyQty[i] + 1); return true; }
                        if (_cardBuy[i].Contains(m))
                        {
                            // Ikki marta bosish (yoki sensorli ekranda takroriy tegish) bitta xaridni ikki marta qilmaydi
                            long now = System.Environment.TickCount64;
                            if (now - _lastBuyTick < 400) return true;
                            _lastBuyTick = now;
                            if (_state.RecruitUnits(UnitCatalog.All[i].Type, _buyQty[i], _buildings)) _buyQty[i] = 1;
                            return true;
                        }
                    }
                    break;
                case DiagTab.State:
                    for (int i = 0; i < 3; i++)
                    {
                        if (_taxMinus[i].Contains(m)) { AdjustTax(i, -GovernmentPolicy.Step); return true; }
                        if (_taxPlus[i].Contains(m)) { AdjustTax(i, +GovernmentPolicy.Step); return true; }
                    }
                    for (int i = 0; i < 3; i++)
                        if (_speedBtn[i].Contains(m)) { _state.SetSpeed(i == 0 ? 1 : i == 1 ? 2 : 4); return true; }
                    if (_petitionViewBtn.Contains(m) && _state.Petitions.HasActive) { PetitionOpen = true; return true; }
                    break;
            }
            return _panelRect.Contains(m);
        }

        // ------------------------------------------------------------------ chizish
        private void DrawDiagnostics(SpriteBatch sb)
        {
            Fill(sb, new Rectangle(0, TopH + 2, 4000, _bottomBar.Y - TopH - 4), new Color(0, 0, 0, 90));
            Panel(sb, _panelRect, PanelBg, Gold, 2);
            _font.Draw(sb, "DIAGNOSTIKA", new Vector2(_panelRect.X + 16, _panelRect.Y + 14), Gold, 3, 1);
            string sub = GameConfig.CityName + "  |  " + Texts.Day + " " + _state.Day;
            _font.DrawRight(sb, sub, new Vector2(_diagClose.X - 14, _panelRect.Y + 20), Muted, 2, 1);
            Panel(sb, _diagClose, PanelBg2, Border, 1);
            DrawButtonText(sb, "X", _diagClose, Cream);

            for (int i = 0; i < 4; i++)
            {
                bool on = (int)_diagTab == i;
                Panel(sb, _diagTabRects[i], on ? PanelBg2 : PanelBg, on ? Gold : Border, on ? 2 : 1);
                DrawButtonText(sb, DiagNames[i], _diagTabRects[i], on ? Gold : Cream);
                if (i == (int)DiagTab.State && _state.Petitions.HasActive)
                    Fill(sb, new Rectangle(_diagTabRects[i].Right - 12, _diagTabRects[i].Y + 4, 8, 8), Gold);
            }

            float prevClip = PixelFont.ClipRight;
            PixelFont.ClipRight = _diagContent.Right;
            switch (_diagTab)
            {
                case DiagTab.City: DrawDiagCity(sb); break;
                case DiagTab.Population: DrawDiagPopulation(sb); break;
                case DiagTab.Army: if (_buyOpen) DrawUnitCatalog(sb); else DrawDiagArmy(sb); break;
                default: DrawDiagState(sb); break;
            }
            PixelFont.ClipRight = prevClip;
        }

        /// <summary>Ko'rsatkich kartasi: yuqorida kichik yorliq, pastda katta qiymat.</summary>
        private void Tile(SpriteBatch sb, Rectangle r, string label, string value, Color valueColor)
        {
            Panel(sb, r, PanelBg2, Border, 1);
            _font.Draw(sb, FitText(label, r.Width - 16), new Vector2(r.X + 8, r.Y + 8), Muted, 2, 1);
            int scale = _font.Measure(value, 3, 1).X <= r.Width - 16 ? 3 : 2;
            _font.Draw(sb, FitText(value, r.Width - 16, scale), new Vector2(r.X + 8, r.Bottom - 8 - 7 * scale), valueColor, scale, 1);
        }

        /// <summary>Tiles qatori: n ta karta teng enda. Qaytaradi: keyingi y.</summary>
        private int TileRow(SpriteBatch sb, int y, (string label, string value, Color c)[] items, int cols = 4, int h = 62)
        {
            var c0 = _diagContent;
            int g = 10, w = (c0.Width - g * (cols - 1)) / cols;
            for (int i = 0; i < items.Length; i++)
            {
                int row = i / cols, col = i % cols;
                Tile(sb, new Rectangle(c0.X + col * (w + g), y + row * (h + g), w, h), items[i].label, items[i].value, items[i].c);
            }
            int rows = (items.Length + cols - 1) / cols;
            return y + rows * (h + g);
        }

        /// <summary>0..100 ko'rsatkich chizig'i.</summary>
        private void Meter(SpriteBatch sb, Rectangle r, string label, int value)
        {
            value = Math.Clamp(value, 0, 100);
            _font.Draw(sb, label, new Vector2(r.X, r.Y + 3), Cream, 2, 1);
            var bar = new Rectangle(r.X + 170, r.Y, r.Width - 170 - 60, 20);
            Fill(sb, bar, new Color(20, 16, 12));
            Color c = value >= 66 ? Green : value >= 40 ? Gold : Red;
            Fill(sb, new Rectangle(bar.X + 2, bar.Y + 2, (bar.Width - 4) * value / 100, bar.Height - 4), c);
            _font.DrawRight(sb, value.ToString(), new Vector2(r.Right, r.Y + 3), c, 2, 1);
        }

        private void SectionTitle(SpriteBatch sb, string text, int x, int y) => _font.Draw(sb, text, new Vector2(x, y), Gold, 2, 1);

        private void DrawDiagCity(SpriteBatch sb)
        {
            var c = _diagContent;
            var pop = _state.Pop;
            int def = CityDefenseManager.Compute(_buildings, _state.Military.TotalStrength);
            int y = TileRow(sb, c.Y, new[]
            {
                ("Binolar", _buildings.Buildings.Count.ToString(), Gold),
                ("Aholi", _state.Population.ToString(), Gold),
                ("Uy-joy", pop.HousingUsed + " / " + pop.HousingCapacity, pop.HousingUsed >= pop.HousingCapacity ? Red : Cream),
                ("Mudofaa", def.ToString(), Cream),
                ("Xazina", _state.Coins.ToString(), Gold),
                ("Oziq-ovqat", _state.Food.ToString(), _state.Food < _state.Population ? Red : Cream),
                ("Sana", "Oy " + _state.Clock.Month + ", Yil " + _state.Clock.Year, Cream),
                ("Fasl / havo", SeasonManager.Name(_state.Season) + " / " + _state.Weather.Name(), Cream),
            });
            y += 6;

            // Bino turlari bo'yicha (2 ustun)
            SectionTitle(sb, "BINOLAR TURI BO'YICHA", c.X, y);
            int half = c.Width / 2;
            SectionTitle(sb, "HOLAT VA OGOHLANTIRISHLAR", c.X + half + 10, y);
            y += 26;
            var counts = new Dictionary<BuildingType, int>();
            foreach (Building b in _buildings.Buildings) counts[b.Type] = counts.TryGetValue(b.Type, out int n) ? n + 1 : 1;
            int ly = y, col = 0;
            foreach (CatalogEntry e in BuildingCatalog.Buildable)
            {
                counts.TryGetValue(e.Type, out int n);
                int x = c.X + col * (half / 2);
                _font.Draw(sb, FitText(e.Name + ": " + n, half / 2 - 10), new Vector2(x, ly), n > 0 ? Cream : Muted, 2, 1);
                col++;
                if (col == 2) { col = 0; ly += 21; }
                if (ly > c.Bottom - 20) break;
            }

            // Ogohlantirishlar — haqiqiy ko'rsatkichlardan
            var warn = new List<(string, Color)>();
            if (pop.HousingUsed >= pop.HousingCapacity) warn.Add(("Uy-joy to'la: yangi uy quring", Red));
            if (pop.Unemployed > 0) warn.Add(("Ishsizlar: " + pop.Unemployed + " kishi", Gold));
            if (_state.Food < _state.Population) warn.Add(("Oziq-ovqat kam: ferma quring", Red));
            if (pop.AvgHappiness < 50) warn.Add(("Baxt past: bog' va masjid kerak", Gold));
            if (_state.Petitions.HasActive) warn.Add(("Javob kutayotgan murojaat bor", Gold));
            if (_state.Military.TotalUnits < 5) warn.Add(("Qo'shin kichik: jangchi sotib oling", Gold));
            if (warn.Count == 0) warn.Add(("Hammasi joyida. Shahar barqaror.", Green));
            int wy = y;
            foreach (var (text, color) in warn)
            {
                foreach (string line in Wrap("- " + text, half - 20, 2))
                {
                    if (wy > c.Bottom - 20) break;
                    _font.Draw(sb, line, new Vector2(c.X + half + 10, wy), color, 2, 1);
                    wy += 21;
                }
            }
        }

        private void DrawDiagPopulation(SpriteBatch sb)
        {
            var c = _diagContent;
            var pop = _state.Pop;
            // Ehtiyojlar joriy holatdan (kun oxirini kutmasdan) — panel ochilganda ham to'g'ri ko'rinsin
            pop.ComputeNeeds(_state.Food, _buildings.Count(BuildingType.Tower), _buildings.Count(BuildingType.Barracks),
                             _buildings.Count(BuildingType.Clinic), _buildings.Count(BuildingType.Madrasa));
            int y = TileRow(sb, c.Y, new[]
            {
                ("Jami aholi", pop.Count.ToString(), Gold),
                ("Oilalar", pop.Families.ToString(), Cream),
                ("Ishchilar", pop.Workers.ToString(), Cream),
                ("Ishsizlar", pop.Unemployed.ToString(), pop.Unemployed > 0 ? Red : Green),
                ("Uy-joy", pop.HousingUsed + " / " + pop.HousingCapacity, Cream),
                ("Bandlik", Employment() + "%", Employment() >= 70 ? Green : Gold),
                ("Fasl / havo", SeasonManager.Name(_state.Season) + " / " + _state.Weather.Name(), Cream),
                ("Havo hissi", SeasonVisuals.ComfortName(SeasonVisuals.ComfortOf(_state.Season, _state.Weather.Current, _state.Clock.DayFraction)),
                    SeasonVisuals.ComfortOf(_state.Season, _state.Weather.Current, _state.Clock.DayFraction) == Comfort.Qulay ? Green : Red),
            });
            y += 8;
            int half = c.Width / 2;
            int lw = Math.Min(half - 20, 560);
            SectionTitle(sb, "EHTIYOJLAR VA TURMUSH", c.X, y);
            SectionTitle(sb, "KASBLAR", c.X + half + 10, y);
            int ly = y + 28;
            (string Name, float V)[] meters =
            {
                ("Oziq-ovqat", pop.NeedFood), ("Uy-joy", pop.NeedHousing), ("Xavfsizlik", pop.NeedSecurity),
                ("Savdo", pop.NeedTrade), ("Ta'lim", pop.NeedEducation), ("Tibbiyot", pop.NeedHealth),
                ("Baxt", pop.AvgHappiness), ("Sog'liq", pop.AvgHealth)
            };
            foreach (var m in meters)
            {
                if (ly > c.Bottom - 24) break;
                Meter(sb, new Rectangle(c.X, ly, lw, 20), m.Name, (int)m.V);
                ly += 27;
            }

            // Kasblar guruhlar bo'yicha (ish o'rni bor yoki band bo'lganlari yorqin)
            int rx = c.X + half + 10, colW = Math.Max(120, (c.Right - rx) / 2), rowY = y + 28, col = 0;
            string lastGroup = null;
            foreach (CitizenProfession p in ProfessionDisplayOrder)
            {
                string g = PopulationManager.ProfessionGroup(p);
                if (g != lastGroup)
                {
                    if (col != 0) { rowY += 19; col = 0; }
                    if (rowY > c.Bottom - 18) break;
                    _font.Draw(sb, g.ToUpperInvariant(), new Vector2(rx, rowY), Gold, 2, 1);
                    rowY += 19;
                    lastGroup = g;
                }
                if (rowY > c.Bottom - 18) break;
                int n = pop.CountProfession(p);
                int x = rx + col * colW;
                _font.Draw(sb, FitText(PopulationManager.ProfessionName(p), colW - 44), new Vector2(x, rowY), n > 0 ? Cream : Muted, 2, 1);
                _font.DrawRight(sb, n.ToString(), new Vector2(x + colW - 14, rowY), n > 0 ? Gold : Muted, 2, 1);
                if (++col == 2) { col = 0; rowY += 19; }
            }
        }

        /// <summary>Aholi panelida kasblar tartibi (guruhlar ketma-ket).</summary>
        private static readonly CitizenProfession[] ProfessionDisplayOrder =
        {
            CitizenProfession.Dehqon, CitizenProfession.Bogbon, CitizenProfession.Chorvador, CitizenProfession.Baliqchi,
            CitizenProfession.Hunarmand, CitizenProfession.Quruvchi, CitizenProfession.Konchi,
            CitizenProfession.Savdogar, CitizenProfession.Tashuvchi, CitizenProfession.Omborchi, CitizenProfession.SoliqYiguvchi, CitizenProfession.BozorEgasi,
            CitizenProfession.Muallim, CitizenProfession.Olim, CitizenProfession.Tabib, CitizenProfession.Memor, CitizenProfession.Amaldor,
            CitizenProfession.Soqchi, CitizenProfession.None
        };

        private void DrawDiagArmy(SpriteBatch sb)
        {
            var c = _diagContent;
            ArmyManager mil = _state.Military;

            Panel(sb, _armyBuyBtn, new Color(70, 54, 24, 245), Gold, 2);
            DrawButtonText(sb, "SOTIB OLISH (" + UnitCatalog.All.Count + " TUR)", _armyBuyBtn, Gold);
            bool training = _state.TrainingActive;
            Panel(sb, _armyTrainBtn, PanelBg2, training ? Green : Border, 1);
            DrawButtonText(sb, training ? "MASHG'ULOT: " + (int)(_state.TrainingProgress * 100) + "%" : "MASHG'ULOT MAYDONI", _armyTrainBtn, training ? Green : Cream);
            Panel(sb, _armyArenaBtn, PanelBg2, Red, 1);
            DrawButtonText(sb, "JANG MAYDONI", _armyArenaBtn, Cream);

            int y = _armyBuyBtn.Bottom + 6;
            if (training)
            {
                var bar = new Rectangle(_armyTrainBtn.X, y, _armyTrainBtn.Width, 6);
                Fill(sb, bar, new Color(20, 16, 12));
                Fill(sb, new Rectangle(bar.X, bar.Y, (int)(bar.Width * _state.TrainingProgress), bar.Height), Green);
            }
            else
                _font.DrawCentered(sb, FitText("Boshlash: " + _state.TrainingCost + " tanga", _armyTrainBtn.Width), new Vector2(_armyTrainBtn.Center.X, y + 7), Muted, 2, 1);
            y += 22;

            int def = CityDefenseManager.Compute(_buildings, mil.TotalStrength);
            y = TileRow(sb, y, new[]
            {
                ("Jami jangchi", mil.TotalUnits.ToString(), Gold),
                ("Qo'shin kuchi", mil.TotalStrength.ToString(), Gold),
                ("Kunlik xarajat", mil.TotalUpkeep + " tanga", Cream),
                ("Navbatda", mil.QueueCount.ToString(), Cream),
                ("Shahar mudofaasi", def.ToString(), Cream),
                ("Janglar", _state.World.Battles.History.Count.ToString(), Cream),
            }, 3, 56);
            y += 6;
            SectionTitle(sb, "QO'SHIN TARKIBI", c.X, y);
            _font.DrawRight(sb, "Navbat: " + mil.QueueCount + " / " + GameStateManager.MaxRecruitQueue, new Vector2(c.Right, y), Muted, 2, 1);
            y += 26;
            int colW = c.Width / 2;
            for (int i = 0; i < UnitCatalog.All.Count; i++)
            {
                UnitDef d = UnitCatalog.All[i];
                int x = c.X + (i % 2) * colW, ry = y + (i / 2) * 22;
                if (ry > c.Bottom - 18) break;
                int n = mil.Count(d.Type);
                int queued = 0;
                foreach (RecruitOrder o in mil.Queue) if (o.Type == d.Type) queued++;
                _font.Draw(sb, d.NameUz, new Vector2(x, ry), n > 0 ? Cream : Muted, 2, 1);
                _font.DrawRight(sb, n + (queued > 0 ? "  (+" + queued + ")" : ""), new Vector2(x + colW - 24, ry), n > 0 ? Gold : Muted, 2, 1);
            }
        }

        /// <summary>10 xil jangchi kartalari: belgi, nom, tur, kuch, narx, ta'minot, mavjudligi, soni va SOTIB OLISH.</summary>
        private void DrawUnitCatalog(SpriteBatch sb)
        {
            var c = _diagContent;
            _font.Draw(sb, "JANGCHILAR KATALOGI", new Vector2(c.X, c.Y + 6), Gold, 2, 1);
            _font.Draw(sb, "Xazina: " + _state.Coins, new Vector2(c.X + 300, c.Y + 6), Cream, 2, 1);
            Panel(sb, _buyBackBtn, PanelBg2, Border, 1);
            DrawButtonText(sb, "ORQAGA", _buyBackBtn, Cream);
            if (_catPages > 1)
            {
                Panel(sb, _pagePrev, PanelBg2, _catPage > 0 ? Gold : Border, 1);
                DrawButtonText(sb, "<", _pagePrev, _catPage > 0 ? Gold : Muted);
                _font.DrawCentered(sb, (_catPage + 1) + " / " + _catPages, new Vector2((_pagePrev.Right + _pageNext.X) / 2f, _pagePrev.Center.Y), Cream, 2, 1);
                Panel(sb, _pageNext, PanelBg2, _catPage < _catPages - 1 ? Gold : Border, 1);
                DrawButtonText(sb, ">", _pageNext, _catPage < _catPages - 1 ? Gold : Muted);
            }
            ArmyManager mil = _state.Military;

            for (int i = 0; i < UnitCatalog.All.Count; i++)
            {
                UnitDef d = UnitCatalog.All[i];
                Rectangle r = _cardRects[i];
                if (r == Rectangle.Empty) continue;
                int researched = _state.Civ.Player.Researched.Count;
                bool unlocked = GameStateManager.UnitRequirementMet(d, _buildings, researched);
                int total = d.TrainingCost * _buyQty[i];
                bool afford = _state.Coins >= total;
                Panel(sb, r, unlocked ? PanelBg2 : new Color(26, 24, 30, 245), unlocked ? Border : new Color(80, 70, 70), 1);
                PixelFont.ClipRight = r.Right - 6;

                // Belgi (jangchi spraytining birinchi kadri)
                var icon = new Rectangle(r.X + 6, r.Y + 6, 44, 44);
                Fill(sb, icon, new Color(18, 15, 12));
                _assets.EnsureFamily(d.IconKey);   // yangi birlik ko'rinishi birinchi ochilganda yasaladi
                Texture2D tex = _assets.TryGet(d.IconKey + "_0");
                if (tex != null) DrawFit(sb, tex, new Rectangle(icon.X + 2, icon.Y + 2, icon.Width - 4, icon.Height - 4));
                _font.Draw(sb, UnitCatalog.RoleName(d.Role), new Vector2(icon.Right + 8, r.Y + 10), Muted, 2, 1);
                _font.Draw(sb, "Sizda: " + mil.Count(d.Type), new Vector2(icon.Right + 8, r.Y + 30), Cream, 2, 1);

                int y = icon.Bottom + 6;
                var nameLines = Wrap(d.NameUz.ToUpperInvariant(), r.Width - 14, 2);
                for (int k = 0; k < nameLines.Count && k < 2; k++) { _font.Draw(sb, nameLines[k], new Vector2(r.X + 7, y), unlocked ? Gold : Muted, 2, 1); y += 18; }
                if (nameLines.Count < 2) y += 18;
                y += 2;
                Color tc = unlocked ? Cream : Muted;
                _font.Draw(sb, "Kuch: " + d.Power + " (" + d.Attack + "/" + d.Defense + ")", new Vector2(r.X + 7, y), tc, 2, 1); y += 19;
                _font.Draw(sb, "HP " + d.Health + (d.Armor > 0 ? "  Zirh " + d.Armor + "%" : ""), new Vector2(r.X + 7, y), tc, 2, 1); y += 19;
                _font.Draw(sb, "Narx: " + d.TrainingCost + (_buyQty[i] > 1 ? " x" + _buyQty[i] + " = " + total : ""), new Vector2(r.X + 7, y), unlocked ? (afford ? Gold : Red) : Muted, 2, 1); y += 19;
                _font.Draw(sb, "Tez " + d.Speed + "  Maosh " + d.Upkeep + "/kun", new Vector2(r.X + 7, y), tc, 2, 1); y += 19;
                if (!unlocked)
                    _font.Draw(sb, "QULF: " + UnitCatalog.LockText(d, researched), new Vector2(r.X + 7, y), Red, 2, 1);
                else
                    _font.Draw(sb, d.TrainingDays + " soatda tayyor", new Vector2(r.X + 7, y), Green, 2, 1);

                // Soni va SOTIB OLISH
                Panel(sb, _cardMinus[i], PanelBg, Border, 1);
                _font.DrawCentered(sb, "-", _cardMinus[i].Center.ToVector2(), Cream, 2, 1);
                _font.DrawCentered(sb, _buyQty[i].ToString(), new Vector2((_cardMinus[i].Right + _cardPlus[i].X) / 2f, _cardMinus[i].Center.Y), Gold, 2, 1);
                Panel(sb, _cardPlus[i], PanelBg, Border, 1);
                _font.DrawCentered(sb, "+", _cardPlus[i].Center.ToVector2(), Cream, 2, 1);
                bool can = unlocked && afford;
                Panel(sb, _cardBuy[i], can ? new Color(70, 54, 24, 245) : PanelBg, can ? Gold : new Color(90, 80, 70), 1);
                PixelFont.ClipRight = _diagContent.Right;
                string bl = _font.Measure("SOTIB OLISH", 2, 1).X <= _cardBuy[i].Width - 8 ? "SOTIB OLISH" : "OLISH";
                _font.DrawCentered(sb, bl, _cardBuy[i].Center.ToVector2(), can ? Gold : Muted, 2, 1);
            }
        }

        private void DrawDiagState(SpriteBatch sb)
        {
            var c = _diagContent;
            var econ = _state.Economy;
            int x = c.X, y = c.Y;
            SectionTitle(sb, "IQTISOD", x, y); y += 28;
            string[] left =
            {
                "Xazina: " + _state.Coins + " tanga",
                "Kunlik daromad: +" + econ.LastIncome,
                "Kunlik xarajat: -" + econ.LastExpense,
                "Ishlab chiqarish: " + econ.LastProduction,
                "Oziq-ovqat: +" + econ.LastFoodProduced + " / -" + econ.LastFoodConsumed,
                "Bandlik: " + Employment() + "%",
                "Qo'shin ta'minoti: -" + _state.Military.TotalUpkeep + "/kun",
            };
            float prev = PixelFont.ClipRight;
            PixelFont.ClipRight = c.X + c.Width / 2 - 10;
            foreach (string l in left) { _font.Draw(sb, l, new Vector2(x, y), Cream, 2, 1); y += 24; }
            PixelFont.ClipRight = prev;

            int tx = c.X + c.Width / 2 + 10;
            SectionTitle(sb, "SOLIQLAR (%)", tx, c.Y);
            string[] taxNames = { "Aholi", "Savdo", "Ishlab chiq." };
            int[] taxVals = { _state.Policy.PopulationTax, _state.Policy.TradeTax, _state.Policy.ProductionTax };
            for (int i = 0; i < 3; i++)
            {
                _font.Draw(sb, taxNames[i], new Vector2(tx, _taxMinus[i].Y + (_taxMinus[i].Height - 14) / 2), Cream, 2, 1);
                Panel(sb, _taxMinus[i], PanelBg2, Border, 1);
                _font.DrawCentered(sb, "-", _taxMinus[i].Center.ToVector2(), Cream, 2, 1);
                // Aholi solig'i g'alayon chegarasida (30% dan oshsa) — qizil: xalq saroy oldiga chiqadi
                bool riot = i == 0 && AndijonSaltanatlar.CityLife.TaxRiotSystem.TaxTooHigh(taxVals[i]);
                _font.DrawCentered(sb, taxVals[i] + "%", new Vector2((_taxMinus[i].Right + _taxPlus[i].X) / 2f, _taxMinus[i].Center.Y), riot ? Red : Gold, 2, 1);
                Panel(sb, _taxPlus[i], PanelBg2, Border, 1);
                _font.DrawCentered(sb, "+", _taxPlus[i].Center.ToVector2(), Cream, 2, 1);
            }

            _font.Draw(sb, "O'yin tezligi", new Vector2(tx, _speedRow.Y), Cream, 2, 1);
            int[] speeds = { 1, 2, 4 };
            for (int i = 0; i < 3; i++)
            {
                bool on = _state.Speed == speeds[i];
                Panel(sb, _speedBtn[i], on ? PanelBg2 : PanelBg, on ? Gold : Border, on ? 2 : 1);
                _font.DrawCentered(sb, speeds[i] + "x", _speedBtn[i].Center.ToVector2(), on ? Gold : Cream, 2, 1);
            }

            bool has = _state.Petitions.HasActive;
            Panel(sb, _petitionViewBtn, PanelBg2, has ? Gold : Border, 1);
            DrawButtonText(sb, has ? "MUROJAATNI KO'RISH (!)" : "MUROJAAT YO'Q", _petitionViewBtn, has ? Gold : Muted);
        }
    }
}
