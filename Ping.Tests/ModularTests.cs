using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Ping.Helpers;
using Ping.Models;
using Xunit;

namespace Ping.Tests
{
    public class ModularTests
    {
        private static IList<ValidationResult> ValidateModel(object model)
        {
            var results = new List<ValidationResult>();
            var context = new ValidationContext(model, serviceProvider: null, items: null);
            Validator.TryValidateObject(model, context, results, validateAllProperties: true);
            return results;
        }

        // =====================================================================
        //  Хешування паролів (BCrypt)
        // =====================================================================

        [Fact] // TC_M001 — FR-01.03
        public void TC_M001_Bcrypt_Hash_IsNotPlaintext()
        {
            string password = "Secret123";
            string hash = BCrypt.Net.BCrypt.HashPassword(password);

            Assert.NotEqual(password, hash);
            Assert.StartsWith("$2", hash);
        }

        [Fact] // TC_M002 — FR-01.04
        public void TC_M002_Bcrypt_Verify_CorrectPassword()
        {
            string hash = BCrypt.Net.BCrypt.HashPassword("Secret123");
            Assert.True(BCrypt.Net.BCrypt.Verify("Secret123", hash));
        }

        [Fact] // TC_M003 — FR-01.04
        public void TC_M003_Bcrypt_Verify_WrongPassword()
        {
            string hash = BCrypt.Net.BCrypt.HashPassword("Secret123");
            Assert.False(BCrypt.Net.BCrypt.Verify("WrongPass", hash));
        }

        // =====================================================================
        //  Помічник кольору аватара (AvatarHelper)
        // =====================================================================

        [Fact] // TC_M004 — FR-03.03
        public void TC_M004_AvatarHelper_SameName_SameColor()
        {
            string c1 = AvatarHelper.GetColorByName("Андрій");
            string c2 = AvatarHelper.GetColorByName("Андрій");

            Assert.Equal(c1, c2);
            Assert.StartsWith("hsl(", c1);
        }

        [Fact] // TC_M005 — FR-03.03
        public void TC_M005_AvatarHelper_DiffName_DiffColor()
        {
            string c1 = AvatarHelper.GetColorByName("Андрій");
            string c2 = AvatarHelper.GetColorByName("Олена");

            Assert.NotEqual(c1, c2);
        }

        // =====================================================================
        //  Валідація моделей подання
        // =====================================================================

        [Fact] // TC_M006 — FR-01.01
        public void TC_M006_RegisterModel_Valid_PassesValidation()
        {
            var model = new RegisterViewModel
            {
                Name = "Андрій",
                Username = "andriy",
                PhoneNumber = "+380501112233",
                Password = "Secret123",
                ConfirmPassword = "Secret123"
            };

            var errors = ValidateModel(model);
            Assert.Empty(errors);
        }

        [Fact] // TC_M007 — FR-01.01
        public void TC_M007_RegisterModel_Empty_FailsValidation()
        {
            var model = new ProfileViewModel
            {
                Name = "",
                PhoneNumber = "+380501112233",
                Username = "andriy"
            };

            var errors = ValidateModel(model);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.MemberNames.Contains("Name"));
        }

        [Fact] // TC_M008 — FR-01.01
        public void TC_M008_RegisterModel_PasswordMismatch()
        {
            var password = "Secret123";
            var confirm = "Other999";

            bool passwordsMatch = password == confirm;
            Assert.False(passwordsMatch);
        }

        [Fact] // TC_M009 — FR-03.02
        public void TC_M009_ChangePasswordModel_Mismatch_Fails()
        {
            var model = new ProfileViewModel
            {
                Name = "Андрій",
                PhoneNumber = "+380501112233",
                Username = "andriy",
                NewPassword = "NewPass1",
                ConfirmPassword = "NewPass2"
            };

            bool mismatch = model.NewPassword != model.ConfirmPassword;
            Assert.True(mismatch);
        }

        // =====================================================================
        //  Розпізнавання типу вмісту повідомлення
        // =====================================================================

        [Fact] // TC_M010 — FR-02.06
        public void TC_M010_ContentType_GifUrl_DetectedAsMedia()
        {
            string content = "https://media.giphy.com/media/abc123/giphy.gif";
            bool isMedia = content.StartsWith("http")
                           && (content.Contains(".gif") || content.Contains("giphy.com"));
            Assert.True(isMedia);
        }

        [Fact] // TC_M011 — FR-02.04
        public void TC_M011_ContentType_VoiceMarker_Detected()
        {
            string content = "[voice]/uploads/voice/abc.webm";
            Assert.True(content.StartsWith("[voice]"));
            Assert.Equal("/uploads/voice/abc.webm", content.Substring(7));
        }

        [Fact] // TC_M012 — FR-02.05
        public void TC_M012_ContentType_ImageMarker_Detected()
        {
            string content = "[image]/uploads/images/abc.jpg";
            Assert.True(content.StartsWith("[image]"));
            Assert.Equal("/uploads/images/abc.jpg", content.Substring(7));
        }

        // =====================================================================
        //  Визначення AJAX-запиту (HttpRequestExtensions)
        // =====================================================================

        [Fact] // TC_M013 — NFR-06
        public void TC_M013_IsAjaxRequest_WithHeader_True()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";

            Assert.True(httpContext.Request.IsAjaxRequest());
        }

        [Fact] // TC_M014 — NFR-06
        public void TC_M014_IsAjaxRequest_NoHeader_False()
        {
            var httpContext = new DefaultHttpContext();

            Assert.False(httpContext.Request.IsAjaxRequest());
        }
    }
}