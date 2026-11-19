using System;
using System.Threading.Tasks;
using AndijonSaltanatlar.Core;
using AndijonSaltanatlar.Online;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.UI.Design;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#if ANDROID
using Microsoft.Xna.Framework.Input;
#endif

namespace AndijonSaltanatlar.UI
{
    /// <summary>
    /// Kirish ekrani (yagona usul — Google). Orqa fonda tirik Asaka shahri (Game1 chizadi) + yumshoq qoraytirish.
    ///   Login   — "GOOGLE ORQALI KIRISH"; holatlar: kutish, tekshirish, bekor, xato, internet yo'q;
    ///   Profile — YANGI HUKMDOR: taxallus, avatar, til → "DAVOM ETISH" (Google ismi/emaili boshqalarga ko'rsatilmaydi);
    ///   Welcome — XUSH KELIBSIZ! taxallus → "O'YINNI DAVOM ETTIRISH" (qaytgan o'yinchi).
    /// </summary>
    public sealed class GoogleLoginScreen
    {
        public enum Stage { Login, Profile, Welcome, Shah }

        private readonly CloudSession _cloud;
        private InputManager _input;
        private string _server;
        private bool _editServer;
        private bool _copied;
        private string _pendingKeyboard;
        private string _nick = "";
        private int _avatar;
        private float _stageTime = -1f;
        private Stage _lastStage = (Stage)(-1);

        public UiKit Ui { get; set; }
        /// <summary>Kirgandan keyin o'yin yuklanayotgan payt (Game1 o'rnatadi).</summary>
        public string Busy { get; set; }
        public Stage Mode { get; set; } = Stage.Login;
        /// <summary>Welcome: "O'YINNI DAVOM ETTIRISH" bosildi.</summary>
        public Action ContinueRequested;
        /// <summary>Login: "SERVERSIZ O'YNASH" — internet/serversiz, o'yin shu qurilmada saqlanadi.</summary>
        public Action OfflineRequested;
        /// <summary>Profile: taxallus saqlandi.</summary>
        public Action ProfileDone;
        /// <summary>Shoh qiyofasi oldindan ko'rinishi uchun sprite (Game1 beradi).</summary>
        public Func<Texture2D> ShahTexture;

        // --- SHOH QIYOFASI (yangi o'yinchi) ---
        private string _shahName = "";
        private int _shahBody = 1, _shahHeight = 1;
        private bool _shahInit;
        /// <summary>Shoh sozlangach: true — profil endi yaratildi (dunyo tanlashga o'tadi), false — Welcome'ga qaytadi.</summary>
        private bool _shahThenProfileDone;
        private string _profileError;
        private bool _profileBusy;

        private static readonly Color Gold = new Color(222, 178, 92);
        private static readonly Color SoftBlue = new Color(120, 168, 226);
        private static readonly Color[] AvatarColors =
        {
            new Color(196, 70, 60), new Color(60, 120, 200), new Color(70, 150, 96),
            new Color(214, 160, 40), new Color(130, 80, 170), new Color(40, 150, 160)
        };
        private static readonly string[] AvatarLetters = { "Q", "S", "K", "T", "B", "O" };

        public GoogleLoginScreen(CloudSession cloud, string defaultServer)
        {
            _cloud = cloud;
            _server = string.IsNullOrEmpty(cloud.ServerUrl) ? defaultServer ?? "" : cloud.ServerUrl;
            _editServer = string.IsNullOrEmpty(_server);
        }

        public void Update(InputManager input)
        {
            _input = input;
            input.MouseOverUI = true;
            string kb = _pendingKeyboard;
            if (kb != null)
            {
                if (Mode == Stage.Profile) _nick = kb; else if (Mode == Stage.Shah) _shahName = kb; else _server = kb;
                _pendingKeyboard = null;
            }
        }

