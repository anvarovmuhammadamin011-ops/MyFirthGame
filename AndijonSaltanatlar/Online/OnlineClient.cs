using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using AndijonSaltanatlar.Shared.DTOs;
using Microsoft.AspNetCore.SignalR.Client;

namespace AndijonSaltanatlar.Online
{
    /// <summary>Server manzili va login (parol saqlanmaydi).</summary>
    public sealed class OnlineSettings
    {
        /// <summary>
        /// Standart server. Windows'da server odatda shu kompyuterda — "localhost" Wi-Fi manzili o'zgarsa ham ishlaydi.
        /// Android telefonda "localhost" ISHLATILMAYDI (telefonning o'zi bo'lib qoladi) — kompyuterning Wi-Fi manzili
        /// qo'lda kiritiladi (u tarmoq qayta ulanganda o'zgarishi mumkin). Internet uchun https://... manzil kiritiladi.
        /// </summary>
#if ANDROID
        public string ServerUrl { get; set; } = "";
#else
        public string ServerUrl { get; set; } = "http://localhost:5080";
#endif
        public string Username { get; set; } = "";
        public int WorldId { get; set; } = 1;

        private static string FilePath => AndijonSaltanatlar.Core.AppPaths.File("online.json");

        public static OnlineSettings Load()
        {
            try
            {
                if (File.Exists(FilePath)) return JsonSerializer.Deserialize<OnlineSettings>(File.ReadAllText(FilePath)) ?? new OnlineSettings();
            }
            catch { }
            return new OnlineSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
            }
            catch { }
        }
    }

    /// <summary>
    /// O'yin clienti ↔ server aloqasi (Windows va Android uchun bir xil kod).
    ///  - REST (HttpClient): login, dunyoga qo'shilish, buyruqlar. Har bir buyruqning CommandId'si bor —
    ///    tarmoq uzilib qayta yuborilsa ham server uni ikki marta bajarmaydi.
    ///  - SignalR (/gamehub): bozor o'zgardi, guruchingiz sotildi, o'yinchi kirdi — xabarlari.
    /// Tarmoq ishlari fonda bajariladi; natijalar asosiy (o'yin) oqimiga navbat orqali o'tadi (<see cref="Pump"/>),
    /// shuning uchun render sikli hech qachon kutib qolmaydi.
    /// Client hech narsani o'zi hisoblamaydi — ko'rsatiladigan holat faqat server javobidan olinadi.
    /// </summary>
    public sealed class OnlineClient : IDisposable
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
        private HttpClient _http;
        private HubConnection _hub;
        private string _token;
        private DateTime _stateReceivedUtc;
        private bool _refreshQueued;

        public OnlineSettings Settings { get; } = OnlineSettings.Load();
        public OnlineStateDto State { get; private set; }
        public List<MarketOfferDto> Offers { get; private set; } = new List<MarketOfferDto>();
        public List<TradeRecordDto> History { get; private set; } = new List<TradeRecordDto>();
        /// <summary>PROMPT 19B: saltanat boshqaruvi (shaharlar, hududlar, munosabatlar, Vazir eslatmalari).</summary>
        public KingdomOverviewDto Kingdom { get; private set; }
        /// <summary>PROMPT 20: ittifoq, takliflar, kelishuvlar, dunyo yangiliklari.</summary>
        public AllianceDto Alliance { get; private set; }
        public List<AllianceInvitationDto> Invitations { get; private set; } = new List<AllianceInvitationDto>();
        public List<DiplomaticAgreementDto> Agreements { get; private set; } = new List<DiplomaticAgreementDto>();
        public List<WorldNewsDto> News { get; private set; } = new List<WorldNewsDto>();
        public List<AllianceDto> AlliancesInWorld { get; private set; } = new List<AllianceDto>();
        /// <summary>PROMPT 20: faqat o'z ittifoqi a'zolari ko'radigan bozor takliflari.</summary>
        public List<MarketOfferDto> AllianceOffers { get; private set; } = new List<MarketOfferDto>();
        /// <summary>Har yangi hudud transferi xabarida oshadi (xarita animatsiyasi uchun).</summary>
        public int TerritorySerial { get; private set; }
        public int LastTransferredRegionId { get; private set; }

        /// <summary>PROMPT 25: harbiy boshqaruv holati (qo'shinlar, sarkardalar, janglar, PvP takliflari).</summary>
        public WarOverviewDto War { get; private set; }
        /// <summary>Hozir ko'rsatilayotgan jang (server holati). Jang sahnasi shuni chizadi.</summary>
        public BattleDto Battle { get; private set; }
        /// <summary>Jang sahnasi ochilishi kerak bo'lgan jang (yangi jang boshlanganda / taklif qabul qilinganda).</summary>
        public int OpenBattleRequest { get; set; }
        /// <summary>Har jang holati yangilanganda oshadi (sahna animatsiyasi uchun).</summary>
        public int BattleSerial { get; private set; }
        public bool WarBusy { get; private set; }

        public bool Busy { get; private set; }
        public bool LoggedIn => _token != null && State != null;
        public bool HubConnected => _hub != null && _hub.State == HubConnectionState.Connected;
        public string Message { get; private set; } = "";
        public bool MessageIsError { get; private set; }
        /// <summary>Har yangi xabarda oshadi (UI toast'ni qayta ko'rsatishi uchun).</summary>
        public int MessageSerial { get; private set; }
        /// <summary>Real vaqt xabari (masalan "Guruchingiz sotildi") — UI bir marta ko'rsatadi va null qiladi.</summary>
        public string Notification { get; set; }

        /// <summary>Oxirgi server javobidan beri o'tgan vaqtni hisobga olgan taxminiy server o'yin daqiqasi (faqat ko'rsatish uchun).</summary>
        public long EstimatedGameMinute
            => State == null ? 0 : State.ServerGameMinute + (long)((DateTime.UtcNow - _stateReceivedUtc).TotalSeconds * Shared.Farming.RiceFarmingRules.GameMinutesPerRealSecond);

        /// <summary>Asosiy oqimda har kadr chaqiriladi: fon vazifalari natijalarini qo'llaydi.</summary>
        public void Pump()
        {
            while (_main.TryDequeue(out Action a))
            {
                try { a(); } catch { }
            }
        }

        private void Post(Action a) => _main.Enqueue(a);

        private void SetMessage(string text, bool error)
        {
            Post(() => { Message = text ?? ""; MessageIsError = error; MessageSerial++; });
        }

        /// <summary>Fon vazifasi: istisno o'yinni to'xtatmaydi, foydalanuvchiga tushunarli xabar bo'ladi.</summary>
        private void Run(Func<Task> work)
        {
            if (Busy) return;
            Busy = true;
            Task.Run(async () =>
            {
                try { await work(); }
                catch (HttpRequestException ex) { SetMessage("Serverga ulanib bo'lmadi: " + Short(ex.Message), true); }
                catch (TaskCanceledException) { SetMessage("Server javob bermadi (vaqt tugadi)", true); }
                catch (Exception ex) { SetMessage("Xato: " + Short(ex.Message), true); }
                finally { Post(() => Busy = false); }
            });
        }

        private static string Short(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 70 ? s.Substring(0, 70) : s);

        private static string NormalizeUrl(string url) => CloudSession.NormalizeUrl(url);

        // ------------------------------------------------------------------
        // Login va dunyoga qo'shilish
        // ------------------------------------------------------------------
        public void Login(string serverUrl, string username, string password, bool register)
        {
            string url = NormalizeUrl(serverUrl);
            string user = (username ?? "").Trim();
            if (url.Length == 0 || user.Length == 0 || string.IsNullOrEmpty(password))
            {
                SetMessage("Server, login va parolni kiriting", true);
                return;
            }
            Run(async () =>
            {
                await DisconnectAsync();
                _http = new HttpClient { BaseAddress = new Uri(url + "/"), Timeout = TimeSpan.FromSeconds(12) };

                HttpResponseMessage resp = register
                    ? await _http.PostAsJsonAsync("api/auth/register", new RegisterRequest { Username = user, Email = user.ToLowerInvariant() + "@andijon.local", Password = password })
                    : await _http.PostAsJsonAsync("api/auth/login", new LoginRequest { Email = user, Password = password });
                if (!resp.IsSuccessStatusCode)
                {
                    SetMessage(await ErrorText(resp, register ? "Ro'yxatdan o'tib bo'lmadi" : "Login yoki parol noto'g'ri"), true);
                    return;
                }
                AuthResponse auth = await resp.Content.ReadFromJsonAsync<AuthResponse>(Json);
                _token = auth.Token;
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

                HttpResponseMessage join = await _http.PostAsJsonAsync("api/online/join", new JoinWorldRequest { WorldId = Settings.WorldId, ChooseStart = true });
                if (!join.IsSuccessStatusCode) { SetMessage(await ErrorText(join, "Dunyoga qo'shilib bo'lmadi"), true); return; }
                OnlineStateDto state = await join.Content.ReadFromJsonAsync<OnlineStateDto>(Json);
                await LoadMarketAsync();
                Post(() =>
                {
                    ApplyState(state);
                    Settings.ServerUrl = url;
                    Settings.Username = user;
                    Settings.Save();
                });
                SetMessage("Xush kelibsiz, " + auth.Username + "! Dunyo: " + state.WorldName, false);
                await ConnectHubAsync(url, state.WorldId);
            });
        }

        /// <summary>
        /// Google orqali olingan token bilan onlayn dunyoga ulanadi (parol so'ralmaydi).
        /// CloudSession kirgandan keyin Game1 shu metodni chaqiradi.
        /// </summary>
        /// <summary>Kirish tokeni yangilandi (sessiya rotation) — keyingi so'rovlar va SignalR yangi tokenni ishlatadi.</summary>
        public void UpdateToken(string token)
        {
            if (string.IsNullOrEmpty(token) || _token == null) return;
            _token = token;
            if (_http != null) _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }

        public void LoginWithToken(string serverUrl, string token, string username)
        {
            string url = NormalizeUrl(serverUrl);
            if (url.Length == 0 || string.IsNullOrEmpty(token)) return;
            Run(async () =>
            {
                await DisconnectAsync();
                _http = new HttpClient { BaseAddress = new Uri(url + "/"), Timeout = TimeSpan.FromSeconds(12) };
                _token = token;
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

                HttpResponseMessage join = await _http.PostAsJsonAsync("api/online/join", new JoinWorldRequest { WorldId = Settings.WorldId, ChooseStart = true });
                if (!join.IsSuccessStatusCode) { SetMessage(await ErrorText(join, "Dunyoga qo'shilib bo'lmadi"), true); return; }
                OnlineStateDto state = await join.Content.ReadFromJsonAsync<OnlineStateDto>(Json);
                await LoadMarketAsync();
                Post(() =>
                {
                    ApplyState(state);
                    Settings.ServerUrl = url;
                    Settings.Username = username ?? "";
                    Settings.Save();
                });
                await ConnectHubAsync(url, state.WorldId);
            });
        }

        private async Task ConnectHubAsync(string url, int worldId)
        {
            try
            {
                _hub = new HubConnectionBuilder()
                    .WithUrl(url + "/gamehub", o => o.AccessTokenProvider = () => Task.FromResult(_token))
                    .WithAutomaticReconnect()
                    .Build();
                _hub.On<int>("MarketChanged", _ => QueueRefresh());
                _hub.On<int>("PlayersChanged", _ => QueueRefresh());
                _hub.On<int, string>("StateChanged", (_, msg) =>
                {
                    if (!string.IsNullOrEmpty(msg)) Post(() => Notification = msg);
                    QueueRefresh();
                });
                _hub.On<int, int, int, int, string>("TerritoryTransferred", (_, regionId, __, ___, msg) =>
                {
                    Post(() => { Notification = msg; LastTransferredRegionId = regionId; TerritorySerial++; });
                    QueueRefresh();
                });
                _hub.On<int, string, int>("AllianceInvitationReceived", (_, fromName, __) =>
                {
                    Post(() => Notification = fromName + " sizni ittifoqqa taklif qildi");
                    QueueRefresh();
                });
                _hub.On<int, int, string>("AllianceMemberJoined", (_, __, name) =>
                {
                    Post(() => Notification = name + " ittifoqqa qo'shildi");
                    QueueRefresh();
                });
                _hub.On<int, string, int>("DiplomaticAgreementCreated", (_, fromName, __) =>
                {
                    Post(() => Notification = fromName + " kelishuv taklifi yubordi");
                    QueueRefresh();
                });
                _hub.On<int, string>("WorldNewsPublished", (_, text) =>
                {
                    Post(() => Notification = text);
                    QueueRefresh();
                });
                _hub.On<int, int>("AllianceProjectUpdated", (_, __) => QueueRefresh());
                // Dunyo umumiy chati
                _hub.On<ChatMessageDto>("ChatMessage", m => Post(() =>
                {
                    if (m == null) return;
                    ChatLog.Add(m);
                    if (ChatLog.Count > 60) ChatLog.RemoveRange(0, ChatLog.Count - 60);
                    ChatSerial++;
                    if (!ChatOpen) UnreadChat++;
                }));
                _hub.On<ChatMessageDto[]>("ChatHistory", list => Post(() =>
                {
                    ChatLog = new List<ChatMessageDto>(list ?? Array.Empty<ChatMessageDto>());
                    ChatSerial++;
                }));
                // PROMPT 25: urush hodisalari — faqat o'zimizga tegishlilari ko'rsatiladi, holat serverdan qayta o'qiladi.
                _hub.On<int, int, int, string>("BattleInvitationCreated", (_, from, to, fromName) =>
                {
                    if (War != null && to == War.KingdomId) Post(() => Notification = fromName + " sizni PvP jangiga taklif qildi");
                    LoadWar();
                });
                _hub.On<int, int, int>("BattleInvitationDeclined", (_, from, __) => { if (War != null && from == War.KingdomId) Post(() => Notification = "PvP taklifingiz rad etildi"); LoadWar(); });
                _hub.On<int, int, int, int>("BattleInvitationAccepted", (_, battleId, from, __) =>
                {
                    if (War != null && from == War.KingdomId) Post(() => { Notification = "PvP taklifingiz qabul qilindi - jang boshlandi!"; OpenBattleRequest = battleId; });
                    LoadWar();
                });
                _hub.On<int, int, int>("BattleStarted", (battleId, a, d) => { if (War != null && (a == War.KingdomId || d == War.KingdomId)) Post(() => OpenBattleRequest = battleId); LoadWar(); });
                _hub.On<int, int, int>("BattleDecisionSubmitted", (battleId, _, __) => { if (Battle != null && Battle.Id == battleId) LoadBattle(battleId); });
                _hub.On<int, int, bool>("BattleRoundResolved", (battleId, _, __) => { if (Battle != null && Battle.Id == battleId) LoadBattle(battleId); });
                _hub.On<int, int, bool>("BattleCompleted", (battleId, _, __) => { if (Battle != null && Battle.Id == battleId) LoadBattle(battleId); LoadWar(); });
                _hub.On<int, int, int>("TerritoryOwnershipUpdated", (_, __, ___) => QueueRefresh());
                _hub.On<int, int>("PeaceAgreed", (_, __) => LoadWar());
                _hub.On<int, int, int, string, int, string>("ArmyMarching", (_, attacker, defender, region, seconds, attackerName) =>
                {
                    if (War != null && defender == War.KingdomId) Post(() => Notification = "DIQQAT! " + attackerName + " qo'shini " + region + " qasringizga yurish boshladi — " + seconds + " soniyada yetib keladi");
                    LoadWar();
                });
                _hub.On<int, string>("WarNotice", (kid, msg) => { if (War != null && kid == War.KingdomId) Post(() => Notification = msg); LoadWar(); });
                _hub.Reconnected += async _ => { await _hub.InvokeAsync("JoinWorld", worldId); QueueRefresh(); };
                await _hub.StartAsync();
                await _hub.InvokeAsync("JoinWorld", worldId);
                QueueRefresh();
            }
            catch (Exception ex)
            {
                // Real vaqt kanali bo'lmasa ham o'yin ishlaydi — holat davriy so'rov bilan yangilanadi.
                SetMessage("Real vaqt aloqasi o'rnatilmadi: " + Short(ex.Message), true);
            }
        }

        private static async Task<string> ErrorText(HttpResponseMessage resp, string fallback)
        {
            try
            {
                string body = await resp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("messageUz", out JsonElement m)) return m.GetString();
            }
            catch { }
            return fallback + " (" + (int)resp.StatusCode + ")";
        }

        public void Logout()
        {
            Task.Run(DisconnectAsync);
            _token = null;
            State = null;
            Offers = new List<MarketOfferDto>();
            History = new List<TradeRecordDto>();
            SetMessage("Chiqildi", false);
        }

        private async Task DisconnectAsync()
        {
            try { if (_hub != null) await _hub.DisposeAsync(); } catch { }
            _hub = null;
            _http?.Dispose();
            _http = null;
        }

        // ------------------------------------------------------------------
        // Holatni yangilash
        // ------------------------------------------------------------------
        private void ApplyState(OnlineStateDto s)
        {
            if (s == null) return;
            State = s;
            _stateReceivedUtc = DateTime.UtcNow;
        }

        /// <summary>Real vaqt xabari kelganda holat va bozorni qayta oladi (bir vaqtda bitta so'rov).</summary>
        public void QueueRefresh()
        {
            if (_refreshQueued || _http == null) return;
            _refreshQueued = true;
            Task.Run(async () =>
            {
                try
                {
                    OnlineStateDto s = await _http.GetFromJsonAsync<OnlineStateDto>("api/online/state", Json);
                    await LoadMarketAsync();
                    try { WarOverviewDto w = await _http.GetFromJsonAsync<WarOverviewDto>("api/war/overview", WarJson); Post(() => War = w); } catch { }
                    Post(() => ApplyState(s));
                }
                catch { }
                finally { _refreshQueued = false; }
            });
        }

        private async Task LoadMarketAsync()
        {
            List<MarketOfferDto> offers = await _http.GetFromJsonAsync<List<MarketOfferDto>>("api/online/market", Json);
            List<TradeRecordDto> history = await _http.GetFromJsonAsync<List<TradeRecordDto>>("api/online/market/history", Json);
            KingdomOverviewDto kingdom = null;
            try { kingdom = await _http.GetFromJsonAsync<KingdomOverviewDto>("api/kingdom/overview", Json); } catch { }
            await LoadDiplomacyAsync();
            Post(() =>
            {
                Offers = offers ?? new List<MarketOfferDto>();
                History = history ?? new List<TradeRecordDto>();
                if (kingdom != null) Kingdom = kingdom;
            });
        }

        private async Task LoadDiplomacyAsync()
        {
            // Har bir bo'lim alohida yuklanadi: bittasi muvaffaqiyatsiz bo'lsa ham qolganlari (masalan takliflar) ko'rinadi.
            // Ittifoqi yo'q o'yinchi uchun server 204 (bo'sh) qaytaradi — bu xato emas, "ittifoq yo'q" degani.
            AllianceDto alliance = null;
            bool allianceOk = false;
            try
            {
                HttpResponseMessage r = await _http.GetAsync("api/diplomacy/alliance/mine");
                if (r.IsSuccessStatusCode)
                {
                    allianceOk = true;
                    if (r.StatusCode != System.Net.HttpStatusCode.NoContent && (r.Content.Headers.ContentLength ?? 1) > 0)
                        alliance = await r.Content.ReadFromJsonAsync<AllianceDto>(Json);
                }
            }
            catch { }
            List<AllianceInvitationDto> invs = await TryGet<List<AllianceInvitationDto>>("api/diplomacy/alliance/invitations");
            List<DiplomaticAgreementDto> ags = await TryGet<List<DiplomaticAgreementDto>>("api/diplomacy/agreement/list");
            List<WorldNewsDto> news = await TryGet<List<WorldNewsDto>>("api/diplomacy/news?take=20");
            List<AllianceDto> alls = await TryGet<List<AllianceDto>>("api/diplomacy/alliance/list");
            List<MarketOfferDto> allianceOffers = alliance != null ? await TryGet<List<MarketOfferDto>>("api/diplomacy/market/offers/" + alliance.Id) : new List<MarketOfferDto>();
            Post(() =>
            {
                if (allianceOk) { Alliance = alliance; AllianceOffers = allianceOffers ?? new List<MarketOfferDto>(); }
                if (invs != null) Invitations = invs;
                if (ags != null) Agreements = ags;
                if (news != null) News = news;
                if (alls != null) AlliancesInWorld = alls;
            });
        }

        private async Task<T> TryGet<T>(string path) where T : class
        {
            try { return await _http.GetFromJsonAsync<T>(path, Json); }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        // Buyruqlar (har biri o'z CommandId'si bilan; tarmoq xatosida bir marta xuddi shu Id bilan qayta yuboriladi)
        // ------------------------------------------------------------------
        private void Command(string path, Func<string, object> body)
        {
            if (!LoggedIn) { SetMessage("Avval serverga kiring", true); return; }
            string commandId = Guid.NewGuid().ToString("N");
            Run(async () =>
            {
                HttpResponseMessage resp;
                try { resp = await _http.PostAsJsonAsync(path, body(commandId)); }
                catch (HttpRequestException) { resp = await _http.PostAsJsonAsync(path, body(commandId)); } // xavfsiz takror (idempotent)
                if (!resp.IsSuccessStatusCode) { SetMessage(await ErrorText(resp, "Buyruq bajarilmadi"), true); return; }
                ActionResultDto r = await resp.Content.ReadFromJsonAsync<ActionResultDto>(Json);
                await LoadMarketAsync();
                Post(() => ApplyState(r.State));
                SetMessage(r.MessageUz, !r.Success);
            });
        }

        // ---- PROMPT 25: urush tizimi ----
        private static readonly JsonSerializerOptions WarJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        /// <summary>Harbiy holatni serverdan yuklaydi (fonda).</summary>
        public void LoadWar()
        {
            if (_http == null) return;
            Task.Run(async () =>
            {
                try
                {
                    WarOverviewDto w = await _http.GetFromJsonAsync<WarOverviewDto>("api/war/overview", WarJson);
                    Post(() =>
                    {
                        War = w;
                        if (w?.ActiveBattle != null && (Battle == null || Battle.Id == w.ActiveBattle.Id)) { Battle = w.ActiveBattle; BattleSerial++; }
                    });
                }
                catch { }
            });
        }

        public void LoadBattle(int battleId)
        {
            if (_http == null) return;
            Task.Run(async () =>
            {
                try
                {
                    BattleDto b = await _http.GetFromJsonAsync<BattleDto>("api/war/battle/" + battleId, WarJson);
                    Post(() => { if (b != null) { Battle = b; BattleSerial++; } });
                }
                catch { }
            });
        }

        public void CloseBattle() => Battle = null;

        private void WarCommand(string path, Func<string, object> body) => WarCommand(path, body, null);

        /// <param name="done">Natija (asosiy oqimda): muvaffaqiyat yoki yo'q.</param>
        private void WarCommand(string path, Func<string, object> body, Action<bool> done)
        {
            if (!LoggedIn) { SetMessage("Avval serverga kiring", true); return; }
            if (WarBusy) return;
            WarBusy = true;
            string commandId = Guid.NewGuid().ToString("N");
            Task.Run(async () =>
            {
                try
                {
                    HttpResponseMessage resp;
                    try { resp = await _http.PostAsJsonAsync(path, body(commandId), WarJson); }
                    catch (HttpRequestException) { resp = await _http.PostAsJsonAsync(path, body(commandId), WarJson); } // xavfsiz takror (idempotent)
                    if (!resp.IsSuccessStatusCode) { SetMessage(await ErrorText(resp, "Buyruq bajarilmadi"), true); if (done != null) Post(() => done(false)); return; }
                    WarActionResultDto r = await resp.Content.ReadFromJsonAsync<WarActionResultDto>(WarJson);
                    BattleDto battle = null;
                    if (r.Success && r.BattleId > 0)
                    {
                        try { battle = await _http.GetFromJsonAsync<BattleDto>("api/war/battle/" + r.BattleId, WarJson); } catch { }
                    }
                    Post(() =>
                    {
                        if (r.Overview != null) War = r.Overview;
                        if (battle != null) { Battle = battle; BattleSerial++; if (path.EndsWith("battle/start") || path.EndsWith("pvp/respond")) OpenBattleRequest = battle.Id; }
                        done?.Invoke(r.Success);
                    });
                    SetMessage(r.MessageUz, !r.Success);
                }
                catch (Exception ex) { SetMessage("Xato: " + Short(ex.Message), true); if (done != null) Post(() => done(false)); }
                finally { Post(() => WarBusy = false); }
            });
        }

        public void Recruit(int armyId, Shared.War.UnitLine line, int count) => WarCommand("api/war/recruit", id => new RecruitRequest { CommandId = id, ArmyId = armyId, Line = line, Count = count });
        public void UpgradeUnits(int armyId, Shared.War.UnitLine line, int fromLevel, int count) => WarCommand("api/war/upgrade", id => new UpgradeUnitsRequest { CommandId = id, ArmyId = armyId, Line = line, FromLevel = fromLevel, Count = count });
        public void BuildFacility(Shared.War.FacilityKind kind) => WarCommand("api/war/facility", id => new FacilityRequest { CommandId = id, Kind = kind });
        public void HireCommander() => WarCommand("api/war/commander/hire", id => new CommanderRequest { CommandId = id });
        public void AssignCommander(int commanderId, int armyId) => WarCommand("api/war/commander/assign", id => new CommanderRequest { CommandId = id, CommanderId = commanderId, ArmyId = armyId });
        public void CreateArmy(string name) => WarCommand("api/war/army/create", id => new CreateArmyRequest { CommandId = id, Name = name });
        public void StartBattle(int armyId, int regionId) => WarCommand("api/war/battle/start", id => new StartBattleRequest { CommandId = id, ArmyId = armyId, RegionId = regionId });
        public void SubmitDecision(int battleId, int round, Shared.War.BattleDecision d) => WarCommand("api/war/decision", id => new BattleDecisionRequest { CommandId = id, BattleId = battleId, Round = round, Decision = d });
        /// <summary>Qamal: shahardagi jangchilar bilan qasrga yurish (server ro'yxatga oladi, jang mijozda).</summary>
        public void SiegeStart(int regionId, Action<bool> done) => WarCommand("api/war/siege/start", id => new SiegeStartRequest { CommandId = id, RegionId = regionId }, done);
        /// <summary>Qamal natijasi: g'alabada hudud (poytaxt bo'lmasa) saltanatga o'tadi.</summary>
        public void SiegeFinish(SiegeFinishRequest req) { WarBusy = false; WarCommand("api/war/siege/finish", id => { req.CommandId = id; return req; }, ok => LoadWar()); }
        public void March(int armyId, int regionId) => WarCommand("api/war/march", id => new MarchRequest { CommandId = id, ArmyId = armyId, RegionId = regionId });
        public void ProposePeace(int kingdomId) => WarCommand("api/war/peace", id => new PeaceRequest { CommandId = id, KingdomId = kingdomId });
        public void InvitePvp(int toKingdomId, int armyId, int stakeRegionId) => WarCommand("api/war/pvp/invite", id => new PvpInviteRequest { CommandId = id, ToKingdomId = toKingdomId, ArmyId = armyId, StakeRegionId = stakeRegionId });
        public void RespondPvp(int invitationId, bool accept, int armyId) => WarCommand("api/war/pvp/respond", id => new PvpRespondRequest { CommandId = id, InvitationId = invitationId, Accept = accept, ArmyId = armyId });
        public void SetPvp(bool accepts) => WarCommand("api/war/pvp/setting", id => new PvpSettingRequest { CommandId = id, AcceptsPvp = accepts });

        // ---- Boshlang'ich yurt (xaritadan tanlash) va bayroq ----
        /// <summary>Yurt tanlash xaritasi (barcha hududlar, bo'sh/band holati bilan).</summary>
        public StartRegionsDto StartRegions { get; private set; }

        public void LoadStartRegions()
        {
            if (_http == null) return;
            Task.Run(async () =>
            {
                try
                {
                    StartRegionsDto d = await _http.GetFromJsonAsync<StartRegionsDto>("api/online/start-regions", Json);
                    Post(() => { if (d != null) StartRegions = d; });
                }
                catch { }
            });
        }

        /// <summary>Tanlangan mustaqil hududda saltanat qurish (nom va bayroq bilan).</summary>
        public void ChooseStart(int regionId, string kingdomName, string flag)
            => StateCommand("api/online/start", new ChooseStartRequest { CommandId = Guid.NewGuid().ToString("N"), RegionId = regionId, KingdomName = kingdomName ?? "", Flag = flag ?? "" },
                "Saltanatingiz qurildi! Xush kelibsiz.", reloadStarts: true);

        /// <summary>Bayroqni o'zgartirish.</summary>
        public void SetFlag(string flag)
            => StateCommand("api/online/flag", new SetFlagRequest { CommandId = Guid.NewGuid().ToString("N"), Flag = flag ?? "" }, "Bayroq saqlandi.", reloadStarts: false);

        private void StateCommand(string path, object body, string okMessage, bool reloadStarts)
        {
            if (!LoggedIn) { SetMessage("Avval serverga kiring", true); return; }
            Run(async () =>
            {
                HttpResponseMessage resp = await _http.PostAsJsonAsync(path, body);
                if (!resp.IsSuccessStatusCode)
                {
                    SetMessage(await ErrorText(resp, "Bajarilmadi"), true);
                    if (reloadStarts) LoadStartRegions();
                    return;
                }
                OnlineStateDto st = await resp.Content.ReadFromJsonAsync<OnlineStateDto>(Json);
                await LoadMarketAsync();
                try { WarOverviewDto w = await _http.GetFromJsonAsync<WarOverviewDto>("api/war/overview", WarJson); Post(() => War = w); } catch { }
                Post(() => ApplyState(st));
                SetMessage(okMessage, false);
            });
        }

        public void BuildField() => Command("api/online/fields/build", id => new FieldActionRequest { CommandId = id });
        public void BuildWater() => Command("api/online/water/build", id => new FieldActionRequest { CommandId = id });
        public void Plant(int fieldId) => Command("api/online/fields/plant", id => new FieldActionRequest { CommandId = id, FieldId = fieldId });
        public void Harvest(int fieldId) => Command("api/online/fields/harvest", id => new FieldActionRequest { CommandId = id, FieldId = fieldId });
        public void CreateOffer(int quantity, int totalPrice) => CreateOffer("Rice", quantity, totalPrice);
        public void CreateOffer(string product, int quantity, int totalPrice) => Command("api/online/market/offers", id => new CreateOfferRequest { CommandId = id, Product = product, Quantity = quantity, TotalPrice = totalPrice });
        public void PlantCrop(int fieldId, string crop) => Command("api/online/fields/plant-crop", id => new PlantCropRequest { CommandId = id, FieldId = fieldId, Crop = crop });

        // ---- PROMPT 19B: saltanat boshqaruvi ----
        public void SendEnvoy(int regionId) => Command("api/kingdom/envoy", id => new RegionActionRequest { CommandId = id, RegionId = regionId });
        public void Annex(int regionId) => Command("api/kingdom/annex", id => new RegionActionRequest { CommandId = id, RegionId = regionId });
        public void SetGovernorMode(int cityId, GovernorMode mode) => Command("api/kingdom/governor/mode", id => new GovernorRequest { CommandId = id, CityId = cityId, Mode = mode });
        public void GovernorAnalyze(int cityId) => Command("api/kingdom/governor/analyze", id => new GovernorRequest { CommandId = id, CityId = cityId });
        public void ApprovePlan(int planId) => Command("api/kingdom/governor/approve", id => new GovernorRequest { CommandId = id, PlanId = planId });
        public void RejectPlan(int planId) => Command("api/kingdom/governor/reject", id => new GovernorRequest { CommandId = id, PlanId = planId });
        public void Buy(int offerId) => Command("api/online/market/buy", id => new BuyOfferRequest { CommandId = id, OfferId = offerId });
        public void Cancel(int offerId) => Command("api/online/market/cancel", id => new BuyOfferRequest { CommandId = id, OfferId = offerId });

        // ---- PROMPT 20: ittifoqlar, kelishuvlar ----
        public void CreateAlliance(string name, int emblem) => Command("api/diplomacy/alliance/create", id => new CreateAllianceRequest { CommandId = id, Name = name, Emblem = emblem });
        public void InviteAlliance(int allianceId, int toKingdomId) => Command("api/diplomacy/alliance/invite", id => new InviteToAllianceRequest { CommandId = id, AllianceId = allianceId, ToKingdomId = toKingdomId });
        public void RespondInvitation(int invitationId, bool accept) => Command("api/diplomacy/alliance/respond", id => new RespondInvitationRequest { CommandId = id, InvitationId = invitationId, Accept = accept });
        public void SetAllianceRole(int kingdomId, int role) => Command("api/diplomacy/alliance/role", id => new SetAllianceRoleRequest { CommandId = id, KingdomId = kingdomId, Role = role });
        public void LeaveAlliance(int allianceId) => Command("api/diplomacy/alliance/leave", id => new LeaveAllianceRequest { CommandId = id, AllianceId = allianceId });
        public void CreateAllianceProject(int allianceId, int type) => Command("api/diplomacy/alliance/project/create", id => new CreateAllianceProjectRequest { CommandId = id, AllianceId = allianceId, Type = type });
        public void ContributeAlliance(int allianceId, int projectId, string resource, int amount) => Command("api/diplomacy/alliance/contribute", id => new ContributeAllianceRequest { CommandId = id, AllianceId = allianceId, ProjectId = projectId, Resource = resource, Amount = amount });
        public void ProposeAgreement(int type, int toKingdomId, int regionId, int price) => Command("api/diplomacy/agreement/propose", id => new ProposeAgreementRequest { CommandId = id, Type = type, ToKingdomId = toKingdomId, RegionId = regionId, Price = price });
        public void RespondAgreement(int agreementId, bool accept) => Command("api/diplomacy/agreement/respond", id => new RespondAgreementRequest { CommandId = id, AgreementId = agreementId, Accept = accept });
        public void CreateAllianceOffer(string product, int quantity, int totalPrice, int allianceId) => Command("api/online/market/offers", id => new CreateOfferRequest { CommandId = id, Product = product, Quantity = quantity, TotalPrice = totalPrice, AllianceId = allianceId });

        // ---- Promt 6: boshqa o'yinchi shahriga mehmon, sovg'a va xat ----
        /// <summary>Hozir ko'rilayotgan boshqa saltanatning ochiq ko'rinishi (snapshot). null — hali yuklanmagan.</summary>
        public CitySnapshotDto Visit { get; private set; }
        public bool VisitBusy { get; private set; }
        /// <summary>Kelgan xatlar qutisi (o'qilmaganlar soni bilan).</summary>
        public LettersResponse Inbox { get; private set; }
        public int UnreadLetters => Inbox?.Unread ?? 0;

        /// <summary>Boshqa o'yinchi shahrining ochiq ko'rinishini oladi (maxfiy ma'lumotsiz).</summary>
        public void VisitCity(int kingdomId)
        {
            if (_http == null) return;
            VisitBusy = true; Visit = null;
            Task.Run(async () =>
            {
                try
                {
                    CitySnapshotDto dto = await _http.GetFromJsonAsync<CitySnapshotDto>("api/online/city/" + kingdomId, Json);
                    Post(() => Visit = dto);
                }
                catch (Exception ex) { SetMessage("Shaharni ochib bo'lmadi: " + Short(ex.Message), true); }
                finally { Post(() => VisitBusy = false); }
            });
        }

        public void CloseVisit() => Visit = null;

        /// <summary>Boshqa saltanatga sovg'a (coin|food) yuboradi. Server kunlik limit va miqdorni tekshiradi.</summary>
        public void SendGift(int toKingdomId, string kind, int amount)
        {
            if (!LoggedIn) { SetMessage("Avval serverga kiring", true); return; }
            Run(async () =>
            {
                HttpResponseMessage resp = await _http.PostAsJsonAsync("api/online/gift",
                    new GiftRequest { CommandId = Guid.NewGuid().ToString("N"), ToKingdomId = toKingdomId, Kind = kind, Amount = amount }, Json);
                if (!resp.IsSuccessStatusCode) { SetMessage(await ErrorText(resp, "Sovg'a yuborilmadi"), true); return; }
                SetMessage("Sovg'a yuborildi", false);
                QueueRefresh();
            });
        }

        /// <summary>Boshqa saltanatga xat yuboradi. Server uzunlik, filtr va spam limitini tekshiradi.</summary>
        public void SendLetter(int toKingdomId, string text)
        {
            if (!LoggedIn) { SetMessage("Avval serverga kiring", true); return; }
            Run(async () =>
            {
                HttpResponseMessage resp = await _http.PostAsJsonAsync("api/online/letter",
                    new LetterRequest { CommandId = Guid.NewGuid().ToString("N"), ToKingdomId = toKingdomId, Text = text ?? "" }, Json);
                if (!resp.IsSuccessStatusCode) { SetMessage(await ErrorText(resp, "Xat yuborilmadi"), true); return; }
                SetMessage("Elchingiz maktubni olib yo'lga chiqdi", false);
                LoadLetters(false);
            });
        }

        /// <summary>Kelgan xatlar qutisini (fonda) yuklaydi.</summary>
        /// <param name="markRead">true — xatlar qutisi ochildi (kelgan xatlar o'qilgan deb belgilanadi).</param>
        public void LoadLetters(bool markRead = false)
        {
            if (_http == null) return;
            Task.Run(async () =>
            {
                try
                {
                    LettersResponse dto = await _http.GetFromJsonAsync<LettersResponse>("api/online/letters?read=" + (markRead ? "true" : "false"), Json);
                    Post(() => { if (dto != null) { Inbox = dto; InboxLoadedAt = DateTime.UtcNow; } });
                }
                catch { }
            });
        }

        /// <summary>Xatlar (va elchilar holati) oxirgi yuklangan vaqt — elchi o'rnini silliq hisoblash uchun.</summary>
        public DateTime InboxLoadedAt { get; private set; }

        // ---- Dunyo umumiy chati (real vaqt, SignalR) ----
        /// <summary>Oxirgi chat xabarlari (eng yangisi oxirida).</summary>
        public List<ChatMessageDto> ChatLog { get; private set; } = new List<ChatMessageDto>();
        /// <summary>Har yangi chat xabarida oshadi (UI avtomatik pastga siljishi uchun).</summary>
        public int ChatSerial { get; private set; }
        /// <summary>Chat oynasi yopiq bo'lganda kelgan o'qilmagan xabarlar soni.</summary>
        public int UnreadChat { get; private set; }
        /// <summary>Chat oynasi ochiqligi — ochiq bo'lsa yangi xabarlar o'qilmagan deb sanalmaydi.</summary>
        public bool ChatOpen { get; set; }
        public void MarkChatRead() => UnreadChat = 0;

        /// <summary>Dunyo umumiy chatiga xabar yuboradi (server uzunlik va filtrni tekshiradi).</summary>
        public void SendChat(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0 || _hub == null || State == null) return;
            int worldId = State.WorldId;
            Task.Run(async () => { try { await _hub.InvokeAsync("SendChat", worldId, text); } catch { } });
        }

        public void Dispose()
        {
            try { DisconnectAsync().Wait(2000); } catch { }
        }
    }
}
