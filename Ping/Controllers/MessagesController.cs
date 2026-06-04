using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Ping.Models;
using SignalRChat.Hubs;
using System.Security.Claims;

namespace Ping.Controllers
{
    [Authorize]
    public class MessagesController : Controller
    {
        private readonly PingdbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;

        public MessagesController(PingdbContext context, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpGet]
        public async Task<IActionResult> Chat(int id)
        {
            if (!Request.IsAjaxRequest())
            {
                return RedirectToAction("Index", "Home", new { route = $"/Messages/Chat/{id}" });
            }

            var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(currentUserIdStr, out int currentUserId);

            var chat = await _context.Chats
                .Include(c => c.FirstUserNavigation)
                .Include(c => c.SecondUserNavigation)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (chat == null) return NotFound();

            var companion = chat.FirstUser == currentUserId ? chat.SecondUserNavigation : chat.FirstUserNavigation;

            var unreadMessages = await _context.Messages
                .Where(m => m.ChatId == id && m.SenderId != currentUserId && !m.IsRead)
                .ToListAsync();

            if (unreadMessages.Any())
            {
                foreach (var msg in unreadMessages) msg.IsRead = true;
                await _context.SaveChangesAsync();

                await _hubContext.Clients.Group(id.ToString()).SendAsync("ChatRead", id, currentUserId);
            }

            var messages = await _context.Messages
                .Where(m => m.ChatId == id)
                .Include(m => m.MessageReactions)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            ViewBag.CurrentChatId = id;
            ViewBag.CompanionId = companion.Id;
            ViewBag.CompanionName = companion.Name;
            ViewBag.CompanionFirstLetter = companion.Name?.Substring(0, 1).ToUpper() ?? "?";
            ViewBag.CompanionAvatarPath = companion.AvatarPath;
            ViewBag.IsOnline = SignalRChat.Hubs.ChatHub.OnlineUsers.ContainsKey(companion.Id);

            return PartialView("_ChatMessages", messages);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> CompanionProfile(int userId, int chatId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return NotFound();

            ViewBag.ChatId = chatId;
            return PartialView("_CompanionProfile", user);
        }

        [HttpGet]
        public async Task<IActionResult> SearchUsers(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Json(new List<object>());

            var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(currentUserIdStr, out int currentUserId);

            var users = await _context.Users
                .Where(u => u.Id != currentUserId && (u.Username.Contains(query) || u.Name.Contains(query)))
                .Select(u => new { id = u.Id, name = u.Name, username = u.Username, avatarPath = u.AvatarPath })
                .Take(10)
                .ToListAsync();

            return Json(users);
        }

        [HttpPost]
        public async Task<IActionResult> StartChat(int targetUserId)
        {
            var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(currentUserIdStr, out int currentUserId);

            if (currentUserId == 0 || targetUserId == 0) return BadRequest();

            var existingChat = await _context.Chats
                .FirstOrDefaultAsync(c =>
                    (c.FirstUser == currentUserId && c.SecondUser == targetUserId) ||
                    (c.FirstUser == targetUserId && c.SecondUser == currentUserId));

            if (existingChat != null) return Json(new { chatId = existingChat.Id });

            var newChat = new Chat { FirstUser = currentUserId, SecondUser = targetUserId };
            _context.Chats.Add(newChat);
            await _context.SaveChangesAsync();

            return Json(new { chatId = newChat.Id });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteChat(int chatId)
        {
            var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(currentUserIdStr, out int currentUserId);

            var chat = await _context.Chats.FirstOrDefaultAsync(c =>
                c.Id == chatId && (c.FirstUser == currentUserId || c.SecondUser == currentUserId));
            if (chat == null) return NotFound();

            var messages = _context.Messages.Where(m => m.ChatId == chatId);
            _context.Messages.RemoveRange(messages);

            _context.Chats.Remove(chat);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> GetChatInfo(int chatId)
        {
            var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(currentUserIdStr, out int currentUserId);

            var chat = await _context.Chats
                .Include(c => c.FirstUserNavigation)
                .Include(c => c.SecondUserNavigation)
                .FirstOrDefaultAsync(c => c.Id == chatId);
            if (chat == null) return NotFound();

            var companion = chat.FirstUser == currentUserId ? chat.SecondUserNavigation : chat.FirstUserNavigation;

            return Json(new
            {
                id = chat.Id,
                name = companion.Name,
                firstLetter = companion.Name?.Substring(0, 1).ToUpper() ?? "?",
                avatarPath = companion.AvatarPath
            });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteMessage(int messageId)
        {
            var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(currentUserIdStr, out int currentUserId);

            var msg = await _context.Messages.FindAsync(messageId);
            if (msg == null) return NotFound();
            if (msg.SenderId != currentUserId) return Forbid();

            if ((DateTime.UtcNow - msg.CreatedAt).TotalMinutes > 1)
            {
                return BadRequest(new { error = "Час на видалення війшов (більше 1 хвилини)." });
            }

            if (!string.IsNullOrEmpty(msg.Content))
            {
                string? fileUrl = null;
                if (msg.Content.StartsWith("[voice]"))
                    fileUrl = msg.Content.Substring(7);
                else if (msg.Content.StartsWith("[image]"))
                    fileUrl = msg.Content.Substring(7);

                if (fileUrl != null)
                {
                    var rel = fileUrl.TrimStart('/');
                    var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot",
                                            rel.Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(path))
                    {
                        try { System.IO.File.Delete(path); }
                        catch (Exception ex) { Console.WriteLine($"Не видалив файл: {ex.Message}"); }
                    }
                }
            }

            _context.Messages.Remove(msg);
            await _context.SaveChangesAsync();
            await _hubContext.Clients.Group(msg.ChatId.ToString()).SendAsync("MessageDeleted", messageId);

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> EditMessage(int messageId, string newContent, [FromServices] IHubContext<ChatHub> hubContext)
        {
            var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(currentUserIdStr, out int currentUserId);

            if (string.IsNullOrWhiteSpace(newContent))
                return BadRequest(new { error = "Текст пустий" });

            var msg = await _context.Messages.FindAsync(messageId);
            if (msg == null) return NotFound();
            if (msg.SenderId != currentUserId) return Forbid();

            if ((DateTime.UtcNow - msg.CreatedAt).TotalMinutes > 1)
            {
                return BadRequest(new { error = "Час на видалення війшов (більше 1 хвилини)." });
            }

            msg.Content = newContent;
            msg.IsEdited = true; 

            await _context.SaveChangesAsync();

            await hubContext.Clients.Group(msg.ChatId.ToString())
                .SendAsync("MessageEdited", messageId, newContent);

            return Ok(new { content = newContent });
        }

        [HttpPost]
        public async Task<IActionResult> UploadImage(IFormFile imageFile)
        {
            var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(currentUserIdStr, out int currentUserId);
            if (currentUserId == 0) return Unauthorized();

            if (imageFile == null || imageFile.Length == 0)
                return BadRequest("Файл пустий");

            var allowed = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
            if (!allowed.Contains(imageFile.ContentType.ToLowerInvariant()))
                return BadRequest("Недопустимий тип файлу");

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "images");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid()}.jpg";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await imageFile.CopyToAsync(stream);
            }

            return Ok(new { url = $"/uploads/images/{fileName}" });
        }

        [HttpPost]
        public async Task<IActionResult> UploadVoice(IFormFile audioFile)
        {
            var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(currentUserIdStr, out int currentUserId);
            if (currentUserId == 0) return Unauthorized();

            if (audioFile == null || audioFile.Length == 0) return BadRequest("Аудіофайл пустий");

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "voice");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var fileName = $"{Guid.NewGuid()}.webm";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await audioFile.CopyToAsync(stream);
            }

            return Ok(new { url = $"/uploads/voice/{fileName}" });
        }
    }
}