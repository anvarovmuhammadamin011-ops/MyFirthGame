using System.Globalization;
using Microsoft.Xna.Framework;

namespace AndijonSaltanatlar;

public sealed class LaunchOptions
{
	public string ScreenshotPath;

	public string ExportSpritesDir;

	public Vector2? CenterTile;

	public float? Zoom;

	public string BuildType;

	public Point? HoverTile;

	public Point? SelectTile;

	public float? Time;

	public string Tab;

	public int? SimDays;

	public int? PopTax;

	/// <summary>Sinov: faslni majburan tanlash (bahor | yoz | kuz | qish).</summary>
	public string SeasonName;

	public int? Food;

	public bool Map;

	public int? AttackRegion;

	public bool Civ;

	public int? Techs;

	public string ResearchId;

	public bool Menu;

	public bool Advisor;

	public bool Governor;

	public string Plan;

	public string WeatherName;

	public bool ShowLevels;

	public bool Story;

	public bool QuestJournal;

	public string SmokeLog;

	// --- PROMPT 19A: online sinovlari uchun ---
	public bool Online;
	public string OnlineServer;
	public string OnlineUser;
	public string OnlinePass;
	public bool OnlineRegister;
	public string OnlineTab;

	/// <summary>Google kirish ekranini majburan ko'rsatish (skrinshot/sinov rejimida ham).</summary>
	public bool Login;

	/// <summary>Sinov: brauzer ochilmaydi, Google login manzili shu faylga yoziladi.</summary>
	public string LoginUrlFile;

	/// <summary>Server manzili (kirish ekrani uchun standart qiymat).</summary>
	public string Server;

	/// <summary>Sinov: kirish + bulutdan yuklash tugagach skrinshot olinsin (kirish kutiladi).</summary>
	public bool ScreenshotAfterLogin;

	/// <summary>Sinov: kirgandan keyin shu uchastkani ochib, bulutga saqlash (yer kengaytirish e2e).</summary>
	public int? UnlockParcelAfterLogin;

	/// <summary>Ma'lumotlar papkasi (token, sozlamalar) — sinovlar haqiqiy hisobga tegmasligi uchun.</summary>
	public string DataDir;

	/// <summary>PC'da telefon interfeysini sinash (katta tugmalar, telefon UI masshtabi).</summary>
	public bool Phone;
	/// <summary>Skrinshotni shuncha kadrdan keyin olish (server javobini kutish uchun).</summary>
	public int? ScreenshotFrames;

	/// <summary>Oyna o'lchami (masalan 854x480 — telefon ekranini sinash uchun).</summary>
	public Point? WindowSize;

	/// <summary>Bir martalik: kirgandan keyin shaharni to'liq kuchaytirish (binolar max, texnologiyalar, yer, resurslar).</summary>
	public bool FullUpgrade;

	/// <summary>Sinov: jang maydoni (mashq jangi) darhol ochiladi.</summary>
	public bool Arena;

	/// <summary>Sinov: shu hududga qamal darhol boshlanadi (-1 — birinchi dushman hududi).</summary>
	public int? SiegeRegion;

	/// <summary>Sinov: qamalda dushman shohi darhol yiqiladi (askarlar tomonga o'tadi).</summary>
	public bool SiegeShahDown;

	/// <summary>Sinov: qamalda hech kimning ichiga kirilmaydi (qo'shin o'zi jang qiladi).</summary>
	public bool SiegeWatch;

	/// <summary>Sinov: qamal oldidan qo'shinga shuncha aralash jangchi qo'shiladi.</summary>
	public int? TestArmy;

	/// <summary>Sinov: kirish ekrani bosqichi (login, profile, welcome) — skrinshot uchun.</summary>
	public string AuthStage;

	/// <summary>Sinov: fasldan qat'i nazar odamlar cho'miladi.</summary>
	public bool Swim;

	/// <summary>Sinov: barcha dalalarga ekin ekib, o'sishni shu ulushga (0..1) yetkazadi va kamerani eng yaqin dalaga qaratadi.</summary>
	public float? FarmDemo;

	/// <summary>Sinov: saltanat xaritasida shu hududga yurish boshlanadi.</summary>
	public int? MarchRegion;

	/// <summary>Sinov: urug' tugagan, fermer murojaati oynasi ochiq.</summary>
	public bool FarmRequest;

	/// <summary>Sinov: uy ichida holat (sit, eat, lie) — --enterhouse bilan.</summary>
	public string HousePose;

	/// <summary>Sinov: ko'rinadigan fasl (0 bahor, 1 yoz, 2 kuz, 3 qish) — saqlanmaydi.</summary>
	public int? Season;

	/// <summary>Sinov: darhol bosqin (0 shimol, 1 janub, 2 g'arb, 3 sharq).</summary>
	public int? RaidGate;

	/// <summary>Sinov: shu tile'ga eng yaqin jangchi ichiga kirish.</summary>
	public Vector2? PossessNear;

	/// <summary>Sinov: K-uyga (ro'yxat tartibida) kirish — ichki ko'rinishni tekshirish uchun.</summary>
	public int? EnterHouse;

