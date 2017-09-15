using System;
using System.IO;
using System.Linq;
using System.Net;
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
    [Route("app")]
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

        [Route("")]
        [HttpGet]
        public IActionResult NewSession()
        {
            var id = (string)(TempData[nameof(Data.Session.Id)] ?? _db.NewGuid());
            var id2 = (string)(TempData[nameof(Data.Session.IdVerification)] ?? _db.NewGuid());

            return Session(id, id2, verifySessionExistance: false);
        }

        private IActionResult Session(string id, string id2, bool verifySessionExistance = true)
        {
            if (verifySessionExistance)
            {
                var session = _db.Sessions.FindById(id);
                if (session == null || session.IdVerification != id2)
                {
                    return NotFound();
                }
            }

            var vm = new SessionModel
            {
                SessionId = id,
                SessionVerification = id2
            };

            return View("Session", vm);
        }

        [Route("new")]
        [HttpPost]
        public IActionResult CreateSessionAjax(string id, string id2)
        {
            if ((id != null && id.Length != 32)
                || (id2 != null && id2.Length != 32))
                return BadRequest();

            if (_db.Sessions.FindById(id) != null)
            {
                return Ok();
            }

            var sessionId = id;
            var idVerification = id2;

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
            return Accepted();
        }

        [Route("post/files")]
        [HttpPost]
        [DisableFormValueModelBinding]
        [IgnoreAntiforgeryToken]
        [RequestSizeLimit(2_147_483_648)]
        public async Task<IActionResult> PostWithFile()
        {
            FormValueProvider formModel;
            var postedFilePath = Path.GetTempFileName();
            using (var stream = IOFile.Create(postedFilePath))
            {
                formModel = await Request.StreamFile(stream);
            }

            var model = new SessionModel();
            var bindingSuccessful = await TryUpdateModelAsync(model, prefix: "", valueProvider: formModel);

            if (!bindingSuccessful)
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }
            }

            var fileInfo = new FileInfo(postedFilePath);
            if (fileInfo.Length > Convert.ToInt64(_config["MaxUploadSizeBytes"]))
            {
                return BadRequest();
            }
            var message = GetMessageFromModel(model, true);
            var uploadDir = GetUploadFolder(model.SessionId, _env.WebRootPath);
            if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

            // Use original file name
            var fileName = message.Text;
            if (IOFile.Exists(Path.Combine(uploadDir, fileName)))
            {
                // if exist then append digits from the session id
                fileName = Path.GetFileNameWithoutExtension(message.Text) + "_" + message.Id.Substring(0, 6) + Path.GetExtension(message.Text);
            }
            var destUploadPath = Path.Combine(uploadDir, fileName);

            message.Text = fileName;
            message.HasFile = true;
            message.FileSizeBytes = fileInfo.Length;

            IOFile.Move(postedFilePath, destUploadPath);

            return SaveMessageAndReturnResponse(message);
        }

        [Route("post")]
        [HttpPost]
        public IActionResult Post(SessionModel model)
        {
            var message = GetMessageFromModel(model);
            return SaveMessageAndReturnResponse(message);
        }

        private IActionResult SaveMessageAndReturnResponse(Message message)
        {
            _db.MessagesInsert(message);
            _hub.RequestReloadMessage(message.SessionId);

            return Accepted();
        }

        private Message GetMessageFromModel(SessionModel model, bool hasFile = false)
        {
            var utcNow = DateTime.UtcNow;
            var message = new Message
            {
                Id = _db.NewGuid(),
                SessionId = model.SessionId,
                SocketConnectionId = model.SocketConnectionId,
                EncryptionPublicKeyAlias = model.EncryptionPublicKeyAlias,
                DateSent = utcNow,
                DateSentEpoch = utcNow.ToEpoch(),
                Text = model.ComposerText,
                HasFile = hasFile
            };
            return message;
        }

        [HttpGet]
        [Route("file/{id}/{sessionId}")]
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
        public IActionResult GetMessages(string id, string id2, int @from) => GetMessagesInternal(id, id2, @from);

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

            TempData[nameof(Data.Session.Id)] = session.Id;
            TempData[nameof(Data.Session.IdVerification)] = session.IdVerification;

            return RedirectToAction(nameof(NewSession));
            //return Session(session.Id, session.IdVerification);
        }

        [HttpPost]
        [Route("message-raw")]
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

        private IActionResult GetMessagesInternal(string id, string id2, int @from)
        {
            var session = _db.Sessions.FindById(id);
            if (session == null || session.IdVerification != id2)
            {
                return NotFound();
            }

            var messages = _db
                .Messages
                .Find(x => x.SessionId == id && x.SessionMessageSequence > @from)
                .OrderByDescending(x => x.DateSent);
            return View("GetMessages", messages);
        }
    }
}