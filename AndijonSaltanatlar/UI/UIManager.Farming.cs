using System;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.UI
{
    /// <summary>
    /// FERMERLIK: dalalar holati, dehqonchilik ombori va FERMER MUROJAATI (SOTIB OLISH / KEYINROQ / OMBORNI KO'RISH).
    /// Xarid faqat o'yinchi tasdiqlasa bo'ladi.
    /// </summary>
    public sealed partial class UIManager
    {
        private Rectangle _farmBuyBtn, _farmLaterBtn, _farmModal, _modalBuyBtn, _modalLaterBtn, _modalStoreBtn;

        /// <summary>Fermer murojaati oynasi ochiqmi (boshqa bosishlarni to'sadi).</summary>
        public bool FarmRequestOpen => _state.Farming.PopupPending && _state.Farming.Active != null;

        private void LayoutFarm(int vw, int vh)
        {
            int pw = Math.Min(vw - 24, 760), ph = 300;
            _panelRect = new Rectangle(12, vh - BottomH - ph - 6, pw, ph);
            int bh = Touch ? 36 : 30;
            int rx = _panelRect.X + pw / 2 + 8;
            _farmBuyBtn = new Rectangle(rx, _panelRect.Bottom - 14 - bh, 170, bh);
            _farmLaterBtn = new Rectangle(_farmBuyBtn.Right + 8, _farmBuyBtn.Y, _panelRect.Right - 16 - (_farmBuyBtn.Right + 8), bh);
        }

        private void LayoutFarmModal(int vw, int vh)
        {
            int w = Math.Min(vw - 40, 680), h = 270;
            _farmModal = new Rectangle(vw / 2 - w / 2, vh / 2 - h / 2, w, h);
            int bh = Touch ? 38 : 32, g = 10;
            int bw = (w - 40 - 2 * g) / 3;
            int by = _farmModal.Bottom - 16 - bh;
            _modalBuyBtn = new Rectangle(_farmModal.X + 20, by, bw, bh);
            _modalLaterBtn = new Rectangle(_modalBuyBtn.Right + g, by, bw, bh);
            _modalStoreBtn = new Rectangle(_modalLaterBtn.Right + g, by, bw, bh);
        }

        private void BuyFarmRequest()
        {
            int coins = _state.Coins;
            bool ok = _state.Farming.Buy(ref coins, _state.Day, out string msg);
            if (ok) _state.Coins = coins;
            _state.ShowMessage(msg, !ok);
        }

        /// <summary>Murojaat oynasi bosishlari. Oyna ochiq bo'lsa true (tashqaridagi bosishlar e'tiborsiz).</summary>
        private bool ClickFarmModal(Point m)
        {
            if (!FarmRequestOpen) return false;
            if (_modalBuyBtn.Contains(m)) { BuyFarmRequest(); return true; }
            if (_modalLaterBtn.Contains(m)) { _state.Farming.Later(_state.Day); _state.ShowMessage("Fermerlar 3 kundan keyin yana so'raydi"); return true; }
            if (_modalStoreBtn.Contains(m)) { _state.Farming.PopupPending = false; _tab = Tab.Farm; return true; }
            return true;
        }

        private bool ClickFarm(Point m)
        {
            FarmRequest r = _state.Farming.Active;
            if (r != null && _farmBuyBtn.Contains(m)) { BuyFarmRequest(); return true; }
            if (r != null && _farmLaterBtn.Contains(m)) { _state.Farming.Later(_state.Day); return true; }
            return false;
        }

        private void DrawFarmPanel(SpriteBatch sb)
        {
            Panel(sb, _panelRect, PanelBg, Border, 2);
            FarmingManager f = _state.Farming;
            int x = _panelRect.X + 16, y = _panelRect.Y + 12;
            int half = _panelRect.Width / 2;
            _font.Draw(sb, "FERMERLIK", new Vector2(x, y), Gold, 3, 1);
            y += 32;

            int farms = 0, empty = 0, growing = 0, mature = 0, stored = 0;
            foreach (Building b in _buildings.Buildings)
            {
                if (!(b is Farm farm)) continue;
                farms++;
                stored += farm.StoredProduce;
                if (farm.CropStage == FarmCropStage.Empty) empty++;
                else if (farm.CropStage == FarmCropStage.Growing) growing++;
                else mature++;
            }
            float prev = PixelFont.ClipRight;
            PixelFont.ClipRight = _panelRect.X + half - 8;
            string[] left =
            {
                "Dalalar: " + farms,
                "Ekilgan (o'smoqda): " + growing,
                "Hosilga tayyor: " + mature,
                "Bo'sh dala: " + empty + (f.IdleFields > 0 ? "  (urug' yo'q!)" : ""),
                "Dehqonlar: " + _state.Pop.CountProfession(AndijonSaltanatlar.Population.CitizenProfession.Dehqon),
                "Oziq-ovqat: " + _state.Food,
                "Jami o'rilgan hosil: " + f.HarvestedTotal,
            };
            foreach (string l in left) { _font.Draw(sb, l, new Vector2(x, y), Cream, 2, 1); y += 21; }
            PixelFont.ClipRight = prev;

            // Ombor
            int rx = _panelRect.X + half + 8, ry = _panelRect.Y + 44;
            _font.Draw(sb, "OMBOR", new Vector2(rx, ry), Gold, 2, 1); ry += 24;
            PixelFont.ClipRight = _panelRect.Right - 12;
            _font.Draw(sb, "Urug': " + f.Seeds + " qop", new Vector2(rx, ry), f.Seeds < Math.Max(3, farms) ? Red : Cream, 2, 1); ry += 21;
            _font.Draw(sb, "Asboblar: " + f.ToolsLevel + " / " + FarmingManager.MaxToolsLevel + "  (+" + 25 * f.ToolsLevel + "%)", new Vector2(rx, ry), Cream, 2, 1); ry += 21;
            _font.Draw(sb, "O'g'it: " + f.FertilizerCharges + " hosilga", new Vector2(rx, ry), Cream, 2, 1); ry += 21;
            _font.Draw(sb, "Ariq: " + (f.Irrigation ? "tozalangan (+10%)" : "tozalanmagan"), new Vector2(rx, ry), f.Irrigation ? Green : Muted, 2, 1); ry += 21;
            _font.Draw(sb, "Dalada saqlangan hosil: " + stored, new Vector2(rx, ry), Cream, 2, 1); ry += 26;
            PixelFont.ClipRight = prev;

            FarmRequest r = f.Active;
            if (r != null)
            {
                _font.Draw(sb, FitText("SO'ROV: " + r.Item + " — " + r.Cost + " tanga", _panelRect.Right - 16 - rx), new Vector2(rx, _farmBuyBtn.Y - 24), Gold, 2, 1);
                bool afford = _state.Coins >= r.Cost;
                Panel(sb, _farmBuyBtn, afford ? new Color(70, 54, 24, 245) : PanelBg2, afford ? Gold : Red, 1);
                DrawButtonText(sb, "SOTIB OLISH", _farmBuyBtn, afford ? Gold : Red);
                Panel(sb, _farmLaterBtn, PanelBg2, Border, 1);
                DrawButtonText(sb, "KEYINROQ", _farmLaterBtn, Cream);
            }
            else
                _font.Draw(sb, FitText("Hozircha so'rov yo'q", _panelRect.Right - 16 - rx), new Vector2(rx, _farmBuyBtn.Y + 6), Muted, 2, 1);
        }

        private void DrawFarmModal(SpriteBatch sb, int vw, int vh)
        {
            FarmRequest r = _state.Farming.Active;
            if (r == null) return;
            Fill(sb, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 140));
            // Gapirayotgan fermer — pastda yarim tanasi ko'rinib, boshini tebratib "gapiradi"
            DrawSpeaker(sb, vw, vh, _assets.TryGet(SpriteKeys.UnitCitizen + "3_0"), "FERMER");
            Panel(sb, _farmModal, PanelBg, Gold, 2);
            int x = _farmModal.X + 20, y = _farmModal.Y + 16;
            Texture2D icon = _assets.TryGet(SpriteKeys.UnitCitizen + "3_0");
            if (icon != null) DrawFit(sb, icon, new Rectangle(x, y, 44, 52));
            _font.Draw(sb, "FERMER MUROJAATI", new Vector2(x + 56, y + 2), Gold, 3, 1);
            _font.Draw(sb, FitText(r.Title, _farmModal.Width - 96), new Vector2(x + 56, y + 30), Cream, 2, 1);
            y += 64;
            foreach (string line in Wrap(r.Body, _farmModal.Width - 40, 2)) { if (y > _modalBuyBtn.Y - 50) break; _font.Draw(sb, line, new Vector2(x, y), Cream, 2, 1); y += 21; }
            bool afford = _state.Coins >= r.Cost;
            _font.Draw(sb, "Narxi: " + r.Cost + " tanga   (xazina: " + _state.Coins + ")", new Vector2(x, _modalBuyBtn.Y - 30), afford ? Gold : Red, 2, 1);
            Panel(sb, _modalBuyBtn, afford ? new Color(70, 54, 24, 245) : PanelBg2, afford ? Gold : Red, 2);
            DrawButtonText(sb, "SOTIB OLISH", _modalBuyBtn, afford ? Gold : Red);
            Panel(sb, _modalLaterBtn, PanelBg2, Border, 1);
            DrawButtonText(sb, "KEYINROQ", _modalLaterBtn, Cream);
            Panel(sb, _modalStoreBtn, PanelBg2, Border, 1);
            DrawButtonText(sb, "OMBORNI KO'RISH", _modalStoreBtn, Cream);
        }

        /// <summary>Ekranning pastki chap burchagida gapirayotgan NPC portreti: yarim tanasi ko'rinadi,
        /// boshi tebranadi va og'iz oldida pulslanuvchi nuqtalar bilan "gapirayotgani" bildiriladi (ovoz tizimi yo'q).</summary>
        private void DrawSpeaker(SpriteBatch sb, int vw, int vh, Texture2D icon, string name)
        {
            if (icon == null) return;
            float t = System.Environment.TickCount / 1000f;
            int maxW = Math.Max(70, _farmModal.X - 24);
            int ph = (int)(vh * 0.66f);
            int pw = ph * icon.Width / Math.Max(1, icon.Height);
            if (pw > maxW) { pw = maxW; ph = pw * icon.Height / Math.Max(1, icon.Width); }
            int bob = (int)(MathF.Sin(t * 3.0f) * 3f);
            int px = 16;
            int py = vh - (int)(ph * 0.55f) + bob; // tananing yarmi ekran pastidan tashqarida
            Fill(sb, new Rectangle(px - 6, py + 6, pw + 12, vh - py), new Color(18, 14, 8, 120));
            DrawFit(sb, icon, new Rectangle(px, py, pw, ph));
            // gapirish belgisi (og'iz oldida pulslanuvchi nuqtalar)
            int talk = (int)(t * 5) % 3 + 1;
            int my = py + (int)(ph * 0.16f), mx = px + pw - 12;
            for (int i = 0; i < talk; i++) Fill(sb, new Rectangle(mx + i * 6, my - i * 2, 4, 4), new Color(255, 236, 170));
            // ism lavhasi
            Vector2 nm = _font.Measure(name, 2);
            var nr = new Rectangle(px + pw / 2 - (int)nm.X / 2 - 8, py - 4, (int)nm.X + 16, 22);
            Fill(sb, nr, new Color(44, 32, 16, 235));
            _font.DrawCentered(sb, name, nr.Center.ToVector2(), Gold, 2);
        }
    }
}