        public void Draw(SpriteBatch sb, int vw, int vh, float time)
        {
            if (Ui == null || _input == null) return;
            UiKit ui = Ui;
            ui.Begin(sb, _input, vw, vh, time);
            if (Mode != _lastStage) { _lastStage = Mode; _stageTime = time; }
            float appear = MathHelper.Clamp((time - _stageTime) / 0.35f, 0f, 1f);
            ui.Fill(new Rectangle(0, 0, vw, vh), new Color(10, 12, 18) * 0.55f);
            ui.Fill(new Rectangle(0, vh / 2, vw, vh / 2), new Color(10, 12, 18) * 0.25f);
            switch (Mode)
            {
                case Stage.Profile: DrawProfile(ui, sb, vw, vh, time, appear); break;
                case Stage.Welcome: DrawWelcome(ui, sb, vw, vh, time, appear); break;
                case Stage.Shah: DrawShah(ui, sb, vw, vh, time, appear); break;
                default: DrawLogin(ui, sb, vw, vh, time, appear); break;
            }
            ui.End();
        }

        private static Rectangle Card(UiKit ui, int vw, int vh, int w, int h, float appear)
        {
            w = Math.Min(vw - 32, w);
            h = Math.Min(vh - 24, h);
            var card = new Rectangle((vw - w) / 2, (vh - h) / 2 + (int)((1f - appear) * 24), w, h);
            ui.Panel(card);
            ui.Rounded(new Rectangle(card.X, card.Y, card.Width, 5), Gold, 3);
            ui.Fill(new Rectangle(card.X + 24, card.Y + 6, card.Width - 48, 2), SoftBlue * 0.6f);
            return card;
        }

        private static void Title(UiKit ui, SpriteBatch sb, Rectangle card, ref int y, string title, string sub)
        {
            float th = ui.Type.LineHeight(TextStyle.MainTitle), bh = ui.Type.LineHeight(TextStyle.Body);
            ui.Type.DrawFitted(sb, title, new Rectangle(card.X + 16, y, card.Width - 32, (int)th), TextStyle.MainTitle, Gold, 1);
            y += (int)th + 2;
            if (sub == null) return;
            ui.Type.DrawFitted(sb, sub, new Rectangle(card.X + 16, y, card.Width - 32, (int)bh), TextStyle.Body, SoftBlue, 1);
            y += (int)bh;
        }

