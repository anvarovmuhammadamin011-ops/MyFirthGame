using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Advisor;
using AndijonSaltanatlar.AdvancedEconomy;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Campaigns;
using AndijonSaltanatlar.Civilization;
using AndijonSaltanatlar.Defense;
using AndijonSaltanatlar.Economy;
using AndijonSaltanatlar.Environment;
using AndijonSaltanatlar.Government;
using AndijonSaltanatlar.Kingdoms;
using AndijonSaltanatlar.LandExpansion;
using AndijonSaltanatlar.Military;
using AndijonSaltanatlar.Population;
using AndijonSaltanatlar.Progression;
using AndijonSaltanatlar.Story;
using AndijonSaltanatlar.Technology;
using AndijonSaltanatlar.World;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar.Core;

public sealed class GameStateManager
{
	private readonly ResourceManager _resources = new ResourceManager(500, 200);

	private readonly EconomyManager _economy;

	public GameMode Mode { get; private set; } = GameMode.Playing;

	public bool Paused { get; set; }

	public bool ShowHelp { get; set; }

	public int Speed { get; private set; } = 1;

	public GameClock Clock { get; } = new GameClock();

	public ResourceManager Resources => _resources;

	public EconomyManager Economy => _economy;

	public GovernmentPolicy Policy { get; } = new GovernmentPolicy();

	public PopulationManager Pop { get; } = new PopulationManager();

	public PetitionManager Petitions { get; } = new PetitionManager();

	public WorldSimulation World { get; } = new WorldSimulation();

	public ArmyManager Military { get; } = new ArmyManager();

	public CivilizationManager Civ { get; } = new CivilizationManager();

	public CityLandExpansionManager Land { get; } = new CityLandExpansionManager();

	/// <summary>Aholini uy sig'imigacha to'ldiradi (to'liq kuchaytirish). Qo'shilganlar soni.</summary>
	public int FillPopulation(AndijonSaltanatlar.Buildings.BuildingManager buildings)
	{
		int n = Pop.FillToCapacity(buildings);
		Population = Pop.Count;
		return n;
	}

	/// <summary>Yer ochildi — bulutga darhol saqlash kerak (Game1 tekshiradi va tushiradi).</summary>
	public bool LandChanged { get; set; }

	public AdvisorManager Advisor { get; } = new AdvisorManager();

	public WeatherManager Weather { get; } = new WeatherManager();

	/// <summary>Shahar hayoti (mahallalar, murojaatlar, voqealar) — PROMPT 17/18 yagona egasi.</summary>
	public CityLifeManager Life { get; } = new CityLifeManager();

	/// <summary>
	/// Joriy fasl: o'yin oyidan (har 3 oy — bir fasl). SeasonShift — faslni qo'lda oldinga surish
	/// (N tugmasi / --season sinov parametri); saqlanmaydi.
	/// </summary>
	public Season Season => (Season)((((int)SeasonManager.Of(Clock.Month) + SeasonShift) % 4 + 4) % 4);

	public int SeasonShift { get; set; }

	public StoryModeManager Story { get; private set; }

	public bool IsStoryMode { get; set; }

	public CampaignState PendingCampaignState { get; set; }

	public int Coins
	{
		get
		{
			return _resources.Get(ResourceType.Coin);
		}
		set
		{
			_resources.Set(ResourceType.Coin, value);
		}
	}

	public int Food
	{
		get
		{
			return _resources.Get(ResourceType.Food);
		}
		set
		{
			_resources.Set(ResourceType.Food, value);
		}
	}

	public int Population { get; private set; }

	public int Day => Clock.Day;

	public BuildingType SelectedBuildType { get; private set; }

	public object Selected { get; private set; }

	public string Message { get; private set; }

	public float MessageTimer { get; private set; }

	public bool MessageIsError { get; private set; }

	public GameStateManager()
	{
		_economy = new EconomyManager(_resources);
	}

	public void EnterBuildMode(BuildingType type)
	{
		Mode = GameMode.BuildMode;
		SelectedBuildType = type;
		Selected = null;
	}

	public void CancelBuild()
	{
		Mode = GameMode.Playing;
		MovingBuilding = null;
	}

	/// <summary>Ko'chirilayotgan bino (MoveMode).</summary>
	public Building MovingBuilding { get; private set; }

	/// <summary>Bino ko'chirilishi mumkinmi (saroy shahar markazi — ko'chirilmaydi).</summary>
	public static bool CanRelocate(Building b) => b != null && b.Type != BuildingType.MainHall;

