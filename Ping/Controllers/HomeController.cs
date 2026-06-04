using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ping.Models;
using System.Diagnostics;

namespace Ping.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly PingdbContext _context;

        public HomeController(PingdbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var chats = await _context.Chats.ToListAsync();
            return View(chats);
        }

        public IActionResult Messages()
        {
            if (Request.IsAjaxRequest())
            {
                return PartialView();
            }
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