        // ------------------------------------------------------------------ 1) Kirish
        private void DrawLogin(UiKit ui, SpriteBatch sb, int vw, int vh, float time, float appear)
        {
            ThemeTokens T = ui.T;
            int h = ui.ControlH + (ui.IsTouch ? 8 : 10), gap = ui.Gap;
            float capH = ui.Type.LineHeight(TextStyle.Secondary);
            int titleW = (int)ui.Type.Measure("ANDIJON: TAXTLAR JANGI", TextStyle.MainTitle).X + 72;
            CloudLoginState st = _cloud.State;
            bool extraRow = st == CloudLoginState.Error || st == CloudLoginState.WaitingForBrowser;
            int cardH = (int)(ui.Type.LineHeight(TextStyle.MainTitle) + ui.Type.LineHeight(TextStyle.Body) + 5 * capH) + (extraRow ? 4 : 3) * h + 9 * gap + 70;
            Rectangle card = Card(ui, vw, vh, Math.Max(titleW, ui.Compact ? 640 : 580), cardH, appear);

            int x = card.X + 28, w = card.Width - 56, y = card.Y + 24;
            Title(ui, sb, card, ref y, "ANDIJON: TAXTLAR JANGI", "O'z saltanatingni yarat!");
            y += gap * 2;

            bool working = Busy != null || st == CloudLoginState.CheckingToken || st == CloudLoginState.SignedIn;
            var main = new Rectangle(x, y, w, h);
            if (working)
            {
                string text = Busy ?? (st == CloudLoginState.CheckingToken ? "Profil yuklanmoqda" : "Saltanat yuklanmoqda");
                ui.RoundedBorder(main, T.Border, T.SurfaceAlt, 8);
                Spinner(ui, new Vector2(main.X + 22, main.Center.Y), time);
                ui.Type.DrawFitted(sb, text + Dots(time), new Rectangle(main.X + 40, main.Y, main.Width - 48, main.Height), TextStyle.Button, T.TextPrimary, 1);
                y += h + gap;
            }
            else if (st == CloudLoginState.WaitingForBrowser)
            {
                ui.RoundedBorder(main, SoftBlue, T.SurfaceAlt, 8);
                Spinner(ui, new Vector2(main.X + 22, main.Center.Y), time);
                string wait = _cloud.UsingNativePicker ? "Google akkauntini tanlang" : "Brauzerda Google orqali kiring";
                ui.Type.DrawFitted(sb, wait + Dots(time), new Rectangle(main.X + 40, main.Y, main.Width - 48, main.Height), TextStyle.Button, T.TextPrimary, 1);
                y += h + gap;
                if (_cloud.UsingNativePicker)
                {
                    if (ui.Button(new Rectangle(x, y, w, h - 8), "BEKOR QILISH", ButtonKind.Ghost, true)) _cloud.CancelLogin();
                }
                else
                {
                    int bw = (w - gap) / 2;
                    if (ui.Button(new Rectangle(x, y, bw, h - 8), "BRAUZERNI QAYTA OCHISH", ButtonKind.Secondary, true)) _cloud.StartGoogleLogin(_server);
                    if (ui.Button(new Rectangle(x + bw + gap, y, bw, h - 8), "BEKOR QILISH", ButtonKind.Ghost, true)) _cloud.CancelLogin();
                    int cy = y + (h - 8) + 6;
                    if (ui.Button(new Rectangle(x, cy, w, h - 8), _copied ? "HAVOLA NUSXALANDI" : "HAVOLANI NUSXALASH",
                            ButtonKind.Ghost, !string.IsNullOrEmpty(_cloud.LoginUrl)))
                        _copied = AndijonSaltanatlar.Core.Clipboard.TrySet(_cloud.LoginUrl);
                    y = cy;
                }
                y += h + gap;
            }
            else
            {
                if (GoogleButton(ui, sb, main, "GOOGLE ORQALI KIRISH")) { ui.FocusId = null; _cloud.StartGoogleLogin(_server); }
                y += h + gap;
                // Serversiz: internet va server bo'lmasa ham o'ynash (online imkoniyatlarsiz)
                if (ui.Button(new Rectangle(x, y, w, h - 6), "SERVERSIZ O'YNASH", ButtonKind.Secondary, true, "Internet va serversiz — o'yin shu qurilmada saqlanadi"))
                { ui.FocusId = null; OfflineRequested?.Invoke(); }
                y += h - 6 + gap;
            }

            string msg = _cloud.Message;
            if (string.IsNullOrEmpty(msg) && st == CloudLoginState.SignedOut)
                msg = "Google bilan kirsangiz — saltanat serverda saqlanadi va online o'ynaysiz. Serversiz rejimda o'yin shu qurilmada saqlanadi.";
            if (!string.IsNullOrEmpty(msg))
                y += (int)ui.Paragraph(msg, new Rectangle(x, y, w, (int)(capH * 3)), TextStyle.Secondary, _cloud.MessageIsError ? T.Danger : T.TextSecondary, 3) + gap;

            if (st == CloudLoginState.Error && !working)
            {
                if (ui.Button(new Rectangle(x, y, w, h - 8), "QAYTA URINISH", ButtonKind.Secondary, true))
                {
                    if (SecureTokenStorage.Load() != null || !string.IsNullOrEmpty(StoredAuth.Load()?.Token)) _cloud.TryResume(_server);
                    else _cloud.StartGoogleLogin(_server);
                }
                y += h + gap;
            }

            // Pastki qator: havolalar va server manzili
            int ly = card.Bottom - (int)capH - 14;
            string l1 = "MAXFIYLIK SIYOSATI", l2 = "FOYDALANISH SHARTLARI";
            float w1 = ui.Type.Measure(l1, TextStyle.Caption).X, w2 = ui.Type.Measure(l2, TextStyle.Caption).X;
            var r1 = new Rectangle(x, ly, (int)w1 + 4, (int)capH);
            var r2 = new Rectangle(r1.Right + 22, ly, (int)w2 + 4, (int)capH);
            ui.Text(l1, new Vector2(r1.X, r1.Y), TextStyle.Caption, ui.Hover(r1) ? Gold : SoftBlue);
            ui.Text("·", new Vector2(r1.Right + 8, ly), TextStyle.Caption, T.TextSecondary);
            ui.Text(l2, new Vector2(r2.X, r2.Y), TextStyle.Caption, ui.Hover(r2) ? Gold : SoftBlue);
            if (ui.Clicked(r1)) CloudSession.OpenUrl(CloudSession.NormalizeUrl(_server) + "/privacy");
            if (ui.Clicked(r2)) CloudSession.OpenUrl(CloudSession.NormalizeUrl(_server) + "/terms");

            int sy = ly - h - 4;
            if (!working && st != CloudLoginState.WaitingForBrowser && sy > y - 4)
            {
                if (_editServer)
                {
                    int lw = (int)ui.Type.Measure("Server:", TextStyle.Secondary).X + 10;
                    ui.Text("Server:", new Vector2(x, sy + (h - 8 - capH) / 2), TextStyle.Secondary, T.TextSecondary);
                    _server = ui.TextInput(new Rectangle(x + lw, sy, w - lw, h - 8), "login_server", _server, "https://server-manzili yoki 192.168.1.5:5080", false, AndroidKeyboard);
                }
                else
                {
                    string label = "Server: " + (string.IsNullOrEmpty(_server) ? "-" : _server);
                    int bw = Math.Min(w / 2, (int)ui.Type.Measure("O'zgartirish", TextStyle.Button).X + 28);
                    ui.TextFit(label, new Rectangle(x, sy, w - bw - gap, h - 8), TextStyle.Caption, T.TextSecondary);
                    if (ui.Button(new Rectangle(x + w - bw, sy, bw, h - 8), "O'zgartirish", ButtonKind.Ghost, true)) { _editServer = true; ui.FocusId = "login_server"; }
                }
            }
        }

