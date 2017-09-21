using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using JustSending.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using IOFile = System.IO.File;
using System;
using JustSending.Data;

namespace JustSending.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        public HomeController(AppDbContext db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View();
        }

        [Route("stats")]
        public IActionResult Stats(int date = 0)
        {
            var stat = _db.Statistics.FindById(date);
            if(stat == null) return NotFound();

            return View(stat);
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
