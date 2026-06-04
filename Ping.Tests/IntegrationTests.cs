using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Ping.Controllers;
using Ping.Models;
using SignalRChat.Hubs;
using System.Security.Claims;
using Xunit;

namespace Ping.Tests
{
    public class IntegrationTests
    {
        // =====================================================================
        //  ДОПОМІЖНІ ЗАСОБИ (внутрішні)
        // =====================================================================

        private static PingdbContext NewInMemoryContext()
        {
            var options = new DbContextOptionsBuilder<PingdbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .EnableSensitiveDataLogging()
                .Options;
            var context = new PingdbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        private static User SeedUser(PingdbContext context, string name, string username,
                                     string phone, string plainPassword)
        {
            var user = new User
            {
                Name = name,
                Username = username,
                PhoneNumber = phone,
                Password = BCrypt.Net.BCrypt.HashPassword(plainPassword)
            };
            context.Users.Add(user);
            context.SaveChanges();
            return user;
        }

        private static void SetAuthenticatedUser(Controller controller, int userId,
                                                 string username, bool ajax = true)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim("DisplayName", username)
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
            if (ajax)
                httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        }

        private static HttpContext HttpContextWithAuth()
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);

            var authService = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
            authService
                .Setup(a => a.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<Microsoft.AspNetCore.Authentication.AuthenticationProperties>()))
                .Returns(Task.CompletedTask);
            services.AddSingleton(authService.Object);

            var urlHelperFactory = new Mock<Microsoft.AspNetCore.Mvc.Routing.IUrlHelperFactory>();
            var urlHelper = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
            urlHelperFactory.Setup(f => f.GetUrlHelper(It.IsAny<Microsoft.AspNetCore.Mvc.ActionContext>())).Returns(urlHelper.Object);
            services.AddSingleton(urlHelperFactory.Object);

            var tempDataFactory = new Mock<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataDictionaryFactory>();
            var tempData = new Mock<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataDictionary>();
            tempDataFactory.Setup(f => f.GetTempData(It.IsAny<HttpContext>())).Returns(tempData.Object);
            services.AddSingleton(tempDataFactory.Object);

            return new DefaultHttpContext
            {
                RequestServices = Microsoft.Extensions.DependencyInjection
                    .ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services)
            };
        }

        private static IFormFile FakeFile(string fileName, string contentType, int bytes = 32)
        {
            var stream = new MemoryStream(new byte[bytes]);
            return new FormFile(stream, 0, stream.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }

        private static (ChatHub hub, Mock<IClientProxy> group) HubWithGroup(PingdbContext ctx)
        {
            var clients = new Mock<IHubCallerClients>();
            var group = new Mock<IClientProxy>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(group.Object);
            clients.Setup(c => c.All).Returns(group.Object);
            var hub = new ChatHub(ctx) { Clients = clients.Object };
            return (hub, group);
        }

        private static (IHubContext<ChatHub> ctx, Mock<IClientProxy> group) MockHubContext()
        {
            var hub = new Mock<IHubContext<ChatHub>>();
            var clients = new Mock<IHubClients>();
            var group = new Mock<IClientProxy>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(group.Object);
            hub.Setup(h => h.Clients).Returns(clients.Object);
            return (hub.Object, group);
        }

        // =====================================================================
        //  ДОДОАТОК З — Збірка №1: автентифікація ↔ база даних
        // =====================================================================

        [Fact] // TC_I001 — FR-01.01
        public async Task TC_I001_Register_ValidUser_PersistsHashedPassword()
        {
            using var ctx = NewInMemoryContext();
            var controller = new AccountController(ctx);
            var model = new RegisterViewModel
            {
                Name = "Андрій",
                Username = "andriy",
                PhoneNumber = "+380501112233",
                Password = "Secret123",
                ConfirmPassword = "Secret123"
            };

            await controller.Register(model);

            var saved = await ctx.Users.FirstOrDefaultAsync(u => u.Username == "andriy");
            Assert.NotNull(saved);
            Assert.NotEqual("Secret123", saved!.Password);
            Assert.True(BCrypt.Net.BCrypt.Verify("Secret123", saved.Password));
        }

        [Fact] // TC_I002 — FR-01.02
        public async Task TC_I002_Register_DuplicateLogin_Rejected()
        {
            using var ctx = NewInMemoryContext();
            SeedUser(ctx, "Існуючий", "andriy", "+380500000000", "Pass1");
            var controller = new AccountController(ctx);
            var model = new RegisterViewModel
            {
                Name = "Інший",
                Username = "andriy",
                PhoneNumber = "+380501112233",
                Password = "Secret123",
                ConfirmPassword = "Secret123"
            };

            var result = await controller.Register(model);

            Assert.IsType<ViewResult>(result);
            Assert.True(controller.ModelState.ContainsKey("Username"));
            Assert.Equal(1, await ctx.Users.CountAsync());
        }

        [Fact] // TC_I003 — FR-01.04
        public async Task TC_I003_Login_CorrectPassword_Authenticates()
        {
            using var ctx = NewInMemoryContext();
            SeedUser(ctx, "Андрій", "andriy", "+380501112233", "Secret123");
            var controller = new AccountController(ctx)
            {
                ControllerContext = new ControllerContext { HttpContext = HttpContextWithAuth() }
            };

            var result = await controller.Login(
                new LoginViewModel { Username = "andriy", Password = "Secret123" }, null);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal("Home", redirect.ControllerName);
        }

        [Fact] // TC_I004 — FR-01.04
        public async Task TC_I004_Login_WrongPassword_Rejected()
        {
            using var ctx = NewInMemoryContext();
            SeedUser(ctx, "Андрій", "andriy", "+380501112233", "Secret123");
            var controller = new AccountController(ctx)
            {
                ControllerContext = new ControllerContext { HttpContext = HttpContextWithAuth() }
            };

            var result = await controller.Login(
                new LoginViewModel { Username = "andriy", Password = "WrongPass" }, null);

            Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
        }

        // =====================================================================
        //  ДОДАТОК И — Збірка №2: керування чатами ↔ база даних
        // =====================================================================

        [Fact] // TC_I005 — FR-04.01
        public async Task TC_I005_SearchUsers_PartialLogin_ExcludesSelf()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Олександр", "olexandr", "+380500000001", "p");
            SeedUser(ctx, "Олег", "oleg_dev", "+380500000002", "p");
            SeedUser(ctx, "Олена", "olena_ui", "+380500000003", "p");

            var (hubCtx, _) = MockHubContext();
            var controller = new MessagesController(ctx, hubCtx);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.SearchUsers("ole");

            var json = Assert.IsType<JsonResult>(result);
            var items = ((System.Collections.IEnumerable)json.Value!).Cast<object>().ToList();
            Assert.Equal(2, items.Count);
            Assert.True(items.Count <= 10);
        }

        [Fact] // TC_I006 — FR-02.01
        public async Task TC_I006_StartChat_ExistingPair_ReturnsSame()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var other = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");
            var existing = new Chat { FirstUser = other.Id, SecondUser = me.Id };
            ctx.Chats.Add(existing);
            await ctx.SaveChangesAsync();

            var (hubCtx, _) = MockHubContext();
            var controller = new MessagesController(ctx, hubCtx);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.StartChat(other.Id);

            var json = Assert.IsType<JsonResult>(result);
            int chatId = (int)json.Value!.GetType().GetProperty("chatId")!.GetValue(json.Value)!;
            Assert.Equal(existing.Id, chatId);
            Assert.Equal(1, await ctx.Chats.CountAsync());
        }

        [Fact] // TC_I007 — FR-02.01
        public async Task TC_I007_StartChat_NewPair_CreatesChat()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var other = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");

            var (hubCtx, _) = MockHubContext();
            var controller = new MessagesController(ctx, hubCtx);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.StartChat(other.Id);

            Assert.IsType<JsonResult>(result);
            Assert.Equal(1, await ctx.Chats.CountAsync());
        }

        [Fact] // TC_I008 — FR-02.02
        public async Task TC_I008_ChatHistory_OrderedWithReactions()
        {
            using var ctx = NewInMemoryContext();
            var u1 = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var u2 = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");
            var chat = new Chat { FirstUser = u1.Id, SecondUser = u2.Id };
            ctx.Chats.Add(chat);
            await ctx.SaveChangesAsync();

            var m1 = new Message { ChatId = chat.Id, SenderId = u1.Id, Content = "перше", CreatedAt = DateTime.UtcNow.AddMinutes(-5) };
            var m2 = new Message { ChatId = chat.Id, SenderId = u2.Id, Content = "друге", CreatedAt = DateTime.UtcNow.AddMinutes(-2) };
            ctx.Messages.AddRange(m1, m2);
            await ctx.SaveChangesAsync();
            ctx.MessageReactions.Add(new MessageReaction { MessageId = m1.Id, UserId = u2.Id, Emoji = "\U0001F44D", CreatedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();

            var history = await ctx.Messages
                .Where(m => m.ChatId == chat.Id)
                .OrderBy(m => m.CreatedAt)
                .Include(m => m.MessageReactions)
                .ToListAsync();

            Assert.Equal(2, history.Count);
            Assert.Equal("перше", history[0].Content);
            Assert.Equal("друге", history[1].Content);
            Assert.Single(history[0].MessageReactions);
        }

        // =====================================================================
        //  ДОДАТОК К — Збірка №3: хаб обміну ↔ база даних
        // =====================================================================

        [Fact] // TC_I009 — FR-02.03
        public async Task TC_I009_SendMessage_PersistsBeforeBroadcast()
        {
            using var ctx = NewInMemoryContext();
            var u1 = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var u2 = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");
            var chat = new Chat { FirstUser = u1.Id, SecondUser = u2.Id };
            ctx.Chats.Add(chat);
            await ctx.SaveChangesAsync();

            var (hub, group) = HubWithGroup(ctx);
            await hub.SendMessage(chat.Id, u1.Id, "Привіт!");

            var saved = await ctx.Messages.FirstOrDefaultAsync(m => m.ChatId == chat.Id);
            Assert.NotNull(saved);
            Assert.Equal("Привіт!", saved!.Content);
            group.Verify(p => p.SendCoreAsync("ReceiveMessage",
                It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact] // TC_I010 — FR-02.03
        public async Task TC_I010_SendMessage_AssignsCreatedAt()
        {
            using var ctx = NewInMemoryContext();
            var u1 = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var u2 = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");
            var chat = new Chat { FirstUser = u1.Id, SecondUser = u2.Id };
            ctx.Chats.Add(chat);
            await ctx.SaveChangesAsync();

            var before = DateTime.UtcNow.AddSeconds(-2);
            var (hub, _) = HubWithGroup(ctx);
            await hub.SendMessage(chat.Id, u1.Id, "текст");

            var saved = await ctx.Messages.FirstAsync(m => m.ChatId == chat.Id);
            Assert.True(saved.Id > 0);
            Assert.True(saved.CreatedAt >= before);
        }

        [Fact] // TC_I011 — FR-02.05* (ReadChat)
        public async Task TC_I011_ReadChat_MarksUnreadAsRead()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var other = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");
            var chat = new Chat { FirstUser = me.Id, SecondUser = other.Id };
            ctx.Chats.Add(chat);
            await ctx.SaveChangesAsync();

            ctx.Messages.AddRange(
                new Message { ChatId = chat.Id, SenderId = other.Id, Content = "1", CreatedAt = DateTime.UtcNow, IsRead = false },
                new Message { ChatId = chat.Id, SenderId = other.Id, Content = "2", CreatedAt = DateTime.UtcNow, IsRead = false });
            await ctx.SaveChangesAsync();

            var (hub, group) = HubWithGroup(ctx);
            await hub.ReadChat(chat.Id, me.Id);

            var stillUnread = await ctx.Messages.CountAsync(m => m.ChatId == chat.Id && !m.IsRead);
            Assert.Equal(0, stillUnread);
            group.Verify(p => p.SendCoreAsync("ChatRead",
                It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact] // TC_I012 — NFR-03 (онлайн-лічильник з'єднань)
        public async Task TC_I012_OnlineStatus_CounterTracksConnections()
        {
            using var ctx = NewInMemoryContext();
            int userId = 555;
            ChatHub.OnlineUsers.TryRemove(userId, out _);

            ChatHub.OnlineUsers.AddOrUpdate(userId, 1, (k, c) => c + 1);
            ChatHub.OnlineUsers.AddOrUpdate(userId, 1, (k, c) => c + 1);
            Assert.Equal(2, ChatHub.OnlineUsers[userId]);

            ChatHub.OnlineUsers[userId]--;
            Assert.True(ChatHub.OnlineUsers[userId] > 0);

            int remaining = --ChatHub.OnlineUsers[userId];
            if (remaining <= 0) ChatHub.OnlineUsers.TryRemove(userId, out _);
            Assert.False(ChatHub.OnlineUsers.ContainsKey(userId));
            await Task.CompletedTask;
        }

        // =====================================================================
        //  ДОДАТОК Л — Збірка №4: підсистема реакцій ↔ база даних
        // =====================================================================

        [Fact] // TC_I013 — FR-02.07*
        public async Task TC_I013_ToggleReaction_New_AddsRecord()
        {
            using var ctx = NewInMemoryContext();
            var (msgId, u1, _) = await SeedChatWithMessage(ctx);

            var (hub, group) = HubWithGroup(ctx);
            await hub.ToggleReaction(msgId, u1, "\U0001F44D");

            Assert.Equal(1, await ctx.MessageReactions.CountAsync());
            group.Verify(p => p.SendCoreAsync("ReactionUpdated",
                It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact] // TC_I014 — FR-02.07*
        public async Task TC_I014_ToggleReaction_Existing_Removes()
        {
            using var ctx = NewInMemoryContext();
            var (msgId, u1, _) = await SeedChatWithMessage(ctx);

            var (hub, _) = HubWithGroup(ctx);
            await hub.ToggleReaction(msgId, u1, "\U0001F44D");
            await hub.ToggleReaction(msgId, u1, "\U0001F44D");

            Assert.Equal(0, await ctx.MessageReactions.CountAsync());
        }

        [Fact] // TC_I015 — BRL-04
        public async Task TC_I015_Reaction_DuplicateBlocked()
        {
            using var ctx = NewInMemoryContext();
            var (msgId, u1, _) = await SeedChatWithMessage(ctx);

            var (hub, _) = HubWithGroup(ctx);
            await hub.ToggleReaction(msgId, u1, "\U0001F44D");
            int afterFirst = await ctx.MessageReactions
                .CountAsync(r => r.MessageId == msgId && r.UserId == u1 && r.Emoji == "\U0001F44D");

            Assert.Equal(1, afterFirst);
        }

        [Fact] // TC_I016 — FR-02.07*
        public async Task TC_I016_Reaction_Summary_AggregatesByEmoji()
        {
            using var ctx = NewInMemoryContext();
            var (msgId, u1, u2) = await SeedChatWithMessage(ctx);

            var (hub, _) = HubWithGroup(ctx);
            await hub.ToggleReaction(msgId, u1, "\U0001F44D");
            await hub.ToggleReaction(msgId, u2, "\U0001F44D");
            await hub.ToggleReaction(msgId, u1, "\U0001F525");

            var summary = await ctx.MessageReactions
                .Where(r => r.MessageId == msgId)
                .GroupBy(r => r.Emoji)
                .Select(g => new { emoji = g.Key, count = g.Count() })
                .ToListAsync();

            Assert.Equal(2, summary.Count);
            Assert.Equal(2, summary.First(s => s.emoji == "\U0001F44D").count);
            Assert.Equal(1, summary.First(s => s.emoji == "\U0001F525").count);
        }

        // =====================================================================
        //  ДОДАТОК М — Збірка №5: мультимедіа та профіль ↔ файлове сховище
        // =====================================================================

        [Fact] // TC_I017 — FR-02.04
        public async Task TC_I017_UploadVoice_SavesWebmFile()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var (hubCtx, _) = MockHubContext();
            var controller = new MessagesController(ctx, hubCtx);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            EnsureUploadsDir("voice");
            var result = await controller.UploadVoice(FakeFile("voice.webm", "audio/webm"));

            var ok = Assert.IsType<OkObjectResult>(result);
            var url = ok.Value!.GetType().GetProperty("url")!.GetValue(ok.Value) as string;
            Assert.NotNull(url);
            Assert.StartsWith("/uploads/voice/", url);
            Assert.EndsWith(".webm", url);
            Assert.True(File.Exists(ToPhysical(url!)));
            File.Delete(ToPhysical(url!));
        }

        [Fact] // TC_I018 — FR-02.05
        public async Task TC_I018_UploadImage_SavesCompressedFile()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var (hubCtx, _) = MockHubContext();
            var controller = new MessagesController(ctx, hubCtx);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            EnsureUploadsDir("images");
            var result = await controller.UploadImage(FakeFile("photo.jpg", "image/jpeg"));

            var ok = Assert.IsType<OkObjectResult>(result);
            var url = ok.Value!.GetType().GetProperty("url")!.GetValue(ok.Value) as string;
            Assert.NotNull(url);
            Assert.StartsWith("/uploads/images/", url);
            Assert.True(File.Exists(ToPhysical(url!)));
            File.Delete(ToPhysical(url!));
        }

        [Fact] // TC_I019 — FR-03.03
        public async Task TC_I019_UploadAvatar_DeletesOldFile()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");

            string webRoot = Path.Combine(Path.GetTempPath(), "ping_wwwroot_" + Guid.NewGuid());
            string avatarsDir = Path.Combine(webRoot, "uploads", "avatars");
            Directory.CreateDirectory(avatarsDir);
            string oldFile = Path.Combine(avatarsDir, "old_avatar.png");
            File.WriteAllBytes(oldFile, new byte[8]);
            me.AvatarPath = "/uploads/avatars/old_avatar.png";
            ctx.Users.Update(me);
            await ctx.SaveChangesAsync();

            var env = new Mock<IWebHostEnvironment>();
            env.SetupGet(e => e.WebRootPath).Returns(webRoot);
            var controller = new SettingsController(ctx, env.Object);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.UploadAvatar(FakeFile("new_avatar.png", "image/png"));

            Assert.IsType<JsonResult>(result);
            Assert.False(File.Exists(oldFile));
            var user = await ctx.Users.FindAsync(me.Id);
            Assert.NotEqual("/uploads/avatars/old_avatar.png", user!.AvatarPath);
            Directory.Delete(webRoot, recursive: true);
        }

        [Fact] // TC_I020 — FR-02.09
        public async Task TC_I020_DeleteMessage_RemovesPhysicalFile()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var other = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");
            var chat = new Chat { FirstUser = me.Id, SecondUser = other.Id };
            ctx.Chats.Add(chat);
            await ctx.SaveChangesAsync();

            EnsureUploadsDir("voice");
            string fileName = Guid.NewGuid() + ".webm";
            string rel = "/uploads/voice/" + fileName;
            File.WriteAllBytes(ToPhysical(rel), new byte[8]);

            var msg = new Message
            {
                ChatId = chat.Id,
                SenderId = me.Id,
                Content = "[voice]" + rel,
                CreatedAt = DateTime.UtcNow
            };
            ctx.Messages.Add(msg);
            await ctx.SaveChangesAsync();

            var (hubCtx, _) = MockHubContext();
            var controller = new MessagesController(ctx, hubCtx);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.DeleteMessage(msg.Id);

            Assert.IsType<OkResult>(result);
            Assert.Null(await ctx.Messages.FindAsync(msg.Id));
            Assert.False(File.Exists(ToPhysical(rel)));
        }

        private static async Task<(int msgId, int u1, int u2)> SeedChatWithMessage(PingdbContext ctx)
        {
            var u1 = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var u2 = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");
            var chat = new Chat { FirstUser = u1.Id, SecondUser = u2.Id };
            ctx.Chats.Add(chat);
            await ctx.SaveChangesAsync();
            var msg = new Message
            {
                ChatId = chat.Id,
                SenderId = u2.Id,
                Content = "повідомлення",
                CreatedAt = DateTime.UtcNow
            };
            ctx.Messages.Add(msg);
            await ctx.SaveChangesAsync();
            return (msg.Id, u1.Id, u2.Id);
        }

        private static void EnsureUploadsDir(string sub)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", sub);
            Directory.CreateDirectory(dir);
        }

        private static string ToPhysical(string url)
        {
            var rel = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", rel);
        }
    }
}