        // ------------------------------------------------------------------ 2) YANGI HUKMDOR
        private void DrawProfile(UiKit ui, SpriteBatch sb, int vw, int vh, float time, float appear)
        {
            ThemeTokens T = ui.T;
            int h = ui.ControlH + (ui.IsTouch ? 8 : 10), gap = ui.Gap;
            float capH = ui.Type.LineHeight(TextStyle.Secondary);
            int av = ui.IsTouch ? 64 : 56;
            int cardH = (int)(ui.Type.LineHeight(TextStyle.MainTitle) + ui.Type.LineHeight(TextStyle.Body) + capH * 7) + 2 * h + av + 8 * gap + 60;
            Rectangle card = Card(ui, vw, vh, ui.Compact ? 680 : 620, cardH, appear);
            int x = card.X + 28, w = card.Width - 56, y = card.Y + 22;
            Title(ui, sb, card, ref y, "YANGI HUKMDOR", "Saltanatingiz uchun taxallus tanlang");
            y += gap;

            ui.Text("Taxallus (" + ProfileRules.MinLen + "-" + ProfileRules.MaxLen + " harf)", new Vector2(x, y), TextStyle.Secondary, T.TextSecondary);
            y += (int)capH + 4;
            _nick = ui.TextInput(new Rectangle(x, y, w, h - 6), "profile_nick", _nick, "Masalan: Bobur Mirzo", false, AndroidNickKeyboard) ?? "";
            if (_nick.Length > ProfileRules.MaxLen) _nick = _nick.Substring(0, ProfileRules.MaxLen);
            y += h + gap;

            ui.Text("Avatar", new Vector2(x, y), TextStyle.Secondary, T.TextSecondary);
            y += (int)capH + 4;
            int n = AvatarColors.Length, ag = Math.Max(6, (w - n * av) / Math.Max(1, n - 1));
            for (int i = 0; i < n; i++)
            {
                var r = new Rectangle(x + i * (av + ag), y, av, av);
                bool sel = i == _avatar;
                ui.RoundedBorder(r, sel ? Gold : T.Border, AvatarColors[i] * (sel ? 1f : 0.65f), 10);
                ui.Type.DrawFitted(sb, AvatarLetters[i], r, TextStyle.PanelTitle, Color.White, 1);
                if (ui.Clicked(r)) _avatar = i;
            }
            y += av + gap;
            ui.Paragraph("Til: O'zbek. Haqiqiy ismingiz va emailingiz boshqa o'yinchilarga ko'rsatilmaydi.", new Rectangle(x, y, w, (int)(capH * 3)), TextStyle.Caption, T.TextSecondary, 3);
            y += (int)(capH * 3) + 4;

            string clean = ProfileRules.Clean(_nick);
            string rule = ProfileRules.Validate(clean);
            string err = _profileError ?? (_nick.Length > 0 ? rule : null);
            if (err != null) ui.Paragraph(err, new Rectangle(x, y, w, (int)(capH * 2)), TextStyle.Secondary, T.Danger, 2);

            var btn = new Rectangle(x, card.Bottom - h - 18, w, h);
            if (ui.Button(btn, _profileBusy ? "SAQLANMOQDA" + Dots(time) : "DAVOM ETISH", ButtonKind.Primary, rule == null && !_profileBusy))
            {
                _profileBusy = true;
                _profileError = null;
                _cloud.SubmitProfile(clean, _avatar, e =>
                {
                    _profileBusy = false;
                    _profileError = e;
                    if (e == null) OpenShahSetup(true, clean);
                });
            }
        }

