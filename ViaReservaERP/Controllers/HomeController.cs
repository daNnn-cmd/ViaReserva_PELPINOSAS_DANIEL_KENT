using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ViaReservaERP.Models;
using Microsoft.EntityFrameworkCore;
namespace ViaReservaERP.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ViaReservaERP.Data.ViaReservaDbContext _db;

        public HomeController(ILogger<HomeController> logger, ViaReservaERP.Data.ViaReservaDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var hotels = await _db.Companies
                .Where(c => !c.IsDeleted && c.IsActive && c.SubscriptionStatus == "Active")
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
            
            return View(hotels);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
