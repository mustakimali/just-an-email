using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using JustSending.Data;
using JustSending.Data.Models;
using JustSending.Models;
using JustSending.Services;
using JustSending.Services.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<AppController> _logger;

        public AppController(
                AppDbContext db,
                ConversationHub hub,
                IWebHostEnvironment env,
                IConfiguration config,
                ILogger<AppController> logger)
        {
            _db = db;
            _hub = hub;
            _env = env;
            _config = config;
            _logger = logger;
        }

        [Route("")]
        [ResponseCache(Duration = 3600 * 24, Location = ResponseCacheLocation.Any)]
        public Task<IActionResult> NewSession()
        {
            return Session("", "", verifySessionExistance: false);
        }

        private async Task<IActionResult> Session(string id, string id2, bool verifySessionExistance = true)
        {
            if (verifySessionExistance)
            {
                var session = await _db.Get<Session>(id);
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
        public async Task<IActionResult> CreateSessionAjax(string id, string id2)
        {
            if ((id is {Length: not 32})
                || (id2 is {Length: not 32}))
                return BadRequest();

            if (await _db.Exist<Session>(id))
            {
                return Ok();
            }

            await CreateSession(id, id2);
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
                formModel = await Request.StreamFile(stream, _logger, HttpContext.RequestAborted);
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

        [Route("/f/{sessionId}")]
        [HttpPost]
        [DisableFormValueModelBinding]
        [IgnoreAntiforgeryToken]
        [RequestSizeLimit(2_147_483_648)]
        public async Task<IActionResult> PostFileFromCli([FromRoute] string sessionId, IFormFile? file)
        {
            var session = await _db.Get<Session>(sessionId);
            if (session == null)
            {
                return StatusCode(400, new
                {
                    error = "Invalid Session, follow this redirect_uri to start a session in the browser first.",
                    redirect_uri = Url.Action("NewSession", "App", null, "https") + $"#{sessionId}{AppDbContext.NewGuid()}"
                });
            }

            if (!session.ConnectionIds.Any())
            {
                return StatusCode(400, new
                {
                    error =
                        "No client is connected, the session may be lost. Try starting the session again or refreshing the browser window."
                });
            }

            if (file == null && Request.Form.Files.Any())
            {
                file = Request.Form.Files[0];
            }

            if (file == null)
            {
                return NotFound(new
                {
                    error = "No file posted"
                });
            }

            var model = new SessionModel
            {
                SessionId = sessionId,
                ComposerText = file.FileName
            };
            var tmpFile = Path.GetTempFileName();
            await using (var f = System.IO.File.OpenWrite(tmpFile))
            {
                await file.CopyToAsync(f);
            }

            try
            {
                var message = SavePostedFile(tmpFile, model);
                var _ = await SaveMessageAndReturnResponse(message);
                return Json(new
                {
                    session_id = sessionId,
                    message_id = message.Id,
                    file_name = message.Text,
                    file_size = file.Length,
                    downlod_url = Url.Action("DownloadFile", "App", new { id = message.Id, sessionId = sessionId },
                        "https")
                });
            }
            catch (InvalidOperationException)
            {
                return StatusCode(413, new
                {
                    error = "The posted file is too large"
                });
            }
            catch (BadHttpRequestException)
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

        private static void DeleteIfExists(string path)
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
            // validate
            var session = await _db.GetSession(message.SessionId, message.SessionIdVerification);
            if (session == null)
                return BadRequest();

            await _db.MessagesInsert(message);
            _db.RecordMessageStats(message);

            await ScheduleOrExtendSessionCleanup(message.SessionId, lite);

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
                Id = AppDbContext.NewGuid(),
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
        public async Task<IActionResult> DownloadFile(string id, string sessionId)
        {
            var msg = await _db.Get<Message>(id);
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
        public Task<IActionResult> GetMessages(string id, string id2, int from) => GetMessagesViewInternal(id, id2, from);

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

            var shareToken = await _db.Get<ShareToken>(model.Token.ToString());
            if (shareToken == null)
            {
                ModelState.AddModelError(nameof(model.Token), "Invalid PIN!");
                return View(model);
            }

            var session = await _db.Get<Session>(shareToken.SessionId);
            if (session == null)
            {
                ModelState.AddModelError(nameof(model.Token), "The Session does not exist!");
                return View(model);
            }

            // Ready to join,
            // Delete the Token
            await _db.Remove<ShareToken>(model.Token.ToString());

            if (model.NoJs && !session.IsLiteSession)
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

            return await Session(session.Id, session.IdVerification);
        }

        [HttpPost]
        [Route("message-raw")]
        public async Task<IActionResult> GetMessage(string messageId, string sessionId)
        {
            var msg = await _db.Get<Message>(messageId);
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
            if (!await IsValidSession(id, id2)) return BadRequest();

            await ConvertToLiteSession(id, id2);

            return Ok();
        }

        private async Task ConvertToLiteSession(string id, string id2)
        {
            var session = await _db.GetSession(id, id2);
            if (session == null) return;
            session.IsLiteSession = true;
            await _db.Set(id, session);

            await _hub.RedirectTo(id, Url.Action(nameof(LiteSession), new {id1 = id, id2, r = "u"}))
                .ConfigureAwait(false);
        }

        private async Task<IActionResult> GetMessagesViewInternal(string id, string id2, int from)
        {
            var messages = await GetMessagesInternal(id, id2, from);
            if (messages.Length == 0) return new EmptyResult();

            return View("GetMessages", messages);
        }
    }
}