        // ------------------------------------------------------------------ 3) XUSH KELIBSIZ
        private void DrawWelcome(UiKit ui, SpriteBatch sb, int vw, int vh, float time, float appear)
        {
            ThemeTokens T = ui.T;
            int h = ui.ControlH + (ui.IsTouch ? 10 : 12), gap = ui.Gap;
            int av = ui.IsTouch ? 72 : 64;
            float capH = ui.Type.LineHeight(TextStyle.Secondary), ph = ui.Type.LineHeight(TextStyle.PanelTitle);
            int cardH = (int)(ui.Type.LineHeight(TextStyle.MainTitle) + ph + capH) + av + 3 * h + 8 * gap + 50;
            Rectangle card = Card(ui, vw, vh, ui.Compact ? 620 : 540, cardH, appear);
            int x = card.X + 28, w = card.Width - 56, y = card.Y + 22;
            Title(ui, sb, card, ref y, "XUSH KELIBSIZ!", null);
            y += gap;
            var ar = new Rectangle(card.Center.X - av / 2, y, av, av);
            int a = Math.Clamp(_cloud.AvatarId, 0, AvatarColors.Length - 1);
            ui.RoundedBorder(ar, Gold, AvatarColors[a], 12);
            ui.Type.DrawFitted(sb, AvatarLetters[a], ar, TextStyle.ScreenTitle, Color.White, 1);
            y += av + gap;
            ui.Type.DrawFitted(sb, _cloud.DisplayName ?? "", new Rectangle(x, y, w, (int)ph), TextStyle.PanelTitle, T.TextPrimary, 1);
            y += (int)ph + 2;
            ui.Type.DrawFitted(sb, _cloud.IsVip ? "VIP HUKMDOR · Barcha imkoniyatlar ochiq" : "Saltanatingiz sizni kutmoqda",
                new Rectangle(x, y, w, (int)capH), TextStyle.Secondary, _cloud.IsVip ? Gold : T.TextSecondary, 1);

            var go = new Rectangle(x, card.Bottom - 3 * h - 2 * gap - 2, w, h);
            if (Busy != null)
            {
                ui.RoundedBorder(go, T.Border, T.SurfaceAlt, 8);
                Spinner(ui, new Vector2(go.X + 22, go.Center.Y), time);
                ui.Type.DrawFitted(sb, Busy + Dots(time), new Rectangle(go.X + 40, go.Y, go.Width - 48, go.Height), TextStyle.Button, T.TextPrimary, 1);
            }
            else if (ui.Button(go, "O'YINNI DAVOM ETTIRISH", ButtonKind.Primary, true)) ContinueRequested?.Invoke();
            var shahBtn = new Rectangle(x, go.Bottom + gap, w, h - 10);
            if (ui.Button(shahBtn, "SHOH QIYOFASI: " + AndijonSaltanatlar.Core.ShahLook.Name.ToUpperInvariant(), ButtonKind.Secondary, Busy == null))
                OpenShahSetup(false, null);
            if (ui.Button(new Rectangle(x, shahBtn.Bottom + gap, w, h - 10), "BOSHQA HISOB BILAN KIRISH", ButtonKind.Ghost, Busy == null))
            {
                _cloud.SignOut();
                Mode = Stage.Login;
            }
        }

