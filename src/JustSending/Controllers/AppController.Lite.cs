using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JustSending.Data;
using JustSending.Models;
using JustSending.Services;
using JustSending.Services.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using IOFile = System.IO.File;

namespace JustSending.Controllers
{
    public partial  class AppController : Controller
    {
        [Route("lite")]
        public IActionResult LiteSessionNew() => RedirectToAction(nameof(LiteSession), new {id1 = Guid.NewGuid().ToString("N"), id2 = Guid.NewGuid().ToString("N")});

        [Route("{id1}/{id2}")]
        public IActionResult LiteSession(string id1, string id2)
        {
            int? token = null;
            var session = _db.Sessions.FindById(id1);
            if (session != null && session.IdVerification == id2)
            {
                // Connected
                _db.RecordStats(s => s.Devices++);
                token = _db.ShareTokens.FindOne(x => x.SessionId == id1)?.Id;
            }
            else
            {
                token = CreateSession(id1, id2, liteSession: true);
            }

            var vm = new LiteSessionModel
            {
                SessionId = id1,
                SessionVerification = id2,

                Token = token,
                Messages = GetMessagesInternal(id1, id2, -1).ToArray()
            };
            ViewData["LiteMode"] = true;
            return View(vm);
        }
        
    }
}