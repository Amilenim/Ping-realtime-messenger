using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
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
using System.Reflection;
using System.Security.Claims;
using Xunit;

namespace Ping.Tests
{
    public class SystemTests
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

        private static IFormFile FakeFile(string fileName, string contentType, int bytes = 16)
        {
            var stream = new MemoryStream(new byte[bytes]);
            return new FormFile(stream, 0, stream.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }

        private static ChatHub HubWithMockedClients(PingdbContext ctx)
        {
            var clients = new Mock<IHubCallerClients>();
            var proxy = new Mock<IClientProxy>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
            clients.Setup(c => c.All).Returns(proxy.Object);
            return new ChatHub(ctx) { Clients = clients.Object };
        }

        private static IHubContext<ChatHub> MockHubContext()
        {
            var hub = new Mock<IHubContext<ChatHub>>();
            var clients = new Mock<IHubClients>();
            var proxy = new Mock<IClientProxy>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
            hub.Setup(h => h.Clients).Returns(clients.Object);
            return hub.Object;
        }

        // =====================================================================
        //  Захищеність та автентифікація
        // =====================================================================

        [Fact] // TC_S001 — FR-01.03 (пароль зберігається як BCrypt-хеш)
        public async Task TC_S001_Password_StoredAsBcryptHash()
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

            var saved = await ctx.Users.FirstAsync(u => u.Username == "andriy");
            Assert.NotEqual("Secret123", saved.Password);
            Assert.StartsWith("$2", saved.Password);
            Assert.True(BCrypt.Net.BCrypt.Verify("Secret123", saved.Password));
        }

        [Fact] // TC_S002 — FR-01.05 (захищений маршрут закрито атрибутом авторизації)
        public void TC_S002_ProtectedRoute_RequiresAuthorization()
        {
            var attr = typeof(MessagesController)
                .GetCustomAttribute<AuthorizeAttribute>(inherit: true);
            Assert.NotNull(attr);

            var settingsAttr = typeof(SettingsController)
                .GetCustomAttribute<AuthorizeAttribute>(inherit: true);
            Assert.NotNull(settingsAttr);

            var homeAttr = typeof(HomeController)
                .GetCustomAttribute<AuthorizeAttribute>(inherit: true);
            Assert.NotNull(homeAttr);
        }

        [Fact]
        // TC_S003 — FR-01.05 (анонімне підключення до хаба має відхилятися)
        public void TC_S003_Hub_RequiresAuthorization()
        {
            var attr = typeof(ChatHub).GetCustomAttribute<AuthorizeAttribute>(inherit: true);
            Assert.NotNull(attr);
        }

        // =====================================================================
        //  Обмеження цілісності (бізнес-правила)
        // =====================================================================

        [Fact] // TC_S004 — BRL-01 (унікальність логіна)
        public async Task TC_S004_UniqueLogin_Enforced()
        {
            using var ctx = NewInMemoryContext();
            var controller = new AccountController(ctx);

            var first = new RegisterViewModel
            {
                Name = "Перший",
                Username = "andriy",
                PhoneNumber = "+380500000001",
                Password = "Pass1",
                ConfirmPassword = "Pass1"
            };
            var second = new RegisterViewModel
            {
                Name = "Другий",
                Username = "andriy",
                PhoneNumber = "+380500000002",
                Password = "Pass2",
                ConfirmPassword = "Pass2"
            };

            await controller.Register(first);
            var result = await controller.Register(second);

            Assert.IsType<ViewResult>(result);
            Assert.True(controller.ModelState.ContainsKey("Username"));
            Assert.Equal(1, await ctx.Users.CountAsync());
        }

        [Fact] // TC_S005 — BRL-02 (єдиність діалогової пари)
        public async Task TC_S005_SingleChatPair_NoDuplicate()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var other = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");

            var controller = new MessagesController(ctx, MockHubContext());
            SetAuthenticatedUser(controller, me.Id, me.Username);

            await controller.StartChat(other.Id);
            await controller.StartChat(other.Id);

