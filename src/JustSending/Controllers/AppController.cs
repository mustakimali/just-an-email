using System.ComponentModel;
using System;
using System.IO;
using System.Linq;
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
using OpenTelemetry.Trace;
using Hangfire;

namespace JustSending.Controllers
{
    [Route("app")]
    public partial class AppController : Controller
    {
        private readonly AppDbContext _db;
        private readonly StatsDbContext _statDb;
        private readonly ConversationHub _hub;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly Tracer _tracer;
        private readonly ILogger<AppController> _logger;

        public AppController(
                AppDbContext db,
                StatsDbContext statDb,
                ConversationHub hub,
                IWebHostEnvironment env,
                IConfiguration config,
                Tracer tracer,
                ILogger<AppController> logger)
        {
            _db = db;
            _statDb = statDb;
            _hub = hub;
            _env = env;
            _config = config;
            _tracer = tracer;
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
                var session = await _db.GetSessionById(id);
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
            if ((id is { Length: not 32 })
                || (id2 is { Length: not 32 }))
                return BadRequest();

            if (await _db.GetSessionById(id) != null)
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
            var session = await _db.GetSessionById(sessionId);
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

            // Read additional form fields for encrypted uploads
            var encryptionAlias = Request.Form["EncryptionPublicKeyAlias"].FirstOrDefault();
            var composerText = Request.Form["ComposerText"].FirstOrDefault();

            var model = new SessionModel
            {
                SessionId = sessionId,
                ComposerText = string.IsNullOrEmpty(composerText) ? file.FileName : composerText,
                EncryptionPublicKeyAlias = encryptionAlias
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
                    download_url = Url.Action("DownloadFile", "App", new { id = message.Id, sessionId = sessionId },
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
            using var span = _tracer.StartActiveSpan("save-posted-file");
            span.SetAttribute("path", postedFilePath);

            var fileInfo = new FileInfo(postedFilePath);
            if (fileInfo.Length > Convert.ToInt64(_config["MaxUploadSizeBytes"]))
            {
                //BadHttpRequestException.Throw(RequestRejectionReason.InvalidContentLength, HttpMethod.Post);
                throw new InvalidOperationException();
            }

            var message = GetMessageFromModel(model, true);
            var uploadDir = Helper.GetUploadFolder(model.SessionId, _env.WebRootPath);
            if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

            string diskFileName;
            if (!string.IsNullOrEmpty(model.EncryptionPublicKeyAlias))
            {
                // For encrypted files, use the message ID as disk filename to avoid
                // issues with base64 characters (like /) in the encrypted filename
                diskFileName = message.Id + ".enc";
            }
            else
            {
                // For unencrypted files, use the original filename
                diskFileName = message.Text;
                if (IOFile.Exists(Path.Combine(uploadDir, diskFileName)))
                {
                    diskFileName = Path.GetFileNameWithoutExtension(message.Text) + "_" + message.Id.Substring(0, 6) + Path.GetExtension(message.Text);
                }
            }

            var destUploadPath = Path.Combine(uploadDir, diskFileName);

            message.FileName = diskFileName;
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

        [Route("post/quick-upload")]
        [HttpPost]
        public async Task<IActionResult> QuickUpload()
        {
            if (!Request.Form.Files.Any()) return BadRequest();
            var file = Request.Form.Files[0];

            var id = Guid.NewGuid().ToString("N");
            var id2 = Guid.NewGuid().ToString("N");
            await CreateSession(id, id2, false);
            _logger.LogInformation("Session created: {id} {id2}", id, id2);

            var tempFile = Path.GetTempFileName();
            await using (var f = IOFile.OpenWrite(tempFile))
            {
                await file.CopyToAsync(f);
            }
            _logger.LogInformation("Temp file created: {file}", tempFile);

            var message = SavePostedFile(tempFile, new SessionModel
            {
                SessionId = id,
                SessionVerification = id2,
                SocketConnectionId = id,
                ComposerText = file.FileName
            });
            await SaveMessageAndReturnResponse(message, false);
            _logger.LogInformation("Message saved: {message}", message.Id);

            return Json(new
            {
                session_id = id,
                session_idv = id2,
                message_id = message.Id,
                file_name = message.Text,
                file_size = message.FileSizeBytes,
            });
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
            var session = await _db.GetSessionById(message.SessionId);
            if (session == null)
                return BadRequest();

            await _db.MessagesInsert(message);
            _statDb.RecordMessageStats(message);

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
            var msg = await _db.GetMessagesById(id);
            if (msg == null || msg.SessionId != sessionId)
            {
                // Chances of link forgery? 0%!
                return NotFound();
            }

            var uploadDir = Helper.GetUploadFolder(sessionId, _env.WebRootPath);
            // Use FileName if available, fall back to Text for backwards compatibility
            var diskFileName = msg.FileName ?? msg.Text;
            var path = Path.Combine(uploadDir, diskFileName);
            if (!IOFile.Exists(path))
                return NotFound();

            return PhysicalFile(path, "application/octet-stream", diskFileName);
        }

        [Route("messages"), HttpPost]
        public Task<IActionResult> GetMessages(string id, string id2, int from) => GetMessagesViewInternal(id, id2, from);

        public class SaveKeyRequest
        {
            public string SessionId { get; set; }
            public string SessionVerification { get; set; }
            public string Alias { get; set; }
            public string PublicKey { get; set; }
        }

        [Route("key")]
        [HttpPost]
        public async Task<IActionResult> SavePublicKey([FromBody] SaveKeyRequest request)
        {
            if (string.IsNullOrEmpty(request?.SessionId) ||
                string.IsNullOrEmpty(request?.SessionVerification) ||
                string.IsNullOrEmpty(request?.Alias) ||
                string.IsNullOrEmpty(request?.PublicKey))
            {
                return BadRequest();
            }

            // Validate the public key is a well-formed RSA JWK
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(request.PublicKey);
                var root = doc.RootElement;
                if (root.GetProperty("kty").GetString() != "RSA" ||
                    !root.TryGetProperty("n", out _) ||
                    !root.TryGetProperty("e", out _))
                {
                    return BadRequest(new { error = "Invalid RSA public key" });
                }
            }
            catch
            {
                return BadRequest(new { error = "Invalid public key format" });
            }

            var session = await _db.GetSessionById(request.SessionId);
            if (session == null || session.IdVerification != request.SessionVerification)
            {
                return NotFound();
            }

            var publicKeyId = await _db.SavePublicKey(request.SessionId, request.Alias, request.PublicKey);
            return Ok(new { id = publicKeyId });
        }

        [Route("/k/{id}")]
        [HttpGet]
        public async Task<IActionResult> GetPublicKey(string id)
        {
            var publicKey = await _db.GetPublicKeyById(id);
            if (publicKey == null)
            {
                return NotFound(new { error = "Public key not found" });
            }

            return Content(publicKey.PublicKeyJson, "application/json");
        }

        [Route("/k/{id}/upload")]
        [HttpGet]
        public async Task<IActionResult> GetUploadScript(string id)
        {
            var publicKey = await _db.GetPublicKeyById(id);
            if (publicKey == null)
            {
                return NotFound("# Error: Public key not found");
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            // Base64-encode all user-supplied values to prevent code injection
            var jwkB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(publicKey.PublicKeyJson));
            var sessionIdB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(publicKey.SessionId));
            var aliasB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(publicKey.Alias));

            var script = $@"#!/usr/bin/env python3
# Usage: curl -s {baseUrl}/k/{id}/upload | python3 - file.txt
import sys, json, base64, os
from cryptography.hazmat.primitives.asymmetric import rsa, padding
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.backends import default_backend
import urllib.request

if len(sys.argv) < 2:
    print('Usage: curl -s {baseUrl}/k/{id}/upload | python3 - <file>', file=sys.stderr)
    sys.exit(1)

jwk = json.loads(base64.b64decode('{jwkB64}').decode())
session_id = base64.b64decode('{sessionIdB64}').decode()
alias = base64.b64decode('{aliasB64}').decode()

n = int.from_bytes(base64.urlsafe_b64decode(jwk['n'] + '=='), 'big')
e = int.from_bytes(base64.urlsafe_b64decode(jwk['e'] + '=='), 'big')
pub = rsa.RSAPublicNumbers(e, n).public_key(default_backend())

filepath = sys.argv[1]
filename = os.path.basename(filepath)
with open(filepath, 'rb') as f:
    data = f.read()

aes_key = os.urandom(32)
iv = os.urandom(12)
encrypted_data = AESGCM(aes_key).encrypt(iv, data, None)
encrypted_key = pub.encrypt(aes_key, padding.OAEP(padding.MGF1(hashes.SHA256()), hashes.SHA256(), None))

payload = base64.b64encode(json.dumps({{
    'encryptedKey': base64.b64encode(encrypted_key).decode(),
    'iv': base64.b64encode(iv).decode(),
    'encryptedData': base64.b64encode(encrypted_data).decode()
}}).encode()).decode()

fn_iv = os.urandom(12)
fn_encrypted = AESGCM(aes_key).encrypt(fn_iv, filename.encode(), None)
fn_key_enc = pub.encrypt(aes_key, padding.OAEP(padding.MGF1(hashes.SHA256()), hashes.SHA256(), None))
fn_payload = base64.b64encode(json.dumps({{
    'encryptedKey': base64.b64encode(fn_key_enc).decode(),
    'iv': base64.b64encode(fn_iv).decode(),
    'encryptedData': base64.b64encode(fn_encrypted).decode()
}}).encode()).decode()

boundary = '----PythonFormBoundary'
body = (
    f'--{{boundary}}\r\nContent-Disposition: form-data; name=""EncryptionPublicKeyAlias""\r\n\r\n{{alias}}\r\n'
    f'--{{boundary}}\r\nContent-Disposition: form-data; name=""ComposerText""\r\n\r\n{{fn_payload}}\r\n'
    f'--{{boundary}}\r\nContent-Disposition: form-data; name=""file""; filename=""{{filename}}.enc""\r\nContent-Type: application/octet-stream\r\n\r\n'
).encode() + payload.encode() + f'\r\n--{{boundary}}--\r\n'.encode()

req = urllib.request.Request(f'{baseUrl}/f/{{session_id}}', body, {{
    'Content-Type': f'multipart/form-data; boundary={{boundary}}'
}})
urllib.request.urlopen(req)
print(f'Uploaded: {{filename}}')
";
            return Content(script, "text/plain");
        }

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

            var shareToken = await _db.KvGet<ShareToken>(model.Token.ToString());
            if (shareToken == null)
            {
                ModelState.AddModelError(nameof(model.Token), "Invalid PIN!");
                return View(model);
            }

            var session = await _db.GetSessionById(shareToken.SessionId);
            if (session == null)
            {
                ModelState.AddModelError(nameof(model.Token), "The Session does not exist!");
                return View(model);
            }

            // Ready to join,
            // Delete the Token
            await _db.KvRemove<ShareToken>(model.Token.ToString());

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
            var msg = await _db.KvGet<Message>(messageId);
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
            await _db.AddOrUpdateSession(session);

            await _hub.RedirectTo(id, Url.Action(nameof(LiteSession), new { id1 = id, id2, r = "u" })!)
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