        // ------------------------------------------------------------------ 4) SHOH QIYOFASI
        /// <summary>Shoh qiyofasini sozlash bosqichini ochadi. afterProfile — yangi hisob (keyin dunyo tanlash).</summary>
        public void OpenShahSetup(bool afterProfile, string suggestedName)
        {
            _shahThenProfileDone = afterProfile;
            _shahInit = false;
            if (!AndijonSaltanatlar.Core.ShahLook.Configured && !string.IsNullOrWhiteSpace(suggestedName)) _shahName = suggestedName;
            Mode = Stage.Shah;
        }

        private void DrawShah(UiKit ui, SpriteBatch sb, int vw, int vh, float time, float appear)
        {
            if (!_shahInit)
            {
                _shahInit = true;
                if (AndijonSaltanatlar.Core.ShahLook.Configured || string.IsNullOrWhiteSpace(_shahName)) _shahName = AndijonSaltanatlar.Core.ShahLook.Name;
                _shahBody = AndijonSaltanatlar.Core.ShahLook.Body;
                _shahHeight = AndijonSaltanatlar.Core.ShahLook.Height;
            }
            ThemeTokens T = ui.T;
            int h = ui.ControlH + (ui.IsTouch ? 8 : 10), gap = ui.Gap;
            float capH = ui.Type.LineHeight(TextStyle.Secondary);
            int full = (int)(ui.Type.LineHeight(TextStyle.MainTitle) + ui.Type.LineHeight(TextStyle.Body) + capH * 4) + 5 * h + 9 * gap + 70;
            // Past ekran (telefon yotiq holatda): ixcham — sarlavha ostidagi matn va izoh yo'q, oraliqlar kichik
            bool compact = full > vh - 24;
            if (compact) { h = ui.ControlH; gap = Math.Max(4, gap / 2); }
            int cardH = compact ? (int)(ui.Type.LineHeight(TextStyle.MainTitle) + capH * 3) + 5 * h + 7 * gap + 44 : full;
            Rectangle card = Card(ui, vw, vh, ui.Compact ? 760 : 700, cardH, appear);
            int x = card.X + 28, w = card.Width - 56, y = card.Y + (compact ? 12 : 22);
            Title(ui, sb, card, ref y, "SHOHINGIZNI YARATING", compact ? null : "Shohingiz shaharda, saroyda va jangda shunday ko'rinadi");
            y += gap;

            // Chapda — oldindan ko'rinish (sahna: zamin, soya, shoh)
            int pw = Math.Min(w * 2 / 5, 260);
            int bottomBtn = card.Bottom - h - (compact ? 10 : 18);
            var prev = new Rectangle(x, y, pw, Math.Max(120, bottomBtn - gap - y));
            ui.RoundedBorder(prev, T.Border, new Color(30, 34, 44), 10);
            ui.Fill(new Rectangle(prev.X + 2, prev.Y + prev.Height * 2 / 3, prev.Width - 4, prev.Height / 3 - 2), new Color(64, 54, 40));
            Texture2D tex = ShahTexture?.Invoke();
            if (tex != null)
            {
                Vector2 bs = AndijonSaltanatlar.Core.ShahLook.ScaleOf(_shahBody, _shahHeight);
                float baseScale = prev.Height * 0.72f / Math.Max(1, tex.Height);
                var feet = new Vector2(prev.Center.X, prev.Y + prev.Height * 0.86f);
                float sh = tex.Width * baseScale * bs.X * 0.32f;
                for (int k = 0; k < 4; k++) // yumaloq soya (ellips)
                {
                    float f = 1f - k * 0.22f;
                    ui.Fill(new Rectangle((int)(feet.X - sh * f), (int)feet.Y - 4 + k, (int)(sh * 2 * f), 8 - 2 * k), new Color(0, 0, 0, 40));
                }
                float breathe = 1f + 0.012f * MathF.Sin(time * 2.2f);
                sb.Draw(tex, feet, null, Color.White, 0f, new Vector2(tex.Width / 2f, tex.Height),
                    new Vector2(baseScale * bs.X, baseScale * bs.Y * breathe), SpriteEffects.None, 0f);
            }

            // O'ngda — sozlamalar
            int rx = prev.Right + gap * 2, rw = card.Right - 28 - rx, ry = y;
            ui.Text("Shoh ismi", new Vector2(rx, ry), TextStyle.Secondary, T.TextSecondary);
            ry += (int)capH + 4;
            _shahName = ui.TextInput(new Rectangle(rx, ry, rw, h - 6), "shah_name", _shahName, "Masalan: Zahiriddin", false, AndroidShahKeyboard) ?? "";
            if (_shahName.Length > AndijonSaltanatlar.Core.ShahLook.NameMax) _shahName = _shahName.Substring(0, AndijonSaltanatlar.Core.ShahLook.NameMax);
            ry += h + gap;

            ry = Choice(ui, "Tana tuzilishi", AndijonSaltanatlar.Core.ShahLook.BodyNames, ref _shahBody, rx, ry, rw, h, gap, capH);
            ry = Choice(ui, "Bo'yi", AndijonSaltanatlar.Core.ShahLook.HeightNames, ref _shahHeight, rx, ry, rw, h, gap, capH);
            if (!compact && ry + capH * 2 < bottomBtn)
                ui.Paragraph("Keyin ham o'zgartirsa bo'ladi: kirish oynasida \"SHOH QIYOFASI\" tugmasi.",
                    new Rectangle(rx, ry, rw, Math.Min((int)(capH * 4), bottomBtn - ry - 4)), TextStyle.Caption, T.TextSecondary, 4);

            var btn = new Rectangle(x, bottomBtn, w, h);
            if (ui.Button(btn, "TAYYOR", ButtonKind.Primary, true))
            {
                AndijonSaltanatlar.Core.ShahLook.Set(_shahName, _shahBody, _shahHeight);
                ShahChanged?.Invoke();
                if (_shahThenProfileDone) ProfileDone?.Invoke();
                else Mode = Stage.Welcome;
            }
        }