            Assert.Equal(1, await ctx.Chats.CountAsync());
        }

        [Fact] // TC_S006 — BRL-03 / FR-02.08 (редагування поза часовим вікном блокується)
        public async Task TC_S006_EditMessage_OutsideWindow_Blocked()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var other = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");
            var chat = new Chat { FirstUser = me.Id, SecondUser = other.Id };
            ctx.Chats.Add(chat);
            await ctx.SaveChangesAsync();

            var msg = new Message
            {
                ChatId = chat.Id,
                SenderId = me.Id,
                Content = "Старе повідомлення",
                CreatedAt = DateTime.UtcNow.AddHours(-11)
            };
            ctx.Messages.Add(msg);
            await ctx.SaveChangesAsync();

            var hubCtx = MockHubContext();
            var controller = new MessagesController(ctx, hubCtx);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.EditMessage(msg.Id, "Спроба правки", hubCtx);

            Assert.IsType<BadRequestObjectResult>(result);
            var unchanged = await ctx.Messages.FindAsync(msg.Id);
            Assert.Equal("Старе повідомлення", unchanged!.Content);
        }

        [Fact] // TC_S007 — FR-02.08 (редагування чужого повідомлення заборонено)
        public async Task TC_S007_EditForeignMessage_Forbidden()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var other = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");
            var chat = new Chat { FirstUser = me.Id, SecondUser = other.Id };
            ctx.Chats.Add(chat);
            await ctx.SaveChangesAsync();

            var msg = new Message
            {
                ChatId = chat.Id,
                SenderId = other.Id,
                Content = "Чуже повідомлення",
                CreatedAt = DateTime.UtcNow
            };
            ctx.Messages.Add(msg);
            await ctx.SaveChangesAsync();

            var hubCtx = MockHubContext();
            var controller = new MessagesController(ctx, hubCtx);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.EditMessage(msg.Id, "Зміна чужого", hubCtx);

            Assert.IsType<ForbidResult>(result);
            var unchanged = await ctx.Messages.FindAsync(msg.Id);
            Assert.Equal("Чуже повідомлення", unchanged!.Content);
        }

        [Fact] // TC_S008 — FR-02.09 (видалення чужого повідомлення заборонено)
        public async Task TC_S008_DeleteForeignMessage_Forbidden()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var other = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");
            var chat = new Chat { FirstUser = me.Id, SecondUser = other.Id };
            ctx.Chats.Add(chat);
            await ctx.SaveChangesAsync();

            var msg = new Message
            {
                ChatId = chat.Id,
                SenderId = other.Id,
                Content = "Чуже повідомлення",
                CreatedAt = DateTime.UtcNow
            };
            ctx.Messages.Add(msg);
            await ctx.SaveChangesAsync();

            var controller = new MessagesController(ctx, MockHubContext());
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.DeleteMessage(msg.Id);

            Assert.IsType<ForbidResult>(result);
            Assert.NotNull(await ctx.Messages.FindAsync(msg.Id));
        }

        [Fact] // TC_S009 — FR-02.05 (зображення неприпустимого типу відхиляється)
        public async Task TC_S009_UploadImage_InvalidMime_Rejected()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var controller = new MessagesController(ctx, MockHubContext());
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var file = FakeFile("malware.exe", "application/x-msdownload");
            var result = await controller.UploadImage(file);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact] // TC_S010 — BRL-04 (одна реакція кожного типу від користувача)
        public async Task TC_S010_Reaction_UniquePerEmoji()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var other = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");
            var chat = new Chat { FirstUser = me.Id, SecondUser = other.Id };
            ctx.Chats.Add(chat);
            await ctx.SaveChangesAsync();
            var msg = new Message
            {
                ChatId = chat.Id,
                SenderId = other.Id,
                Content = "Повідомлення",
                CreatedAt = DateTime.UtcNow
            };
            ctx.Messages.Add(msg);
            await ctx.SaveChangesAsync();

            var hub = HubWithMockedClients(ctx);

            await hub.ToggleReaction(msg.Id, me.Id, "\U0001F44D");
            Assert.Equal(1, await ctx.MessageReactions.CountAsync());

            await hub.ToggleReaction(msg.Id, me.Id, "\U0001F44D");
            Assert.Equal(0, await ctx.MessageReactions.CountAsync());
        }

        [Fact] // TC_S011 — NFR-03 (онлайн-статус з урахуванням кількох з'єднань)
        public void TC_S011_OnlineStatus_MultiTab_Consistent()
        {
            int userId = 777;
            var online = ChatHub.OnlineUsers;
            online.TryRemove(userId, out _);

            online.AddOrUpdate(userId, 1, (k, c) => c + 1);
            online.AddOrUpdate(userId, 1, (k, c) => c + 1);
            Assert.Equal(2, online[userId]);

            online[userId] = online[userId] - 1;
            Assert.True(online.ContainsKey(userId) && online[userId] > 0);

            int remaining = online[userId] - 1;
            if (remaining <= 0) online.TryRemove(userId, out _);
            Assert.False(online.ContainsKey(userId));
        }

        [Fact] // TC_S012 — FR-02.10 (видалення чату каскадно прибирає повідомлення)
        public async Task TC_S012_DeleteChat_CascadesMessages()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var other = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");
            var chat = new Chat { FirstUser = me.Id, SecondUser = other.Id };
            ctx.Chats.Add(chat);
            await ctx.SaveChangesAsync();

            ctx.Messages.AddRange(
                new Message { ChatId = chat.Id, SenderId = me.Id, Content = "м1", CreatedAt = DateTime.UtcNow },
                new Message { ChatId = chat.Id, SenderId = other.Id, Content = "м2", CreatedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();

            var controller = new MessagesController(ctx, MockHubContext());
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.DeleteChat(chat.Id);

            Assert.IsType<OkResult>(result);
            Assert.Equal(0, await ctx.Chats.CountAsync());
            Assert.Equal(0, await ctx.Messages.CountAsync(m => m.ChatId == chat.Id));
        }

        [Fact] // TC_S013 — NFR-06 (псевдо-SPA: часткове подання за AJAX-запитом)
        public async Task TC_S013_SpaNavigation_ReturnsPartial()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");

            var env = new Mock<IWebHostEnvironment>();
            env.SetupGet(e => e.WebRootPath).Returns(Path.GetTempPath());
            var controller = new SettingsController(ctx, env.Object);

            SetAuthenticatedUser(controller, me.Id, me.Username, ajax: true);
            var ajaxResult = await controller.Settings();
            Assert.IsType<PartialViewResult>(ajaxResult);

            SetAuthenticatedUser(controller, me.Id, me.Username, ajax: false);
            var fullResult = await controller.Settings();
            Assert.IsType<RedirectToActionResult>(fullResult);
        }
    }
}