	public static LaunchOptions Parse(string[] args)
	{
		LaunchOptions launchOptions = new LaunchOptions();
		for (int i = 0; i < args.Length; i++)
		{
			string text = ((i + 1 < args.Length) ? args[i + 1] : null);
			switch (args[i])
			{
			case "--screenshot":
				launchOptions.ScreenshotPath = text;
				i++;
				break;
			case "--export-sprites":
				launchOptions.ExportSpritesDir = text;
				i++;
				break;
			case "--zoom":
				launchOptions.Zoom = float.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--time":
				launchOptions.Time = float.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--tab":
				launchOptions.Tab = text;
				i++;
				break;
			case "--simdays":
				launchOptions.SimDays = int.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--poptax":
				launchOptions.PopTax = int.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--real-season":
				launchOptions.SeasonName = text;
				i++;
				break;
			case "--food":
				launchOptions.Food = int.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--map":
				launchOptions.Map = true;
				break;
			case "--attack":
				launchOptions.AttackRegion = int.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--civ":
				launchOptions.Civ = true;
				break;
			case "--techs":
				launchOptions.Techs = int.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--research":
				launchOptions.ResearchId = text;
				i++;
				break;
			case "--menu":
				launchOptions.Menu = true;
				break;
			case "--advisor":
				launchOptions.Advisor = true;
				break;
			case "--governor":
				launchOptions.Governor = true;
				break;
			case "--plan":
				launchOptions.Plan = text;
				i++;
				break;
			case "--weather":
				launchOptions.WeatherName = text;
				i++;
				break;
			case "--showlevels":
				launchOptions.ShowLevels = true;
				break;
			case "--story":
				launchOptions.Story = true;
				break;
			case "--quest":
				launchOptions.QuestJournal = true;
				break;
			case "--smoke":
				launchOptions.SmokeLog = text ?? "smoke_result.txt";
				if (text != null)
				{
					i++;
				}
				break;
			case "--online":
				launchOptions.Online = true;
				break;
			case "--login":
				launchOptions.Login = true;
				break;
			case "--login-url-file":
				launchOptions.LoginUrlFile = text;
				launchOptions.Login = true;
				i++;
				break;
			case "--data-dir":
				launchOptions.DataDir = text;
				i++;
				break;
			case "--phone":
				launchOptions.Phone = true;
				break;
			case "--server":
				launchOptions.Server = text;
				i++;
				break;
			case "--shot-after-login":
				launchOptions.ScreenshotAfterLogin = true;
				break;
			case "--unlock-parcel":
				launchOptions.UnlockParcelAfterLogin = int.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--online-server":
				launchOptions.OnlineServer = text;
				launchOptions.Online = true;
				i++;
				break;
			case "--online-user":
				launchOptions.OnlineUser = text;
				i++;
				break;
			case "--online-pass":
				launchOptions.OnlinePass = text;
				i++;
				break;
			case "--online-register":
				launchOptions.OnlineRegister = true;
				break;
			case "--online-tab":
				launchOptions.OnlineTab = text;
				i++;
				break;
			case "--size":
			{
				string[] wh = (text ?? "1280x720").Split('x');
				launchOptions.WindowSize = new Point(int.Parse(wh[0], CultureInfo.InvariantCulture), int.Parse(wh[1], CultureInfo.InvariantCulture));
				i++;
				break;
			}
			case "--frames":
				launchOptions.ScreenshotFrames = int.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--season":
				launchOptions.Season = int.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--pose":
				launchOptions.HousePose = text;
				i++;
				break;
			case "--farm-request":
				launchOptions.FarmRequest = true;
				break;
			case "--march":
				launchOptions.MarchRegion = int.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--farm-demo":
				launchOptions.FarmDemo = float.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--swim":
				launchOptions.Swim = true;
				break;
			case "--auth-stage":
				launchOptions.AuthStage = text;
				i++;
				break;
			case "--arena":
				launchOptions.Arena = true;
				break;
			case "--siege":
				launchOptions.SiegeRegion = int.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--siege-watch":
				launchOptions.SiegeWatch = true;
				break;
			case "--army":
				launchOptions.TestArmy = int.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--siege-shah-down":
				launchOptions.SiegeShahDown = true;
				break;
			case "--full-upgrade":
				launchOptions.FullUpgrade = true;
				break;
			case "--battle-demo":
				AndijonSaltanatlar.UI.BattleScene.ReplayLastRound = true;
				AndijonSaltanatlar.UI.OnlinePanel.DemoBattle = AndijonSaltanatlar.UI.OnlinePanel.BuildDemoBattle();
				break;
			case "--replay-round":
				AndijonSaltanatlar.UI.BattleScene.ReplayLastRound = true;
				break;
			case "--raid":
				launchOptions.RaidGate = int.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--enterhouse":
				launchOptions.EnterHouse = int.Parse(text, CultureInfo.InvariantCulture);
				i++;
				break;
			case "--possess":
				launchOptions.PossessNear = ParsePair(text);
				i++;
				break;
			case "--build":
				launchOptions.BuildType = text;
				i++;
				break;
			case "--center":
			{
				Vector2 value = ParsePair(text);
				launchOptions.CenterTile = value;
				i++;
				break;
			}
			case "--hover":
			{
				Vector2 vector2 = ParsePair(text);
				launchOptions.HoverTile = new Point((int)vector2.X, (int)vector2.Y);
				i++;
				break;
			}
			case "--select":
			{
				Vector2 vector = ParsePair(text);
				launchOptions.SelectTile = new Point((int)vector.X, (int)vector.Y);
				i++;
				break;
			}
			}
		}
		return launchOptions;
	}

	private static Vector2 ParsePair(string s)
	{
		string[] array = (s ?? "0,0").Split(',');
		return new Vector2(float.Parse(array[0], CultureInfo.InvariantCulture), float.Parse(array[1], CultureInfo.InvariantCulture));
	}
}
