using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Online;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Shared.War;
using AndijonSaltanatlar.Shared.Farming;
using AndijonSaltanatlar.UI.Design;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AndijonSaltanatlar.UI
{
    /// <summary>
    /// PROMPT 19A/19B — online o'yin oynasi yagona dizayn tizimida (ThemeManager + Typography + UiKit):
    ///   KIRISH (server, login, dunyo tanlash) · DALALAR · BOZOR · KARVONLAR · SALTANAT · VAZIR.
    /// PC: markazdagi oyna, chapda navigatsiya, hover va tooltip. Telefon (sensor yoki tor ekran): to'liq ekran,
    /// pastki navigatsiya, katta tugmalar va kattaroq shrift — PC interfeysi shunchaki kichraytirilmaydi.
    /// Panel hech narsani o'zi hisoblamaydi: barcha raqamlar server holatidan; har amal — server buyrug'i.
    /// </summary>
    public sealed partial class OnlinePanel
    {
        private enum Section { Fields, Market, Caravans, Kingdom, War, Vizier }
        private static readonly string[] SectionNames = { "Dalalar", "Bozor", "Karvonlar", "Saltanat", "Harbiy", "Vazir" };
        private static readonly string[] MarketTabs = { "Sotib olish", "Sotish", "Takliflarim", "Savdo tarixi" };
        private static readonly string[] KingdomTabs = { "Umumiy", "Shaharlar", "Hududlar", "Aholi", "Iqtisodiyot", "Diplomatiya", "Hokimlar", "Bayroq" };

        private readonly AssetManager _assets;
        private readonly OnlineClient _client;
        private readonly UiKit _ui;
        private InputManager _input;

        private Section _section = Section.Fields;
        private Section _sectionAfterLogin = Section.Fields;
        private int _marketTab, _kingdomTab, _diplTab;
        private string _server = "", _user = "", _pass = "";
        private int _worldId = 1;
        private string _crop = "Rice";
        private string _sellProduct = "Rice";
        private int _qty = 100, _price = 500;
        private MarketOfferDto _confirm;
        private int _selectedRegion = -1, _selectedCity = -1;
        private int _lastSerial;
        private float _toastTimer;
        private string _toast = "";
        private bool _toastError;
        private bool _wasLoggedIn;
        private volatile string _pendingKeyboard;
        private string _pendingKeyboardField;
        private int _seenTerritorySerial;
        private float _territoryPulse;

        /// <summary>Sinov: namunaviy jang (--battle-demo). null bo'lsa oddiy rejim.</summary>
        public static BattleDto DemoBattle;

        public static BattleDto BuildDemoBattle() => new BattleDto
        {
            Id = 9001, Status = BattleStatus.InProgress, Round = 2, RegionName = "Samarqand", IsPvp = true,
            AttackerName = "Urgut sultonligi", DefenderName = "Buxoro amirligi", AttackerCommander = "Sherzod", DefenderCommander = "Nodirbek",
            AttackerColor = new[] { 214, 112, 40 }, DefenderColor = new[] { 120, 60, 40 }, AttackerFlag = "1-8-0-0-3-6", DefenderFlag = "10-2-6-1-3-0",
            AttackerStartPower = 160, DefenderStartPower = 150, AttackerPower = 140, DefenderPower = 118, AttackerMorale = 74, DefenderMorale = 61,
            IAmAttacker = true, IAmParticipant = true, RoundSecondsLeft = 25,
            AttackerGroups = new List<UnitGroupDto>
            {
                new UnitGroupDto { Line = UnitLine.Piyoda, Level = 2, Count = 40 }, new UnitGroupDto { Line = UnitLine.Piyoda, Level = 1, Count = 30 },
                new UnitGroupDto { Line = UnitLine.Otliq, Level = 1, Count = 15 }, new UnitGroupDto { Line = UnitLine.Masofaviy, Level = 1, Count = 20 }
            },
            DefenderGroups = new List<UnitGroupDto>
            {
                new UnitGroupDto { Line = UnitLine.Piyoda, Level = 3, Count = 35 }, new UnitGroupDto { Line = UnitLine.Piyoda, Level = 1, Count = 30 },
                new UnitGroupDto { Line = UnitLine.Masofaviy, Level = 1, Count = 18 }
            },
            Rounds = new List<BattleRoundDto> { new BattleRoundDto { Round = 1, AttackerDecision = BattleDecision.Faol, DefenderDecision = BattleDecision.Muvozanatli, AttackerLoss = 10, DefenderLoss = 22 } }
        };

        public bool IsOpen { get; private set; }
        public bool WantsKeyboard => IsOpen && _ui.FocusId != null;

        public OnlinePanel(AssetManager assets, PixelFont font, OnlineClient client, UiKit ui)
        {
            _assets = assets;
            _client = client;
            _ui = ui;
            _server = client.Settings.ServerUrl;
            _user = client.Settings.Username;
            _worldId = Math.Max(1, client.Settings.WorldId);
        }

        public void Open()
        {
            IsOpen = true;
            if (_client.LoggedIn) _client.QueueRefresh();
        }

        public void Close() { IsOpen = false; FullMap = false; _ui.FocusId = null; _confirm = null; _dipMode = 0; _chatOpen = false; _client.ChatOpen = false; }

        /// <summary>To'liq ekranli dunyo xaritasi (M): menyu va sarlavhasiz, butun ekran — xarita.</summary>
        public bool FullMap { get; private set; }

        /// <summary>M tugmasi: katta dunyo xaritasi — kimning yeri va qasri qayerda (ma'lumot har ochilganda yangilanadi).</summary>
        public void ToggleWorldMap()
        {
            if (IsOpen && FullMap) { Close(); return; }
            ShowTab("warmap");
            FullMap = true;
            _warRequested = false;
            if (_client.LoggedIn) { _client.LoadWar(); _client.LoadLetters(); }
            Open();
        }
        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void Prefill(string server, string user, string pass)
        {
            if (!string.IsNullOrEmpty(server)) _server = server;
            if (!string.IsNullOrEmpty(user)) _user = user;
            if (!string.IsNullOrEmpty(pass)) _pass = pass;
        }

        public void LoginNow(bool register)
        {
            _client.Settings.WorldId = _worldId;
            _client.Login(_server, _user, _pass, register);
        }

        /// <summary>Test/boshlang'ich bo'lim: rice|sell|buy|market|caravans|kingdom|regions|cities|governors|vizier ...</summary>
        public void ShowTab(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "rice": case "guruch": case "fields": _sectionAfterLogin = Section.Fields; break;
                case "buy": _sectionAfterLogin = Section.Market; _marketTab = 0; break;
                case "sell": _sectionAfterLogin = Section.Market; _marketTab = 1; break;
                case "offers": _sectionAfterLogin = Section.Market; _marketTab = 2; break;
                case "history": _sectionAfterLogin = Section.Market; _marketTab = 3; break;
                case "caravans": _sectionAfterLogin = Section.Caravans; break;
                case "kingdom": _sectionAfterLogin = Section.Kingdom; _kingdomTab = 0; break;
                case "cities": _sectionAfterLogin = Section.Kingdom; _kingdomTab = 1; break;
                case "regions": case "map": _sectionAfterLogin = Section.Kingdom; _kingdomTab = 2; break;
                case "population": _sectionAfterLogin = Section.Kingdom; _kingdomTab = 3; break;
                case "economy": _sectionAfterLogin = Section.Kingdom; _kingdomTab = 4; break;
                case "diplomacy": _sectionAfterLogin = Section.Kingdom; _kingdomTab = 5; break;
                case "governors": _sectionAfterLogin = Section.Kingdom; _kingdomTab = 6; break;
                case "flag": case "bayroq": _sectionAfterLogin = Section.Kingdom; _kingdomTab = 7; break;
                case "start-flag": _startStep = 1; _startRegion = -2; break; // sinov: yurt tanlashning 2-qadami
                case "vizier": case "vazir": _sectionAfterLogin = Section.Vizier; break;
                case "war": case "harbiy": case "warmap": _sectionAfterLogin = Section.War; _warTab = 0; break;
                case "fullmap": _sectionAfterLogin = Section.War; _warTab = 0; FullMap = true; break;
                case "armies": _sectionAfterLogin = Section.War; _warTab = 1; break;
                case "army-units": _sectionAfterLogin = Section.War; _warTab = 3; break;
                case "conflicts": _sectionAfterLogin = Section.War; _warTab = 5; break;
                case "pvp": _sectionAfterLogin = Section.War; _warTab = 6; break;
                case "battle": _sectionAfterLogin = Section.War; _warTab = 1; _openActiveBattle = true; break;
            }
            _section = _sectionAfterLogin;
        }

        /// <summary>Eski API bilan moslik: "ONLINE" tugmasi endi Draw ichida (UiKit) chiziladi va bosiladi.</summary>
        public bool UpdateLauncher(InputManager input, int vw) => false;
        public void DrawLauncher(SpriteBatch sb, int vw) { }

        // ------------------------------------------------------------------
        // Update: tarmoq natijalari, toast, klaviatura
        // ------------------------------------------------------------------
        public void Update(InputManager input, int vw, int vh, float dt)
        {
            _input = input;
            _client.Pump();
            UpdateSiegeMarch();
            if (_client.MessageSerial != _lastSerial) { _lastSerial = _client.MessageSerial; ShowToast(_client.Message, _client.MessageIsError); }
            if (_client.Notification != null) { ShowToast(_client.Notification, false); _client.Notification = null; }
            if (_toastTimer > 0f) _toastTimer -= dt;
            if (_territoryPulse > 0f) _territoryPulse -= dt;
            if (_client.TerritorySerial != _seenTerritorySerial) { _seenTerritorySerial = _client.TerritorySerial; _territoryPulse = 4f; }

            if (_client.LoggedIn && !_wasLoggedIn) { _section = _sectionAfterLogin; _pass = ""; _ui.FocusId = null; }
            _wasLoggedIn = _client.LoggedIn;

            string kb = _pendingKeyboard;
            if (kb != null && _pendingKeyboardField != null)
            {
                if (_pendingKeyboardField == "server") _server = kb; else if (_pendingKeyboardField == "user") _user = kb; else if (_pendingKeyboardField == "pass") _pass = kb;
                else if (_pendingKeyboardField == "kname") _nameDraft = kb;
                else if (_pendingKeyboardField == "letter") _letterDraft = kb;
                else if (_pendingKeyboardField == "chat") _chatDraft = kb;
                _pendingKeyboard = null; _pendingKeyboardField = null;
            }

            if (_client.OpenBattleRequest != 0)
            {
                int bid = _client.OpenBattleRequest;
                _client.OpenBattleRequest = 0;
                IsOpen = true;
                _section = Section.War;
                OpenBattle(bid);
            }
            // Yangi o'yinchi: yurt tanlanmaguncha oyna ochiq turadi (xaritadan boshlang'ich hududni tanlash).
            if (_client.LoggedIn && _client.State?.NeedsHome == true && !IsOpen) IsOpen = true;
            if (!IsOpen) return;
            input.MouseOverUI = true;
            if (BattleOpen && input.KeyPressed(Keys.Escape)) { _battleOpen = false; return; }
            if (input.KeyPressed(Keys.Escape))
            {
                if (_confirm != null) _confirm = null;
                else if (_ui.FocusId != null) _ui.FocusId = null;
                else if (_chatOpen) CloseChat();
                else if (_dipMode != 0) { _dipMode = 0; _client.CloseVisit(); }
                else if (_client.State?.NeedsHome == true) { if (_startStep == 1) _startStep = 0; }
                else Close();
            }
            if (!_client.LoggedIn && _ui.FocusId != null)
            {
                if (input.KeyPressed(Keys.Tab)) _ui.FocusId = _ui.FocusId == "server" ? "user" : _ui.FocusId == "user" ? "pass" : "server";
                if (input.KeyPressed(Keys.Enter)) { _ui.FocusId = null; LoginNow(false); }
            }
            else if (_ui.FocusId == null) input.ClearText();
        }

        private void ShowToast(string text, bool error)
        {
            if (string.IsNullOrEmpty(text)) return;
            _toast = text; _toastError = error; _toastTimer = 4.5f;
        }

        private void AndroidKeyboard(string field, string title, string value, bool password)
        {
#if ANDROID
            Task<string> t = KeyboardInput.Show(title, "", value, password);
            t.ContinueWith(r => { if (r.Status == TaskStatus.RanToCompletion && r.Result != null) { _pendingKeyboardField = field; _pendingKeyboard = r.Result; } });
#endif
        }

        // ------------------------------------------------------------------
        // Draw (immediate-mode: hit-test + render bitta joyda)
        // ------------------------------------------------------------------
        public void Draw(SpriteBatch sb, int vw, int vh, float time)
        {
            if (_input == null && DemoBattle != null) _input = new InputManager();
            if (_input == null) return;
            _sbCache = sb;
            UiKit ui = _ui;
            ui.Begin(sb, _input, vw, vh, time);
            ThemeTokens T = ui.T;

            // Sinov (--battle-demo): jang sahnasi namunaviy qo'shinlar bilan (serverga ulanishsiz)
            if (DemoBattle != null)
            {
                float ddt = _lastDrawTime < 0 ? 0f : Math.Clamp(time - _lastDrawTime, 0f, 0.1f);
                _lastDrawTime = time;
                ui.Fill(new Rectangle(0, 0, vw, vh), T.Background);
                _battleScene ??= new BattleScene(_assets);
                BattleScene.Bind(sb);
                _battleScene.Draw(ui, sb, DemoBattle, vw, vh, ddt, false);
                ui.End();
                return;
            }

            if (!IsOpen)
            {
                // "ONLINE" tugmasi endi UIManager yuqori panelida (joylashuvi ekran eniga moslashadi).
                ui.Toast(_toast, _toastError, Math.Min(1f, _toastTimer));
                ui.End();
                return;
            }

            float dt = _lastDrawTime < 0 ? 0f : Math.Clamp(time - _lastDrawTime, 0f, 0.1f);
            _lastDrawTime = time;
            if (BattleOpen && _client.LoggedIn)
            {
                DrawBattleOverlay(vw, vh, dt);
                ui.Toast(_toast, _toastError, Math.Min(1f, _toastTimer));
                ui.End();
                return;
            }

            if (FullMap && _client.LoggedIn && _client.State != null && !_client.State.NeedsHome)
            {
                DrawFullMap(vw, vh);
                DrawDiplomacyOverlay(vw, vh);
                DrawChatOverlay(vw, vh);
                ui.Toast(_toast, _toastError, Math.Min(1f, _toastTimer));
                ui.End();
                return;
            }
            ui.Fill(new Rectangle(0, 0, vw, vh), T.Scrim);
            Rectangle win = ui.Compact ? new Rectangle(0, 0, vw, vh) : new Rectangle((vw - Math.Min(vw - 40, 1240)) / 2, (vh - Math.Min(vh - 40, 690)) / 2, Math.Min(vw - 40, 1240), Math.Min(vh - 40, 690));
            if (ui.Compact) ui.Fill(win, T.Background); else ui.Panel(win);

            if (!_client.LoggedIn) DrawLogin(win);
            else DrawApp(win);

            if (_confirm != null) DrawConfirm();
            DrawDiplomacyOverlay(vw, vh);
            DrawChatOverlay(vw, vh);
            ui.Toast(_toast, _toastError, Math.Min(1f, _toastTimer));
            ui.End();
        }

        // ------------------------------------------------------------------
        // KIRISH + DUNYO TANLASH
        // ------------------------------------------------------------------
        /// <summary>Google orqali qayta ulanish so'raldi (Game1 CloudSession tokeni bilan qayta ulaydi).</summary>
        public Action ReconnectRequested;

        /// <summary>M — butun ekran: O'zbekiston xaritasi, har saltanatning yerlari (rangi) va qasrlari (bayroq bilan).</summary>
        private void DrawFullMap(int vw, int vh)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            int h = ui.ControlH, pad = ui.Compact ? 8 : 14;
            ui.Fill(new Rectangle(0, 0, vw, vh), T.Background);
            int headH = (int)ui.Type.LineHeight(TextStyle.ScreenTitle) + 14;
            ui.Text("DUNYO XARITASI", new Vector2(pad, pad), TextStyle.ScreenTitle, T.Accent);
            string hint = "Qasrni bosing — egasi  ·  M — yopish";
            // Xatlar qutisi va chat tugmalari (o'qilmaganlar soni bilan)
            int unread = _client.UnreadLetters;
            int inboxW = (int)ui.Type.Measure("Xatlar (00)", TextStyle.Button).X + 28;
            var inboxBtn = new Rectangle(vw - pad - h - 8 - inboxW, pad, inboxW, h);
            if (ui.Button(inboxBtn, "Xatlar" + (unread > 0 ? " (" + unread + ")" : ""), unread > 0 ? ButtonKind.Accent : ButtonKind.Secondary))
            { _dipMode = 4; _client.LoadLetters(); }
            int uc = _client.UnreadChat;
            int chatW = (int)ui.Type.Measure("Chat (00)", TextStyle.Button).X + 28;
            var chatBtn = new Rectangle(inboxBtn.X - 8 - chatW, pad, chatW, h);
            if (ui.Button(chatBtn, "Chat" + (uc > 0 ? " (" + uc + ")" : ""), uc > 0 ? ButtonKind.Accent : ButtonKind.Secondary)) OpenChat();
            ui.TextFit(hint, new Rectangle(pad + 330, pad + 6, chatBtn.X - (pad + 330) - 12, (int)ui.Type.LineHeight(TextStyle.Secondary)), TextStyle.Secondary, T.TextSecondary);
            if (ui.IconButton(new Rectangle(vw - pad - h, pad, h, h), "×", "Yopish (M)")) { Close(); return; }
            var body = new Rectangle(pad, pad + headH, vw - 2 * pad, vh - 2 * pad - headH);
            WarOverviewDto w = _client.War;
            if (w == null)
            {
                if (!_warRequested) { _warRequested = true; _client.LoadWar(); }
                ui.Paragraph("Xarita yuklanmoqda…", body, TextStyle.Body);
                return;
            }
            DrawWarMap(body, w);
        }

        /// <summary>
        /// Kirish endi FAQAT Google orqali (o'yin boshida). Bu yerda login/parol formasi yo'q — faqat
        /// onlayn dunyoga ulanish holati va "Qayta ulanish" tugmasi.
        /// </summary>
        private void DrawLogin(Rectangle win)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            int h = ui.ControlH, gap = ui.Gap;
            if (ui.IconButton(new Rectangle(win.Right - h - 8, win.Y + 8, h, h), "×", "Yopish")) { Close(); return; }

            int cw = Math.Min(win.Width - 32, 520);
            int x = win.X + (win.Width - cw) / 2;
            int y = win.Y + Math.Max(20, (win.Height - (3 * h + 4 * gap + 120)) / 2);
            ui.Text("ANDIJON: TAXTLAR JANGI", new Vector2(x, y), TextStyle.ScreenTitle, T.Accent);
            y += (int)ui.Type.LineHeight(TextStyle.ScreenTitle) + 4;
            string status = _client.Busy ? "Onlayn dunyoga ulanmoqda…" : "Onlayn dunyoga ulanilmagan";
            y += (int)ui.Paragraph(status, new Rectangle(x, y, cw, 60), TextStyle.Body, _client.Busy ? T.Accent : T.TextPrimary) + gap;
            if (!string.IsNullOrEmpty(_client.Message) && _client.MessageIsError)
                y += (int)ui.Paragraph(_client.Message, new Rectangle(x, y, cw, 80), TextStyle.Secondary, T.Danger) + gap;
            if (ui.Button(new Rectangle(x, y, cw, h), "Qayta ulanish", ButtonKind.Primary, !_client.Busy))
                ReconnectRequested?.Invoke();
        }

        // ------------------------------------------------------------------
        // ASOSIY ILOVA: sarlavha + navigatsiya + bo'lim
        // ------------------------------------------------------------------
        private void DrawApp(Rectangle win)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            OnlineStateDto s = _client.State;
            int h = ui.ControlH;
            int pad = ui.Compact ? 12 : 20;
            if (s.NeedsHome) { DrawStartChooser(win); return; }

            // Sarlavha
            int headH = (int)ui.Type.LineHeight(TextStyle.ScreenTitle) + 18;
            var head = new Rectangle(win.X + pad, win.Y + 10, win.Width - 2 * pad, headH);
            string title = _section == Section.War ? "Harbiy boshqaruv" : _section == Section.Market ? "Saltanatlar bozori" : _section == Section.Kingdom ? "Saltanat boshqaruvi" : _section == Section.Caravans ? "Savdo karvonlari" : _section == Section.Vizier ? "Donishmand Vazir" : "Ekin dalalari";
            ui.Text(title, new Vector2(head.X, head.Y + 4), TextStyle.ScreenTitle);
            if (ui.IconButton(new Rectangle(head.Right - h, head.Y, h, h), "×", "Yopish (Esc)")) { Close(); return; }
            if (!ui.Compact)
            {
                float rx = head.Right - h - 16;
                string[] vals = { s.Coin.ToString(), s.Population.ToString(), s.CityCount.ToString() };
                string[] labs = { "tanga", "aholi", "shahar" };
                Color[] dots = { T.Accent, T.Secondary, T.Success };
                for (int i = 2; i >= 0; i--)
                {
                    float w = 18 + ui.Type.Measure(vals[i], TextStyle.Body).X + 6 + ui.Type.Measure(labs[i], TextStyle.Secondary).X + 18;
                    rx -= w;
                    ui.ResourceCounter(new Vector2(rx, head.Y + 10), dots[i], vals[i], labs[i]);
                }
            }

            // Navigatsiya: PC — chapda, telefon — pastda
            Rectangle content;
            int navIndex = (int)_section;
            if (ui.Compact)
            {
                int navH = h + 8;
                var nav = new Rectangle(win.X, win.Bottom - navH, win.Width, navH);
                ui.Fill(nav, T.Surface);
                ui.Fill(new Rectangle(nav.X, nav.Y, nav.Width, 1), T.Border);
                navIndex = ui.NavList(new Rectangle(nav.X + 6, nav.Y + 4, nav.Width - 12, h), SectionNames, navIndex, true);
                int rl = (int)ui.Type.LineHeight(TextStyle.Secondary);
                // Telefonda resurslar sarlavha ostida, bitta qatorda (kontent undan keyin boshlanadi — ustma-ust tushmaydi)
                ui.TextFit(s.Coin + " tanga · " + s.Population + " aholi · " + s.CityCount + " shahar", new Rectangle(head.X, head.Bottom, head.Width, rl), TextStyle.Secondary, T.TextSecondary);
                content = new Rectangle(win.X + pad, head.Bottom + rl + 8, win.Width - 2 * pad, nav.Y - head.Bottom - rl - 16);
            }
            else
            {
                var nav = new Rectangle(win.X + pad, head.Bottom + 12, 190, win.Bottom - head.Bottom - 70);
                navIndex = ui.NavList(nav, SectionNames, navIndex, false);
                string st = (_client.HubConnected ? "● Ulangan" : "○ Real vaqt yo'q") + "\n" + _client.Settings.ServerUrl.Replace("https://", "").Replace("http://", "");
                ui.Paragraph(st, new Rectangle(nav.X + 4, win.Bottom - 60, 190, 50), TextStyle.Caption, _client.HubConnected ? T.Success : T.Warning);
                content = new Rectangle(nav.Right + 20, head.Bottom + 12, win.Right - nav.Right - 20 - pad, win.Bottom - head.Bottom - 32);
                ui.Fill(new Rectangle(nav.Right + 10, head.Bottom + 12, 1, content.Height), T.Border);
            }
            if (navIndex != (int)_section) { _section = (Section)navIndex; _client.QueueRefresh(); }

            switch (_section)
            {
                case Section.Fields: DrawFields(content); break;
                case Section.Market: DrawMarket(content); break;
                case Section.Caravans: DrawCaravans(content); break;
                case Section.Kingdom: DrawKingdom(content); break;
                case Section.War: DrawWar(content); break;
                case Section.Vizier: DrawVizier(content); break;
            }
        }

        private static Color ProductColor(string key)
        {
            switch (key)
            {
                case "Wheat": return new Color(214, 176, 92);
                case "Cotton": return new Color(232, 232, 238);
                case "Fruit": return new Color(214, 88, 76);
                case "Vegetable": return new Color(110, 170, 80);
                default: return new Color(240, 226, 190); // guruch
            }
        }

        private void ProductIcon(Rectangle r, string key)
        {
            UiKit ui = _ui;
            ui.Rounded(r, Color.Lerp(ui.T.SurfaceAlt, ProductColor(key), 0.35f), 8);
            ui.Rounded(new Rectangle(r.Center.X - r.Width / 5, r.Center.Y - r.Height / 5, r.Width * 2 / 5, r.Height * 2 / 5), ProductColor(key), r.Width / 5);
        }

        private static string ProductName(string key) => CropCatalog.NameOf(key);

        // ------------------------------------------------------------------
        // DALALAR
        // ------------------------------------------------------------------
        private void DrawFields(Rectangle c)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T; OnlineStateDto s = _client.State;
            int h = ui.ControlH, gap = ui.Gap;
            int active = s.Fields.Count(f => f.State == RiceFieldState.Planted || f.State == RiceFieldState.Growing || f.State == RiceFieldState.Mature);
            int cap = s.WaterSources * RiceFarmingRules.FieldsPerWaterSource;
            int lh = (int)ui.Type.LineHeight(TextStyle.Secondary);
            ui.TextFit("Suv: " + s.WaterSources + " ariq · " + active + "/" + cap + " maydon sug'orilmoqda · Maydonlar " + s.Fields.Count + "/" + RiceFarmingRules.MaxFieldsPerKingdom,
                new Rectangle(c.X, c.Y, c.Width, lh), TextStyle.Secondary, active >= cap ? T.Warning : T.TextSecondary);
            int y = c.Y + lh + 6;
            int bw = Math.Min((int)ui.Type.Measure("Maydon qurish · 100", TextStyle.Button).X + 32, (c.Width - gap) / 2);
            if (ui.Button(new Rectangle(c.X, y, bw, h), "Maydon qurish · " + RiceFarmingRules.FieldBuildCost, ButtonKind.Secondary,
                !_client.Busy && s.Fields.Count < RiceFarmingRules.MaxFieldsPerKingdom && s.Coin >= RiceFarmingRules.FieldBuildCost, "Yangi ekin maydoni (" + RiceFarmingRules.FieldBuildCost + " tanga)"))
                _client.BuildField();
            if (ui.Button(new Rectangle(c.X + bw + gap, y, bw, h), "Ariq qazish · 150", ButtonKind.Secondary, !_client.Busy && s.Coin >= 150, "Har bir ariq 4 ta maydonni sug'oradi"))
                _client.BuildWater();
            y += h + gap;

            int chipH = h - 8;
            int labelW = (int)ui.Type.Measure("Ekin:", TextStyle.Secondary).X + 12;
            ui.TextFit("Ekin:", new Rectangle(c.X, y, labelW, chipH), TextStyle.Secondary, T.TextSecondary);
            int cx = c.X + labelW;
            foreach (CropDef d in CropCatalog.All)
            {
                string label = d.NameUz + " · " + d.MatureAtMinutes / 60 + " daq";
                int w = Math.Min(c.Width - labelW, (int)ui.Type.Measure(label, TextStyle.Secondary).X + 40);
                if (cx + w > c.Right) { cx = c.X + labelW; y += chipH + 6; }
                if (ui.Chip(new Rectangle(cx, y, w, chipH), label, _crop == d.Key, ProductColor(d.Key))) _crop = d.Key;
                cx += w + 8;
            }
            y += chipH + gap;

            if (s.Fields.Count == 0)
            {
                ui.Paragraph("Hali ekin maydoni yo'q. «Maydon qurish» tugmasini bosing — so'ng ekin tanlab, ekishingiz mumkin.", new Rectangle(c.X, y + 10, c.Width, 80), TextStyle.Body);
                return;
            }
            int cols = Math.Max(1, Math.Min(3, c.Width / 280));
            int cw = (c.Width - (cols - 1) * gap) / cols;
            int rows = (s.Fields.Count + cols - 1) / cols;
            int ch = Math.Max(180, Math.Min(230, (c.Bottom - y - (rows - 1) * gap) / Math.Max(1, rows)));
            var view = new Rectangle(c.X, y, c.Width, c.Bottom - y);
            int scroll = ui.Scroll("fields", view, rows * (ch + gap));
            long now = _client.EstimatedGameMinute;
            for (int i = 0; i < s.Fields.Count; i++)
            {
                RiceFieldDto f = s.Fields[i];
                var card = new Rectangle(c.X + (i % cols) * (cw + gap), y + (i / cols) * (ch + gap) - scroll, cw, ch);
                if (!UiKit.InView(card, view)) continue;
                CropType crop = CropCatalog.TryParse(f.Crop, out CropType ct) ? ct : CropType.Rice;
                RiceFieldState stored = f.State == RiceFieldState.Growing || f.State == RiceFieldState.Mature ? RiceFieldState.Planted : f.State;
                RiceFieldState st = CropCatalog.Evaluate(crop, stored, f.PlantedAtGameMinute, now);
                float progress = CropCatalog.Progress(crop, stored, f.PlantedAtGameMinute, now);
                ui.Card(card, st == RiceFieldState.Mature);
                string name = st == RiceFieldState.Empty ? "Bo'sh maydon" : CropCatalog.Get(crop).NameUz;
                ui.TextFit((i + 1) + "-maydon · " + name, new Rectangle(card.X + 12, card.Y + 8, card.Width - 24, 24), TextStyle.PanelTitle);
                ui.Badge(new Vector2(card.X + 12, card.Y + 38), RiceFarmingRules.StateNameUz(st), st == RiceFieldState.Mature ? T.Accent : st == RiceFieldState.Empty || st == RiceFieldState.Harvested ? T.Secondary : T.Success);
                if (st == RiceFieldState.Planted || st == RiceFieldState.Growing)
                    ui.Type.DrawRight(_sbOf(), CropCatalog.SecondsToMature(crop, f.PlantedAtGameMinute, now) + " s", new Vector2(card.Right - 12, card.Y + 40), TextStyle.Secondary, T.TextSecondary);
                var plot = new Rectangle(card.X + 12, card.Y + 64, card.Width - 24, card.Height - 64 - h - 28);
                DrawPaddy(plot, st, progress, ui.Time + i * 0.7f, ProductColor(crop == CropType.Rice ? "Wheat" : CropCatalog.Get(crop).Key));
                ui.ProgressBar(new Rectangle(card.X + 12, plot.Bottom + 8, card.Width - 24, 6), progress, st == RiceFieldState.Mature ? T.Accent : T.Success);
                var br = new Rectangle(card.X + 12, card.Bottom - h - 10, card.Width - 24, h);
                int fid = f.Id;
                if (RiceFarmingRules.CanHarvest(st)) { if (ui.Button(br, "Hosilni yig'ish", ButtonKind.Accent, !_client.Busy)) _client.Harvest(fid); }
                else if (RiceFarmingRules.CanPlant(st))
                {
                    CropDef d = CropCatalog.Get(CropCatalog.TryParse(_crop, out CropType sel) ? sel : CropType.Rice);
                    if (ui.Button(br, d.NameUz + " ekish · " + d.SeedCost, ButtonKind.Primary, !_client.Busy && s.Coin >= d.SeedCost)) _client.PlantCrop(fid, d.Key);
                }
                else ui.Button(br, "O'smoqda…", ButtonKind.Secondary, false);
            }
        }

        /// <summary>Qator balandligi (piksel shrift o'lchamiga qarab) — qattiq raqamlar o'rniga.</summary>
        private int LH(TextStyle st) => (int)_ui.Type.LineHeight(st);
        /// <summary>Tugma eni: yorliq to'liq sig'adigan qilib, maksimum bilan.</summary>
        private int MeasureBtn(string label, int max) => Math.Min(max, (int)_ui.Type.Measure(label, TextStyle.Button).X + 36);

        private SpriteBatch _sbCache;
        private float _lastDrawTime = -1f;
        private SpriteBatch _sbOf() => _sbCache;

        /// <summary>Dala: suvli tuproq + bosqichga mos o'simliklar (shamolda tebranadi).</summary>
        private void DrawPaddy(Rectangle r, RiceFieldState st, float progress, float time, Color ripe)
        {
            UiKit ui = _ui;
            if (r.Height < 10) return;
            bool watered = st == RiceFieldState.Planted || st == RiceFieldState.Growing || st == RiceFieldState.Mature;
            ui.Rounded(r, new Color(96, 70, 48), 6);
            if (watered)
            {
                ui.Rounded(new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6), new Color(66, 108, 138), 5);
                int glint = (int)((time * 30) % Math.Max(1, r.Width - 24));
                ui.Fill(new Rectangle(r.X + 6 + glint, r.Y + r.Height / 3, 16, 2), new Color(150, 200, 225, 150));
            }
            if (st == RiceFieldState.Empty) return;
            int cols = Math.Max(4, r.Width / 16), rows = Math.Max(2, r.Height / 22);
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    float px = r.X + 10 + i * (r.Width - 20) / (float)Math.Max(1, cols - 1);
                    float baseY = r.Y + 18 + j * (r.Height - 24) / (float)Math.Max(1, rows - 1);
                    if (st == RiceFieldState.Harvested) { ui.Fill(new Rectangle((int)px, (int)baseY - 3, 2, 3), new Color(176, 150, 90)); continue; }
                    float hgt = 4f + progress * 20f;
                    float sway = MathF.Sin(time * 2.2f + i * 0.6f + j) * (1.5f + progress * 2.5f);
                    Color stalk = st == RiceFieldState.Mature ? ripe : Color.Lerp(new Color(110, 180, 90), new Color(70, 140, 60), progress);
                    for (int k = 0; k < (int)hgt; k += 2)
                        ui.Fill(new Rectangle((int)(px + sway * (k / Math.Max(1f, hgt))), (int)(baseY - k), 2, 2), stalk);
                    if (st == RiceFieldState.Mature) ui.Fill(new Rectangle((int)(px + sway) - 1, (int)(baseY - hgt) - 3, 4, 4), Color.Lerp(ripe, Color.White, 0.25f));
                }
        }

        // ------------------------------------------------------------------
        // BOZOR
        // ------------------------------------------------------------------
        private void DrawMarket(Rectangle c)
        {
            UiKit ui = _ui;
            int h = ui.ControlH;
            _marketTab = ui.TabBar(new Rectangle(c.X, c.Y, c.Width, h), MarketTabs, _marketTab);
            var body = new Rectangle(c.X, c.Y + h + ui.Gap + 4, c.Width, c.Height - h - ui.Gap - 4);
            switch (_marketTab)
            {
                case 0: DrawBuy(body); break;
                case 1: DrawSell(body); break;
                case 2: DrawMyOffers(body); break;
                default: DrawHistory(body); break;
            }
        }

        /// <summary>UITradeCard: mahsulot ikonkasi, nomi, miqdori, narxi, sotuvchi, yetkazish turi.</summary>
        private Rectangle TradeCard(Rectangle r, MarketOfferDto o, string extra)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            ui.Card(r);
            int icon = r.Height - 20;
            ProductIcon(new Rectangle(r.X + 10, r.Y + 10, icon, icon), o.Product);
            int tx = r.X + icon + 22;
            // O'lchamlar matnga qarab (piksel shrift keng): tugma va narx o'z joyiga sig'adi, matnlar ustma-ust tushmaydi.
            int rightW = (int)ui.Type.Measure("Bekor qilish", TextStyle.Button).X + 40;
            string price = o.TotalPrice + " tanga";
            int priceW = (int)ui.Type.Measure(price, TextStyle.PanelTitle).X + 16;
            int textW = Math.Max(60, r.Right - rightW - priceW - 20 - tx);
            int lt = (int)ui.Type.LineHeight(TextStyle.PanelTitle), ls = (int)ui.Type.LineHeight(TextStyle.Secondary);
            int top = r.Y + Math.Max(4, (r.Height - lt - ls - 4) / 2);
            ui.TextFit(o.Quantity + " " + ProductName(o.Product).ToLowerInvariant(), new Rectangle(tx, top, textW, lt), TextStyle.PanelTitle);
            ui.TextFit(o.SellerName + " · " + (o.TotalPrice / (float)Math.Max(1, o.Quantity)).ToString("0.##") + " tanga/birlik · " + extra,
                new Rectangle(tx, top + lt + 4, textW, ls), TextStyle.Secondary, T.TextSecondary);
            ui.Type.DrawFitted(_sbOf(), price, new Rectangle(r.Right - rightW - priceW - 6, r.Y, priceW, r.Height), TextStyle.PanelTitle, T.Accent, 2);
            int bh = Math.Min(ui.ControlH, r.Height - 12);
            return new Rectangle(r.Right - rightW, r.Y + (r.Height - bh) / 2, rightW - 10, bh);
        }

        private void DrawBuy(Rectangle c)
        {
            UiKit ui = _ui; OnlineStateDto s = _client.State;
            var list = _client.Offers.Where(o => !o.IsMine).ToList();
            if (list.Count == 0) { ui.Paragraph("Hozircha boshqa saltanatlardan taklif yo'q.", c, TextStyle.Body); return; }
            int ch = Math.Max(ui.ControlH + 24, 76), gap = ui.Gap;
            int scroll = ui.Scroll("buy", c, list.Count * (ch + gap));
            for (int i = 0; i < list.Count; i++)
            {
                var r = new Rectangle(c.X, c.Y + i * (ch + gap) - scroll, c.Width - 8, ch);
                if (!UiKit.InView(r, c)) continue;
                MarketOfferDto o = list[i];
                Rectangle br = TradeCard(r, o, "karvon orqali");
                if (ui.Button(br, "Sotib olish", ButtonKind.Primary, !_client.Busy && s.Coin >= o.TotalPrice, s.Coin >= o.TotalPrice ? null : "Tanga yetarli emas"))
                    _confirm = o;
            }
        }

        private void DrawConfirm()
        {
            UiKit ui = _ui; ThemeTokens T = ui.T; OnlineStateDto s = _client.State; MarketOfferDto o = _confirm;
            Rectangle m = ui.Modal(520, 330, "Savdoni tasdiqlash");
            int x = m.X + 20, y = m.Y + 70, w = m.Width - 40;
            ProductIcon(new Rectangle(x, y, 56, 56), o.Product);
            ui.Text(o.Quantity + " " + ProductName(o.Product).ToLowerInvariant(), new Vector2(x + 70, y + 2), TextStyle.PanelTitle);
            ui.Text("Sotuvchi: " + o.SellerName, new Vector2(x + 70, y + 30), TextStyle.Secondary, T.TextSecondary);
            y += 72;
            void Row(string a, string b, Color col) { ui.Text(a, new Vector2(x, y), TextStyle.Body, T.TextSecondary); ui.Type.DrawRight(_sbOf(), b, new Vector2(x + w, y), TextStyle.Body, col); y += 26; }
            Row("Sarflanadi", o.TotalPrice + " tanga", T.Accent);
            Row("Xazinada qoladi", (s.Coin - o.TotalPrice) + " tanga", s.Coin >= o.TotalPrice ? T.TextPrimary : T.Danger);
            Row("Yetkazish", "Karvon orqali (mol yo'lda — tanga rezervda)", T.TextPrimary);
            int bw = (w - ui.Gap) / 2, by = m.Bottom - ui.ControlH - 18;
            if (ui.Button(new Rectangle(x, by, bw, ui.ControlH), "Bekor qilish", ButtonKind.Secondary)) _confirm = null;
            else if (ui.Button(new Rectangle(x + bw + ui.Gap, by, bw, ui.ControlH), "Tasdiqlash", ButtonKind.Primary, !_client.Busy && s.Coin >= o.TotalPrice))
            { _client.Buy(o.Id); _confirm = null; }
        }

        private void DrawSell(Rectangle c)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T; OnlineStateDto s = _client.State;
            int h = ui.ControlH, gap = ui.Gap, y = c.Y;
            ui.TextFit("Mahsulot", new Rectangle(c.X, y, c.Width, LH(TextStyle.Secondary)), TextStyle.Secondary, T.TextSecondary); y += LH(TextStyle.Secondary) + 4;
            int cx = c.X;
            foreach (CropDef d in CropCatalog.All)
            {
                int have = s.Inventory.TryGetValue(d.Key, out int v) ? v : 0;
                string label = d.NameUz + " · " + have;
                int w = (int)ui.Type.Measure(label, TextStyle.Secondary).X + 44;
                if (cx + w > c.Right) { cx = c.X; y += h; }
                if (ui.Chip(new Rectangle(cx, y, w, h - 8), label, _sellProduct == d.Key, ProductColor(d.Key))) _sellProduct = d.Key;
                cx += w + 8;
            }
            y += h + gap;
            int stock = s.Inventory.TryGetValue(_sellProduct, out int st) ? st : 0;
            CropDef def = CropCatalog.Get(CropCatalog.TryParse(_sellProduct, out CropType ct) ? ct : CropType.Rice);

            y = Stepper(c.X, y, "Miqdor", ref _qty, new[] { -100, -10, 10, 100 }, 1, Math.Max(1, stock), " birlik");
            y = Stepper(c.X, y, "Umumiy narx", ref _price, new[] { -500, -50, 50, 500 }, MarketRules.MinPrice, MarketRules.MaxPrice, " tanga");
            float unit = _price / (float)Math.Max(1, _qty);
            ui.Paragraph("1 birlik = " + unit.ToString("0.##") + " tanga · tavsiya etilgan bozor narxi ≈ " + def.ReferencePrice + " tanga/birlik (" + def.ReferencePrice * _qty + " tanga)",
                new Rectangle(c.X, y, c.Width, LH(TextStyle.Secondary) * 2 + 4), TextStyle.Secondary, T.TextSecondary, 2);
            y += LH(TextStyle.Secondary) * 2 + 10;
            bool ok = stock >= _qty && MarketRules.Validate(_qty, _price) == null;
            int sb = MeasureBtn("Sotuvga qo'yish", c.Width / 2);
            if (ui.Button(new Rectangle(c.X, y, sb, h), "Sotuvga qo'yish", ButtonKind.Primary, ok && !_client.Busy))
                _client.CreateOffer(_sellProduct, _qty, _price);
            if (stock < _qty) ui.TextFit("Omborda buncha " + def.NameUz.ToLowerInvariant() + " yo'q (" + stock + ")", new Rectangle(c.X + sb + 16, y, c.Width - sb - 16, h), TextStyle.Secondary, T.Danger);
        }

        private int Stepper(int x, int y, string label, ref int value, int[] steps, int min, int max, string unit)
        {
            UiKit ui = _ui; int h = ui.ControlH, gap = 6;
            int ls = LH(TextStyle.Secondary);
            ui.TextFit(label, new Rectangle(x, y, 600, ls), TextStyle.Secondary, ui.T.TextSecondary);
            y += ls + 4;
            int bw = (int)ui.Type.Measure("+500", TextStyle.Button).X + 24;
            int vw = (int)ui.Type.Measure("99999 tanga", TextStyle.Body).X + 24;
            int cx = x;
            for (int i = 0; i < 2; i++) { if (ui.Button(new Rectangle(cx, y, bw, h), steps[i].ToString(), ButtonKind.Secondary)) value = Math.Clamp(value + steps[i], min, max); cx += bw + gap; }
            var vr = new Rectangle(cx, y, vw, h);
            ui.Frame(vr, ui.T.Background, ui.T.Border, 1, false);
            ui.Type.DrawFitted(_sbOf(), value + unit, new Rectangle(vr.X + 6, vr.Y, vr.Width - 12, vr.Height), TextStyle.Body, ui.T.TextPrimary, 1);
            cx += vw + gap;
            for (int i = 2; i < 4; i++) { if (ui.Button(new Rectangle(cx, y, bw, h), "+" + steps[i], ButtonKind.Secondary)) value = Math.Clamp(value + steps[i], min, max); cx += bw + gap; }
            return y + h + ui.Gap;
        }

        private void DrawMyOffers(Rectangle c)
        {
            UiKit ui = _ui;
            var list = _client.Offers.Where(o => o.IsMine).ToList();
            if (list.Count == 0) { ui.Paragraph("Sotuvda hech narsa yo'q. «Sotish» bo'limida taklif yarating.", c, TextStyle.Body); return; }
            int ch = Math.Max(ui.ControlH + 24, 76), gap = ui.Gap;
            int scroll = ui.Scroll("mine", c, list.Count * (ch + gap));
            for (int i = 0; i < list.Count; i++)
            {
                var r = new Rectangle(c.X, c.Y + i * (ch + gap) - scroll, c.Width - 8, ch);
                if (!UiKit.InView(r, c)) continue;
                Rectangle br = TradeCard(r, list[i], "rezervda");
                int id = list[i].Id;
                if (ui.Button(br, "Bekor qilish", ButtonKind.Danger, !_client.Busy)) _client.Cancel(id);
            }
        }

        private void DrawHistory(Rectangle c)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            if (_client.History.Count == 0) { ui.Paragraph("Savdo tarixi bo'sh.", c, TextStyle.Body); return; }
            int ch = 58, gap = 8;
            int scroll = ui.Scroll("hist", c, _client.History.Count * (ch + gap));
            for (int i = 0; i < _client.History.Count; i++)
            {
                TradeRecordDto t = _client.History[i];
                var r = new Rectangle(c.X, c.Y + i * (ch + gap) - scroll, c.Width - 8, ch);
                if (!UiKit.InView(r, c)) continue;
                ui.Card(r);
                ProductIcon(new Rectangle(r.X + 8, r.Y + 8, ch - 16, ch - 16), t.Product);
                ui.TextFit(t.Quantity + " " + ProductName(t.Product).ToLowerInvariant() + " · " + t.TotalPrice + " tanga", new Rectangle(r.X + ch, r.Y + 6, r.Width - ch - 150, 24), TextStyle.Body);
                ui.TextFit(t.SellerName + " → " + t.BuyerName + " · " + t.AtUtc.Replace("Z", ""), new Rectangle(r.X + ch, r.Y + 30, r.Width - ch - 150, 20), TextStyle.Caption, T.TextSecondary);
                ui.Badge(new Vector2(r.Right - 130, r.Y + 18), t.DeliveryStatus == 1 ? "Yetkazildi" : "Karvon yo'lda", t.DeliveryStatus == 1 ? T.Success : T.Warning);
            }
        }

        // ------------------------------------------------------------------
        // KARVONLAR
        // ------------------------------------------------------------------
        private void DrawCaravans(Rectangle c)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T; var list = _client.State.Caravans;
            if (list.Count == 0) { ui.Paragraph("Hozircha karvon yo'q. Bozorda savdo tasdiqlangach karvon sotuvchi omboridan yo'lga chiqadi.", c, TextStyle.Body); return; }
            int ch = 104, gap = ui.Gap;
            int scroll = ui.Scroll("car", c, list.Count * (ch + gap));
            for (int i = 0; i < list.Count; i++)
            {
                CaravanDto v = list[i];
                var r = new Rectangle(c.X, c.Y + i * (ch + gap) - scroll, c.Width - 8, ch);
                if (!UiKit.InView(r, c)) continue;
                ui.Card(r, v.Status == 0);
                ui.TextFit((v.Outgoing ? "Chiquvchi · " : "Keluvchi · ") + v.Quantity + " " + ProductName(v.Product).ToLowerInvariant(), new Rectangle(r.X + 14, r.Y + 8, r.Width - 180, 26), TextStyle.PanelTitle);
                ui.Badge(new Vector2(r.Right - 140, r.Y + 12), v.Status == 1 ? "Yetib keldi" : v.SecondsLeft + " s qoldi", v.Status == 1 ? T.Success : T.Warning);
                ui.Text(v.FromName, new Vector2(r.X + 14, r.Y + 40), TextStyle.Secondary, T.TextSecondary);
                ui.Type.DrawRight(_sbOf(), v.ToName, new Vector2(r.Right - 14, r.Y + 40), TextStyle.Secondary, T.TextSecondary);
                // Yo'l chizig'i va harakatlanayotgan karvon (server vaqti bo'yicha progress, oraliqda silliq)
                var road = new Rectangle(r.X + 14, r.Y + 74, r.Width - 28, 4);
                ui.Rounded(road, T.Border, 2);
                float p = v.Status == 1 ? 1f : Math.Min(1f, v.Progress + (1f - v.Progress) * 0f);
                int px = road.X + (int)((road.Width - 22) * p);
                ui.Rounded(new Rectangle(road.X, road.Y, px - road.X + 4, 4), T.Accent, 2);
                float bob = v.Status == 0 ? MathF.Sin(ui.Time * 8f) * 1.5f : 0f;
                ui.Rounded(new Rectangle(px, road.Y - 14 + (int)bob, 22, 14), new Color(150, 110, 70), 4);   // yuk
                ui.Rounded(new Rectangle(px + 3, road.Y - 20 + (int)bob, 16, 7), ProductColor(v.Product), 3);
            }
        }

        // ------------------------------------------------------------------
        // SALTANAT BOSHQARUVI
        // ------------------------------------------------------------------
        private void DrawKingdom(Rectangle c)
        {
            UiKit ui = _ui;
            KingdomOverviewDto k = _client.Kingdom;
            if (k == null) { ui.Paragraph("Saltanat ma'lumotlari yuklanmoqda…", c, TextStyle.Body); return; }
            int h = ui.ControlH;
            _kingdomTab = ui.TabBar(new Rectangle(c.X, c.Y, c.Width, h), KingdomTabs, _kingdomTab);
            var body = new Rectangle(c.X, c.Y + h + ui.Gap + 4, c.Width, c.Height - h - ui.Gap - 4);
            switch (_kingdomTab)
            {
                case 0: DrawOverview(body, k); break;
                case 1: DrawCities(body, k); break;
                case 2: DrawRegions(body, k); break;
                case 3: DrawPopulation(body, k); break;
                case 4: DrawEconomy(body, k); break;
                case 5: DrawDiplomacy(body, k); break;
                case 7: DrawFlagTab(body, k); break;
                default: DrawGovernors(body, k); break;
            }
        }

        private void StatCard(Rectangle r, string label, string value, Color accent)
        {
            UiKit ui = _ui;
            ui.Card(r);
            ui.Rounded(new Rectangle(r.X, r.Y + 10, 4, r.Height - 20), accent, 2);
            int ls = (int)ui.Type.LineHeight(TextStyle.Secondary), lp = (int)ui.Type.LineHeight(TextStyle.PanelTitle);
            int top = r.Y + Math.Max(4, (r.Height - ls - lp - 2) / 2);
            ui.TextFit(label, new Rectangle(r.X + 16, top, r.Width - 24, ls), TextStyle.Secondary, ui.T.TextSecondary);
            ui.TextFit(value, new Rectangle(r.X + 16, top + ls + 2, r.Width - 24, lp), TextStyle.PanelTitle);
        }

        private void DrawOverview(Rectangle c, KingdomOverviewDto k)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            (string, string, Color)[] stats =
            {
                ("Saltanat", k.Name, T.Primary), ("Hukmdor", k.Ruler, T.Primary), ("Poytaxt", k.Capital, T.Primary),
                ("Shaharlar", k.CityCount.ToString(), T.Success), ("Aholi", k.Population.ToString(), T.Success), ("Hududlar", k.RegionCount.ToString(), T.Success),
                ("Xazina", k.Coin + " tanga", T.Accent), ("Rivojlanish", k.DevelopmentScore.ToString(), T.Accent)
            };
            int cols = c.Width >= 760 ? 4 : 2, gap = ui.Gap;
            int cw = (c.Width - (cols - 1) * gap) / cols;
            int ch = Math.Max(70, (int)(ui.Type.LineHeight(TextStyle.Secondary) + ui.Type.LineHeight(TextStyle.PanelTitle)) + 18);
            for (int i = 0; i < stats.Length; i++)
                StatCard(new Rectangle(c.X + (i % cols) * (cw + gap), c.Y + (i / cols) * (ch + gap), cw, ch), stats[i].Item1, stats[i].Item2, stats[i].Item3);
            int y = c.Y + ((stats.Length + cols - 1) / cols) * (ch + gap) + 6;
            int lpt = (int)ui.Type.LineHeight(TextStyle.PanelTitle);
            ui.TextFit("Vazir eslatmasi", new Rectangle(c.X, y, c.Width, lpt), TextStyle.PanelTitle, T.Primary);
            string note = k.AdvisorNotes.Count > 0 ? k.AdvisorNotes[0].Split('|')[0] : "";
            ui.Paragraph(note, new Rectangle(c.X, y + lpt + 6, c.Width, c.Bottom - y - lpt - 6), TextStyle.Body, T.TextSecondary, 4);
        }

        /// <summary>UICityCard.</summary>
        private void DrawCities(Rectangle c, KingdomOverviewDto k)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            int lp = LH(TextStyle.PanelTitle), ls = LH(TextStyle.Secondary), lc = LH(TextStyle.Caption);
            int mh = lc + 12, gap = ui.Gap;
            int ch = Math.Max(150, 12 + lp + 4 + ls + 10 + mh + 10 + lc + 12);
            int bw = MeasureBtn("Shaharni boshqarish", c.Width / 3);
            int scroll = ui.Scroll("cities", c, k.Cities.Count * (ch + gap));
            for (int i = 0; i < k.Cities.Count; i++)
            {
                CityDto city = k.Cities[i];
                var r = new Rectangle(c.X, c.Y + i * (ch + gap) - scroll, c.Width - 8, ch);
                if (!UiKit.InView(r, c)) continue;
                ui.Card(r, city.Id == _selectedCity);
                int tw = r.Width - bw - 40, yy = r.Y + 12;
                ui.TextFit(city.Name + (city.IsCapital ? " · poytaxt" : "") + " · " + city.Level + "-daraja", new Rectangle(r.X + 14, yy, tw, lp), TextStyle.PanelTitle); yy += lp + 4;
                ui.TextFit("Hudud: " + city.RegionName + " · Hokim: " + city.GovernorName + " (" + ModeName(city.GovernorMode) + ")", new Rectangle(r.X + 14, yy, tw, ls), TextStyle.Secondary, T.TextSecondary); yy += ls + 10;
                int colW = Math.Max(80, (tw - 20) / 3);
                Metric(new Rectangle(r.X + 14, yy, colW, mh), "Aholi", city.Population + " / " + city.HousingCapacity, city.Population / (float)Math.Max(1, city.HousingCapacity), T.Secondary);
                Metric(new Rectangle(r.X + 24 + colW, yy, colW, mh), "Mamnunlik", city.Happiness.ToString(), city.Happiness / 100f, T.Success);
                Metric(new Rectangle(r.X + 34 + 2 * colW, yy, colW, mh), "Ishonch", city.Trust.ToString(), city.Trust / 100f, city.Trust < 50 ? T.Warning : T.Primary);
                yy += mh + 10;
                string problems = city.Problems.Count == 0 ? "Muammo yo'q" : string.Join(" · ", city.Problems);
                ui.TextFit("Daromad " + city.DailyIncome + " / xarajat " + city.DailyExpense + " · " + problems, new Rectangle(r.X + 14, yy, tw, lc), TextStyle.Caption, city.Problems.Count == 0 ? T.Success : T.Warning);
                if (ui.Button(new Rectangle(r.Right - bw - 14, r.Y + (ch - ui.ControlH) / 2, bw, ui.ControlH), "Shaharni boshqarish", ButtonKind.Primary))
                { _selectedCity = city.Id; _kingdomTab = 6; }
            }
        }

        private void Metric(Rectangle r, string label, string value, float bar, Color color)
        {
            UiKit ui = _ui;
            int lc = LH(TextStyle.Caption);
            ui.TextFit(label + ": " + value, new Rectangle(r.X, r.Y, r.Width, lc), TextStyle.Caption, ui.T.TextSecondary);
            ui.ProgressBar(new Rectangle(r.X, r.Y + lc + 3, r.Width, 6), bar, color);
        }

        private static string ModeName(GovernorMode m) => m == GovernorMode.Manual ? "qo'lda" : m == GovernorMode.Advisory ? "maslahat" : "tasdiqlangan avtomatika";

        private UzMapCanvas _regionCanvas;

        /// <summary>HUDUDLAR: O'zbekiston xaritasi (hududlar egasi rangida, bayroqlari bilan) + tanlangan hudud kartasi.</summary>
        private void DrawRegions(Rectangle c, KingdomOverviewDto k)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            bool side = c.Width >= 760;
            int infoW = side ? Math.Min((int)(330 * Math.Max(1f, ui.Type.UiScale)), c.Width * 2 / 5) : c.Width;
            var map = side ? new Rectangle(c.X, c.Y, c.Width - infoW - 14, c.Height) : new Rectangle(c.X, c.Y, c.Width, c.Height * 55 / 100);
            var info = side ? new Rectangle(map.Right + 14, c.Y, infoW, c.Height) : new Rectangle(c.X, map.Bottom + 10, c.Width, c.Height - map.Height - 10);

            _regionCanvas ??= new UzMapCanvas(ui);
            UzMapCanvas cv = _regionCanvas;
            RegionDto home = k.Regions.FirstOrDefault(r => r.IsMine && r.Name == k.Capital) ?? k.Regions.FirstOrDefault(r => r.IsMine);
            if (!cv.Focused && home != null) cv.FocusOn(home.MapX, home.MapY, 3.5f);
            cv.Begin(map);
            _cellBuf.Clear();
            foreach (RegionDto r in k.Regions) _cellBuf.Add(new MapCell(r.Id, r.MapX, r.MapY, r.OwnerKingdomId, new Color(r.ColorR, r.ColorG, r.ColorB), r.Province, r.IsMine));
            cv.DrawBase(_cellBuf, _selectedRegion, T.Focus);

            float sc = cv.MarkerScale;
            foreach (RegionDto r in k.Regions.OrderBy(x => x.MapY))
            {
                Vector2 p = cv.ToScreen(r.MapX, r.MapY);
                if (!cv.InView(p, 40)) continue;
                var col = new Color(r.ColorR, r.ColorG, r.ColorB);
                bool pulse = r.Id == _client.LastTransferredRegionId && _territoryPulse > 0f;
                if (pulse) { int g = (int)(10 * sc + 6 + 6 * MathF.Sin(ui.Time * 8)); ui.Rounded(new Rectangle((int)p.X - g, (int)p.Y - g, 2 * g, 2 * g), T.Accent * 0.5f, g); }
                int d = Math.Max(4, (int)(7 * sc));
                ui.Rounded(new Rectangle((int)p.X - d, (int)p.Y - d / 2, d * 2, d), Ink, d / 2);
                ui.Rounded(new Rectangle((int)p.X - d + 2, (int)p.Y - d / 2 + 1, d * 2 - 4, d - 2), r.OwnerKingdomId == 0 ? new Color(236, 220, 180) : col, d / 2);
                if (r.OwnerKingdomId != 0)
                    FlagRenderer.DrawOnPole(ui, p, (int)(26 * sc), (int)(22 * sc), FlagRenderer.Spec(r.Flag, col), ui.Time, r.Id);
            }
            bool showAll = cv.Zoom >= 2.4f;
            foreach (RegionDto r in k.Regions.OrderByDescending(x => x.Id == _selectedRegion).ThenByDescending(x => x.IsMine).ThenByDescending(x => x.Population))
            {
                if (!showAll && r.Id != _selectedRegion && !r.IsMine) continue;
                Vector2 p = cv.ToScreen(r.MapX, r.MapY);
                if (!cv.InView(p)) continue;
                cv.Label(r.Name, p + new Vector2(0, 6), r.IsMine ? new Color(40, 70, 130) : Ink, r.IsMine ? new Color(30, 60, 120) : Ink, r.Id == _selectedRegion);
            }

            // PROMPT 25: qo'shinlarim (bayroqcha + birliklar soni) — bosilsa Harbiy bo'limga o'tadi
            WarOverviewDto war = _client.War;
            if (war != null)
            {
                if (war.ActiveBattle != null)
                {
                    RegionDto br = k.Regions.FirstOrDefault(x => x.Id == war.ActiveBattle.RegionId);
                    if (br != null)
                    {
                        Vector2 bp = cv.ToScreen(br.MapX, br.MapY);
                        int g = (int)(16 * sc + 8 + 5 * MathF.Sin(ui.Time * 6));
                        ui.Rounded(new Rectangle((int)bp.X - g, (int)bp.Y - g, 2 * g, 2 * g), T.Danger * 0.35f, g);
                    }
                }
                Color myColor = k.Color != null && k.Color.Length >= 3 ? new Color(k.Color[0], k.Color[1], k.Color[2]) : T.Primary;
                var myFlag = FlagRenderer.Spec(k.Flag, myColor);
                for (int i = 0; i < war.Armies.Count; i++)
                {
                    ArmyDto a = war.Armies[i];
                    RegionDto ar = k.Regions.FirstOrDefault(x => x.Id == a.RegionId);
                    if (ar == null) continue;
                    Vector2 p = cv.ToScreen(ar.MapX, ar.MapY) + new Vector2(14 * sc + i * 18 * sc, 6 * sc);
                    if (!cv.InView(p)) continue;
                    FlagRenderer.DrawOnPole(ui, p, (int)(20 * sc), (int)(16 * sc), myFlag, ui.Time, 100 + i);
                    ui.Text(a.UnitCount.ToString(), new Vector2(p.X + 2, p.Y - 6 * sc), TextStyle.Caption, Ink);
                    if (ui.Clicked(new Rectangle((int)p.X - 4, (int)(p.Y - 22 * sc), (int)(22 * sc) + 8, (int)(26 * sc)))) { _warArmy = a.Id; _section = Section.War; _warTab = 1; }
                }
            }

            int picked = cv.PickNearest(k.Regions.Select(r => (r.Id, cv.ToScreen(r.MapX, r.MapY))), 30 * sc + 14);
            if (picked >= 0) _selectedRegion = picked;
            cv.DrawOverlay("O'zbekiston");

            RegionDto s = k.Regions.FirstOrDefault(x => x.Id == _selectedRegion);
            ui.Card(info);
            int x = info.X + 14, y = info.Y + 12, w = info.Width - 28, h = ui.ControlH;
            if (s == null) { ui.Paragraph("Xaritadan hududni tanlang. O'z hududlaringiz ko'k halqa bilan belgilangan.", new Rectangle(x, y, w, 120), TextStyle.Body); return; }
            ui.TextFit(s.Name, new Rectangle(x, y, w, 28), TextStyle.PanelTitle); y += 32;
            void Line(string a, string b) { ui.TextFit(a + ": " + b, new Rectangle(x, y, w, 22), TextStyle.Secondary, T.TextSecondary); y += 24; }
            Line("Viloyat", AndijonSaltanatlar.Shared.Geo.UzMap.ProvinceName(s.Province));
            Line("Egasi", s.OwnerName);
            Line("Aholi", s.Population.ToString());
            Line("Shaharlar", string.Join(", ", s.Cities));
            Line("Iqtisodiyot (rivojlanish)", s.Development.ToString());
            Line("Barqarorlik", s.Stability.ToString());
            if (!s.IsMine && s.OwnerKingdomId != 0) Line("Munosabat", s.Relation + " / 100");
            y += 6;
            if (s.IsMine)
            {
                CityDto city = k.Cities.FirstOrDefault(cc => cc.RegionId == s.Id);
                if (city != null && ui.Button(new Rectangle(x, y, w, h), Typography.Uz(city.Name).ToUpperInvariant() + "NI BOSHQARISH", ButtonKind.Primary))
                { _selectedCity = city.Id; _kingdomTab = 6; }
                return;
            }
            if (s.OwnerKingdomId != 0 && ui.Button(new Rectangle(x, y, w, h), "Elchi yuborish · " + TerritoryRules.EnvoyCost, ButtonKind.Secondary, !_client.Busy, "Munosabat +" + TerritoryRules.EnvoyRelationGain))
                _client.SendEnvoy(s.Id);
            if (s.OwnerKingdomId != 0) y += h + 8;
            if (ui.Button(new Rectangle(x, y, w, h), "Diplomatik birlashish · " + s.AnnexCost, ButtonKind.Accent, s.CanAnnex && !_client.Busy))
                _client.Annex(s.Id);
            y += h + 6;
            if (!s.CanAnnex) ui.Paragraph(s.AnnexBlockReasonUz, new Rectangle(x, y, w, info.Bottom - y - 8), TextStyle.Caption, T.Warning, 3);
        }

        private void DrawPopulation(Rectangle c, KingdomOverviewDto k)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            float ph = ui.Paragraph("Saltanat aholisi — unga tegishli shaharlar aholisi yig'indisi (har bir fuqaro bir marta). Hudud qo'shilganda aholi o'z uyi, oilasi va ishi bilan yangi boshqaruvga o'tadi.",
                new Rectangle(c.X, c.Y, c.Width, LH(TextStyle.Secondary) * 3 + 8), TextStyle.Secondary, T.TextSecondary, 3);
            int y = c.Y + (int)ph + 10, rh = Math.Max(44, LH(TextStyle.Body) + 16);
            foreach (CityDto city in k.Cities)
            {
                var r = new Rectangle(c.X, y, c.Width - 8, rh);
                ui.Card(r);
                // Eni matn o'lchamiga qarab: nom — chapda, raqamlar — o'ngda, chiziq — o'rtadagi qolgan joyda.
                string stats = city.Population + " kishi · " + city.Households + " oila · " + city.Jobs + " ish o'rni";
                int nameW = Math.Min(r.Width / 4, (int)ui.Type.Measure(city.Name, TextStyle.Body).X + 24);
                int statW = Math.Min(r.Width / 2, (int)ui.Type.Measure(stats, TextStyle.Secondary).X + 16);
                ui.TextFit(city.Name, new Rectangle(r.X + 12, r.Y, nameW, rh), TextStyle.Body);
                int bx = r.X + 12 + nameW + 8, bw = Math.Max(40, r.Right - 12 - statW - 12 - bx);
                ui.ProgressBar(new Rectangle(bx, r.Center.Y - 4, bw, 8), city.Population / (float)Math.Max(1, k.Population), T.Secondary);
                ui.TextFit(stats, new Rectangle(r.Right - 12 - statW, r.Y, statW, rh), TextStyle.Secondary, T.TextSecondary);
                y += rh + 6;
                if (y > c.Bottom - rh) break;
            }
            ui.Text("Jami: " + k.Population + " kishi", new Vector2(c.X, Math.Min(y + 6, c.Bottom - 24)), TextStyle.PanelTitle, T.Accent);
        }

        private void DrawEconomy(Rectangle c, KingdomOverviewDto k)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            int income = k.Cities.Sum(x => x.DailyIncome), expense = k.Cities.Sum(x => x.DailyExpense);
            int gap = ui.Gap, cw = (c.Width - 2 * gap) / 3;
            int sh = Math.Max(70, LH(TextStyle.Secondary) + LH(TextStyle.PanelTitle) + 18);
            StatCard(new Rectangle(c.X, c.Y, cw, sh), "Xazina", k.Coin + " tanga", T.Accent);
            StatCard(new Rectangle(c.X + cw + gap, c.Y, cw, sh), "Daromad (kunlik)", "+" + income, T.Success);
            StatCard(new Rectangle(c.X + 2 * (cw + gap), c.Y, cw, sh), "Xarajat (kunlik)", "−" + expense, T.Danger);
            int y = c.Y + sh + 14;
            ui.TextFit("Ombor", new Rectangle(c.X, y, c.Width, LH(TextStyle.PanelTitle)), TextStyle.PanelTitle, T.Primary); y += LH(TextStyle.PanelTitle) + 6;
            int rh = Math.Max(40, LH(TextStyle.Body) + 12);
            foreach (CropDef d in CropCatalog.All)
            {
                int have = k.Inventory.TryGetValue(d.Key, out int v) ? v : 0;
                var r = new Rectangle(c.X, y, c.Width - 8, rh);
                ui.Card(r);
                int ic = rh - 10;
                ProductIcon(new Rectangle(r.X + 6, r.Y + 5, ic, ic), d.Key);
                ui.TextFit(d.NameUz, new Rectangle(r.X + ic + 16, r.Y, r.Width / 2 - ic - 16, rh), TextStyle.Body);
                ui.Type.DrawFitted(_sbOf(), have + " birlik · ≈" + have * d.ReferencePrice + " tanga", new Rectangle(r.X + r.Width / 2, r.Y, r.Width / 2 - 12, rh), TextStyle.Secondary, T.TextSecondary, 2);
                y += rh + 6;
                if (y > c.Bottom - rh) break;
            }
        }

        private void DrawDiplomacy(Rectangle c, KingdomOverviewDto k)
        {
            UiKit ui = _ui;
            string[] tabs = { "Munosabatlar", "Ittifoqlar", "Kelishuvlar", "Takliflar", "Yangiliklar" };
            _diplTab = ui.TabBar(new Rectangle(c.X, c.Y, c.Width, ui.ControlH), tabs, _diplTab);
            var body = new Rectangle(c.X, c.Y + ui.ControlH + ui.Gap + 4, c.Width, c.Height - ui.ControlH - ui.Gap - 4);
            switch (_diplTab)
            {
                case 0: DrawDiplomacyRelations(body, k, ui); break;
                case 1: DrawAlliances(body, ui); break;
                case 2: DrawAgreements(body, ui); break;
                case 3: DrawInvitations(body, ui); break;
                default: DrawNews(body, ui); break;
            }
        }

        private void DrawDiplomacyRelations(Rectangle c, KingdomOverviewDto k, UiKit ui)
        {
            ThemeTokens T = ui.T;
            int lb = (int)ui.Type.LineHeight(TextStyle.Body), lc = (int)ui.Type.LineHeight(TextStyle.Caption);
            int y = c.Y, rh = Math.Max(64, lb + lc + 18);
            int valW = (int)ui.Type.Measure("-100", TextStyle.Body).X + 20, barW = Math.Min(180, c.Width / 5);
            foreach (KingdomRelationDto r in k.Relations)
            {
                var card = new Rectangle(c.X, y, c.Width - 8, rh);
                ui.Card(card);
                int textW = card.Width - barW - valW - 50;
                ui.TextFit(r.Name + (r.IsAi ? " · AI saltanat" : " · o'yinchi"), new Rectangle(card.X + 14, card.Y + 8, textW, lb), TextStyle.Body);
                ui.TextFit(r.Regions + " hudud", new Rectangle(card.X + 14, card.Y + 10 + lb, textW, lc), TextStyle.Caption, T.TextSecondary);
                var bar = new Rectangle(card.Right - valW - barW - 20, card.Center.Y - 4, barW, 8);
                ui.Rounded(bar, T.Disabled, 4);
                int mid = bar.X + bar.Width / 2, len = (int)(bar.Width / 2f * Math.Abs(r.Value) / 100f);
                ui.Rounded(r.Value >= 0 ? new Rectangle(mid, bar.Y, Math.Max(2, len), 8) : new Rectangle(mid - len, bar.Y, Math.Max(2, len), 8), r.Value >= 0 ? T.Success : T.Danger, 4);
                ui.Type.DrawFitted(_sbOf(), (r.Value > 0 ? "+" : "") + r.Value, new Rectangle(card.Right - valW - 10, card.Y, valW, card.Height), TextStyle.Body, r.Value >= 0 ? T.Success : T.Danger, 2);
                y += rh + 8;
                if (y > c.Bottom - rh) break;
            }
            int ph = lc * 2 + 6;
            ui.Paragraph("Munosabatni oshirish: Hududlar xaritasida hududni tanlab, elchi yuboring. Ittifoq va kelishuvlar — Ittifoqlar bo'limida.",
                new Rectangle(c.X, Math.Min(y + 4, c.Bottom - ph), c.Width, ph), TextStyle.Caption, T.TextSecondary);
        }

        private void DrawAlliances(Rectangle c, UiKit ui)
        {
            ThemeTokens T = ui.T;
            int h = ui.ControlH, y = c.Y;
            AllianceDto mine = _client.Alliance;
            if (mine == null)
            {
                ui.Text("Siz hali ittifoqdasiz emas.", new Vector2(c.X, y), TextStyle.Body); y += 26;
                // Create alliance simple form: name input not available easily; use a default name button
                if (ui.Button(new Rectangle(c.X, y, 260, h), "Yangi ittifoq yaratish", ButtonKind.Primary, !_client.Busy))
                    _client.CreateAlliance("Andijon ittifoqi", 1);
                y += h + 10;
                ui.Text("Boshqa saltanatga taklif yuborish uchun avval ittifoq yarating yoki mavjud ittifoqqa qo'shiling.", new Vector2(c.X, y), TextStyle.Secondary, T.TextSecondary);
                y += 40;
            }
            else
            {
                ui.TextFit(mine.Name + " · A'zolar: " + mine.Members.Count, new Rectangle(c.X, y, c.Width, 26), TextStyle.PanelTitle); y += 30;
                ui.Text("Xazina: " + mine.TreasuryCoin + " tanga · " + mine.TreasuryWood + " yog'och · " + mine.TreasuryStone + " tosh", new Vector2(c.X, y), TextStyle.Secondary, T.TextSecondary); y += 24;
                foreach (AllianceMemberDto m in mine.Members)
                {
                    ui.TextFit(m.KingdomName + " — " + m.RoleNameUz, new Rectangle(c.X + 8, y, c.Width - 16, 20), TextStyle.Secondary); y += 20;
                    if (y > c.Bottom - 120) break;
                }
                y += 6;
                if (ui.Button(new Rectangle(c.X, y, 220, h), "Loyiha yaratish", ButtonKind.Secondary, !_client.Busy && (mine.MyRole == 0 || mine.MyRole == 1)))
                    _client.CreateAllianceProject(mine.Id, 0);
                if (ui.Button(new Rectangle(c.X + 230, y, 180, h), "Hissa qo'shish (100 tanga)", ButtonKind.Secondary, !_client.Busy))
                    _client.ContributeAlliance(mine.Id, 0, "Coin", 100);
                y += h + 8;
                if (ui.Button(new Rectangle(c.X, y, 180, h), "Ittifoqdan chiqish", ButtonKind.Danger, !_client.Busy && mine.MyRole != 0))
                    _client.LeaveAlliance(mine.Id);
                y += h + 10;
            }

            // Projects
            if (mine != null && mine.Projects.Count > 0)
            {
                ui.Text("Qo'shma loyihalar", new Vector2(c.X, y), TextStyle.PanelTitle); y += 28;
                foreach (AllianceProjectDto p in mine.Projects)
                {
                    var card = new Rectangle(c.X, y, c.Width - 8, 78);
                    if (card.Bottom > c.Bottom) break;
                    ui.Card(card);
                    ui.TextFit(p.TypeNameUz + " — " + p.StatusUz, new Rectangle(card.X + 12, card.Y + 6, card.Width - 24, 22), TextStyle.Body);
                    ui.TextFit(p.GotCoin + "/" + p.ReqCoin + " tanga · " + p.GotWood + "/" + p.ReqWood + " yog'och · " + p.GotStone + "/" + p.ReqStone + " tosh", new Rectangle(card.X + 12, card.Y + 30, card.Width - 24, 20), TextStyle.Caption, T.TextSecondary);
                    ui.ProgressBar(new Rectangle(card.X + 12, card.Y + 56, card.Width - 24, 6), p.Progress, T.Success);
                    y += 86;
                }
            }

            // All alliances in world
            y = Math.Min(y + 6, c.Bottom - 120);
            ui.Text("Dunyodagi ittifoqlar", new Vector2(c.X, y), TextStyle.PanelTitle); y += 26;
            foreach (AllianceDto a in _client.AlliancesInWorld)
            {
                if (y > c.Bottom - 30) break;
                ui.TextFit(a.Name + " (" + a.Members.Count + " a'zo)", new Rectangle(c.X + 8, y, c.Width - 180, 20), TextStyle.Secondary);
                // invite button if I'm in an alliance and founder/envoy
                if (mine != null && (mine.MyRole == 0 || mine.MyRole == 1) && !mine.Members.Any(m => m.KingdomId == a.FounderKingdomId))
                {
                    if (ui.Button(new Rectangle(c.Right - 170, y - 2, 160, 22), "Taklif", ButtonKind.Secondary, !_client.Busy))
                        _client.InviteAlliance(mine.Id, a.FounderKingdomId);
                }
                y += 24;
            }
        }

        private void DrawAgreements(Rectangle c, UiKit ui)
        {
            ThemeTokens T = ui.T;
            int h = ui.ControlH, y = c.Y;
            if (ui.Button(new Rectangle(c.X, y, 260, h), "Savdo kelishuvi taklifi", ButtonKind.Secondary, !_client.Busy && _client.Kingdom != null && _client.Kingdom.Relations.Count > 0))
            {
                var rel = _client.Kingdom.Relations.FirstOrDefault();
                if (rel != null) _client.ProposeAgreement(0, rel.KingdomId, 0, 0);
            }
            y += h + 8;
            foreach (DiplomaticAgreementDto a in _client.Agreements)
            {
                var card = new Rectangle(c.X, y, c.Width - 8, 70);
                if (card.Bottom > c.Bottom) break;
                ui.Card(card);
                ui.TextFit(a.TypeNameUz + " · " + a.StatusUz, new Rectangle(card.X + 12, card.Y + 6, card.Width - 24, 22), TextStyle.Body);
                ui.TextFit(a.FromKingdomName + " → " + a.ToKingdomName + (a.RegionName.Length > 0 ? " · " + a.RegionName : "") + (a.Price > 0 ? " · " + a.Price + " tanga" : ""),
                    new Rectangle(card.X + 12, card.Y + 30, card.Width - 24, 20), TextStyle.Caption, T.TextSecondary);
                if (a.Status == 0 && a.ToKingdomId == (_client.Kingdom?.KingdomId ?? 0))
                {
                    if (ui.Button(new Rectangle(card.Right - 200, card.Y + 40, 90, 24), "Qabul", ButtonKind.Primary, !_client.Busy)) _client.RespondAgreement(a.Id, true);
                    if (ui.Button(new Rectangle(card.Right - 100, card.Y + 40, 90, 24), "Rad", ButtonKind.Danger, !_client.Busy)) _client.RespondAgreement(a.Id, false);
                }
                y += 78;
            }
            if (_client.Agreements.Count == 0) ui.Paragraph("Kelishuvlar yo'q.", new Rectangle(c.X, y, c.Width, 40), TextStyle.Body);
        }

        private void DrawInvitations(Rectangle c, UiKit ui)
        {
            ThemeTokens T = ui.T;
            int y = c.Y;
            if (_client.Invitations.Count == 0) { ui.Paragraph("Kutilayotgan ittifoq takliflari yo'q.", c, TextStyle.Body); return; }
            foreach (AllianceInvitationDto inv in _client.Invitations)
            {
                var card = new Rectangle(c.X, y, c.Width - 8, 70);
                if (card.Bottom > c.Bottom) break;
                ui.Card(card);
                ui.TextFit(inv.AllianceName + " · " + inv.FromKingdomName + " taklif qildi", new Rectangle(card.X + 12, card.Y + 6, card.Width - 24, 22), TextStyle.Body);
                ui.TextFit(inv.StatusUz + " · " + inv.CreatedAt, new Rectangle(card.X + 12, card.Y + 32, card.Width - 24, 20), TextStyle.Caption, T.TextSecondary);
                if (ui.Button(new Rectangle(card.Right - 200, card.Y + 40, 90, 24), "Qabul", ButtonKind.Primary, !_client.Busy)) _client.RespondInvitation(inv.Id, true);
                if (ui.Button(new Rectangle(card.Right - 100, card.Y + 40, 90, 24), "Rad", ButtonKind.Danger, !_client.Busy)) _client.RespondInvitation(inv.Id, false);
                y += 78;
            }
        }

        private void DrawNews(Rectangle c, UiKit ui)
        {
            ThemeTokens T = ui.T;
            int y = c.Y;
            if (_client.News.Count == 0) { ui.Paragraph("Dunyo yangiliklari yo'q.", c, TextStyle.Body); return; }
            foreach (WorldNewsDto n in _client.News)
            {
                var card = new Rectangle(c.X, y, c.Width - 8, 58);
                if (card.Bottom > c.Bottom) break;
                ui.Card(card);
                ui.TextFit(n.TextUz, new Rectangle(card.X + 12, card.Y + 6, card.Width - 24, 22), TextStyle.Body);
                ui.TextFit(n.KingdomAName + (n.KingdomBName.Length > 0 ? " ↔ " + n.KingdomBName : "") + " · " + n.GameDay + "-kun · " + n.AtUtc.Replace("Z", ""), new Rectangle(card.X + 12, card.Y + 32, card.Width - 24, 20), TextStyle.Caption, T.TextSecondary);
                y += 66;
            }
        }

        private void DrawGovernors(Rectangle c, KingdomOverviewDto k)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T;
            if (k.Cities.Count == 0) return;
            if (k.Cities.All(x => x.Id != _selectedCity)) _selectedCity = k.Cities[0].Id;
            int h = ui.ControlH, gap = ui.Gap, cx = c.X, y = c.Y;
            foreach (CityDto city in k.Cities)
            {
                int w = (int)ui.Type.Measure(city.Name, TextStyle.Secondary).X + 36;
                if (cx + w > c.Right) { cx = c.X; y += h; }
                if (ui.Chip(new Rectangle(cx, y, w, h - 8), city.Name, city.Id == _selectedCity, city.PendingPlan != null ? T.Accent : (Color?)null)) _selectedCity = city.Id;
                cx += w + 8;
            }
            y += h + gap;
            CityDto sel = k.Cities.First(x => x.Id == _selectedCity);
            ui.TextFit(sel.GovernorName + " — " + sel.Name + " hokimi", new Rectangle(c.X, y, c.Width, LH(TextStyle.PanelTitle)), TextStyle.PanelTitle, T.Primary);
            y += LH(TextStyle.PanelTitle) + 6;
            ui.TextFit("Boshqaruv rejimi", new Rectangle(c.X, y, c.Width, LH(TextStyle.Secondary)), TextStyle.Secondary, T.TextSecondary); y += LH(TextStyle.Secondary) + 4;
            string[] modes = { "Qo'lda", "Maslahat", "Avtomatika" };
            int newMode = ui.TabBar(new Rectangle(c.X, y, Math.Min(c.Width, (int)ui.Type.Measure("Avtomatika", TextStyle.Button).X * 3 + 120), h), modes, (int)sel.GovernorMode);
            if (newMode != (int)sel.GovernorMode && !_client.Busy) _client.SetGovernorMode(sel.Id, (GovernorMode)newMode);
            y += h + 8;
            string explain = sel.GovernorMode == GovernorMode.Manual ? "Siz hamma narsani o'zingiz qilasiz. Hokim tahlil qilmaydi."
                           : sel.GovernorMode == GovernorMode.Advisory ? "Hokim shaharni tahlil qilib reja tavsiya qiladi; bajarish uchun «Tasdiqlangan avtomatika» kerak."
                           : "Hokim siz tasdiqlagan rejalarni bajaradi. Ruxsatsiz katta xarajat qilmaydi (limit: " + sel.GovernorSpendLimit + " tanga).";
            y += (int)ui.Paragraph(explain, new Rectangle(c.X, y, c.Width, LH(TextStyle.Secondary) * 2 + 4), TextStyle.Secondary, T.TextSecondary, 2) + 8;
            if (ui.Button(new Rectangle(c.X, y, MeasureBtn("Shaharni tahlil qilish", c.Width), h), "Shaharni tahlil qilish", ButtonKind.Secondary, sel.GovernorMode != GovernorMode.Manual && !_client.Busy))
                _client.GovernorAnalyze(sel.Id);
            y += h + gap;

            GovernorPlanDto p = sel.PendingPlan;
            if (p == null) { ui.Paragraph(sel.Problems.Count == 0 ? "Hozircha reja yo'q — shahar barqaror." : "Muammolar: " + string.Join(" · ", sel.Problems), new Rectangle(c.X, y, c.Width, 60), TextStyle.Body, T.TextSecondary); return; }
            int lpp = LH(TextStyle.PanelTitle), lss = LH(TextStyle.Secondary), lbb = LH(TextStyle.Body);
            var card = new Rectangle(c.X, y, c.Width - 8, Math.Min(12 + lpp + 4 + lss + 6 + lbb + 12 + h + 12, c.Bottom - y));
            ui.Card(card, true);
            int cy2 = card.Y + 12;
            ui.TextFit(p.TitleUz, new Rectangle(card.X + 14, cy2, card.Width - 28, lpp), TextStyle.PanelTitle); cy2 += lpp + 4;
            ui.TextFit(p.ReasonUz, new Rectangle(card.X + 14, cy2, card.Width - 28, lss), TextStyle.Secondary, T.TextSecondary); cy2 += lss + 6;
            ui.TextFit(p.Houses + " uy · " + p.Markets + " bozor · " + p.Gardens + " bog'  =  " + p.Cost + " tanga" + (p.Cost > sel.GovernorSpendLimit ? " (limitdan katta — alohida tasdiq)" : ""),
                new Rectangle(card.X + 14, cy2, card.Width - 28, lbb), TextStyle.Body, p.Cost > sel.GovernorSpendLimit ? T.Warning : T.TextPrimary);
            int bw = Math.Min(MeasureBtn("Tasdiqlash · 9999", (card.Width - 40) / 2), (card.Width - 40) / 2), by = card.Bottom - h - 12;
            if (ui.Button(new Rectangle(card.X + 14, by, bw, h), "Tasdiqlash · " + p.Cost, ButtonKind.Primary,
                sel.GovernorMode == GovernorMode.ApprovedAutomation && _client.State.Coin >= p.Cost && !_client.Busy,
                sel.GovernorMode != GovernorMode.ApprovedAutomation ? "Avval «Tasdiqlangan avtomatika» rejimini tanlang" : null))
                _client.ApprovePlan(p.Id);
            if (ui.Button(new Rectangle(card.X + 24 + bw, by, bw, h), "Rad etish", ButtonKind.Secondary, !_client.Busy)) _client.RejectPlan(p.Id);
        }

        // ------------------------------------------------------------------
        // VAZIR
        // ------------------------------------------------------------------
        private void DrawVizier(Rectangle c)
        {
            UiKit ui = _ui; ThemeTokens T = ui.T; KingdomOverviewDto k = _client.Kingdom;
            if (k == null) return;
            int y = c.Y, h = ui.ControlH;
            int bw = Math.Min(c.Width / 3, (int)ui.Type.Measure("Karvonlarni ko'rish", TextStyle.Button).X + 40);
            foreach (string raw in k.AdvisorNotes)
            {
                string[] parts = raw.Split('|');
                string text = parts[0], action = parts.Length > 1 ? parts[1] : "";
                float th = ui.Type.Wrap(text, c.Width - bw - 50, TextStyle.Body).Count * (ui.Type.LineHeight(TextStyle.Body) + 2);
                var card = new Rectangle(c.X, y, c.Width - 8, (int)Math.Max(h + 24, th + 28));
                if (card.Bottom > c.Bottom) break;
                ui.Card(card);
                ui.Rounded(new Rectangle(card.X, card.Y + 10, 4, card.Height - 20), T.Accent, 2);
                ui.Paragraph(text, new Rectangle(card.X + 16, card.Y + 12, card.Width - bw - 50, card.Height - 16), TextStyle.Body, T.TextPrimary);
                var br = new Rectangle(card.Right - bw - 14, card.Y + (card.Height - h) / 2, bw, h);
                if (action.StartsWith("city:") && int.TryParse(action.Substring(5), out int cityId))
                {
                    string name = k.Cities.FirstOrDefault(x => x.Id == cityId)?.Name ?? "Shahar";
                    if (ui.Button(br, Typography.Uz(name).ToUpperInvariant() + "NI BOSHQARISH", ButtonKind.Primary)) { _section = Section.Kingdom; _kingdomTab = 6; _selectedCity = cityId; }
                }
                else if (action.StartsWith("region:") && int.TryParse(action.Substring(7), out int regionId))
                {
                    if (ui.Button(br, "Hududni ko'rish", ButtonKind.Secondary)) { _section = Section.Kingdom; _kingdomTab = 2; _selectedRegion = regionId; }
                }
                else if (action == "market")
                {
                    if (ui.Button(br, "Karvonlarni ko'rish", ButtonKind.Secondary)) _section = Section.Caravans;
                }
                y += card.Height + ui.Gap;
            }
        }

        /// <summary>Draw boshida chaqiriladi — DrawRight kabi to'g'ridan-to'g'ri Typography chaqiruvlari uchun SpriteBatch.</summary>
        public void SetBatch(SpriteBatch sb) => _sbCache = sb;
    }
}
