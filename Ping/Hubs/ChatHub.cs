using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Ping.Models;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace SignalRChat.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly PingdbContext _context;
        public static readonly ConcurrentDictionary<int, int> OnlineUsers = new();

        public ChatHub(PingdbContext context) => _context = context;

        public override async Task OnConnectedAsync()
        {
            var userIdStr = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdStr, out int userId))
            {
                var currentConnections = OnlineUsers.AddOrUpdate(userId, 1, (key, count) => count + 1);

                if (currentConnections == 1)
                {
                    await Clients.All.SendAsync("UserOnlineStatus", userId, true);
                }
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userIdStr = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdStr, out int userId))
            {
                if (OnlineUsers.TryGetValue(userId, out int count))
                {
                    if (count <= 1)
                    {
                        OnlineUsers.TryRemove(userId, out _);
                        await Clients.All.SendAsync("UserOnlineStatus", userId, false);
                    }
                    else
                    {
                        OnlineUsers[userId] = count - 1;
                    }
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinChat(string chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, chatId);
        }

        public async Task SendMessage(int chatId, int senderId, string content)
        {
            var msg = new Message
            {
                ChatId = chatId,
                SenderId = senderId,
                Content = content,
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            };

            _context.Messages.Add(msg);
            await _context.SaveChangesAsync();

            await Clients.Group(chatId.ToString()).SendAsync(
                "ReceiveMessage",
                chatId,
                senderId,
                msg.Id,
                content,
                msg.CreatedAt.ToLocalTime().ToString("HH:mm")
            );
        }

        public async Task ReadChat(int chatId, int userId)
        {
            var unreadMessages = await _context.Messages
                .Where(m => m.ChatId == chatId && m.SenderId != userId && !m.IsRead)
                .ToListAsync();

            if (unreadMessages.Any())
            {
                foreach (var msg in unreadMessages)
                {
                    msg.IsRead = true;
                }
                await _context.SaveChangesAsync();

                await Clients.Group(chatId.ToString()).SendAsync("ChatRead", chatId, userId);
            }
        }

        public async Task ToggleReaction(int messageId, int userId, string emoji)
        {
            var msg = await _context.Messages.FindAsync(messageId);
            if (msg == null) return;

            var existing = await _context.MessageReactions
                .FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == userId && r.Emoji == emoji);

            if (existing != null)
            {
                _context.MessageReactions.Remove(existing);
            }
            else
            {
                _context.MessageReactions.Add(new MessageReaction
                {
                    MessageId = messageId,
                    UserId = userId,
                    Emoji = emoji,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _context.SaveChangesAsync();

            var reactions = await _context.MessageReactions
                .Where(r => r.MessageId == messageId)
                .GroupBy(r => r.Emoji)
                .Select(g => new {
                    emoji = g.Key,
                    count = g.Count(),
                    userIds = g.Select(r => r.UserId).ToList()
                })
                .ToListAsync();

            await Clients.Group(msg.ChatId.ToString())
                .SendAsync("ReactionUpdated", messageId, reactions);
        }
    }
}