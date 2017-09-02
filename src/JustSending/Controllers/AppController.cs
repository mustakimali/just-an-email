using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JustSending.Data;
using JustSending.Models;
using JustSending.Services;
using LiteDB;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace JustSending.Controllers
{
    [Route("a")]
    public class AppController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ConversationHub _hub;
        private readonly IHostingEnvironment _env;
        private readonly IConfiguration _config;

        public AppController(
                AppDbContext db,
                ConversationHub hub,
                IHostingEnvironment env,
                IConfiguration config)
        {
            _db = db;
            _hub = hub;
            _env = env;
            _config = config;
        }

        [HttpGet]
        [Route("")]
        public IActionResult CreateNewSessionGet() => RedirectToAction("Index", "Home");

        [Route("")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateNewSession()
        {
            var sessionId = _db.NewGuid();
            var idVerification = _db.NewGuid();

            // Create Session
            var session = new Session
            {
                DateCreated = DateTime.UtcNow,
                Id = sessionId,
                IdVerification = idVerification
            };
            _db.Sessions.Insert(session);

            // New ShareToken
            _db.CreateNewShareToken(sessionId);

            //return RedirectToAction("Session", new { id = sessionId, id2 = idVerification });
            return Session(session.Id, session.IdVerification);
        }

        private IActionResult Session(string id, string id2)
        {
            var session = _db.Sessions.FindById(id);
            if (session == null || session.IdVerification != id2)
            {
                return NotFound();
            }

            var vm = new SessionModel
            {
                SessionId = id,
                SessionVarification = id2
            };

            // Find OpenConnection
            var shareToken = _db.ShareTokens.FindOne(x => x.SessionId == id);
            if (shareToken != null)
            {
                vm.ActiveShareToken = shareToken.Id;
            }

            return View("Session", vm);
        }

        [Route("post"), HttpPost]
        public async Task<IActionResult> Post(SessionModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var message = new Message
            {
                Id = _db.NewGuid(),
                SessionId = model.SessionId,
                DateSent = DateTime.UtcNow,
                Text = System.Net.WebUtility.HtmlEncode(model.ComposerText),
                HasFile = Request.Form.Files.Any()
            };

            if (message.HasFile)
            {
                if (Request.Form.Files.First().Length > Convert.ToInt64(_config["MaxUploadSizeBytes"]))
                {
                    return BadRequest();
                }
                var uploadDir = GetUploadFolder(model.SessionId, _env.WebRootPath);
                if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

                // Use original file name
                var fileName = message.Text;
                if (System.IO.File.Exists(Path.Combine(uploadDir, fileName)))
                {
                    // if exist then append digits from the session id
                    fileName = Path.GetFileNameWithoutExtension(message.Text) + "_" + message.Id.Substring(0, 6) + Path.GetExtension(message.Text);
                }
                var path = Path.Combine(uploadDir, fileName);

                message.Text = fileName;

                using (var fileStream = System.IO.File.OpenWrite(path))
                {
                    await Request.Form.Files.First().CopyToAsync(fileStream);
                }
            }

            _db.Messages.Insert(message);
            _hub.RequestReloadMessage(model.SessionId);

            return Accepted();
        }

        [HttpGet]
        [Route("f/{id}/{sessionId}")]
        public IActionResult DownloadFile(string id, string sessionId, string fileName)
        {
            var msg = _db.Messages.FindById(id);
            if (msg == null
                || msg.SessionId != sessionId
                || msg.Text != fileName)
            {
                // Chances of link forgery? 0%!
                return NotFound();
            }

            var uploadDir = GetUploadFolder(sessionId, _env.WebRootPath);
            var path = Path.Combine(uploadDir, msg.Text);
            if (!System.IO.File.Exists(path))
                return NotFound();

            return PhysicalFile(path, "application/" + Path.GetExtension(path).Trim('.'), msg.Text);
        }

        public static string GetUploadFolder(string sessionId, string root)
        {
            return Path.Combine(root, "upload", sessionId);
        }

        [Route("messages"), HttpPost]
        public IActionResult GetMessages(string id, string id2)
        {
            return GetMessagesInternal(id, id2);
        }

        [Route("connect")]
        public IActionResult Connect()
        {
            return View();
        }

        [HttpPost]
        [Route("connect")]
        public IActionResult Connect(ConnectViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var shareToken = _db.ShareTokens.FindById(model.Token);
            if (shareToken == null)
            {
                ModelState.AddModelError(nameof(model.Token), "Invalid PIN!");
                return View(model);
            }

            var session = _db.Sessions.FindById(shareToken.SessionId);
            if (session == null)
            {
                ModelState.AddModelError(nameof(model.Token), "The Session does not exist!");
                return View(model);
            }

            // Ready to join,
            // Delete the Token
            _db.ShareTokens.Delete(model.Token);
            _hub.HideSharePanel(session.Id);

            return Session(session.Id, session.IdVerification);
        }

        [HttpPost]
        [Route("message")]
        public IActionResult GetMessage(string messageId, string sessionId)
        {
            var msg = _db.Messages.FindById(messageId);
            if (msg == null || msg.SessionId != sessionId)
                return NotFound();

            return Json(new
            {
                Content = msg.Text
            });
        }

        private IActionResult GetMessagesInternal(string id, string id2)
        {
            var session = _db.Sessions.FindById(id);
            if (session == null || session.IdVerification != id2)
            {
                return NotFound();
            }

            var messages = _db
                .Messages
                .Find(Query.EQ(nameof(Message.SessionId), id))
                .OrderByDescending(x => x.DateSent);
            return View("GetMessages", messages);
        }
    }
}