	public void BeginMove(Building b)
	{
		if (!CanRelocate(b)) { ShowMessage("Saroy shahar markazi — uni ko'chirib bo'lmaydi", isError: true); return; }
		MovingBuilding = b;
		Mode = GameMode.MoveMode;
		Selected = null;
		ShowMessage(b.Name + ": yangi joyni tanlang (yashil — mumkin). O'ng tugma / Esc — bekor");
	}

	public void Select(object obj)
	{
		Selected = obj;
	}

	public void ShowMessage(string text, bool isError = false)
	{
		Message = text;
		MessageIsError = isError;
		MessageTimer = 2.5f;
	}

	// Vazir ekran xabari: missiya yoki muhim voqea yuz berganda Donishmand Vazir ekranda paydo bo'lib aytadi.
	private readonly System.Collections.Generic.Queue<(string Title, string Body)> _vizierQueue = new System.Collections.Generic.Queue<(string, string)>();

	/// <summary>Vazirni ekranga chaqiradi (Game1 navbatni VizierPopup'ga uzatadi). Takroriy bir xil xabar o'tkazilmaydi.</summary>
	public void ShowVizier(string title, string body)
	{
		if (string.IsNullOrWhiteSpace(body)) return;
		if (_vizierQueue.Count > 0)
		{
			var last = _vizierQueue.ToArray()[_vizierQueue.Count - 1];
			if (last.Body == body) return;
		}
		_vizierQueue.Enqueue((string.IsNullOrWhiteSpace(title) ? "Donishmand Vazir" : title, body));
	}

	/// <summary>Navbatdagi Vazir xabarini oladi (Game1 har kadr chaqiradi).</summary>
	public bool TryDequeueVizier(out string title, out string body)
	{
		if (_vizierQueue.Count > 0) { var m = _vizierQueue.Dequeue(); title = m.Title; body = m.Body; return true; }
		title = null; body = null; return false;
	}

	public bool CanAfford(int cost)
	{
		return _resources.CanAfford(ResourceType.Coin, cost);
	}

	public void CycleSpeed()
	{
		Speed = ((Speed >= 4) ? 1 : (Speed * 2));
	}

	public void SetSpeed(int s)
	{
		Speed = ((s != 2 && s != 4) ? 1 : s);
	}

	public void ResolvePetition(int choice)
	{
		Petitions.Resolve(choice, Pop, _resources, Policy);
	}

	public void InitPopulation(BuildingManager buildings)
	{
		Pop.Initialize(buildings);
		Military.Initialize(World.Kingdoms.PlayerKingdomId, 1);
		List<int> list = new List<int>();
		foreach (Kingdom item in World.Kingdoms.AIKingdoms())
		{
			list.Add(item.Id);
		}
		Civ.Initialize(World.Kingdoms.PlayerKingdomId, list);
		Land.Initialize();
		Land.RefreshStates(Pop.Count);
		Population = Pop.Count;
		Advisor.Analyze(this);
		Story = new StoryModeManager();
		Story.Initialize(this, PendingCampaignState, IsStoryMode);
		PendingCampaignState = null;
	}

	public bool IsLandBuildable(Point pos, int size)
	{
		for (int i = pos.Y; i < pos.Y + size; i++)
		{
			for (int j = pos.X; j < pos.X + size; j++)
			{
				if (!Land.IsBuildable(j, i))
				{
					return false;
				}
			}
		}
		return true;
	}

	public void UnlockSelectedParcel()
	{
		if (Selected is LandParcel { State: not LandParcelState.Open } landParcel)
		{
			if (!Land.CanUnlockRing(landParcel))
			{
				ShowMessage("Avval " + (landParcel.Ring - 1) + "-halqaning 4 tomonini oching", isError: true);
				return;
			}
			if (Population < landParcel.RequiredPopulation)
			{
				ShowMessage("Aholi yetarli emas (kerak: " + landParcel.RequiredPopulation + ")", isError: true);
				return;
			}
			if (!_resources.CanAfford(ResourceType.Coin, landParcel.UnlockCost))
			{
				ShowMessage("Tanga yetarli emas (" + landParcel.UnlockCost + ")", isError: true);
				return;
			}
			Coins -= landParcel.UnlockCost;
			Land.Unlock(landParcel);
			Land.RefreshStates(Population);
			if (Land.LastCompletedRing > 0 && Land.LastCompletedRing < Land.RingCount)
				ShowMessage(Land.LastCompletedRing + "-HALQA TO'LIQ OCHILDI! " + (Land.LastCompletedRing + 1) + "-halqada 4 ta yangi yer paydo bo'ldi");
			else
				ShowMessage("YANGI HUDUD OCHILDI: " + landParcel.NameUz);
			LandChanged = true;
			Story?.NotifyLandUnlocked();
			Advisor.Analyze(this);
		}
	}

