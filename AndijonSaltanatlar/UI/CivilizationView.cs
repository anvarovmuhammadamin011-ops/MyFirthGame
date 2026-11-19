using System.Collections.Generic;
using AndijonSaltanatlar.Civilization;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.History;
using AndijonSaltanatlar.Progression;
using AndijonSaltanatlar.Technology;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.UI
{
    /// <summary>
    /// SIVILIZATSIYA ekrani: hukmdor boshqaruv paneli (ilm/madaniyat/davr/joriy tadqiqot),
    /// TEXNOLOGIYALAR daraxti (kartalar: ochilmagan/tayyor/jarayonda/kashf etilgan) va TARIX XAZINASI.
    /// Shahar ekranidan C tugmasi yoki SIVILIZATSIYA bo'limi orqali ochiladi.
    /// </summary>
    public sealed class CivilizationView
    {
        private static readonly Color Bg = new Color(16, 20, 34);
        private static readonly Color PanelBg = new Color(28, 26, 40, 240);
        private static readonly Color Border = new Color(150, 116, 66);
        private static readonly Color Gold = new Color(235, 200, 120);
        private static readonly Color Cream = new Color(232, 224, 206);
        private static readonly Color Muted = new Color(168, 160, 150);
        private static readonly Color Teal = new Color(90, 200, 190);
        private static readonly Color Locked = new Color(60, 58, 70);
        private static readonly Color Green = new Color(120, 214, 132);

        private readonly AssetManager _assets;
        private readonly PixelFont _font;
        private readonly GameStateManager _state;
        private readonly Texture2D _px;

        public bool BackRequested;
        private bool _showHistory;
        /// <summary>Tarix xazinasi bo'limini ochish (sinov: --civ --tab history).</summary>
        public void ShowHistory() => _showHistory = true;

        private Rectangle _backBtn, _techTabBtn, _histTabBtn;
        private readonly List<(TechnologyDefinition tech, Rectangle rect)> _cards = new List<(TechnologyDefinition, Rectangle)>();

        private CivilizationState Civ => _state.Civ.Player;

        public CivilizationView(AssetManager assets, PixelFont font, GameStateManager state)
        {
            _assets = assets; _font = font; _state = state; _px = assets.Pixel;
        }

        private void Layout(int vw, int vh)
        {
            _backBtn = new Rectangle(vw - 226, 14, 210, 30);
            _techTabBtn = new Rectangle(360, 54, 190, 26);
            _histTabBtn = new Rectangle(556, 54, 190, 26);

            _cards.Clear();
            if (!_showHistory)
            {
                // Kartalar ekran eniga moslashadi (nomlari karta ichida qoladi).
                // Ustunlar soni ekran eniga, karta balandligi ekran bo'yiga moslashadi (telefonda ham hammasi ko'rinadi)
                int gx = 360, gy = 92, px = 12, py = 8;
                int cols = System.Math.Clamp((vw - gx - 16 + px) / (170 + px), 1, 4);
                int rows = (TechnologyCatalog.All.Count + cols - 1) / cols;
                int ch = System.Math.Clamp((vh - gy - 12 - (rows - 1) * py) / System.Math.Max(1, rows), 50, 66);
                int cw = System.Math.Max(160, System.Math.Min(270, (vw - gx - 16 - (cols - 1) * px) / cols));
                for (int i = 0; i < TechnologyCatalog.All.Count; i++)
                {
                    int col = i % cols, row = i / cols;
                    _cards.Add((TechnologyCatalog.All[i], new Rectangle(gx + col * (cw + px), gy + row * (ch + py), cw, ch)));
                }
            }
        }

        public void Update(InputManager input, int vw, int vh)
        {
            Layout(vw, vh);
            input.MouseOverUI = true;
            if (!input.LeftPressed) return;
            Point m = input.MousePosition;

            if (_backBtn.Contains(m)) { BackRequested = true; return; }
            if (_techTabBtn.Contains(m)) { _showHistory = false; return; }
            if (_histTabBtn.Contains(m)) { _showHistory = true; return; }

            if (!_showHistory)
                foreach (var (tech, rect) in _cards)
                    if (rect.Contains(m) && Civ.CanResearch(tech.Id)) { _state.StartResearch(tech.Id); return; }
        }

        public void Draw(SpriteBatch sb, int vw, int vh)
        {
            Layout(vw, vh);
            Fill(sb, new Rectangle(0, 0, vw, vh), Bg);
            _font.Draw(sb, "TARAQQIYOT", new Vector2(16, 14), Gold, 3, 1);
            Panel(sb, _backBtn, PanelBg, Border, 1);
            _font.DrawCentered(sb, FitPx("SHAHARGA QAYTISH", _backBtn.Width - 10), _backBtn.Center.ToVector2(), Cream, 2, 1);

            DrawDashboard(sb, vh);

            Panel(sb, _techTabBtn, _showHistory ? PanelBg : new Color(50, 46, 64, 245), _showHistory ? Border : Gold, _showHistory ? 1 : 2);
            _font.DrawCentered(sb, "TEXNOLOGIYALAR", _techTabBtn.Center.ToVector2(), _showHistory ? Cream : Gold, 2, 1);
            Panel(sb, _histTabBtn, _showHistory ? new Color(50, 46, 64, 245) : PanelBg, _showHistory ? Gold : Border, _showHistory ? 2 : 1);
            _font.DrawCentered(sb, "TARIX XAZINASI", _histTabBtn.Center.ToVector2(), _showHistory ? Gold : Cream, 2, 1);

            if (_showHistory) DrawHistory(sb, vw, vh);
            else DrawTechTree(sb);
        }

        private void DrawDashboard(SpriteBatch sb, int vh)
        {
            var box = new Rectangle(16, 54, 330, vh - 70);
            Panel(sb, box, PanelBg, Border, 2);
            int x = box.X + 14, y = box.Y + 14;
            _font.Draw(sb, "HUKMDOR PANELI", new Vector2(x, y), Gold, 3, 1); y += 30;

            string current = Civ.IsResearching ? (TechnologyCatalog.Get(Civ.CurrentResearch)?.NameUz ?? "-") : "Yo'q";
            string[] lines =
            {
                "Davr: " + EraManager.EraName(Civ.Researched.Count),
                "Ilm ballari: " + Civ.Science,
                "Kunlik ilm: " + _state.Civ.LastSciencePerDay,
                "Madaniyat: " + Civ.Culture + " (+" + _state.Civ.LastCulturePerDay + "/kun)",
                "Texnologiyalar: " + Civ.Researched.Count + " / " + TechnologyCatalog.All.Count,
                "",
                "Hozirgi tadqiqot:",
                "  " + current,
            };
            foreach (string l in lines)
                foreach (string w in l.Length == 0 ? new List<string> { "" } : Wrap(l, box.Width - 28, 2)) { _font.Draw(sb, w, new Vector2(x, y), Cream, 2, 1); y += 21; }

            if (Civ.IsResearching)
            {
                TechnologyDefinition t = TechnologyCatalog.Get(Civ.CurrentResearch);
                int cost = t?.ResearchCost ?? 1;
                float p = MathClamp((float)Civ.CurrentProgress / cost, 0f, 1f);
                var bar = new Rectangle(x, y + 4, box.Width - 36, 16);
                Fill(sb, bar, Locked);
                Fill(sb, new Rectangle(bar.X, bar.Y, (int)(bar.Width * p), bar.Height), Teal);
                _font.DrawCentered(sb, Civ.CurrentProgress + " / " + cost, bar.Center.ToVector2(), new Color(20, 24, 30), 2, 1);
                y += 28;
            }

            y += 10;
            foreach (string w in Wrap("Texnologiya yangi binolarni va iqtisodiy bonuslarni ochadi.", box.Width - 28, 2))
            { _font.Draw(sb, w, new Vector2(x, y), Muted, 2, 1); y += 21; }
        }

        private void DrawTechTree(SpriteBatch sb)
        {
            foreach (var (t, rect) in _cards)
            {
                bool done = Civ.IsResearched(t.Id);
                bool researching = Civ.CurrentResearch == t.Id;
                bool avail = Civ.CanResearch(t.Id);

                Color border = done ? Teal : researching ? Gold : avail ? Gold : Locked;
                Color bg = done ? new Color(30, 60, 58, 245) : avail || researching ? new Color(46, 42, 60, 245) : new Color(26, 26, 34, 245);
                Panel(sb, rect, bg, border, done || researching || avail ? 2 : 1);

                Color nameCol = done ? Teal : avail || researching ? Cream : Muted;
                _font.Draw(sb, FitPx(t.NameUz, rect.Width - 14), new Vector2(rect.X + 8, rect.Y + 7), nameCol, 2, 1);
                _font.Draw(sb, FitPx(t.Category, rect.Width - 14), new Vector2(rect.X + 8, rect.Y + 25), Muted, 2, 1);

                string status = done ? "KASHF ETILGAN" : researching ? "TADQIQOTDA (" + Civ.CurrentProgress + "/" + t.ResearchCost + ")"
                                : avail ? "Ilm: " + t.ResearchCost : "QULFLANGAN";
                Color stCol = done ? Teal : researching ? Gold : avail ? Gold : Muted;
                _font.Draw(sb, FitPx(status, rect.Width - 14), new Vector2(rect.X + 8, rect.Y + 44), stCol, 2, 1);
            }
        }

        private void DrawHistory(SpriteBatch sb, int vw, int vh)
        {
            var box = new Rectangle(360, 90, vw - 360 - 16, vh - 90 - 16);
            Panel(sb, box, PanelBg, Border, 2);
            int x = box.X + 16, y = box.Y + 14;
            foreach (HistoryEntry e in HistoryRepository.Entries)
            {
                _font.Draw(sb, e.TitleUz + "  (" + e.HistoricalPeriod + ")", new Vector2(x, y), Gold, 2, 1);
                Color sc = e.Status == VerificationStatus.Tasdiqlangan ? Green : e.Status == VerificationStatus.Tekshirilmagan ? Muted : Teal;
                _font.DrawRight(sb, VerificationStatusText.Name(e.Status), new Vector2(box.Right - 16, y), sc, 2, 1);
                y += 21;
                foreach (string line in Wrap(e.DescriptionUz, box.Width - 32, 2)) { _font.Draw(sb, line, new Vector2(x, y), Cream, 2, 1); y += 21; }
                if (e.SourceReferences.Count > 0) { _font.Draw(sb, "Manba: " + string.Join(", ", e.SourceReferences), new Vector2(x, y), Muted, 2, 1); y += 21; }
                y += 10;
            }
        }

        // helpers
        private void Fill(SpriteBatch sb, Rectangle r, Color c) => sb.Draw(_px, r, c);
        private void Panel(SpriteBatch sb, Rectangle r, Color bg, Color b, int t)
        {
            Fill(sb, r, bg);
            Fill(sb, new Rectangle(r.X, r.Y, r.Width, t), b);
            Fill(sb, new Rectangle(r.X, r.Bottom - t, r.Width, t), b);
            Fill(sb, new Rectangle(r.X, r.Y, t, r.Height), b);
            Fill(sb, new Rectangle(r.Right - t, r.Y, t, r.Height), b);
        }
        private static float MathClamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        private static string Fit(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        /// <summary>Matnni piksel kengligiga sig'diradi (sig'masa ".." bilan qisqartiriladi).</summary>
        private string FitPx(string text, int maxPx)
        {
            text = text ?? "";
            if (_font.Measure(text, 2, 1).X <= maxPx) return text;
            while (text.Length > 1 && _font.Measure(text + "..", 2, 1).X > maxPx) text = text.Substring(0, text.Length - 1);
            return text.TrimEnd() + "..";
        }

        private List<string> Wrap(string text, int maxPx, int scale)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            string[] words = text.Split(' ');
            string cur = "";
            foreach (string w in words)
            {
                string test = cur.Length == 0 ? w : cur + " " + w;
                if (_font.Measure(test, scale, 1).X > maxPx && cur.Length > 0) { lines.Add(cur); cur = w; }
                else cur = test;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }
    }
}
