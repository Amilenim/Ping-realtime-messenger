using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ping.Models;
using System.Security.Claims;

namespace Ping.Controllers
{
    [Authorize]
    public class SettingsController : Controller
    {
        private readonly PingdbContext _context;
        private readonly IWebHostEnvironment _environment;

        public SettingsController(PingdbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpGet]
        public async Task<IActionResult> Settings()
        {
            if (!Request.IsAjaxRequest())
            {
                return RedirectToAction("Index", "Home", new { route = "/Settings/Settings" });
            }

            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdStr, out int userId))
            {
                var user = await _context.Users.FindAsync(userId);
                if (user != null)
                {
                    var model = new ProfileViewModel
                    {
                        Name = user.Name,
                        PhoneNumber = user.PhoneNumber,
                        AvatarPath = user.AvatarPath,
                        Username = user.Username
                    };
                    return PartialView(model);
                }
            }
            return RedirectToAction("Login", "Account");
        }

        [HttpPost]
        public async Task<IActionResult> Settings(ProfileViewModel model)
        {
            if (!Request.IsAjaxRequest())
            {
                return RedirectToAction("Index", "Home", new { route = "/Settings/Settings" });
            }

            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdStr, out int userId)) return RedirectToAction("Login", "Account");

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(model.Username))
            {
                var usernameExists = await _context.Users.AnyAsync(u => u.Username == model.Username.Trim() && u.Id != userId);
                if (usernameExists)
                {
                    ModelState.AddModelError("Username", "Цей никнейм вже зайнятий іншим користувачем.");
                }
            }
            else
            {
                ModelState.AddModelError("Username", "Нікнейм не може бути порожнім.");
            }

            if (!string.IsNullOrWhiteSpace(model.PhoneNumber))
            {
                var phoneExists = await _context.Users.AnyAsync(u => u.PhoneNumber == model.PhoneNumber.Trim() && u.Id != userId);
                if (phoneExists)
                {
                    ModelState.AddModelError("PhoneNumber", "Цей номер телефону вже прив'язаний до іншого аккаунту.");
                }
            }

            if (!string.IsNullOrWhiteSpace(model.NewPassword))
            {
                if (string.IsNullOrWhiteSpace(model.CurrentPassword))
                {
                    ModelState.AddModelError("CurrentPassword", "Введіть поточний пароль для підтвердження змін.");
                }
                else if (!BCrypt.Net.BCrypt.Verify(model.CurrentPassword, user.Password))
                {
                    ModelState.AddModelError("CurrentPassword", "Невірний поточний пароль.");
                }

                if (model.NewPassword != model.ConfirmPassword)
                {
                    ModelState.AddModelError("ConfirmPassword", "Новий пароль і підтвердження не співпадають.");
                }
            }

            if (!ModelState.IsValid) return PartialView(model);

            bool usernameChanged = user.Username != model.Username?.Trim();

            user.Name = model.Name;
            user.PhoneNumber = model.PhoneNumber?.Trim();
            user.Username = model.Username?.Trim();

            if (!string.IsNullOrWhiteSpace(model.NewPassword))
            {
                user.Password = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            }

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            if (usernameChanged)
            {
                var identity = (ClaimsIdentity)User.Identity;
                var nameClaim = identity.FindFirst(ClaimTypes.Name);
                if (nameClaim != null) identity.RemoveClaim(nameClaim);
                identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties { IsPersistent = true });
            }

            ViewBag.SuccessMessage = "Профіль успішно оновлено!";

            model.CurrentPassword = null;
            model.NewPassword = null;
            model.ConfirmPassword = null;
            model.AvatarPath = user.AvatarPath;

            return PartialView(model);
        }

        [HttpPost]
        public async Task<IActionResult> UploadAvatar(IFormFile avatar)
        {
            if (avatar == null || avatar.Length == 0) return Json(new { success = false, message = "Файл не вибраний" });
            if (!avatar.ContentType.StartsWith("image/")) return Json(new { success = false, message = "Тільки зображення" });

            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdStr, out int userId);

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return Json(new { success = false });

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "avatars");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            if (!string.IsNullOrEmpty(user.AvatarPath))
            {
                var oldPath = Path.Combine(_environment.WebRootPath, user.AvatarPath.TrimStart('/'));
                if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
            }

            var extension = Path.GetExtension(avatar.FileName);
            var fileName = $"{userId}_{DateTime.UtcNow.Ticks}{extension}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await avatar.CopyToAsync(stream);
            }

            user.AvatarPath = $"/uploads/avatars/{fileName}";
            await _context.SaveChangesAsync();

            return Json(new { success = true, avatarUrl = user.AvatarPath });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAvatar()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdStr, out int userId);

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return Json(new { success = false });

            if (!string.IsNullOrEmpty(user.AvatarPath))
            {
                var oldPath = Path.Combine(_environment.WebRootPath, user.AvatarPath.TrimStart('/'));
                if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);

                user.AvatarPath = null;
                await _context.SaveChangesAsync();
            }
            return Json(new { success = true });
        }
    }
}