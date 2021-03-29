using System.Diagnostics;
using JustSending.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using IOFile = System.IO.File;
using JustSending.Data;
using JustSending.Data.Models.Bson;
using JustSending.Services;

namespace JustSending.Controllers
{

    public class HomeController : Controller
    {
        [Route("/")]
        public IActionResult Index()
        {
            return View();
        }
        
        [Route("/password")]
        public IActionResult Password()
        {
            return View();
        }

        [Route("stats")]
        public IActionResult Stats([FromServices] StatsDbContext db, int date = -1)
        {
            var stat = db.Statistics.FindById(date);
            if (stat == null)
            {
                stat = new Stats();
            }
            return View(stat);
        }

        [Route("api/prime")]
        public IActionResult Prime([FromServices] IWebHostEnvironment env)
        {
            return Json(new
            {
                Size_1024 = Helper.GetPrime(1024, env),
                Size_2 = Helper.GetPrime(2, env)
            });
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
