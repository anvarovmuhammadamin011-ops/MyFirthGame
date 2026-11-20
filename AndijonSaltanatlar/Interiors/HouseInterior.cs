using System;
using System.Collections.Generic;
using AndijonSaltanatlar.Buildings;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Environment;
using AndijonSaltanatlar.Population;
using AndijonSaltanatlar.Units;
using AndijonSaltanatlar.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AndijonSaltanatlar.Interiors
{
    /// <summary>
    /// BINOGA KIRISH: o'yinchi boshqarayotgan odam (jangchi, fuqaro yoki shoh) bino yonida ENTER bossa,
    /// uning ichki ko'rinishi ochiladi:
    ///   - UY: yotoqxona + oshxona, uy turiga mos dizayn; ichkarida shu uyning HAQIQIY oilasi (PopulationManager).
    ///   - SAROY: taxt zali — taxtda shoh, soqchilar, vazir, kotib. Saroyga hamma kira oladi.
    ///     Shoh yonida F bosilsa — o'yinchi shohga aylanib, shaharga sayrga chiqadi (BecomeShahRequested).
    ///
    /// Shahar simulyatsiyasi orqada davom etadi. Boshqarilayotgan unitning shahardagi Position'i
    /// ichkarida o'zgarmaydi; chiqishda u bino eshigi oldiga qo'yiladi.
    /// </summary>
    public sealed partial class HouseInterior
    {
        private const int N = InteriorArt.N;
        private const float WalkSpeed = 2.1f, RunSpeed = 3.1f;

        private readonly AssetManager _assets;
        private readonly MapManager _map;
        private readonly BuildingManager _buildings;
        private readonly GameStateManager _state;

        private sealed class Prop
        {
            public Texture2D Tex;
            public Point Tile;
            public int Size;
            public float Depth => Tile.X + Tile.Y + Size;
            public Vector2 DrawPos => WorldObjectExtensions.AnchorBottomCenter(Tex, Tile, Size);
        }

        private sealed class Resident
        {
            public string Name;      // to'liq ism
            public string Short;     // uzoqdan ko'rinadigan qisqa ism
            public string Info;      // yaqinlashganda: yosh, kasb
            public string Line;      // salomlashish
            public Vector2 Pos;
            public Texture2D Tex;
            public float Scale = 1f;
            public float Lift;       // taxtda o'tirgan shoh — balandroq
            public bool IsShah;
            public bool FixedFacing; // taxtdagi shoh doim zalga qaraydi
            public string ChatText;  // o'zaro suhbat (ambient gaplashish) matni
            public float ChatTime;   // suhbat pufagi qolgan ko'rsatish vaqti
            // --- Qamal jangi (saroy himoyachisi) ---
            public SiegeFoe Foe;
            public float Hurt, Lunge, Cooldown, DownTime;
        }

        private readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();
        private readonly Dictionary<int, (Texture2D shell, Vector2 origin)> _shells = new Dictionary<int, (Texture2D, Vector2)>();
        private readonly List<Prop> _props = new List<Prop>();
        private readonly List<Resident> _residents = new List<Resident>();
        private readonly bool[,] _blocked = new bool[N, N];
        private Texture2D _shell;
        private Vector2 _shellOrigin;
        private Texture2D _glow;

        private Unit _avatar;
        private Vector2 _pos;
        private bool _leftDoor;
        private float _time;
        private string _title = "";
        private string _subtitle = "";
        private bool _palace;
        private int _doorX0, _doorX1;
        private InteriorArt.Style _style;

        // ---- Umumiy bino ichki tizimi (barcha bino turlari) ----
        private InteriorKind _kind = InteriorKind.House;
        private readonly List<InteriorHotspot> _hotspots = new List<InteriorHotspot>();
        private InteriorHotspot _nearHotspot;
        private bool _chestOpen;
        private bool _upperPlatform;
        private string _locationStatus = "";

        // ---- Uy ichidagi harakatlar: dasturxon atrofida o'tirish / ovqatlanish, to'shakda yotish ----
        public enum Pose { Stand, Sit, Eat, Lie, Bake }

        // ---- Non va tandir: dasturxonda non bo'lmasa — tandirda yopiladi (o'yinchi o'zi yoki oila a'zosi) ----
        /// <summary>Har uydagi dasturxondagi nonlar soni endi InteriorStore (BuildingId) da saqlanadi.</summary>
        public const int MaxBread = 4;
        private static readonly Dictionary<int, float> BakeClock = new Dictionary<int, float>();
        public const float BakeTime = 3f;
        private readonly Vector2 _stovePos = new Vector2(7.3f, 1.55f);   // tandir oldi
        private bool _carryBread;
        private Resident _baker;
        private int _bakerStage;
        private float _bakerTime;
        private Vector2 _bakerHome;
        private readonly Random _brng = new Random();

        // ---- Ambient suhbat: uy/bino ichidagi odamlar o'zaro gaplashadi ----
        private float _chatTimer = 4f;
        private float _replyDelay;
        private string _pendingReply;
        private Resident _replyFrom;
        private static readonly (string Say, string Reply)[] SmallTalk =
        {
            ("Ishlaringiz qalay?", "Shukr, yurib turibmiz."),
            ("Bugun havo yaxshi-a?", "Ha, ko'ngilga yoqadi."),
            ("Osh-noningiz tayyormi?", "Tayyor bo'lay deb qoldi."),
            ("Bolalar tinchmi?", "Tinch, hovlida o'ynashyapti."),
            ("Choy damladim, keling.", "Rahmat, hozir boraman."),
            ("Bozor narxi qalay ekan?", "Un biroz qimmatlashibdi."),
            ("Mehmon kutyapmizmi?", "Ha, qo'shnilar kelarmish."),
            ("Tandirga o't yoqdingmi?", "Yoqdim, non yopsa bo'ladi."),
            ("Hordiq olib o'tiring.", "Rahmat, charchamadim."),
            ("Yana ko'rishaylik.", "Albatta, xush kelibsiz."),
        };

        private int Bread
        {
            get => House != null ? InteriorStore.For(House.Id).Bread : 0;
            set { if (House != null) InteriorStore.For(House.Id).Bread = Math.Clamp(value, 0, MaxBread); }
        }

        /// <summary>Uyga kirmagan paytda oilalar o'zlari non yopadi: har uyda har ~6 o'yin soatida +1 non (3 tagacha).</summary>
        public static void TickOutside(float gameHours)
        {
            if (gameHours <= 0f) return;
            foreach (InteriorState st in new List<InteriorState>(InteriorStore.All))
            {
                int id = st.BuildingId;
                BakeClock.TryGetValue(id, out float c);
                c += gameHours;
                while (c >= 6f) { c -= 6f; if (st.Bread < 3) st.Bread++; }
                BakeClock[id] = c;
            }
        }
        public Pose CurrentPose { get; private set; } = Pose.Stand;
        public const int MealFood = 5;
        public const float EatTime = 3f;
        private Vector2 _standPos;          // o'tirish/yotishdan oldingi joy (turganda qaytadi)
        private Vector2 _tableCenter = new Vector2(6f, 5f);
        private Vector2 _bedPos = new Vector2(1.0f, 6.0f);
        private float _poseTime;
        private Rectangle _act1, _act2;     // ekrandagi harakat tugmalari (sensorli ekran uchun ham)
        private string _act1Label = "", _act2Label = "";
        private static readonly Vector2[] Seats = { new Vector2(5.5f, 6.4f), new Vector2(6.5f, 6.4f), new Vector2(7.45f, 4.5f), new Vector2(7.45f, 5.5f) };

        public bool Active { get; private set; }
        public Building House { get; private set; }
        public bool IsPalace => _palace;
        /// <summary>Boshqarilayotgan odam yonidagi (kirsa bo'ladigan) bino — ENTER ko'rsatmasi uchun.</summary>
        public Building NearbyHouse { get; private set; }
        /// <summary>Saroyda shoh yonida F bosildi — Game1 shohni shaharga chiqaradi.</summary>
        public bool BecomeShahRequested { get; set; }
        /// <summary>Shoh hozir shaharda bo'lsa, taxt bo'sh turadi (Game1 har kadr o'rnatadi).</summary>
        public bool ShahAway { get; set; }

        public HouseInterior(AssetManager assets, MapManager map, BuildingManager buildings, GameStateManager state)
        {
            _assets = assets;
            _map = map;
            _buildings = buildings;
            _state = state;
        }

        /// <summary>
        /// Devordan boshqa barcha qurilgan binolarga kirish mumkin: uylar, bozor, ferma, bog', saroy,
        /// masjid, madrasa, minora va darvoza — har biriga o'z shabloni beriladi. Devor tekshiriladi (ichki xona yo'q).
        /// </summary>
        internal static bool IsEnterable(Building b) =>
            b != null && b.BuildProgress >= 1f && b.Type != BuildingType.Wall && b.Type != BuildingType.Farm
            && b.Type != BuildingType.Mine && b.Type != BuildingType.Fishery;   // kon va baliqxona — ochiq ish joylari
            // Ferma — ichiga kirilmaydigan ochiq dala (dehqon tashqarida ishlaydi).

        // ==================================================================
        // Shahardagi binoni topish
        // ==================================================================

        /// <summary>Har kadr: boshqarilayotgan odam yonida kirsa bo'ladigan bino bormi (null — tekshirmaslik).</summary>
        private Unit _nearbyWho;

        /// <summary>Saroyga faqat soqchilar, jangchilar va shoh kiradi — oddiy fuqaro kira olmaydi.</summary>
        public static bool MayEnter(Building b, Unit who) => !(b != null && b.Type == BuildingType.MainHall && who is CitizenUnit);

        public void UpdateNearby(Unit who)
        {
            _nearbyWho = who;
            NearbyHouse = null;
            if (who == null || who.IsDown) return;
            float best = 0.9f;
            foreach (Building b in _buildings.Buildings)
            {
                if (!IsEnterable(b)) continue;
                float dx = MathF.Max(0f, MathF.Max(b.Position.X - who.Position.X, who.Position.X - (b.Position.X + b.Size.X)));
                float dy = MathF.Max(0f, MathF.Max(b.Position.Y - who.Position.Y, who.Position.Y - (b.Position.Y + b.Size.Y)));
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d <= best) { best = d; NearbyHouse = b; }
            }
        }

        // ==================================================================
        // Kirish / chiqish
        // ==================================================================

        public void Enter(Building building, Unit avatar)
        {
            if (building == null || avatar == null) return;
            _fight = false;
            if (!MayEnter(building, avatar))
            {
                _state.ShowMessage("Saroyga faqat soqchilar va jangchilar kira oladi!", true);
                return;
            }
            House = building;
            _avatar = avatar;
            _palace = building.Type == BuildingType.MainHall;
            _kind = InteriorStore.KindOf(building.Type);
            _glow ??= InteriorArt.Glow(96).ToTexture(_assets.GraphicsDevice);
            BecomeShahRequested = false;
            _hotspots.Clear();
            _nearHotspot = null;
            _chestOpen = false;
            _upperPlatform = false;

            if (_palace)
            {
                _doorX0 = InteriorArt.PalaceDoorX0;
                _doorX1 = InteriorArt.PalaceDoorX1;
                LoadShell(100, () => InteriorArt.PalaceShell(out Vector2 o).WithOrigin(o));
                BuildPalaceLayout();
                BuildPalaceResidents();
                _pos = new Vector2(_doorX1 + 0.05f, 7.4f);
            }
            else
            {
                _doorX0 = _doorX1 = 6;
                int v = VariantOf(building);
                InteriorArt.Style s = InteriorArt.For(v);
                _style = s;
                LoadShell(v, () => InteriorArt.Shell(s, out Vector2 o).WithOrigin(o));
                if (building.Type == BuildingType.House)
                {
                    BuildHouseLayout(v, s);
                    BuildHouseResidents(building);
                }
                else
                {
                    BuildLocation(building, _kind, s);
                }
                _pos = new Vector2(6.5f, 7.4f);
            }
            _leftDoor = false;
            CurrentPose = Pose.Stand;
            _carryBread = false;
            _baker = null;
            _chatTimer = 3f; _replyDelay = 0f; _pendingReply = null; _replyFrom = null;
            InteriorState st = InteriorStore.For(building.Id);
            st.Visited = true;
            avatar.DriveIdle();
            avatar.SetFacing(new Vector2(-1, -1));
            Active = true;
            _state.ShowMessage(_title + ": xush kelibsiz!");
        }

        /// <summary>Binodan chiqish. newAvatar berilsa (shohga aylanish), tashqariga o'sha chiqadi.</summary>
        public void Exit(Unit newAvatar = null)
        {
            if (!Active) return;
            Active = false;
            CurrentPose = Pose.Stand;
            if (newAvatar != null) _avatar = newAvatar;
            if (_avatar != null)
            {
                _avatar.Position = OutsideDoor(House);
                _avatar.SetFacing(new Vector2(1, 1));
                _avatar.DriveIdle();
            }
            if (newAvatar == null)
                _state.ShowMessage(_palace ? "Saroydan chiqdingiz"
                    : _kind == InteriorKind.House ? "Uydan chiqdingiz"
                    : House != null ? House.Name + " hududidan chiqdingiz" : "Tashqariga chiqdingiz");
            _residents.Clear();
        }

        /// <summary>Bino eshigi oldidagi yuriladigan nuqta (uy: old hovli darvozasi, saroy: old tomon o'rtasi).</summary>
        public Vector2 OutsideDoor(Building h)
        {
            Point entrance = h.FrontEntranceTile;
            var preferred = new Vector2(entrance.X + 0.5f, entrance.Y + 0.5f);
            if (_map.IsWalkable((int)preferred.X, (int)preferred.Y)) return preferred;
            for (int r = 0; r <= 2; r++)
                for (int y = h.Position.Y - 1 - r; y <= h.Position.Y + h.Size.Y + r; y++)
                    for (int x = h.Position.X - 1 - r; x <= h.Position.X + h.Size.X + r; x++)
                    {
                        bool inside = x >= h.Position.X && x < h.Position.X + h.Size.X && y >= h.Position.Y && y < h.Position.Y + h.Size.Y;
                        if (!inside && _map.IsWalkable(x, y)) return new Vector2(x + 0.5f, y + 0.5f);
                    }
            return _avatar != null ? _avatar.Position : preferred;
        }

        private static int VariantOf(Building h)
        {
            string key = h.SpriteKey ?? "";
            char last = key.Length > 0 ? key[key.Length - 1] : '0';
            return char.IsDigit(last) ? (last - '0') % 4 : 0;
        }

        private void LoadShell(int key, Func<(Canvas c, Vector2 o)> make)
        {
            if (!_shells.TryGetValue(key, out var s))
            {
                var (c, o) = make();
                s = (c.ToTexture(_assets.GraphicsDevice), o);
                _shells[key] = s;
            }
            _shell = s.shell;
            _shellOrigin = s.origin;
        }

        private Texture2D Tex(string key, Func<Canvas> make)
        {
            if (!_tex.TryGetValue(key, out Texture2D t))
            {
                t = make().ToTexture(_assets.GraphicsDevice);
                _tex[key] = t;
            }
            return t;
        }

        private void ClearLayout()
        {
            _props.Clear();
            Array.Clear(_blocked, 0, _blocked.Length);
        }

        private void Add(Texture2D t, int x, int y, int size)
        {
            _props.Add(new Prop { Tex = t, Tile = new Point(x, y), Size = size });
            for (int yy = y; yy < y + size; yy++)
                for (int xx = x; xx < x + size; xx++) _blocked[xx, yy] = true;
        }

        // ------------------------------------------------------------------
        // UY
        // ------------------------------------------------------------------

        private void BuildHouseLayout(int v, InteriorArt.Style s)
        {
            ClearLayout();
            string k = "h" + v + "_";
            Texture2D partition = Tex(k + "part", () => InteriorArt.Partition(s));
            foreach (int y in new[] { 0, 1, 2, 5, 6, 7 }) Add(partition, 4, y, 1); // o'rtada o'tish joyi (y = 3..4)

            // YOTOQXONA
            Add(Tex(k + "chest", () => InteriorArt.Chest(s)), 1, 0, 1);
            Hot("SANDIQ", 1.5f, 0.5f, InteriorAction.OpenChest, "SANDIQNI OCHISH");
            Add(Tex(k + "bed", () => InteriorArt.Bed(s)), 0, 5, 2);
            if (v == 1 || v == 2) Add(Tex(k + "stairs", () => InteriorArt.Stairs(s)), 3, 0, 1); // bolaxonaga zinapoya
            else Add(Tex(k + "cradle", () => InteriorArt.Cradle(s)), 3, 6, 1);

            // OSHXONA
            Add(Tex("shelf", InteriorArt.Shelf), 5, 0, 1);
            Add(Tex("jars", InteriorArt.Jars), 6, 0, 1);
            Add(Tex("stove", InteriorArt.Stove), 7, 0, 1);
            Add(Tex("sacks", InteriorArt.Sacks), 7, 2, 1);
            Add(Tex(k + "table", () => InteriorArt.Table(s)), 5, 4, 2);
        }

        // ------------------------------------------------------------------
        // UMUMIY BINO ICHKI TIZIMI: har bir bino turiga o'z hududi va harakatlari
        // ------------------------------------------------------------------

        private void BuildLocation(Building building, InteriorKind kind, InteriorArt.Style s)
        {
            _residents.Clear();
            _hotspots.Clear();
            _title = building.Name.ToUpperInvariant();
            _subtitle = "";
            _locationStatus = "";
            Season season = _state.Season;
            bool snow = season == Season.Qish;
            switch (kind)
            {
                case InteriorKind.Market: BuildMarket(building, s, season); break;
                case InteriorKind.Farm: BuildFarm(building, season); break;
                case InteriorKind.Garden: BuildGarden(building, season, snow); break;
                case InteriorKind.Caravanserai: BuildCaravanserai(building, season); break;
                case InteriorKind.Barracks:
                case InteriorKind.Arena: BuildMilitary(building, kind, season); break;
                case InteriorKind.Madrasa: BuildMadrasa(building, s); break;
                case InteriorKind.Mosque: BuildMosque(building, season); break;
                case InteriorKind.Tower: BuildTower(building); break;
                case InteriorKind.Gate: BuildGate(building, season); break;
                default: BuildPublicFallback(building, s); break;
            }
            // Daraja interyerda aks etadi (Promt 2): yuqori daraja — hashamatliroq nom.
            if (building.Level > 1)
            {
                string lvl = AndijonSaltanatlar.Buildings.BuildingUpgradeEffects.LevelName(building.Type, building.Level);
                _subtitle = string.IsNullOrEmpty(_subtitle) ? lvl : _subtitle + " · " + lvl;
            }
        }

        /// <summary>Harakatni ro'yxatga qo'shadi (ma'lumotga asoslangan interaktiv obyekt).</summary>
        private InteriorHotspot Hot(string label, float x, float y, InteriorAction a, string al,
            InteriorAction b = InteriorAction.None, string bl = "", float radius = 1.85f, string status = "")
        {
            var h = new InteriorHotspot
            {
                Id = _hotspots.Count.ToString(),
                Label = label,
                Tile = new Vector2(x, y),
                Primary = a,
                PrimaryLabel = al,
                Secondary = b,
                SecondaryLabel = bl ?? "",
                Radius = radius,
                Status = status
            };
            _hotspots.Add(h);
            return h;
        }

        private void AddNpc(string name, string info, string line, float x, float y, Texture2D tex, float scale = 1f)
        {
            _residents.Add(new Resident
            {
                Name = name,
                Short = name,
                Info = info,
                Line = line,
                Pos = new Vector2(x, y),
                Tex = tex,
                Scale = scale
            });
        }

        private Texture2D CitizenTex(int art) => _assets.Get(SpriteKeys.UnitCitizen + art + "_0");

        private void LoadOpen(int key, Color ground, Color? path, bool snow)
        {
            LoadShell(key, () => LocationArt.OpenShell(ground, out Vector2 o, path, snow).WithOrigin(o));
            _doorX0 = _doorX1 = 6;
        }

        private Color SeasonLeaf(Season season) => season switch
        {
            Season.Bahor => new Color(96, 162, 82),
            Season.Yoz => new Color(74, 136, 62),
            Season.Kuz => new Color(196, 148, 58),
            _ => new Color(120, 140, 120)
        };

        private void BuildMarket(Building building, InteriorArt.Style s, Season season)
        {
            ClearLayout();
            InteriorArt.Style ms = s;
            ms.Floor = new Color(202, 178, 138);
            ms.Accent = new Color(154, 62, 48);
            ms.Accent2 = new Color(222, 182, 64);
            LoadShell(300, () => InteriorArt.Shell(ms, out Vector2 o).WithOrigin(o));
            _doorX0 = _doorX1 = 6;
            _subtitle = "Savdo toqi — rastalar va savdogarlar";

            Texture2D stallA = Tex("mstallA", () => LocationArt.Stall(new Color(150, 62, 48), 0));
            Texture2D stallB = Tex("mstallB", () => LocationArt.Stall(new Color(42, 94, 150), 1));
            Texture2D stallC = Tex("mstallC", () => LocationArt.Stall(new Color(64, 118, 70), 2));
            Add(stallA, 1, 1, 2);
            Add(stallB, 5, 1, 2);
            Add(stallC, 1, 5, 2);
            Add(Tex("mcrate", () => LocationArt.Crate(new Color(140, 100, 62))), 5, 5, 1);
            Add(Tex("mcrate2", () => LocationArt.Crate(new Color(158, 118, 74))), 6, 5, 1);
            Add(Tex("msacks", InteriorArt.Sacks), 7, 4, 1);

            Hot("GURUCH RASTASI", 1.9f, 2.4f, InteriorAction.Buy, "SOTIB OLISH (10 dona)", InteriorAction.Sell, "SOTISH (10 dona)");
            Hot("MATO RASTASI", 5.9f, 2.4f, InteriorAction.Buy, "SOTIB OLISH (10 dona)", InteriorAction.Sell, "SOTISH (10 dona)");
            Hot("MEVA RASTASI", 1.9f, 5.6f, InteriorAction.Buy, "SOTIB OLISH (10 dona)", InteriorAction.Sell, "SOTISH (10 dona)");
            Hot("OMBOR", 5.6f, 6.2f, InteriorAction.OpenChest, "OMBORNI OCHISH");

            AddNpc("Savdogar Karim", "Guruch savdogari", "Guruchim sarxil, oling! Bugun arzon.", 3.2f, 2.6f, CitizenTex(CitizenArt.Merchant));
            AddNpc("Savdogar Zuhra", "Mato savdogari", "Atlas va adras — to'yga kerak bo'ladi.", 5.2f, 3.6f, CitizenTex(CitizenArt.Merchant));
            AddNpc("Xaridor Dilnoza", "Bozor xaridori", "Narxlar biroz qimmatlashibdi.", 3.0f, 5.4f, CitizenTex(CitizenArt.Buyer));
        }

        private void BuildFarm(Building building, Season season)
        {
            ClearLayout();
            bool snow = season == Season.Qish;
            LoadOpen(400, snow ? new Color(214, 220, 226) : new Color(146, 168, 104), new Color(182, 158, 112), snow);
            _subtitle = "Dehqonchilik xo'jaligi";
            Farm farm = building as Farm;
            string st = farm == null ? "" : farm.CropStage == FarmCropStage.Empty ? "Dala bo'sh" : farm.CropStage == FarmCropStage.Growing ? "Ekin o'smoqda" : "Hosil pishgan";
            _locationStatus = st;

            float growth = farm?.Growth ?? 0f;
            Texture2D crop = Tex("crop" + (int)(growth * 4), () => LocationArt.CropRow(growth, season));
            for (int i = 0; i < 3; i++)
            {
                Add(crop, 5, 1 + i, 1);
                Add(crop, 6, 1 + i, 1);
            }
            Add(Tex("water", LocationArt.WaterChannel), 1, 3, 2);
            Add(Tex("fcrate", () => LocationArt.Crate(new Color(140, 100, 62))), 1, 6, 1);
            Add(Tex("fcrate2", () => LocationArt.Crate(new Color(158, 118, 74))), 2, 6, 1);

            Hot("DALA", 5.6f, 2.2f, InteriorAction.Plant, "EKIN EKISH", InteriorAction.Harvest, "HOSILNI YIG'ISH");
            Hot("ARIQ", 1.8f, 3.6f, InteriorAction.Water, "SUG'ORISH HOLATI");
            Hot("OMBOR", 1.9f, 5.8f, InteriorAction.OpenChest, "OMBORNI OCHISH");

            AddNpc("Fermer Tohir", "Dehqon", farm != null && farm.Tended ? "Dalani o'zim parvarish qilaman." : "Urug' bo'lsa, bugun ekamiz.", 4.3f, 2.4f, CitizenTex(CitizenArt.Farmer));
            AddNpc("Fermer Oydin", "Dehqon", "Ariq suvi yetarlimi? Hosil shunga bog'liq.", 2.6f, 4.6f, CitizenTex(CitizenArt.Farmer));
        }

        private void BuildGarden(Building building, Season season, bool snow)
        {
            ClearLayout();
            LoadOpen(500, snow ? new Color(216, 222, 228) : new Color(118, 158, 88), new Color(200, 180, 144), snow);
            _subtitle = season switch
            {
                Season.Bahor => "Bahor: gullar ochilgan",
                Season.Yoz => "Yoz: soya va meva",
                Season.Kuz => "Kuz: barglar sarg'aygan",
                _ => "Qish: qor ostidagi chorbog'"
            };
            _locationStatus = _subtitle;

            Texture2D tree = Tex("gtree", () => LocationArt.Tree(SeasonLeaf(season), snow));
            Add(tree, 1, 1, 1);
            Add(tree, 6, 1, 1);
            Add(tree, 1, 6, 1);
            Add(tree, 6, 6, 1);
            Add(Tex("gflower", () => LocationArt.Flowers(season == Season.Kuz ? new Color(214, 170, 70) : new Color(214, 92, 120))), 5, 4, 1);
            Add(Tex("gflower2", () => LocationArt.Flowers(new Color(228, 176, 72))), 2, 4, 1);
            Add(Tex("gwell", LocationArt.Well), 4, 6, 1);
            Texture2D bench = Tex("gbench", () => LocationArt.Bench(_style.Variant == 2 ? new Color(150, 108, 70) : new Color(126, 84, 48)));
            Add(bench, 1, 3, 2);

            Hot("CHORBOG' DARAXTI", 1.5f, 1.6f, InteriorAction.Inspect, "DARAXTNI KO'RISH");
            Hot("GULZOR", 5.4f, 4.3f, InteriorAction.Inspect, "GULLARNI KO'RISH");
            Hot("SUPACHА", 1.9f, 3.8f, InteriorAction.Sit, "O'TIRISH", InteriorAction.Stand, "TURISH");
            Hot("QUDUQ", 4.4f, 6.3f, InteriorAction.Water, "SUG'ORISH HOLATI");

            AddNpc("Bog'bon Sodiq", "Bog'bon", "Bog'imiz har fasl o'z go'zalligida.", 3.2f, 2.2f, CitizenTex(CitizenArt.Farmer));
        }

        private void BuildCaravanserai(Building building, Season season)
        {
            ClearLayout();
            LoadOpen(600, season == Season.Qish ? new Color(212, 216, 220) : new Color(196, 176, 140), new Color(176, 152, 116), season == Season.Qish);
            _subtitle = "Karvonsaroy — savdo karvonlari qo'nog'i";
            Add(Tex("karch", LocationArt.GateArch), 1, 0, 2);
            Add(Tex("kcargo", () => LocationArt.CaravanCargo(new Color(150, 62, 48))), 5, 2, 2);
            Add(Tex("kcrate", () => LocationArt.Crate(new Color(148, 108, 68))), 1, 5, 1);
            Add(Tex("kcrate2", () => LocationArt.Crate(new Color(160, 120, 78))), 2, 5, 1);
            Add(Tex("kstall", () => LocationArt.Stall(new Color(42, 94, 150), 1)), 5, 5, 2);

            Hot("KARVON YUKI", 5.8f, 3.2f, InteriorAction.Caravan, "KARVONLARNI KO'RISH");
            Hot("SAVDO IDORASI", 5.8f, 6.2f, InteriorAction.Caravan, "SAVDO YO'LLARI");
            Hot("OMBOR", 1.8f, 5.6f, InteriorAction.OpenChest, "OMBORNI OCHISH");

            AddNpc("Karvonboshi Anvar", "Karvon rahbari", "Samarqanddan karvon yo'lda. Yuk og'ir.", 3.2f, 3.2f, CitizenTex(CitizenArt.Merchant));
            AddNpc("Savdogar Bekzod", "Savdogar", "Yo'llar ochiq bo'lsa, savdo jonlanadi.", 4.6f, 4.4f, CitizenTex(CitizenArt.Merchant));
        }

        private void BuildMilitary(Building building, InteriorKind kind, Season season)
        {
            ClearLayout();
            LoadOpen(700, season == Season.Qish ? new Color(214, 214, 214) : new Color(184, 160, 120), new Color(158, 136, 104), season == Season.Qish);
            _title = kind == InteriorKind.Arena ? "JANG MAYDONI" : "QO'SHINXONA";
            _subtitle = kind == InteriorKind.Arena ? "Gladiatorlar mashq maydoni" : "Navkarlar mashq qo'rg'oni";
            Add(Tex("marker", () => LocationArt.TrainingMarker(new Color(158, 46, 40))), 3, 1, 1);
            Add(Tex("dummy", LocationArt.PracticeDummy), 1, 2, 1);
            Add(Tex("dummy2", LocationArt.PracticeDummy), 6, 2, 1);
            Add(Tex("dummy3", LocationArt.PracticeDummy), 1, 5, 1);
            Add(Tex("mbench", () => LocationArt.Bench(new Color(126, 84, 48))), 5, 5, 2);
            Add(Tex("mchest", () => InteriorArt.Chest(_style)), 6, 6, 1);

            Hot("MASHG'ULOT NUQTASI", 3.5f, 1.6f, InteriorAction.Train, "MASHG'ULOTNI BOSHLASH");
            Hot("DAM OLISH JOYI", 5.9f, 5.8f, InteriorAction.Sit, "O'TIRISH", InteriorAction.Stand, "TURISH");
            Hot("QUROL SANDIG'I", 6.4f, 6.4f, InteriorAction.OpenChest, "SANDIQNI OCHISH");

            _residents.Clear();
            AddNpc("Sarkarda Rustam", "Qo'shin sarkardasi", "Jangchilar mashqqa tayyor.", 3.0f, 3.2f, _assets.Get(SpriteKeys.UnitSpearman + "_0"));
            AddNpc("Mashqchi Navkar", "Jangchi", "Har kuni mashq — g'alaba kaliti.", 4.6f, 2.8f, _assets.Get(SpriteKeys.UnitSwordsman + "_0"));
        }

        private void BuildMadrasa(Building building, InteriorArt.Style s)
        {
            ClearLayout();
            InteriorArt.Style ms = s;
            ms.Floor = new Color(190, 166, 128);
            ms.Accent = new Color(34, 120, 128);
            ms.Accent2 = new Color(222, 182, 64);
            LoadShell(800, () => InteriorArt.Shell(ms, out Vector2 o).WithOrigin(o));
            _doorX0 = _doorX1 = 6;
            _subtitle = "Ilm maskani — darsxona va kutubxona";
            Add(Tex("books", () => LocationArt.Bookshelf(new Color(122, 80, 46))), 1, 0, 1);
            Add(Tex("books2", () => LocationArt.Bookshelf(new Color(138, 92, 52))), 2, 0, 1);
            Add(Tex("desk", () => LocationArt.LowDesk(new Color(126, 84, 46))), 4, 2, 2);
            Add(Tex("desk2", () => LocationArt.LowDesk(new Color(140, 96, 54))), 4, 5, 2);
            Add(Tex("rug", () => LocationArt.PrayerRug(new Color(42, 94, 150))), 6, 2, 2);

            Hot("USTOZ", 3.1f, 3.3f, InteriorAction.Talk, "O'QITUVCHI BILAN GAPLASHISH");
            Hot("KUTUBXONA", 1.6f, 1.4f, InteriorAction.Library, "KUTUBXONANI KO'RISH");
            Hot("DARSXONA", 4.9f, 3.2f, InteriorAction.Lessons, "DARSLARNI KO'RISH", InteriorAction.Research, "ILM-FAN");

            AddNpc("Ustoz Komil", "Mudarris", "Ilm — eng yaxshi boylik. Savollaringiz bormi?", 3.0f, 3.0f, CitizenTex(CitizenArt.Craftsman));
            AddNpc("Talaba Islom", "Shogird", "Bugun hisob va nahv darslari bor.", 5.4f, 4.2f, CitizenTex(CitizenArt.Citizen));
            AddNpc("Talaba Maryam", "Shogird", "Kutubxonada qo'lyozmalar ko'p.", 2.2f, 5.2f, CitizenTex(CitizenArt.Citizen));
        }

        private void BuildMosque(Building building, Season season)
        {
            ClearLayout();
            InteriorArt.Style ms = _style;
            ms.Floor = new Color(180, 176, 158);
            ms.Accent = new Color(30, 110, 118);
            ms.Accent2 = new Color(222, 182, 64);
            LoadShell(900, () => InteriorArt.Shell(ms, out Vector2 o).WithOrigin(o));
            _doorX0 = _doorX1 = 6;
            _subtitle = "Jome masjidi — xotirjam ibodat maskani";
            Add(Tex("mcol", InteriorArt.Column), 1, 2, 1);
            Add(Tex("mcol2", InteriorArt.Column), 6, 2, 1);
            Add(Tex("mcol3", InteriorArt.Column), 2, 6, 1);
            Add(Tex("mcol4", InteriorArt.Column), 5, 6, 1);
            Add(Tex("mrug", () => LocationArt.PrayerRug(new Color(30, 110, 118))), 2, 1, 2);
            Add(Tex("mrug2", () => LocationArt.PrayerRug(new Color(38, 84, 140))), 5, 1, 2);
            Add(Tex("mrug3", () => LocationArt.PrayerRug(new Color(46, 120, 82))), 2, 4, 2);
            Add(Tex("mrug4", () => LocationArt.PrayerRug(new Color(120, 48, 44))), 5, 4, 2);

            Hot("IBODAT JOYI", 2.8f, 2.0f, InteriorAction.Pray, "XOTIRJAM O'TIRISH");
            Hot("MASJID HOVLISI", 4.4f, 5.6f, InteriorAction.Inspect, "MASJID HAQIDA");

            AddNpc("Imom Abdulahad", "Masjid imomi", "Assalomu alaykum. Masjid eshigi doim ochiq.", 3.4f, 3.4f, CitizenTex(CitizenArt.Craftsman));
            AddNpc("Musofir Bobo", "Mahalla oqsoqoli", "Tinchlik va totuvlik bo'lsin.", 4.6f, 4.6f, CitizenTex(CitizenArt.Citizen));
        }

        private void BuildTower(Building building)
        {
            ClearLayout();
            InteriorArt.Style ts = _style;
            ts.Floor = new Color(150, 146, 136);
            ts.Wall = new Color(176, 166, 148);
            ts.Accent = new Color(110, 82, 50);
            ts.Accent2 = new Color(180, 180, 170);
            LoadShell(1000, () => InteriorArt.Shell(ts, out Vector2 o).WithOrigin(o));
            _doorX0 = _doorX1 = 6;
            _subtitle = "Minora — qo'riqlash posti";
            Add(Tex("tstairs", () => InteriorArt.Stairs(ts)), 1, 1, 1);
            Add(Tex("tchest", () => InteriorArt.Chest(ts)), 6, 1, 1);
            Add(Tex("tbrazier", InteriorArt.Brazier), 1, 6, 1);

            if (!_upperPlatform)
            {
                Hot("ZINAPOYA", 1.6f, 1.6f, InteriorAction.Climb, "YUQORIGA CHIQISH");
                Hot("QO'RIQCHI SANDIG'I", 6.4f, 1.6f, InteriorAction.OpenChest, "SANDIQNI OCHISH");
                AddNpc("Qo'riqchi", "Minora qo'riqchisi", "Yuqoridan butun vodiy ko'rinadi.", 4.4f, 3.4f, _assets.Get(SpriteKeys.UnitSpearman + "_0"));
            }
            else
            {
                Hot("KUZATISH MAYDOCHASI", 4.0f, 3.6f, InteriorAction.Inspect, "ATROFNI KUZATISH");
                Hot("ZINAPOYA", 1.6f, 1.6f, InteriorAction.Descend, "PASTGA TUSHISH");
            }
        }

        private void BuildGate(Building building, Season season)
        {
            ClearLayout();
            LoadOpen(1100, season == Season.Qish ? new Color(208, 212, 216) : new Color(188, 170, 138), new Color(168, 146, 112), season == Season.Qish);
            _title = "DARVOZA";
            _subtitle = "Shahar darvozasi va qo'riqxona";
            Add(Tex("garch", LocationArt.GateArch), 1, 0, 2);
            Add(Tex("gcrate", () => LocationArt.Crate(new Color(148, 108, 68))), 5, 1, 1);
            Add(Tex("gbench", () => LocationArt.Bench(new Color(126, 84, 48))), 5, 4, 2);

            Hot("DARVOZA YO'LI", 2.0f, 1.8f, InteriorAction.PassGate, "DARVOZADAN O'TISH");
            Hot("QO'RIQCHI", 4.6f, 3.0f, InteriorAction.Talk, "QO'RIQCHI BILAN GAPLASHISH", InteriorAction.Inspect, "DARVOZA HOLATI");

            AddNpc("Darvoza qo'riqchisi", "Shahar qo'riqchisi", "Darvoza ochiq, yo'lovchilar tinch.", 4.4f, 3.0f, _assets.Get(SpriteKeys.UnitSpearman + "_0"));
        }

        private void BuildPublicFallback(Building building, InteriorArt.Style s)
        {
            ClearLayout();
            Add(Tex("pftable", () => InteriorArt.Table(s)), 3, 3, 2);
            Hot("BINO ICHI", 4.0f, 4.6f, InteriorAction.Inspect, "KO'RISH");
            _subtitle = "Bino ichki qismi";
        }

        private static readonly Vector2[] HouseSlots =
        {
            new Vector2(7.45f, 4.7f), new Vector2(7.5f, 1.35f), new Vector2(5.8f, 3.5f),
            new Vector2(2.5f, 2.9f), new Vector2(2.9f, 5.2f), new Vector2(1.7f, 3.9f)
        };

        private static readonly string[] Greetings =
        {
            "Assalomu alaykum! Xush kelibsiz!",
            "Marhamat, to'rga o'ting!",
            "Choy ichib keting, endi damlandi.",
            "Osh tayyor, dasturxonga marhamat!",
            "Uyimizga kelganingizdan xursandmiz.",
            "Yaxshi keldingizmi? Charchamadingizmi?"
        };

        private void BuildHouseResidents(Building house)
        {
            _residents.Clear();
            Household home = null;
            int households = 0;
            foreach (Household h in _state.Pop.Households)
            {
                if (h.HomeBuildingId != house.Id) continue;
                households++;
                home ??= h;
            }

            var members = new List<Citizen>();
            foreach (Citizen c in _state.Pop.Citizens)
                if (c.HomeBuildingId == house.Id || (home != null && c.HouseholdId == home.Id))
                    if (!members.Contains(c)) members.Add(c);

            string family = home != null && !string.IsNullOrWhiteSpace(home.FamilyName) ? home.FamilyName : null;
            _title = family != null ? family.ToUpperInvariant() + " XONADONI" : "UY";
            _subtitle = members.Count > 0
                ? members.Count + " kishi yashaydi" + (households > 1 ? " (" + households + " oila)" : "")
                : "Bu uyda hozircha hech kim yashamaydi";

            bool soldier = _avatar != null && !(_avatar is CitizenUnit);
            for (int i = 0; i < members.Count && i < HouseSlots.Length; i++)
            {
                Citizen c = members[i];
                string line = Greetings[(c.Id + i) % Greetings.Length];
                if (_avatar is ShahUnit && i == 0) line = "Hazratim! Kulbamizga qadam ranjida qildingiz!";
                else if (soldier && i == 0) line = "Soqchi aka, xush kelibsiz! Shahrimiz tinchmi?";
                string full = c.Name ?? "";
                int sp = full.IndexOf(' ');
                string prof = PopulationManager.ProfessionName(c.Profession);
                _residents.Add(new Resident
                {
                    Name = full,
                    Short = sp > 0 ? full.Substring(0, sp) : full,
                    Info = c.Age + " yosh" + (string.IsNullOrEmpty(prof) ? "" : " - " + prof),
                    Line = line,
                    Pos = HouseSlots[i],
                    Tex = _assets.Get(SpriteKeys.UnitCitizen + KindFor(c) + "_0"),
                    Scale = c.Age < 14 ? 0.72f : 1f
                });
            }
        }

        private static int KindFor(Citizen c)
        {
            switch (c.Profession)
            {
                case CitizenProfession.Savdogar: return CitizenArt.Merchant;
                case CitizenProfession.Dehqon:
                case CitizenProfession.Bogbon: return CitizenArt.Farmer;
                case CitizenProfession.Tashuvchi:
                case CitizenProfession.Muallim: return CitizenArt.Craftsman;
                case CitizenProfession.None: return c.Age < 14 ? CitizenArt.Citizen : CitizenArt.Buyer;
                default: return CitizenArt.Citizen;
            }
        }

        // ------------------------------------------------------------------
        // SAROY
        // ------------------------------------------------------------------

        private static readonly Vector2 ThroneSeat = new Vector2(4.0f, 1.05f);

        private void BuildPalaceLayout()
        {
            ClearLayout();
            InteriorArt.Style s = InteriorArt.PalaceStyle;
            Add(Tex("throne", InteriorArt.Throne), 3, 0, 2);
            Texture2D brazier = Tex("brazier", InteriorArt.Brazier);
            Add(brazier, 2, 1, 1);
            Add(brazier, 5, 1, 1);
            Texture2D column = Tex("column", InteriorArt.Column);
            Add(column, 1, 3, 1);
            Add(column, 6, 3, 1);
            Add(column, 1, 6, 1);
            Add(column, 6, 6, 1);
            Texture2D treasure = Tex("treasure", InteriorArt.Treasure);
            Add(treasure, 0, 0, 1);
            Add(treasure, 7, 0, 1);
            Add(Tex("palace_table", () => InteriorArt.Table(s)), 6, 4, 2); // shoh dasturxoni
        }

        private void BuildPalaceResidents()
        {
            _residents.Clear();
            bool asShah = _avatar is ShahUnit;
            bool shahHome = !ShahAway && !asShah;
            _title = "SHOH SAROYI";
            _subtitle = shahHome ? "Shoh " + ShahUnit.ShahName + " hazratlari taxtda" : asShah ? "Hazratim, saroyingizga xush kelibsiz" : "Shoh hazratlari shahar sayrida";

            Texture2D guard = _assets.Get(SpriteKeys.UnitSpearman + "_0");
            Texture2D vizier = _assets.Get(SpriteKeys.UnitCitizen + CitizenArt.Merchant + "_0");
            Texture2D scribe = _assets.Get(SpriteKeys.UnitCitizen + CitizenArt.Craftsman + "_0");
            bool soldier = _avatar != null && !(_avatar is CitizenUnit) && !asShah;

            if (shahHome)
            {
                _residents.Add(new Resident
                {
                    Name = "Shoh " + ShahUnit.ShahName,
                    Short = "SHOH",
                    Info = "Saltanat hukmdori",
                    Line = soldier ? "Botir navkarim, shahar osoyishtami?" : "Xush kelding, fuqarom! Arzing bo'lsa, ayt.",
                    Pos = ThroneSeat,
                    Tex = _assets.Get(SpriteKeys.UnitShah + "_0"),
                    Lift = 16,
                    IsShah = true,
                    FixedFacing = true
                });
            }
            string guardLine = asShah ? "Hazratim! Amringizga muntazirmiz!" : "Shoh huzuridasiz, odob saqlang!";
            _residents.Add(new Resident { Name = "Saroy soqchisi", Short = "Soqchi", Info = "Shoh qo'riqchisi", Line = guardLine, Pos = new Vector2(2.4f, 2.6f), Tex = guard });
            _residents.Add(new Resident { Name = "Saroy soqchisi", Short = "Soqchi", Info = "Shoh qo'riqchisi", Line = guardLine, Pos = new Vector2(5.6f, 2.6f), Tex = guard });
            _residents.Add(new Resident
            {
                Name = "Vazir Nizomiddin", Short = "Vazir", Info = "Bosh vazir",
                Line = asShah ? "Hazratim, xazina to'la, xalq tinch!" : shahHome ? "Hazratim bugun xush kayfiyatdalar." : "Shoh hazratlari shahar sayriga chiqdilar.",
                Pos = new Vector2(2.4f, 4.6f), Tex = vizier
            });
            _residents.Add(new Resident
            {
                Name = "Kotib Umar", Short = "Kotib", Info = "Saroy kotibi",
                Line = "Shohning farmonlarini men yozaman.",
                Pos = new Vector2(5.5f, 5.5f), Tex = scribe
            });
        }

        // ==================================================================
        // Update
        // ==================================================================

        private Resident NearestResident(float maxDist)
        {
            Resident near = null;
            float nd = maxDist;
            foreach (Resident r in _residents)
            {
                float d = Vector2.Distance(r.Pos, _pos);
                if (d < nd) { nd = d; near = r; }
            }
            return near;
        }

        public void Update(float dt, InputManager input)
        {
            if (!Active || _avatar == null) return;
            _time += dt;
            if (_fight)
            {
                for (int i = 0; i < _residents.Count; i++) if (_residents[i].ChatTime > 0f) _residents[i].ChatTime -= dt;
                UpdateFight(dt, input);
                return;
            }
            UpdateFamilyBaker(dt);
            UpdateChatter(dt);
            if (input == null) { _avatar.DriveIdle(); return; }

            // Saroyda shoh yonida F — shohga aylanib shaharga chiqish
            if (input.KeyPressed(Keys.F) && _palace)
            {
                Resident near = NearestResident(1.9f);
                if (near != null && near.IsShah) { BecomeShahRequested = true; return; }
            }
            if (_chestOpen)
            {
                UpdateChestPanel(input);
                _avatar.DriveIdle();
                return;
            }
            if (input.KeyPressed(Keys.Escape) || input.KeyPressed(Keys.Enter) || input.KeyPressed(Keys.F))
            {
                Exit();
                return;
            }

            UpdateNearHotspot();
            if (HandleHotspotKeys(input)) { _avatar.DriveIdle(); return; }
            if (_upperPlatform) { _avatar.DriveIdle(); return; }

            if (UpdatePose(dt, input)) return;

            Vector2 screen = Vector2.Zero;
            if (input.KeyDown(Keys.A) || input.KeyDown(Keys.Left)) screen.X -= 1;
            if (input.KeyDown(Keys.D) || input.KeyDown(Keys.Right)) screen.X += 1;
            if (input.KeyDown(Keys.W) || input.KeyDown(Keys.Up)) screen.Y -= 1;
            if (input.KeyDown(Keys.S) || input.KeyDown(Keys.Down)) screen.Y += 1;

            if (screen != Vector2.Zero && dt > 0f)
            {
                screen.Normalize();
                Vector2 dir = IsoMath.WorldToTileF(screen * 32f);
                dir.Normalize();
                float speed = input.KeyDown(Keys.LeftShift) || input.KeyDown(Keys.RightShift) ? RunSpeed : WalkSpeed;
                _avatar.DriveMove(dir, speed);
                Vector2 step = dir * speed * dt;
                if (!TryMove(_pos + step))
                    if (!TryMove(new Vector2(_pos.X + step.X, _pos.Y)))
                        TryMove(new Vector2(_pos.X, _pos.Y + step.Y));
            }
            else _avatar.DriveIdle();

            // Eshikka qaytib kelsa — tashqariga chiqadi
            Point tile = new Point((int)MathF.Floor(_pos.X), (int)MathF.Floor(_pos.Y));
            bool inDoorTile = tile.Y == N - 1 && tile.X >= _doorX0 && tile.X <= _doorX1;
            if (!inDoorTile) _leftDoor = true;
            else if (_leftDoor && _pos.Y > N - 0.45f) Exit();
        }

        // ==================================================================
        // INTERAKTIV OBYEKTLAR (ma'lumotga asoslangan harakatlar)
        // ==================================================================

        /// <summary>Uy/bino ichidagi odamlar o'zaro gaplashadi: vaqti-vaqti bilan biri gap boshlaydi, boshqasi javob beradi.</summary>
        private void UpdateChatter(float dt)
        {
            for (int i = 0; i < _residents.Count; i++)
                if (_residents[i].ChatTime > 0f) _residents[i].ChatTime -= dt;

            if (_replyDelay > 0f)
            {
                _replyDelay -= dt;
                if (_replyDelay <= 0f && _replyFrom != null && _pendingReply != null)
                {
                    _replyFrom.ChatText = _pendingReply;
                    _replyFrom.ChatTime = 3.2f;
                    _replyFrom = null; _pendingReply = null;
                }
            }

            _chatTimer -= dt;
            if (_chatTimer > 0f || _residents.Count == 0) return;
            _chatTimer = 6f + (float)_brng.NextDouble() * 6f;

            Resident speaker = _residents[_brng.Next(_residents.Count)];
            if (speaker == _baker || speaker.ChatTime > 0f) return; // band yoki hozir gapiryapti
            var pair = SmallTalk[_brng.Next(SmallTalk.Length)];
            speaker.ChatText = pair.Say;
            speaker.ChatTime = 3.4f;

            if (_residents.Count > 1)
            {
                Resident replier;
                do { replier = _residents[_brng.Next(_residents.Count)]; } while (replier == speaker);
                if (replier != _baker) { _replyFrom = replier; _pendingReply = pair.Reply; _replyDelay = 1.7f; }
            }
        }

        private void UpdateNearHotspot()
        {
            _nearHotspot = null;
            if (House == null || _hotspots.Count == 0) return;
            float best = float.MaxValue;
            foreach (InteriorHotspot h in _hotspots)
            {
                Vector2 center = h.Tile + new Vector2(h.Size * 0.5f);
                float d = Vector2.Distance(center, _pos);
                if (d <= h.Radius && d < best) { best = d; _nearHotspot = h; }
            }
        }

        /// <summary>E — asosiy, Q — ikkinchi harakat. Uy/saroyning o'z harakatlari (dasturxon, to'shak) ustun turadi.</summary>
        private bool HandleHotspotKeys(InputManager input)
        {
            if (_nearHotspot == null) return false;
            bool houseStyle = _kind == InteriorKind.House || _palace;
            if (input.KeyPressed(Keys.E) && (!houseStyle || _nearHotspot.Primary == InteriorAction.OpenChest || _nearHotspot.Primary == InteriorAction.Climb || _nearHotspot.Primary == InteriorAction.Descend))
            {
                Execute(_nearHotspot.Primary);
                return true;
            }
            if (input.KeyPressed(Keys.Q) && _nearHotspot.Secondary != InteriorAction.None)
            {
                Execute(_nearHotspot.Secondary);
                return true;
            }
            return false;
        }

        private void Execute(InteriorAction action)
        {
            if (House == null) return;
            InteriorState st = InteriorStore.For(House.Id);
            switch (action)
            {
                case InteriorAction.Sit:
                    _standPos = _pos;
                    if (_nearHotspot != null) _pos = _nearHotspot.Tile + new Vector2(0.5f);
                    CurrentPose = Pose.Sit;
                    _poseTime = 0f;
                    _avatar.DriveIdle();
                    _state.ShowMessage("O'tirdingiz. W/Q — turish");
                    break;
                case InteriorAction.Stand: StandUp(); break;
                case InteriorAction.Eat: Eat(); break;
                case InteriorAction.Lie: LieDown(); break;
                case InteriorAction.OpenChest:
                    _chestOpen = true;
                    _state.ShowMessage("Sandiq ochildi: 1-2 oziq-ovqat, 3-4 urug', 5 mahsulot. ESC — yopish");
                    break;
                case InteriorAction.Buy: MarketBuy(); break;
                case InteriorAction.Sell: MarketSell(); break;
                case InteriorAction.Talk: TalkToNpc(); break;
                case InteriorAction.Plant: FarmPlant(); break;
                case InteriorAction.Water: ShowWaterStatus(); break;
                case InteriorAction.Harvest: FarmHarvest(); break;
                case InteriorAction.Train:
                    _state.StartTrainingSession(_buildings);
                    break;
                case InteriorAction.Lessons:
                    _state.ShowMessage("Darslar: " + _state.Pop.Count + " aholi, o'rtacha ma'lumot " + MathF.Round(_state.Pop.AvgEducation) + "%. " + _state.Civ.Player.Researched.Count + " texnologiya o'rganilgan");
                    break;
                case InteriorAction.Research:
                    _state.ShowMessage(string.IsNullOrEmpty(_state.Civ.Player.CurrentResearch)
                        ? "Hozir tadqiqot yo'q. TARAQQIYOT bo'limidan yangi ilm tanlang."
                        : "Tadqiqot davom etmoqda: " + _state.Civ.Player.CurrentResearch);
                    break;
                case InteriorAction.Library:
                    _state.ShowMessage("Kutubxona: " + _state.Civ.Player.Researched.Count + " asosiy asar. Aholi ma'lumoti " + MathF.Round(_state.Pop.AvgEducation) + "%");
                    break;
                case InteriorAction.Pray:
                    _state.ShowMessage("Masjidda xotirjam o'tirdingiz. Qalbingiz taskin topdi.");
                    break;
                case InteriorAction.Climb:
                    _upperPlatform = true;
                    BuildTower(House);
                    _pos = new Vector2(4.0f, 5.2f);
                    _state.ShowMessage("Minora tepasiga chiqdingiz — atrofni kuzatishingiz mumkin");
                    break;
                case InteriorAction.Descend:
                    _upperPlatform = false;
                    BuildTower(House);
                    _pos = new Vector2(2.4f, 2.4f);
                    _state.ShowMessage("Pastga tushdingiz");
                    break;
                case InteriorAction.PassGate: Exit(); break;
                case InteriorAction.Inspect: Inspect(); break;
                case InteriorAction.Caravan: ShowCaravanStatus(); break;
                case InteriorAction.WallInfo: ShowWallStatus(); break;
            }
        }

        private void MarketBuy()
        {
            InteriorState st = InteriorStore.For(House.Id);
            const int amount = 10, cost = 30;
            if (st.MarketStock < amount) { _state.ShowMessage("Rastada mahsulot qolmadi. Keyinroq keling.", true); return; }
            if (!_state.CanAfford(cost)) { _state.ShowMessage("Tanga yetarli emas: " + cost + " kerak", true); return; }
            _state.Coins -= cost;
            _state.Food += amount;
            st.MarketStock -= amount;
            st.MarketPurchases++;
            _state.ShowMessage("Sotib olindi: +" + amount + " oziq-ovqat (-" + cost + " tanga). Rastada: " + st.MarketStock);
        }

        private void MarketSell()
        {
            InteriorState st = InteriorStore.For(House.Id);
            const int amount = 10, gain = 18;
            if (_state.Food < amount) { _state.ShowMessage("Sotish uchun omborda " + amount + " dona mahsulot kerak", true); return; }
            _state.Food -= amount;
            _state.Coins += gain;
            st.MarketStock += amount;
            st.MarketSales++;
            _state.ShowMessage("Sotildi: -" + amount + " oziq-ovqat (+ " + gain + " tanga). Savdo: " + st.MarketSales + " ta");
        }

        private void FarmPlant()
        {
            if (!(House is Farm farm)) return;
            if (farm.CropStage != FarmCropStage.Empty) { _state.ShowMessage("Dalada ekin bor — avval hosilni yig'ing", true); return; }
            if (_state.Farming.Seeds <= 0) { _state.ShowMessage("Urug' tugagan! FERMERLIK bo'limida urug' sotib oling — bepul berilmaydi", true); return; }
            _state.Farming.Seeds--;
            farm.PlantCrop();
            InteriorStore.For(House.Id).GardenCare++;
            _state.ShowMessage("Ekin ekildi. Omborda " + _state.Farming.Seeds + " qop urug' qoldi");
            RebuildLocation();
        }

        private void FarmHarvest()
        {
            if (!(House is Farm farm)) return;
            if (farm.CropStage != FarmCropStage.Mature) { _state.ShowMessage("Hosil hali pishmagan — " + (int)(farm.Growth * 100) + "% tayyor", true); return; }
            int yield = farm.HarvestCrop();
            int stored = farm.SellStoredProduce();
            _state.Food += stored;
            _state.ShowMessage("Hosil yig'ildi: +" + stored + " oziq-ovqat" + (yield > 0 ? " (" + yield + " dona)" : ""));
            RebuildLocation();
        }

        private void ShowWaterStatus()
        {
            string water = _state.Farming.Irrigation ? "Ariqlar toza, suv yetarli" : "Ariq quruq — Yoz faslida tozalash kerak";
            string grow = House is Farm f ? " Ekin holati: " + (int)(f.Growth * 100) + "%" : "";
            _state.ShowMessage(water + "." + grow);
        }

        private void TalkToNpc()
        {
            Resident r = NearestResident(3.2f);
            _state.ShowMessage(r == null ? "Yaqin atrofda hech kim yo'q." : r.Name + ": " + r.Line);
        }

        private void Inspect()
        {
            InteriorState st = InteriorStore.For(House.Id);
            switch (_kind)
            {
                case InteriorKind.Garden:
                    _state.ShowMessage(_subtitle + ". Bog' parvarishi: " + st.GardenCare + " marta ishlov berilgan.");
                    break;
                case InteriorKind.Tower:
                    _state.ShowMessage("Atrof kuzatilmoqda: " + _buildings.Buildings.Count + " bino, " + _state.Population + " aholi, mudofaa " + AndijonSaltanatlar.Defense.CityDefenseManager.Compute(_buildings, _state.Military.TotalStrength));
                    break;
                case InteriorKind.Mosque:
                    _state.ShowMessage("Jome masjidi: mahalla ahli uchun xotirjam ibodat maskani.");
                    break;
                case InteriorKind.Gate:
                    ShowWallStatus();
                    break;
                default:
                    _state.ShowMessage(_title + ". " + _subtitle);
                    break;
            }
        }

        private void ShowCaravanStatus()
        {
            int saroy = _buildings.Count(BuildingType.Caravanserai);
            _state.ShowMessage("Faol karvonsaroylar: " + saroy + ". Tanga: " + _state.Coins + ", oziq-ovqat: " + _state.Food);
        }

        private void ShowWallStatus()
        {
            Building b = House;
            _state.ShowMessage(b.Name + ": daraja " + b.Level + ", mustahkamlik " + b.HitPoints + "/" + b.MaxHitPoints);
        }

        private void RebuildLocation()
        {
            if (House == null) return;
            if (_kind == InteriorKind.House || _palace) return;
            BuildLocation(House, _kind, _style);
            UpdateNearHotspot();
        }

        // ---- Sandiq paneli: BuildingId bo'yicha saqlanadigan real zaxira ----
        private readonly Rectangle[] _chestRects = new Rectangle[5];
        private Rectangle _chestClose;

        private void UpdateChestPanel(InputManager input)
        {
            InteriorState st = InteriorStore.For(House.Id);
            if (input.KeyPressed(Keys.Escape) || input.KeyPressed(Keys.E)) { _chestOpen = false; return; }
            int clicked = -1;
            if (input.LeftPressed)
            {
                if (_chestClose != Rectangle.Empty && _chestClose.Contains(input.MousePosition)) { _chestOpen = false; return; }
                for (int i = 0; i < _chestRects.Length; i++)
                    if (_chestRects[i] != Rectangle.Empty && _chestRects[i].Contains(input.MousePosition)) clicked = i;
            }
            int food = _state.Food;
            if (input.KeyPressed(Keys.D1) || clicked == 0) MoveToChest(ref food, ref st.ChestFood, 5);
            else if (input.KeyPressed(Keys.D2) || clicked == 1) MoveToChest(ref st.ChestFood, ref food, 5);
            else if (input.KeyPressed(Keys.D3) || clicked == 2) MoveSeedsToChest(st, 5);
            else if (input.KeyPressed(Keys.D4) || clicked == 3) MoveSeedsFromChest(st, 5);
            else if (input.KeyPressed(Keys.D5) || clicked == 4) MoveToChest(ref st.ChestProduce, ref food, 5);
            _state.Food = food;
        }

        private void MoveToChest(ref int from, ref int to, int amount)
        {
            int n = Math.Min(amount, from);
            if (n <= 0) { _state.ShowMessage("Bu yerda hech narsa yo'q", true); return; }
            from -= n;
            to += n;
        }

        private void MoveSeedsToChest(InteriorState st, int amount)
        {
            int n = Math.Min(amount, _state.Farming.Seeds);
            if (n <= 0) { _state.ShowMessage("Urug' yo'q", true); return; }
            _state.Farming.Seeds -= n;
            st.ChestSeeds += n;
        }

        private void MoveSeedsFromChest(InteriorState st, int amount)
        {
            int n = Math.Min(amount, st.ChestSeeds);
            if (n <= 0) { _state.ShowMessage("Sandiqda urug' yo'q", true); return; }
            st.ChestSeeds -= n;
            _state.Farming.Seeds += n;
        }

        // ------------------------------------------------------------------ harakatlar
        private bool IsHouse => House != null && House.Type == BuildingType.House && !_palace;
        private bool NearTable => IsHouse && Vector2.Distance(_pos, _tableCenter) < 1.9f;
        private bool NearStove => IsHouse && Vector2.Distance(_pos, _stovePos) < 1.5f;

        /// <summary>
        /// Oila a'zosi: dasturxonda non qolmasa (va o'yinchi o'zi yopmayotgan bo'lsa) — tandirga borib non yopadi,
        /// dasturxonga qo'yadi va joyiga qaytadi.
        /// </summary>
        private void UpdateFamilyBaker(float dt)
        {
            if (!IsHouse) return;
            if (_baker == null)
            {
                if (Bread > 0 || _carryBread || CurrentPose == Pose.Bake) return;
                foreach (Resident r in _residents)
                    if (!r.IsShah && r.Scale >= 1f) { _baker = r; break; }
                if (_baker == null) return;
                _bakerHome = _baker.Pos;
                _bakerStage = 0;
                _bakerTime = 0f;
                _baker.Line = "Non tugabdi — tandirda non yopay";
            }
            Resident b = _baker;
            Vector2 tableSpot = new Vector2(7.45f, 5.0f);
            switch (_bakerStage)
            {
                case 0: if (WalkResident(b, _stovePos, dt)) { _bakerStage = 1; _bakerTime = 0f; b.Line = "Tandirda non yopyapman..."; } break;
                case 1:
                    _bakerTime += dt;
                    if (_bakerTime >= BakeTime) { _bakerStage = 2; b.Line = "Issiq non tayyor!"; }
                    break;
                case 2: if (WalkResident(b, tableSpot, dt)) { Bread += 2; _bakerStage = 3; b.Line = "Dasturxonga non qo'ydim, marhamat!"; } break;
                case 3: if (WalkResident(b, _bakerHome, dt)) { _baker = null; } break;
            }
        }

        private static bool WalkResident(Resident r, Vector2 to, float dt)
        {
            Vector2 d = to - r.Pos;
            float dist = d.Length();
            if (dist < 0.05f) return true;
            r.Pos += d / dist * MathF.Min(dist, 1.3f * dt);
            return false;
        }
        private bool NearBed => IsHouse && Vector2.Distance(_pos, _bedPos) < 1.8f;

        /// <summary>Hozir bajarsa bo'ladigan harakatlar (ekrandagi tugmalar va klaviatura ko'rsatmasi).</summary>
        private void ActionLabels(out string a1, out string a2)
        {
            a1 = a2 = "";
            switch (CurrentPose)
            {
                case Pose.Sit:
                    if (IsHouse) { a1 = "[E] OVQATLANISH"; a2 = "[Q] TURISH"; }
                    else a1 = "[Q] TURISH";
                    break;
                case Pose.Eat: a1 = "OVQATLANMOQDA..."; break;
                case Pose.Lie: a1 = "DAM OLMOQDA..."; a2 = "[Q] TURISH"; break;
                case Pose.Bake: a1 = "NON YOPILMOQDA..."; break;
                default:
                    if (!IsHouse && !_palace && _nearHotspot != null)
                    {
                        a1 = "[E] " + _nearHotspot.PrimaryLabel;
                        if (_nearHotspot.Secondary != InteriorAction.None) a2 = "[Q] " + _nearHotspot.SecondaryLabel;
                    }
                    else if (_carryBread && NearTable) a1 = "[E] NONNI DASTURXONGA QO'YISH";
                    else if (NearTable && !_carryBread) a1 = "[E] DASTURXONGA O'TIRISH";
                    else if (NearStove && !_carryBread) a1 = "[E] TANDIRDA NON YOPISH";
                    else if (NearBed) a1 = "[E] YOTISH";
                    break;
            }
        }

        /// <summary>Sinov: avatarni dasturxon yoki to'shak yoniga qo'yib, holatni o'rnatadi.</summary>
        public void TestPose(string pose)
        {
            if (!IsHouse) return;
            switch ((pose ?? "").ToLowerInvariant())
            {
                case "sit": _pos = new Vector2(6.4f, 6.6f); SitDown(); break;
                case "eat": _pos = new Vector2(6.4f, 6.6f); SitDown(); _state.Food = Math.Max(_state.Food, MealFood); Eat(); _poseTime = 0.5f; break;
                case "lie": _pos = new Vector2(2.4f, 6.2f); LieDown(); _poseTime = 0.4f; break;
                case "table": _pos = new Vector2(6.4f, 6.8f); break;
                case "nobread": Bread = 0; break;
                case "stove": Bread = 1; _pos = new Vector2(7.2f, 2.3f); CurrentPose = Pose.Bake; _poseTime = 1f; break;
            }
        }

        /// <summary>Asosiy harakat (E yoki 1-tugma).</summary>
        public void DoPrimary()
        {
            switch (CurrentPose)
            {
                case Pose.Stand:
                    if (_carryBread && NearTable)
                    {
                        _carryBread = false;
                        Bread += 2;
                        _state.ShowMessage("Issiq nonni dasturxonga qo'ydingiz (" + Bread + " ta non)");
                    }
                    else if (NearTable && !_carryBread) SitDown();
                    else if (NearStove && !_carryBread)
                    {
                        if (_baker != null) { _state.ShowMessage("Oila a'zosi allaqachon non yopyapti"); break; }
                        CurrentPose = Pose.Bake;
                        _poseTime = 0f;
                        _avatar.DriveIdle();
                        _avatar.SetFacing(_stovePos - _pos + new Vector2(0, -1));
                        _state.ShowMessage("Tandirga xamir yopishtiryapsiz...");
                    }
                    else if (NearBed) LieDown();
                    break;
                case Pose.Sit:
                    Eat();
                    break;
            }
        }

        /// <summary>O'rnidan turish (Q yoki 2-tugma).</summary>
        public void StandUp()
        {
            if (CurrentPose == Pose.Stand || CurrentPose == Pose.Eat || CurrentPose == Pose.Bake) return;
            CurrentPose = Pose.Stand;
            _pos = _standPos;
            _avatar.DriveIdle();
        }

        private void SitDown()
        {
            _standPos = _pos;
            Vector2 best = Seats[0];
            foreach (Vector2 s in Seats) if (Vector2.Distance(s, _pos) < Vector2.Distance(best, _pos)) best = s;
            _pos = best;
            CurrentPose = Pose.Sit;
            _poseTime = 0f;
            _avatar.DriveIdle();
            _avatar.SetFacing(_tableCenter - _pos);
            _state.ShowMessage("Dasturxon atrofida o'tirdingiz. E — ovqatlanish, Q — turish");
        }

        private void Eat()
        {
            if (IsHouse && Bread <= 0)
            {
                _state.ShowMessage("Dasturxonda non yo'q — tandirda non yoping (yoki oila a'zosi yopadi)", true);
                return;
            }
            if (_state.Food < MealFood)
            {
                _state.ShowMessage("Uyda ovqat yo'q — omborda oziq-ovqat tugagan", true);
                return;
            }
            _state.Food -= MealFood;
            if (IsHouse) Bread -= 1;
            CurrentPose = Pose.Eat;
            _poseTime = 0f;
            _state.ShowMessage("Bismillah! Osh tortildi (-" + MealFood + " oziq-ovqat)");
        }

        private void LieDown()
        {
            _standPos = _pos;
            _pos = _bedPos;
            CurrentPose = Pose.Lie;
            _poseTime = 0f;
            _avatar.DriveIdle();
            _state.ShowMessage("To'shakda dam olyapsiz. Q — turish");
        }

        /// <summary>O'tirgan/yotgan holat. true — bu kadrda yurish yo'q.</summary>
        private bool UpdatePose(float dt, InputManager input)
        {
            ActionLabels(out _act1Label, out _act2Label);
            bool click1 = input.LeftPressed && _act1 != Rectangle.Empty && _act1.Contains(input.MousePosition);
            bool click2 = input.LeftPressed && _act2 != Rectangle.Empty && _act2.Contains(input.MousePosition);
            if (click1)
            {
                if (!IsHouse && !_palace && _nearHotspot != null) Execute(_nearHotspot.Primary);
                else DoPrimary();
            }
            else if (click2)
            {
                if (!IsHouse && !_palace && _nearHotspot != null && _nearHotspot.Secondary != InteriorAction.None) Execute(_nearHotspot.Secondary);
                else StandUp();
            }
            else if (input.KeyPressed(Keys.E)) DoPrimary();
            else if (input.KeyPressed(Keys.Q)) StandUp();

            if (CurrentPose == Pose.Stand) return false;
            _poseTime += dt;
            _avatar.DriveIdle();
            if (CurrentPose == Pose.Eat && _poseTime >= EatTime)
            {
                CurrentPose = Pose.Sit;
                _poseTime = 0f;
                _avatar.HitPoints = Math.Min(_avatar.MaxHitPoints, _avatar.HitPoints + 40);
                _state.ShowMessage("Osh juda mazali bo'ldi! Kuch qaytdi.");
            }
            if (CurrentPose == Pose.Bake && _poseTime >= BakeTime)
            {
                CurrentPose = Pose.Stand;
                _carryBread = true;
                _state.ShowMessage("Non pishdi! Endi dasturxonga qo'ying (E)");
            }
            if (CurrentPose == Pose.Lie && _avatar.HitPoints < _avatar.MaxHitPoints && (int)(_poseTime * 2f) != (int)((_poseTime - dt) * 2f))
                _avatar.HitPoints = Math.Min(_avatar.MaxHitPoints, _avatar.HitPoints + 2);
            // Yurish tugmasi bosilsa — o'rnidan turadi (ovqat tugamaguncha emas)
            bool moveKey = input.KeyDown(Keys.W) || input.KeyDown(Keys.A) || input.KeyDown(Keys.S) || input.KeyDown(Keys.D) ||
                           input.KeyDown(Keys.Up) || input.KeyDown(Keys.Down) || input.KeyDown(Keys.Left) || input.KeyDown(Keys.Right);
            if (moveKey && CurrentPose != Pose.Eat && CurrentPose != Pose.Bake) { StandUp(); return false; }
            return true;
        }

        private bool TryMove(Vector2 p)
        {
            bool inDoor = p.X >= _doorX0 + 0.15f && p.X <= _doorX1 + 0.85f;
            float maxY = inDoor ? N - 0.05f : N - 0.3f;
            if (p.X < 0.25f || p.Y < 0.3f || p.X > N - 0.3f || p.Y > maxY) return false;
            int tx = (int)MathF.Floor(p.X), ty = (int)MathF.Floor(MathF.Min(p.Y, N - 0.01f));
            if (_blocked[tx, ty]) return false;
            foreach (Resident r in _residents)
                if (!r.IsShah && Vector2.Distance(r.Pos, p) < 0.38f && Vector2.Distance(r.Pos, _pos) >= 0.38f) return false;
            _pos = p;
            return true;
        }

        // ==================================================================
        // Chizish
        // ==================================================================

        private Matrix Transform(int vw, int vh)
        {
            float roomW = N * 2 * GameConfig.HalfTileW + 40;
            float roomH = N * 2 * GameConfig.HalfTileH + InteriorArt.WallH + 40;
            float scale = MathHelper.Clamp(MathF.Min((vw - 32) / roomW, (vh - 130) / roomH), 0.9f, 2.4f);
            scale = MathF.Floor(scale * 4f) / 4f; // piksel-art aniq qolishi uchun
            Vector2 focus = new Vector2(0, N * GameConfig.HalfTileH - InteriorArt.WallH / 2f + 6);
            return Matrix.CreateTranslation(-focus.X, -focus.Y, 0) * Matrix.CreateScale(scale, scale, 1f)
                 * Matrix.CreateTranslation(MathF.Round(vw / 2f), MathF.Round(vh / 2f + 16), 0);
        }

        public void Draw(SpriteBatch sb, Texture2D px, PixelFont font, int vw, int vh, float night)
        {
            if (!Active) return;
            _pxCache = px;
            Matrix m = Transform(vw, vh);

            // --- Xona ---
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, m);
            sb.Draw(_shell, -_shellOrigin, Color.White);

            var items = new List<(float depth, int layer, Action draw)>();
            foreach (Prop p in _props)
            {
                Prop pp = p;
                items.Add((pp.Depth, 0, () => sb.Draw(pp.Tex, pp.DrawPos, Color.White)));
            }
            if (IsHouse)
            {
                int nb = Bread;
                items.Add((_tableCenter.X + _tableCenter.Y + 0.9f, 0, () =>
                {
                    for (int i = 0; i < nb; i++)
                    {
                        Vector2 w = IsoMath.TileToWorld(new Vector2(5.55f + (i % 2) * 0.55f, 4.55f + (i / 2) * 0.5f)) + new Vector2(0, -13);
                        DrawNon(sb, _pxCache, w);
                    }
                }));
            }
            foreach (Resident r in _residents)
            {
                Resident rr = r;
                bool left = rr.FixedFacing ? false : FacesLeft(rr.Pos, _pos);
                items.Add((rr.Pos.X + rr.Pos.Y, 1, () => DrawResident(sb, rr, left)));
            }
            float avatarDepth = CurrentPose == Pose.Lie ? _bedPos.X + _bedPos.Y + 1.2f : _pos.X + _pos.Y;
            if (_carryBread)
                items.Add((avatarDepth + 0.01f, 2, () => DrawNon(sb, px, IsoMath.TileToWorld(_pos) + new Vector2(_avatar.Flipped ? -7 : 7, -24))));
            if (CurrentPose == Pose.Bake)
            {
                // tandir ichida non pishmoqda — olov uchqunlari
                float bt = _poseTime;
                items.Add((_stovePos.X + _stovePos.Y + 0.5f, 2, () =>
                {
                    for (int i = 0; i < 5; i++)
                    {
                        float u = (bt * 1.3f + i / 5f) % 1f;
                        Vector2 p = IsoMath.TileToWorld(new Vector2(7.5f, 0.8f)) + new Vector2((i - 2) * 3f, -12f - u * 18f);
                        sb.Draw(px, new Rectangle((int)p.X, (int)p.Y, 2, 2), new Color(255, 170, 60) * (1f - u));
                    }
                }));
            }
            items.Add((avatarDepth, 1, () =>
            {
                Texture2D tex = _avatar.Texture;
                if (tex == null) return;
                Vector2 feet = IsoMath.TileToWorld(_pos);
                var origin = new Vector2(tex.Width / 2f, tex.Height - AssetManager.FootOf(tex));
                float k = AssetManager.ScaleOf(tex);
                SpriteEffects fx = _avatar.Flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                switch (CurrentPose)
                {
                    case Pose.Sit:
                    case Pose.Eat:
                        // O'tirgan: gavda pastroq (bukilgan), ovqatlanayotganda qo'l/bosh biroz qimirlaydi
                        float bob = CurrentPose == Pose.Eat ? MathF.Abs(MathF.Sin(_poseTime * 7f)) * 1.5f : 0f;
                        sb.Draw(tex, new Vector2(MathF.Round(feet.X), MathF.Round(feet.Y + 3 + bob)), null, Color.White, 0f, origin, new Vector2(k, 0.78f * k), fx, 0f);
                        break;
                    case Pose.Lie:
                        // Yotgan: to'shak ustida, yonboshlab
                        Vector2 bed = IsoMath.TileToWorld(_bedPos) + new Vector2(0, -10);
                        sb.Draw(tex, new Vector2(MathF.Round(bed.X), MathF.Round(bed.Y)), null, Color.White, -MathHelper.PiOver2 * 0.92f, origin, 0.95f * k, SpriteEffects.None, 0f);
                        break;
                    default:
                        sb.Draw(tex, new Vector2(MathF.Round(feet.X), MathF.Round(feet.Y)), null, Color.White, 0f, origin, k, fx, 0f);
                        break;
                }
            }));
            items.Sort((a, b) => a.depth != b.depth ? a.depth.CompareTo(b.depth) : a.layer.CompareTo(b.layer));
            foreach (var it in items) it.draw();
            sb.End();

            // --- Yorug'lik ---
            if (night > 0.05f)
            {
                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
                sb.Draw(px, new Rectangle(0, 0, vw, vh), new Color(10, 8, 20) * (0.45f * night));
                sb.End();
            }
            sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp, null, null, null, m);
            float flicker = 0.8f + 0.2f * MathF.Sin(_time * 9f) * MathF.Sin(_time * 3.7f + 1f);
            var glowOrigin = new Vector2(_glow.Width / 2f, _glow.Height / 2f);
            if (_palace)
            {
                foreach (Vector2 b in new[] { new Vector2(2.5f, 1.5f), new Vector2(5.5f, 1.5f) })
                {
                    Vector2 w = IsoMath.TileToWorld(b) + new Vector2(0, -34);
                    sb.Draw(_glow, w, null, new Color(255, 160, 60) * ((0.4f + 0.35f * night) * flicker), 0f, glowOrigin, 1.6f, SpriteEffects.None, 0f);
                }
            }
            else
            {
                Vector2 fire = IsoMath.TileToWorld(new Vector2(7.5f, 1.0f)) + new Vector2(0, -8);
                sb.Draw(_glow, fire, null, new Color(255, 140, 50) * ((0.35f + 0.4f * night) * flicker), 0f, glowOrigin, 1.5f, SpriteEffects.None, 0f);
                if (night > 0.05f)
                {
                    Vector2 lamp = IsoMath.TileToWorld(new Vector2(2.6f, 3.0f)) + new Vector2(0, -40);
                    sb.Draw(_glow, lamp, null, new Color(255, 200, 120) * (0.45f * night), 0f, glowOrigin, 3.2f, SpriteEffects.None, 0f);
                }
            }
            sb.End();

            // --- Ekran qatlami ---
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            Vector2 ToScreen(Vector2 tile, float lift) => Vector2.Transform(IsoMath.TileToWorld(tile) + new Vector2(0, -lift), m);

            Color roomLabel = new Color(240, 226, 196), roomBg = new Color(0, 0, 0, 110);
            if (_palace)
            {
                Label(sb, px, font, ToScreen(new Vector2(0.6f, 7.6f), -12), "TAXT ZALI", roomLabel, roomBg);
            }
            else if (IsHouse)
            {
                Label(sb, px, font, ToScreen(new Vector2(2.0f, 7.4f), -6), "YOTOQXONA", roomLabel, roomBg);
                Label(sb, px, font, ToScreen(new Vector2(7.7f, 6.6f), -14), "OSHXONA", roomLabel, roomBg);
            }
            else
            {
                Label(sb, px, font, ToScreen(new Vector2(2.0f, 7.4f), -6), _title, roomLabel, roomBg);
            }

            // Interaktiv obyektlar belgilari (yaqinlashganda nomi ko'rinadi)
            if (!IsHouse && !_palace)
            {
                foreach (InteriorHotspot h in _hotspots)
                {
                    Vector2 hp = ToScreen(h.Tile + new Vector2(h.Size * 0.5f), 8);
                    bool nearH = h == _nearHotspot;
                    Color dot = nearH ? new Color(255, 214, 90) : new Color(230, 200, 140) * 0.75f;
                    sb.Draw(px, new Rectangle((int)hp.X - 3, (int)hp.Y - 3, 6, 6), dot);
                    if (nearH) Label(sb, px, font, hp + new Vector2(0, -16), h.Label, new Color(255, 226, 150), new Color(0, 0, 0, 140));
                }
            }
            Label(sb, px, font, ToScreen(new Vector2((_doorX0 + _doorX1 + 1) / 2f, N + 0.5f), -10), "CHIQISH", new Color(255, 226, 150), new Color(0, 0, 0, 130));

            Resident near = NearestResident(1.5f);
            if (near == null && _palace) { Resident s = NearestResident(1.9f); if (s != null && s.IsShah) near = s; }
            foreach (Resident r in _residents)
            {
                float lift = (r.Scale < 1f ? 40 : 54) + r.Lift + (r.IsShah ? 8 : 0);
                Vector2 sp = ToScreen(r.Pos, lift);
                bool isNear = r == near;
                string name = isNear ? r.Name : r.Short;
                Color nameCol = r.IsShah ? new Color(255, 214, 90) : isNear ? new Color(255, 220, 120) : new Color(230, 220, 200);
                Label(sb, px, font, sp, name, nameCol, new Color(0, 0, 0, 140));
                // Suhbat pufagi: o'zaro gaplashayotgan odamlar (yaqin bo'lmasa ham), aks holda yaqindagining salomi
                string bubble = r.ChatTime > 0f && !string.IsNullOrEmpty(r.ChatText) ? r.ChatText : (isNear && !_fight ? r.Line : null);
                if (isNear && !string.IsNullOrEmpty(r.Info)) Label(sb, px, font, sp + new Vector2(0, 16), r.Info, new Color(200, 190, 170), new Color(0, 0, 0, 120));
                if (!string.IsNullOrEmpty(bubble)) Bubble(sb, px, font, sp + new Vector2(0, -26), bubble);
                if (isNear && r.IsShah && !_fight) Label(sb, px, font, sp + new Vector2(0, 34), "[F] SHOH BO'LIB SHAHARGA CHIQISH", new Color(255, 214, 90), new Color(60, 20, 10, 220));
            }

            // Non yopayotgan oila a'zosi — nima qilayotgani doim ko'rinadi
            if (_baker != null && !string.IsNullOrEmpty(_baker.Line))
                Bubble(sb, px, font, ToScreen(_baker.Pos, (_baker.Scale < 1f ? 40 : 54) + _baker.Lift) + new Vector2(0, -26), _baker.Line);
            if (IsHouse)
                Label(sb, px, font, ToScreen(new Vector2(6.0f, 4.0f), 44), "DASTURXON: " + Bread + " NON", Bread > 0 ? new Color(255, 226, 150) : new Color(255, 140, 120), new Color(0, 0, 0, 140));

            // Sarlavha
            Vector2 tsize = font.Measure(_title, 3);
            Vector2 ssize = font.Measure(_subtitle, 2);
            int headW = (int)MathF.Max(tsize.X, ssize.X) + 48;
            var head = new Rectangle(vw / 2 - headW / 2, 10, headW, 60);
            sb.Draw(px, new Rectangle(head.X - 2, head.Y - 2, head.Width + 4, head.Height + 4), new Color(216, 167, 83, 230));
            sb.Draw(px, head, new Color(28, 22, 18, 235));
            font.DrawCentered(sb, _title, new Vector2(head.Center.X, head.Y + 19), new Color(255, 214, 90), 3);
            font.DrawCentered(sb, _subtitle, new Vector2(head.Center.X, head.Y + 44), new Color(210, 196, 170), 2);

            // Ovqat bug'i va uyqu "Zzz"
            if (CurrentPose == Pose.Eat)
                for (int i = 0; i < 3; i++)
                {
                    float u = (_poseTime * 0.8f + i / 3f) % 1f;
                    Vector2 sp = ToScreen(_tableCenter, 18 + u * 30) + new Vector2((i - 1) * 8 + MathF.Sin(_poseTime * 3f + i) * 3f, 0);
                    sb.Draw(px, new Rectangle((int)sp.X, (int)sp.Y, 4, 4), new Color(255, 255, 255) * (0.6f * (1f - u)));
                }
            if (CurrentPose == Pose.Lie)
            {
                float u = _poseTime * 0.6f % 1f;
                Vector2 zp = ToScreen(_bedPos, 40 + u * 26) + new Vector2(10 + u * 12, 0);
                font.Draw(sb, "Z", zp, new Color(230, 236, 255) * (1f - u), 2);
                font.Draw(sb, "z", zp + new Vector2(-12, 14), new Color(230, 236, 255) * (0.7f * (1f - u)), 2);
            }

            // Harakat tugmalari (klaviatura: E / Q; sensorli ekranda bosiladi)
            _act1 = _act2 = Rectangle.Empty;
            ActionLabels(out _act1Label, out _act2Label);
            int abh = 34, ay = vh - 92;
            int w1 = _act1Label.Length > 0 ? (int)font.Measure(_act1Label, 2).X + 28 : 0;
            int w2 = _act2Label.Length > 0 ? (int)font.Measure(_act2Label, 2).X + 28 : 0;
            int ax = vw / 2 - (w1 + w2 + (w1 > 0 && w2 > 0 ? 10 : 0)) / 2;
            if (w1 > 0)
            {
                _act1 = new Rectangle(ax, ay, w1, abh);
                sb.Draw(px, new Rectangle(_act1.X - 2, _act1.Y - 2, _act1.Width + 4, _act1.Height + 4), new Color(216, 167, 83, 240));
                sb.Draw(px, _act1, new Color(56, 42, 22, 245));
                font.DrawCentered(sb, _act1Label, _act1.Center.ToVector2(), new Color(255, 214, 90), 2);
                ax += w1 + 10;
            }
            if (w2 > 0)
            {
                _act2 = new Rectangle(ax, ay, w2, abh);
                sb.Draw(px, new Rectangle(_act2.X - 2, _act2.Y - 2, _act2.Width + 4, _act2.Height + 4), new Color(150, 116, 66, 240));
                sb.Draw(px, _act2, new Color(36, 28, 22, 245));
                font.DrawCentered(sb, _act2Label, _act2.Center.ToVector2(), new Color(234, 224, 202), 2);
            }

            // ---- Sandiq paneli ----
            if (_chestOpen)
            {
                InteriorState st = InteriorStore.For(House.Id);
                int pw = Math.Min(420, vw - 24), ph = 210;
                var pr = new Rectangle(vw / 2 - pw / 2, vh / 2 - ph / 2, pw, ph);
                sb.Draw(px, new Rectangle(pr.X - 3, pr.Y - 3, pr.Width + 6, pr.Height + 6), new Color(216, 167, 83, 240));
                sb.Draw(px, pr, new Color(28, 22, 18, 248));
                font.DrawCentered(sb, "SANDIQ — " + _title, new Vector2(pr.Center.X, pr.Y + 18), new Color(255, 214, 90), 3);
                int ly = pr.Y + 46;
                font.Draw(sb, "Oziq-ovqat: ombor " + _state.Food + " / sandiq " + st.ChestFood, new Vector2(pr.X + 16, ly), new Color(236, 224, 200), 2); ly += 22;
                font.Draw(sb, "Urug': ombor " + _state.Farming.Seeds + " / sandiq " + st.ChestSeeds, new Vector2(pr.X + 16, ly), new Color(236, 224, 200), 2); ly += 22;
                font.Draw(sb, "Mahsulot: sandiqda " + st.ChestProduce, new Vector2(pr.X + 16, ly), new Color(236, 224, 200), 2); ly += 30;
                string[] acts = { "1  Oziq-ovqat qo'yish", "2  Oziq-ovqat olish", "3  Urug' qo'yish", "4  Urug' olish", "5  Mahsulotni olish" };
                int bw = (pw - 44) / 2, bh = 26;
                for (int i = 0; i < acts.Length; i++)
                {
                    int col = i % 2, row = i / 2;
                    _chestRects[i] = new Rectangle(pr.X + 16 + col * (bw + 12), ly + row * (bh + 6), bw, bh);
                    sb.Draw(px, _chestRects[i], new Color(56, 42, 26));
                    sb.Draw(px, new Rectangle(_chestRects[i].X, _chestRects[i].Y, _chestRects[i].Width, 1), new Color(150, 116, 66));
                    font.Draw(sb, acts[i], new Vector2(_chestRects[i].X + 6, _chestRects[i].Y + 6), new Color(234, 224, 202), 2);
                }
                _chestClose = new Rectangle(pr.Right - 96, pr.Bottom - 32, 80, 24);
                sb.Draw(px, _chestClose, new Color(60, 44, 30));
                font.DrawCentered(sb, "YOPISH", _chestClose.Center.ToVector2(), new Color(236, 220, 184), 2);
            }

            // ---- Minora kuzatish maydoni ----
            if (_upperPlatform)
            {
                int pw = Math.Min(430, vw - 24), ph = 150;
                var pr = new Rectangle(vw / 2 - pw / 2, 96, pw, ph);
                sb.Draw(px, new Rectangle(pr.X - 3, pr.Y - 3, pr.Width + 6, pr.Height + 6), new Color(216, 167, 83, 230));
                sb.Draw(px, pr, new Color(22, 26, 34, 242));
                font.DrawCentered(sb, "KUZATISH MAYDOCHASI", new Vector2(pr.Center.X, pr.Y + 18), new Color(255, 214, 90), 3);
                int ly = pr.Y + 48;
                font.Draw(sb, "Shahar binolari: " + _buildings.Buildings.Count, new Vector2(pr.X + 18, ly), new Color(226, 222, 210), 2); ly += 22;
                font.Draw(sb, "Aholi: " + _state.Population + "   Qo'shin: " + _state.Military.TotalUnits, new Vector2(pr.X + 18, ly), new Color(226, 222, 210), 2); ly += 22;
                font.Draw(sb, "Mudofaa kuchi: " + AndijonSaltanatlar.Defense.CityDefenseManager.Compute(_buildings, _state.Military.TotalStrength), new Vector2(pr.X + 18, ly), new Color(226, 222, 210), 2);
            }

            if (_fight) DrawFightOverlay(sb, px, font, m);
            string hint = _fight ? (TouchUi ? "Joystik - yurish  |  ZARBA - urish  |  eshik - chiqish" : "WASD yurish | SPACE / sichqoncha - zarba | SHIFT yugurish | eshik - chiqish")
                : TouchUi ? "Joystik - yurish  |  CHIQ yoki eshik - tashqariga chiqish"
                : _kind == InteriorKind.House || _palace
                ? "WASD yurish | SHIFT yugurish | ENTER / ESC yoki eshik - tashqariga chiqish"
                : "WASD yurish | E harakat | Q ikkinchi harakat | ESC - tashqariga chiqish";
            // Telefonda pastki burchaklar joystik/tugmalarga band — ko'rsatma tepada, tor ekranda qatorlarga bo'linadi
            bool topHint = TouchUi;
            var hintLines = AndijonSaltanatlar.UI.HudText.Wrap(font, hint, 2, vw - 40, 2);
            int hintTop = topHint ? 78 : vh - 12 - (PixelFont.GH * 2 + 4) * hintLines.Count - 8;
            Rectangle hr = AndijonSaltanatlar.UI.HudText.Box(sb, px, font, hintLines, 2, vw / 2f, hintTop, new Color(20, 16, 14, 210), new Color(220, 205, 175), 16, 4);

            if (_state.MessageTimer > 0f && !string.IsNullOrEmpty(_state.Message))
                AndijonSaltanatlar.UI.HudText.Box(sb, px, font, AndijonSaltanatlar.UI.HudText.Wrap(font, _state.Message, 2, vw - 40, 2), 2, vw / 2f, topHint ? hr.Bottom + 6 : 80,
                    new Color(0, 0, 0, 170), _state.MessageIsError ? new Color(255, 140, 120) : new Color(150, 230, 150), 6, 3);
            sb.End();
        }

        private Texture2D _pxCache;

        /// <summary>Dumaloq non (tandir noni): oltin-jigarrang, o'rtasi naqshli.</summary>
        private static void DrawNon(SpriteBatch sb, Texture2D px, Vector2 c)
        {
            if (px == null) return;
            Color crust = new Color(196, 132, 62), mid = new Color(226, 172, 96), dot = new Color(150, 96, 50);
            for (int y = -3; y <= 3; y++)
            {
                int hw = (int)MathF.Round(7f * MathF.Sqrt(1f - (y * y) / 12.5f));
                sb.Draw(px, new Rectangle((int)c.X - hw, (int)c.Y + y, hw * 2, 1), Math.Abs(y) >= 2 ? crust : mid);
            }
            sb.Draw(px, new Rectangle((int)c.X - 2, (int)c.Y - 1, 1, 1), dot);
            sb.Draw(px, new Rectangle((int)c.X + 1, (int)c.Y, 1, 1), dot);
            sb.Draw(px, new Rectangle((int)c.X - 1, (int)c.Y + 1, 1, 1), dot);
        }

        private static bool FacesLeft(Vector2 who, Vector2 target)
        {
            float sx = IsoMath.TileToWorld(target).X - IsoMath.TileToWorld(who).X;
            return sx < 0f;
        }

        private static void DrawPerson(SpriteBatch sb, Texture2D tex, Vector2 tile, bool flip, float scale, float lift)
        {
            Vector2 feet = IsoMath.TileToWorld(tile) + new Vector2(0, -lift);
            sb.Draw(tex, new Vector2(MathF.Round(feet.X), MathF.Round(feet.Y)), null, Color.White, 0f,
                new Vector2(tex.Width / 2f, tex.Height - AssetManager.FootOf(tex)), scale * AssetManager.ScaleOf(tex),
                flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }

        private static void Label(SpriteBatch sb, Texture2D px, PixelFont font, Vector2 center, string text, Color fg, Color bg)
        {
            Vector2 s = font.Measure(text, 2);
            var r = new Rectangle((int)(center.X - s.X / 2 - 5), (int)(center.Y - s.Y / 2 - 3), (int)s.X + 10, (int)s.Y + 6);
            sb.Draw(px, r, bg);
            font.Draw(sb, text, new Vector2(r.X + 5, r.Y + 3), fg, 2);
        }

        private static void Bubble(SpriteBatch sb, Texture2D px, PixelFont font, Vector2 bottomCenter, string text)
        {
            Vector2 s = font.Measure(text, 2);
            var r = new Rectangle((int)(bottomCenter.X - s.X / 2 - 8), (int)(bottomCenter.Y - s.Y - 12), (int)s.X + 16, (int)s.Y + 12);
            sb.Draw(px, new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), new Color(60, 44, 30));
            sb.Draw(px, r, new Color(252, 246, 232));
            sb.Draw(px, new Rectangle((int)bottomCenter.X - 3, r.Bottom, 6, 5), new Color(252, 246, 232));
            font.Draw(sb, text, new Vector2(r.X + 8, r.Y + 6), new Color(50, 36, 26), 2);
        }

        /// <summary>Shahar ko'rinishida: bino yonida turganda "ENTER — kirish" ko'rsatmasi.</summary>
        public void DrawPrompt(SpriteBatch sb, Texture2D px, PixelFont font, CameraManager cam, int vw, int vh)
        {
            if (Active || NearbyHouse == null) return;
            Building h = NearbyHouse;
            Vector2 top = cam.WorldToScreen(IsoMath.TileToWorld(new Vector2(h.Position.X + h.Size.X, h.Position.Y + h.Size.Y)) + new Vector2(0, -h.Texture.Height - 6));
            bool denied = !MayEnter(h, _nearbyWho);
            bool openAir = h.Type == BuildingType.Garden || h.Type == BuildingType.Farm
                || h.Type == BuildingType.Barracks || h.Type == BuildingType.GladiatorArena
                || h.Type == BuildingType.Caravanserai || h.Type == BuildingType.Gate;
            string t = denied ? "SAROYGA FAQAT SOQCHILAR KIRADI"
                : h.Type == BuildingType.MainHall ? "[ENTER] SAROYGA KIRISH"
                : openAir ? "[ENTER] " + h.Name.ToUpperInvariant() + " — HUDUDGA KIRISH"
                : "[ENTER] " + h.Name.ToUpperInvariant() + " — ICHKARIGA KIRISH";
            Vector2 s = font.Measure(t, 2);
            var r = new Rectangle((int)(top.X - s.X / 2 - 8), (int)(top.Y - s.Y - 8), (int)s.X + 16, (int)s.Y + 10);
            r.X = Math.Clamp(r.X, 8, vw - r.Width - 8);
            r.Y = Math.Clamp(r.Y, 44, vh - r.Height - 140);
            sb.Draw(px, new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), new Color(216, 167, 83, 230));
            sb.Draw(px, r, new Color(28, 22, 18, 235));
            font.DrawCentered(sb, t, r.Center.ToVector2(), denied ? new Color(255, 130, 110) : new Color(255, 214, 90), 2);
        }
    }

    internal static class CanvasOriginExt
    {
        public static (Canvas, Vector2) WithOrigin(this Canvas c, Vector2 origin) => (c, origin);
    }
}
