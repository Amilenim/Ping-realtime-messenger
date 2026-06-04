using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ping.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using User = Ping.Models.User;

namespace Ping.Controllers
{
    public class AccountController : Controller
    {
        private readonly PingdbContext _context;

        public AccountController(PingdbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Register() => View(new RegisterViewModel());

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var isExist = await _context.Users.AnyAsync(u => u.Username == model.Username);
            if (isExist)
            {
                ModelState.AddModelError("Username", "Цей логін вже зайнятий");
                return View(model);
            }

            var newUser = new User
            {
                Name = model.Name,
                Username = model.Username,
                PhoneNumber = model.PhoneNumber,
                Password = BCrypt.Net.BCrypt.HashPassword(model.Password)
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid) return View(model);

            var dbUser = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == model.Username);

            if (dbUser != null && BCrypt.Net.BCrypt.Verify(model.Password, dbUser.Password))
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, dbUser.Username),
                    new Claim(ClaimTypes.NameIdentifier, dbUser.Id.ToString()),
                    new Claim("DisplayName", dbUser.Name)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties { IsPersistent = true };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction("Index", "Home");
            }

            ModelState.AddModelError("", "Невірний логін або пароль");
            return View(model);
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }
    }
}