﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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
        public async Task<IActionResult> Stats([FromServices] StatsDbContext db, [FromServices] IDataStore store, int? date = null)
        {
            var stat = await db.StatsFindByDateOrNew(date);

            ViewData["LastBuild"] = new DirectoryInfo(Directory.GetCurrentDirectory()).LastWriteTimeUtc.ToString("s");
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

        [Route("error")]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}