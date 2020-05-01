using System.Diagnostics;
using JustSending.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using IOFile = System.IO.File;
using JustSending.Data;
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

        [Route("stats")]
        public IActionResult Stats([FromServices] AppDbContext db, int date = -1)
        {
            var stat = db.Statistics.FindById(date);
            if (stat == null)
            {
                stat = new Stats();
            }
            return View(stat);
        }

        [Route("stats/raw")]
        public IActionResult StatsRaw([FromServices] AppDbContext db)
        {
            var data = db
                        .Statistics
                        .Find(x => x.Id > 1)
                        .GroupBy(x => x.Id.ToString().Substring(0, 2))
                        .Select(x => new
                        {
                            Year = x.Key,
                            Months = x.GroupBy(y => y.Id.ToString().Substring(2, 2))
                                        .Select(c => new
                                        {
                                            Month = c.Key,
                                            Days = c
                                        })
                        });

            return Json(data);
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