        /// <summary>Shoh qiyofasi o'zgardi (Game1 shahardagi shoh figurasini yangilaydi).</summary>
        public Action ShahChanged;

        private static int Choice(UiKit ui, string label, string[] options, ref int value, int x, int y, int w, int h, int gap, float capH)
        {
            ui.Text(label, new Vector2(x, y), TextStyle.Secondary, ui.T.TextSecondary);
            y += (int)capH + 4;
            int n = options.Length, bw = (w - (n - 1) * gap) / n;
            for (int i = 0; i < n; i++)
            {
                var r = new Rectangle(x + i * (bw + gap), y, bw, h - 8);
                if (ui.Button(r, options[i].ToUpperInvariant(), i == value ? ButtonKind.Primary : ButtonKind.Secondary, true)) value = i;
            }
            return y + h + gap;
        }

        private void AndroidShahKeyboard()
        {
#if ANDROID
            Task<string> t = KeyboardInput.Show("Shoh ismi", "1-18 harf", _shahName, false);
            t.ContinueWith(r => { if (r.Status == TaskStatus.RanToCompletion && r.Result != null) _pendingKeyboard = r.Result; });
#endif
        }

        // ------------------------------------------------------------------ yordamchilar
        private static string Dots(float time) => new string('.', 1 + (int)(time * 2.5f) % 3);

