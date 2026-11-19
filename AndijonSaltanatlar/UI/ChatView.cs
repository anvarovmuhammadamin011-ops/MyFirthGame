using System;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.UI.Design;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace AndijonSaltanatlar.UI
{
    /// <summary>
    /// #2 — dunyo umumiy chati: real vaqtda (SignalR) xabar almashish. Barcha o'yinchilar bitta
    /// dunyo chatini ko'radi. Server uzunlik va nomaqbul so'z filtrini qo'llaydi.
    /// </summary>
    public sealed partial class OnlinePanel
    {
        private bool _chatOpen;
        private string _chatDraft = "";
        private float _chatScroll;
        private int _chatSeenSerial = -1;
        private const int ChatMaxLength = 200;

        private void OpenChat()
        {
            _chatOpen = true;
            _client.ChatOpen = true;
            _client.MarkChatRead();
            _chatSeenSerial = -1; // ochilganda pastga (eng yangi xabarga) tushamiz
        }

        private void CloseChat()
        {
            _chatOpen = false;
            _client.ChatOpen = false;
            if (_ui.FocusId == "chat") _ui.FocusId = null;
        }

        private void DrawChatOverlay(int vw, int vh)
        {
            if (!_chatOpen) return;
            UiKit ui = _ui; ThemeTokens T = ui.T;
            _client.ChatOpen = true;
            _client.MarkChatRead();

            Rectangle m = ui.Modal(560, 560, "Dunyo chati");
            int x = m.X + 20, w = m.Width - 40, h = ui.ControlH;
            if (ui.IconButton(new Rectangle(m.Right - h - 8, m.Y + 10, h, h), "×", "Yopish (Esc)")) { CloseChat(); return; }

            int inputY = m.Bottom - h - 16;
            var view = new Rectangle(x, m.Y + 64, w, inputY - (m.Y + 64) - 10);
            var log = _client.ChatLog;
            float lh = ui.Type.LineHeight(TextStyle.Secondary);

            int LinesOf(ChatMessageDto c) => Math.Clamp((int)Math.Ceiling(ui.Type.Measure(c.Text ?? "", TextStyle.Secondary).X / Math.Max(1, w - 24)), 1, 5);
            int HeightOf(ChatMessageDto c) => (int)lh + (int)(LinesOf(c) * lh) + 12;
            int gap = 6, contentH = 0;
            for (int i = 0; i < log.Count; i++) contentH += HeightOf(log[i]) + gap;
            int maxScroll = Math.Max(0, contentH - view.Height);

            // Yangi xabar kelganda yoki oyna yangi ochilganda — pastga yopishamiz
            if (_client.ChatSerial != _chatSeenSerial) { _chatSeenSerial = _client.ChatSerial; _chatScroll = maxScroll; }
            if (ui.Hover(view) && _input != null)
            {
                _chatScroll -= _input.WheelDelta * 0.4f;
                if (_input.IsPanDragging) _chatScroll -= _input.MouseDelta.Y;
            }
            _chatScroll = MathHelper.Clamp(_chatScroll, 0, maxScroll);

            if (log.Count == 0)
                ui.Paragraph("Hali xabar yo'q. Birinchi bo'lib salom yozing!", view, TextStyle.Body, T.TextSecondary);

            ui.BeginClip(view);
            int cy = view.Y - (int)_chatScroll;
            string me = _client.Settings.Username ?? "";
            foreach (ChatMessageDto c in log)
            {
                int ch = HeightOf(c);
                var r = new Rectangle(view.X, cy, view.Width, ch);
                cy += ch + gap;
                if (r.Bottom < view.Y || r.Y > view.Bottom) continue;
                bool mine = me.Length > 0 && string.Equals(c.Name, me, StringComparison.OrdinalIgnoreCase);
                ui.TextFit(c.Name + "  ·  " + c.SentAt.ToLocalTime().ToString("HH:mm"),
                    new Rectangle(r.X + 4, r.Y, r.Width - 8, (int)lh), TextStyle.Caption, mine ? T.Accent : T.Secondary);
                ui.Paragraph(c.Text, new Rectangle(r.X + 4, r.Y + (int)lh, r.Width - 8, ch - (int)lh - 6), TextStyle.Secondary, T.TextPrimary, 5);
            }
            ui.EndClip();

            // Pastki kiritish qatori + yuborish
            int sendW = (int)ui.Type.Measure("Yuborish", TextStyle.Button).X + 28;
            _chatDraft = ui.TextInput(new Rectangle(x, inputY, w - sendW - ui.Gap, h), "chat", _chatDraft, "Xabar yozing…", false,
                () => AndroidKeyboard("chat", "Chat xabari", _chatDraft, false), ChatMaxLength);
            bool send = ui.Button(new Rectangle(x + w - sendW, inputY, sendW, h), "Yuborish", ButtonKind.Primary, _chatDraft.Trim().Length > 0);
            bool enter = _ui.FocusId == "chat" && _input != null && _input.KeyPressed(Keys.Enter);
            if ((send || enter) && _chatDraft.Trim().Length > 0)
            {
                _client.SendChat(_chatDraft);
                _chatDraft = "";
            }
        }
    }
}
