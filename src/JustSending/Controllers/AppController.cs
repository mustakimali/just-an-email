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
    [Route("app")]
    public partial class AppController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ConversationHub _hub;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public AppController(
                AppDbContext db,
                ConversationHub hub,
                IWebHostEnvironment env,
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

            CreateSession(id, id2);
            return Accepted();
        }

        [Route("post/files-stream")]
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

            try
            {
                var message = SavePostedFile(postedFilePath, model);
                return await SaveMessageAndReturnResponse(message);
            }
            catch (Microsoft.AspNetCore.Http.BadHttpRequestException)
            {
                return BadRequest();
            }

        }

        private Message SavePostedFile(string postedFilePath, SessionModel model)
        {
            var fileInfo = new FileInfo(postedFilePath);
            if (fileInfo.Length > Convert.ToInt64(_config["MaxUploadSizeBytes"]))
            {
                //BadHttpRequestException.Throw(RequestRejectionReason.InvalidContentLength, HttpMethod.Post);
                throw new InvalidOperationException();
            }

            var message = GetMessageFromModel(model, true);
            var uploadDir = Helper.GetUploadFolder(model.SessionId, _env.WebRootPath);
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
            return message;
        }

        [Route("post/files")]
        [HttpPost]
        public async Task<IActionResult> PostWithFileAfterStreaming(SessionModel model)
        {
            // Locate the file
            var uploadedPath = Path.Combine(Helper.GetUploadFolder(string.Empty, _env.WebRootPath), model.SocketConnectionId + ConversationHub.FILE_EXT);
            if (!IOFile.Exists(uploadedPath))
            {
                return NotFound();
            }

            var message = GetMessageFromModel(model);
            var uploadFolder = Helper.GetUploadFolder(model.SessionId, _env.WebRootPath);
            var moveToPath = Path.Combine(uploadFolder, message.Id + ConversationHub.FILE_EXT);

            if (!Directory.Exists(uploadFolder))
                Directory.CreateDirectory(uploadFolder);

            IOFile.Move(uploadedPath, moveToPath);

            var fileInfo = new FileInfo(moveToPath);
            if (!ModelState.IsValid || fileInfo.Length > Convert.ToInt64(_config["MaxUploadSizeBytes"]))
            {
                DeleteIfExists(moveToPath);

                return BadRequest(ModelState);
            }

            message.HasFile = true;
            message.FileSizeBytes = fileInfo.Length;

            return await SaveMessageAndReturnResponse(message);
        }

        private void DeleteIfExists(string path)
        {
            if (IOFile.Exists(path))
                IOFile.Delete(path);
        }

        [Route("post")]
        [HttpPost]
        public async Task<IActionResult> Post(SessionModel model, bool lite = false)
        {
            var message = GetMessageFromModel(model);
            return await SaveMessageAndReturnResponse(message, lite);
        }

        private async Task<IActionResult> SaveMessageAndReturnResponse(Message message, bool lite = false)
        {
            _db.MessagesInsert(message);
            _db.RecordMessageStats(message);

            ScheduleOrExtendSessionCleanup(message.SessionId, lite);

            await _hub.RequestReloadMessage(message.SessionId);

            if (lite)
                return RedirectToAction(nameof(LiteSession), new { id1 = message.SessionId, id2 = message.SessionIdVerification });
            else
                return Accepted();
        }

        private Message GetMessageFromModel(SessionModel model, bool hasFile = false)
        {
            var utcNow = DateTime.UtcNow;
            return new Message
            {
                Id = _db.NewGuid(),
                SessionId = model.SessionId,
                SessionIdVerification = model.SessionVerification,
                SocketConnectionId = model.SocketConnectionId,
                EncryptionPublicKeyAlias = model.EncryptionPublicKeyAlias,
                DateSent = utcNow,
                DateSentEpoch = utcNow.ToEpoch(),
                Text = model.ComposerText,
                HasFile = hasFile
            };
        }

        [HttpGet]
        [Route("file/{id}/{sessionId}")]
        public IActionResult DownloadFile(string id, string sessionId)
        {
            var msg = _db.Messages.FindById(id);
            if (msg == null || msg.SessionId != sessionId)
            {
                // Chances of link forgery? 0%!
                return NotFound();
            }

            var uploadDir = Helper.GetUploadFolder(sessionId, _env.WebRootPath);
            var path = Path.Combine(uploadDir, msg.Text);
            if (!IOFile.Exists(path))
                return NotFound();

            _db.RecordStats(s => s.FilesSizeBytes += msg.FileSizeBytes);
            return PhysicalFile(path, "application/" + Path.GetExtension(path).Trim('.'), msg.Text);
        }

        [Route("messages"), HttpPost]
        public IActionResult GetMessages(string id, string id2, int @from) => GetMessagesViewInternal(id, id2, @from);

        [Route("connect")]
        public IActionResult Connect()
        {
            return View();
        }

        [HttpPost]
        [Route("connect")]
        public async Task<IActionResult> Connect(ConnectViewModel model)
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

            if(model.NoJs && !session.IsLiteSession)
            {
                await ConvertToLiteSession(session.Id, session.IdVerification);
                session.IsLiteSession = true;
            }
            
            if (session.IsLiteSession)
            {
                await _hub.AddSessionNotification(session.Id, "A new device has been connected!", true);
                return RedirectToAction(nameof(LiteSession), new { id1 = session.Id, id2 = session.IdVerification });
            }

            await _hub.HideSharePanel(session.Id);

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

        [HttpPost]
        [Route("lite/notify/unsupported-device")]
        public async Task<IActionResult> NotifyUnsupportedClientConnected(string id, string id2)
        {
            if (!IsValidSession(id, id2)) return BadRequest();

            await ConvertToLiteSession(id, id2);

            return Ok();
        }

        private async Task ConvertToLiteSession(string id, string id2)
        {
            var session = _db.Sessions.FindById(id);
            session.IsLiteSession = true;
            _db.Sessions.Upsert(session);

            await _hub.RedirectTo(id, Url.Action(nameof(LiteSession), new { id1 = id, id2, r = "u" }))
                .ConfigureAwait(false);
        }

        private IActionResult GetMessagesViewInternal(string id, string id2, int @from)
        {
            var messages = GetMessagesInternal(id, id2, @from).ToArray();
            if (messages.Length == 0) return new EmptyResult();

            return View("GetMessages", messages);
        }
    }
}