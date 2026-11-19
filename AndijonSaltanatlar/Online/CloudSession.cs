using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AndijonSaltanatlar.Shared.DTOs;

namespace AndijonSaltanatlar.Online
{
    public enum CloudLoginState { SignedOut, CheckingToken, WaitingForBrowser, SignedIn, Error }

    /// <summary>Qurilmada SHIFRLANGAN holda saqlanadigan sessiya (SecureTokenStorage).</summary>
    internal sealed class SecureSession
    {
        public string Access { get; set; } = "";
        public string Refresh { get; set; } = "";
        public DateTime AccessExpiresUtc { get; set; }
    }

    /// <summary>Qurilmada saqlanadigan kirish ma'lumoti (parol yo'q — faqat server bergan token).</summary>
    public sealed class StoredAuth
    {
        public string ServerUrl { get; set; } = "";
        /// <summary>Eski versiyalar (shifrlanmagan) — faqat bir martalik ko'chirish uchun o'qiladi, endi yozilmaydi.</summary>
        public string Token { get; set; } = "";
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Email { get; set; } = "";

        private static string FilePath => AndijonSaltanatlar.Core.AppPaths.File("auth.json");

        public static StoredAuth Load()
        {
            try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<StoredAuth>(File.ReadAllText(FilePath)); }
            catch { }
            return null;
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

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }
    }

    /// <summary>
    /// Doimiy onlayn sessiya: Google orqali kirish va bulutdagi saqlash.
    ///   - Kirish: server /api/auth/google/start → tizim brauzeri (Google) → /api/auth/google/poll (har 2 soniya).
    ///   - Token qurilmada saqlanadi: keyingi safar o'yin avtomatik kiradi (/api/auth/me bilan tekshiriladi).
    ///   - Saqlash: GET/PUT /api/save (versiya nazorati bilan) — o'yin istalgan qurilmada qolgan joyidan davom etadi.
    /// Barcha tarmoq ishlari fonda; natijalar <see cref="Pump"/> orqali o'yin oqimiga o'tadi.
    /// </summary>
    public sealed class CloudSession
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
        private CancellationTokenSource _loginCts;
        private HttpClient _http;
        private int _uploading;

        public CloudLoginState State { get; private set; } = CloudLoginState.SignedOut;
        public string ServerUrl { get; private set; } = "";
        public string Token { get; private set; }
        private string _refresh = "";
        private DateTime _accessExpiresUtc = DateTime.MaxValue;
        private bool _renewing;
        private float _renewWait;
        /// <summary>Yangi o'yinchi: taxallus tanlashi kerak (YANGI HUKMDOR).</summary>
        public bool NeedsProfile { get; private set; }
        public int AvatarId { get; private set; }
        /// <summary>VIP hisob: barcha imkoniyatlar ochilgan (server qaytaradi).</summary>
        public bool IsVip { get; private set; }
        /// <summary>Kirish tokeni yangilandi — onlayn ulanish ham yangi tokenni oladi.</summary>
        public Action<string> TokenRenewed;
        public string Username { get; private set; } = "";
        public string DisplayName { get; private set; } = "";
        public string Email { get; private set; } = "";
        public string Message { get; private set; } = "";
        public bool MessageIsError { get; private set; }
        public string LoginUrl { get; private set; } = "";

        /// <summary>Oxirgi muvaffaqiyatli saqlash versiyasi (server tasdiqlagan).</summary>
        public long SaveVersion { get; private set; }
        public DateTime LastSavedUtc { get; private set; }
        public bool LastSaveFailed { get; private set; }
        public bool Uploading => _uploading != 0;

        /// <summary>Kirish muvaffaqiyatli bo'lganda (asosiy oqimda) chaqiriladi.</summary>
        public Action SignedIn;
        /// <summary>Kirish tugagach o'yin oynasini oldinga chiqarish (Game1 o'rnatadi; Windows'da SetForegroundWindow).</summary>
        public Action BringToFront;
        /// <summary>Boshqa qurilma yangiroq saqlagan (409) — o'yin bulutdan qayta yuklanishi kerak.</summary>
        public Action SaveConflict;

        /// <summary>
        /// Android: telefondagi Google akkauntlar ro'yxati (tizim oynasi). Web Client ID oladi, tanlangan akkauntning
        /// Google ID tokenini qaytaradi; foydalanuvchi oynani yopsa OperationCanceledException. null — brauzer orqali.
        /// </summary>
        public Func<string, Task<string>> NativeAccountPicker { get; set; }

        /// <summary>
        /// Serversiz Google kirish (telefon): server topilmadi — telefondagi akkaunt tanlanadi, ID tokendan ism va
        /// identifikator olinadi (ism, email, sub). O'yin qurilmada saqlanadi.
        /// </summary>
        public Action<string, string, string> LocalGoogleSignedIn;
        /// <summary>Server topilmadi va telefon akkaunt oynasi yo'q (kompyuter) — serversiz o'yin boshlanadi.</summary>
        public Action ServerUnavailable;

        /// <summary>Server javob beradimi (4 soniya ichida). Har qanday HTTP javob — ha; ulanib bo'lmasa — yo'q.</summary>
        private async Task<bool> ServerReachableAsync(string url, CancellationToken ct)
        {
            try
            {
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(TimeSpan.FromSeconds(4));
                await Send(url, HttpMethod.Get, "api/auth/google/config", ct: t.Token);
                return true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
            catch (HttpRequestException) { return false; }
        }

        /// <summary>Serversiz: telefondagi Google akkauntni tanlash va ID tokendan ism/identifikatorni o'qish.</summary>
        private async Task LocalNativeLoginAsync(CancellationToken ct)
        {
            SetMessage("Server kerak emas — telefondagi Google akkauntni tanlang.", false);
            string idToken;
            try { idToken = await NativeAccountPicker(GoogleClient.WebClientId); }
            catch (OperationCanceledException)
            {
                Post(() => { State = CloudLoginState.SignedOut; UsingNativePicker = false; Message = "Kirish bekor qilindi."; MessageIsError = false; });
                return;
            }
            catch (Exception ex)
            {
                Post(() => { State = CloudLoginState.SignedOut; UsingNativePicker = false; Message = "Google akkaunt oynasi ochilmadi — SERVERSIZ O'YNASH tugmasini bosing." + Detail(ex); MessageIsError = true; });
                return;
            }
            (string name, string email, string sub) = ReadIdToken(idToken);
            Post(() =>
            {
                State = CloudLoginState.SignedOut; UsingNativePicker = false;
                DisplayName = name; Message = "Xush kelibsiz, " + name + "!"; MessageIsError = false;
                LocalGoogleSignedIn?.Invoke(name, email, sub);
            });
        }

        /// <summary>Google ID token (JWT) ichidan ism, email va sub (imzo serversiz tekshirilmaydi — faqat profil uchun).</summary>
        public static (string Name, string Email, string Sub) ReadIdToken(string jwt)
        {
            try
            {
                string[] parts = (jwt ?? "").Split('.');
                if (parts.Length < 2) return ("Hukmdor", "", "google");
                string b = parts[1].Replace('-', '+').Replace('_', '/');
                b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
                using JsonDocument doc = JsonDocument.Parse(Convert.FromBase64String(b));
                JsonElement root = doc.RootElement;
                string Get(string k) => root.TryGetProperty(k, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
                string name = Get("given_name");
                if (string.IsNullOrEmpty(name)) name = Get("name");
                if (string.IsNullOrEmpty(name)) name = Get("email").Split('@')[0];
                return (string.IsNullOrEmpty(name) ? "Hukmdor" : name, Get("email"), Get("sub"));
            }
            catch { return ("Hukmdor", "", "google"); }
        }

        /// <summary>Hozir telefonning akkaunt tanlash oynasi ochiq (kirish ekrani matni uchun).</summary>
        public bool UsingNativePicker { get; private set; }

        /// <summary>Sinov uchun: brauzer ochilmaydi, login manzili shu faylga yoziladi (Playwright o'zi ochadi).</summary>
        public string LoginUrlFile { get; set; }

        public string AccountLabel => (string.IsNullOrEmpty(Email) ? DisplayName : DisplayName + " (" + Email + ")") + (IsVip ? "  ·  VIP" : "");

        public void Pump()
        {
            while (_main.TryDequeue(out Action a))
            {
                try { a(); } catch { }
            }
        }

        private void Post(Action a) => _main.Enqueue(a);
        private void SetMessage(string text, bool error) => Post(() => { Message = text ?? ""; MessageIsError = error; });

        public static string NormalizeUrl(string url)
        {
            url = (url ?? "").Trim().TrimEnd('/');
            if (url.Length > 0 && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = (LooksLocal(url) ? "http://" : "https://") + url;
            return url;
        }

        /// <summary>IP manzil / localhost / port ko'rsatilgan bo'lsa — uy tarmog'i (http). Domen nomi (masalan
        /// xxx.trycloudflare.com) — internet manzili, standart https.</summary>
        public static bool LooksLocal(string hostAndRest)
        {
            string host = hostAndRest.Split('/')[0];
            if (host.Contains(':')) return true;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return System.Net.IPAddress.TryParse(host, out _);
        }

        private HttpClient Http(string url)
        {
            HttpClient h = _http;
            if (h == null || !string.Equals(h.BaseAddress.ToString().TrimEnd('/'), url, StringComparison.OrdinalIgnoreCase))
                _http = h = new HttpClient { BaseAddress = new Uri(url + "/"), Timeout = TimeSpan.FromSeconds(15) };
            return h;
        }

        /// <summary>So'rov yuboradi; token har so'rovga alohida qo'shiladi (umumiy header'lar oqimlar orasida o'zgarmaydi).</summary>
        private Task<HttpResponseMessage> Send(string url, HttpMethod method, string path, HttpContent content = null,
                                               string token = null, CancellationToken ct = default)
        {
            var req = new HttpRequestMessage(method, path) { Content = content };
            if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return Http(url).SendAsync(req, ct);
        }

        // ------------------------------------------------------------------
        // Avtomatik kirish (saqlangan token)
        // ------------------------------------------------------------------
        public void TryResume(string defaultServer)
        {
            StoredAuth a = StoredAuth.Load();
            ServerUrl = NormalizeUrl(a?.ServerUrl ?? defaultServer);
            SecureSession sec = LoadSecure();
            string legacy = a?.Token;
            if ((sec == null || string.IsNullOrEmpty(sec.Refresh)) && string.IsNullOrEmpty(legacy) || ServerUrl.Length == 0) { State = CloudLoginState.SignedOut; return; }
            State = CloudLoginState.CheckingToken;
            Message = "Serverga ulanmoqda...";
            MessageIsError = false;
            string url = ServerUrl;
            Task.Run(async () =>
            {
                try
                {
                    AuthResponse auth = null;
                    if (sec != null && !string.IsNullOrEmpty(sec.Refresh))
                    {
                        // Sessiyani yangilash (rotation): yangi kirish + yangi yangilash tokeni
                        HttpResponseMessage rr = await Send(url, HttpMethod.Post, "api/auth/refresh", JsonContent.Create(new RefreshRequest { RefreshToken = sec.Refresh }, options: Json));
                        if (rr.StatusCode == HttpStatusCode.Unauthorized) { Expired(); return; }
                        rr.EnsureSuccessStatusCode();
                        auth = await rr.Content.ReadFromJsonAsync<AuthResponse>(Json);
                    }
                    else
                    {
                        // Eski versiyadan: ochiq saqlangan token bir marta shifrlangan sessiyaga almashtiriladi
                        HttpResponseMessage ur = await Send(url, HttpMethod.Post, "api/auth/upgrade", token: legacy);
                        if (ur.StatusCode == HttpStatusCode.Unauthorized || ur.StatusCode == HttpStatusCode.Forbidden) { Expired(); return; }
                        ur.EnsureSuccessStatusCode();
                        auth = await ur.Content.ReadFromJsonAsync<AuthResponse>(Json);
                    }
                    HttpResponseMessage r = await Send(url, HttpMethod.Get, "api/auth/me", token: auth.Token);
                    r.EnsureSuccessStatusCode();
                    MeResponse me = await r.Content.ReadFromJsonAsync<MeResponse>(Json);
                    Post(() => FinishSignIn(url, auth, me.Email));
                }
                catch (Exception ex)
                {
                    bool net = ex is HttpRequestException || ex is TaskCanceledException;
                    Post(() => { State = CloudLoginState.Error; Message = (net ? "INTERNET ALOQASI YO'Q yoki SERVERGA ULANIB BO'LMADI." : "SERVERGA ULANIB BO'LMADI.") + Detail(ex); MessageIsError = true; });
                }
            });
        }

        // ------------------------------------------------------------------
        // Google bilan kirish
        // ------------------------------------------------------------------
        /// <summary>Google kirishni tekshirish oralig'i: birinchi 30 soniyada 1 s (tez javob), keyin 2 s.</summary>
        public static int PollDelayMs(double elapsedSeconds) => elapsedSeconds < 30 ? 1000 : 2000;

        public void StartGoogleLogin(string serverUrl)
        {
            string url = NormalizeUrl(serverUrl);
            if (url.Length == 0) { Message = "Server manzilini kiriting"; MessageIsError = true; return; }
            CancelLogin();
            ServerUrl = url;
            State = CloudLoginState.WaitingForBrowser;
            UsingNativePicker = NativeAccountPicker != null;
            Message = UsingNativePicker ? "Google akkauntlari ochilmoqda..." : "Brauzer ochilmoqda...";
            MessageIsError = false;
            var cts = _loginCts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                try
                {
                    // 0) Server yo'q (yoki ulanib bo'lmaydi) — server kerak emas: telefon Google akkaunti bilan qurilmada o'ynash
                    if (!await ServerReachableAsync(url, cts.Token))
                    {
                        if (NativeAccountPicker != null) { await LocalNativeLoginAsync(cts.Token); return; }
                        Post(() => { State = CloudLoginState.SignedOut; UsingNativePicker = false; Message = "Server topilmadi — serversiz o'ynaysiz."; MessageIsError = false; ServerUnavailable?.Invoke(); });
                        return;
                    }
                    // 1) Telefon: tizimning Google akkaunt tanlash oynasi (telefondagi barcha akkauntlar).
                    if (NativeAccountPicker != null && await TryNativeLoginAsync(url, cts.Token)) return;
                    Post(() => UsingNativePicker = false);

                    // 2) Brauzer orqali Google (kompyuter yoki telefon oynasi ishlamasa).
                    HttpResponseMessage r = await Send(url, HttpMethod.Post, "api/auth/google/start", ct: cts.Token);
                    if (!r.IsSuccessStatusCode) { await FailFrom(r, "Google kirishni boshlab bo'lmadi"); return; }
                    GoogleLoginStartResponse start = await r.Content.ReadFromJsonAsync<GoogleLoginStartResponse>(Json, cts.Token);
                    Post(() => LoginUrl = start.LoginUrl);
                    OpenBrowser(start.LoginUrl);
                    SetMessage("Brauzerda Google hisobingizni tanlang, so'ng o'yinga qayting.", false);

                    DateTime started = DateTime.UtcNow;
                    DateTime until = DateTime.UtcNow.AddSeconds(Math.Max(60, start.ExpiresInSeconds));
                    while (DateTime.UtcNow < until)
                    {
                        // Birinchi 30 soniya tez-tez (1 s), keyin sekinroq (2 s) tekshiramiz.
                        await Task.Delay(PollDelayMs((DateTime.UtcNow - started).TotalSeconds), cts.Token);
                        HttpResponseMessage p;
                        try { p = await Send(url, HttpMethod.Get, "api/auth/google/poll?s=" + Uri.EscapeDataString(start.SessionId) + "&k=" + Uri.EscapeDataString(start.PollKey), ct: cts.Token); }
                        catch (HttpRequestException) { continue; } // vaqtincha tarmoq uzilishi — qayta urinamiz
                        if (!p.IsSuccessStatusCode) continue;
                        GoogleLoginPollResponse res = await p.Content.ReadFromJsonAsync<GoogleLoginPollResponse>(Json, cts.Token);
                        if (res.Status == "pending") continue;
                        if (res.Status == "ok" && res.Auth != null)
                        {
                            Post(() => FinishSignIn(url, res.Auth, res.Email));
                            return;
                        }
                        Post(() => { State = CloudLoginState.Error; Message = string.IsNullOrEmpty(res.MessageUz) ? "Kirib bo'lmadi" : res.MessageUz; MessageIsError = true; });
                        return;
                    }
                    Post(() => { State = CloudLoginState.Error; Message = "Kirish vaqti tugadi. Qaytadan urinib ko'ring."; MessageIsError = true; });
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Post(() => { State = CloudLoginState.Error; Message = "Serverga ulanib bo'lmadi." + Detail(ex); MessageIsError = true; });
                }
            });
        }

        /// <summary>
        /// Telefondagi akkaunt tanlash oynasi orqali kirish. true — jarayon tugadi (kirildi, bekor qilindi yoki xato
        /// ko'rsatildi); false — bu usul mavjud emas (server Google sozlanmagan / Play xizmatlari yo'q), brauzerga o'tiladi.
        /// </summary>
        private async Task<bool> TryNativeLoginAsync(string url, CancellationToken ct)
        {
            GoogleClientConfig cfg;
            HttpResponseMessage c = await Send(url, HttpMethod.Get, "api/auth/google/config", ct: ct);
            if (!c.IsSuccessStatusCode) return false;
            cfg = await c.Content.ReadFromJsonAsync<GoogleClientConfig>(Json, ct);
            if (cfg == null || !cfg.Configured || string.IsNullOrEmpty(cfg.WebClientId)) return false;

            SetMessage("Telefoningizdagi Google akkauntlardan birini tanlang.", false);
            string idToken;
            try
            {
                idToken = await NativeAccountPicker(cfg.WebClientId);
            }
            catch (OperationCanceledException)
            {
                Post(() => { State = CloudLoginState.SignedOut; UsingNativePicker = false; Message = "Kirish bekor qilindi."; MessageIsError = false; });
                return true;
            }
            catch (Exception ex)
            {
                // Masalan, Google Play xizmatlari yo'q yoki ilova Google Cloud'da ro'yxatdan o'tmagan — brauzer orqali davom etamiz.
                SetMessage("Telefon akkaunt oynasi ochilmadi, brauzer orqali davom etamiz." + Detail(ex), false);
                return false;
            }

            HttpResponseMessage r = await Send(url, HttpMethod.Post, "api/auth/google/idtoken",
                JsonContent.Create(new GoogleIdTokenRequest { IdToken = idToken }, options: Json), ct: ct);
            if (!r.IsSuccessStatusCode) { await FailFrom(r, "Google hisobini tasdiqlab bo'lmadi"); return true; }
            GoogleLoginPollResponse res = await r.Content.ReadFromJsonAsync<GoogleLoginPollResponse>(Json, ct);
            Post(() => { UsingNativePicker = false; FinishSignIn(url, res.Auth, res.Email); });
            return true;
        }

        public void CancelLogin()
        {
            try { _loginCts?.Cancel(); } catch { }
            _loginCts = null;
            if (State == CloudLoginState.WaitingForBrowser) { State = CloudLoginState.SignedOut; Message = ""; }
        }

        private void FinishSignIn(string url, AuthResponse auth, string email)
        {
            ServerUrl = url;
            ApplyAuth(auth);
            Username = auth.Username ?? "";
            DisplayName = string.IsNullOrEmpty(auth.DisplayName) ? Username : auth.DisplayName;
            NeedsProfile = auth.NeedsProfile;
            AvatarId = auth.AvatarId;
            IsVip = auth.IsVip;
            Email = email ?? "";   // faqat shu qurilmada ko'rsatiladi (diskka yozilmaydi)
            State = CloudLoginState.SignedIn;
            Message = "Xush kelibsiz, " + DisplayName + "!";
            MessageIsError = false;
            // Ochiq faylda faqat server manzili va taxallus (tokenlar — shifrlangan faylda)
            new StoredAuth { ServerUrl = url, Token = "", Username = Username, DisplayName = DisplayName, Email = "" }.Save();
            SignedIn?.Invoke();
            try { BringToFront?.Invoke(); } catch { /* oynani oldinga chiqarish muvaffaqiyatsiz bo'lsa ham kirish tugadi */ }
        }

        private void ApplyAuth(AuthResponse auth)
        {
            Token = auth.Token;
            if (!string.IsNullOrEmpty(auth.RefreshToken)) _refresh = auth.RefreshToken;
            _accessExpiresUtc = auth.ExpiresInSeconds > 0 ? DateTime.UtcNow.AddSeconds(auth.ExpiresInSeconds) : DateTime.UtcNow.AddDays(6);
            SaveSecure();
        }

        private void SaveSecure()
        {
            if (string.IsNullOrEmpty(_refresh)) return;
            SecureTokenStorage.Save(JsonSerializer.Serialize(new SecureSession { Access = Token ?? "", Refresh = _refresh, AccessExpiresUtc = _accessExpiresUtc }));
        }

        private static SecureSession LoadSecure()
        {
            string s = SecureTokenStorage.Load();
            if (string.IsNullOrEmpty(s)) return null;
            try { return JsonSerializer.Deserialize<SecureSession>(s); } catch { return null; }
        }

        private void Expired()
        {
            SecureTokenStorage.Clear();
            IsVip = false;
            NeedsProfile = false;
            StoredAuth a = StoredAuth.Load();
            if (a != null) { a.Token = ""; a.Save(); }
            Post(() => { State = CloudLoginState.SignedOut; Message = "SESSIYA MUDDATI TUGADI. Google orqali qayta kiring."; MessageIsError = true; });
        }

        /// <summary>
        /// Har kadr: kirish tokeni tugashiga 5 daqiqa qolganda fonda yangilanadi (o'yin to'xtamaydi).
        /// Internet yo'q bo'lsa 30 soniyadan keyin qayta uriniladi; sessiya bekor qilingan bo'lsa — kirish ekrani.
        /// </summary>
        public void Tick(float dt)
        {
            if (State != CloudLoginState.SignedIn || string.IsNullOrEmpty(_refresh) || _renewing) return;
            if (_renewWait > 0f) { _renewWait -= dt; return; }
            if (DateTime.UtcNow < _accessExpiresUtc.AddMinutes(-5)) return;
            _renewing = true;
            string url = ServerUrl, refresh = _refresh;
            Task.Run(async () =>
            {
                try
                {
                    HttpResponseMessage r = await Send(url, HttpMethod.Post, "api/auth/refresh", JsonContent.Create(new RefreshRequest { RefreshToken = refresh }, options: Json));
                    if (r.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        Post(() => { _renewing = false; Token = null; SessionLost?.Invoke(); });
                        Expired();
                        return;
                    }
                    r.EnsureSuccessStatusCode();
                    AuthResponse a = await r.Content.ReadFromJsonAsync<AuthResponse>(Json);
                    Post(() => { _renewing = false; ApplyAuth(a); TokenRenewed?.Invoke(Token); });
                }
                catch
                {
                    Post(() => { _renewing = false; _renewWait = 30f; });   // internet uzildi — keyinroq yana
                }
            });
        }

        /// <summary>Sessiya serverda bekor qilingan/muddati tugagan — Game1 kirish ekraniga qaytaradi (progress saqlanadi).</summary>
        public Action SessionLost;

        /// <summary>YANGI HUKMDOR: taxallus va avatarni serverga saqlaydi. done(xato) — null bo'lsa muvaffaqiyat.</summary>
        public void SubmitProfile(string nickname, int avatar, Action<string> done)
        {
            string url = ServerUrl, token = Token;
            Task.Run(async () =>
            {
                try
                {
                    HttpResponseMessage r = await Send(url, HttpMethod.Post, "api/auth/profile",
                        JsonContent.Create(new ProfileRequest { Nickname = nickname, AvatarId = avatar }, options: Json), token: token);
                    if (!r.IsSuccessStatusCode)
                    {
                        string err = "Saqlab bo'lmadi";
                        try { using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); if (doc.RootElement.TryGetProperty("messageUz", out var m)) err = m.GetString(); } catch { }
                        Post(() => done?.Invoke(err));
                        return;
                    }
                    MeResponse me = await r.Content.ReadFromJsonAsync<MeResponse>(Json);
                    Post(() =>
                    {
                        DisplayName = me.DisplayName;
                        AvatarId = me.AvatarId;
                        IsVip = me.IsVip;
                        NeedsProfile = false;
                        new StoredAuth { ServerUrl = url, Token = "", Username = Username, DisplayName = DisplayName, Email = "" }.Save();
                        done?.Invoke(null);
                    });
                }
                catch { Post(() => done?.Invoke("SERVERGA ULANIB BO'LMADI")); }
            });
        }

        // ------------------------------------------------------------------
        // DUNYONI TANLASH / boshlang'ich yurt (kirgandan keyin, o'yin boshlanishidan oldin)
        // ------------------------------------------------------------------
        /// <summary>Mavjud dunyolar va o'z saltanati qaysi dunyoda ekani.</summary>
        public void LoadWorlds(Action<WorldsResponse, string> done)
        {
            string url = ServerUrl, token = Token;
            Task.Run(async () =>
            {
                try
                {
                    HttpResponseMessage r = await Send(url, HttpMethod.Get, "api/online/worlds", token: token);
                    if (r.StatusCode == HttpStatusCode.Unauthorized) { Expired(); Post(() => done?.Invoke(null, "SESSIYA MUDDATI TUGADI.")); return; }
                    if (!r.IsSuccessStatusCode) { Post(() => done?.Invoke(null, "DUNYOLARNI YUKLAB BO'LMADI.")); return; }
                    WorldsResponse dto = await r.Content.ReadFromJsonAsync<WorldsResponse>(Json);
                    Post(() => done?.Invoke(dto, null));
                }
                catch (Exception ex) { Post(() => done?.Invoke(null, "SERVERGA ULANIB BO'LMADI." + Detail(ex))); }
            });
        }

        /// <summary>Tanlangan dunyoga qo'shilish (yangi o'yinchi uchun saltanat yaratiladi, yurt keyin tanlanadi).</summary>
        public void JoinWorld(int worldId, Action<OnlineStateDto, string> done)
        {
            string url = ServerUrl, token = Token;
            Task.Run(async () =>
            {
                try
                {
                    HttpResponseMessage r = await Send(url, HttpMethod.Post, "api/online/join",
                        JsonContent.Create(new JoinWorldRequest { WorldId = worldId, ChooseStart = true }, options: Json), token);
                    if (r.StatusCode == HttpStatusCode.Unauthorized) { Expired(); Post(() => done?.Invoke(null, "SESSIYA MUDDATI TUGADI.")); return; }
                    if (!r.IsSuccessStatusCode) { string e = await ReadError(r, "DUNYOGA QO'SHILIB BO'LMADI."); Post(() => done?.Invoke(null, e)); return; }
                    OnlineStateDto dto = await r.Content.ReadFromJsonAsync<OnlineStateDto>(Json);
                    Post(() => done?.Invoke(dto, null));
                }
                catch (Exception ex) { Post(() => done?.Invoke(null, "SERVERGA ULANIB BO'LMADI." + Detail(ex))); }
            });
        }

        /// <summary>Bo'sh hududlar ro'yxati (faqat dunyoga qo'shilgandan keyin).</summary>
        public void LoadStartRegions(Action<StartRegionsDto, string> done)
        {
            string url = ServerUrl, token = Token;
            Task.Run(async () =>
            {
                try
                {
                    HttpResponseMessage r = await Send(url, HttpMethod.Get, "api/online/start-regions", token: token);
                    if (r.StatusCode == HttpStatusCode.Unauthorized) { Expired(); Post(() => done?.Invoke(null, "SESSIYA MUDDATI TUGADI.")); return; }
                    if (!r.IsSuccessStatusCode) { Post(() => done?.Invoke(null, "HUDUDLARNI YUKLAB BO'LMADI.")); return; }
                    StartRegionsDto dto = await r.Content.ReadFromJsonAsync<StartRegionsDto>(Json);
                    Post(() => done?.Invoke(dto, null));
                }
                catch (Exception ex) { Post(() => done?.Invoke(null, "SERVERGA ULANIB BO'LMADI." + Detail(ex))); }
            });
        }

        /// <summary>
        /// Boshlang'ich hududni tanlash (server bandlikni qayta tekshiradi va bir marta saqlaydi).
        /// <paramref name="done"/>: (holat, xato, hududBand) — hududBand true bo'lsa ro'yxat eskirgan, qayta yuklash kerak.
        /// </summary>
        public void ChooseStart(int regionId, string kingdomName, string flag, Action<OnlineStateDto, string, bool> done, bool auto = false)
        {
            string url = ServerUrl, token = Token;
            Task.Run(async () =>
            {
                try
                {
                    HttpResponseMessage r = await Send(url, HttpMethod.Post, "api/online/start",
                        JsonContent.Create(new ChooseStartRequest
                        {
                            CommandId = Guid.NewGuid().ToString("N"),
                            RegionId = regionId,
                            KingdomName = kingdomName ?? "",
                            Flag = flag ?? "",
                            Auto = auto
                        }, options: Json), token);
                    if (r.StatusCode == HttpStatusCode.Unauthorized) { Expired(); Post(() => done?.Invoke(null, "SESSIYA MUDDATI TUGADI.", false)); return; }
                    if (!r.IsSuccessStatusCode)
                    {
                        bool taken = r.StatusCode == HttpStatusCode.Conflict;
                        string e = await ReadError(r, "HUDUDNI TANLAB BO'LMADI.");
                        Post(() => done?.Invoke(null, e, taken));
                        return;
                    }
                    OnlineStateDto dto = await r.Content.ReadFromJsonAsync<OnlineStateDto>(Json);
                    Post(() => done?.Invoke(dto, null, false));
                }
                catch (Exception ex) { Post(() => done?.Invoke(null, "SERVERGA ULANIB BO'LMADI." + Detail(ex), false)); }
            });
        }

        // --- Promt 6: mehmon, sovg'a, xat (server endpointlari bilan ishlaydi) ---

        /// <summary>Boshqa o'yinchi shahrining ochiq ko'rinishini oladi.</summary>
        public void VisitCity(int kingdomId, Action<CitySnapshotDto, string> done)
        {
            string url = ServerUrl, token = Token;
            Task.Run(async () =>
            {
                try
                {
                    HttpResponseMessage r = await Send(url, HttpMethod.Get, "api/online/city/" + kingdomId, token: token);
                    if (!r.IsSuccessStatusCode) { string e = await ReadError(r, "SHAHARNI OCHIB BO'LMADI."); Post(() => done?.Invoke(null, e)); return; }
                    CitySnapshotDto dto = await r.Content.ReadFromJsonAsync<CitySnapshotDto>(Json);
                    Post(() => done?.Invoke(dto, null));
                }
                catch (Exception ex) { Post(() => done?.Invoke(null, "SERVERGA ULANIB BO'LMADI." + Detail(ex))); }
            });
        }

        /// <summary>Sovg'a yuboradi. done(null) — muvaffaqiyat, aks holda xato matni.</summary>
        public void SendGift(int toKingdomId, string kind, int amount, Action<string> done)
        {
            string url = ServerUrl, token = Token;
            Task.Run(async () =>
            {
                try
                {
                    HttpResponseMessage r = await Send(url, HttpMethod.Post, "api/online/gift",
                        JsonContent.Create(new GiftRequest { CommandId = Guid.NewGuid().ToString("N"), ToKingdomId = toKingdomId, Kind = kind, Amount = amount }, options: Json), token);
                    if (!r.IsSuccessStatusCode) { string e = await ReadError(r, "SOVG'A YUBORILMADI."); Post(() => done?.Invoke(e)); return; }
                    Post(() => done?.Invoke(null));
                }
                catch (Exception ex) { Post(() => done?.Invoke("SERVERGA ULANIB BO'LMADI." + Detail(ex))); }
            });
        }

        /// <summary>Xat yuboradi. done(null) — muvaffaqiyat, aks holda xato matni.</summary>
        public void SendLetter(int toKingdomId, string text, Action<string> done)
        {
            string url = ServerUrl, token = Token;
            Task.Run(async () =>
            {
                try
                {
                    HttpResponseMessage r = await Send(url, HttpMethod.Post, "api/online/letter",
                        JsonContent.Create(new LetterRequest { CommandId = Guid.NewGuid().ToString("N"), ToKingdomId = toKingdomId, Text = text ?? "" }, options: Json), token);
                    if (!r.IsSuccessStatusCode) { string e = await ReadError(r, "XAT YUBORILMADI."); Post(() => done?.Invoke(e)); return; }
                    Post(() => done?.Invoke(null));
                }
                catch (Exception ex) { Post(() => done?.Invoke("SERVERGA ULANIB BO'LMADI." + Detail(ex))); }
            });
        }

        /// <summary>Kelgan xatlar qutisini oladi.</summary>
        public void FetchLetters(Action<LettersResponse, string> done)
        {
            string url = ServerUrl, token = Token;
            Task.Run(async () =>
            {
                try
                {
                    HttpResponseMessage r = await Send(url, HttpMethod.Get, "api/online/letters", token: token);
                    if (!r.IsSuccessStatusCode) { string e = await ReadError(r, "XATLARNI OLIB BO'LMADI."); Post(() => done?.Invoke(null, e)); return; }
                    LettersResponse dto = await r.Content.ReadFromJsonAsync<LettersResponse>(Json);
                    Post(() => done?.Invoke(dto, null));
                }
                catch (Exception ex) { Post(() => done?.Invoke(null, "SERVERGA ULANIB BO'LMADI." + Detail(ex))); }
            });
        }

        private static async Task<string> ReadError(HttpResponseMessage r, string fallback)
        {
            string msg = fallback;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("messageUz", out JsonElement m) && !string.IsNullOrWhiteSpace(m.GetString()))
                    msg = m.GetString();
            }
            catch { }
            return msg;
        }

        /// <summary>Kirgandan keyingi xato (masalan saqlashni yuklab bo'lmadi) — kirish ekrani xabar va "Qayta urinish" ko'rsatadi.</summary>
        public void ReportError(string message)
        {
            State = CloudLoginState.Error;
            Message = message ?? "Xato";
            MessageIsError = true;
        }

        public void SignOut()
        {
            CancelLogin();
            // Serverda shu qurilma sessiyasi bekor qilinadi (fonda; internet bo'lmasa ham qurilmadan o'chiriladi)
            string url = ServerUrl, token = Token;
            if (!string.IsNullOrEmpty(token) && url.Length > 0)
                Task.Run(async () => { try { await Send(url, HttpMethod.Post, "api/auth/logout", token: token); } catch { } });
            StoredAuth a = StoredAuth.Load();
            if (a != null) { a.Token = ""; a.Username = ""; a.DisplayName = ""; a.Save(); }   // server manzili qoladi
            SecureTokenStorage.Clear();
            _refresh = "";
            NeedsProfile = false;
            IsVip = false;
            AvatarId = 0;
            Token = null;
            SaveVersion = 0;
            State = CloudLoginState.SignedOut;
            Message = "Hisobdan chiqdingiz.";
            MessageIsError = false;
        }

        /// <summary>Tizim brauzerida manzil ochish (maxfiylik siyosati va h.k.).</summary>
        public static void OpenUrl(string url)
        {
            try
            {
#if ANDROID
                var intent = new Android.Content.Intent(Android.Content.Intent.ActionView, Android.Net.Uri.Parse(url));
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
#else
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
#endif
            }
            catch { }
        }

        private void OpenBrowser(string url)
        {
            if (!string.IsNullOrEmpty(LoginUrlFile))
            {
                try { File.WriteAllText(LoginUrlFile, url); } catch { }
                return;
            }
            try
            {
#if ANDROID
                var intent = new Android.Content.Intent(Android.Content.Intent.ActionView, Android.Net.Uri.Parse(url));
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
#else
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
#endif
            }
            catch (Exception ex)
            {
                SetMessage("Brauzerni ochib bo'lmadi: " + ex.Message, true);
            }
        }

        private async Task FailFrom(HttpResponseMessage r, string fallback)
        {
            string msg = fallback + " (" + (int)r.StatusCode + ")";
            try
            {
                using JsonDocument doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("messageUz", out JsonElement m)) msg = m.GetString();
            }
            catch { }
            Post(() => { State = CloudLoginState.Error; Message = msg; MessageIsError = true; });
        }

        private static string Detail(Exception ex)
        {
            string m = ex.GetBaseException().Message ?? "";
            return m.Length == 0 ? "" : " (" + (m.Length > 60 ? m.Substring(0, 60) : m) + ")";
        }

        // ------------------------------------------------------------------
        // Bulutdagi saqlash
        // ------------------------------------------------------------------
        /// <summary>Bulutdagi o'yinni yuklaydi. onDone(json yoki null — saqlash yo'q, xato bo'lsa error).</summary>
        public void LoadSave(Action<string, string> onDone)
        {
            string url = ServerUrl, token = Token;
            Task.Run(async () =>
            {
                try
                {
                    HttpResponseMessage r = await Send(url, HttpMethod.Get, "api/save", token: token);
                    if (r.StatusCode == HttpStatusCode.NotFound) { Post(() => { SaveVersion = 0; onDone(null, null); }); return; }
                    if (r.StatusCode == HttpStatusCode.Unauthorized) { Post(() => onDone(null, "unauthorized")); return; }
                    r.EnsureSuccessStatusCode();
                    CloudSaveDto dto = await r.Content.ReadFromJsonAsync<CloudSaveDto>(Json);
                    Post(() => { SaveVersion = dto.Version; LastSavedUtc = dto.UpdatedAt; onDone(dto.Json, null); });
                }
                catch (Exception ex)
                {
                    Post(() => onDone(null, "Saqlangan o'yinni yuklab bo'lmadi." + Detail(ex)));
                }
            });
        }

        /// <summary>O'yinni bulutga yuboradi (fonda; bir vaqtda faqat bitta yuborish).</summary>
        public void UploadSave(string json, Action<bool> onDone = null)
        {
            if (State != CloudLoginState.SignedIn || string.IsNullOrEmpty(json)) { onDone?.Invoke(false); return; }
            if (Interlocked.Exchange(ref _uploading, 1) == 1) { onDone?.Invoke(false); return; }
            string url = ServerUrl, token = Token;
            long baseVersion = SaveVersion;
#if ANDROID
            const string device = "Android";
#else
            const string device = "Windows";
#endif
            Task.Run(async () =>
            {
                try
                {
                    HttpResponseMessage r = await Send(url, HttpMethod.Put, "api/save",
                        JsonContent.Create(new CloudSaveUploadRequest { Json = json, BaseVersion = baseVersion, Device = device }, options: Json), token);
                    if (r.StatusCode == HttpStatusCode.Conflict)
                    {
                        Post(() => { LastSaveFailed = true; SaveConflict?.Invoke(); onDone?.Invoke(false); });
                        return;
                    }
                    r.EnsureSuccessStatusCode();
                    CloudSaveDto dto = await r.Content.ReadFromJsonAsync<CloudSaveDto>(Json);
                    Post(() => { SaveVersion = dto.Version; LastSavedUtc = DateTime.UtcNow; LastSaveFailed = false; onDone?.Invoke(true); });
                }
                catch (Exception)
                {
                    Post(() => { LastSaveFailed = true; onDone?.Invoke(false); });
                }
                finally { Interlocked.Exchange(ref _uploading, 0); }
            });
        }

        /// <summary>Ilova yopilayotganda: oxirgi holatni sinxron (qisqa kutish bilan) yuboradi.</summary>
        public bool UploadSaveBlocking(string json, int timeoutMs = 4000)
        {
            if (State != CloudLoginState.SignedIn || string.IsNullOrEmpty(json)) return false;
            try
            {
                var task = Send(ServerUrl, HttpMethod.Put, "api/save",
                    JsonContent.Create(new CloudSaveUploadRequest { Json = json, BaseVersion = SaveVersion, Device = "exit" }, options: Json), Token);
                if (!task.Wait(timeoutMs) || !task.Result.IsSuccessStatusCode) return false;
                CloudSaveDto dto = task.Result.Content.ReadFromJsonAsync<CloudSaveDto>(Json).Result;
                SaveVersion = dto.Version;
                return true;
            }
            catch { return false; }
        }
    }
}
