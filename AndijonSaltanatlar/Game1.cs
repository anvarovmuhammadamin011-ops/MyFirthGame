using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AndijonSaltanatlar.AIGovernor;
using AndijonSaltanatlar.Battles;
using AndijonSaltanatlar.Blueprints;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Progression;
using AndijonSaltanatlar.Campaigns;
using AndijonSaltanatlar.CityPlanning;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Environment;
using AndijonSaltanatlar.Graphics;
using AndijonSaltanatlar.LandExpansion;
using AndijonSaltanatlar.SaveSystem;
using AndijonSaltanatlar.SmartConstruction;
using AndijonSaltanatlar.Technology;
using AndijonSaltanatlar.UI;
using AndijonSaltanatlar.Units;
using AndijonSaltanatlar.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AndijonSaltanatlar;

public sealed class Game1 : Game
{
	private readonly GraphicsDeviceManager _graphics;

	private readonly LaunchOptions _opt;

	private SpriteBatch _sb;

	private AssetManager _assets;

	private PixelFont _font;

	private MapManager _map;

	private BuildingManager _buildings;

	private UnitManager _units;

	private CameraManager _camera;

	private Core.PerformanceMonitor _perf;

	private InputManager _input;

	private GameStateManager _state;

	private ConstructionSystem _construction;

	// Jang: istalgan jangchi/fuqaro ichiga kirish (F) + shaharga bosqin (B / avtomatik)
	private AndijonSaltanatlar.Combat.CombatSystem _combat;

	private AndijonSaltanatlar.NPC.CitizenSocialSystem _citizenSocial;

	private AndijonSaltanatlar.CityLife.CitizenSocialSimulation _citizenLife;

	private AndijonSaltanatlar.NPC.NPCInteractionManager _npcInteractions;

	private GladiatorUnit _arenaGladiatorOne;

	private GladiatorUnit _arenaGladiatorTwo;

	private int _arenaBuildingId;

	// Uyga kirish: boshqarilayotgan odam uy yonida ENTER bossa — ichki xona (yotoqxona + oshxona)
	private AndijonSaltanatlar.Interiors.HouseInterior _interior;

	// Parrandalar: kaptarlar, chumchuqlar, laylaklar (bezak, odam yaqinlashsa uchib ketadi)
	private BirdSystem _birds;

	// Chorva: cho'ponlarning qo'y, echki va mollari (har kuni ko'payadi)
	private LivestockSystem _livestock;

	private AndijonSaltanatlar.World.CurfewController _curfew;

	private AndijonSaltanatlar.World.CartSystem _carts;

	// Soliq g'alayoni: aholi solig'i 30%+ bo'lsa xalq saroy oldiga chiqib tosh otadi
	private AndijonSaltanatlar.CityLife.TaxRiotSystem _taxRiot;

	// Fasllar ko'rinishi: bahor guli, kuz xazoni, qish qori (yer, daraxt, tomlar)

	private Season? _announcedSeason;

	private readonly System.Collections.Generic.List<IWorldObject> _worldExtras = new System.Collections.Generic.List<IWorldObject>();

	private Unit _lastPossessed;

	private float _zoomBeforePossess = 0.8f;

	private AIGovernorManager _governor;

	private BlueprintRenderer _blueprintRenderer;

	private WorldRenderer _renderer;

	private UIManager _ui;

	private StrategicView _strategic;
	private ArenaController _arena;
	private bool _arenaFromMap;

	/// <summary>QAMAL: yurish yetib kelganda dushman shahrida real vaqt jangi.</summary>
	private AndijonSaltanatlar.Combat.SiegeMode _siege;
	private bool _siegeFromMap;
	private bool _siegeTestPending;
	/// <summary>Qamal onlayn dunyo xaritasidan (M) boshlangan — tugagach xarita qayta ochiladi.</summary>
	private bool _siegeOnline;
	private readonly FarmWorkController _farmWork = new FarmWorkController();
	private readonly SwimController _swim = new SwimController();

	private bool _strategicMode;

	private CivilizationView _civ;

	private bool _civMode;

	private QuestJournalView _quest;

	// PROMPT 19A: online (server bilan aloqa + Saltanatlar bozori paneli)
	private AndijonSaltanatlar.Online.OnlineClient _online;

	private OnlinePanel _onlinePanel;

	private AndijonSaltanatlar.UI.Design.UiKit _uiKit;

	// Doimiy onlayn: Google orqali kirish + bulutdagi saqlash (o'yin qolgan joyidan davom etadi).
	private AndijonSaltanatlar.Online.CloudSession _cloud;

	private GoogleLoginScreen _loginScreen;

	// Yangi hukmdor: profil saqlangach DUNYONI TANLASH / YURTNI TANLASH.
	private WorldSelectionScreen _worldSelect;

	// Yangilash: GitHub'da yangi reliz bo'lsa ko'rsatiladigan modal oyna.
	private AndijonSaltanatlar.Online.UpdateInfo _pendingUpdate;
	private AndijonSaltanatlar.UI.UpdateScreen _updateScreen;

	// Kirish videosi (studio intro): o'yin ochilganda birinchi bo'lib ko'rsatiladi.
	private bool _introMode;
	private AndijonSaltanatlar.UI.IntroScreen _introScreen;

	private bool _loginMode;

	private bool _loadingCloud;

	private bool _autoLoginStarted;

	private float _autosaveTimer;

	/// <summary>Avtomatik saqlash oralig'i (soniya). Yer ochilganda darhol saqlanadi.</summary>
	private const float AutosaveSeconds = 30f;

	private bool _questMode;

	private StoryOverlay _storyOverlay;

	private VizierPopup _vizierPopup;

	private MainMenuScreen _mainMenu;

	private bool _menuMode;

	private bool _pendingStoryMode;

	private CampaignState _pendingStory;

	private readonly DayNightCycle _dayNight = new DayNightCycle();

	private Texture2D _glow;

	private float _time;

	private int _currentSeed = 1483;

	private int _frames;

	private bool _captureNextDraw;

	private bool _exitRequested;

	private static readonly BlendState Multiply = new BlendState
	{
		ColorSourceBlend = Blend.DestinationColor,
		ColorDestinationBlend = Blend.Zero,
		AlphaSourceBlend = Blend.DestinationAlpha,
		AlphaDestinationBlend = Blend.Zero
	};

	public Game1(LaunchOptions opt)
	{
		_opt = opt ?? new LaunchOptions();
		_graphics = new GraphicsDeviceManager(this)
		{
			PreferredBackBufferWidth = 1280,
			PreferredBackBufferHeight = 720,
			SynchronizeWithVerticalRetrace = true,
			PreferMultiSampling = false
		};
		Content.RootDirectory = "Content";
		IsMouseVisible = true;
		IsFixedTimeStep = true;
		if (_opt.WindowSize.HasValue)
		{
			_graphics.PreferredBackBufferWidth = _opt.WindowSize.Value.X;
			_graphics.PreferredBackBufferHeight = _opt.WindowSize.Value.Y;
		}
		// Oyna yopilganda oxirgi holat serverga yuboriladi (qisqa kutish bilan).
		Exiting += (s, e) => FlushCloudSaveBlocking();
#if ANDROID
		// Telefon: to'liq ekran, gorizontal holat.
		_graphics.IsFullScreen = true;
		_graphics.SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;
#endif
	}

	protected override void Initialize()
	{
		Window.Title = "ANDIJON: TAXTLAR JANGI";
#if !ANDROID
		Window.AllowUserResizing = true;
#endif
		_graphics.ApplyChanges();
		base.Initialize();
	}

	protected override void LoadContent()
	{
		_sb = new SpriteBatch(GraphicsDevice);
		string contentDir = Path.Combine(AppContext.BaseDirectory, "Content");
		_assets = new AssetManager(GraphicsDevice, contentDir, _opt.ExportSpritesDir);
		_assets.LoadAll();
		_font = new PixelFont(GraphicsDevice);
		if (_opt.ExportSpritesDir != null)
		{
			_exitRequested = true;
			return;
		}
		_input = new InputManager { ForceTouchUi = _opt.Phone };
		if (_opt.DataDir != null) AppPaths.Root = _opt.DataDir;
		TimePrefs.Load();
		if (_opt.Season.HasValue) TimePrefs.Current.SeasonView = Math.Clamp(_opt.Season.Value, -1, 3);
		GraphicsQualityManager.Current = (GraphicsQuality)TimePrefs.Current.Quality;
#if !ANDROID
		// PC: klaviaturadan matn (login maydonlari uchun). Android'da ekran klaviaturasi ishlatiladi.
		Window.TextInput += (object s, TextInputEventArgs e) => _input.PushText(e.Character);
#endif
		_online = new AndijonSaltanatlar.Online.OnlineClient();
		// PROMPT 19B: yagona dizayn tizimi (ThemeManager + Typography/Noto Sans + UiKit komponentlari)
		_uiKit = new AndijonSaltanatlar.UI.Design.UiKit(_assets.Pixel, new AndijonSaltanatlar.UI.Design.Typography(_font));
		_perf = new Core.PerformanceMonitor();
		_onlinePanel = new OnlinePanel(_assets, _font, _online, _uiKit);
		_onlinePanel.LocalArmyCount = () => _state?.Military?.TotalUnits ?? 0;
		_renderer = new WorldRenderer(_assets, _font);
		_glow = BuildGlow();
		_mainMenu = new MainMenuScreen(_assets, _font) { Ui = _uiKit };
		// Fonda GitHub'dan yangilash tekshiriladi (internet yo'q bo'lsa jim o'tadi, o'yinni bloklamaydi).
		StartUpdateCheck();
		_cloud = new AndijonSaltanatlar.Online.CloudSession { LoginUrlFile = _opt.LoginUrlFile };
		_cloud.BringToFront = BringGameWindowToFront; // login tugagach o'yin oynasi oldinga chiqadi
		_cloud.NativeAccountPicker = NativeAccountPicker;
		_cloud.SignedIn = OnCloudSignedIn;
		_cloud.TokenRenewed = t => _online.UpdateToken(t);
		// Sessiya serverda bekor qilingan/muddati tugagan: kirish ekrani (lokal zaxira saqlangan, progress yo'qolmaydi)
		_cloud.SessionLost = () =>
		{
			if (_loginMode) return;
			_online.Logout();
			_onlinePanel.Close();
			_worldSelect = null;
			_fullUpgradeDone = false;
			_loginScreen = new GoogleLoginScreen(_cloud, _cloud.ServerUrl) { Ui = _uiKit, ShahTexture = () => _assets.TryGet(SpriteKeys.UnitShah + "_0"), ShahChanged = RefreshShahLook };
			_loginMode = true;
		};
		_cloud.SaveConflict = OnCloudConflict;
		_onlinePanel.ReconnectRequested = ConnectOnlineWorld;
		_pendingStoryMode = _opt.Story || _opt.SmokeLog != null;
		NewGame(_currentSeed);
		Vector2 startWorld = (_opt.CenterTile.HasValue ? IsoMath.TileToWorld(_opt.CenterTile.Value) : IsoMath.TileToWorld(new Vector2(GameConfig.CityCenter, GameConfig.CityCenter)));
		_camera = new CameraManager(GraphicsDevice.Viewport, startWorld, _opt.Zoom ?? StartZoom());
		if (_opt.SmokeLog != null)
		{
			RunSmokeTest(_opt.SmokeLog);
			_exitRequested = true;
			return;
		}
		if (_opt.Time.HasValue)
		{
			_state.Clock.Set(1, _opt.Time.Value);
			_dayNight.Update(TimePrefs.VisualFraction(_state.Clock.DayFraction), TimePrefs.VisualSeason(_state.Season));
		}
		if (_opt.PopTax.HasValue)
		{
			_state.Policy.PopulationTax = _opt.PopTax.Value;
		}
		if (_opt.SeasonName != null && TryParseSeason(_opt.SeasonName, out Season wanted))
		{
			_state.SeasonShift = ((int)wanted - (int)SeasonManager.Of(_state.Clock.Month) + 4) % 4;
			ApplySeasonWeather(wanted);
			_announcedSeason = wanted;
		}
		if (_opt.Food.HasValue)
		{
			_state.Food = _opt.Food.Value;
		}
		if (_opt.Techs.HasValue)
		{
			for (int i = 0; i < _opt.Techs.Value && i < TechnologyCatalog.All.Count; i++)
			{
				_state.Civ.Player.Researched.Add(TechnologyCatalog.All[i].Id);
			}
		}
		if (_opt.ResearchId != null)
		{
			_state.StartResearch(_opt.ResearchId);
		}
		if (_opt.SimDays.HasValue)
		{
			for (int j = 0; j < _opt.SimDays.Value; j++)
			{
				_state.Update(31f, _buildings);
			}
		}
		if (_opt.FarmRequest)
		{
			_state.Farming.Seeds = 0;
			_state.Farming.EndDay(_state.Day, Math.Max(1, _buildings.Count(BuildingType.Farm)), _state.Season);
		}
		if (_opt.MarchRegion.HasValue)
		{
			_strategicMode = true;
			_strategic.StartMarch(_opt.MarchRegion.Value);
		}
		if (_opt.Arena)
		{
			_arena.Start();
		}
		if (_opt.Tab != null)
		{
			_ui.OpenTab(_opt.Tab);
		}
		if (_opt.Map)
		{
			_strategicMode = true;
		}
		if (_opt.Civ)
		{
			_civMode = true;
			if (_opt.Tab == "history") _civ.ShowHistory();
		}
		if (_opt.QuestJournal)
		{
			_questMode = true;
		}
		if (_opt.Advisor)
		{
			_ui.AdvisorOpen = true;
		}
		if (_opt.Governor)
		{
			_ui.GovernorPanel?.Open();
		}
		if (_opt.Plan != null && Enum.TryParse<DistrictType>(_opt.Plan, ignoreCase: true, out var result))
		{
			_governor.GeneratePlan(result);
			_ui.GovernorPanel?.ShowPlan();
		}
		if (_opt.WeatherName != null && Enum.TryParse<Weather>(_opt.WeatherName, ignoreCase: true, out var result2))
		{
			_state.Weather.SetCurrent(result2);
		}
		if (_opt.ShowLevels)
		{
			foreach (Building building in _buildings.Buildings)
			{
				if (building.Type == BuildingType.MainHall)
				{
					building.Level = 4;
				}
				else if (building.Type == BuildingType.Market)
				{
					building.Level = 3;
				}
				else if (building.Type == BuildingType.Mosque)
				{
					building.Level = 3;
				}
				else if (building.Type == BuildingType.Tower)
				{
					building.Level = 2;
				}
				else if (building.Type == BuildingType.Madrasa)
				{
					building.Level = 2;
				}
			}
		}
		// Oddiy ishga tushirish: avval Google bilan kirish (o'yin doimiy onlayn). Sinov parametrlari bilan — to'g'ridan-to'g'ri.
		bool testLaunch = _opt.ScreenshotPath != null || _opt.Tab != null || _opt.Map || _opt.Civ || _opt.Story || _opt.QuestJournal || _opt.Online || _opt.Menu;
		// Kirish videosi faqat oddiy ishga tushirishda (sinov/avtomatlashtirishda emas).
		_introMode = !testLaunch && _opt.LoginUrlFile == null && !_opt.Login;
		if (_introMode)
			_introScreen = new AndijonSaltanatlar.UI.IntroScreen(GraphicsDevice, _assets.Pixel);
		_menuMode = _opt.Menu;
		_loginMode = _opt.Login || _opt.AuthStage != null || !testLaunch;
		if (_loginMode)
		{
			_cloud.TryResume(DefaultServer());
			_loginScreen = new GoogleLoginScreen(_cloud, _cloud.ServerUrl.Length > 0 ? _cloud.ServerUrl : DefaultServer()) { Ui = _uiKit, ShahTexture = () => _assets.TryGet(SpriteKeys.UnitShah + "_0"), ShahChanged = RefreshShahLook };
			if (_opt.AuthStage == "profile") _loginScreen.Mode = GoogleLoginScreen.Stage.Profile;
			else if (_opt.AuthStage == "welcome") _loginScreen.Mode = GoogleLoginScreen.Stage.Welcome;
			else if (_opt.AuthStage == "shah") _loginScreen.OpenShahSetup(false, null);
		}
		if (_opt.Online)
		{
			_onlinePanel.Prefill(_opt.OnlineServer, _opt.OnlineUser, _opt.OnlinePass);
			_onlinePanel.Open();
			if (_opt.OnlineTab != null) _onlinePanel.ShowTab(_opt.OnlineTab);
			if (!string.IsNullOrEmpty(_opt.OnlineUser) && !string.IsNullOrEmpty(_opt.OnlinePass))
				_onlinePanel.LoginNow(_opt.OnlineRegister);
		}
		if (_opt.AttackRegion.HasValue)
		{
			_strategicMode = true;
			BattleResult battleResult = _state.World.PlayerAttackRegion(_opt.AttackRegion.Value);
			if (battleResult != null)
			{
				_state.Coins += battleResult.CoinChange;
			}
		}
		// Sinov: kirishsiz rejimda ham to'liq kuchaytirish (suratga olish uchun)
		if ((_opt.FullUpgrade || _cloud.IsVip) && !_loginMode && !_fullUpgradeDone) { _fullUpgradeDone = true; ApplyFullUpgrade(); }
		if (_opt.FarmDemo.HasValue)
		{
			Building hallF = _buildings.Buildings.FirstOrDefault(b => b.Type == BuildingType.MainHall);
			Farm first = null;
			foreach (Farm fm in _buildings.Buildings.OfType<Farm>().OrderBy(b => hallF == null ? 0 : Vector2.DistanceSquared(new Vector2(b.Position.X, b.Position.Y), new Vector2(hallF.Position.X, hallF.Position.Y))))
			{
				first ??= fm;
				if (fm.CropStage == FarmCropStage.Empty) fm.PlantCrop();
				fm.AdvanceGrowth(_opt.FarmDemo.Value);
			}
			if (first != null) _camera.Jump(IsoMath.TileToWorld(new Vector2(first.Position.X + 2, first.Position.Y + 2)), 2.2f);
		}
		if (_opt.RaidGate.HasValue)
		{
			_combat.StartRaidNow(_opt.RaidGate.Value);
		}
		if (_opt.PossessNear.HasValue)
		{
			_combat.PossessNearestSoldier(_opt.PossessNear.Value);
		}
		if (_opt.EnterHouse.HasValue)
		{
			// -1: saroyga kirish, -2: shohni shaharga chiqarish, 0..: K-uy
			var houses = _buildings.Buildings.Where((Building b) => _opt.EnterHouse.Value < 0 ? b.Type == BuildingType.MainHall : b.Type == BuildingType.House).ToList();
			if (houses.Count > 0)
			{
				Building house = houses[Math.Clamp(_opt.EnterHouse.Value, 0, houses.Count - 1)];
				if (_combat.Possessed == null)
				{
					_combat.PossessNearestSoldier(new Vector2(house.Position.X, house.Position.Y));
				}
				_lastPossessed = _combat.Possessed;
				_interior.Enter(house, _combat.Possessed);
				if (_opt.HousePose != null) _interior.TestPose(_opt.HousePose);
				if (_opt.EnterHouse.Value == -2)
				{
					ShahUnit shah = _combat.SpawnShahAndPossess(_interior.OutsideDoor(house));
					_interior.Exit(shah);
					_lastPossessed = shah;
					_camera.Jump(IsoMath.TileToWorld(shah.Position), GameConfig.PossessZoom);
				}
			}
		}
		if (_opt.BuildType != null)
		{
			BuildingType? buildingType = ParseBuild(_opt.BuildType);
			if (buildingType.HasValue)
			{
				_state.EnterBuildMode(buildingType.Value);
			}
		}
		if (_opt.SelectTile.HasValue)
		{
			Point value = _opt.SelectTile.Value;
			object occupant = _map.GetOccupant(value.X, value.Y);
			if (occupant != null)
			{
				_state.Select(occupant);
			}
		}
	}