        private static void Spinner(UiKit ui, Vector2 c, float time)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * MathF.PI * 2f + time * 5f;
                var p = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 8f;
                ui.Fill(new Rectangle((int)p.X - 2, (int)p.Y - 2, 4, 4), Gold * ((i + 1) / 8f));
            }
        }

        /// <summary>"Sign in with Google" uslubidagi oq tugma: to'rt rangli "G" belgisi + matn.</summary>
        private static bool GoogleButton(UiKit ui, SpriteBatch sb, Rectangle r, string label)
        {
            bool hover = ui.Hover(r);
            Color fill = hover ? new Color(240, 243, 248) : Color.White;
            ui.RoundedBorder(r, hover ? new Color(66, 133, 244) : new Color(218, 220, 224), fill, 8);
            int g = Math.Max(18, (int)(r.Height * 0.5f));
            float tw = Math.Min(ui.Type.Measure(label, TextStyle.Button).X, r.Width - g - 40);
            float total = g + 12 + tw;
            float sx = r.X + (r.Width - total) / 2f;
            DrawGLogo(ui, new Rectangle((int)sx, r.Y + (r.Height - g) / 2, g, g));
            ui.Type.DrawFitted(sb, label, new Rectangle((int)(sx + g + 12), r.Y, (int)(r.Right - 12 - (sx + g + 12)), r.Height), TextStyle.Button, new Color(31, 31, 31), 0);
            return ui.Clicked(r);
        }

        private static void DrawGLogo(UiKit ui, Rectangle r)
        {
            int s = r.Width, t = Math.Max(3, s / 5);
            Color red = new Color(234, 67, 53), yellow = new Color(251, 188, 5), green = new Color(52, 168, 83), blue = new Color(66, 133, 244);
            ui.Fill(new Rectangle(r.X + t, r.Y, s - 2 * t, t), red);
            ui.Fill(new Rectangle(r.X, r.Y + t, t, s - 2 * t), yellow);
            ui.Fill(new Rectangle(r.X + t, r.Bottom - t, s - 2 * t, t), green);
            ui.Fill(new Rectangle(r.Right - t, r.Y + s / 2, t, s / 2 - t), blue);
            ui.Fill(new Rectangle(r.X + s / 2, r.Y + s / 2 - t / 2, s / 2, t), blue);
        }

        private void AndroidKeyboard()
        {
#if ANDROID
            Task<string> t = KeyboardInput.Show("Server manzili", "", _server, false);
            t.ContinueWith(r => { if (r.Status == TaskStatus.RanToCompletion && r.Result != null) _pendingKeyboard = r.Result; });
#endif
        }

        private void AndroidNickKeyboard()
        {
#if ANDROID
            Task<string> t = KeyboardInput.Show("Taxallus", "3-16 harf", _nick, false);
            t.ContinueWith(r => { if (r.Status == TaskStatus.RanToCompletion && r.Result != null) _pendingKeyboard = r.Result; });
#endif
        }
    }
}
