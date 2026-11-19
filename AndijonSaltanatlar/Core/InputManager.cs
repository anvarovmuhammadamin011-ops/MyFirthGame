using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace AndijonSaltanatlar.Core
{
    /// <summary>
    /// Klaviatura, sichqoncha va SENSORLI EKRAN holatini kadrma-kadr kuzatadi.
    /// Boshqa tizimlar xom MonoGame holatiga emas, shu klassga murojaat qiladi.
    ///
    /// PROMPT 19A (Android): sensor sichqonchaga o'giriladi —
    ///   bir barmoq bilan tegib qo'yib yuborish  → chap tugma bosilishi (tap = tanlash / tugma);
    ///   bir barmoq bilan sudrash               → kamerani surish (IsPanDragging + MouseDelta);
    ///   ikki barmoq (pinch)                   → zoom (WheelDelta).
    /// PC'da sichqoncha va klaviatura avvalgidek ishlaydi.
    /// </summary>
    public sealed class InputManager
    {
        private const int TapSlop = 14;          // shu pikseldan ko'p siljisa — tap emas, sudrash
        private const float PinchToWheel = 3f;   // pinch masofa o'zgarishi -> g'ildirak birligi

        private KeyboardState _kb, _prevKb;
        private MouseState _ms, _prevMs;
        private Point _rightPressPos;
        private bool _rightDragged;

        // --- Sensor holati ---
        private bool _touchMode;
        private bool _touchDown;
        private Point _tPos, _tPrevPos, _tStart;
        private bool _tMoved, _tPanning, _tap;
        private int _pinchDelta;
        private float _pinchDist = -1f;

        // --- Matn kiritish (Window.TextInput yoki Android ekran klaviaturasi) ---
        private readonly Queue<char> _text = new Queue<char>();

        // --- EKRANDAGI BOSHQARUV (telefon): joystik + tugmalar -> virtual klaviatura tugmalari ---
        private readonly HashSet<Keys> _vk = new HashSet<Keys>(), _prevVk = new HashSet<Keys>();
        /// <summary>Shu kadrda ekrandagi tezkor tugma bosgan klaviatura tugmalari (bir martalik bosish).</summary>
        private readonly HashSet<Keys> _injected = new HashSet<Keys>();

        /// <summary>Telefon: ekrandagi tugma klaviatura tugmasini shu kadr uchun "bosadi" (M, F, Enter, K ...).</summary>
        public void InjectPress(Keys k) => _injected.Add(k);
        /// <summary>Jangchi ichida / saroy jangida: ekranda joystik va tugmalar (Game1 har kadr o'rnatadi).</summary>
        public bool TouchPadEnabled { get; set; }
        /// <summary>Ekran o'lchami (joystik va tugmalar joylashuvi uchun).</summary>
        public Point TouchPadSize { get; set; } = new Point(1280, 720);
        /// <summary>Joystik og'ishi (-1..1), chizish uchun.</summary>
        public Vector2 StickVector { get; private set; }
        public bool StickActive { get; private set; }

        /// <summary>Joystik markazi va radiusi (ekran pikselida).</summary>
        public void StickGeometry(out Vector2 center, out float radius)
        {
            radius = Math.Max(66f, TouchPadSize.Y * 0.15f);
            center = new Vector2(radius * 1.45f, TouchPadSize.Y - radius * 1.45f);
        }

        /// <summary>Tugmalar: 0 — ZARBA (Space), 1 — CHIQISH (F), 2 — BOSHQASI (E), 3 — YUGURISH (Shift).</summary>
        public Rectangle PadButton(int i, out string label, out Keys key)
        {
            float r = Math.Max(66f, TouchPadSize.Y * 0.15f);
            int big = (int)(r * 1.25f), small = (int)(r * 1.2f);
            int W = TouchPadSize.X, H = TouchPadSize.Y;
            switch (i)
            {
                case 0: label = "ZARBA"; key = Keys.Space; return new Rectangle(W - big - (int)(r * 0.45f), H - big - (int)(r * 0.45f), big, big);
                case 1: label = "CHIQ"; key = Keys.F; return new Rectangle(W - small - (int)(r * 0.45f), H - big - small - (int)(r * 0.8f), small, small);
                case 2: label = "BOSHQA"; key = Keys.E; return new Rectangle(W - big - small - (int)(r * 0.8f), H - small - (int)(r * 0.45f), small, small);
                default: label = "YUGUR"; key = Keys.LeftShift; return new Rectangle(W - big - small - (int)(r * 0.8f), H - 2 * small - (int)(r * 0.8f), small, small);
            }
        }

        /// <summary>Barmoq ekrandagi boshqaruv ustidami (bunday teginish tap/sudrash hisoblanmaydi).</summary>
        private bool ConsumeByPad(Vector2 p)
        {
            if (!TouchPadEnabled) return false;
            StickGeometry(out Vector2 c, out float r);
            Vector2 d = p - c;
            if (d.Length() <= r * 1.6f)
            {
                Vector2 v = d / r;
                if (v.Length() > 1f) v.Normalize();
                StickVector = v;
                StickActive = true;
                if (v.X < -0.35f) _vk.Add(Keys.A);
                if (v.X > 0.35f) _vk.Add(Keys.D);
                if (v.Y < -0.35f) _vk.Add(Keys.W);
                if (v.Y > 0.35f) _vk.Add(Keys.S);
                return true;
            }
            for (int i = 0; i < 4; i++)
            {
                Rectangle b = PadButton(i, out _, out Keys k);
                b.Inflate(10, 10);
                if (b.Contains((int)p.X, (int)p.Y)) { _vk.Add(k); return true; }
            }
            return false;
        }

        /// <summary>Ekrandagi tugma hozir bosilganmi (chizishda yoritish uchun).</summary>
        public bool PadKeyHeld(Keys k) => _vk.Contains(k);

        /// <summary>UI har kadr o'rnatadi: sichqoncha panel ustidami?</summary>
        public bool MouseOverUI { get; set; }

        /// <summary>Oxirgi kiritish sensordan keldimi (UI katta tugmalar uchun).</summary>
        public bool IsTouch => _touchMode || ForceTouchUi;

        /// <summary>Sinov: PC'da telefon interfeysini ko'rsatish (katta tugmalar, telefon masshtabi).</summary>
        public bool ForceTouchUi { get; set; }

        /// <summary>
        /// Eski piksel-shriftli panellar virtual o'lchamda chiziladi (telefonda 720p balandlik). Ular yangilanayotganda
        /// Game1 shu koeffitsientni o'rnatadi — kursor/barmoq koordinatalari panel koordinatalariga o'giriladi.
        /// </summary>
        public float PointerScale { get; set; } = 1f;

        private Point RawPosition => _touchMode ? _tPos : _ms.Position;
        public Point MousePosition => PointerScale == 1f ? RawPosition : new Point((int)(RawPosition.X / PointerScale), (int)(RawPosition.Y / PointerScale));
        public Vector2 MouseVector => new Vector2(RawPosition.X, RawPosition.Y) / PointerScale;
        public Point MouseDelta => _touchMode ? (_tPanning ? _tPos - _tPrevPos : Point.Zero) : _ms.Position - _prevMs.Position;
        public int WheelDelta => _touchMode ? _pinchDelta : _ms.ScrollWheelValue - _prevMs.ScrollWheelValue;

        public bool LeftDown => _touchMode ? _tap : _ms.LeftButton == ButtonState.Pressed;
        public bool LeftPressed => _touchMode ? _tap : _ms.LeftButton == ButtonState.Pressed && _prevMs.LeftButton == ButtonState.Released;
        public bool LeftReleased => _touchMode ? _tap : _ms.LeftButton == ButtonState.Released && _prevMs.LeftButton == ButtonState.Pressed;
        public bool RightDown => !_touchMode && _ms.RightButton == ButtonState.Pressed;
        public bool MiddleDown => !_touchMode && _ms.MiddleButton == ButtonState.Pressed;

        /// <summary>O'ng tugma bosilib, sudralmasdan qo'yib yuborildi (bekor qilish uchun).</summary>
        public bool RightClicked { get; private set; }

        /// <summary>O'ng yoki o'rta tugma (yoki bir barmoq) bilan xaritani sudrash davom etyaptimi.</summary>
        public bool IsPanDragging => _touchMode ? _tPanning : (MiddleDown || (RightDown && _rightDragged));

        public void Update(bool windowActive)
        {
            _injected.Clear();
            _prevKb = _kb;
            _prevMs = _ms;
            if (windowActive)
            {
                _kb = Keyboard.GetState();
                _ms = Mouse.GetState();
            }
            else
            {
                _kb = default;
                // Sichqoncha pozitsiyasi saqlanadi, tugmalar "qo'yib yuborilgan" deb olinadi.
                _ms = new MouseState(_ms.X, _ms.Y, _ms.ScrollWheelValue,
                    ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
            }

            UpdateTouch(windowActive);
            bool back = false;
            try { back = windowActive && GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed; } catch { }
            BackPressed = back && !_backDown;
            _backDown = back;
            if (LeftPressed) _clickLatch = true;

            RightClicked = false;
            bool rightNow = _ms.RightButton == ButtonState.Pressed;
            bool rightBefore = _prevMs.RightButton == ButtonState.Pressed;
            if (rightNow && !rightBefore)
            {
                _rightPressPos = _ms.Position;
                _rightDragged = false;
            }
            else if (rightNow)
            {
                Point d = _ms.Position - _rightPressPos;
                if (Math.Abs(d.X) + Math.Abs(d.Y) > 6) _rightDragged = true;
            }
            else if (rightBefore)
            {
                RightClicked = !_rightDragged;
                _rightDragged = false;
            }
        }

        private void UpdateTouch(bool windowActive)
        {
            _tap = false;
            _pinchDelta = 0;
            _tPrevPos = _tPos;
            _prevVk.Clear();
            foreach (Keys k in _vk) _prevVk.Add(k);
            _vk.Clear();
            StickActive = false;
            StickVector = Vector2.Zero;

            TouchCollection touches;
            try { touches = windowActive ? TouchPanel.GetState() : default; }
            catch { touches = default; } // sensor qo'llab-quvvatlanmaydigan platforma

            int count = 0;
            bool any = false;
            Vector2 a = Vector2.Zero, b = Vector2.Zero;
            foreach (TouchLocation t in touches)
            {
                if (t.State != TouchLocationState.Pressed && t.State != TouchLocationState.Moved) continue;
                any = true;
                if (ConsumeByPad(t.Position)) continue; // joystik/tugma — xaritani surmaydi, tap emas
                if (count == 0) a = t.Position; else if (count == 1) b = t.Position;
                count++;
            }
            if (any) _touchMode = true;

            if (count > 0)
            {
                _touchMode = true;
                if (count >= 2)
                {
                    // Ikki barmoq: zoom. Tap bo'lmaydi.
                    float dist = Vector2.Distance(a, b);
                    if (_pinchDist > 0f) _pinchDelta = (int)((dist - _pinchDist) * PinchToWheel);
                    _pinchDist = dist;
                    _tMoved = true;
                    _tPanning = false;
                    Vector2 mid = (a + b) * 0.5f;
                    _tPos = new Point((int)mid.X, (int)mid.Y);
                    _tPrevPos = _tPos;
                }
                else
                {
                    _pinchDist = -1f;
                    var p = new Point((int)a.X, (int)a.Y);
                    if (!_touchDown) { _tStart = p; _tPrevPos = p; _tMoved = false; }
                    _tPos = p;
                    if (!_tMoved && Math.Abs(p.X - _tStart.X) + Math.Abs(p.Y - _tStart.Y) > TapSlop) _tMoved = true;
                    _tPanning = _tMoved;
                }
                _touchDown = true;
            }
            else
            {
                if (_touchDown && !_tMoved) _tap = true; // barmoq siljimasdan ko'tarildi — tap
                _touchDown = false;
                _tPanning = false;
                _pinchDist = -1f;
                // Sichqoncha qimirlasa yoki bosilsa — PC rejimiga qaytamiz.
                if (_touchMode && !_tap && !any && (_ms.Position != _prevMs.Position || _ms.LeftButton == ButtonState.Pressed)) _touchMode = false;
            }
        }

        private bool _clickLatch;

        /// <summary>
        /// PROMPT 19B UI (Draw bosqichida ishlaydigan komponentlar) uchun: oxirgi chizishdan beri bosish bo'ldimi.
        /// Bir kadrga bir nechta Update to'g'ri kelsa ham bosish yo'qolmaydi. O'qilgach tozalanadi.
        /// </summary>
        public bool TakeClick()
        {
            bool c = _clickLatch;
            _clickLatch = false;
            return c;
        }

        public bool KeyDown(Keys k) => _kb.IsKeyDown(k) || _vk.Contains(k);
        public bool KeyPressed(Keys k) => (_kb.IsKeyDown(k) && !_prevKb.IsKeyDown(k)) || (_vk.Contains(k) && !_prevVk.Contains(k)) || _injected.Contains(k) || (k == Keys.Escape && BackPressed);

        /// <summary>Android "Orqaga" tugmasi shu kadrda bosildi (Esc kabi ishlaydi: panelni yopadi, o'yindan chiqmaydi).</summary>
        public bool BackPressed { get; private set; }
        private bool _backDown;

        // ---- Matn kiritish ----
        public void PushText(char c)
        {
            if (_text.Count < 64) _text.Enqueue(c);
        }

        public bool TryReadChar(out char c)
        {
            if (_text.Count > 0) { c = _text.Dequeue(); return true; }
            c = '\0';
            return false;
        }

        public void ClearText() => _text.Clear();
    }
}