	private void NewGame(int seed, AIGovernorSaveState governorSave = null)
	{
		_garrisoned.Clear();
		_currentSeed = seed;
		_map = new MapManager(_assets, GameConfig.MapSize, GameConfig.MapSize);
		_buildings = new BuildingManager(_map, _assets);
		_units = new UnitManager(_assets);
		_state = new GameStateManager();
		CityGenerator.Generate(_map, _buildings, _units, seed);
		CitizenSpawner.Spawn(_map, _units);
		// Otliq soqchilar bosh ko'chalarda (3D modeldan olingan otliq rasmi)
		float co = GameConfig.CityOffset;
		_units.Add(new CavalryUnit(new[] { new Vector2(16.5f + co, 31.5f + co), new Vector2(48.5f + co, 31.5f + co) }));
		_units.Add(new CavalryUnit(new[] { new Vector2(31.5f + co, 15.5f + co), new Vector2(31.5f + co, 49.5f + co) }, 1.4f));
		PatrolController.RouteAll(_map, _units);
		_state.IsStoryMode = _pendingStoryMode;
		_state.PendingCampaignState = _pendingStory;
		_pendingStory = null;
		_state.InitPopulation(_buildings);
		_npcInteractions = new AndijonSaltanatlar.NPC.NPCInteractionManager();
		_npcInteractions.BindUnits(_units, _state.Pop);
		_construction = new ConstructionSystem(_map, _buildings, _units, _state, _assets);
		_combat = new AndijonSaltanatlar.Combat.CombatSystem(_map, _units, _state) { Buildings = _buildings };
		_citizenSocial = new AndijonSaltanatlar.NPC.CitizenSocialSystem();
		_citizenLife = new AndijonSaltanatlar.CityLife.CitizenSocialSimulation();
		_combat.OnCitizenWorkRequested = PerformCitizenWork;
		_interior = new AndijonSaltanatlar.Interiors.HouseInterior(_assets, _map, _buildings, _state);
		_birds = new BirdSystem(_assets, _map, _units);
		_livestock = new LivestockSystem(_assets, _map, _units, _buildings);
		_curfew = new AndijonSaltanatlar.World.CurfewController();
		_carts = new AndijonSaltanatlar.World.CartSystem(_assets);
		_taxRiot = new AndijonSaltanatlar.CityLife.TaxRiotSystem();
		_announcedSeason = null;
		_governor = new AIGovernorManager
		{
			OnMessage = (string msg) =>
			{
				_state.ShowMessage(msg);
			}
		};
		_governor.Initialize(_state, _buildings, _map, _construction, governorSave);
		_blueprintRenderer = new BlueprintRenderer(_map);
		_ui = new UIManager(_assets, _font, _state, _buildings, _units, _governor)
		{
			OnSave = DoSave,
			OnLoad = DoLoad,
			OnLogout = DoLogout,
			AccountName = _cloud != null && _cloud.State == AndijonSaltanatlar.Online.CloudLoginState.SignedIn ? _cloud.AccountLabel : ""
		};
		if (_ui.GovernorPanel != null)
		{
			_ui.GovernorPanel.FocusLandRequested = (LandParcel parcel) =>
			{
				if (_camera != null && parcel != null)
				{
					Point center = parcel.GridBounds.Center;
					_camera.Jump(IsoMath.TileToWorld(new Vector2(center.X, center.Y)), Math.Max(0.8f, _camera.Zoom));
				}
			};
		}
		_strategic = new StrategicView(_assets, _font, _state);
		_arena = new ArenaController(_assets, _state);
		_siege = new AndijonSaltanatlar.Combat.SiegeMode(_assets, _state, _font);
		_siege.OnlineFinished = req => _online.SiegeFinish(req);
		_siegeTestPending = _opt.SiegeRegion.HasValue;
		_civ = new CivilizationView(_assets, _font, _state);
		_quest = new QuestJournalView(_assets, _font, _state);
		_storyOverlay = new StoryOverlay(_assets, _font, _state);
		_vizierPopup = new VizierPopup(_assets, _font);
	}