	public void DoAdvisorAction(AdvisorMessage m)
	{
		if (m != null)
		{
			switch (m.ActionId)
			{
			case 1:
				EnterBuildMode(BuildingType.House);
				break;
			case 2:
				EnterBuildMode(BuildingType.Garden);
				break;
			case 3:
				EnterBuildMode(BuildingType.Market);
				break;
			case 4:
				ShowMessage("Xaritada yopiq (oltin) hududni tanlab, 'YERNI OCHISH' bosing");
				break;
			case 5:
				Policy.AdjustPopulation(-5);
				ShowMessage("Aholi solig'i kamaytirildi");
				break;
			}
		}
	}

	public bool StartResearch(string techId)
	{
		if (Civ.Player.StartResearch(techId))
		{
			ShowMessage("Tadqiqot boshlandi: " + (TechnologyCatalog.Get(techId)?.NameUz ?? techId));
			return true;
		}
		ShowMessage("Bu texnologiyani hozir o'rganib bo'lmaydi", isError: true);
		return false;
	}

	public bool IsBuildingUnlocked(BuildingType type)
	{
		return Civ.Player.IsBuildingUnlocked(BuildingCatalog.RequiredTech(type));
	}

	public void UpgradeSelected()
	{
		if (Selected is Building building && BuildingUpgradeSystem.CanUpgrade(building))
		{
			int num = BuildingUpgradeSystem.UpgradeCost(building);
			if (!_resources.CanAfford(ResourceType.Coin, num))
			{
				ShowMessage("Daraja uchun tanga yetarli emas", isError: true);
				return;
			}
			Coins -= num;
			BuildingUpgradeSystem.Apply(building);
			ShowMessage(building.Name + " darajasi oshdi (" + building.Level + ")");
			Story?.NotifyBuildingUpgraded(building);
		}
	}

	/// <summary>Dehqonchilik ombori va fermerlar so'rovlari.</summary>
	public FarmingManager Farming { get; } = new FarmingManager();

	public bool RecruitUnit(UnitType type) => RecruitUnits(type, 1, null);

	/// <summary>Navbatdagi (tayyorlanayotgan) jangchilar soni chegarasi — bir bosishda ko'p xarid xatosidan himoya.</summary>
	public const int MaxRecruitQueue = 60;

	/// <summary>
	/// Jangchi turi ochiqmi: tarixiy davr (o'rganilgan texnologiyalar soni) va bino talabi. buildings null bo'lsa — bino
	/// tekshirilmaydi; researched berilmasa — davr tekshirilmaydi.
	/// </summary>
	public static bool UnitRequirementMet(UnitDef d, BuildingManager buildings, int researched = int.MaxValue)
	{
		if (AndijonSaltanatlar.Progression.EraManager.EraIndex(researched) < d.MinEra) return false;
		if (d.RequiredBuilding == null || buildings == null) return true;
		foreach (Building b in buildings.Buildings)
			if (b.Type == d.RequiredBuilding.Value && b.BuildProgress >= 1f && b.Level >= d.RequiredLevel) return true;
		return false;
	}

	/// <summary>
	/// Jangchilarni sotib olish (yollash navbatiga qo'yish). Hammasi bir tranzaksiyada tekshiriladi:
	/// bino talabi, tanga (narx × soni) va navbat chegarasi — biror shart bajarilmasa hech narsa yechilmaydi.
	/// </summary>
	public bool RecruitUnits(UnitType type, int count, BuildingManager buildings)
	{
		UnitDef unitDef = UnitCatalog.Get(type);
		count = Math.Max(1, count);
		int researched = Civ.Player.Researched.Count;
		if (!UnitRequirementMet(unitDef, buildings, researched))
		{
			ShowMessage(unitDef.NameUz + " uchun kerak: " + UnitCatalog.LockText(unitDef, researched), isError: true);
			return false;
		}
		if (Military.QueueCount + count > MaxRecruitQueue)
		{
			ShowMessage("Navbat to'la (" + Military.QueueCount + " / " + MaxRecruitQueue + ")", isError: true);
			return false;
		}
		int total = unitDef.TrainingCost * count;
		if (!_resources.CanAfford(ResourceType.Coin, total))
		{
			ShowMessage("Tanga yetarli emas: " + total + " kerak", isError: true);
			return false;
		}
		Coins -= total;
		for (int i = 0; i < count; i++) Military.Enqueue(type);
		ShowMessage(count + " ta " + unitDef.NameUz + " tayyorlashga berildi (-" + total + ")");
		return true;
	}

