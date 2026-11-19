using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.UI.Design
{
    public enum ButtonKind { Primary, Secondary, Ghost, Danger, Accent }

    /// <summary>
    /// Qayta ishlatiladigan UI komponentlar (PROMPT 19B, 13-bo'lim) — "immediate mode": har kadr Update'da chaqiriladi va
    /// shu kadrning o'zida chiziladi (bir xil kod hit-test va renderni bajaradi, shuning uchun ular hech qachon ajralib
    /// ketmaydi). Har bir interaktiv komponent Normal / Hover / Pressed / Disabled / Focus holatlariga ega.
    /// Barcha ranglar ThemeManager tokenlaridan, barcha matnlar Typography orqali.
    ///
    /// Foydalanish: Draw bosqichida <c>ui.Begin(sb, input, vw, vh)</c>, keyin komponentlar, oxirida <c>ui.End()</c>.
    /// Bosish natijalari (Button → true) shu kadrda qaytariladi.
    /// </summary>
    public sealed class UiKit
    {
        private readonly Texture2D _px;
        public Typography Type { get; }
        /// <summary>Joriy kadrdagi SpriteBatch (Begin..End orasida) — maxsus chizish uchun.</summary>
        public SpriteBatch Batch => _sb;
        public ThemeTokens T => ThemeManager.Current;

        private SpriteBatch _sb;
        private InputManager _input;
        private Point _mouse;
        private bool _click, _down, _touch;
        private string _tooltip;
        private Vector2 _tooltipPos;

        /// <summary>Klaviatura fokusidagi matn maydoni identifikatori.</summary>
        public string FocusId { get; set; }
        public int ViewW { get; private set; }
        public int ViewH { get; private set; }
        public bool Compact { get; private set; }
        public bool IsTouch => _touch;
        /// <summary>Shu kadrda biror komponent bosishni "yutdimi" (orqadagi elementlarga o'tmasligi uchun).</summary>
        public bool ClickConsumed { get; private set; }
        /// <summary>Tugma balandligi: telefonda kattaroq (barmoq uchun ≥ 48dp).</summary>
        public int ControlH => _touch ? (int)(52 * Type.UiScale) : 38;
        public int Gap => _touch ? 12 : 10;
        public float Time { get; private set; }

        public UiKit(Texture2D pixel, Typography type)
        {
            _px = pixel;
            Type = type;
        }

        public void Begin(SpriteBatch sb, InputManager input, int vw, int vh, float time)
        {
            _sb = sb;
            _input = input;
            _mouse = input.MousePosition;
            _click = input.TakeClick();
            _down = input.LeftDown;
            _touch = input.IsTouch;
            ViewW = vw; ViewH = vh;
            Time = time;
            Compact = vw < 1000 || _touch;
            ClickConsumed = false;
            _tooltip = null;
            // Telefonda (sensor) shrift va tugmalar kattaroq; PC'da asl o'lchamlar.
            Type.UiScale = _touch ? Math.Clamp(vh / 600f, 1.1f, 1.8f) : 1f;
        }

        public void End()
        {
            if (_tooltip != null && !_touch) DrawTooltip(_tooltip, _tooltipPos);
        }

        /// <summary>Sichqoncha/barmoq shu to'rtburchak ustidami.</summary>
        public bool Hover(Rectangle r) => r.Contains(_mouse);

        /// <summary>To'rtburchakka bosildimi (bosishni "yutadi").</summary>
        public bool Clicked(Rectangle r)
        {
            if (!_click || ClickConsumed || !r.Contains(_mouse)) return false;
            ClickConsumed = true;
            return true;
        }

        // ------------------------------------------------------------------
        // Asosiy chizish primitivlari
        // ------------------------------------------------------------------
        public void Fill(Rectangle r, Color c) => _sb.Draw(_px, r, c);

        /// <summary>Kiritish holati (xarita surish/kattalashtirish kabi maxsus boshqaruvlar uchun).</summary>
        public InputManager Input => _input;

        private static readonly RasterizerState ClipRaster = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };

        /// <summary>Shu to'rtburchakdan tashqariga chizilgan narsalar kesiladi (EndClip gacha). Panel batch'i qayta boshlanadi.</summary>
        public void BeginClip(Rectangle r)
        {
            _sb.End();
            Viewport vp = _sb.GraphicsDevice.Viewport;
            _sb.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(r, new Rectangle(0, 0, vp.Width, vp.Height));
            _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, ClipRaster);
        }

        public void EndClip()
        {
            _sb.End();
            _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        }

        /// <summary>Yumshoq burchakli to'rtburchak (burchak piksellari kesilgan) — "flat" zamonaviy ko'rinish.</summary>
        public void Rounded(Rectangle r, Color c, int radius = 6)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            // O'yin uslubi: to'rtburchak shakllar burchaksiz chiziladi; faqat doira va "pill" (nuqta, tugun, portret) dumaloq.
            if (radius * 2 < Math.Min(r.Width, r.Height) - 2) { Fill(r, c); return; }
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            Fill(new Rectangle(r.X + radius, r.Y, r.Width - 2 * radius, r.Height), c);
            for (int i = 0; i < radius; i++)
            {
                double dy = radius - i - 0.5;
                int inset = radius - (int)Math.Round(Math.Sqrt(radius * radius - dy * dy));
                Fill(new Rectangle(r.X + inset, r.Y + i, radius - inset, 1), c);
                Fill(new Rectangle(r.Right - radius, r.Y + i, radius - inset, 1), c);
                Fill(new Rectangle(r.X + inset, r.Bottom - 1 - i, radius - inset, 1), c);
                Fill(new Rectangle(r.Right - radius, r.Bottom - 1 - i, radius - inset, 1), c);
            }
            Fill(new Rectangle(r.X, r.Y + radius, radius, r.Height - 2 * radius), c);
            Fill(new Rectangle(r.Right - radius, r.Y + radius, radius, r.Height - 2 * radius), c);
        }

        /// <summary>
        /// O'yin uslubidagi ramka (shahar HUD'idagi kabi): to'rtburchak, chegara va ichki yorug'/soya chizig'i
        /// (bo'rtiq ko'rinish). Barcha panel, karta va tugmalar shu bilan chiziladi.
        /// </summary>
        public void Frame(Rectangle r, Color fill, Color border, int thickness = 1, bool bevel = true)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            if (border.A > 0)
            {
                Fill(new Rectangle(r.X, r.Y, r.Width, thickness), border);
                Fill(new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), border);
                Fill(new Rectangle(r.X, r.Y + thickness, thickness, r.Height - 2 * thickness), border);
                Fill(new Rectangle(r.Right - thickness, r.Y + thickness, thickness, r.Height - 2 * thickness), border);
            }
            var inner = new Rectangle(r.X + thickness, r.Y + thickness, r.Width - 2 * thickness, r.Height - 2 * thickness);
            if (fill.A > 0) Fill(inner, fill);
            if (bevel && fill.A > 0 && inner.Height > 8)
            {
                Fill(new Rectangle(inner.X, inner.Y, inner.Width, 1), Color.Lerp(fill, Color.White, 0.10f));
                Fill(new Rectangle(inner.X, inner.Bottom - 1, inner.Width, 1), Color.Lerp(fill, Color.Black, 0.30f));
            }
        }

        public void RoundedBorder(Rectangle r, Color border, Color fill, int radius = 6, int thickness = 1)
        {
            Rounded(r, border, radius);
            Rounded(new Rectangle(r.X + thickness, r.Y + thickness, r.Width - 2 * thickness, r.Height - 2 * thickness), fill, Math.Max(0, radius - thickness));
        }

        public void Text(string text, Vector2 pos, TextStyle style, Color? color = null)
            => Type.Draw(_sb, text, pos, style, color ?? T.TextPrimary);

        public void TextFit(string text, Rectangle r, TextStyle style, Color? color = null)
            => Type.DrawFitted(_sb, text, r, style, color ?? T.TextPrimary, 0);

        public float Paragraph(string text, Rectangle r, TextStyle style, Color? color = null, int maxLines = 99)
            => Type.DrawWrapped(_sb, text, r, style, color ?? T.TextSecondary, maxLines);

        // ------------------------------------------------------------------
        // UIPanel / UICard / UIModal
        // ------------------------------------------------------------------
        public void Panel(Rectangle r, bool elevated = true)
        {
            if (elevated) Fill(new Rectangle(r.X + 4, r.Y + 4, r.Width, r.Height), T.Shadow);
            Frame(r, T.Surface, T.Border, 2);
            // Oltin burchak bezaklari (o'yin panellaridagi kabi)
            int k = 4;
            Fill(new Rectangle(r.X, r.Y, k, k), T.Primary); Fill(new Rectangle(r.Right - k, r.Y, k, k), T.Primary);
            Fill(new Rectangle(r.X, r.Bottom - k, k, k), T.Primary); Fill(new Rectangle(r.Right - k, r.Bottom - k, k, k), T.Primary);
        }

        /// <summary>UICard: tanlanishi mumkin bo'lgan karta. Bosilsa true.</summary>
        public bool Card(Rectangle r, bool selected = false, bool interactive = false)
        {
            bool hover = interactive && Hover(r) && !_touch;
            Color fill = selected ? Color.Lerp(T.SurfaceAlt, T.Primary, 0.16f) : hover ? Color.Lerp(T.SurfaceAlt, T.Primary, 0.07f) : T.SurfaceAlt;
            Frame(r, fill, selected ? T.Primary : T.Border, selected ? 2 : 1);
            return interactive && Clicked(r);
        }

        public Rectangle Modal(int w, int h, string title)
        {
            Fill(new Rectangle(0, 0, ViewW, ViewH), T.Scrim);
            w = Math.Min(w, ViewW - 24); h = Math.Min(h, ViewH - 24);
            var r = new Rectangle((ViewW - w) / 2, (ViewH - h) / 2, w, h);
            Panel(r);
            Text(title, new Vector2(r.X + 20, r.Y + 16), TextStyle.PanelTitle);
            Fill(new Rectangle(r.X + 20, r.Y + 16 + (int)Type.LineHeight(TextStyle.PanelTitle) + 8, r.Width - 40, 1), T.Border);
            // Modal ostidagi elementlarga bosish o'tmasin: tashqariga bosish "yutiladi".
            if (_click && !r.Contains(_mouse)) ClickConsumed = true;
            return r;
        }

        // ------------------------------------------------------------------
        // UIButton / UIIconButton
        // ------------------------------------------------------------------
        public bool Button(Rectangle r, string label, ButtonKind kind = ButtonKind.Secondary, bool enabled = true, string tooltip = null)
        {
            bool hover = enabled && Hover(r);
            bool pressed = hover && _down;
            Color fill, border, text;
            switch (kind)
            {
                case ButtonKind.Primary:
                    fill = pressed ? T.PrimaryPressed : hover ? T.PrimaryHover : T.Primary; border = Color.Lerp(T.Primary, Color.Black, 0.45f); text = T.TextOnPrimary; break;
                case ButtonKind.Accent:
                    fill = pressed ? Color.Lerp(T.Accent, Color.Black, 0.15f) : hover ? Color.Lerp(T.Accent, Color.White, 0.12f) : T.Accent; border = Color.Lerp(T.Accent, Color.Black, 0.45f); text = T.TextOnPrimary; break;
                case ButtonKind.Danger:
                    Color d = Color.Lerp(T.Danger, Color.Black, 0.35f);
                    fill = pressed ? Color.Lerp(d, Color.Black, 0.15f) : hover ? Color.Lerp(d, T.Danger, 0.35f) : d; border = T.Danger; text = T.TextPrimary; break;
                case ButtonKind.Ghost:
                    fill = pressed ? T.SurfaceAlt : hover ? Color.Lerp(T.Surface, T.Primary, 0.10f) : Color.Transparent; border = hover ? T.Border : Color.Transparent; text = T.Primary; break;
                default:
                    fill = pressed ? Color.Lerp(T.SurfaceAlt, T.Primary, 0.2f) : hover ? Color.Lerp(T.SurfaceAlt, T.Primary, 0.1f) : T.SurfaceAlt; border = hover ? T.Primary : T.Border; text = T.TextPrimary; break;
            }
            if (!enabled) { fill = T.Disabled; border = Color.Lerp(T.Disabled, Color.Black, 0.3f); text = T.DisabledText; }
            Frame(r, fill, border, kind == ButtonKind.Primary || kind == ButtonKind.Danger ? 2 : 1, kind != ButtonKind.Ghost);
            // Bosilganda matn 1px pastga — taktil hissiyot. Matn tugma ichiga sig'diriladi (chiqib ketmaydi).
            var tr = pressed ? new Rectangle(r.X, r.Y + 1, r.Width, r.Height) : r;
            Type.DrawFitted(_sb, label, new Rectangle(tr.X + 7, tr.Y, tr.Width - 14, tr.Height), TextStyle.Button, text, 1);
            if (hover && tooltip != null) { _tooltip = tooltip; _tooltipPos = new Vector2(r.X, r.Bottom + 6); }
            return enabled && Clicked(r);
        }

        /// <summary>UIIconButton: kvadrat tugma, ichida belgi (masalan "×", "+", "−").</summary>
        public bool IconButton(Rectangle r, string glyph, string tooltip = null, bool enabled = true)
        {
            bool clicked = Button(r, "", ButtonKind.Ghost, enabled, tooltip);
            Type.DrawCentered(_sb, glyph, r, TextStyle.ScreenTitle, enabled ? T.TextSecondary : T.DisabledText);
            return clicked;
        }

        // ------------------------------------------------------------------
        // UITabBar
        // ------------------------------------------------------------------
        /// <summary>Gorizontal bo'limlar. Tanlangan indeksni qaytaradi.</summary>
        public int TabBar(Rectangle r, IReadOnlyList<string> tabs, int selected)
        {
            int n = tabs.Count;
            if (n == 0) return selected;
            float w = r.Width / (float)n;
            for (int i = 0; i < n; i++)
            {
                var tr = new Rectangle(r.X + (int)(i * w), r.Y, (int)w - 3, r.Height);
                bool on = i == selected;
                bool hover = Hover(tr) && !_touch;
                // O'yin pastki panelidagi tablar kabi: tanlangan — oltin ramka.
                Frame(tr, on ? T.SurfaceAlt : hover ? Color.Lerp(T.Surface, T.SurfaceAlt, 0.6f) : T.Surface, on ? T.Primary : T.Border, on ? 2 : 1);
                Type.DrawFitted(_sb, tabs[i], new Rectangle(tr.X + 3, tr.Y, tr.Width - 6, tr.Height), TextStyle.Button, on ? T.Primary : T.TextPrimary, 1);
                if (Clicked(tr)) selected = i;
            }
            return selected;
        }

        /// <summary>Vertikal navigatsiya (PC sidebar) yoki pastki navigatsiya (telefon) — bir xil komponent.</summary>
        public int NavList(Rectangle r, IReadOnlyList<string> items, int selected, bool horizontal)
        {
            if (horizontal) return TabBar(r, items, selected);
            int h = ControlH + 4;
            for (int i = 0; i < items.Count; i++)
            {
                var ir = new Rectangle(r.X, r.Y + i * (h + 4), r.Width, h);
                bool on = i == selected;
                bool hover = Hover(ir) && !_touch;
                Frame(ir, on ? T.SurfaceAlt : hover ? Color.Lerp(T.Surface, T.SurfaceAlt, 0.6f) : T.Surface, on ? T.Primary : T.Border, on ? 2 : 1);
                if (on) Fill(new Rectangle(ir.X + 2, ir.Y + 6, 3, ir.Height - 12), T.Primary);
                Type.DrawFitted(_sb, items[i], new Rectangle(ir.X + 14, ir.Y, ir.Width - 24, ir.Height), TextStyle.Button, on ? T.Primary : T.TextPrimary, 0);
                if (Clicked(ir)) selected = i;
            }
            return selected;
        }

        // ------------------------------------------------------------------
        // UIProgressBar / UIResourceCounter / chip
        // ------------------------------------------------------------------
        public void ProgressBar(Rectangle r, float value, Color? color = null)
        {
            Frame(r, T.Disabled, Color.Lerp(T.Border, Color.Black, 0.3f), 1, false);
            int w = (int)((r.Width - 2) * MathHelper.Clamp(value, 0f, 1f));
            if (w > 0) Fill(new Rectangle(r.X + 1, r.Y + 1, w, r.Height - 2), color ?? T.Primary);
        }

        /// <summary>UIResourceCounter: rangli nuqta + qiymat + yorliq (HUD va panellar uchun).</summary>
        public int ResourceCounter(Vector2 pos, Color dot, string value, string label)
        {
            int x = (int)pos.X, y = (int)pos.Y;
            Rounded(new Rectangle(x, y + 4, 12, 12), dot, 6);
            Text(value, new Vector2(x + 18, y), TextStyle.Body, T.TextPrimary);
            float vw = Type.Measure(value, TextStyle.Body).X;
            Text(label, new Vector2(x + 18 + vw + 6, y + 1), TextStyle.Secondary, T.TextSecondary);
            return (int)(18 + vw + 6 + Type.Measure(label, TextStyle.Secondary).X + 18);
        }

        /// <summary>Tanlanadigan "chip" (masalan ekin turi). Bosilsa true.</summary>
        public bool Chip(Rectangle r, string label, bool selected, Color? dot = null)
        {
            bool hover = Hover(r) && !_touch;
            Frame(r, selected ? Color.Lerp(T.SurfaceAlt, T.Primary, 0.22f) : hover ? T.SurfaceAlt : T.Surface, selected ? T.Primary : T.Border, selected ? 2 : 1);
            int tx = r.X + 10;
            if (dot.HasValue) { Fill(new Rectangle(tx, r.Center.Y - 4, 8, 8), dot.Value); tx += 14; }
            Type.DrawFitted(_sb, label, new Rectangle(tx, r.Y, r.Right - tx - 6, r.Height), TextStyle.Secondary, selected ? T.Primary : T.TextPrimary, 0);
            return Clicked(r);
        }

        public void Badge(Vector2 pos, string text, Color bg, Color? fg = null)
        {
            Vector2 m = Type.Measure(text, TextStyle.Caption);
            var r = new Rectangle((int)pos.X, (int)pos.Y, (int)m.X + 14, (int)Type.LineHeight(TextStyle.Caption) + 4);
            Frame(r, Color.Lerp(bg, Color.Black, 0.45f), bg, 1, false);
            Type.DrawCentered(_sb, text, r, TextStyle.Caption, fg ?? bg);
        }

        // ------------------------------------------------------------------
        // UITextInput
        // ------------------------------------------------------------------
        /// <summary>Matn maydoni. Bosilganda fokus oladi; fokusda klaviatura belgilarini qabul qiladi.</summary>
        public string TextInput(Rectangle r, string id, string value, string placeholder, bool password = false, Action onFocus = null, int maxLen = 64)
        {
            bool focus = FocusId == id;
            bool hover = Hover(r) && !_touch;
            Frame(r, T.Background, focus ? T.Focus : hover ? T.Secondary : T.Border, focus ? 2 : 1, false);
            if (Clicked(r)) { FocusId = id; focus = true; onFocus?.Invoke(); }
            if (focus)
                while (_input.TryReadChar(out char c))
                {
                    if (c == '\b') { if (value.Length > 0) value = value.Substring(0, value.Length - 1); }
                    else if (!char.IsControl(c) && value.Length < maxLen) value += c;
                }
            string shown = string.IsNullOrEmpty(value) ? placeholder : (password ? new string('*', value.Length) : value);
            Color col = string.IsNullOrEmpty(value) ? T.TextSecondary : T.TextPrimary;
            var inner = new Rectangle(r.X + 12, r.Y, r.Width - 24, r.Height);
            string fit = Type.Fit(shown, inner.Width - 8, TextStyle.Body);
            Type.Draw(_sb, fit, new Vector2(inner.X, r.Y + (r.Height - Type.LineHeight(TextStyle.Body)) / 2f), TextStyle.Body, col);
            if (focus && ((int)(Time * 2) % 2 == 0))
            {
                float cx = inner.X + (string.IsNullOrEmpty(value) ? 0 : Type.Measure(fit, TextStyle.Body).X) + 1;
                Fill(new Rectangle((int)cx, r.Y + 9, 2, r.Height - 18), T.Focus);
            }
            return value;
        }

        // ------------------------------------------------------------------
        // UIScrollView (oddiy): ro'yxatni siljitish — g'ildirak yoki barmoq bilan
        // ------------------------------------------------------------------
        private readonly Dictionary<string, float> _scroll = new Dictionary<string, float>();

        /// <summary>Ro'yxat uchun siljish (piksel). contentH &gt; r.Height bo'lsa g'ildirak/sudrash bilan siljiydi.</summary>
        public int Scroll(string id, Rectangle r, int contentH)
        {
            float s = _scroll.TryGetValue(id, out float v) ? v : 0f;
            if (Hover(r))
            {
                s -= _input.WheelDelta * 0.4f;
                if (_input.IsPanDragging) s -= _input.MouseDelta.Y;
            }
            s = MathHelper.Clamp(s, 0, Math.Max(0, contentH - r.Height));
            _scroll[id] = s;
            if (contentH > r.Height)
            {
                int bh = Math.Max(24, r.Height * r.Height / contentH);
                int by = r.Y + (int)((r.Height - bh) * (s / Math.Max(1, contentH - r.Height)));
                Fill(new Rectangle(r.Right - 4, by, 4, bh), T.Border);
            }
            return (int)s;
        }

        /// <summary>Element ko'rinadigan oynaga to'liq sig'adimi (qisman ko'ringanlarini chizmaymiz — kesilib qolmasin).</summary>
        public static bool InView(Rectangle item, Rectangle view) => item.Y >= view.Y - 1 && item.Bottom <= view.Bottom + 1;

        // ------------------------------------------------------------------
        // UIToast / tooltip
        // ------------------------------------------------------------------
        public void Toast(string text, bool error, float alpha)
        {
            if (string.IsNullOrEmpty(text) || alpha <= 0f) return;
            float maxW = Math.Min(ViewW - 40, 640);
            List<string> lines = Type.Wrap(text, maxW - 40, TextStyle.Body);
            float lh = Type.LineHeight(TextStyle.Body) + 2;
            float w = 0; foreach (string l in lines) w = Math.Max(w, Type.Measure(l, TextStyle.Body).X);
            int h = (int)(lines.Count * lh + 22);
            var r = new Rectangle((int)(ViewW / 2f - (w + 52) / 2f), ViewH - h - (Compact ? ControlH + 40 : 70), (int)w + 52, h);
            Color a = error ? T.Danger : T.Success;
            Fill(new Rectangle(r.X + 3, r.Y + 3, r.Width, r.Height), T.Shadow * alpha);
            Frame(r, T.Surface * alpha, a * alpha, 2, false);
            Fill(new Rectangle(r.X + 2, r.Y + 2, 5, r.Height - 4), a * alpha);
            float y = r.Y + 11;
            foreach (string l in lines) { Type.Draw(_sb, l, new Vector2(r.X + 22, y), TextStyle.Body, T.TextPrimary * alpha); y += lh; }
        }

        private void DrawTooltip(string text, Vector2 pos)
        {
            Vector2 m = Type.Measure(text, TextStyle.Secondary);
            var r = new Rectangle((int)Math.Min(pos.X, ViewW - m.X - 24), (int)pos.Y, (int)m.X + 20, (int)Type.LineHeight(TextStyle.Secondary) + 12);
            Frame(r, T.SurfaceAlt, T.Primary, 1, false);
            Type.Draw(_sb, text, new Vector2(r.X + 10, r.Y + 6), TextStyle.Secondary, T.TextPrimary);
        }
    }
}
