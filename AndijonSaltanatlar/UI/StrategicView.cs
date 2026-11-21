using System;
using System.Collections.Generic;
using System.Linq;
using AndijonSaltanatlar.Battles;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Diplomacy;
using AndijonSaltanatlar.Kingdoms;
using AndijonSaltanatlar.Regions;
using AndijonSaltanatlar.Trade;
using AndijonSaltanatlar.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AndijonSaltanatlar.UI
{
    /// <summary>
    /// SALTANAT XARITASI — strategik dunyo ekrani: hududlar, egalari (rang), poytaxtlar, savdo
    /// yo'llari, hudud ma'lumoti, diplomatik harakatlar, hukmdor boshqaruv paneli va voqealar tasmasi.
    /// Shahar ekranidan M tugmasi bilan o'tiladi.
    /// </summary>
    public sealed class StrategicView
    {
        private static readonly Color Bg = new Color(18, 22, 30);
        private static readonly Color PanelBg = new Color(30, 25, 20, 240);
        private static readonly Color Border = new Color(150, 116, 66);
        private static readonly Color Gold = new Color(235, 200, 120);
        private static readonly Color Cream = new Color(234, 224, 202);
        private static readonly Color Muted = new Color(176, 160, 134);
        private static readonly Color Green = new Color(120, 214, 132);
        private static readonly Color Red = new Color(236, 112, 96);
        private static readonly Color Edge = new Color(90, 90, 110);

        private readonly AssetManager _assets;
        private readonly PixelFont _font;
        private readonly GameStateManager _state;
        private readonly Texture2D _px;

        public bool BackRequested;
        public bool EnterCityRequested;
        private Vector2 _pan = Vector2.Zero;
        private float _zoom = 1f;

        private WorldSimulation W => _state.World;

        private Rectangle _mapRect, _rightPanel, _backBtn, _acceptBtn, _rejectBtn, _battleClose;
        private readonly Dictionary<int, Vector2> _node = new Dictionary<int, Vector2>();

        // ---- Yurish: qo'shin xaritada nishonga boradi, yetib kelgach jang sahnasi ochiladi ----
        public const float MarchTime = 2.8f;
        private int _marchFrom = -1, _marchTarget = -1;
        private readonly System.Diagnostics.Stopwatch _marchClock = new System.Diagnostics.Stopwatch();
        /// <summary>Xarita animatsiyalari uchun doimiy soat (karvon harakati).</summary>
        private readonly System.Diagnostics.Stopwatch _animClock = System.Diagnostics.Stopwatch.StartNew();
        /// <summary>Qo'shin nishonga yetdi — Game1 jang sahnasini ochadi (keyin -1 ga qaytaradi).</summary>
        public int MarchArrivedRegion { get; set; } = -1;
        public bool Marching => _marchTarget >= 0;

        /// <summary>Yurishni boshlaydi (sinov va tugma uchun).</summary>
        public bool StartMarch(int targetRegionId)
        {
            if (Marching) return false;
            Region target = W.Regions.Get(targetRegionId);
            if (target == null) return false;
            if (_state.Military.TotalUnits == 0) { W.AddEvent("Yurish uchun qo'shin yo'q — avval jangchi sotib oling."); return false; }
            int pk = W.Kingdoms.PlayerKingdomId;
            Region home = null;
            foreach (Region r in W.Regions.Regions)
                if (r.OwnerKingdomId == pk && (home == null || r.IsCapital)) home = r;
            if (home == null) return false;
            _marchFrom = home.Id;
            _marchTarget = target.Id;
            _marchClock.Restart();
            W.AddEvent("Qo'shin " + home.Name + "dan " + target.Name + " sari yurishga chiqdi.");
            return true;
        }
        private readonly Dictionary<int, float> _radius = new Dictionary<int, float>();
        private readonly List<(string label, int action, Rectangle rect)> _actions = new List<(string, int, Rectangle)>();
        private int _selectedRegion;

        public StrategicView(AssetManager assets, PixelFont font, GameStateManager state)
        {
            _assets = assets; _font = font; _state = state; _px = assets.Pixel;
        }

        // ------------------------------------------------------------------
        private void Layout(int vw, int vh)
        {
            _mapRect = new Rectangle(16, 56, (int)(vw * 0.58f), vh - 56 - 150);
            _rightPanel = new Rectangle(_mapRect.Right + 12, 56, vw - _mapRect.Right - 28, vh - 56 - 16);
            _backBtn = new Rectangle(vw - 226, 14, 210, 30);

            _node.Clear(); _radius.Clear();
            var center = new Vector2(_mapRect.Center.X, _mapRect.Center.Y);
            foreach (Region r in W.Regions.Regions)
            {
                var basePos = new Vector2(_mapRect.X + r.X * _mapRect.Width, _mapRect.Y + r.Y * _mapRect.Height);
                _node[r.Id] = center + (basePos - center) * _zoom + _pan;
                _radius[r.Id] = (11 + r.Development / 9f) * _zoom;
            }

            // Tanlangan hudud harakatlari
            _actions.Clear();
            Region sel = W.Regions.Get(_selectedRegion);
            if (sel != null)
            {
                int bx = _rightPanel.X + 16, bw = _rightPanel.Width - 32, bh = 28;
                int by = _rightPanel.Y + 250;
                void Act(string label, int id) { _actions.Add((label, id, new Rectangle(bx, by, bw, bh))); by += bh + 6; }

                if (sel.OwnerKingdomId == W.Kingdoms.PlayerKingdomId) Act("SHAHARGA KIRISH", 7);
                else if (sel.OwnerKingdomId == 0) Act("Hududni qo'shish (" + WorldSimulation.AnnexCost + " tanga)", 6);
                else
                {
                    Act("Elchi yuborish (+5)", 1);
                    Act("Sovg'a berish (" + WorldSimulation.GiftCost + ", +12)", 2);
                    Act("Savdo taklif qilish", 3);
                    Act("Ittifoq taklif qilish", 4);
                    Act("YURISH — urushga borish", 5);
                }
            }

            // Kutilayotgan savdo taklifi
            var offers = W.PendingOffersForPlayer();
            if (offers.Count > 0)
            {
                int cx = _mapRect.X + 10, cy = _mapRect.Y + 10;
                _acceptBtn = new Rectangle(cx, cy + 34, 120, 26);
                _rejectBtn = new Rectangle(cx + 130, cy + 34, 120, 26);
            }
            else { _acceptBtn = _rejectBtn = Rectangle.Empty; }

            if (W.Battles.Last != null)
                _battleClose = new Rectangle(vw / 2 - 70, vh / 2 + 130, 140, 30);
        }

        // ------------------------------------------------------------------
        public void Update(InputManager input, int vw, int vh)
        {
            UpdateCamera(input);
            Layout(vw, vh);
            input.MouseOverUI = true; // strategik ekranda butun oyna UI
            if (Marching)
            {
                // Yurish davomida faqat kutiladi; yetib borgach jang boshlanadi
                if (_marchClock.Elapsed.TotalSeconds >= MarchTime) { MarchArrivedRegion = _marchTarget; _marchTarget = -1; _marchClock.Stop(); }
                return;
            }
            if (!input.LeftPressed) return;
            Point m = input.MousePosition;

            if (W.Battles.Last != null)   // jang natija oynasi ustuvor
            {
                if (_battleClose.Contains(m)) W.Battles.ClearLast();
                return;
            }

            if (_backBtn.Contains(m)) { BackRequested = true; return; }

            // Savdo taklifi
            var offers = W.PendingOffersForPlayer();
            if (offers.Count > 0)
            {
                if (_acceptBtn.Contains(m)) { W.AcceptOffer(offers[0]); return; }
                if (_rejectBtn.Contains(m)) { W.RejectOffer(offers[0]); return; }
            }

            // Hudud harakatlari
            foreach (var (label, action, rect) in _actions)
                if (rect.Contains(m)) { DoAction(action); return; }

            // Hudud tanlash (xarita tugunlari)
            foreach (Region r in W.Regions.Regions)
            {
                float rad = _radius[r.Id] + 4;
                if (Vector2.Distance(_node[r.Id], new Vector2(m.X, m.Y)) <= rad) { _selectedRegion = r.Id; return; }
            }
        }

        private void UpdateCamera(InputManager input)
        {
            const float panSpeed = 10f;
            if (input.KeyDown(Keys.A) || input.KeyDown(Keys.Left)) _pan.X += panSpeed;
            if (input.KeyDown(Keys.D) || input.KeyDown(Keys.Right)) _pan.X -= panSpeed;
            if (input.KeyDown(Keys.W) || input.KeyDown(Keys.Up)) _pan.Y += panSpeed;
            if (input.KeyDown(Keys.S) || input.KeyDown(Keys.Down)) _pan.Y -= panSpeed;
            if (input.IsPanDragging) { Point d = input.MouseDelta; _pan += new Vector2(d.X, d.Y); }
            if (input.WheelDelta != 0) _zoom = MathHelper.Clamp(_zoom * (input.WheelDelta > 0 ? 1.1f : 0.9f), 0.7f, 2.0f);
            _pan.X = MathHelper.Clamp(_pan.X, -360f, 360f);
            _pan.Y = MathHelper.Clamp(_pan.Y, -260f, 260f);
        }

        private void DoAction(int action)
        {
            Region sel = W.Regions.Get(_selectedRegion);
            if (sel == null) return;
            int target = sel.OwnerKingdomId;
            switch (action)
            {
                case 7: EnterCityRequested = true; return;
                case 1: W.PlayerSendEnvoy(target); break;
                case 2: if (_state.Coins >= WorldSimulation.GiftCost) { _state.Coins -= WorldSimulation.GiftCost; W.PlayerGift(target); } else W.AddEvent("Sovg'a uchun tanga yetarli emas."); break;
                case 3: W.PlayerProposeTrade(target); break;
                case 4: W.PlayerProposeAlliance(target); break;
                case 5:
                    // Yurish: qo'shin xaritada boradi, keyin jang sahnasi (o'yinchi o'zi ko'radi va boshqaradi)
                    StartMarch(sel.Id);
                    break;
                case 6: if (_state.Coins >= WorldSimulation.AnnexCost) { _state.Coins -= WorldSimulation.AnnexCost; W.PlayerAnnexNeutral(sel.Id); } else W.AddEvent("Hududni qo'shish uchun tanga yetarli emas."); break;
            }
        }

        // ------------------------------------------------------------------
        public void Draw(SpriteBatch sb, int vw, int vh)
        {
            Layout(vw, vh);
            Fill(sb, new Rectangle(0, 0, vw, vh), Bg);

            _font.Draw(sb, "SALTANAT XARITASI", new Vector2(16, 14), Gold, 3, 1);
            _font.Draw(sb, "Andijon viloyati - muqobil tarix", new Vector2(360, 20), Muted, 2, 1);
            Panel(sb, _backBtn, PanelBg, Border, 1);
            _font.DrawCentered(sb, "SHAHARGA QAYTISH", _backBtn.Center.ToVector2(), Cream, 2, 1);

            Panel(sb, _mapRect, new Color(26, 32, 42, 240), Border, 2);
            DrawEdges(sb);
            DrawTradeRoutes(sb);
            DrawNodes(sb);
            DrawOffer(sb);
            DrawMarch(sb);
            DrawEvents(sb, vh);
            DrawRightPanel(sb);
            if (W.Battles.Last != null) DrawBattleModal(sb, vw, vh);
        }

        private void DrawBattleModal(SpriteBatch sb, int vw, int vh)
        {
            Fill(sb, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 170));
            BattleResult r = W.Battles.Last;
            int w = 560, h = 330;
            var box = new Rectangle(vw / 2 - w / 2, vh / 2 - h / 2, w, h);
            Color oc = r.Outcome == BattleOutcome.Galaba ? Green : r.Outcome == BattleOutcome.Maglubiyat ? Red : Gold;
            Panel(sb, box, PanelBg, oc, 2);
            int x = box.X + 22, y = box.Y + 16;
            _font.DrawCentered(sb, "JANG NATIJASI", new Vector2(box.Center.X, y + 8), Gold, 3, 1); y += 38;
            _font.DrawCentered(sb, BattleResult.OutcomeName(r.Outcome), new Vector2(box.Center.X, y + 4), oc, 3, 1); y += 36;
            string[] lines =
            {
                r.AttackerName + "  vs  " + r.DefenderName,
                "Hudud: " + r.RegionName,
                "Kuch: " + r.AttackerStrength + " / " + r.DefenderStrength,
                "Talofat: " + r.AttackerLosses + " / " + r.DefenderLosses,
                "Davomiyligi: " + r.DurationDays + " kun",
                "Xazina: " + (r.CoinChange >= 0 ? "+" : "") + r.CoinChange,
                r.TerritoryResult,
                r.DiplomaticResult,
            };
            foreach (string l in lines) { _font.Draw(sb, l, new Vector2(x, y), Cream, 2, 1); y += 18; }
            Panel(sb, _battleClose, PanelBg, Border, 1);
            _font.DrawCentered(sb, "Yopish", _battleClose.Center.ToVector2(), Cream, 2, 1);
        }

        private void DrawEdges(SpriteBatch sb)
        {
            var regs = W.Regions.Regions;
            for (int i = 0; i < regs.Count; i++)
                for (int j = i + 1; j < regs.Count; j++)
                {
                    float dx = regs[i].X - regs[j].X, dy = regs[i].Y - regs[j].Y;
                    if (dx * dx + dy * dy < 0.045f) DrawLine(sb, _node[regs[i].Id], _node[regs[j].Id], Edge, 2);
                }
        }

        private static readonly Color Caravan = new Color(222, 180, 120);

        private void DrawTradeRoutes(SpriteBatch sb)
        {
            float time = (float)_animClock.Elapsed.TotalSeconds;
            foreach (TradeAgreement a in W.Trade.Agreements)
            {
                if (a.Status != TradeStatus.Active) continue;
                Kingdom s = W.Kingdoms.Get(a.SellerKingdomId), b = W.Kingdoms.Get(a.BuyerKingdomId);
                if (s == null || b == null) continue;
                if (!_node.TryGetValue(s.CapitalRegionId, out var p1) || !_node.TryGetValue(b.CapitalRegionId, out var p2)) continue;
                DrawLine(sb, p1, p2, Gold, 2);

                // Harakatlanuvchi karvon: sotuvchidan xaridorga mol tashiydi, yo'l bo'ylab takrorlanadi (shaharlararo savdo jonli ko'rinsin)
                float dist = Vector2.Distance(p1, p2);
                if (dist < 6f) continue;
                Vector2 dir = (p2 - p1) / dist;
                float period = MathF.Max(3.5f, dist / 34f);           // uzoqroq yo'l — karvon sekinroq o'tadi
                float frac = ((time + a.Id * 1.37f) % period) / period;
                Vector2 at = Vector2.Lerp(p1, p2, frac);
                at.Y += MathF.Sin(frac * MathF.PI * 6f) * 1.2f;        // yengil tebranish (tuya qadami)
                DrawDisc(sb, at, 3.2f, Caravan);                       // tuya
                DrawDisc(sb, at - dir * 4.2f, 2.4f, Canvas.Shade(Caravan, 0.8f)); // ortidagi yuk
            }
        }

        // AI saltanatlarining shahar darajasi (1..5) — vizual progress marker (Prompt 8 / 21).
        // Shu darajaga mos shahar nurlanishi, daraja ko'rsatkichlari va rivojlanish ikonkalari.
        private static readonly Color[] KingdomProgBorders =
        {
            Color.White, // 1-daraja: oddiy
            new Color(255, 200, 120), // 2: yengil yulduz
            new Color(120, 200, 255), // 3: maroshaga o'xshash
            new Color(255, 180, 80),   // 4
            new Color(255, 220, 160),  // 5: aniq
        };

        private void DrawNodes(SpriteBatch sb)
        {
            foreach (Region r in W.Regions.Regions)
            {
                Vector2 p = _node[r.Id];
                float rad = _radius[r.Id];
                Kingdom owner = r.OwnerKingdomId == 0 ? null : W.Kingdoms.Get(r.OwnerKingdomId);
                Color col = owner?.MapColor ?? new Color(110, 110, 120);

                // Daraja ko'rsatkichlari — AI shahar rivojlanganda ko'rinishi kerak
                int kingdomDevLevel = owner != null ? ComputeKingdomVisualLevel(owner) : 0;
                if (kingdomDevLevel > 0 && owner != null)
                {
                    Color progBorder = KingdomProgBorders[Math.Min(kingdomDevLevel, KingdomProgBorders.Length - 1)];
                    DrawDisc(sb, p, rad + 3, progBorder);

                    // Markazdagi yulduz belgilari (daraja raqami)
                    for (int li = 1; li <= kingdomDevLevel && li <= 5; li++)
                    {
                        float lx = p.X + rad * 0.6f * (float)Math.Cos(MathHelper.TwoPi * (li - 1) / 5f - MathHelper.PiOver2);
                        float ly = p.Y + rad * 0.6f * (float)Math.Sin(MathHelper.TwoPi * (li - 1) / 5f - MathHelper.PiOver2);
                        DrawDisc(sb, new Vector2(lx, ly), 2, progBorder);
                    }
                }

                if (r.Id == _selectedRegion) DrawDisc(sb, p, rad + 5, Gold);
                DrawDisc(sb, p, rad, col);
                DrawDisc(sb, p, rad - 3, Canvas.Shade(col, 1.25f));
                if (r.IsCapital) DrawDisc(sb, p, 4, Color.White);
                _font.DrawCentered(sb, r.Name, new Vector2(p.X, p.Y + rad + 9), Cream, 2, 1);
            }
        }

        /// <summary>AI saltanat darajasini hududlar statistikasiga asosan hisoblaydi (Prompt 8).
        /// Hozircha: Development + Population + Stability + EconomicStrength asosida 1..5 oralig'ida.</summary>
        private int ComputeKingdomVisualLevel(Kingdom k)
        {
            if (k == null) return 1;
            int avgDev = k.RegionCount > 0
                ? k.OwnedRegionIds.Sum(id => W.Regions.Get(id)?.Development ?? 0) / k.RegionCount
                : 0;
            float raw = (avgDev / 20f) + (k.Stability / 25f) + (k.EconomicStrength / 30f);
            return Math.Max(1, Math.Min(5, 1 + (int)raw));
        }

        private void DrawOffer(SpriteBatch sb)
        {
            var offers = W.PendingOffersForPlayer();
            if (offers.Count == 0) return;
            TradeAgreement a = offers[0];
            Kingdom from = W.Kingdoms.Get(a.SellerKingdomId);
            var box = new Rectangle(_mapRect.X + 6, _mapRect.Y + 6, 264, 68);
            Panel(sb, box, PanelBg, Gold, 1);
            _font.Draw(sb, "Savdo taklifi: " + (from?.Name ?? ""), new Vector2(box.X + 8, box.Y + 6), Gold, 2, 1);
            _font.Draw(sb, a.ResourceType + " x" + a.Quantity + "  narx " + a.Price, new Vector2(box.X + 8, box.Y + 22), Cream, 2, 1);
            Panel(sb, _acceptBtn, PanelBg, Green, 1); _font.DrawCentered(sb, "Qabul", _acceptBtn.Center.ToVector2(), Green, 2, 1);
            Panel(sb, _rejectBtn, PanelBg, Red, 1); _font.DrawCentered(sb, "Rad etish", _rejectBtn.Center.ToVector2(), Red, 2, 1);
        }

        private void DrawMarch(SpriteBatch sb)
        {
            if (!Marching || !_node.TryGetValue(_marchFrom, out Vector2 a) || !_node.TryGetValue(_marchTarget, out Vector2 b)) return;
            float t = MathHelper.Clamp((float)(_marchClock.Elapsed.TotalSeconds / MarchTime), 0f, 1f);
            float e = t * t * (3f - 2f * t);
            // Yo'l: uzuq-uzuq oltin chiziq (o'tilgan qismi to'liq)
            for (float u = 0f; u <= 1f; u += 0.025f)
            {
                if (u > e && ((int)(u * 40f) & 1) == 1) continue;
                Vector2 p = Vector2.Lerp(a, b, u);
                Fill(sb, new Rectangle((int)p.X - 2, (int)p.Y - 2, 4, 4), u <= e ? Gold : new Color(235, 200, 120, 110));
            }
            // Qo'shin: uch jangchi va bayroq, yurish ritmida qadam tashlaydi
            Vector2 pos = Vector2.Lerp(a, b, e);
            int frame = (int)(_marchClock.Elapsed.TotalSeconds * 8) % 3;
            Texture2D tex = _assets.TryGet("unit_swordsman_" + frame) ?? _assets.TryGet("unit_swordsman_0");
            bool left = b.X < a.X;
            if (tex != null)
                for (int i = 0; i < 3; i++)
                {
                    Vector2 off = new Vector2((i - 1) * 12, (i == 1 ? -6 : 2));
                    sb.Draw(tex, pos + off, null, Color.White, 0f, new Vector2(tex.Width / 2f, tex.Height - 8 / AssetManager.ScaleOf(tex)), 0.8f * AssetManager.ScaleOf(tex),
                        left ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
                }
            Fill(sb, new Rectangle((int)pos.X + 16, (int)pos.Y - 44, 2, 34), new Color(110, 80, 50));
            Fill(sb, new Rectangle((int)pos.X + 18, (int)pos.Y - 44, 14, 9), new Color(70, 130, 220));
            Region from = W.Regions.Get(_marchFrom), to = W.Regions.Get(_marchTarget);
            string label = "YURISH: " + (from?.Name ?? "") + " -> " + (to?.Name ?? "");
            Vector2 ls = _font.Measure(label, 2, 1);
            var lr = new Rectangle(_mapRect.Center.X - (int)ls.X / 2 - 10, _mapRect.Y + 12, (int)ls.X + 20, 26);
            Panel(sb, lr, PanelBg, Gold, 1);
            _font.DrawCentered(sb, label, lr.Center.ToVector2(), Gold, 2, 1);
        }

        private void DrawEvents(SpriteBatch sb, int vh)
        {
            var box = new Rectangle(16, vh - 140, _mapRect.Width, 128);
            Panel(sb, box, PanelBg, Border, 1);
            _font.Draw(sb, "VOQEALAR", new Vector2(box.X + 10, box.Y + 6), Gold, 2, 1);
            int shown = Math.Min(3, W.Events.Count);
            int y = box.Y + 26;
            for (int i = 0; i < shown; i++)
            {
                string e = W.Events[W.Events.Count - shown + i];
                bool first = true;
                foreach (string line in Wrap(e, box.Width - 44, 2))
                {
                    if (y + 14 > box.Bottom - 6) return;
                    _font.Draw(sb, (first ? "- " : "  ") + line, new Vector2(box.X + 10, y), Cream, 2, 1);
                    y += 19; first = false;
                }
            }
        }

        private void DrawRightPanel(SpriteBatch sb)
        {
            Panel(sb, _rightPanel, PanelBg, Border, 2);
            int x = _rightPanel.X + 16, y = _rightPanel.Y + 14;
            Region sel = W.Regions.Get(_selectedRegion);

            if (sel == null)
            {
                DrawDashboard(sb, x, y);
                return;
            }

            Kingdom owner = sel.OwnerKingdomId == 0 ? null : W.Kingdoms.Get(sel.OwnerKingdomId);
            _font.Draw(sb, sel.Name, new Vector2(x, y), Gold, 3, 1); y += 26;
            _font.Draw(sb, "Egasi: " + (owner?.Name ?? "Mustaqil hudud"), new Vector2(x, y), Cream, 2, 1); y += 21;
            if (owner != null) { _font.Draw(sb, "Hukmdor: " + owner.RulerName, new Vector2(x, y), Muted, 2, 1); y += 21; }
            _font.Draw(sb, "Aholi: " + sel.Population + "   Rivoj: " + sel.Development, new Vector2(x, y), Cream, 2, 1); y += 21;
            _font.Draw(sb, "Xavfsizlik: " + sel.Security + "   Baxt: " + sel.Happiness, new Vector2(x, y), Cream, 2, 1); y += 21;
            _font.Draw(sb, "Koord: " + sel.Latitude.ToString("0.00") + ", " + sel.Longitude.ToString("0.00"), new Vector2(x, y), Muted, 2, 1); y += 19;
            if (!string.IsNullOrEmpty(sel.Source)) { _font.Draw(sb, "Manba: " + sel.Source, new Vector2(x, y), Muted, 2, 1); y += 21; }

            if (owner != null && owner.Id != W.Kingdoms.PlayerKingdomId)
            {
                int rel = W.Diplomacy.Get(W.Kingdoms.PlayerKingdomId, owner.Id);
                RelationBand band = RelationBands.Of(rel);
                Color rc = rel >= 20 ? Green : rel <= -20 ? Red : Muted;
                _font.Draw(sb, "Munosabat: " + rel + " (" + RelationBands.Name(band) + ")", new Vector2(x, y), rc, 2, 1);
                y += 10;
            }

            foreach (var (label, action, rect) in _actions)
            {
                Color bc = action == 5 ? Red : Border;
                Panel(sb, rect, PanelBg, bc, 1);
                _font.DrawCentered(sb, label, rect.Center.ToVector2(), action == 5 ? Red : Cream, 2, 1);
            }
        }

        private void DrawDashboard(SpriteBatch sb, int x, int y)
        {
            Kingdom k = W.PlayerKingdom;
            _font.Draw(sb, "HUKMDOR PANELI", new Vector2(x, y), Gold, 3, 1); y += 28;
            string[] lines =
            {
                "Saltanat: " + k.Name,
                "Hukmdor: " + k.RulerName,
                "Poytaxt: Asaka",
                "Xazina: " + _state.Coins + " tanga",
                "Aholi: " + (_state.Population + W.PlayerExtraPopulation()),
                "Hududlar: " + k.RegionCount,
                "Iqtisod: " + k.EconomicStrength + "   Barqarorlik: " + k.Stability,
                "Obro': " + k.Reputation + "   Texnologiya: " + k.TechnologyLevel,
                "Faol savdo: " + W.Trade.ActiveCount,
            };
            foreach (string l in lines) { _font.Draw(sb, l, new Vector2(x, y), Cream, 2, 1); y += 21; }

            y += 10;
            _font.Draw(sb, "DIPLOMATIYA", new Vector2(x, y), Gold, 2, 1); y += 20;
            foreach (Kingdom other in W.Kingdoms.Kingdoms)
            {
                if (other.Id == k.Id) continue;
                int rel = W.Diplomacy.Get(k.Id, other.Id);
                bool ally = W.Diplomacy.AreAllied(k.Id, other.Id);
                Color rc = rel >= 20 ? Green : rel <= -20 ? Red : Muted;
                _font.Draw(sb, other.Name + ": " + rel + (ally ? " (Ittifoqchi)" : ""), new Vector2(x, y), rc, 2, 1);
                y += 21;
            }
            y += 6;
            _font.Draw(sb, "Hududni tanlab, diplomatiya qiling.", new Vector2(x, y), Muted, 2, 1);
        }

        // ------------------------------------------------------------------
        private void Fill(SpriteBatch sb, Rectangle r, Color c) => sb.Draw(_px, r, c);

        private void Panel(SpriteBatch sb, Rectangle r, Color bg, Color border, int t)
        {
            Fill(sb, r, bg);
            Fill(sb, new Rectangle(r.X, r.Y, r.Width, t), border);
            Fill(sb, new Rectangle(r.X, r.Bottom - t, r.Width, t), border);
            Fill(sb, new Rectangle(r.X, r.Y, t, r.Height), border);
            Fill(sb, new Rectangle(r.Right - t, r.Y, t, r.Height), border);
        }

        private void DrawLine(SpriteBatch sb, Vector2 a, Vector2 b, Color c, int thickness)
        {
            Vector2 d = b - a;
            float len = d.Length();
            if (len < 0.01f) return;
            float ang = (float)Math.Atan2(d.Y, d.X);
            sb.Draw(_px, a, null, c, ang, new Vector2(0, 0.5f), new Vector2(len, thickness), SpriteEffects.None, 0f);
        }

        private void DrawDisc(SpriteBatch sb, Vector2 c, float radius, Color col)
        {
            int r = (int)radius;
            for (int dy = -r; dy <= r; dy++)
            {
                int dx = (int)MathF.Sqrt(r * r - dy * dy);
                sb.Draw(_px, new Rectangle((int)c.X - dx, (int)c.Y + dy, dx * 2, 1), col);
            }
        }

        private List<string> Wrap(string text, int maxPx, int scale)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            string[] words = text.Split(' ');
            string cur = "";
            foreach (string word in words)
            {
                string test = cur.Length == 0 ? word : cur + " " + word;
                if (_font.Measure(test, scale, 1).X > maxPx && cur.Length > 0) { lines.Add(cur); cur = word; }
                else cur = test;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }
    }
}