	public void TrainArmy()
	{
		if (!_resources.CanAfford(ResourceType.Coin, 20))
		{
			ShowMessage("Mashg'ulot uchun tanga yetarli emas", isError: true);
			return;
		}
		Coins -= 20;
		int num = Military.TrainAll(60);
		ShowMessage("Mashg'ulot o'tkazildi" + ((num > 0) ? (" (" + num + " daraja oshdi)") : ""));
	}

	// ---------- Mashg'ulot maydoni (vaqtli sessiya) ----------
	/// <summary>Mashg'ulot sessiyasi davomiyligi (o'yin soniyalari, tezlik bilan ko'payadi).</summary>
	public const float TrainingSessionLength = 30f;
	/// <summary>Qolgan vaqt; 0 — mashg'ulot yo'q.</summary>
	public float TrainingLeft { get; private set; }
	public bool TrainingActive => TrainingLeft > 0f;
	public float TrainingProgress => TrainingActive ? 1f - TrainingLeft / TrainingSessionLength : 0f;
	public int TrainingCost => 20 + 2 * Military.TotalUnits;

	/// <summary>Mashg'ulot maydoni bormi (Qo'shinxona yoki Jang maydoni).</summary>
	public static bool HasTrainingGround(BuildingManager buildings)
	{
		if (buildings == null) return true;
		foreach (Building b in buildings.Buildings)
			if ((b.Type == BuildingType.Barracks || b.Type == BuildingType.GladiatorArena) && b.BuildProgress >= 1f) return true;
		return false;
	}

	public bool StartTrainingSession(BuildingManager buildings)
	{
		if (TrainingActive) { ShowMessage("Mashg'ulot allaqachon davom etmoqda", isError: true); return false; }
		if (Military.TotalUnits == 0) { ShowMessage("Avval jangchi sotib oling", isError: true); return false; }
		if (!HasTrainingGround(buildings)) { ShowMessage("Mashg'ulot uchun Qo'shinxona yoki Jang maydoni kerak", isError: true); return false; }
		int cost = TrainingCost;
		if (!_resources.CanAfford(ResourceType.Coin, cost)) { ShowMessage("Mashg'ulot uchun " + cost + " tanga kerak", isError: true); return false; }
		Coins -= cost;
		TrainingLeft = TrainingSessionLength;
		ShowMessage("Mashg'ulot boshlandi (-" + cost + ")");
		return true;
	}

	private void UpdateTraining(float gameDt)
	{
		if (!TrainingActive) return;
		TrainingLeft -= gameDt;
		if (TrainingLeft > 0f) return;
		TrainingLeft = 0f;
		int up = Military.TrainAll(60);
		ShowMessage("Mashg'ulot yakunlandi" + (up > 0 ? ": " + up + " jangchi darajasi oshdi" : ": tajriba oshdi"));
	}

