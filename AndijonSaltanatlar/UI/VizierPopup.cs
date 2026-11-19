using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AndijonSaltanatlar.UI
{
    /// <summary>
    /// DONISHMAND VAZIR ekran xabari — missiya/vazifa yoki muhim voqea yuz berganda Vazir pastki chap
    /// burchakda chiqib, chiroyli so'z pufagida xabar aytadi. Bir necha soniyadan keyin o'zi yo'qoladi;
    /// Probel/Enter/Esc bilan tezroq yopiladi. O'yin ustidan o'tmaydi (bosishlar orqadagi shaharga o'tadi).
    /// </summary>
    public sealed class VizierPopup
    {
        private readonly AssetManager _assets;
        private readonly PixelFont _font;

        private readonly Queue<(string Title, string Body)> _queue = new();
        private string _title, _body;
        private List<string> _lines = new();
        private float _age;       // joriy xabar yoshi (soniya)
        private float _hold;      // ko'rsatish davomiyligi
        private bool _active;

        public VizierPopup(AssetManager assets, PixelFont font) { _assets = assets; _font = font; }

        public bool Active => _active;

        /// <summary>Nuqta xabar kartasi ustidami (bosish orqadagi shaharga o'tmasin).</summary>
        public bool Covers(Point p) => _active && !Suspended && _card.Contains(p);

        public void Enqueue(string title, string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return;
            _queue.Enqueue((string.IsNullOrWhiteSpace(title) ? "Donishmand Vazir" : title, body));
        }

        public void Clear() { _queue.Clear(); _active = false; }

        /// <summary>Telefon (sensorli ekran): ixcham karta tepada, matn kattaroq, teginish bilan yopiladi.</summary>
        public bool Touch { get; set; }
        /// <summary>Panel/dialog ochiq — xabar kutib turadi (ustiga chiqmaydi, vaqti o'tmaydi).</summary>
        public bool Suspended { get; set; }
        private Rectangle _card;
        private int BodyScale => Touch ? 2 : 1;
        private int PortraitW => Touch ? 70 : 112;

        private void Begin((string Title, string Body) m, int vw)
        {
            _title = m.Title; _body = m.Body;
            int textW = Math.Min(vw - 24, Touch ? 620 : 540) - PortraitW - 38;
            _lines = Wrap(_body, Math.Max(80, textW), BodyScale);
            _age = 0f;
            _hold = Math.Clamp(3.2f + _body.Length * 0.045f, 4f, 9f);
            _active = true;
        }

        public void Update(float dt, InputManager input, int vw, int vh)
        {
            if (!_active)
            {
                if (_queue.Count > 0) Begin(_queue.Dequeue(), vw);
                else return;
            }
            if (Suspended) return;
            _age += dt;
            bool tapped = input != null && input.LeftPressed && _card.Contains(input.MousePosition);
            if (tapped) input.MouseOverUI = true; // teginish orqadagi shaharga o'tmasin
            bool skip = input != null && (input.KeyPressed(Keys.Space) || input.KeyPressed(Keys.Enter) || input.KeyPressed(Keys.Escape) || tapped);
            if (skip && _age > 0.3f) _age = _hold + 1f;
            if (_age >= _hold)
            {
                if (_queue.Count > 0) Begin(_queue.Dequeue(), vw);
                else _active = false;
            }
        }

        /// <summary>Pastdan bo'sh qoldiriladigan joy (pastki menyu ustida chiqishi uchun).</summary>
        public int BottomInset { get; set; }

        public void Draw(SpriteBatch sb, int vw, int vh)
        {
            if (!_active || _font == null || Suspended) return;
            Texture2D px = _assets.Pixel;

            int cardW = Math.Min(vw - 24, Touch ? 620 : 540);
            int lineH = (int)_font.Measure("A", BodyScale, 1).Y + 4;
            int bodyH = Math.Max(1, _lines.Count) * lineH;
            int cardH = Math.Max(Touch ? 96 : 120, 44 + bodyH + (Touch ? 22 : 14));
            int x = Touch ? (vw - cardW) / 2 : 14;

            // Kirish/chiqish animatsiyasi (pastdan suriladi + so'niydi)
            float tIn = Math.Clamp(_age / 0.35f, 0f, 1f);
            float tOut = Math.Clamp((_hold - _age) / 0.35f, 0f, 1f);
            float app = Math.Min(tIn, tOut);                 // 0..1
            float ease = 1f - (1f - app) * (1f - app);
            // Telefonda tepada (yuqori panel ostida) — pastdagi menyu va panellarni yopmaydi
            int yTarget = Touch ? 50 : vh - cardH - 14 - BottomInset;
            int y = Touch ? (int)(yTarget - (1f - ease) * (cardH + 60)) : (int)(yTarget + (1f - ease) * (cardH + 24));
            float alpha = ease;

            var card = new Rectangle(x, y, cardW, cardH);
            _card = card;
            // Soya + fon + ramka
            sb.Draw(px, new Rectangle(card.X + 4, card.Y + 5, card.Width, card.Height), new Color(0, 0, 0, (int)(90 * alpha)));
            Fill(sb, px, card, new Color(38, 28, 18) * alpha);
            Fill(sb, px, new Rectangle(card.X, card.Y, card.Width, card.Height), Color.Transparent);
            Frame(sb, px, card, new Color(46, 34, 22), new Color(214, 170, 86), alpha);

            // Portret paneli (chapda)
            var port = new Rectangle(card.X + 10, card.Y + 10, PortraitW, card.Height - 20);
            Fill(sb, px, port, new Color(60, 46, 30) * alpha);
            Frame(sb, px, port, Color.Transparent, new Color(176, 134, 70), alpha);
            DrawVizier(sb, px, port, alpha);

            // Matn
            int tx = port.Right + 14;
            int tw = card.Right - 12 - tx;
            _font.Draw(sb, _title, new Vector2(tx, card.Y + 12), new Color(246, 206, 110) * alpha, 2, 1);
            int by = card.Y + 12 + (int)_font.Measure(_title, 2, 1).Y + 8;
            foreach (string ln in _lines)
            {
                _font.Draw(sb, ln, new Vector2(tx, by), new Color(238, 230, 210) * alpha, BodyScale, 1);
                by += lineH;
            }
            // Pastki maslahat
            _font.Draw(sb, Touch ? "Teging — yopish" : "Probel — davom etish", new Vector2(tx, card.Bottom - 16), new Color(190, 170, 140) * (alpha * 0.8f), 1, 1);
        }

        /// <summary>Kod bilan chizilgan Vazir: salla, yuz, soqol, yashil to'n.</summary>
        private static void DrawVizier(SpriteBatch sb, Texture2D px, Rectangle r, float a)
        {
            int cx = r.Center.X;
            int top = r.Y + 8;
            // To'n (yelka-ko'krak)
            var robe = new Rectangle(cx - 34, r.Bottom - 40, 68, 40);
            Fill(sb, px, robe, new Color(46, 104, 72) * a);
            Fill(sb, px, new Rectangle(cx - 6, r.Bottom - 40, 12, 40), new Color(214, 178, 96) * a); // o'rta chok (oltin)
            // Soqol
            Fill(sb, px, new Rectangle(cx - 18, top + 30, 36, 26), new Color(222, 224, 228) * a);
            // Yuz
            Fill(sb, px, new Rectangle(cx - 16, top + 18, 32, 22), new Color(228, 190, 150) * a);
            // Ko'zlar
            Fill(sb, px, new Rectangle(cx - 9, top + 26, 3, 3), new Color(40, 30, 24) * a);
            Fill(sb, px, new Rectangle(cx + 6, top + 26, 3, 3), new Color(40, 30, 24) * a);
            // Salla (oq, keng)
            Fill(sb, px, new Rectangle(cx - 22, top + 2, 44, 18), new Color(242, 242, 236) * a);
            Fill(sb, px, new Rectangle(cx - 24, top + 10, 48, 10), new Color(232, 232, 224) * a);
            // Salla jigasi (oltin + yashil tosh)
            Fill(sb, px, new Rectangle(cx - 3, top - 4, 6, 10), new Color(220, 182, 92) * a);
            Fill(sb, px, new Rectangle(cx - 2, top - 6, 4, 4), new Color(90, 200, 140) * a);
        }

        private List<string> Wrap(string text, int maxW, int scale)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            foreach (string para in text.Split('\n'))
            {
                string[] words = para.Split(' ');
                string cur = "";
                foreach (string w in words)
                {
                    string t = cur.Length == 0 ? w : cur + " " + w;
                    if (_font.Measure(t, scale, 1).X > maxW && cur.Length > 0) { lines.Add(cur); cur = w; }
                    else cur = t;
                }
                if (cur.Length > 0) lines.Add(cur);
                if (lines.Count >= 6) break;
            }
            return lines;
        }

        private static void Fill(SpriteBatch sb, Texture2D px, Rectangle r, Color c) => sb.Draw(px, r, c);

        private static void Frame(SpriteBatch sb, Texture2D px, Rectangle r, Color fill, Color border, float a)
        {
            if (fill.A > 0) sb.Draw(px, r, fill);
            Color b = border * a;
            sb.Draw(px, new Rectangle(r.X, r.Y, r.Width, 2), b);
            sb.Draw(px, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), b);
            sb.Draw(px, new Rectangle(r.X, r.Y, 2, r.Height), b);
            sb.Draw(px, new Rectangle(r.Right - 2, r.Y, 2, r.Height), b);
        }
    }
}
