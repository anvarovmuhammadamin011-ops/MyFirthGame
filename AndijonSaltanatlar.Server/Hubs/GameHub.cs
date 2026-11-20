using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using AndijonSaltanatlar.Shared.DTOs;
using AndijonSaltanatlar.Server.Services;

namespace AndijonSaltanatlar.Server.Hubs
{
    /// <summary>
    /// Real-vaqt yangilanishlar (SignalR). Butun xarita emas, faqat o'zgarishlar yuboriladi.
    /// Client dunyoga qo'shiladi (JoinWorld) va shu dunyo guruhiga tegishli xabarlarni oladi.
    ///
    /// Server → client xabarlari:
    ///   "MarketChanged"  (worldId)        — bozorda taklif qo'shildi/sotildi/bekor qilindi;
    ///   "StateChanged"   (kingdomId, ver) — shu foydalanuvchi saltanati holati o'zgardi (masalan guruchi sotildi);
    ///   "PlayersChanged" (worldId)        — dunyoga kimdir kirdi/chiqdi.
    /// Client hech qachon hub orqali holat o'zgartirmaydi — faqat REST buyruqlar (server tekshiradi).
    /// </summary>
    [Authorize]
    public class GameHub : Hub
    {
        private static readonly ConcurrentDictionary<string, (int world, string name)> Connections = new();
        /// <summary>Ulangan foydalanuvchilar (connection → foydalanuvchi id) — AI hujumi faqat o'yindagi o'yinchiga qaratiladi.</summary>
        private static readonly ConcurrentDictionary<string, string> ConnectionUsers = new();

        /// <summary>Foydalanuvchi hozir o'yinda (kamida bitta ulanish bor)mi.</summary>
        public static bool IsUserOnline(int userId)
        {
            string id = userId.ToString();
            foreach (string u in ConnectionUsers.Values) if (u == id) return true;
            return false;
        }

        // Dunyo umumiy chati — oxirgi xabarlar (xotirada, dunyo bo'yicha). Server qayta ishga tushsa tarix tozalanadi.
        private const int ChatHistoryMax = 50;
        private const int ChatMaxLength = 200;
        private static readonly ConcurrentDictionary<int, List<ChatMessageDto>> Chat = new();

        public static string WorldGroup(int worldId) => "world-" + worldId;

        public static List<string> OnlinePlayers(int worldId)
            => Connections.Values.Where(v => v.world == worldId).Select(v => v.name).Distinct().OrderBy(n => n).ToList();

        public async Task JoinWorld(int worldId)
        {
            string name = Context.User?.FindFirst(ClaimTypes.Name)?.Value ?? "?";
            Connections[Context.ConnectionId] = (worldId, name);
            if (Context.UserIdentifier != null) ConnectionUsers[Context.ConnectionId] = Context.UserIdentifier;
            await Groups.AddToGroupAsync(Context.ConnectionId, WorldGroup(worldId));
            await Clients.Group(WorldGroup(worldId)).SendAsync("PlayersChanged", worldId);
            // Qo'shilgan o'yinchiga oxirgi chat xabarlarini yuboramiz
            List<ChatMessageDto> history = Chat.TryGetValue(worldId, out List<ChatMessageDto> h) ? h : null;
            if (history != null)
            {
                ChatMessageDto[] snapshot;
                lock (history) snapshot = history.ToArray();
                await Clients.Caller.SendAsync("ChatHistory", snapshot);
            }
        }

        /// <summary>Dunyo umumiy chatiga xabar yuborish (uzunlik va nomaqbul so'z filtri bilan).</summary>
        public async Task SendChat(int worldId, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if (text.Length > ChatMaxLength) text = text.Substring(0, ChatMaxLength);
            if (ProfanityFilter.Contains(text)) return; // nomaqbul so'z — yuborilmaydi
            string name = Context.User?.FindFirst(ClaimTypes.Name)?.Value ?? "?";
            var msg = new ChatMessageDto { Name = name, Text = text, SentAt = DateTime.UtcNow };
            List<ChatMessageDto> history = Chat.GetOrAdd(worldId, _ => new List<ChatMessageDto>());
            lock (history)
            {
                history.Add(msg);
                if (history.Count > ChatHistoryMax) history.RemoveRange(0, history.Count - ChatHistoryMax);
            }
            await Clients.Group(WorldGroup(worldId)).SendAsync("ChatMessage", msg);
        }

        public async Task LeaveWorld(int worldId)
        {
            Connections.TryRemove(Context.ConnectionId, out _);
            ConnectionUsers.TryRemove(Context.ConnectionId, out _);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, WorldGroup(worldId));
            await Clients.Group(WorldGroup(worldId)).SendAsync("PlayersChanged", worldId);
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            ConnectionUsers.TryRemove(Context.ConnectionId, out _);
            if (Connections.TryRemove(Context.ConnectionId, out var info))
                await Clients.Group(WorldGroup(info.world)).SendAsync("PlayersChanged", info.world);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