	public void Update(float dt, BuildingManager buildings)
	{
		if (MessageTimer > 0f)
		{
			MessageTimer -= dt;
		}
		Population = (Pop.Initialized ? Pop.Count : buildings.TotalPopulation);
		_resources.Set(ResourceType.Population, Population);
		float num = (Paused ? 0f : (dt * (float)Speed));
		UpdateTraining(num);
		if (num <= 0f)
		{
			return;
		}
		int day = Clock.Day;
		int month = Clock.Month;
		int hour = Clock.Hour;
		Clock.Update(num);
		// Jangchilar har o'yin SOATida tayyorlanadi (kun uzun — 60 daqiqa; TrainingDays = soat)
		if (Clock.Hour != hour || Clock.Day != day) Military.DailyTick();
		foreach (Building building in buildings.Buildings)
		{
			if (building.BuildProgress < 1f)
			{
				// Qurilish o'yin soatiga bog'liq (real sekundga emas): kun uzaysa qurilish ham
				// mantiqiy ravishda uzoqroq davom etadi. 120 daqiqalik standart kunda avvalgi
				// tezlik saqlanadi (soatiga 120 = 0.4/sek, 7200 sek/kunda).
				building.BuildProgress = Math.Min(1f, building.BuildProgress + GameClock.GameHours(num, Clock.SecondsPerDay) * 120f);
				if (building.BuildProgress >= 1f)
				{
					ShowMessage(building.Name + " qurilishi yakunlandi");
					Story?.NotifyBuildingConstructed(building);
				}
			}
		}
		if (!Pop.Initialized || Clock.Day == day)
		{
			return;
		}
		Weather.Update(Clock.Day, Season);
		Pop.AssignJobs(buildings);
		bool hasFarmers = Pop.CountProfession(CitizenProfession.Dehqon) > 0;
		Farming.BeginDay();
		int farmCount = 0;
		foreach (Building building in buildings.Buildings)
		{
			if (!(building is Farm farm)) continue;
			farmCount++;
			if (farm.Tended) continue;   // ekish, o'rish, bozorga olib borish — dalaning o'z dehqoni (FarmWorkController)
			// Ekish urug' sarflaydi: urug' tugasa dala bo'sh qoladi (fermerlar sotib olishni so'raydi)
			if (hasFarmers && farm.CropStage == FarmCropStage.Empty && !farm.IsPlayerControlled && !Farming.TryConsumeSeed()) continue;
			int harvest = farm.WorkAutonomously(hasFarmers);
			if (harvest <= 0) continue;
			int bonus = Farming.ApplyYieldBonus(harvest) - harvest;
			if (buildings.Count(BuildingType.Market) > 0) Coins += (farm.SellStoredProduce() + bonus) * 2;
			else Food += harvest + bonus;
		}
		Farming.EndDay(Clock.Day, farmCount, Season);
		CivilizationModifiers civilizationModifiers = Civ.PlayerModifiers();
		float num2 = SeasonManager.FoodModifier(Season) + Weather.ProductionModifier();
		_economy.DailyTick(buildings, Pop, Policy, civilizationModifiers.Get(TechEffectType.FoodProductionBonus) + num2, civilizationModifiers.Get(TechEffectType.TradeIncomeBonus) + Weather.ProductionModifier() * 0.5f, civilizationModifiers.Get(TechEffectType.TaxEfficiencyBonus));
		Pop.DailyTick(buildings, _resources, Policy, civilizationModifiers.Get(TechEffectType.PopulationHealthBonus), civilizationModifiers.Get(TechEffectType.EducationBonus), civilizationModifiers.Get(TechEffectType.CityStabilityBonus));
		Life.DayTick(this, buildings);
		string text = Civ.DailyTick(buildings, Pop.AvgEducation);
		if (text != null)
		{
			ShowMessage("Texnologiya ochildi: " + (TechnologyCatalog.Get(text)?.NameUz ?? text));
			Story?.NotifyTechnologyUnlocked(text);
		}
		Petitions.Update(Clock.Day, Pop, _resources, Policy);
		if (Clock.Month != month)
		{
			Pop.MonthlyGrowth(buildings, _resources);
		}
		Population = Pop.Count;
		Military.MaxConcurrent = Math.Max(2, buildings.Count(BuildingType.Barracks) * 2);
		// Havo hissi (qish sovug'i, yoz issig'i) aholining kayfiyatiga ta'sir qiladi
		Pop.AdjustHappiness(SeasonVisuals.HappinessDelta(SeasonVisuals.ComfortOf(Season, Weather.Current, 0.5f)) + Weather.MoodModifier() * 6f);
		int num3 = buildings.Count(BuildingType.Barracks) * 6;
		if (num3 > 0)
		{
			Military.TrainAll(num3);
		}
		Coins -= Military.TotalUpkeep;
		int cityDefense = CityDefenseManager.Compute(buildings, Military.TotalStrength);
		World.SyncPlayer(Coins, Population, Military.TotalStrength, cityDefense);
		Coins += World.StrategicTick(Clock.Day);
		foreach (Kingdom item in World.Kingdoms.AIKingdoms())
		{
			if (Civ.AITick(item.Id, item.EconomicStrength))
			{
				item.TechnologyLevel = 1 + Civ.AITechCount(item.Id);
				World.AddEvent(item.RulerName + " yangi ilmiy yutuqqa erishdi.");
			}
		}
		Land.RefreshStates(Population);
		Advisor.Analyze(this);
		Story?.OnDayTick();
	}
}
