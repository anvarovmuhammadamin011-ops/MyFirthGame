using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.UI
{
    /// <summary>
    /// Piksel-shriftli HUD yozuvlari uchun moslashuvchan (responsive) yordamchi: uzun matn mayda shriftga
    /// qisqartirilmaydi — o'qiladigan o'lchamda qatorlarga bo'linadi (telefonning tor ekranida ham).
    /// </summary>
    public static class HudText
    {
        /// <summary>Matnni so'zlar bo'yicha qatorlarga bo'ladi (har qator maxW dan oshmaydi). Ortiqcha qatorlar "…" bilan qisqartiriladi.</summary>
        public static List<string> Wrap(PixelFont font, string text, int scale, int maxW, int maxLines = 3)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            string line = "";
            foreach (string word in text.Split(' '))
            {
                if (word.Length == 0) continue;
                string test = line.Length == 0 ? word : line + " " + word;
                if (font.Measure(test, scale).X <= maxW || line.Length == 0) line = test;
                else { lines.Add(line); line = word; }
            }
            if (line.Length > 0) lines.Add(line);
            if (lines.Count > maxLines)
            {
                lines.RemoveRange(maxLines, lines.Count - maxLines);
                string last = lines[maxLines - 1];
                while (last.Length > 1 && font.Measure(last + "..", scale).X > maxW) last = last.Substring(0, last.Length - 1);
                lines[maxLines - 1] = last + "..";
            }
            return lines;
        }

        /// <summary>Eng katta o'qiladigan masshtab: matn bir qatorga sig'sa — want, aks holda minScale (keyin qatorlarga bo'linadi).</summary>
        public static int Scale(PixelFont font, string text, int want, int minScale, int maxW)
        {
            for (int s = want; s > minScale; s--)
                if (font.Measure(text, s).X <= maxW) return s;
            return minScale;
        }

        /// <summary>Qatorlarni fonli quti ichida markazlab chizadi (yuqori-markaz nuqtadan). Qutini qaytaradi.</summary>
        public static Rectangle Box(SpriteBatch sb, Texture2D px, PixelFont font, IList<string> lines, int scale, float centerX, int top,
            Color bg, Color fg, int padX, int padY, Color? border = null, int lineGap = 4)
        {
            if (lines.Count == 0) return Rectangle.Empty;
            float w = 0;
            foreach (string l in lines) w = Math.Max(w, font.Measure(l, scale).X);
            int lh = PixelFont.GH * scale + lineGap;
            int h = lh * lines.Count - lineGap + padY * 2;
            var r = new Rectangle((int)(centerX - w / 2 - padX), top, (int)w + padX * 2, h);
            if (border.HasValue) sb.Draw(px, new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), border.Value);
            sb.Draw(px, r, bg);
            for (int i = 0; i < lines.Count; i++)
                font.DrawCentered(sb, lines[i], new Vector2(centerX, r.Y + padY + i * lh + PixelFont.GH * scale / 2f), fg, scale);
            return r;
        }
    }
}
