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
        public IActionResult Stats(int date = -1)
        {
            var stat = _db.Statistics.FindById(date);
            if(stat == null) return NotFound();

            return View(stat);
        }

        [Route("api/prime")]
        public IActionResult Prime([FromServices] IHostingEnvironment env)
        {
            Response.Headers.Add("Access-Control-Allow-Origin", "*");
            
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