	private void RunSmokeTest(string logPath)
	{
		StringBuilder sb = new StringBuilder();
		int pass = 0;
		int fail = 0;
		sb.AppendLine("ANDIJON: SALTANATLAR вЂ” SMOKE TEST");
		sb.AppendLine("Vaqt: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		sb.AppendLine("Seed: " + _currentSeed);
		sb.AppendLine(new string('-', 60));
		try
		{
			Check("Startup: shahar yuklandi (binolar > 0)", _buildings.Buildings.Count > 0, _buildings.Buildings.Count + " bino");
			Check("Startup: aholi > 0", _state.Population > 0, _state.Population + " kishi");
			Building building = BuildingFactory.Create(BuildingType.House, Point.Zero);
			int x = building.Size.X;
			int cost = building.Cost;
			_state.Coins = Math.Max(_state.Coins, 1000);
			int coins = _state.Coins;
			int count = _buildings.Buildings.Count;
			Point? point = FindBuildableTile(x, -1);
			Check("Qurilish: bo'sh katak topildi", point.HasValue, point.HasValue ? point.Value.ToString() : "topilmadi");
			bool ok = false;
			if (point.HasValue)
			{
				_state.EnterBuildMode(BuildingType.House);
				ok = _construction.PlaceAt(point.Value, showErrors: false);
			}
			Check("Qurilish: uy joylashtirildi", ok);
			Check("Qurilish: bino soni +1", _buildings.Buildings.Count == count + 1);
			Check("Iqtisod: tanga BIR marta ayirildi (-" + cost + ")", _state.Coins == coins - cost, coins + " -> " + _state.Coins);
			_state.CancelBuild();
			int day = _state.Day;
			for (int i = 0; i < 40; i++)
			{
				_state.Update(31f, _buildings);
			}
			Check("Simulyatsiya: o'yin kuni o'tdi", _state.Day > day, "kun " + day + " -> " + _state.Day);
			Check("Qurilish: bosqichli qurilish yakunlandi", _buildings.Buildings.TrueForAll((Building b) => b.BuildProgress >= 1f));
			Check("Iqtisod: resurslar manfiy emas", _state.Coins >= 0 && _state.Food >= 0, "tanga " + _state.Coins + ", oziq " + _state.Food);
			_state.Coins = Math.Max(_state.Coins, 3000);
			LandParcel landParcel = _state.Land.Parcels.FirstOrDefault((LandParcel p) => p.State == LandParcelState.Unlockable);
			string text;
			if (landParcel != null)
			{
				_state.Select(landParcel);
				_state.UnlockSelectedParcel();
				text = "gating-path (talablar bajarildi)";
			}
			else
			{
				landParcel = _state.Land.Parcels.FirstOrDefault((LandParcel p) => p.State == LandParcelState.Locked);
				if (landParcel != null)
				{
					_state.Land.Unlock(landParcel);
					text = "direct (aholi talabi smoke oynasida yetmadi)";
				}
				else
				{
					text = "ochiladigan uchastka yo'q";
				}
			}
			bool flag = landParcel != null && landParcel.State == LandParcelState.Open;
			Check("Yer ochish: uchastka ochildi [" + text + "]", flag, (landParcel != null) ? ("id=" + landParcel.Id) : "yo'q");
			bool ok2 = false;
			if (flag)
			{
				Point? point2 = FindBuildableTile(x, landParcel.Id);
				if (point2.HasValue)
				{
					_state.EnterBuildMode(BuildingType.House);
					ok2 = _construction.PlaceAt(point2.Value, showErrors: false);
					_state.CancelBuild();
				}
			}
			Check("Yer ochish: ochilgan yerda qurilish mumkin", ok2);
			_state.Advisor.Analyze(_state);
			Check("Maslahatchi: tavsiya beradi", _state.Advisor.Messages.Count > 0, _state.Advisor.Messages.Count + " ta");
			_governor.Analyze();
			Check("Vazir: shahar tahlili", _governor.Report != null && _governor.Report.OverallCityHealthScore >= 0 && _governor.Report.OverallCityHealthScore <= 100, (_governor.Report == null) ? "hisobot yo'q" : ("salomatlik " + _governor.Report.OverallCityHealthScore));
			Check("Vazir: tavsiyalar beradi", _governor.Recommendations.Count > 0, _governor.Recommendations.Count + " ta");
			int num = CountClosedParcels();
			_state.Coins = Math.Max(_state.Coins, 4000);
			int coins2 = _state.Coins;
			int count2 = _buildings.Buildings.Count;
			CityPlan cityPlan = _governor.GeneratePlan(DistrictType.Mixed);
			bool ok3 = cityPlan != null && cityPlan.HouseCount >= 3 && cityPlan.MarketCount >= 1 && cityPlan.GardenCount >= 1 && cityPlan.RoadCount > 0;
			Check("Vazir: minimal mahalla rejasi (3 uy + bozor + bog' + yo'l)", ok3, (cityPlan == null) ? "reja null" : (cityPlan.HouseCount + " uy, " + cityPlan.MarketCount + " savdo, " + cityPlan.GardenCount + " bog', " + cityPlan.RoadCount + " yo'l"));
			Check("Vazir: preview GameState'ni o'zgartirmadi", _buildings.Buildings.Count == count2 && _state.Coins == coins2, "binolar " + count2 + " -> " + _buildings.Buildings.Count + ", tanga " + coins2 + " -> " + _state.Coins);
			Check("Vazir: blueprint yaroqli va rang holatlari hisoblangan", cityPlan != null && cityPlan.IsValid && cityPlan.BuildableCount > 0, (cityPlan == null) ? "yo'q" : ("yaroqli=" + cityPlan.IsValid + ", yashil=" + cityPlan.BuildableCount));
			PlanCostEstimate planCostEstimate = ((cityPlan == null) ? null : PlanCostEstimator.Estimate(cityPlan, _state.Coins, 10));
			Check("Vazir: xarajat smetasi haqiqiy narxlardan", planCostEstimate != null && planCostEstimate.BuildingsCost > 0 && planCostEstimate.RoadsCost > 0 && planCostEstimate.TotalCost == cityPlan.TotalCost, (planCostEstimate == null) ? "smeta yo'q" : (planCostEstimate.TotalCost + " tanga"));
			Check("Vazir: 3 xil iqtisodiy prognoz", _governor.ForecastCurrent().Count == 3, _governor.ForecastCurrent().Count + " ta");
			int num2 = cityPlan?.Buildings.Count ?? 0;
			int num3 = cityPlan?.Roads.Count ?? 0;
			Point bpTileBeforeSave = ((cityPlan != null && cityPlan.Buildings.Count > 0) ? cityPlan.Buildings[0].Tile : new Point(-1, -1));
			int num4 = ((cityPlan != null && cityPlan.Buildings.Count > 0) ? cityPlan.Buildings[0].Id : 0);
			_governor.SelectBlueprintItem(num4);
			string text2 = _governor.DescribeSelectedBlueprintUz();
			Check("Vazir: tanlangan bino ma'lumoti (narx, maydon, foyda, talab)", text2.Contains("tanga") && text2.Contains("Foyda") && text2.Contains("Talab"), text2);
			bool flag2 = SaveManager.Save(SaveManager.DefaultPath, BuildSave());
			DoLoad();
			CityPlan blueprint = _governor.Blueprint;
			Check("Vazir: blueprint va rejalashtirilgan joylar Save/Load'dan tiklandi", flag2 && blueprint != null && blueprint.Buildings.Count == num2 && blueprint.Roads.Count == num3 && blueprint.Buildings.Exists((BlueprintItem b) => b.Tile == bpTileBeforeSave) && _governor.Blueprints.SelectedItemId == num4, (blueprint == null) ? "blueprint yo'q" : (blueprint.Buildings.Count + " bino, " + blueprint.Roads.Count + " yo'l"));
			cityPlan = blueprint;
			if (cityPlan != null)
			{
				MapPlanningGrid grid = new MapPlanningGrid(_state, _map);
				CityPlan cityPlan2 = cityPlan.Clone();
				BlueprintItem blueprintItem = cityPlan2.Buildings.FirstOrDefault((BlueprintItem b) => b.Kind == BuildingType.Garden);
				Point point3 = blueprintItem?.Tile ?? Point.Zero;
				bool flag3 = blueprintItem != null && BlueprintEditor.MoveBuilding(cityPlan2, blueprintItem.Id, new Point(-5, -5), grid, _state.IsBuildingUnlocked, _state.Coins, 10);
				Check("Vazir: muharrir noto'g'ri ko'chirishni rad etadi", blueprintItem != null && !flag3 && blueprintItem.Tile == point3, "eski joy saqlandi");
				CityPlan cityPlan3 = cityPlan.Clone();
				BlueprintItem blueprintItem2 = cityPlan3.Buildings.FirstOrDefault((BlueprintItem b) => b.Kind == BuildingType.Garden);
				bool flag4 = blueprintItem2 != null && BlueprintEditor.RemoveBuilding(cityPlan3, blueprintItem2.Id, grid, _state.IsBuildingUnlocked, _state.Coins, 10);
				Check("Vazir: muharrir elementni o'chiradi va qayta hisoblaydi", flag4 && cityPlan3.Buildings.Count == cityPlan.Buildings.Count - 1, flag4 ? (cityPlan3.Buildings.Count + " bino qoldi") : "o'chirilmadi");
			}
			AutonomyLevel level = _governor.Permissions.Level;
			_governor.Permissions.Level = AutonomyLevel.Manual;
			bool ok4 = _governor.GeneratePlan(DistrictType.Garden) == null;
			_governor.Permissions.Level = level;
			Check("Vazir: 'faqat maslahat' rejimida reja tuzilmaydi", ok4);
			Check("Vazir: o'zbekcha buyruq parseri", _governor.HandleCommand("tahlil qil").Length > 10);
			int num5 = cityPlan?.TotalCost ?? 0;
			int num6 = ((cityPlan != null) ? (Enumerable.Count(cityPlan.Buildings, (BlueprintItem b) => b.Status != BlueprintStatus.Blocked) + Enumerable.Count(cityPlan.Roads, (BlueprintRoad r) => r.Status != BlueprintStatus.Blocked)) : 0);
			bool flag5 = _governor.ApprovePlan();
			Check("Vazir: tasdiqlash qurilish navbatini yaratadi", flag5 && _governor.Queue.Plan != null && _governor.Queue.Plan.State == PlanExecState.InProgress && _governor.Queue.Plan.Steps.Count == num6, num6 + " qadam, budjet " + num5);
			Check("Vazir: tasdiqlashdan keyin xazina hali sarflanmagan", _state.Coins == coins2, coins2 + " -> " + _state.Coins);
			Check("Vazir: tasdiqdan keyin preview yopildi", _governor.Blueprint == null);
			int num7 = _governor.Queue.Plan?.PendingCount ?? 0;
			bool ok5 = _state.Story != null && _state.Story.Quests.ActiveQuests().Any();
			Check("Hikoya: faol vazifa mavjud", ok5);
			int coins3 = _state.Coins;
			int day2 = _state.Day;
			int count3 = _buildings.Buildings.Count;
			int count4 = _state.Land.OpenParcelIds().Count;
			bool ok6 = SaveManager.Save(SaveManager.DefaultPath, BuildSave());
			Check("Save: fayl yozildi", ok6);
			DoLoad();
			Check("Load: tanga tiklandi", _state.Coins == coins3, coins3 + " -> " + _state.Coins);
			Check("Load: o'yin kuni tiklandi", _state.Day == day2, day2 + " -> " + _state.Day);
			Check("Load: ochilgan yerlar tiklandi", _state.Land.OpenParcelIds().Count == count4, count4 + " -> " + _state.Land.OpenParcelIds().Count);
			Check("Load: binolar tiklandi", _buildings.Buildings.Count == count3, count3 + " -> " + _buildings.Buildings.Count);
			Check("Load: Vazir navbati tiklandi", _governor.Queue.Plan != null && _governor.Queue.Plan.PendingCount == num7, num7 + " kutilayotgan qadam");
			int coins4 = _state.Coins;
			int count5 = _buildings.Buildings.Count;
			int num8 = CountRoadTiles();
			int totalPopulation = _buildings.TotalPopulation;
			int num9 = _buildings.Count(BuildingType.House);
			int num10 = _buildings.Count(BuildingType.Market);
			int num11 = _buildings.Count(BuildingType.Garden);
			int num12 = _governor.Queue.Plan?.DoneCount ?? 0;
			_governor.OnDayTick();
			int num13 = _governor.Queue.Plan?.DoneCount ?? 0;
			int ok7;
			if (num13 <= num12)
			{
				ApprovedConstructionPlan plan = _governor.Queue.Plan;
				ok7 = ((plan != null && plan.State == PlanExecState.Completed) ? 1 : 0);
			}
			else
			{
				ok7 = 1;
			}
			Check("Vazir: kunlik tick qurilish navbatini suradi", (byte)ok7 != 0, num12 + " -> " + num13);
			int num14 = 0;
			while (_governor.Queue.Plan != null && _governor.Queue.Plan.State == PlanExecState.InProgress && num14++ < 300)
			{
				_governor.StepQueueOnce();
			}
			ApprovedConstructionPlan plan2 = _governor.Queue.Plan;
			Check("Vazir: navbat yakunlandi", plan2 != null && plan2.State == PlanExecState.Completed, (plan2 == null) ? "navbat yo'q" : plan2.State.ToString());
			Check("Vazir: yakunlangan reja tarixga yozildi", _governor.History.Count > 0 && _governor.History[_governor.History.Count - 1].State == PlanExecState.Completed && _governor.History[_governor.History.Count - 1].Spent == (plan2?.SpentSoFar ?? (-1)), (_governor.History.Count == 0) ? "tarix bo'sh" : _governor.History[_governor.History.Count - 1].SummaryUz);
			Check("Vazir: yakunlangach tahlil darhol yangilandi", _governor.Report != null && _governor.Report.HousingCapacity == _buildings.TotalPopulation, "hisobot " + (_governor.Report?.HousingCapacity.ToString() ?? "yo'q") + ", haqiqiy " + _buildings.TotalPopulation);
			GovernorHistoryEntry governorHistoryEntry = ((_governor.History.Count > 0) ? _governor.History[_governor.History.Count - 1] : null);
			Check("Vazir: hisobot haqiqiy uy-joy/aholi natijasini ko'rsatadi", governorHistoryEntry != null && governorHistoryEntry.HasImpact && governorHistoryEntry.HousingAfter == _buildings.TotalPopulation && governorHistoryEntry.HousingBefore == (plan2?.HousingCapacityAtApproval ?? (-1)) && governorHistoryEntry.BuiltBuildings >= 5 && governorHistoryEntry.BuiltRoads > 0, (governorHistoryEntry == null) ? "tarix yo'q" : governorHistoryEntry.SummaryUz);
			int count6 = _governor.History.Count;
			bool flag6 = SaveManager.Save(SaveManager.DefaultPath, BuildSave());
			DoLoad();
			GovernorHistoryEntry governorHistoryEntry2 = ((_governor.History.Count > 0) ? _governor.History[_governor.History.Count - 1] : null);
			Check("Vazir: reja tarixi Save/Load'dan tiklandi", flag6 && _governor.History.Count == count6 && governorHistoryEntry2 != null && governorHistoryEntry2.State == PlanExecState.Completed, _governor.History.Count + " ta yozuv");
			Check("Vazir: rivojlanish natijasi Save/Load'dan tiklandi", governorHistoryEntry2 != null && governorHistoryEntry2.HasImpact && governorHistoryEntry != null && governorHistoryEntry2.HousingAfter == governorHistoryEntry.HousingAfter && governorHistoryEntry2.PopulationAfter == governorHistoryEntry.PopulationAfter && governorHistoryEntry2.BuiltBuildings == governorHistoryEntry.BuiltBuildings && governorHistoryEntry2.BuiltRoads == governorHistoryEntry.BuiltRoads, (governorHistoryEntry2 == null) ? "tarix yo'q" : governorHistoryEntry2.SummaryUz);
			Check("Vazir: 3+ uy, bozor va bog' qurildi", _buildings.Count(BuildingType.House) >= num9 + 3 && _buildings.Count(BuildingType.Market) >= num10 + 1 && _buildings.Count(BuildingType.Garden) >= num11 + 1, "uy " + num9 + "->" + _buildings.Count(BuildingType.House) + ", bozor " + num10 + "->" + _buildings.Count(BuildingType.Market) + ", bog' " + num11 + "->" + _buildings.Count(BuildingType.Garden));
			Check("Vazir: rejalashtirilgan ko'cha qurildi", CountRoadTiles() > num8, num8 + " -> " + CountRoadTiles() + " yo'l katagi");
			Check("Vazir: aholi sig'imi oshdi", _buildings.TotalPopulation > totalPopulation, totalPopulation + " -> " + _buildings.TotalPopulation);
			Check("Vazir: xarajat budjet ichida, bir marta yechildi", plan2 != null && plan2.SpentSoFar > 0 && plan2.SpentSoFar <= plan2.ApprovedBudget && _state.Coins == coins4 - plan2.SpentSoFar, coins4 + " -> " + _state.Coins + " (sarflandi " + (plan2?.SpentSoFar ?? 0) + ")");
			Check("Vazir: AI tasdiqsiz yer sotib olmadi", CountClosedParcels() == num, "yopiq uchastkalar " + num + " -> " + CountClosedParcels());
			for (int num15 = 0; num15 < 5; num15++)
			{
				_state.Update(31f, _buildings);
			}
			Check("Vazir: qurilishdan keyin iqtisod barqaror", _state.Coins >= 0 && _state.Food >= 0, "tanga " + _state.Coins + ", oziq " + _state.Food);
		}
		catch (Exception ex)
		{
			Check("Smoke test kutilmagan xatosiz ishladi", false, ex.GetType().Name + ": " + ex.Message);
			sb.AppendLine("STACK: " + (ex.StackTrace ?? "").Replace("\r", "").Replace("\n", " || "));
		}
		sb.AppendLine(new string('-', 60));
		sb.AppendLine("NATIJA: " + pass + " PASS / " + fail + " FAIL");
		string text3 = sb.ToString();
		try
		{
			File.WriteAllText(logPath, text3);
		}
		catch
		{
		}
		Console.WriteLine(text3);
		void Check(string name, bool flag7, string detail = "")
		{
			if (flag7)
			{
				pass++;
			}
			else
			{
				fail++;
			}
			sb.AppendLine((flag7 ? "PASS" : "FAIL") + " | " + name + ((detail.Length > 0) ? ("  [" + detail + "]") : ""));
		}
	}

	private Point? FindBuildableTile(int size, int parcelId)
	{
		int num = GameConfig.CityCenter;
		for (int i = 0; i <= GameConfig.MapSize / 2; i++)
		{
			for (int j = -i; j <= i; j++)
			{
				for (int k = -i; k <= i; k++)
				{
					if (Math.Abs(k) != i && Math.Abs(j) != i)
					{
						continue;
					}
					int num2 = num + k;
					int num3 = num + j;
					if (num2 < 1 || num3 < 1 || num2 >= GameConfig.MapSize - size || num3 >= GameConfig.MapSize - size)
					{
						continue;
					}
					Point point = new Point(num2, num3);
					if (parcelId >= 0)
					{
						LandParcel landParcel = _state.Land.ParcelAt(num2, num3);
						if (landParcel == null || landParcel.Id != parcelId)
						{
							continue;
						}
					}
					if (!_state.IsLandBuildable(point, size) || !_buildings.CanPlace(point, size) || _units.AnyInArea(point, size))
					{
						continue;
					}
					return point;
				}
			}
		}
		return null;
	}

	private int CountClosedParcels()
	{
		int num = 0;
		foreach (LandParcel parcel in _state.Land.Parcels)
		{
			if (parcel.State != LandParcelState.Open)
			{
				num++;
			}
		}
		return num;
	}

	private int CountRoadTiles()
	{
		int num = 0;
		for (int i = 0; i < _map.Height; i++)
		{
			for (int j = 0; j < _map.Width; j++)
			{
				if (_map.GetTile(j, i) == TileType.Road)
				{
					num++;
				}
			}
		}
		return num;
	}

	private static BuildingType? ParseBuild(string s)
	{
		switch ((s ?? "").Trim().ToLowerInvariant())
		{
		case "uy":
		case "house":
			return BuildingType.House;
		case "minora":
		case "tower":
			return BuildingType.Tower;
		case "bozor":
		case "market":
			return BuildingType.Market;
		case "devor":
		case "wall":
			return BuildingType.Wall;
		default:
			return null;
		}
	}

	private SaveGameData BuildSave()
	{
		SaveGameData saveGameData = new SaveGameData
		{
			Seed = _currentSeed,
			MapOffset = GameConfig.CityOffset,
			Coins = _state.Coins,
			Food = _state.Food,
			Day = _state.Clock.Day,
			DayFraction = _state.Clock.DayFraction,
			CameraX = _camera.Position.X,
			CameraY = _camera.Position.Y,
			Zoom = _camera.Zoom,
			Speed = _state.Speed,
			PopTax = _state.Policy.PopulationTax,
			TradeTax = _state.Policy.TradeTax,
			ProdTax = _state.Policy.ProductionTax,
			Citizens = _state.Pop.Snapshot(),
			CitizenBindings = _npcInteractions.Bindings.ToList(),
			World = _state.World.Snapshot(),
			Military = _state.Military.Snapshot(),
			Civ = _state.Civ.Snapshot(),
			OpenParcels = _state.Land.OpenParcelIds(),
			Weather = (int)_state.Weather.Current,
			Story = _state.Story?.State,
			Governor = _governor?.GetSaveState(),
			EliteArmy = _eliteArmy,
			FarmSeeds = _state.Farming.Seeds,
			FarmTools = _state.Farming.ToolsLevel,
			FarmFertilizer = _state.Farming.FertilizerCharges,
			FarmIrrigation = _state.Farming.Irrigation,
			Interiors = AndijonSaltanatlar.Interiors.InteriorStore.Snapshot()
		};
		foreach (Building building in _buildings.Buildings)
		{
			if (!building.BuiltByPlayer && building.GeneratedOrigin.HasValue)
				saveGameData.MovedBuildings.Add(new[] { building.GeneratedOrigin.Value.X, building.GeneratedOrigin.Value.Y, building.Position.X, building.Position.Y });
			if (!building.BuiltByPlayer && building.Level > 1)
				saveGameData.BuildingLevels.Add(new[] { building.Position.X, building.Position.Y, building.Level });
			if (building.BuiltByPlayer)
			{
				saveGameData.PlayerBuildings.Add(new SavedBuilding
				{
					Type = (int)building.Type,
					X = building.Position.X,
					Y = building.Position.Y,
					Variant = building.Variant,
					HitPoints = building.HitPoints,
					Level = building.Level,
					CropStage = building is Farm farm ? (int)farm.CropStage : 0,
					GrowthDays = building is Farm cropFarm ? cropFarm.GrowthDays : 0,
					StoredProduce = building is Farm storedFarm ? storedFarm.StoredProduce : 0
				});
			}
		}
		for (int i = 0; i < _map.Height; i++)
		{
			for (int j = 0; j < _map.Width; j++)
			{
				if (_map.GetTile(j, i) == TileType.Road)
				{
					saveGameData.Roads.Add(new SavedRoad
					{
						X = j,
						Y = i
					});
				}
			}
		}
		return saveGameData;
	}

	/// <summary>Android: telefondagi Google akkaunt tanlash oynasi (Activity1 o'rnatadi; Windows'da null — brauzer).</summary>
	public System.Func<string, System.Threading.Tasks.Task<string>> NativeAccountPicker { get; set; }

	private bool CloudSignedIn => _cloud != null && _cloud.State == AndijonSaltanatlar.Online.CloudLoginState.SignedIn;

	private string DefaultServer()
	{
		if (!string.IsNullOrEmpty(_opt.Server)) return _opt.Server;
		if (!string.IsNullOrEmpty(_opt.OnlineServer)) return _opt.OnlineServer;
		return _online?.Settings?.ServerUrl ?? "";
	}

	/// <summary>Saqlash: kirilgan bo'lsa serverga (bulut), aks holda lokal faylga (sinov rejimi).</summary>
	private void DoSave()
	{
		if (CloudSignedIn)
		{
			UploadNow(showMessage: true);
			return;
		}
		bool flag = SaveManager.Save(SaveManager.DefaultPath, BuildSave());
		_state.ShowMessage(flag ? "O'yin saqlandi" : "Saqlab bo'lmadi", !flag);
	}

	/// <summary>Yuklash: kirilgan bo'lsa serverdagi eng so'nggi holat, aks holda lokal fayl.</summary>
	private void DoLoad()
	{
		if (CloudSignedIn)
		{
			ReloadFromCloud("O'yin serverdan yuklandi");
			return;
		}
		SaveGameData saveGameData = SaveManager.Load(SaveManager.DefaultPath);
		if (saveGameData == null)
		{
			_state.ShowMessage("Saqlangan o'yin topilmadi", isError: true);
			return;
		}
		ApplySave(saveGameData, "O'yin yuklandi");
	}

	private bool _fullUpgradeDone;
	private bool _eliteArmy;
	private readonly HashSet<int> _garrisoned = new HashSet<int>();

	private static readonly BuildingType[] OnceTypes = { BuildingType.GladiatorArena, BuildingType.Madrasa, BuildingType.Mosque };
	private readonly Dictionary<BuildingType, int> _sizeCache = new Dictionary<BuildingType, int>();

	private int SizeOf(BuildingType t)
	{
		if (!_sizeCache.TryGetValue(t, out int s)) _sizeCache[t] = s = BuildingFactory.Create(t, Point.Zero).Size.X;
		return s;
	}

	/// <summary>
	/// Ochiq yerlarning bo'sh joylarini eng yuqori darajali binolar bilan to'ldiradi. Shimol — harbiy hudud
	/// (kazarma, gladiator arenasi, minoralar), sharq — bozor (bozorlar, karvonsaroy, bog'), janub — dehqonchilik (fermalar),
	/// g'arb — bog'lar va uylar, 1-halqa mahallalari — uylar. Binolar orasida yo'llar qoladi. Qo'shilgan binolar soni.
	/// </summary>
	private int FillEmptyLand()
	{
		int placed = 0;
		foreach (var p in _state.Land.Parcels)
		{
			if (p.State != AndijonSaltanatlar.LandExpansion.LandParcelState.Open) continue;
			placed += FillParcel(p.GridBounds, ZonePlan(p), new Random(p.Id * 7919));
		}
		_state.Land.RefreshStates(_state.Population);
		return placed;
	}

	private static BuildingType[] ZonePlan(AndijonSaltanatlar.LandExpansion.LandParcel p)
	{
		var side = p.Side;
		if (side == AndijonSaltanatlar.LandExpansion.LandSide.East)
			return new[] { BuildingType.Market, BuildingType.Caravanserai, BuildingType.Market, BuildingType.Garden };
		if (p.Ring >= 2 && side == AndijonSaltanatlar.LandExpansion.LandSide.North)
			return new[] { BuildingType.GladiatorArena, BuildingType.Barracks, BuildingType.Tower, BuildingType.Barracks, BuildingType.Tower, BuildingType.Barracks };
		if (p.Ring >= 2 && side == AndijonSaltanatlar.LandExpansion.LandSide.South)
			return new[] { BuildingType.Farm, BuildingType.Farm, BuildingType.Garden, BuildingType.House };
		if (p.Ring >= 2 && side == AndijonSaltanatlar.LandExpansion.LandSide.West)
			return new[] { BuildingType.Garden, BuildingType.House, BuildingType.House, BuildingType.Madrasa, BuildingType.House };
		return new[] { BuildingType.House, BuildingType.House, BuildingType.House, BuildingType.Garden };
	}

	private int FillParcel(Rectangle r, BuildingType[] plan, Random rnd)
	{
		int placed = 0, idx = 0;
		var once = new HashSet<BuildingType>();
		for (int y = r.Y + 1; y < r.Bottom - 1; y++)
			for (int x = r.X + 1; x < r.Right - 1; x++)
			{
				for (int k = 0; k < plan.Length; k++)
				{
					BuildingType t = plan[(idx + k) % plan.Length];
					if (Array.IndexOf(OnceTypes, t) >= 0 && once.Contains(t)) continue;
					int size = SizeOf(t);
					if (x + size + 1 > r.Right || y + size + 1 > r.Bottom) continue;
					// atrofida bir katak bo'sh joy (yo'l) qoladi
					if (!_buildings.CanPlace(new Point(x - 1, y - 1), size + 2)) continue;
					Building b = BuildingFactory.Create(t, new Point(x, y), t == BuildingType.House ? rnd.Next(4) : 0);
					b.BuiltByPlayer = true;
					if (!_buildings.Add(b)) continue;
					while (BuildingUpgradeSystem.CanUpgrade(b)) b.Upgrade();
					PaveAround(b);
					if (Array.IndexOf(OnceTypes, t) >= 0) once.Add(t);
					idx = (idx + k + 1) % plan.Length;
					placed++;
					break;
				}
			}
		return placed;
	}

	/// <summary>Bino atrofidagi bo'sh kataklar — yo'l.</summary>
	private void PaveAround(Building b)
	{
		int s = b.Size.X;
		for (int y = b.Position.Y - 1; y <= b.Position.Y + s; y++)
			for (int x = b.Position.X - 1; x <= b.Position.X + s; x++)
			{
				bool edge = x == b.Position.X - 1 || y == b.Position.Y - 1 || x == b.Position.X + s || y == b.Position.Y + s;
				if (!edge || !_map.InBounds(x, y) || _map.GetOccupant(x, y) != null || !_map.IsBuildableTerrain(x, y)) continue;
				_map.SetTile(x, y, TileType.Road);
			}
	}

	/// <summary>
	/// Barcha jangchilar (soqchi, nayzachi, qilichboz, patrul) — xos navkar; o'yinchi qurgan kazarma, minora va arena
	/// oldida xos navkar qorovullar turadi. Xos navkarlar soni.
	/// </summary>
	private int ApplyEliteArmy()
	{
		foreach (Unit u in _units.Units.ToList())
		{
			if (u.Team != UnitTeam.Player || u is CitizenUnit || u is ShahUnit || u is GladiatorUnit || u is RaiderUnit) continue;
			_units.MakeElite(u);
		}
		foreach (Building b in _buildings.Buildings.ToList())
		{
			if (!b.BuiltByPlayer || _garrisoned.Contains(b.Id)) continue;
			int n = b.Type == BuildingType.Barracks || b.Type == BuildingType.GladiatorArena ? 2 : 0;
			if (n == 0) continue;
			_garrisoned.Add(b.Id);
			int s = b.Size.X;
			for (int i = 0; i < n; i++)
			{
				var pos = new Vector2(b.Position.X + s + 0.45f, b.Position.Y + 0.5f + i * Math.Max(1f, (s - 1f) / Math.Max(1, n - 1)));
				var g = _units.Add(new GuardUnit(pos, Direction.East));
				_units.MakeElite(g);
			}
		}
		return _units.Units.Count(u => u.IsElite);
	}

	/// <summary>
	/// "To'liq kuchaytirish" (faqat shahar, onlayn qo'shin va urushga tegmaydi): barcha binolar oxirgi darajada va
	/// to'liq qurilgan, barcha texnologiyalar o'rganilgan, sotib olinadigan barcha yer halqalari ochiq, xazina va ombor to'la.
	/// Boshqa saltanatlarga tegishli yerlar (faqat urush bilan olinadi) o'zgarmaydi. Natija darhol serverga saqlanadi.
	/// </summary>
	private void ApplyFullUpgrade()
	{
		int levels = 0;
		foreach (Building b in _buildings.Buildings)
		{
			if (b.BuildProgress < 1f) b.BuildProgress = 1f;
			while (BuildingUpgradeSystem.CanUpgrade(b)) { b.Upgrade(); levels++; }
			b.HitPoints = b.MaxHitPoints;
		}
		var civ = _state.Civ.Player;
		int techs = 0;
		foreach (var t in AndijonSaltanatlar.Technology.TechnologyCatalog.All)
			if (civ.Researched.Add(t.Id)) techs++;
		civ.CurrentResearch = "";
		civ.CurrentProgress = 0;
		// Butun yer (barcha halqalar, chegaradagi yerlar ham) ochiq va bo'sh joylar to'ldiriladi
		_state.Land.ApplyOpen(_state.Land.Parcels.Select(p => p.Id).ToList());
		int built = FillEmptyLand();
		// Barcha jangchilar — xos navkar, harbiy binolarda qorovullar
		_eliteArmy = true;
		int elite = ApplyEliteArmy();
		_state.Coins = Math.Max(_state.Coins, 50000);
		_state.Food = Math.Max(_state.Food, 20000);
		// Dehqonchilik ombori to'la: urug', eng yaxshi asboblar, tozalangan ariqlar
		_state.Farming.Seeds = Math.Max(_state.Farming.Seeds, 500);
		_state.Farming.ToolsLevel = AndijonSaltanatlar.Economy.FarmingManager.MaxToolsLevel;
		_state.Farming.Irrigation = true;
		// Aholi: har uy sig'imigacha to'la, xursand
		int people = _state.FillPopulation(_buildings);
		_state.Land.RefreshStates(_state.Population);
		_governor?.Analyze();
		_state.ShowMessage("To'liq kuchaytirildi: +" + built + " bino, " + elite + " xos navkar, aholi " + _state.Population);
		UploadNow(showMessage: false);
	}

	private float _comfortTalkTimer = 6f;
	private readonly Random _comfortRng = new Random();
	private static readonly string[] ColdLines = { "Brr... sovuq!", "Qo'llarim muzlab qoldi", "Uyga kirib isinay", "Sandal yoqish kerak", "Qor yog'yapti!" };
	private static readonly string[] VeryColdLines = { "Brrr! Juda sovuq!", "Muzlab qolyapman!", "Tezroq olovga!" };
	private static readonly string[] HotLines = { "Juda issiq!", "Soya qayerda?", "Bir piyola choy ichsam", "Hovuzga borsam..." };
	private static readonly string[] NiceLines = { "Ajoyib havo!", "Bahor keldi!", "Yomg'irdan keyin havo toza", "Barglar to'kilyapti..." };

	/// <summary>Vaqti-vaqti bilan fuqarolardan biri havo haqida gapiradi (sovuq/issiq/qulay).</summary>
	private void UpdateComfortTalk(float dt)
	{
		_comfortTalkTimer -= dt;
		if (_comfortTalkTimer > 0f || _units == null) return;
		_comfortTalkTimer = 7f + (float)_comfortRng.NextDouble() * 6f;
		var list = _units.Units.OfType<CitizenUnit>().Where(c => !c.IsDown && !c.IsInsideBuilding && string.IsNullOrEmpty(c.SpeechText)).ToList();
		if (list.Count == 0) return;
		string[] lines = SeasonVisuals.CurrentComfort switch
		{
			Comfort.JudaSovuq => VeryColdLines,
			Comfort.Sovuq => ColdLines,
			Comfort.Issiq => HotLines,
			_ => NiceLines,
		};
		if (lines == NiceLines && _comfortRng.NextDouble() < 0.6) return; // qulay havoda kamroq gapiradi
		list[_comfortRng.Next(list.Count)].Say(lines[_comfortRng.Next(lines.Length)], 3.5f);
	}

	/// <summary>O'yin holatini serverga yuboradi (fonda). Zaxira nusxa qurilmada ham qoladi.</summary>
	private void UploadNow(bool showMessage)
	{
		if (!CloudSignedIn) return;
		SaveGameData data = BuildSave();
		string json = SaveManager.ToJson(data);
		SaveManager.Save(AppPaths.File("cloud_backup.json"), data);
		_cloud.UploadSave(json, ok =>
		{
			if (showMessage) _state.ShowMessage(ok ? "O'yin serverga saqlandi" : "Serverga saqlab bo'lmadi — avtomatik qayta urinadi", !ok);
		});
	}

	/// <summary>Ilova yopilayotganda: oxirgi holatni serverga sinxron yuborish.</summary>
	private void FlushCloudSaveBlocking()
	{
		try
		{
			if (CloudSignedIn && !_loginMode && _state != null && _camera != null)
				_cloud.UploadSaveBlocking(SaveManager.ToJson(BuildSave()));
		}
		catch (Exception)
		{
		}
	}

	/// <summary>Android: ilova fonga o'tganda (OnPause) chaqiriladi — progress yo'qolmasin.</summary>
	public void SaveNowInBackground()
	{
		if (CloudSignedIn && !_loginMode && _state != null && _camera != null) UploadNow(showMessage: false);
	}

	/// <summary>Google orqali kirildi: serverdagi saltanatni yuklaymiz (yo'q bo'lsa yangi saltanat).</summary>
	/// <summary>
	/// Kirgandan keyin: yangi o'yinchi — YANGI HUKMDOR (taxallus), qaytgan o'yinchi — XUSH KELIBSIZ (O'YINNI DAVOM ETTIRISH).
	/// Sinov (skrinshot) rejimida darhol davom etadi.
	/// </summary>
	private void OnCloudSignedIn()
	{
		_fullUpgradeDone = false;   // VIP hisobda har kirishda to'liq ochiladi
		bool auto = _opt.ScreenshotPath != null && _opt.AuthStage == null;
		if (_loginScreen != null && !auto)
		{
			_loginScreen.ContinueRequested = LoadCloudGame;
			_loginScreen.ProfileDone = ShowWorldSelection;
			_loginScreen.Mode = _cloud.NeedsProfile ? GoogleLoginScreen.Stage.Profile : GoogleLoginScreen.Stage.Welcome;
			// Shohini hali sozlamagan o'yinchi — avval shoh qiyofasi (semiz/ingichka, bo'yi, ismi)
			if (!_cloud.NeedsProfile && !ShahLook.Configured) _loginScreen.OpenShahSetup(false, _cloud.DisplayName);
			return;
		}
		LoadCloudGame();
	}

	/// <summary>Shoh qiyofasi o'zgardi: shaharda yurgan shoh figurasiga darhol qo'llanadi.</summary>
	private void RefreshShahLook()
	{
		if (_units == null) return;
		foreach (Unit u in _units.Units)
			if (u is ShahUnit sh && sh.Team == UnitTeam.Player) sh.BodyScale = ShahLook.Scale;
	}

	/// <summary>Yangi hukmdor: YANGI HUKMDOR → DUNYONI TANLASH → YURTNI TANLASH → o'yin.</summary>
	private void ShowWorldSelection()
	{
		if (_loginScreen != null) _loginScreen.Mode = GoogleLoginScreen.Stage.Welcome;
		_worldSelect = new WorldSelectionScreen(_cloud) { Ui = _uiKit };
		_worldSelect.ContinueRequested = () => { _worldSelect = null; LoadCloudGame(); };
		_worldSelect.WorldChosen = worldId =>
		{
			SetOnlineWorld(worldId);
			_worldSelect = null;
			LoadCloudGame();
		};
		_worldSelect.CancelRequested = () =>
		{
			int joined = _worldSelect?.JoinedWorldId ?? 0;
			if (joined > 0) SetOnlineWorld(joined);
			_worldSelect = null;
			LoadCloudGame();
		};
		_worldSelect.Begin();
	}

	/// <summary>Keyingi onlayn ulanish aynan shu dunyoga bo'lsin (cross-platform bir xil saltanat).</summary>
	private void SetOnlineWorld(int worldId)
	{
		if (worldId <= 0) return;
		_online.Settings.WorldId = worldId;
		_online.Settings.Save();
	}

	private void LoadCloudGame()
	{
		if (_loadingCloud) return;
		_loadingCloud = true;
		if (_loginScreen != null) _loginScreen.Busy = "Saltanat yuklanmoqda";
		_cloud.LoadSave((json, err) =>
		{
			_loadingCloud = false;
			if (_loginScreen != null) _loginScreen.Busy = null;
			if (err == "unauthorized")
			{
				_cloud.SignOut();
				return;
			}
			if (err != null)
			{
				_cloud.ReportError(err);
				return;
			}
			SaveGameData data = SaveManager.FromJson(json);
			bool fresh = data == null;
			if (data != null)
			{
				ApplySave(data, "Xush kelibsiz, " + _cloud.DisplayName + "! Saltanat qolgan joyidan davom etmoqda");
			}
			else
			{
				_pendingStoryMode = true;
				_pendingStory = null;
				NewGame(_currentSeed);
				_camera.Jump(IsoMath.TileToWorld(new Vector2(GameConfig.CityCenter, GameConfig.CityCenter)), StartZoom());
				_state.ShowMessage("Xush kelibsiz, " + _cloud.DisplayName + "! Yangi saltanat boshlandi");
			}
			_ui.AccountName = _cloud.AccountLabel;
			_loginMode = false;
			_menuMode = false;
			_frames = 0;
			_autosaveTimer = 0f;
			bool upgraded = (_opt.FullUpgrade || _cloud.IsVip) && !_fullUpgradeDone;
			if (upgraded) { _fullUpgradeDone = true; ApplyFullUpgrade(); }
			// Yangi o'yinda dastlabki saqlash VIP kuchaytirishdan KEYIN yuboriladi (aks holda eski holat yuklanadi).
			else if (fresh) UploadNow(showMessage: false);
			ConnectOnlineWorld();
			if (_opt.OnlineTab != null) { _onlinePanel.ShowTab(_opt.OnlineTab); _onlinePanel.Open(); }
			if (_opt.UnlockParcelAfterLogin.HasValue) RunUnlockParcelTest(_opt.UnlockParcelAfterLogin.Value);
		});
	}

	/// <summary>Sinov: berilgan uchastkani ochadi (resurs/aholi talabini chetlab) va darhol serverga saqlaydi.</summary>
	private void RunUnlockParcelTest(int parcelId)
	{
		LandParcel parcel = _state.Land.Parcels.FirstOrDefault((LandParcel p) => p.Id == parcelId);
		if (parcel == null) return;
		_state.Land.Unlock(parcel);
		_state.Land.RefreshStates(_state.Population);
		_state.ShowMessage("Sinov: " + parcel.NameUz + " ochildi");
		Point c = parcel.GridBounds.Center;
		_camera.Jump(IsoMath.TileToWorld(new Vector2(c.X, c.Y)), 0.5f);
		UploadNow(showMessage: false);
	}

	/// <summary>Onlayn dunyo (bozor, ittifoq, saltanat) — Google tokeni bilan, parolsiz.</summary>
	private void ConnectOnlineWorld()
	{
		if (CloudSignedIn) _online.LoginWithToken(_cloud.ServerUrl, _cloud.Token, _cloud.Username);
	}

	/// <summary>Boshqa qurilma yangiroq saqlagan: eng so'nggi holatni yuklaymiz (eski holat ustidan yozilmaydi).</summary>
	private void OnCloudConflict()
	{
		ReloadFromCloud("O'yin boshqa qurilmada davom ettirilgan — eng so'nggi holat yuklandi");
	}

	private void ReloadFromCloud(string message)
	{
		if (!CloudSignedIn) return;
		_cloud.LoadSave((json, err) =>
		{
			SaveGameData data = err == null ? SaveManager.FromJson(json) : null;
			if (data == null)
			{
				_state.ShowMessage(err ?? "Serverda saqlangan o'yin yo'q", isError: true);
				return;
			}
			ApplySave(data, message);
			_ui.AccountName = _cloud.AccountLabel;
		});
	}

	/// <summary>Google hisobidan chiqish: progress serverga saqlanadi, so'ng kirish ekrani.</summary>
	private void DoLogout()
	{
		FlushCloudSaveBlocking();
		_online.Logout();
		_cloud.SignOut();
		_onlinePanel.Close();
		_worldSelect = null;
		_fullUpgradeDone = false;
		NewGame(_currentSeed);
		_loginScreen = new GoogleLoginScreen(_cloud, _cloud.ServerUrl) { Ui = _uiKit, ShahTexture = () => _assets.TryGet(SpriteKeys.UnitShah + "_0"), ShahChanged = RefreshShahLook };
		_loginMode = true;
	}

	/// <summary>Saqlangan holatni o'yinga qo'llaydi (lokal fayl yoki bulut).</summary>
	private void ApplySave(SaveGameData saveGameData, string message)
	{
		SaveManager.MigrateLayout(saveGameData, GameConfig.CityOffset, GameConfig.HalfTileH);
		_pendingStory = saveGameData.Story;
		_pendingStoryMode = saveGameData.Story != null && saveGameData.Story.StoryModeActive;
		NewGame(saveGameData.Seed, saveGameData.Governor);
		_state.Coins = saveGameData.Coins;
		_state.Food = saveGameData.Food;
		_state.Clock.Set(saveGameData.Day, saveGameData.DayFraction);
		_state.SetSpeed(saveGameData.Speed);
		_state.Policy.PopulationTax = saveGameData.PopTax;
		_state.Policy.TradeTax = saveGameData.TradeTax;
		_state.Policy.ProductionTax = saveGameData.ProdTax;
		_state.Pop.Restore(saveGameData.Citizens, _buildings);
		_npcInteractions.BindUnits(_units, _state.Pop, saveGameData.CitizenBindings);
		if (saveGameData.World != null)
		{
			_state.World.Restore(saveGameData.World);
		}
		if (saveGameData.Military != null)
		{
			_state.Military.Restore(saveGameData.Military);
		}
		if (saveGameData.Civ != null)
		{
			_state.Civ.Restore(saveGameData.Civ);
		}
		if (saveGameData.OpenParcels != null)
		{
			_state.Land.ApplyOpen(saveGameData.OpenParcels);
		}
		_state.Weather.SetCurrent((Weather)saveGameData.Weather);
		if (saveGameData.Roads != null)
		{
			foreach (SavedRoad road in saveGameData.Roads)
			{
				if (_map.InBounds(road.X, road.Y) && _map.GetOccupant(road.X, road.Y) == null)
				{
					_map.SetTile(road.X, road.Y, TileType.Road);
				}
			}
		}
		// Ko'chirilgan yaratilgan binolar — o'yinchi binolaridan OLDIN (bo'shagan joyga o'yinchi qurgan bo'lishi mumkin)
		if (saveGameData.MovedBuildings != null)
		{
			foreach (int[] mv in saveGameData.MovedBuildings)
			{
				if (mv == null || mv.Length < 4) continue;
				Building gb = _buildings.Buildings.FirstOrDefault(b => !b.BuiltByPlayer && b.GeneratedOrigin == null && b.Position.X == mv[0] && b.Position.Y == mv[1]);
				if (gb == null) continue;
				if (_buildings.Move(gb, new Point(mv[2], mv[3]), strictTerrain: false)) gb.GeneratedOrigin = new Point(mv[0], mv[1]);
			}
		}
		foreach (SavedBuilding playerBuilding in saveGameData.PlayerBuildings)
		{
			BuildingType type = (BuildingType)playerBuilding.Type;
			Building building = BuildingFactory.Create(type, new Point(playerBuilding.X, playerBuilding.Y), playerBuilding.Variant);
			building.BuiltByPlayer = true;
			if (_buildings.Add(building))
			{
				// Daraja Upgrade() orqali tiklanadi — mustahkamlik va sig'im ham darajaga mos bo'ladi
				while (building.Level < Math.Min(playerBuilding.Level, BuildingUpgradeSystem.MaxLevel)) building.Upgrade();
				if (playerBuilding.HitPoints > 0)
				{
					building.HitPoints = Math.Min(playerBuilding.HitPoints, building.MaxHitPoints);
				}
				if (building is Farm farm)
				{
					farm.RestoreCropState(playerBuilding.CropStage, playerBuilding.GrowthDays, playerBuilding.StoredProduce);
				}
			}
		}
		if (saveGameData.BuildingLevels != null)
		{
			foreach (int[] lv in saveGameData.BuildingLevels)
			{
				if (lv == null || lv.Length < 3) continue;
				Building gb = _buildings.Buildings.FirstOrDefault(b => !b.BuiltByPlayer && b.Position.X == lv[0] && b.Position.Y == lv[1]);
				if (gb == null) continue;
				while (gb.Level < Math.Min(lv[2], BuildingUpgradeSystem.MaxLevel)) gb.Upgrade();
			}
		}
		_eliteArmy = saveGameData.EliteArmy;
		if (_eliteArmy) ApplyEliteArmy();
		AndijonSaltanatlar.Interiors.InteriorStore.Restore(saveGameData.Interiors);
		if (saveGameData.FarmSeeds >= 0) _state.Farming.Seeds = saveGameData.FarmSeeds;
		_state.Farming.ToolsLevel = saveGameData.FarmTools;
		_state.Farming.FertilizerCharges = saveGameData.FarmFertilizer;
		_state.Farming.Irrigation = saveGameData.FarmIrrigation;
		_state.Land.RefreshStates(_state.Population);
		_camera.Jump(new Vector2(saveGameData.CameraX, saveGameData.CameraY), saveGameData.Zoom);
		_governor?.Analyze();
		_state.ShowMessage(message);
	}

	protected override void Update(GameTime gameTime)
	{
		_perf?.BeginFrame();
		_perf?.BeginUpdate();
		if (_introMode && _introScreen != null)
		{
			_introScreen.Update((float)gameTime.ElapsedGameTime.TotalSeconds, IsActive);
			if (_introScreen.Done) { _introScreen.Dispose(); _introScreen = null; _introMode = false; }
			_perf?.EndUpdate();
			_perf?.EndFrame();
			base.Update(gameTime);
			return;
		}
		if (TimePrefs.Dirty && _state != null)
		{
			_state.Clock.SetDayLength(TimePrefs.Current.DayMinutes * 60f);
			TimePrefs.Dirty = false;
		}
		if (_state != null)
		{
			float vf = TimePrefs.VisualFraction(_state.Clock.DayFraction);
			SeasonVisuals.CurrentComfort = SeasonVisuals.ComfortOf(TimePrefs.VisualSeason(_state.Season), _state.Weather.Current, vf);
			UpdateComfortTalk((float)gameTime.ElapsedGameTime.TotalSeconds);
		}
		if (_exitRequested)
		{
			_perf?.EndUpdate();
			_perf?.EndFrame();
			Exit();
			return;
		}
		float num = (float)gameTime.ElapsedGameTime.TotalSeconds;
		_input.Update(IsActive);
		_cloud?.Pump();
		_cloud?.Tick((float)gameTime.ElapsedGameTime.TotalSeconds);
		// Yangilash oynasi (modal): GitHub'da yangi versiya topilsa, har qanday rejim ustidan ko'rsatiladi.
		if (_updateScreen == null && _pendingUpdate != null && _uiKit != null)
		{
			_updateScreen = new AndijonSaltanatlar.UI.UpdateScreen(_pendingUpdate) { Ui = _uiKit };
			_updateScreen.Dismissed = () => { _updateScreen = null; _pendingUpdate = null; };
			_updateScreen.Downloaded = () => { _updateScreen = null; _pendingUpdate = null; };
		}
		if (_updateScreen != null)
		{
			_updateScreen.Update(_input);
			_dayNight.Update(TimePrefs.VisualFraction(_state.Clock.DayFraction), TimePrefs.VisualSeason(_state.Season));
			_time += num;
			_perf?.EndUpdate();
			_perf?.EndFrame();
			base.Update(gameTime);
			return;
		}
		if (_loginMode)
		{
			if (_worldSelect != null) _worldSelect.Update(_input);
			else _loginScreen.Update(_input);
			// Sinov: login manzili faylga yoziladi va Playwright Google sahifasini o'zi to'ldiradi.
			if (_opt.LoginUrlFile != null && !_autoLoginStarted && _cloud.State == AndijonSaltanatlar.Online.CloudLoginState.SignedOut)
			{
				_autoLoginStarted = true;
				_cloud.StartGoogleLogin(DefaultServer());
			}
			_onlinePanel.Update(_input, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height, num);
			_units.Update(num * 0.6f);
			_map.Update(num);
			_dayNight.Update(TimePrefs.VisualFraction(_state.Clock.DayFraction), TimePrefs.VisualSeason(_state.Season));
			_time += num;
			_frames++;
			if (_opt.ScreenshotPath != null && !_opt.ScreenshotAfterLogin && _frames >= (_opt.ScreenshotFrames ?? 6))
			{
				_captureNextDraw = true;
			}
			_perf?.EndUpdate();
			_perf?.EndFrame();
			base.Update(gameTime);
			return;
		}
		if (_menuMode)
		{
			Viewport viewport = GraphicsDevice.Viewport;
			_mainMenu.SetMouse(_input.MousePosition);
			switch (_mainMenu.Update(_input, viewport.Width, viewport.Height))
			{
			case MainMenuAction.Start:
				_pendingStoryMode = true;
				NewGame(_currentSeed);
				_menuMode = false;
				break;
			case MainMenuAction.Continue:
				DoLoad();
				_menuMode = false;
				break;
			case MainMenuAction.History:
				_menuMode = false;
				_civMode = true;
				break;
			case MainMenuAction.Exit:
				_exitRequested = true;
				break;
			case MainMenuAction.Online:
				_menuMode = false;
				_onlinePanel.Open();
				break;
			}
			_onlinePanel.Update(_input, viewport.Width, viewport.Height, num);
			_units.Update(num * 0.6f);
			_birds?.Update(num * 0.6f, _dayNight.NightAmount, null);
			_livestock?.Update(num * 0.6f);
			_map.Update(num);
			_dayNight.Update(TimePrefs.VisualFraction(_state.Clock.DayFraction), TimePrefs.VisualSeason(_state.Season));
			_frames++;
			if (_opt.ScreenshotPath != null && _frames >= (_opt.ScreenshotFrames ?? 6))
			{
				_captureNextDraw = true;
			}
			_perf?.EndUpdate();
			_perf?.EndFrame();
			base.Update(gameTime);
			return;
		}
		Viewport viewport2 = GraphicsDevice.Viewport;
		// Online panel: tarmoq natijalarini qabul qiladi (har kadr); ochiq bo'lsa barcha kiritishni o'zi oladi.
		bool onlineOpen = _onlinePanel.IsOpen;
		_onlinePanel.Update(_input, viewport2.Width, viewport2.Height, num);
		if (!onlineOpen)
		{
			HandleHotkeys();
		}
		else if (_input.KeyPressed(Keys.M) && !_onlinePanel.WantsKeyboard)
		{
			_onlinePanel.ToggleWorldMap();   // M — xaritani yopish
		}
		if (_siegeTestPending && _siege != null && !_siege.Active && !_loginMode && !_menuMode && !_introMode)
		{
			_siegeTestPending = false;
			if (_opt.TestArmy.HasValue)
			{
				var types = new[] { AndijonSaltanatlar.Military.UnitType.Qilichboz, AndijonSaltanatlar.Military.UnitType.Nayzador, AndijonSaltanatlar.Military.UnitType.Otliq, AndijonSaltanatlar.Military.UnitType.Qalqonchi, AndijonSaltanatlar.Military.UnitType.Piyoda };
				for (int i = 0; i < _opt.TestArmy.Value; i++) _state.Military.Recruit(types[i % types.Length], 1, 2);
			}
			if (_siege.Start(_opt.SiegeRegion.Value, GraphicsDevice.Viewport))
			{
				if (_opt.SiegeWatch) _siege.Combat.Release(false);
				_siegeFromMap = true;
				_strategicMode = false;
				if (_opt.SiegeShahDown) _siege.TestKillShah();
				if (_opt.SiegePalace) _siege.TestEnterPalace();
			}
		}
		// Onlayn dunyo xaritasidan (M) yurish qasrga yetib keldi — dushman shahrida qamal
		if (_onlinePanel.SiegeArrived != null && !_siege.Active && !_loginMode && !_menuMode)
		{
			var target = _onlinePanel.SiegeArrived;
			_onlinePanel.SiegeArrived = null;
			if (_onlinePanel.IsOpen) _onlinePanel.Close();
			if (_interior != null && _interior.Active) _interior.Exit();
			if (_combat != null && _combat.Possessed != null) _combat.Release(false);
			_strategicMode = _civMode = _questMode = false;
			if (_siege.StartOnline(target, GraphicsDevice.Viewport)) { _siegeFromMap = true; _siegeOnline = true; }
		}
		if (_siegeFromMap && !_siege.Active)
		{
			// Qamal tugadi — xaritaga qaytamiz (natija va yangi bayroq xaritada ko'rinadi)
			_siegeFromMap = false;
			if (_siegeOnline) { _siegeOnline = false; if (!_onlinePanel.IsOpen) _onlinePanel.ToggleWorldMap(); }
			else _strategicMode = true;
		}
		if (_arenaFromMap && !_arena.Active)
		{
			// Yurish jangi tugadi — saltanat xaritasiga qaytamiz
			_arenaFromMap = false;
			_strategicMode = true;
		}
		if (onlineOpen)
		{
			// O'yin dunyosi orqada yashashda davom etadi (pastdagi simulyatsiya), lekin bosishlar panelga tegishli.
		}
		else if (_siege.Active)
		{
			// Qamal: dushman shahri — kiritish to'liq qamalga tegishli
			_siege.Update(_state.Paused ? 0f : num, _input, GraphicsDevice.Viewport);
		}
		else if (_arena.Active)
		{
			// Jang maydoni: tugmalar sahnaning o'zida (UiKit, Draw paytida) — bu yerda faqat Esc
			if (_input.KeyPressed(Keys.Escape)) _arena.Close();
		}
		else if (_strategicMode)
		{
			Point sv = LegacyUiSize(out float ss);
			_input.PointerScale = ss;
			_strategic.Update(_input, sv.X, sv.Y);
			_input.PointerScale = 1f;
			if (_strategic.MarchArrivedRegion >= 0)
			{
				int rid = _strategic.MarchArrivedRegion;
				_strategic.MarchArrivedRegion = -1;
				// Yurish yetib keldi: qo'shin dushman SHAHRIGA kiradi (qamal)
				if (_siege.Start(rid, GraphicsDevice.Viewport)) { _strategicMode = false; _siegeFromMap = true; }
			}
			if (_strategic.BackRequested)
			{
				_strategic.BackRequested = false;
				_strategicMode = false;
			}
			if (_strategic.EnterCityRequested)
			{
				_strategic.EnterCityRequested = false;
				_strategicMode = false;
			}
		}
		else if (_civMode)
		{
			Point cv = LegacyUiSize(out float cs);
			_input.PointerScale = cs;
			_civ.Update(_input, cv.X, cv.Y);
			_input.PointerScale = 1f;
			if (_civ.BackRequested)
			{
				_civ.BackRequested = false;
				_civMode = false;
			}
		}
		else if (_questMode)
		{
			Point qv = LegacyUiSize(out float qs);
			_input.PointerScale = qs;
			_quest.Update(_input, qv.X, qv.Y);
			_input.PointerScale = 1f;
			if (_quest.FocusMapRequested)
			{
				_quest.FocusMapRequested = false;
				_camera.Jump(_quest.FocusTarget, _camera.Zoom);
			}
			if (_quest.BackRequested)
			{
				_quest.BackRequested = false;
				_questMode = false;
			}
		}
		else
		{
			bool flag = _storyOverlay != null && _storyOverlay.DialogueOpen;
			// Uy ichida (interyer) shahar UI'si yangilanmaydi.
			bool inHouse = _interior.Active;
			// Eski piksel-shriftli UI virtual o'lchamda (telefonda 720p) — kursor ham shu masshtabga o'giriladi.
			Point uv = LegacyUiSize(out float us);
			_ui.Touch = _input.IsTouch || _opt.Phone;
			_input.PointerScale = us;
			if (_storyOverlay != null) _storyOverlay.HideTracker = true; // yonboshdagi missiya kartasi olib tashlandi
			_storyOverlay?.Update(_input, num, uv.X, uv.Y);
			// Vazir ekran xabari (missiya/muhim voqea) — navbatni olib, ko'rsatamiz.
			if (_vizierPopup != null)
			{
				while (_state.TryDequeueVizier(out string vzT, out string vzB)) _vizierPopup.Enqueue(vzT, vzB);
				_vizierPopup.Update(num, _input, uv.X, uv.Y);
			}
			if (!inHouse) _ui.Update(_input, uv.X, uv.Y);
			// Missiya kartasi ustidagi bosish orqadagi xaritaga o'tmasin
			if (_storyOverlay != null && _storyOverlay.PointerOverTracker) _input.MouseOverUI = true;
			_input.PointerScale = 1f;
			if (!inHouse && _ui.OpenOnlineRequested)
			{
				_ui.OpenOnlineRequested = false;
				_onlinePanel.Open();
				if (CloudSignedIn && !_online.LoggedIn && !_online.Busy) ConnectOnlineWorld();
			}
			if (_ui.OpenCivRequested)
			{
				_ui.OpenCivRequested = false;
				_civMode = true;
			}
			if (_ui.OpenMissionsRequested || (_storyOverlay != null && _storyOverlay.JournalRequested))
			{
				_ui.OpenMissionsRequested = false;
				if (_storyOverlay != null) _storyOverlay.JournalRequested = false;
				_questMode = true;
			}
			if (_ui.BeautifyRequested)
			{
				_ui.BeautifyRequested = false;
				int added = World.CityBeautifier.Beautify(_map, _buildings, new Random());
				_state.ShowMessage(added > 0 ? "Shahar bezatildi — " + added + " ta bezak qo'shildi" : "Bezash uchun bo'sh joy topilmadi");
			}
			if (_ui.FindNextRequested)
			{
				_ui.FindNextRequested = false;
				FindNextBuilding();
			}
			if (_ui.MergeRequested)
			{
				_ui.MergeRequested = false;
				MergeSelectedBuilding();
			}
			if (_ui.OpenWorldRequested)
			{
				_ui.OpenWorldRequested = false;
				_strategicMode = true;
			}
			if (_ui.OpenArenaRequested)
			{
				_ui.OpenArenaRequested = false;
				_arena.Start();
			}
			bool possessing = _combat.Possessed != null;
			_camera.KeyboardPanEnabled = !possessing;
			if (!inHouse)
			{
				_camera.Update(num, _input, viewport2);
			}
			bool flag2 = _ui.GovernorPanel != null && _ui.GovernorPanel.PickMode != GovernorMapMode.None;
			if (!flag && !flag2 && !possessing)
			{
				_construction.Update(_input, _camera);
			}
			if (flag2)
			{
				if (!flag && _input.LeftPressed && !_input.MouseOverUI)
				{
					Point tile = IsoMath.WorldToTile(_camera.ScreenToWorld(_input.MouseVector));
					_ui.GovernorPanel.HandleMapClick(tile);
				}
			}
			else if (!flag && !possessing && _state.Mode == GameMode.Playing && _input.LeftPressed && !_input.MouseOverUI)
			{
				Vector2 vector = _camera.ScreenToWorld(_input.MouseVector);
				object obj = _renderer.PickAt(vector, _map, _buildings, _units);
				if (obj == null)
				{
					Point point = IsoMath.WorldToTile(vector);
					LandParcel landParcel = _state.Land.ParcelAt(point.X, point.Y);
					if (landParcel != null && landParcel.State != LandParcelState.Open)
					{
						obj = landParcel;
					}
				}
				bool flag3 = false;
				if (obj == null && _governor?.Blueprint != null)
				{
					Point tile2 = IsoMath.WorldToTile(vector);
					if (_governor.SelectBlueprintAt(tile2))
					{
						flag3 = true;
						_state.ShowMessage(_governor.DescribeSelectedBlueprintUz());
					}
				}
				if (!flag3)
				{
					_state.Select(obj);
				}
			}
			if (_opt.HoverTile.HasValue && _state.Mode == GameMode.BuildMode)
			{
				_construction.SetHover(_opt.HoverTile.Value);
			}
		}
		// Jang real vaqtda (o'yin tezligiga bog'liq emas), pauzada to'xtaydi.
		// Kiritish faqat shahar ekranida va hech qanday panel/dialog ochiq bo'lmaganda beriladi.
		bool combatInput = !onlineOpen && !_strategicMode && !_civMode && !_questMode && !_arena.Active && !_siege.Active
			&& !(_storyOverlay != null && _storyOverlay.DialogueOpen)
			&& !(_ui.GovernorPanel != null && _ui.GovernorPanel.IsOpen);
		bool wasInHouse = _interior.Active;
		_combat.Update(_state.Paused ? 0f : num, (combatInput && !wasInHouse) ? _input : null, _camera);

		// Odam ichiga kirish/chiqishda kamera yaqinlashadi va avvalgi holatiga qaytadi (3-shaxs ko'rinishi)
		Unit possessedNow = _combat.Possessed;
		if (possessedNow != null && _lastPossessed == null)
		{
			_zoomBeforePossess = _camera.Zoom;
			_camera.SetTargetZoom(GameConfig.PossessZoom);
		}
		else if (possessedNow == null && _lastPossessed != null)
		{
			if (_interior.Active) _interior.Exit();
			_camera.SetTargetZoom(_zoomBeforePossess);
		}
		_lastPossessed = possessedNow;

		// Uyga kirish / uy ichida yurish
		bool canEnter = combatInput && possessedNow != null && !_combat.RaidActive && !_combat.RaidIncoming;
		_interior.ShahAway = _combat.ShahInWorld;
		// Bino tafsilotlari panelidagi "ICHKARIGA KIRISH" tugmasi
		if (!_interior.Active && _ui.EnterRequested != null)
		{
			Building target = _ui.EnterRequested;
			_ui.EnterRequested = null;
			if (canEnter && possessedNow != null && AndijonSaltanatlar.Interiors.HouseInterior.IsEnterable(target))
			{
				if (AndijonSaltanatlar.Interiors.HouseInterior.MayEnter(target, possessedNow))
					_interior.Enter(target, possessedNow);
				else
					_state.ShowMessage("Saroyga faqat soqchilar va jangchilar kira oladi!", true);
			}
			else if (possessedNow == null)
				_state.ShowMessage("Avval odamni tanlab, unga kirish buyrug'ini bering", true);
		}
		if (_interior.Active)
		{
			_interior.Update(_state.Paused ? 0f : num, combatInput ? _input : null);
			if (_interior.BecomeShahRequested)
			{
				// Saroyda shoh yonida F: o'yinchi shohga aylanadi va u saroy eshigidan shaharga chiqadi
				_interior.BecomeShahRequested = false;
				Vector2 door = _interior.OutsideDoor(_interior.House);
				ShahUnit shah = _combat.SpawnShahAndPossess(door);
				_interior.Exit(shah);
				_lastPossessed = _combat.Possessed;
			}
		}
		else
		{
			_interior.UpdateNearby(canEnter ? possessedNow : null);
			if (canEnter && _interior.NearbyHouse != null && _input.KeyPressed(Keys.Enter))
			{
				_interior.Enter(_interior.NearbyHouse, possessedNow);
			}
		}

		Unit followNow = _combat.Possessed;
		if (followNow != null && !_interior.Active)
		{
			if (wasInHouse)
			{
				_camera.Jump(IsoMath.TileToWorld(followNow.Position) + new Vector2(0f, -20f), _camera.Zoom);
			}
			_camera.Follow(IsoMath.TileToWorld(followNow.Position) + new Vector2(0f, -20f), num);
		}
		float dt = (_state.Paused ? 0f : (num * (float)_state.Speed));
		float speedScale = 1f - 0.8f * _dayNight.NightAmount;
		foreach (Unit unit in _units.Units)
		{
			if (unit is CitizenUnit)
			{
				unit.SpeedScale = speedScale;
			}
		}
		_units.Update(dt * (_combat?.TimeScale ?? 1f));
		UpdateMilitaryRanks(dt);
		_farmWork.Update(dt, _map, _buildings, _units, _state);
		_units.ApplyPhysics(_map, _combat.Possessed, dt);
		_combat.UnrestHigh = _taxRiot != null && _taxRiot.Active;
		_swim.Force = _opt.Swim;
		if (_opt.FarmDemo.HasValue && _opt.ScreenshotPath != null && _farmWork.FirstCarrier is Unit carrier) _camera.Jump(IsoMath.TileToWorld(carrier.Position) + new Vector2(0, 60), 2.8f);
		if (_opt.Swim && _opt.ScreenshotPath != null)
		{
			if (_swim.FirstInWater is Unit swimmer) _camera.Jump(IsoMath.TileToWorld(swimmer.Position) + new Vector2(0, 60), 2.6f);
			_state.ShowMessage(_swim.Debug);
		}
		_swim.Update(dt, _map, _units, _state, _combat.Possessed, u => _farmWork.IsWorker(u));
		// Uyga kirmagan paytda oilalar o'zlari non yopadi
		if (!_state.Paused) AndijonSaltanatlar.Interiors.HouseInterior.TickOutside(dt * _state.Speed / Math.Max(1f, _state.Clock.SecondsPerDay) * 24f);
		_renderer.Tick(dt);
		_birds?.Update(dt, _dayNight.NightAmount, _combat.Possessed);
		_livestock?.Update(dt);
		_carts?.Update(dt, _map, _buildings, _state);
		_curfew?.Update(dt, _map, _buildings, _units, _dayNight.NightAmount, _combat.Possessed, u => (_farmWork?.IsWorker(u) ?? false) || (_swim?.IsSwimming(u) ?? false));
		_citizenSocial.Update(_units, dt);
		_citizenLife?.Update(dt, _map, _buildings, _units, _state, _combat.Possessed);
		_taxRiot?.SetEscortShield(_combat.EscortCount);
		_taxRiot?.Update(dt, _state, _map, _buildings, _units, _combat.Possessed, _combat.RaidActive || _combat.RaidIncoming);
		if (_taxRiot?.PalaceAlert != null) { _state.ShowMessage(_taxRiot.PalaceAlert, true); _taxRiot.PalaceAlert = null; }
		if (_opt.ScreenshotPath != null && _taxRiot != null && _taxRiot.Active) _state.ShowMessage("DBG " + _taxRiot.DebugSummary);
		UpdateFarmControl();
		_map.Update(dt);
		int day = _state.Day;
		_state.Update(num, _buildings);
		SyncArenaGladiators();
		if (_state.Day != day)
		{
			_npcInteractions.RefreshUnits(_units, _state.Pop);
			_citizenLife?.DayTick(_state);
			_governor?.OnDayTick();
			string herdNews = _livestock?.DayTick(_state);
			if (herdNews != null)
			{
				_state.ShowMessage(herdNews);
			}
		}
		_time += num;
		_dayNight.Update(TimePrefs.VisualFraction(_state.Clock.DayFraction), TimePrefs.VisualSeason(_state.Season));
		_ui.PhaseName = _dayNight.PhaseName;
		AnnounceSeason();
		// Avtomatik saqlash: har 30 soniyada va yer ochilganda darhol (o'yin doim serverda).
		if (CloudSignedIn)
		{
			_autosaveTimer += num;
			if (_state.LandChanged || _autosaveTimer >= AutosaveSeconds)
			{
				_state.LandChanged = false;
				_autosaveTimer = 0f;
				UploadNow(showMessage: false);
			}
			_ui.CloudOk = !_cloud.LastSaveFailed;
		}
		else
		{
			_ui.CloudOk = false;
		}
		_frames++;
		if (_opt.ScreenshotPath != null && _frames >= (_opt.ScreenshotFrames ?? 6))
		{
			_captureNextDraw = true;
		}
		_perf?.SetVisibleSprites(_renderer != null ? _renderer.LastDrawnCount : 0);
		_perf?.SetActiveNpcs(_units != null ? _units.Units.Count : 0);
		_perf?.EndUpdate();
		_perf?.EndFrame();
		base.Update(gameTime);
	}

	/// <summary>Joriy faslni yer, daraxt, qushlarga beradi (har kadr; arzon — atlas va teksturalar keshlanadi).</summary>
	private void SyncSeason()
	{
		// Fasl ko'rinishi (yer, qor, daraxtlar) — SeasonVisuals (static) orqali; bu yerda faqat qushlar
		Season season = TimePrefs.VisualSeason(_state.Season);
		if (_birds != null) _birds.Season = season;
	}

	/// <summary>Fasl almashganda xabar (o'yin boshida emas — faqat almashuvda).</summary>
	private void AnnounceSeason()
	{
		Season season = _state.Season;
		if (_announcedSeason == season) return;
		bool first = !_announcedSeason.HasValue;
		_announcedSeason = season;
		if (first) return;
		string text = season switch
		{
			Season.Bahor => "BAHOR keldi! O'riklar gulladi, laylaklar qaytdi. Chorva tez ko'payadi.",
			Season.Yoz => "YOZ keldi! Jazirama issiq, ariqlar sayozlashadi.",
			Season.Kuz => "KUZ keldi! Hosil pishdi, barglar sarg'aydi.",
			_ => "QISH keldi! Qor yog'di, ariqlar muzladi. Laylaklar issiq o'lkalarga uchib ketdi."
		};
		_state.ShowMessage(text);
	}

	private void ApplySeasonWeather(Season season)
	{
		_state.Weather.SetCurrent(season == Season.Qish ? Weather.Snowy : season == Season.Kuz ? Weather.Windy : Weather.Clear);
	}

	private static bool TryParseSeason(string name, out Season season)
	{
		switch ((name ?? "").Trim().ToLowerInvariant())
		{
			case "bahor": case "spring": season = Season.Bahor; return true;
			case "yoz": case "summer": season = Season.Yoz; return true;
			case "kuz": case "autumn": case "fall": season = Season.Kuz; return true;
			case "qish": case "winter": season = Season.Qish; return true;
		}
		season = Season.Yoz;
		return false;
	}

	private static Vector2 BuildingCenter(Building building)
	{
		return new Vector2(building.Position.X + building.Size.X / 2f, building.Position.Y + building.Size.Y / 2f);
	}

	private void UpdateFarmControl()
	{
		CitizenUnit controlledFarmer = _combat.Possessed as CitizenUnit;
		foreach (Building building in _buildings.Buildings)
		{
			if (!(building is Farm farm)) continue;
			farm.IsPlayerControlled = controlledFarmer != null &&
				controlledFarmer.Kind == CitizenArt.Farmer &&
				controlledFarmer.IsBound &&
				_state.Pop.GetById(controlledFarmer.CitizenId)?.WorkplaceBuildingId == farm.Id;
		}
	}

	private void SyncArenaGladiators()
	{
		Building arena = _buildings.Buildings.FirstOrDefault(b => b.Type == BuildingType.GladiatorArena && b.BuildProgress >= 1f);
		if (arena == null)
		{
			if (_arenaGladiatorOne != null) _units.Remove(_arenaGladiatorOne);
			if (_arenaGladiatorTwo != null) _units.Remove(_arenaGladiatorTwo);
			_arenaGladiatorOne = null;
			_arenaGladiatorTwo = null;
			_arenaBuildingId = 0;
			return;
		}
		if (_arenaBuildingId == arena.Id && _arenaGladiatorOne != null && _arenaGladiatorTwo != null) return;

		if (_arenaGladiatorOne != null) _units.Remove(_arenaGladiatorOne);
		if (_arenaGladiatorTwo != null) _units.Remove(_arenaGladiatorTwo);
		float y = arena.Position.Y + arena.Size.Y / 2f;
		Vector2 west = new Vector2(arena.Position.X - 1f, y);
		Vector2 east = new Vector2(arena.Position.X + arena.Size.X + 1f, y);
		_arenaGladiatorOne = _units.Add(new GladiatorUnit(
			new[] { west, west + new Vector2(0f, 1.5f), west + new Vector2(1f, 1.5f) },
			"Gladiator Bahodir", arena.Id));
		_arenaGladiatorTwo = _units.Add(new GladiatorUnit(
			new[] { east, east + new Vector2(0f, -1.5f), east + new Vector2(-1f, -1.5f) },
			"Gladiator Rustam", arena.Id));
		_arenaBuildingId = arena.Id;
	}

	private Building FindNearbyBuilding(BuildingType type, Vector2 position, float range)
	{
		Building nearest = null;
		float best = range;
		foreach (Building building in _buildings.Buildings)
		{
			if (building.Type != type) continue;
			float distance = Vector2.Distance(position, BuildingCenter(building));
			if (distance < best)
			{
				best = distance;
				nearest = building;
			}
		}
		return nearest;
	}

	private void PerformCitizenWork(CitizenUnit citizen)
	{
		if (!citizen.TryBeginWork(_state.Day))
		{
			_state.ShowMessage("Bu fuqaro bugungi ishini bajardi. Ertaga davom etadi.");
			return;
		}

		if (citizen.Kind == CitizenArt.Farmer)
		{
			Farm farm = null;
			float best = 2.8f;
			foreach (Building building in _buildings.Buildings)
			{
				if (!(building is Farm candidate)) continue;
				float distance = Vector2.Distance(citizen.Position, BuildingCenter(candidate));
				if (distance < best) { best = distance; farm = candidate; }
			}

			if (farm != null)
			{
				if (farm.CropStage == FarmCropStage.Empty)
				{
					if (!_state.Farming.TryConsumeSeed())
					{
						_state.ShowMessage("Urug' yo'q. FERMERLIK bo'limida urug' sotib oling.", true);
						return;
					}
					farm.PlantCrop();
					_state.ShowMessage("Dehqon urug' sepdi. Hosil 3 o'yin kunida yetiladi.");
				}
				else if (farm.CropStage == FarmCropStage.Growing)
				{
					_state.ShowMessage("Ekin o'smoqda: " + farm.GrowthDays + "/" + Farm.CropGrowthDays + " kun.");
				}
				else
				{
					int harvest = farm.HarvestCrop();
					_state.ShowMessage(harvest + " birlik hosil o'rildi. Bozorga olib boring.");
				}
				return;
			}

			Building market = FindNearbyBuilding(BuildingType.Market, citizen.Position, 2.8f);
			if (market != null)
			{
				int sold = 0;
				foreach (Building building in _buildings.Buildings)
				{
					if (!(building is Farm storedFarm) || storedFarm.StoredProduce == 0) continue;
					if (Vector2.Distance(BuildingCenter(market), BuildingCenter(storedFarm)) > 12f) continue;
					sold += storedFarm.SellStoredProduce();
				}
				if (sold > 0)
				{
					int income = sold * 2;
					_state.Coins += income;
					_state.ShowMessage("Bozorda " + sold + " birlik hosil sotildi: +" + income + " tanga.");
				}
				else _state.ShowMessage("Sotish uchun hosil yo'q. Avval fermadan hosil o'ring.");
				return;
			}

			_state.ShowMessage("Dehqon sifatida ishlash uchun ferma yoki bozor yoniga boring.");
			return;
		}

		if (citizen.Kind == CitizenArt.Merchant || citizen.Kind == CitizenArt.Craftsman)
		{
			if (FindNearbyBuilding(BuildingType.Market, citizen.Position, 2.8f) != null)
			{
				_state.Coins += 4;
				_state.ShowMessage(citizen.RoleLabel + " bozor savdosida qatnashdi: +4 tanga.");
			}
			else _state.ShowMessage("Savdo qilish uchun bozorga boring.");
			return;
		}

		if (citizen.Kind == CitizenArt.Shepherd)
		{
			if (FindNearbyBuilding(BuildingType.Farm, citizen.Position, 2.8f) != null ||
				FindNearbyBuilding(BuildingType.Garden, citizen.Position, 2.8f) != null)
			{
				_state.Food += 3;
				_state.ShowMessage("Chopon chorvani parvarish qildi: +3 oziq-ovqat.");
			}
			else _state.ShowMessage("Chorvani boqish uchun ferma yoki bog'ga boring.");
			return;
		}

		_state.ShowMessage(citizen.RoleLabel + " kundalik yumushini davom ettirdi.");
	}

	private void HandleHotkeys()
	{
		// Jang maydoni (TABS) ochiq — klaviatura faqat jangga tegishli.
		if (_arena != null && _arena.Active) return;
		if (_siege != null && _siege.Active) return;
		// Jangchi ichida: klaviatura to'liq jangchiga tegishli (boshqaruvni CombatSystem o'qiydi).
		if (_combat.Possessed != null)
		{
			if (_input.KeyPressed(Keys.F3))
			{
				_perf.Enabled = !_perf.Enabled;
			}
			return;
		}
		if (_input.KeyPressed(Keys.E) && _state.Selected is CitizenUnit citizen)
		{
			string line = citizen.Kind == CitizenArt.Farmer
				? "Ekinlarim yaxshi o'syapti!"
				: citizen.Kind == CitizenArt.Merchant ? "Bozorda savdo qizg'in!"
				: "Shahar tinchligi barqaror.";
			citizen.Say(line, 4f);
			_state.ShowMessage(citizen.Name + ": " + line);
			return;
		}
		if (_input.KeyPressed(Keys.F) && !_strategicMode && !_civMode && !_questMode)
		{
			_combat.TryPossess(_state.Selected as Unit);
			return;
		}
		if (_input.KeyPressed(Keys.B) && !_strategicMode && !_civMode && !_questMode)
		{
			_combat.TriggerRaid(manual: true);
			return;
		}
		if (_input.KeyPressed(Keys.K) && !_strategicMode && !_civMode && !_questMode)
		{
			_curfew?.Ring(_state); // markaziy qo'ng'iroqni chalish — xalq uylariga kiradi
			return;
		}
		if (_input.KeyPressed(Keys.O))
		{
			_onlinePanel.Toggle();
			return;
		}
		if (_input.KeyPressed(Keys.M))
		{
			// Onlayn dunyoga ulangan bo'lsa — katta xarita: barcha saltanatlar yerlari va qasrlari
			if (CloudSignedIn || _online.LoggedIn)
			{
				_strategicMode = false;
				_civMode = false;
				_questMode = false;
				if (!_online.LoggedIn && !_online.Busy) ConnectOnlineWorld();
				_onlinePanel.ToggleWorldMap();
				return;
			}
			_strategicMode = !_strategicMode;
			_civMode = false;
			_questMode = false;
			return;
		}
		if (_input.KeyPressed(Keys.C))
		{
			_civMode = !_civMode;
			_strategicMode = false;
			_questMode = false;
			return;
		}
		if (_input.KeyPressed(Keys.J))
		{
			_questMode = !_questMode;
			_strategicMode = false;
			_civMode = false;
			return;
		}
		if (_input.KeyPressed(Keys.Escape))
		{
			AIGovernorPanel governorPanel = _ui.GovernorPanel;
			if (governorPanel != null && governorPanel.IsOpen)
			{
				governorPanel.Close();
			}
			else if (governorPanel != null && governorPanel.PickMode != GovernorMapMode.None)
			{
				governorPanel.CancelMapMode();
				_state.ShowMessage("Tahrir rejimi bekor qilindi.");
			}
			else if (_civMode)
			{
				_civMode = false;
			}
			else if (_questMode)
			{
				_questMode = false;
			}
			else if (_strategicMode)
			{
				_strategicMode = false;
			}
			else if (_ui.PetitionOpen)
			{
				_ui.ClosePetition();
			}
			else if (_ui.TimePanelOpen)
			{
				_ui.ToggleTimePanel();
			}
			else if (_ui.CloseOpenPanel())
			{
			}
			else if (_state.ShowHelp)
			{
				_state.ShowHelp = false;
			}
			else if (_state.Mode != GameMode.Playing)
			{
				_state.CancelBuild();
			}
			else if (_state.Selected != null)
			{
				_state.Select(null);
			}
		}
		if (_input.KeyPressed(Keys.Tab))
		{
			_state.CycleSpeed();
		}
		if (_input.KeyPressed(Keys.N))
		{
			// Keyingi fasl (ko'rish uchun) — saqlanmaydi, o'yin oyi bo'yicha fasl keyin o'z yo'lida davom etadi
			_state.SeasonShift++;
			ApplySeasonWeather(_state.Season);
		}
		if (_input.KeyPressed(Keys.P))
		{
			_ui.TogglePetition();
		}
		if (_input.KeyPressed(Keys.V))
		{
			_ui.ToggleGovernor();
		}
		if (_input.KeyPressed(Keys.G))
		{
			GraphicsQualityManager.Cycle();
			TimePrefs.Current.Quality = (int)GraphicsQualityManager.Current;
			TimePrefs.Save();
			_state.ShowMessage("Grafika sifati: " + GraphicsQualityManager.Name());
		}
		if (_input.KeyPressed(Keys.Space))
		{
			_state.Paused = !_state.Paused;
		}
		if (_input.KeyPressed(Keys.H) || _input.KeyPressed(Keys.F1))
		{
			_state.ShowHelp = !_state.ShowHelp;
		}
		if (_input.KeyPressed(Keys.F3))
		{
			_perf.Enabled = !_perf.Enabled;
			_state.ShowMessage(_perf.Enabled ? "Profiler yoqildi" : "Profiler o'chirildi");
		}
		if (_input.KeyPressed(Keys.F5))
		{
			DoSave();
		}
		if (_input.KeyPressed(Keys.F9))
		{
			DoLoad();
		}
		if (!_strategicMode)
		{
			if (_input.KeyPressed(Keys.D1))
			{
				_state.EnterBuildMode(BuildingType.House);
			}
			if (_input.KeyPressed(Keys.D2))
			{
				_state.EnterBuildMode(BuildingType.Tower);
			}
			if (_input.KeyPressed(Keys.D3))
			{
				_state.EnterBuildMode(BuildingType.Market);
			}
			if (_input.KeyPressed(Keys.D4))
			{
				_state.EnterBuildMode(BuildingType.Wall);
			}
			if (_input.KeyPressed(Keys.Home))
			{
				_camera.Jump(IsoMath.TileToWorld(new Vector2(GameConfig.CityCenter, GameConfig.CityCenter)), _camera.Zoom);
			}
		}
	}

	protected override void Draw(GameTime gameTime)
	{
		if (_opt.ExportSpritesDir != null)
		{
			GraphicsDevice.Clear(Color.Black);
			base.Draw(gameTime);
			return;
		}
		_perf?.BeginDraw();
		if (_introMode && _introScreen != null)
		{
			GraphicsDevice.Clear(Color.Black);
			_introScreen.Draw(_sb, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
			_perf?.EndDraw();
			base.Draw(gameTime);
			return;
		}
		SeasonVisuals.Current = TimePrefs.VisualSeason(_state.Season);
		SeasonVisuals.Weather = _state.Weather.Current;
		SeasonVisuals.Wind = _state.Weather.Current switch
		{
			AndijonSaltanatlar.Environment.Weather.Storm => 1f,
			AndijonSaltanatlar.Environment.Weather.Windy => 0.82f,
			AndijonSaltanatlar.Environment.Weather.Rainy => 0.5f,
			AndijonSaltanatlar.Environment.Weather.Snowy => 0.35f,
			AndijonSaltanatlar.Environment.Weather.Clear => 0.18f,
			_ => 0.3f
		};
		bool flag = _captureNextDraw && _opt.ScreenshotPath != null;
		RenderTarget2D renderTarget2D = null;
		if (flag)
		{
			renderTarget2D = new RenderTarget2D(GraphicsDevice, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
			GraphicsDevice.SetRenderTarget(renderTarget2D);
		}
		if (_siege != null && _siege.Active)
		{
			Viewport vpS = GraphicsDevice.Viewport;
			LegacyUiSize(out float siegeHud);
			_siege.Draw(_sb, GraphicsDevice, vpS.Width, vpS.Height, siegeHud);
		}
		else if (_arena.Active)
		{
			GraphicsDevice.Clear(new Color(18, 20, 28));
			Viewport vpA = GraphicsDevice.Viewport;
			_sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
			_arena.Draw(_uiKit, _sb, _input, vpA.Width, vpA.Height, _time);
			_sb.End();
		}
		else if (_strategicMode)
		{
			DrawStrategic();
		}
		else if (_civMode)
		{
			DrawCiv();
		}
		else if (_questMode)
		{
			DrawQuest();
		}
		else if (_interior != null && _interior.Active && !_menuMode)
		{
			GraphicsDevice.Clear(new Color(20, 16, 14));
			Viewport vpIn = GraphicsDevice.Viewport;
			_interior.Draw(_sb, _assets.Pixel, _font, vpIn.Width, vpIn.Height, _dayNight.NightAmount);
		}
		else
		{
			DrawScene();
			DrawAmbient();
			DrawGlows();
			DrawWeather();
			if (_loginMode)
			{
				DrawLoginScreen();
			}
			else if (_menuMode)
			{
				DrawMenu();
			}
			else
			{
				DrawUI();
				DrawStoryOverlay();
				DrawVizierPopup();
				DrawOnline();
			}
		}
		DrawUpdateOverlay();
		if (flag)
		{
			GraphicsDevice.SetRenderTarget(null);
			try
			{
				string directoryName = Path.GetDirectoryName(Path.GetFullPath(_opt.ScreenshotPath));
				if (!string.IsNullOrEmpty(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				using FileStream stream = File.Create(_opt.ScreenshotPath);
				renderTarget2D.SaveAsPng(stream, renderTarget2D.Width, renderTarget2D.Height);
			}
			catch (Exception)
			{
			}
			_sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
			_sb.Draw(renderTarget2D, Vector2.Zero, Color.White);
			_sb.End();
			renderTarget2D.Dispose();
			_exitRequested = true;
		}
		// Profiler overlay (development-only, F3 bilan).
		if (_perf != null && _perf.Enabled)
		{
			_sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
			_sb.Draw(_assets.Pixel, new Rectangle(8, 8, 150, 130), new Color(0, 0, 0, 150));
			_perf.Draw(_sb, _font, 12, 12);
			_sb.End();
		}
		// Shu kadrda UI chizmagan ekranlarda (xarita, sivilizatsiya...) ushlab qolingan bosish keyingi panelga o'tib ketmasin.
		_perf?.EndDraw();
		_input?.TakeClick();
		base.Draw(gameTime);
	}

	private void DrawScene()
	{
		GraphicsDevice.Clear(new Color(26, 30, 22));
		Vector4 visibleWorldRect = _camera.VisibleWorldRect;
		SyncSeason();
		_sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, _camera.Transform);
		_map.Draw(_sb, visibleWorldRect);
		_birds?.DrawShadows(_sb, visibleWorldRect);
		_taxRiot?.DrawShadows(_sb, _assets.Pixel);
		LandExpansionRenderer.Draw(_sb, _map, _state.Land, visibleWorldRect);
		if (_state.Mode == GameMode.Playing && !_input.MouseOverUI)
		{
			Point point = IsoMath.WorldToTile(_camera.ScreenToWorld(_input.MouseVector));
			if (_map.InBounds(point.X, point.Y))
			{
				_map.DrawTileOverlay(_sb, point.X, point.Y, new Color(255, 255, 255, 45), outline: true);
			}
		}
		DrawSelectionHighlight();
		_combat?.DrawGroundMarkers(_sb);
		_blueprintRenderer?.Draw(_sb, _governor?.Blueprint, _governor?.Blueprints.SelectedItemId ?? 0);
		_worldExtras.Clear();
		if (_birds != null) _worldExtras.AddRange(_birds.GroundObjects);
		if (_livestock != null) _worldExtras.AddRange(_livestock.GroundObjects);
		if (_carts != null) _worldExtras.AddRange(_carts.Objects);
		_renderer.Draw(_sb, visibleWorldRect, _map, _buildings, _units, _worldExtras);
		_taxRiot?.DrawAir(_sb, _assets.Pixel);
		_birds?.DrawSky(_sb, visibleWorldRect);
		_construction.Draw(_sb);
		_sb.End();
	}

	private void DrawSelectionHighlight()
	{
		Color color = new Color(255, 215, 120, 80);
		object selected = _state.Selected;
		object obj = selected;
		if (!(obj is Building building))
		{
			if (!(obj is Decoration decoration))
			{
				if (obj is Unit unit)
				{
					_map.DrawMarker(_sb, IsoMath.TileToWorld(unit.Position), new Color(255, 215, 120, 220));
				}
			}
			else
			{
				_map.DrawTileOverlay(_sb, decoration.Position.X, decoration.Position.Y, color, outline: false);
			}
			return;
		}
		for (int i = building.Position.Y; i < building.Position.Y + building.Size.X; i++)
		{
			for (int j = building.Position.X; j < building.Position.X + building.Size.X; j++)
			{
				_map.DrawTileOverlay(_sb, j, i, color, outline: false);
			}
		}
	}

	private void DrawAmbient()
	{
		Viewport viewport = GraphicsDevice.Viewport;
		_sb.Begin(SpriteSortMode.Deferred, Multiply, SamplerState.PointClamp);
		Color ambient = _dayNight.Ambient;
		_sb.Draw(_assets.Pixel, new Rectangle(0, 0, viewport.Width, viewport.Height), ambient);
		_sb.End();
	}

	private void DrawGlows()
	{
		if (_dayNight.NightAmount < 0.06f || _glow == null)
		{
			return;
		}
		float num = 0.85f + 0.15f * MathF.Sin(_time * 3.1f);
		float num2 = _dayNight.NightAmount * num;
		Color color = new Color(255, 170, 80);
		Vector2 origin = new Vector2((float)_glow.Width / 2f, (float)_glow.Height / 2f);
		_sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.PointClamp, null, null, null, _camera.Transform);
		foreach (Decoration decoration in _map.Decorations)
		{
			if (decoration.Type == DecorationType.Brazier)
			{
				Vector2 position = IsoMath.TileToWorld(new Vector2((float)decoration.Position.X + 0.5f, (float)decoration.Position.Y + 0.5f));
				position.Y -= 14f;
				_sb.Draw(_glow, position, null, color * (num2 * 0.9f), 0f, origin, 1.7f, SpriteEffects.None, 0f);
			}
		}
		_sb.End();
	}

	private Texture2D BuildGlow()
	{
		Canvas canvas = new Canvas(64, 64);
		float num = 32f;
		for (int i = 0; i < 64; i++)
		{
			for (int j = 0; j < 64; j++)
			{
				float num2 = ((float)j + 0.5f - num) / num;
				float num3 = ((float)i + 0.5f - num) / num;
				float num4 = MathF.Sqrt(num2 * num2 + num3 * num3);
				if (!(num4 >= 1f))
				{
					float num5 = 1f - num4;
					num5 *= num5;
					canvas.Pixels[i * 64 + j] = new Color(255, 255, 255, (int)(num5 * 255f));
				}
			}
		}
		return canvas.ToTexture(GraphicsDevice);
	}

	/// <summary>
	/// Eski (piksel-shriftli) panellar uchun masshtab: telefonda ekran balandligi 720 virtual pikselga keltiriladi —
	/// shunda tugmalar barmoq uchun yetarli katta va joylashuv har qanday telefonda bir xil. PC'da 1 (katta monitorlarda 1.5/2).
	/// </summary>
	private float LegacyUiScale()
	{
		float h = GraphicsDevice.Viewport.Height;
		bool phone = _opt.Phone;
#if ANDROID
		phone = true;
#endif
		// Telefon: 540 virtual piksel balandlik — 1080p ekranda 2x (tugmalar barmoq uchun yetarli katta).
		if (phone) return MathHelper.Clamp(h / 540f, 1f, 4f);
		return h >= 1080f ? (float)Math.Floor(h / 720f * 2f) / 2f : 1f;
	}

	/// <summary>Boshlang'ich kamera yaqinligi: telefonda kattaroq (binolar barmoq bilan tanlashga qulay).</summary>
	private float StartZoom()
	{
		float s = LegacyUiScale();
		return MathHelper.Clamp(GameConfig.StartZoom * (s > 1f ? s * 0.8f : 1f), GameConfig.MinZoom, GameConfig.MaxZoom);
	}

	private Point LegacyUiSize(out float scale)
	{
		scale = LegacyUiScale();
		Viewport vp = GraphicsDevice.Viewport;
		return new Point((int)(vp.Width / scale), (int)(vp.Height / scale));
	}

	private void BeginLegacyUi(out Point size)
	{
		size = LegacyUiSize(out float s);
		_sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, Matrix.CreateScale(s, s, 1f));
	}

	private void DrawUI()
	{
		// Jang HUD (odam ichiga kirganda) va uyga kirish ko'rsatkichi — haqiqiy ekran o'lchamida
		Viewport viewport = GraphicsDevice.Viewport;
		_sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
		LegacyUiSize(out float hudScale);
		_combat?.DrawHud(_sb, _assets.Pixel, _font, _camera, viewport.Width, viewport.Height, Math.Max(1, (int)Math.Round(hudScale)));
		_interior?.DrawPrompt(_sb, _assets.Pixel, _font, _camera, viewport.Width, viewport.Height);
		_livestock?.DrawHerdInfo(_sb, _assets.Pixel, _font, _combat?.Possessed ?? (_state.Selected as Unit), viewport.Width);
		_sb.End();
		BeginLegacyUi(out Point v);
		_ui.Draw(_sb, v.X, v.Y);
		_sb.End();
	}

#if !ANDROID
	[System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
	[System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
#endif
	/// <summary>Login tugagach o'yin oynasini oldinga chiqaradi (minimallashgan bo'lsa tiklaydi). Windows uchun.</summary>
	private void BringGameWindowToFront()
	{
#if !ANDROID
		try
		{
			IntPtr h = Window.Handle;
			if (h != IntPtr.Zero) { ShowWindow(h, 9 /* SW_RESTORE */); SetForegroundWindow(h); }
		}
		catch { /* oynani oldinga chiqarish ixtiyoriy */ }
#endif
	}

	private float _rankTrainTimer;
	// Harbiy unvonlar: kazarmada mashq orqali askarlar XP oladi; kazarma darajasi eng yuqori unvonni cheklaydi.
	private void UpdateMilitaryRanks(float dt)
	{
		if (dt <= 0f || _units == null || _buildings == null || _state == null) return;
		int maxBarracks = 0;
		foreach (Building b in _buildings.Buildings)
			if (b.Type == BuildingType.Barracks && b.BuildProgress >= 1f && b.Level > maxBarracks) maxBarracks = b.Level;
		int maxRank = Math.Min(AndijonSaltanatlar.Military.MilitaryRank.Max, maxBarracks);

		_rankTrainTimer += dt;
		int gained = 0;
		if (_rankTrainTimer >= 2f) { gained = (int)(_rankTrainTimer / 2f) * 2; _rankTrainTimer %= 2f; }

		foreach (Unit u in _units.Units)
		{
			if (!u.CanRank || u.IsDown || u.Team != UnitTeam.Player) continue;
			if (gained > 0 && maxRank > 0) u.AddExperience(gained, maxRank);
			if (u.RankUpPending)
			{
				u.RankUpPending = false;
				_state.ShowMessage(u.Name + " unvoni oshdi: " + AndijonSaltanatlar.Military.MilitaryRank.Name(u.Rank) + "!");
			}
		}
	}

	/// <summary>Jang/g'alayon natijasida askarга XP beradi (tashqi tizimlar chaqiradi).</summary>
	public void AwardSoldierXp(Unit u, int xp)
	{
		if (u == null || !u.CanRank) return;
		int maxBarracks = 0;
		foreach (Building b in _buildings.Buildings)
			if (b.Type == BuildingType.Barracks && b.BuildProgress >= 1f && b.Level > maxBarracks) maxBarracks = b.Level;
		u.AddExperience(xp, Math.Min(AndijonSaltanatlar.Military.MilitaryRank.Max, maxBarracks));
	}

	private void DrawLoginScreen()
	{
		Viewport viewport = GraphicsDevice.Viewport;
		_sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
		if (_worldSelect != null) _worldSelect.Draw(_sb, viewport.Width, viewport.Height, _time);
		else _loginScreen.Draw(_sb, viewport.Width, viewport.Height, _time);
		_sb.End();
	}

	// Fonda GitHub Releases'dan yangilashni tekshiradi; yangi versiya bo'lsa _pendingUpdate ga qo'yadi.
	private void StartUpdateCheck()
	{
		try
		{
			System.Threading.Tasks.Task.Run(async () =>
			{
				var info = await AndijonSaltanatlar.Online.UpdateChecker.CheckAsync();
				if (info != null && info.Available) _pendingUpdate = info; // asosiy tsikl buni o'qiydi
			});
		}
		catch { /* yangilash tekshiruvi hech qachon o'yinni buzmasin */ }
	}

	private void DrawUpdateOverlay()
	{
		if (_updateScreen == null) return;
		Viewport viewport = GraphicsDevice.Viewport;
		_sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
		_updateScreen.Draw(_sb, viewport.Width, viewport.Height, _time);
		_sb.End();
	}

	private void DrawStrategic()
	{
		GraphicsDevice.Clear(new Color(18, 22, 30));
		BeginLegacyUi(out Point v);
		_strategic.Draw(_sb, v.X, v.Y);
		_sb.End();
	}

	private void DrawCiv()
	{
		GraphicsDevice.Clear(new Color(16, 20, 34));
		BeginLegacyUi(out Point v);
		_civ.Draw(_sb, v.X, v.Y);
		_sb.End();
	}

	private void DrawQuest()
	{
		GraphicsDevice.Clear(new Color(18, 20, 28));
		BeginLegacyUi(out Point v);
		_quest.Draw(_sb, v.X, v.Y);
		_sb.End();
	}

	private void DrawStoryOverlay()
	{
		if (_storyOverlay != null)
		{
			BeginLegacyUi(out Point v);
			_storyOverlay.RightInset = _state.Selected != null ? 248 : 0;
			_storyOverlay.HideTracker = true; // yonboshdagi missiya kartasi olib tashlandi
			_storyOverlay.Draw(_sb, v.X, v.Y);
			_sb.End();
		}
	}

	private void DrawVizierPopup()
	{
		if (_vizierPopup == null || !_vizierPopup.Active) return;
		BeginLegacyUi(out Point v);
		_vizierPopup.Draw(_sb, v.X, v.Y);
		_sb.End();
	}

	private int _findIndex = -1;

	/// <summary>TOPISH: keyingi binoga kamerani markazlaydi. Bino tanlangan bo'lsa — shu turdagilarni aylanadi.</summary>
	private void FindNextBuilding()
	{
		var all = _buildings.Buildings;
		if (all == null || all.Count == 0) { _state.ShowMessage("Hali bino yo'q", true); return; }
		BuildingType? filter = _state.Selected is Building sel ? sel.Type : (BuildingType?)null;
		var list = new System.Collections.Generic.List<Building>();
		foreach (Building b in all) if (filter == null || b.Type == filter) list.Add(b);
		if (list.Count == 0) { _state.ShowMessage("Bunday bino topilmadi", true); return; }
		list.Sort((a, b) => (a.Position.Y * 1000 + a.Position.X).CompareTo(b.Position.Y * 1000 + b.Position.X));
		_findIndex = (_findIndex + 1) % list.Count;
		Building t = list[_findIndex];
		Vector2 center = IsoMath.TileToWorld(t.Position.X + (t.Size.X - 1) / 2f, t.Position.Y + (t.Size.Y - 1) / 2f);
		_camera.Jump(center, Math.Max(_camera.Zoom, 1.2f));
		_state.Select(t);
		_state.ShowMessage(t.Name + "  (" + (_findIndex + 1) + "/" + list.Count + ")" + (filter != null ? " — shu turdagilar" : ""));
	}

	/// <summary>BIRLASHTIRISH: tanlangan dala/bog'ni yonidagi bir xil turdagilari bilan birlashtiradi —
	/// har biri qo'shilgach survivor darajasi (ko'rinishi va hosili) oshadi, yutilganlarining joyi bo'shaydi.</summary>
	private void MergeSelectedBuilding()
	{
		if (!(_state.Selected is Building s) || (s.Type != BuildingType.Farm && s.Type != BuildingType.Garden))
		{
			_state.ShowMessage("Faqat dala yoki bog'ni birlashtirish mumkin", true);
			return;
		}
		string word = s.Type == BuildingType.Garden ? "bog'" : "dala";
		if (s.Level >= AssetManager.MaxArtLevel) { _state.ShowMessage(s.Name + " allaqachon eng katta", true); return; }

		var neighbors = new System.Collections.Generic.List<Building>();
		foreach (Building b in _buildings.Buildings)
		{
			if (b == s || b.Type != s.Type) continue;
			var ra = new Rectangle(s.Position.X - 1, s.Position.Y - 1, s.Size.X + 2, s.Size.Y + 2);
			var rb = new Rectangle(b.Position.X, b.Position.Y, b.Size.X, b.Size.Y);
			if (ra.Intersects(rb)) neighbors.Add(b);
		}
		if (neighbors.Count == 0) { _state.ShowMessage("Yonida birlashtirish uchun boshqa " + word + " yo'q", true); return; }

		int merged = 0;
		foreach (Building n in neighbors)
		{
			if (s.Level >= AssetManager.MaxArtLevel) break;
			_buildings.Remove(n);
			s.Level = Math.Min(AssetManager.MaxArtLevel, s.Level + 1);
			merged++;
		}
		_state.Select(s);
		_state.ShowMessage(merged + " ta " + word + " birlashdi — " + s.Name + " endi " + s.Level + "-daraja (kattaroq, ko'proq hosil)");
	}

	private void DrawOnline()
	{
		Viewport viewport = GraphicsDevice.Viewport;
		// LinearClamp: TTF shrift atlasi silliq (xira emas) chiziladi; piksel-art dunyo alohida PointClamp bilan.
		_sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
		_onlinePanel.DrawLauncher(_sb, viewport.Width);
		_onlinePanel.Draw(_sb, viewport.Width, viewport.Height, _time);
		_sb.End();
	}

	private void DrawMenu()
	{
		Viewport viewport = GraphicsDevice.Viewport;
		_sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
		_mainMenu.Draw(_sb, viewport.Width, viewport.Height);
		_sb.End();
	}

	private void DrawWeather()
	{
		Viewport viewport = GraphicsDevice.Viewport;
		_sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
		EnvironmentRenderer.Draw(_sb, _assets.Pixel, _state.Weather.Current, viewport.Width, viewport.Height, _time);
		SeasonVisuals.DrawParticles(_sb, _assets.Pixel, viewport.Width, viewport.Height, _time, GraphicsQualityManager.ParticleCount, _dayNight.NightAmount);
		_sb.End();
	}
}
