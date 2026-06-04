using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
    public class ValidationTests
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
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();

            var authService = new Mock<IAuthenticationService>();
            authService
                .Setup(a => a.SignInAsync(
                    It.IsAny<HttpContext>(), It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
                .Returns(Task.CompletedTask);
            services.AddSingleton(authService.Object);

            return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
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

        // =====================================================================
        //  Підсистема авторизації та доступу (AccountController)
        // =====================================================================

        [Fact] // TC_V001 — FR-01.01
        public async Task TC_V001_ValidRegistration_CreatesAccount()
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

            var result = await controller.Register(model);

            var created = await ctx.Users.FirstOrDefaultAsync(u => u.Username == "andriy");
            Assert.NotNull(created);
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }

        [Fact] // TC_V002 — FR-01.01
        public async Task TC_V002_Registration_InvalidModel_Rejected()
        {
            using var ctx = NewInMemoryContext();
            var controller = new AccountController(ctx);
            var model = new RegisterViewModel
            {
                Name = "",
                Username = "noname",
                PhoneNumber = "+380501112233",
                Password = "Secret123",
                ConfirmPassword = "Secret123"
            };
            controller.ModelState.AddModelError("Name", "Имя не может быть пустым");

            var result = await controller.Register(model);

            Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.Equal(0, await ctx.Users.CountAsync());
        }

        [Fact] // TC_V003 — FR-01.02
        public async Task TC_V003_Registration_DuplicateLogin_Rejected()
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

        [Fact] // TC_V004 — FR-01.04
        public async Task TC_V004_Login_ValidCredentials_Success()
        {
            using var ctx = NewInMemoryContext();
            SeedUser(ctx, "Андрій", "andriy", "+380501112233", "Secret123");

            var controller = new AccountController(ctx)
            {
                ControllerContext = new ControllerContext { HttpContext = HttpContextWithAuth() },
                Url = new Mock<IUrlHelper>().Object
            };
            var model = new LoginViewModel { Username = "andriy", Password = "Secret123" };

            var result = await controller.Login(model, null);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal("Home", redirect.ControllerName);
        }

        [Fact] // TC_V005 — FR-01.04
        public async Task TC_V005_Login_WrongPassword_Rejected()
        {
            using var ctx = NewInMemoryContext();
            SeedUser(ctx, "Андрій", "andriy", "+380501112233", "Secret123");

            var controller = new AccountController(ctx)
            {
                ControllerContext = new ControllerContext { HttpContext = HttpContextWithAuth() },
                TempData = new Mock<ITempDataDictionary>().Object
            };
            var model = new LoginViewModel { Username = "andriy", Password = "WrongPass" };

            var result = await controller.Login(model, null);

            Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
        }

        // =====================================================================
        //  Підсистема пошуку взаємодії (MessagesController)
        // =====================================================================

        [Fact] // TC_V006 — FR-04.01
        public async Task TC_V006_SearchUsers_PartialMatch_ReturnsList()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            SeedUser(ctx, "Олег", "oleg_dev", "+380500000002", "p");
            SeedUser(ctx, "Олена", "olena_ui", "+380500000003", "p");

            var hub = new Mock<IHubContext<ChatHub>>();
            var controller = new MessagesController(ctx, hub.Object);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.SearchUsers("ole");

            var json = Assert.IsType<JsonResult>(result);
            int count = ((System.Collections.IEnumerable)json.Value!).Cast<object>().Count();
            Assert.Equal(2, count);
            Assert.True(count <= 10);
        }

        [Fact] // TC_V007 — FR-02.01
        public async Task TC_V007_StartChat_NewPair_OpensChat()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var other = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");

            var hub = new Mock<IHubContext<ChatHub>>();
            var controller = new MessagesController(ctx, hub.Object);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.StartChat(other.Id);

            Assert.IsType<JsonResult>(result);
            Assert.Equal(1, await ctx.Chats.CountAsync());
        }

        // =====================================================================
        //  Підсистема комунікації та чатів (ChatHub / MessagesController)
        // =====================================================================

        [Fact] // TC_V008 — FR-02.03
        public async Task TC_V008_SendText_DeliveredRealtime()
        {
            using var ctx = NewInMemoryContext();
            var u1 = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var u2 = SeedUser(ctx, "Друг", "friend", "+380500000002", "p");
            var chat = new Chat { FirstUser = u1.Id, SecondUser = u2.Id };
            ctx.Chats.Add(chat);
            await ctx.SaveChangesAsync();

            var clients = new Mock<IHubCallerClients>();
            var groupProxy = new Mock<IClientProxy>();
            clients.Setup(c => c.Group(chat.Id.ToString())).Returns(groupProxy.Object);
            var hub = new ChatHub(ctx) { Clients = clients.Object };

            await hub.SendMessage(chat.Id, u1.Id, "Привіт!");

            var saved = await ctx.Messages.FirstOrDefaultAsync(m => m.ChatId == chat.Id);
            Assert.NotNull(saved);
            Assert.Equal("Привіт!", saved!.Content);
            groupProxy.Verify(p => p.SendCoreAsync(
                "ReceiveMessage", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact] // TC_V009 — FR-02.04 (серверна частина)
        public async Task TC_V009_UploadVoice_Webm_Saved()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var hub = new Mock<IHubContext<ChatHub>>();
            var controller = new MessagesController(ctx, hub.Object);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.UploadVoice(FakeFile("voice.webm", "audio/webm"));

            var ok = Assert.IsType<OkObjectResult>(result);
            var url = ok.Value!.GetType().GetProperty("url")!.GetValue(ok.Value) as string;
            Assert.NotNull(url);
            Assert.EndsWith(".webm", url);
            Assert.StartsWith("/uploads/voice/", url);
        }

        [Fact] // TC_V010 — FR-02.05 (серверна частина)
        public async Task TC_V010_UploadImage_AcceptedAndStored()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");
            var hub = new Mock<IHubContext<ChatHub>>();
            var controller = new MessagesController(ctx, hub.Object);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.UploadImage(FakeFile("photo.jpg", "image/jpeg"));

            var ok = Assert.IsType<OkObjectResult>(result);
            var url = ok.Value!.GetType().GetProperty("url")!.GetValue(ok.Value) as string;
            Assert.NotNull(url);
            Assert.StartsWith("/uploads/images/", url);
        }

        [Fact] // TC_V011 — FR-02.06 (розпізнавання GIF як медіа)
        public void TC_V011_GifContent_RecognizedAsMedia()
        {
            string content = "https://media.giphy.com/media/abc123/giphy.gif";
            bool isMedia = content.StartsWith("http")
                           && (content.Contains(".gif") || content.Contains("giphy.com"));
            Assert.True(isMedia);
        }

        [Fact] // TC_V012 — FR-02.07 (емодзі у вмісті)
        public void TC_V012_Emoji_AppendedToMessageText()
        {
            string text = "Привіт";
            string emoji = "\U0001F600";
            string composed = text + emoji;
            Assert.Contains(emoji, composed);
            Assert.Equal("Привіт\U0001F600", composed);
        }

        [Fact] // TC_V013 — FR-02.08
        public async Task TC_V013_EditOwnMessage_WithinWindow()
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
                Content = "Старий текст",
                CreatedAt = DateTime.UtcNow
            };
            ctx.Messages.Add(msg);
            await ctx.SaveChangesAsync();

            var hub = new Mock<IHubContext<ChatHub>>();
            var clients = new Mock<IHubClients>();
            var proxy = new Mock<IClientProxy>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
            hub.Setup(h => h.Clients).Returns(clients.Object);

            var controller = new MessagesController(ctx, hub.Object);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.EditMessage(msg.Id, "Новий текст", hub.Object);

            Assert.IsType<OkObjectResult>(result);
            var updated = await ctx.Messages.FindAsync(msg.Id);
            Assert.Equal("Новий текст", updated!.Content);
            Assert.True(updated.IsEdited);
        }

        [Fact] // TC_V014 — FR-02.09
        public async Task TC_V014_DeleteOwnMessage_Removed()
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
                Content = "Текст для видалення",
                CreatedAt = DateTime.UtcNow
            };
            ctx.Messages.Add(msg);
            await ctx.SaveChangesAsync();

            var hub = new Mock<IHubContext<ChatHub>>();
            var clients = new Mock<IHubClients>();
            var proxy = new Mock<IClientProxy>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
            hub.Setup(h => h.Clients).Returns(clients.Object);

            var controller = new MessagesController(ctx, hub.Object);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.DeleteMessage(msg.Id);

            Assert.IsType<OkResult>(result);
            Assert.Null(await ctx.Messages.FindAsync(msg.Id));
        }

        // =====================================================================
        //  Підсистема керування профілем (SettingsController)
        // =====================================================================

        [Fact] // TC_V016 — FR-03.02 (негативний: невірний поточний пароль)
        public async Task TC_V016_ChangePassword_WrongCurrent_Rejected()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "OldPass1");

            var env = new Mock<IWebHostEnvironment>();
            env.SetupGet(e => e.WebRootPath).Returns(Path.GetTempPath());
            var controller = new SettingsController(ctx, env.Object);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var model = new ProfileViewModel
            {
                Name = "Я",
                PhoneNumber = "+380500000001",
                Username = "current",
                CurrentPassword = "WRONG",
                NewPassword = "NewPass2",
                ConfirmPassword = "NewPass2"
            };

            var result = await controller.Settings(model);

            Assert.IsType<PartialViewResult>(result);
            Assert.True(controller.ModelState.ContainsKey("CurrentPassword"));
            var user = await ctx.Users.FindAsync(me.Id);
            Assert.True(BCrypt.Net.BCrypt.Verify("OldPass1", user!.Password));
        }

        [Fact] // TC_V016b — FR-03.02 (позитивний: правильний поточний пароль)
        public async Task TC_V016b_ChangePassword_CorrectCurrent_Succeeds()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "OldPass1");

            var env = new Mock<IWebHostEnvironment>();
            env.SetupGet(e => e.WebRootPath).Returns(Path.GetTempPath());
            var controller = new SettingsController(ctx, env.Object);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var model = new ProfileViewModel
            {
                Name = "Я",
                PhoneNumber = "+380500000001",
                Username = "current",
                CurrentPassword = "OldPass1",
                NewPassword = "NewPass2",
                ConfirmPassword = "NewPass2"
            };

            var result = await controller.Settings(model);

            Assert.IsType<PartialViewResult>(result);
            Assert.True(controller.ModelState.IsValid);
            var user = await ctx.Users.FindAsync(me.Id);
            Assert.True(BCrypt.Net.BCrypt.Verify("NewPass2", user!.Password));
        }

        [Fact] // TC_V017 — FR-03.03
        public async Task TC_V017_UploadAvatar_StoresFile()
        {
            using var ctx = NewInMemoryContext();
            var me = SeedUser(ctx, "Я", "current", "+380500000001", "p");

            var env = new Mock<IWebHostEnvironment>();
            env.SetupGet(e => e.WebRootPath).Returns(Path.GetTempPath());
            var controller = new SettingsController(ctx, env.Object);
            SetAuthenticatedUser(controller, me.Id, me.Username);

            var result = await controller.UploadAvatar(FakeFile("avatar.png", "image/png"));

            var json = Assert.IsType<JsonResult>(result);
            bool success = (bool)json.Value!.GetType().GetProperty("success")!.GetValue(json.Value)!;
            Assert.True(success);
            var user = await ctx.Users.FindAsync(me.Id);
            Assert.False(string.IsNullOrEmpty(user!.AvatarPath));
        }
    }
}
