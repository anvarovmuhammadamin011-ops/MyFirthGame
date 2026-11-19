using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Advisor;
using AndijonSaltanatlar.AIGovernor;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Defense;
using AndijonSaltanatlar.Environment;
using AndijonSaltanatlar.LandExpansion;
using AndijonSaltanatlar.Government;
using AndijonSaltanatlar.Military;
using AndijonSaltanatlar.Progression;
using AndijonSaltanatlar.Units;
using AndijonSaltanatlar.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AndijonSaltanatlar.UI
{
    /// <summary>
    /// Butun interfeys: yuqori sarlavha (nom, shahar, resurslar, kun, tezlik), pastki bo'limlar
    /// (QURISH / SHAHAR / AHOLI / QO'SHIN / DAVLAT / SOZLAMALAR), QURISH menyusi, DAVLAT panelida
    /// iqtisod + soliq boshqaruvi, hukmdorga murojaat oynasi, tanlangan obyekt paneli, toast,
    /// pauza va yordam. MouseOverUI ni ham belgilaydi.
    /// </summary>
    public sealed partial class UIManager
    {
        private static readonly Color PanelBg = new Color(30, 25, 20, 236);
        private static readonly Color PanelBg2 = new Color(50, 42, 33, 242);
        private static readonly Color Border = new Color(150, 116, 66);
        private static readonly Color Gold = new Color(235, 200, 120);
        private static readonly Color Cream = new Color(234, 224, 202);
        private static readonly Color Muted = new Color(176, 160, 134);
        private static readonly Color Green = new Color(120, 214, 132);
        private static readonly Color Red = new Color(236, 112, 96);

        /// <summary>Telefon (sensorli ekran) rejimi: yuqori/pastki panellar va kichik tugmalar barmoq uchun kattaroq.</summary>
        public bool Touch { get; set; }
        private int TopH => Touch ? 44 : 36;
        private int BottomH => Touch ? 58 : 44;
        private int SmallBtn => Touch ? 30 : 22;
        private const int PanelW = 248;

        private enum Tab { None, Build, City, Population, Army, State, Civ, Settings, Diag, Farm, World, Mission, Beautify, Find }

        private readonly AssetManager _assets;
        private readonly PixelFont _font;
        private readonly GameStateManager _state;
        private readonly BuildingManager _buildings;
        private readonly UnitManager _units;
        private readonly Texture2D _px;
        private readonly AIGovernorPanel _governorPanel;

        public Action OnSave;
        public Action OnLoad;
        /// <summary>Google hisobidan chiqish (Sozlamalar paneli).</summary>
        public Action OnLogout;
        /// <summary>Kirgan Google hisobi (Sozlamalar panelida ko'rsatiladi).</summary>
        public string AccountName { get; set; } = "";
        public string PhaseName = "";
        public bool PetitionOpen { get; private set; }
        public bool OpenCivRequested;
        /// <summary>MISSIYA bo'limi bosildi (Game1 missiyalar ro'yxatini ochadi).</summary>
        public bool OpenMissionsRequested;
        /// <summary>BEZASH bosildi (Game1 shaharni rang-barang bezaklar bilan bezaydi).</summary>
        public bool BeautifyRequested;
        /// <summary>TOPISH bosildi (Game1 keyingi binoga kamerani markazlaydi).</summary>
        public bool FindNextRequested;
        /// <summary>BIRLASHTIRISH bosildi (Game1 tanlangan dala/bog'ni yonidagilar bilan birlashtiradi).</summary>
        public bool MergeRequested;
        /// <summary>Katta oyna ochiq (DIAGNOSTIKA, VAQT, murojaat, vazir...) — missiya kartasi ko'rsatilmaydi.</summary>
        public bool BlocksTracker => _tab == Tab.Diag || TimePanelOpen || FarmRequestOpen || PetitionOpen || AdvisorOpen || (_governorPanel?.IsOpen ?? false);
        public bool AdvisorOpen;
        public AIGovernorPanel GovernorPanel => _governorPanel;
        private Rectangle _upgradeBtn, _moveBtn, _enterBtn, _mergeBtn;
        /// <summary>"ICHKARIGA KIRISH" bosildi — Game1 boshqarilayotgan odam bilan interyerga kiradi.</summary>
        public Building EnterRequested { get; set; }
        /// <summary>Devorni tekshirish (ichki xonasi yo'q) so'raldi.</summary>
        public Building InspectRequested { get; set; }
        private Rectangle _onlineBtn;
        private bool _showTitle = true, _showCity = true;
        private string _dayText = "";
        private float _chipsLeft;
        /// <summary>Yuqori paneldagi ONLINE tugmasi bosildi (Game1 onlayn panelni ochadi).</summary>
        public bool OpenOnlineRequested { get; set; }
        /// <summary>Onlayn holat: true — server bilan ulangan va o'yin saqlangan.</summary>
        public bool CloudOk { get; set; } = true;
        private Rectangle _advisorBtn, _advBox, _advActionBtn, _advLaterBtn, _advCloseBtn;

        public void ToggleAdvisor() => AdvisorOpen = !AdvisorOpen;
        public void ToggleGovernor()
        {
            if (_governorPanel == null) { AdvisorOpen = !AdvisorOpen; return; }
            AdvisorOpen = false;
            _governorPanel.Toggle();
        }

        private Tab _tab = Tab.None;
        private Rectangle _topBar, _bottomBar, _rightPanel, _cancelBtn, _panelRect, _saveBtn, _loadBtn, _logoutBtn;
        private bool _rightVisible;

        private readonly (string, Tab)[] _tabs;
        private readonly Rectangle[] _tabRects;
        private readonly List<(CatalogEntry entry, Rectangle rect)> _menuItems = new List<(CatalogEntry, Rectangle)>();

        // Qurilish menyusi kategoriyalari — uzun ro'yxat ekrandan chiqib ketmaydi.
        private static readonly string[] BuildCats = { "BARCHASI", "TURAR JOY", "DEHQONCHILIK", "SAVDO", "HARBIY", "ILM-FAN", "DAVLAT", "MUDOFAA" };
        private readonly Rectangle[] _buildCatRects = new Rectangle[8];
        private int _buildCat;
        private Point _mouse;

        // DAVLAT paneli boshqaruvlari
        private readonly Rectangle[] _taxMinus = new Rectangle[3];
        private readonly Rectangle[] _taxPlus = new Rectangle[3];
        private readonly Rectangle[] _speedBtn = new Rectangle[3];
        private Rectangle _petitionViewBtn;
        private readonly Rectangle[] _recruitBtn = new Rectangle[6];
        private Rectangle _trainBtn;

        // Murojaat oynasi
        private Rectangle _petModal;
        private readonly List<Rectangle> _petChoiceRects = new List<Rectangle>();

        public UIManager(AssetManager assets, PixelFont font, GameStateManager state,
                         BuildingManager buildings, UnitManager units, AIGovernorManager governor = null)
        {
            _assets = assets; _font = font; _state = state;
            _buildings = buildings; _units = units; _px = assets.Pixel;
            if (governor != null)
            {
                _governorPanel = new AIGovernorPanel(assets, font, state, governor);
                _governorPanel.OpenStateTab = () =>
                {
                    OpenDiagnostics(DiagTab.State);
                    _governorPanel.Close();
                };
            }

            // Yagona navigatsiya: shahar ma'lumotlari bitta DIAGNOSTIKA oynasida (SHAHAR/AHOLI/QO'SHIN/DAVLAT),
            // TARAQQIYOT va DUNYO — alohida to'liq ekranlar.
            _tabs = new[]
            {
                ("QURILISH", Tab.Build),
                ("DIAGNOSTIKA", Tab.Diag),
                ("FERMERLIK", Tab.Farm),
                ("MISSIYA", Tab.Mission),
                (Texts.TabCivilization, Tab.Civ),
                (Texts.TabSettings, Tab.Settings),
            };
            _tabRects = new Rectangle[_tabs.Length];
        }

        public void TogglePetition() => PetitionOpen = _state.Petitions.HasActive && !PetitionOpen;

        /// <summary>Ochiq pastki panelni yopadi (Android "Orqaga" / Esc). Yopilgan bo'lsa true.</summary>
        public bool CloseOpenPanel()
        {
            if (_tab == Tab.None) return false;
            if (_tab == Tab.Build) _state.CancelBuild();
            _tab = Tab.None;
            return true;
        }
        public void ClosePetition() => PetitionOpen = false;

        /// <summary>Test/boshlang'ich holat uchun panelni ochadi.</summary>
        public void OpenTab(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "build": case "qurish": _tab = Tab.Build; break;
                case "diag": case "city": case "shahar": OpenDiagnostics(DiagTab.City); break;
                case "pop": case "aholi": OpenDiagnostics(DiagTab.Population); break;
                case "army": case "qoshin": OpenDiagnostics(DiagTab.Army); break;
                case "buy": OpenDiagnostics(DiagTab.Army, buy: true); break;
                case "state": case "davlat": OpenDiagnostics(DiagTab.State); break;
                case "farm": case "fermerlik": _tab = Tab.Farm; break;
                case "time": case "vaqt": TimePanelOpen = true; break;
                case "settings": case "sozlamalar": _tab = Tab.Settings; break;
                case "vazir": case "governor": case "ai": _governorPanel?.Open(); break;
                case "petition": case "murojaat": OpenDiagnostics(DiagTab.State); PetitionOpen = _state.Petitions.HasActive; break;
            }
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------
        private void Layout(int vw, int vh)
        {
            _topBar = new Rectangle(0, 0, vw, TopH);
            _bottomBar = new Rectangle(0, vh - BottomH, vw, BottomH);

            // Pastki tablar ekran eniga moslashadi: hech biri ekrandan chiqib ketmaydi.
            int th = BottomH - 10, gap = 3;
            int cancelW = _state.Mode != GameMode.Playing ? 110 : 0;
            int avail = vw - 16 - (cancelW > 0 ? cancelW + 8 : 0);
            int tw = Math.Max(60, Math.Min(180, (avail - gap * (_tabs.Length - 1)) / _tabs.Length));
            int x = 8, y = vh - BottomH + 5;
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabRects[i] = new Rectangle(x, y, tw, th);
                x += tw + gap;
            }
            _cancelBtn = new Rectangle(x + 5, y, Math.Max(80, cancelW), th);

            if (_state.Mode == GameMode.BuildMode && _tab == Tab.None) _tab = Tab.Build;

            _menuItems.Clear();
            if (_tab == Tab.Build)
            {
                const int bw = 150, bh = 42, gx = 8, gy = 6;
                int maxCols = Math.Max(1, (vw - 24) / 158);
                int panelW = Math.Min(vw - 24, maxCols * (bw + gx) - gx + 12);
                int cols = Math.Min(maxCols, Math.Max(1, (panelW - 12 + gx) / (bw + gx)));
                // Kategoriya filtri olib tashlandi — barcha binolar bitta ro'yxatda ko'rinadi.
                var shown = new List<CatalogEntry>();
                for (int i = 0; i < BuildingCatalog.Buildable.Count; i++)
                    shown.Add(BuildingCatalog.Buildable[i]);
                int rows = Math.Max(1, (shown.Count + cols - 1) / cols);
                int tabsH = 8;
                int panelH = rows * (bh + gy) + tabsH + 12;
                _panelRect = new Rectangle(12, vh - BottomH - panelH - 6, panelW, panelH);
                for (int i = 0; i < _buildCatRects.Length; i++) _buildCatRects[i] = Rectangle.Empty;
                int sx = _panelRect.X + 6, sy = _panelRect.Y + tabsH + 6;
                for (int i = 0; i < shown.Count; i++)
                {
                    int r = i / cols, c = i % cols;
                    _menuItems.Add((shown[i], new Rectangle(sx + c * (bw + gx), sy + r * (bh + gy), bw, bh)));
                }
            }
            else if (_tab == Tab.Diag)
            {
                LayoutDiagnostics(vw, vh);
            }
            else if (_tab == Tab.Farm)
            {
                LayoutFarm(vw, vh);
            }
            else if (_tab == Tab.State)
            {
                _panelRect = new Rectangle(12, vh - BottomH - 196 - 6, 580, 196);
                // Soliq boshqaruvlari (o'ng ustun)
                int tx = _panelRect.X + 300, ty = _panelRect.Y + 44;
                for (int i = 0; i < 3; i++)
                {
                    int sb2 = SmallBtn, step = Touch ? 36 : 28;
                    _taxMinus[i] = new Rectangle(tx + 120, ty + i * step - (Touch ? 4 : 0), sb2, sb2);
                    _taxPlus[i] = new Rectangle(tx + 120 + sb2 + 50, ty + i * step - (Touch ? 4 : 0), sb2, sb2);
                }
                // Tezlik tugmalari (pastki chap)
                int by = _panelRect.Bottom - 34;
                for (int i = 0; i < 3; i++)
                    _speedBtn[i] = new Rectangle(_panelRect.X + 16 + i * 46, by, 42, 24);
                _petitionViewBtn = new Rectangle(_panelRect.X + 300, by, 250, 24);
            }
            else if (_tab == Tab.Army)
            {
                // Chapda statistika, o'ngda yollash tugmalari + mashg'ulot (bir-birining ustiga chiqmaydi).
                int bh = Touch ? 34 : 28, step = bh + 6;
                int ph = Math.Max(236, 40 + 4 * step + 10);
                _panelRect = new Rectangle(12, vh - BottomH - ph - 6, Math.Min(660, vw - 24), ph);
                int rx = _panelRect.X + 310, ry = _panelRect.Y + 40;
                int bw = (_panelRect.Right - 12 - rx - 8) / 2;
                for (int i = 0; i < 6; i++)
                {
                    int col = i % 2, row = i / 2;
                    _recruitBtn[i] = new Rectangle(rx + col * (bw + 8), ry + row * step, bw, bh);
                }
                _trainBtn = new Rectangle(rx, ry + 3 * step, 2 * bw + 8, bh);
            }
            else if (_tab != Tab.None)
            {
                int h = _tab == Tab.Settings ? 150 : 170;
                int pw = Math.Min(460, vw - 24);
                _panelRect = new Rectangle(12, vh - BottomH - h - 6, pw, h);
                int bw = (pw - 32 - 16) / 3;
                _saveBtn = new Rectangle(_panelRect.X + 16, _panelRect.Bottom - 44, bw, 30);
                _loadBtn = new Rectangle(_saveBtn.Right + 8, _panelRect.Bottom - 44, bw, 30);
                _logoutBtn = new Rectangle(_loadBtn.Right + 8, _panelRect.Bottom - 44, bw, 30);
            }

            // Murojaat oynasi
            _petChoiceRects.Clear();
            if (PetitionOpen && _state.Petitions.HasActive)
            {
                int w = 580, h = 250;
                _petModal = new Rectangle(vw / 2 - w / 2, vh / 2 - h / 2, w, h);
                Petition p = _state.Petitions.Active;
                int cy = _petModal.Bottom - 12 - p.Choices.Length * 34;
                for (int i = 0; i < p.Choices.Length; i++)
                    _petChoiceRects.Add(new Rectangle(_petModal.X + 20, cy + i * 34, w - 40, 28));
            }

            if (FarmRequestOpen) LayoutFarmModal(vw, vh);

            _rightVisible = _state.Selected != null;
            _rightPanel = new Rectangle(vw - PanelW, TopH + 8, PanelW - 8, vh - BottomH - TopH - 16);
            _upgradeBtn = Rectangle.Empty;
            _moveBtn = Rectangle.Empty;
            _enterBtn = Rectangle.Empty;
            _mergeBtn = Rectangle.Empty;
            int stackY = _rightPanel.Bottom - 40;
            if (_rightVisible && (_state.Selected is Building || _state.Selected is LandParcel))
                _upgradeBtn = new Rectangle(_rightPanel.X + 14, stackY, _rightPanel.Width - 28, 28);
            if (_rightVisible && _state.Selected is Building selB && GameStateManager.CanRelocate(selB))
                _moveBtn = new Rectangle(_rightPanel.X + 14, stackY - 36, _rightPanel.Width - 28, 28);
            if (_rightVisible && _state.Selected is Building enterable)
                _enterBtn = new Rectangle(_rightPanel.X + 14, stackY - 76, _rightPanel.Width - 28, 30);
            // Dala/bog'ni yonidagisi bilan birlashtirish
            if (_rightVisible && _state.Selected is Building mgBld && (mgBld.Type == BuildingType.Farm || mgBld.Type == BuildingType.Garden))
                _mergeBtn = new Rectangle(_rightPanel.X + 14, stackY - 116, _rightPanel.Width - 28, 28);

            LayoutTopBar(vw);
            LayoutTimePanel(vw, vh);
            if (AdvisorOpen && _state.Advisor.Current != null)
            {
                int w = 600, h = 300;
                _advBox = new Rectangle(vw / 2 - w / 2, vh / 2 - h / 2, w, h);
                _advActionBtn = new Rectangle(_advBox.X + 20, _advBox.Bottom - 42, 230, 28);
                _advLaterBtn = new Rectangle(_advBox.X + 260, _advBox.Bottom - 42, 150, 28);
                _advCloseBtn = new Rectangle(_advBox.Right - 120, _advBox.Bottom - 42, 100, 28);
            }
        }

        // ------------------------------------------------------------------
        // Update
        // ------------------------------------------------------------------
        public void Update(InputManager input, int vw, int vh)
        {
            Layout(vw, vh);
            if (_governorPanel != null && _governorPanel.IsOpen)
            {
                _governorPanel.Update(input, vw, vh);
                return;
            }
            Point m = input.MousePosition;
            _mouse = m;

            bool overPanel = _tab != Tab.None && _panelRect.Contains(m);
            bool overCancel = _state.Mode != GameMode.Playing && _cancelBtn.Contains(m);
            bool overModal = PetitionOpen && _state.Petitions.HasActive && _petModal.Contains(m);
            bool overAdvisor = AdvisorOpen && _advBox.Contains(m);
            input.MouseOverUI = _state.ShowHelp || overModal || overAdvisor || FarmRequestOpen || TimePanelOpen || _topBar.Contains(m) || _bottomBar.Contains(m) ||
                                overPanel || (_rightVisible && _rightPanel.Contains(m));

            if (!input.LeftPressed) return;

            if (_state.ShowHelp) { _state.ShowHelp = false; return; }
            if (ClickTime(m)) return;

            // Murojaat oynasi ochiq bo'lsa — faqat tanlovlar
            if (PetitionOpen && _state.Petitions.HasActive)
            {
                for (int i = 0; i < _petChoiceRects.Count; i++)
                    if (_petChoiceRects[i].Contains(m))
                    {
                        _state.ResolvePetition(i);
                        PetitionOpen = false;
                        _state.ShowMessage(_state.Petitions.LastOutcome);
                        return;
                    }
                return; // oyna ochiq — tashqarini e'tiborsiz qoldiramiz
            }

            // Fermer murojaati — faqat o'yinchi tasdiqlasa xarid qilinadi
            if (ClickFarmModal(m)) return;

            if (AdvisorOpen && _state.Advisor.Current != null)
            {
                AdvisorMessage cur = _state.Advisor.Current;
                if (cur.ActionId != 0 && _advActionBtn.Contains(m)) { _state.DoAdvisorAction(cur); AdvisorOpen = false; return; }
                if (_advLaterBtn.Contains(m) || _advCloseBtn.Contains(m)) { AdvisorOpen = false; return; }
                return;
            }
            if (_onlineBtn.Contains(m)) { OpenOnlineRequested = true; return; }
            if (_advisorBtn.Contains(m))
            {
                if (_governorPanel != null) { AdvisorOpen = false; _governorPanel.Open(); }
                else AdvisorOpen = true;
                return;
            }

            if (_rightVisible && _enterBtn != Rectangle.Empty && _enterBtn.Contains(m) && _state.Selected is Building eb)
            {
                if (eb.Type == BuildingType.Wall)
                {
                    InspectRequested = eb;
                    _state.ShowMessage(eb.Name + ": daraja " + eb.Level + ", mustahkamlik " + eb.HitPoints + "/" + eb.MaxHitPoints
                        + (eb.BuildProgress < 1f ? ", qurilish " + (int)(eb.BuildProgress * 100) + "%" : ""));
                }
                else if (eb.BuildProgress < 1f)
                {
                    _state.ShowMessage(eb.Name + " hali qurilmoqda — " + (int)(eb.BuildProgress * 100) + "% tayyor", true);
                }
                else EnterRequested = eb;
                return;
            }
            if (_rightVisible && _mergeBtn != Rectangle.Empty && _mergeBtn.Contains(m) && _state.Selected is Building)
            {
                MergeRequested = true;
                return;
            }
            if (_rightVisible && _moveBtn != Rectangle.Empty && _moveBtn.Contains(m) && _state.Selected is Building mvb)
            {
                _tab = Tab.None;
                _state.BeginMove(mvb);
                return;
            }
            if (_rightVisible && _upgradeBtn != Rectangle.Empty && _upgradeBtn.Contains(m))
            {
                if (_state.Selected is Building) _state.UpgradeSelected();
                else if (_state.Selected is LandParcel) _state.UnlockSelectedParcel();
                return;
            }

            for (int i = 0; i < _tabRects.Length; i++)
                if (_tabRects[i].Contains(m))
                {
                    Tab t = _tabs[i].Item2;
                    if (t == Tab.Civ) { OpenCivRequested = true; return; } // to'liq ekran
                    if (t == Tab.World) { OpenWorldRequested = true; return; } // saltanat xaritasi (to'liq ekran)
                    if (t == Tab.Mission) { OpenMissionsRequested = true; return; } // MISSIYALAR ro'yxati (to'liq ekran)
                    if (_tab == t) { _tab = Tab.None; if (t == Tab.Build) _state.CancelBuild(); }
                    else _tab = t;
                    return;
                }

            if (overCancel) { _state.CancelBuild(); return; }

            if (_tab == Tab.Diag)
            {
                if (ClickDiagnostics(m)) return;
            }
            else if (_tab == Tab.Farm)
            {
                if (ClickFarm(m)) return;
            }
            else if (_tab == Tab.Build)
            {
                for (int i = 0; i < _buildCatRects.Length; i++)
                    if (_buildCatRects[i] != Rectangle.Empty && _buildCatRects[i].Contains(m))
                    {
                        _buildCat = i;
                        _state.CancelBuild();
                        return;
                    }
                foreach (var (entry, rect) in _menuItems)
                    if (rect.Contains(m))
                    {
                        if (_state.IsBuildingUnlocked(entry.Type)) _state.EnterBuildMode(entry.Type);
                        else _state.ShowMessage("Avval texnologiyani oching (TARAQQIYOT)", true);
                        return;
                    }
            }
            else if (_tab == Tab.State)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (_taxMinus[i].Contains(m)) { AdjustTax(i, -GovernmentPolicy.Step); return; }
                    if (_taxPlus[i].Contains(m)) { AdjustTax(i, +GovernmentPolicy.Step); return; }
                }
                for (int i = 0; i < 3; i++)
                    if (_speedBtn[i].Contains(m)) { _state.SetSpeed(i == 0 ? 1 : i == 1 ? 2 : 4); return; }
                if (_petitionViewBtn.Contains(m) && _state.Petitions.HasActive) { PetitionOpen = true; return; }
            }
            else if (_tab == Tab.Army)
            {
                for (int i = 0; i < 6; i++)
                    if (_recruitBtn[i].Contains(m)) { _state.RecruitUnit(UnitCatalog.All[i].Type); return; }
                if (_trainBtn.Contains(m)) { _state.TrainArmy(); return; }
            }
            else if (_tab == Tab.Settings)
            {
                if (_saveBtn.Contains(m)) { OnSave?.Invoke(); return; }
                if (_loadBtn.Contains(m)) { OnLoad?.Invoke(); return; }
                if (_logoutBtn.Contains(m)) { OnLogout?.Invoke(); return; }
            }
        }

        private void AdjustTax(int i, int d)
        {
            if (i == 0) _state.Policy.AdjustPopulation(d);
            else if (i == 1) _state.Policy.AdjustTrade(d);
            else _state.Policy.AdjustProduction(d);
        }

        // ------------------------------------------------------------------
        // Draw
        // ------------------------------------------------------------------
        public void Draw(SpriteBatch sb, int vw, int vh)
        {
            Layout(vw, vh);
            DrawTopBar(sb, vw);
            DrawBottomBar(sb);
            if (_tab == Tab.Build) DrawBuildMenu(sb);
            else if (_tab == Tab.State) DrawStatePanel(sb);
            else if (_tab == Tab.Army) DrawArmyPanel(sb);
            else if (_tab == Tab.Farm) DrawFarmPanel(sb);
            else if (_tab != Tab.None && _tab != Tab.Diag) DrawInfoPanel(sb);
            if (_rightVisible) DrawSelectedPanel(sb);
            if (_tab == Tab.Diag) DrawDiagnostics(sb);
            DrawToast(sb, vw, vh);
            if (_state.Paused) DrawCenterBadge(sb, vw, vh, Texts.Paused, Gold);
            if (PetitionOpen && _state.Petitions.HasActive) DrawPetitionModal(sb, vw, vh);
            else if (FarmRequestOpen) DrawFarmModal(sb, vw, vh);
            bool govOpen = _governorPanel != null && _governorPanel.IsOpen;
            if (!govOpen && AdvisorOpen && _state.Advisor.Current != null) DrawAdvisorModal(sb, vw, vh);
            DrawTimePanel(sb, vw, vh);
            if (_state.ShowHelp) DrawHelp(sb, vw, vh);
            if (govOpen) _governorPanel.Draw(sb, vw, vh);
        }

        /// <summary>
        /// Yuqori panel joylashuvi: chapda sarlavha + shahar + ONLINE/VAZIR tugmalari, o'ngda resurslar,
        /// o'rtada kun/fasl matni. Joy yetmasa avval kun matni qisqaradi, keyin sarlavha va shahar nomi
        /// yashiriladi — elementlar hech qachon bir-birining ustiga chiqmaydi.
        /// </summary>
        private void LayoutTopBar(int vw)
        {
            int btnW = Touch ? 104 : 96, btnH = Touch ? 32 : 24;
            const int pad = 12;
            float chips = ChipWidth(_state.Population.ToString()) + ChipWidth(_state.Food.ToString()) + ChipWidth(_state.Coins.ToString());
            _chipsLeft = vw - 16 - chips;
            float titleW = _font.Measure(Texts.Title, 3, 1).X;
            float cityW = _font.Measure(GameConfig.CityName, 2, 1).X;

            // Soat: "08:30 | BAHOR | 3-KUN" (bosilsa VAQT VA MUHIT oynasi ochiladi)
            string spd = "  " + _state.Speed + "x" + (_state.Paused ? " ||" : "");
            string full = ClockLabel(true) + spd;
            string mid = ClockLabel(true);
            string tiny = ClockLabel(false);

            float LeftWidth(bool title, bool city) => 14 + (title ? titleW + pad : 0) + (city ? cityW + pad : 0) + 2 * btnW + 8;
            float Room(string d) => _chipsLeft - 18 - _font.Measure(d, 2, 1).X;

            _dayText = "";
            foreach (var (title, city) in new[] { (true, true), (false, true), (false, false) })
            {
                _showTitle = title; _showCity = city;
                float left = LeftWidth(title, city);
                if (left <= Room(full)) { _dayText = full; break; }
                if (left <= Room(mid)) { _dayText = mid; break; }
                if (left <= Room(tiny)) { _dayText = tiny; break; }
            }

            float dw = _dayText.Length > 0 ? _font.Measure(_dayText, 2, 1).X : 0f;
            _clockRect = dw > 0 ? new Rectangle((int)(_chipsLeft - 6 - dw - 8), 2, (int)dw + 16, TopH - 4) : Rectangle.Empty;
            float bx = 14 + (_showTitle ? titleW + pad : 0) + (_showCity ? cityW + pad : 0);
            int by = (TopH - btnH) / 2;
            _onlineBtn = new Rectangle((int)bx, by, btnW, btnH);
            _advisorBtn = new Rectangle((int)bx + btnW + 8, by, btnW, btnH);
        }

        private float ChipWidth(string value) => 22 + 6 + _font.Measure(value, 2, 1).X + 18;

        private void DrawTopBar(SpriteBatch sb, int vw)
        {
            Fill(sb, _topBar, PanelBg);
            Fill(sb, new Rectangle(0, TopH, vw, 2), Border);
            float x = 14;
            if (_showTitle) x += _font.Draw(sb, Texts.Title, new Vector2(x, (TopH - 21) / 2), Gold, 3, 1) + 12;
            if (_showCity) _font.Draw(sb, GameConfig.CityName, new Vector2(x, (TopH - 14) / 2), Cream, 2, 1);

            Color ok = new Color(110, 200, 120);
            Panel(sb, _onlineBtn, PanelBg2, CloudOk ? ok : Red, 1);
            DrawButtonText(sb, "ONLINE", _onlineBtn, CloudOk ? Cream : Red);
            // Holat nuqtasi: yashil — server bilan ulangan va saqlangan, qizil — ulanmagan.
            Fill(sb, new Rectangle(_onlineBtn.Right - 8, _onlineBtn.Y + 3, 4, 4), CloudOk ? ok : Red);

            bool urgent = _state.Advisor.HasUrgent;
            Panel(sb, _advisorBtn, urgent ? new Color(64, 52, 28, 240) : PanelBg2, urgent ? Gold : Border, 1);
            DrawButtonText(sb, "VAZIR" + (urgent ? " !" : ""), _advisorBtn, urgent ? Gold : Cream);

            float rx = vw - 16;
            rx = DrawChip(sb, rx, SpriteKeys.IconPopulation, _state.Population.ToString(), Cream);
            rx = DrawChip(sb, rx, SpriteKeys.IconFood, _state.Food.ToString(), Cream);
            rx = DrawChip(sb, rx, SpriteKeys.IconCoin, _state.Coins.ToString(), Gold);
            if (_dayText.Length > 0)
            {
                Panel(sb, _clockRect, TimePanelOpen ? PanelBg2 : new Color(40, 33, 26, 236), TimePanelOpen ? Gold : new Color(96, 78, 52), 1);
                _font.DrawRight(sb, _dayText, new Vector2(rx - 6, (TopH - 14) / 2f), TimePrefs.Current.Lock != TimeLock.Auto ? Gold : Cream, 2, 1);
            }
        }

        private float DrawChip(SpriteBatch sb, float right, string iconKey, string value, Color valueColor)
        {
            const int icon = 22;
            Vector2 tsize = _font.Measure(value, 2, 1);
            float w = icon + 6 + tsize.X;
            float x = right - w;
            sb.Draw(_assets.Get(iconKey), new Rectangle((int)x, (TopH - icon) / 2, icon, icon), Color.White);
            _font.Draw(sb, value, new Vector2(x + icon + 6, (TopH - tsize.Y) / 2f), valueColor, 2, 1);
            return x - 18;
        }

        private void DrawBottomBar(SpriteBatch sb)
        {
            Fill(sb, _bottomBar, PanelBg);
            Fill(sb, new Rectangle(0, _bottomBar.Y - 2, _bottomBar.Width, 2), Border);

            for (int i = 0; i < _tabs.Length; i++)
            {
                bool active = _tab == _tabs[i].Item2;
                Panel(sb, _tabRects[i], active ? PanelBg2 : PanelBg, active ? Gold : Border, active ? 2 : 1);
                DrawButtonText(sb, _tabs[i].Item1, _tabRects[i], active ? Gold : Cream);
                // DAVLAT tabida murojaat belgisi
                if ((_tabs[i].Item2 == Tab.Diag && _state.Petitions.HasActive) || (_tabs[i].Item2 == Tab.Farm && _state.Farming.Active != null))
                    Fill(sb, new Rectangle(_tabRects[i].Right - 12, _tabRects[i].Y + 4, 8, 8), Gold);
            }

            if (_state.Mode != GameMode.Playing)
            {
                Panel(sb, _cancelBtn, PanelBg2, Red, 1);
                DrawButtonText(sb, Texts.Cancel, _cancelBtn, Cream);
            }
        }

        /// <summary>Qurilish kategoriyasi indeksi (0 — barchasi).</summary>
        private static int BuildCatOf(CatalogEntry e) => e.Type switch
        {
            BuildingType.House => 1,
            BuildingType.Garden or BuildingType.Farm => 2,
            BuildingType.Market or BuildingType.Caravanserai => 3,
            BuildingType.Barracks or BuildingType.GladiatorArena => 4,
            BuildingType.Madrasa or BuildingType.Mosque => 5,
            BuildingType.MainHall => 6,
            BuildingType.Tower or BuildingType.Gate or BuildingType.Wall => 7,
            _ => 0
        };

        private void DrawBuildMenu(SpriteBatch sb)
        {
            Panel(sb, _panelRect, PanelBg, Border, 2);
            foreach (var (entry, rect) in _menuItems)
            {
                bool unlocked = _state.IsBuildingUnlocked(entry.Type);
                bool active = _state.Mode == GameMode.BuildMode && _state.SelectedBuildType == entry.Type;
                bool affordable = _state.Coins >= entry.Cost;
                Panel(sb, rect, !unlocked ? new Color(26, 26, 34, 245) : active ? PanelBg2 : PanelBg, active ? Gold : Border, active ? 2 : 1);
                var iconBox = new Rectangle(rect.X + 5, rect.Y + 5, 32, rect.Height - 10);
                DrawFit(sb, _assets.Get(entry.IconKey), iconBox);
                int tx = iconBox.Right + 6;
                _font.Draw(sb, FitText(entry.Name, rect.Right - tx - 4), new Vector2(tx, rect.Y + 6), !unlocked ? Muted : active ? Gold : Cream, 2, 1);
                if (!unlocked)
                {
                    _font.Draw(sb, "QULF", new Vector2(tx, rect.Y + 25), Muted, 2, 1);
                }
                else
                {
                    sb.Draw(_assets.Get(SpriteKeys.IconCoin), new Rectangle(tx, rect.Y + 24, 14, 14), Color.White);
                    _font.Draw(sb, entry.Cost.ToString(), new Vector2(tx + 18, rect.Y + 25), affordable ? Cream : Red, 2, 1);
                }
            }

            // Uzun nomlar va tavsif — to'liq matn bilan ko'rsatiladi (qisqartirish yo'q)
            foreach (var (entry, rect) in _menuItems)
            {
                if (!rect.Contains(_mouse)) continue;
                var lines = Wrap(entry.Name + " — " + entry.Description, Math.Max(180, Math.Min(440, _panelRect.Width - 24)), 2);
                int tw = 0;
                foreach (string l in lines) tw = Math.Max(tw, (int)_font.Measure(l, 2).X);
                int tht = lines.Count * 18 + 14;
                var tr = new Rectangle(_panelRect.X + 8, Math.Max(6, _panelRect.Y - tht - 8), tw + 20, tht);
                tr.X = Math.Clamp(tr.X, 8, Math.Max(8, _panelRect.Right - tr.Width));
                Panel(sb, tr, new Color(22, 18, 15, 248), Gold, 1);
                int ty2 = tr.Y + 7;
                foreach (string l in lines) { _font.Draw(sb, l, new Vector2(tr.X + 10, ty2), Cream, 2, 1); ty2 += 18; }
                break;
            }
        }

        private void DrawStatePanel(SpriteBatch sb)
        {
            Panel(sb, _panelRect, PanelBg, Border, 2);
            var econ = _state.Economy;
            int x = _panelRect.X + 16, y = _panelRect.Y + 12;

            _font.Draw(sb, Texts.TabState, new Vector2(x, y), Gold, 2, 1);
            y += 26;
            string[] left =
            {
                "Xazina: " + _state.Coins + " tanga",
                "Kunlik daromad: " + econ.LastIncome,
                "Kunlik xarajat: " + econ.LastExpense,
                "Ishlab chiqarish: " + econ.LastProduction,
                "Oziq-ovqat: +" + econ.LastFoodProduced + " / -" + econ.LastFoodConsumed,
                "Bandlik: " + Employment() + "%",
            };
            foreach (string l in left) { _font.Draw(sb, l, new Vector2(x, y), Cream, 2, 1); y += 18; }

            // Soliqlar (o'ng ustun) — qisqa yorliqlar, tugmalar ustma-ust tushmasligi uchun
            int tx = _panelRect.X + 300, ty = _panelRect.Y + 12;
            _font.Draw(sb, "Soliqlar (%)", new Vector2(tx, ty), Gold, 2, 1);
            string[] taxNames = { "Aholi", "Savdo", "Ishlab" };
            int[] taxVals = { _state.Policy.PopulationTax, _state.Policy.TradeTax, _state.Policy.ProductionTax };
            for (int i = 0; i < 3; i++)
            {
                int ry = _panelRect.Y + 44 + i * 28;
                _font.Draw(sb, taxNames[i], new Vector2(tx, ry + 4), Cream, 2, 1);
                Panel(sb, _taxMinus[i], PanelBg2, Border, 1);
                _font.DrawCentered(sb, "-", _taxMinus[i].Center.ToVector2(), Cream, 2, 1);
                // Aholi solig'i g'alayon chegarasida (30% dan oshsa) — qizil: xalq saroy oldiga chiqadi
                bool riot = i == 0 && AndijonSaltanatlar.CityLife.TaxRiotSystem.TaxTooHigh(taxVals[i]);
                _font.DrawCentered(sb, taxVals[i] + "%", new Vector2((_taxMinus[i].Right + _taxPlus[i].X) / 2f, _taxMinus[i].Center.Y), riot ? Red : Gold, 2, 1);
                Panel(sb, _taxPlus[i], PanelBg2, Border, 1);
                _font.DrawCentered(sb, "+", _taxPlus[i].Center.ToVector2(), Cream, 2, 1);
            }

            // Tezlik tugmalari
            int[] speeds = { 1, 2, 4 };
            for (int i = 0; i < 3; i++)
            {
                bool on = _state.Speed == speeds[i];
                Panel(sb, _speedBtn[i], on ? PanelBg2 : PanelBg, on ? Gold : Border, on ? 2 : 1);
                _font.DrawCentered(sb, speeds[i] + "x", _speedBtn[i].Center.ToVector2(), on ? Gold : Cream, 2, 1);
            }

            // Murojaatni ko'rish
            bool has = _state.Petitions.HasActive;
            Panel(sb, _petitionViewBtn, PanelBg2, has ? Gold : Border, 1);
            _font.DrawCentered(sb, has ? "Murojaatni ko'rish (!)" : "Murojaat yo'q",
                _petitionViewBtn.Center.ToVector2(), has ? Gold : Muted, 2, 1);
        }

        private int Employment()
        {
            int c = _state.Pop.Count;
            return c <= 0 ? 0 : _state.Pop.Workers * 100 / c;
        }

        private void DrawArmyPanel(SpriteBatch sb)
        {
            Panel(sb, _panelRect, PanelBg, Border, 2);
            ArmyManager mil = _state.Military;
            int x = _panelRect.X + 16, y = _panelRect.Y + 12;
            _font.Draw(sb, Texts.TabArmy, new Vector2(x, y), Gold, 2, 1); y += 24;

            int def = CityDefenseManager.Compute(_buildings, mil.TotalStrength);
            string[] left =
            {
                "Jami jangchilar: " + mil.TotalUnits,
                "Qo'shin kuchi: " + mil.TotalStrength,
                "Kunlik xarajat: " + mil.TotalUpkeep,
                "Navbatda: " + mil.QueueCount,
                "Shahar mudofaasi: " + def,
                "Janglar: " + _state.World.Battles.History.Count,
            };
            foreach (string l in left) { _font.Draw(sb, l, new Vector2(x, y), Cream, 2, 1); y += 17; }
            y += 4;
            string counts = "";
            foreach (UnitDef d in UnitCatalog.All) counts += d.NameUz + ":" + mil.Count(d.Type) + "  ";
            foreach (string line in Wrap(counts.Trim(), 280, 2)) { if (y > _panelRect.Bottom - 20) break; _font.Draw(sb, line, new Vector2(x, y), Muted, 2, 1); y += 16; }

            _font.Draw(sb, "YOLLASH", new Vector2(_panelRect.X + 300, _panelRect.Y + 12), Gold, 2, 1);
            for (int i = 0; i < 6; i++)
            {
                UnitDef d = UnitCatalog.All[i];
                bool afford = _state.Coins >= d.TrainingCost;
                Panel(sb, _recruitBtn[i], PanelBg2, Border, 1);
                string cost = d.TrainingCost.ToString();
                int costW = (int)_font.Measure(cost, 2, 1).X;
                float ty = _recruitBtn[i].Y + (_recruitBtn[i].Height - 14) / 2f;
                _font.Draw(sb, FitText(d.NameUz, _recruitBtn[i].Width - costW - 20), new Vector2(_recruitBtn[i].X + 6, ty), afford ? Cream : Red, 2, 1);
                _font.DrawRight(sb, cost, new Vector2(_recruitBtn[i].Right - 6, ty), afford ? Gold : Red, 2, 1);
            }
            Panel(sb, _trainBtn, PanelBg2, Gold, 1);
            DrawButtonText(sb, "Mashg'ulot (20)", _trainBtn, Cream);
        }

        private void DrawInfoPanel(SpriteBatch sb)
        {
            Panel(sb, _panelRect, PanelBg, Border, 2);
            int x = _panelRect.X + 16, y = _panelRect.Y + 12;
            string header;
            var lines = new List<string>();

            switch (_tab)
            {
                case Tab.City:
                    header = Texts.TabCity;
                    lines.Add("Shahar: " + GameConfig.CityName);
                    lines.Add(GameConfig.RulerName);
                    lines.Add(Texts.Day + ": " + _state.Day + " (" + PhaseName + ")  Oy: " + _state.Clock.Month + "  Yil: " + _state.Clock.Year);
                    lines.Add("Binolar: " + _buildings.Buildings.Count + "   Aholi: " + _state.Population);
                    break;
                case Tab.Population:
                    header = Texts.TabPopulation;
                    var pop = _state.Pop;
                    lines.Add("Jami aholi: " + pop.Count + "   Oilalar: " + pop.Families);
                    lines.Add("Ishchilar: " + pop.Workers + "   Ishsizlar: " + pop.Unemployed);
                    lines.Add("Uy-joy: " + pop.HousingUsed + " / " + pop.HousingCapacity);
                    lines.Add("Baxt: " + (int)pop.AvgHappiness + "   Sog'liq: " + (int)pop.AvgHealth);
                    lines.Add("Ta'lim: " + (int)pop.AvgEducation + "   Xavfsizlik: " + (int)pop.AvgSecurity);
                    break;
                case Tab.Army:
                    header = Texts.TabArmy;
                    CountArmy(out int spear, out int shield, out int sword, out int patrol);
                    lines.Add("Nayzali soqchi: " + spear);
                    lines.Add("Qalqonli jangchi: " + shield);
                    lines.Add("Qilichli soqchi: " + sword);
                    lines.Add("Patrul askari: " + patrol);
                    lines.Add("Jami qo'shin: " + (spear + shield + sword + patrol));
                    break;
                default:
                    header = Texts.TabSettings;
                    lines.Add("Hisob: " + (string.IsNullOrEmpty(AccountName) ? "-" : AccountName));
                    lines.Add("O'yin serverda avtomatik saqlanadi.");
                    lines.Add("Istalgan qurilmadan davom ettiring.");
                    break;
            }

            _font.Draw(sb, header, new Vector2(x, y), Gold, 3, 1);
            y += 28;
            foreach (string line in lines) { _font.Draw(sb, FitText(line, _panelRect.Width - 32), new Vector2(x, y), Cream, 2, 1); y += 21; }

            if (_tab == Tab.Settings)
            {
                Panel(sb, _saveBtn, PanelBg2, Gold, 1);
                DrawButtonText(sb, "SAQLASH", _saveBtn, Cream);
                Panel(sb, _loadBtn, PanelBg2, Gold, 1);
                DrawButtonText(sb, "YUKLASH", _loadBtn, Cream);
                Panel(sb, _logoutBtn, PanelBg2, Red, 1);
                DrawButtonText(sb, "CHIQISH", _logoutBtn, Cream);
            }
        }

        private void DrawPetitionModal(SpriteBatch sb, int vw, int vh)
        {
            Fill(sb, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 150));
            Petition p = _state.Petitions.Active;
            Panel(sb, _petModal, PanelBg, Gold, 2);
            int x = _petModal.X + 20, y = _petModal.Y + 16;
            _font.Draw(sb, Texts.Petition + ": " + p.Title, new Vector2(x, y), Gold, 2, 1);
            y += 26;
            foreach (string line in Wrap(p.Body, _petModal.Width - 40, 2)) { _font.Draw(sb, line, new Vector2(x, y), Cream, 2, 1); y += 18; }

            for (int i = 0; i < _petChoiceRects.Count; i++)
            {
                Panel(sb, _petChoiceRects[i], PanelBg2, Border, 1);
                _font.DrawCentered(sb, p.Choices[i], _petChoiceRects[i].Center.ToVector2(), Cream, 2, 1);
            }
        }

        private void CountArmy(out int spear, out int shield, out int sword, out int patrol)
        {
            spear = shield = sword = patrol = 0;
            foreach (Unit u in _units.Units)
            {
                if (u is CitizenUnit) continue;
                if (u is SpearmanUnit) spear++;
                else if (u is GuardUnit) shield++;
                else if (u is SwordsmanUnit) sword++;
                else if (u is PatrolUnit) patrol++;
            }
        }

        private void DrawSelectedPanel(SpriteBatch sb)
        {
            Panel(sb, _rightPanel, PanelBg, Border, 2);
            int x = _rightPanel.X + 14;
            int y = _rightPanel.Y + 14;
            int maxW = _rightPanel.Width - 28;

            string title = "", subtitle = "", desc = "";
            var rows = new List<(string, Color)>();

            switch (_state.Selected)
            {
                case Building b:
                    title = b.Name;
                    subtitle = Building.CategoryName(b.Category);
                    desc = b.Description;
                    rows.Add(("Daraja: " + b.Level + "/" + BuildingUpgradeSystem.MaxLevel + " (" + BuildingUpgradeSystem.LevelName(b) + ")", Gold));
                    if (BuildingUpgradeSystem.CanUpgrade(b))
                        rows.Add(("Keyingi: " + Buildings.BuildingUpgradeEffects.LevelName(b.Type, b.Level + 1) + " — " + Buildings.BuildingUpgradeEffects.Benefit(b.Type) + " oshadi", Green));
                    rows.Add((Texts.HitPoints + ": " + b.HitPoints + "/" + b.MaxHitPoints, Cream));
                    if (b.Cost > 0) rows.Add((Texts.Cost + ": " + b.Cost + " " + Texts.Coin, Muted));
                    if (b.PopulationCapacity > 0) rows.Add((Texts.Capacity + ": +" + b.PopulationCapacity + " " + Texts.Population, Green));
                    rows.Add((b.BuildProgress < 1f ? "Holat: qurilmoqda (" + (int)(b.BuildProgress * 100) + "%)" : "Holat: tayyor", b.BuildProgress < 1f ? Red : Green));
                    break;
                case Unit u:
                    title = u.Name;
                    subtitle = Unit.StateName(u.State);
                    desc = u.Description;
                    rows.Add((Texts.HitPoints + ": " + u.HitPoints + "/" + u.MaxHitPoints, Cream));
                    rows.Add((Texts.Attack + ": " + u.Attack, Muted));
                    if (u.CanRank)
                    {
                        rows.Add(("Unvon: " + AndijonSaltanatlar.Military.MilitaryRank.Name(u.Rank), Gold));
                        rows.Add((u.Rank >= AndijonSaltanatlar.Military.MilitaryRank.Max
                            ? "XP: " + u.Experience + " (eng yuqori unvon)"
                            : "XP: " + u.Experience + " / " + AndijonSaltanatlar.Military.MilitaryRank.NextThreshold(u.Rank), Muted));
                    }
                    if (u.Team == UnitTeam.Enemy) rows.Add(("DUSHMAN", Red));
                    if (!u.IsDown) rows.Add(("[F] ICHIGA KIRISH", Gold));
                    else rows.Add(("Yiqilgan", Red));
                    break;
                case Decoration d:
                    title = d.Name;
                    subtitle = "Bezak";
                    desc = d.Description;
                    break;
                case LandParcel lp:
                    title = lp.NameUz;
                    subtitle = lp.StatusText();
                    desc = lp.Ring == 1
                        ? "Shahar qurilish hududi (mahalla). 1-halqa: 4 tomoni ochilgach tashqi yerlar ochiladi."
                        : lp.Ring + "-halqa yeri. Vazifasi: " + lp.PurposeUz.ToLowerInvariant() + ". Halqaning 4 tomoni ochilsa keyingi halqa ochiladi.";
                    rows.Add(("Halqa: " + lp.Ring + " / " + _state.Land.RingCount, Cream));
                    rows.Add(("Vazifa: " + lp.PurposeUz, Cream));
                    rows.Add(("Maydon: " + lp.GridBounds.Width + " x " + lp.GridBounds.Height + " katak", Cream));
                    rows.Add(("Narx: " + lp.UnlockCost + " tanga", Muted));
                    rows.Add(("Talab: " + lp.RequiredPopulation + " aholi", Muted));
                    break;
            }

            // Sarlavha panelga sig'masa kichikroq shrift, baribir sig'masa ikki qatorga bo'linadi.
            if (_font.Measure(title, 3, 1).X <= maxW) { _font.Draw(sb, title, new Vector2(x, y), Gold, 3, 1); y += 26; }
            else
            {
                foreach (string line in Wrap(title, maxW, 2)) { _font.Draw(sb, line, new Vector2(x, y), Gold, 2, 1); y += 19; }
                y += 6;
            }
            foreach (string line in Wrap(subtitle, maxW, 2)) { _font.Draw(sb, line, new Vector2(x, y), Muted, 2, 1); y += 19; }
            y += 1;
            Fill(sb, new Rectangle(x, y, maxW, 1), Border); y += 10;
            foreach (string line in Wrap(desc, maxW, 2)) { if (y > _rightPanel.Bottom - 96) break; _font.Draw(sb, line, new Vector2(x, y), Cream, 2, 1); y += 19; }
            y += 8;
            foreach (var (text, color) in rows)
                foreach (string line in Wrap(text, maxW, 2)) { if (y > _rightPanel.Bottom - 96) break; _font.Draw(sb, line, new Vector2(x, y), color, 2, 1); y += 19; }

            if (_enterBtn != Rectangle.Empty)
            {
                Panel(sb, _enterBtn, PanelBg2, Gold, 2);
                string label = "ICHKARIGA KIRISH";
                if (_state.Selected is Building eb)
                {
                    if (eb.Type == BuildingType.Wall) label = "DEVORNI TEKSHIRISH";
                    else if (eb.BuildProgress < 1f) label = "QURILMOQDA...";
                    else if (eb.Type == BuildingType.Garden || eb.Type == BuildingType.Farm || eb.Type == BuildingType.Barracks
                             || eb.Type == BuildingType.GladiatorArena || eb.Type == BuildingType.Caravanserai || eb.Type == BuildingType.Gate)
                        label = "HUDUDGA KIRISH";
                }
                DrawButtonText(sb, label, _enterBtn, Gold);
            }
            if (_moveBtn != Rectangle.Empty)
            {
                Panel(sb, _moveBtn, PanelBg2, Border, 1);
                DrawButtonText(sb, "KO'CHIRISH", _moveBtn, Cream);
            }
            if (_mergeBtn != Rectangle.Empty)
            {
                Panel(sb, _mergeBtn, PanelBg2, Gold, 1);
                DrawButtonText(sb, "BIRLASHTIRISH", _mergeBtn, Cream);
            }
            if (_state.Selected is Building bld && BuildingUpgradeSystem.CanUpgrade(bld))
            {
                Panel(sb, _upgradeBtn, PanelBg2, Gold, 1);
                DrawButtonText(sb, "KATTALASHTIRISH (" + BuildingUpgradeSystem.UpgradeCost(bld) + ")", _upgradeBtn, Cream);
            }
            else if (_state.Selected is Building maxB && maxB.Level >= BuildingUpgradeSystem.MaxLevel)
            {
                Panel(sb, _upgradeBtn, PanelBg, Border, 1);
                DrawButtonText(sb, "ENG YUQORI DARAJA", _upgradeBtn, Muted);
            }
            else if (_state.Selected is LandParcel lpb && lpb.State != LandParcelState.Open)
            {
                Panel(sb, _upgradeBtn, PanelBg2, Gold, 1);
                DrawButtonText(sb, (lpb.WaitingForPreviousRing ? "QULFLANGAN (" : "YERNI OCHISH (") + lpb.UnlockCost + ")", _upgradeBtn, lpb.WaitingForPreviousRing ? Muted : Cream);
            }
        }

        private void DrawAdvisorModal(SpriteBatch sb, int vw, int vh)
        {
            Fill(sb, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 150));
            AdvisorMessage c = _state.Advisor.Current;
            Panel(sb, _advBox, PanelBg, Gold, 2);
            int x = _advBox.X + 20, y = _advBox.Y + 16;
            _font.Draw(sb, "DONISHMAND VAZIR", new Vector2(x, y), Gold, 3, 1); y += 30;
            _font.Draw(sb, "[" + AdvisorMessage.CategoryName(c.Category) + "]  " + c.Title,
                new Vector2(x, y), c.Priority >= 90 ? Red : Gold, 2, 1); y += 24;
            foreach (string line in Wrap(c.Body, _advBox.Width - 40, 2)) { _font.Draw(sb, line, new Vector2(x, y), Cream, 2, 1); y += 18; }

            y += 10;
            _font.Draw(sb, "BOSHQA MASLAHATLAR:", new Vector2(x, y), Muted, 2, 1); y += 18;
            int shown = 0;
            foreach (AdvisorMessage mm in _state.Advisor.Messages)
            {
                if (mm == c || shown >= 3) continue;
                _font.Draw(sb, "- " + mm.Title, new Vector2(x, y), Cream, 2, 1); y += 16; shown++;
            }

            if (c.ActionId != 0)
            {
                Panel(sb, _advActionBtn, PanelBg2, Gold, 1);
                _font.DrawCentered(sb, c.ActionLabel, _advActionBtn.Center.ToVector2(), Gold, 2, 1);
            }
            Panel(sb, _advLaterBtn, PanelBg2, Border, 1);
            _font.DrawCentered(sb, "Keyinroq", _advLaterBtn.Center.ToVector2(), Cream, 2, 1);
            Panel(sb, _advCloseBtn, PanelBg2, Border, 1);
            _font.DrawCentered(sb, "Yopish", _advCloseBtn.Center.ToVector2(), Cream, 2, 1);
        }

        private void DrawToast(SpriteBatch sb, int vw, int vh)
        {
            if (_state.MessageTimer <= 0f || string.IsNullOrEmpty(_state.Message)) return;
            Color c = _state.MessageIsError ? Red : Green;
            Vector2 size = _font.Measure(_state.Message, 2, 1);
            int pad = 12;
            var box = new Rectangle((int)(vw / 2f - size.X / 2f) - pad, vh - BottomH - 210,
                                    (int)size.X + pad * 2, (int)size.Y + pad);
            Panel(sb, box, PanelBg2, c, 1);
            _font.DrawCentered(sb, _state.Message, box.Center.ToVector2(), c, 2, 1);
        }

        private void DrawCenterBadge(SpriteBatch sb, int vw, int vh, string text, Color c)
        {
            Vector2 size = _font.Measure(text, 3, 1);
            int pad = 16;
            var box = new Rectangle((int)(vw / 2f - size.X / 2f) - pad, (int)(vh * 0.14f),
                                    (int)size.X + pad * 2, (int)size.Y + pad);
            Panel(sb, box, PanelBg2, c, 2);
            _font.DrawCentered(sb, text, box.Center.ToVector2(), c, 3, 1);
        }

        private void DrawHelp(SpriteBatch sb, int vw, int vh)
        {
            Fill(sb, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 150));
            int w = 580, h = 70 + Texts.HelpLines.Length * 22;
            var box = new Rectangle(vw / 2 - w / 2, vh / 2 - h / 2, w, h);
            Panel(sb, box, PanelBg, Gold, 2);
            _font.DrawCentered(sb, Texts.Help + " - " + Texts.Title, new Vector2(box.Center.X, box.Y + 24), Gold, 2, 1);
            int y = box.Y + 54;
            foreach (string line in Texts.HelpLines) { _font.Draw(sb, line, new Vector2(box.X + 26, y), Cream, 2, 1); y += 22; }
        }

        // ------------------------------------------------------------------
        // Chizish yordamchilari
        // ------------------------------------------------------------------
        private void Fill(SpriteBatch sb, Rectangle r, Color c) => sb.Draw(_px, r, c);

        private void Panel(SpriteBatch sb, Rectangle r, Color bg, Color border, int thickness)
        {
            Fill(sb, r, bg);
            Fill(sb, new Rectangle(r.X, r.Y, r.Width, thickness), border);
            Fill(sb, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), border);
            Fill(sb, new Rectangle(r.X, r.Y, thickness, r.Height), border);
            Fill(sb, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), border);
        }

        private void DrawFit(SpriteBatch sb, Texture2D tex, Rectangle box)
        {
            float scale = MathF.Min(box.Width / (float)tex.Width, box.Height / (float)tex.Height);
            int w = (int)(tex.Width * scale), h = (int)(tex.Height * scale);
            var dest = new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
            sb.Draw(tex, dest, Color.White);
        }

        /// <summary>Matnni berilgan kenglikka sig'diradi: sig'masa oxiri ".." bilan qisqartiriladi.</summary>
        private string FitText(string text, int maxPx, int scale = 2)
        {
            if (string.IsNullOrEmpty(text) || _font.Measure(text, scale, 1).X <= maxPx) return text ?? "";
            string t = text;
            while (t.Length > 1 && _font.Measure(t + "..", scale, 1).X > maxPx) t = t.Substring(0, t.Length - 1);
            return t.TrimEnd() + "..";
        }

        /// <summary>Tugma ichiga markazlangan, chetdan chiqmaydigan matn.</summary>
        private void DrawButtonText(SpriteBatch sb, string text, Rectangle r, Color c)
            => _font.DrawCentered(sb, FitText(text, r.Width - 10), r.Center.ToVector2(), c, 2, 1);

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
