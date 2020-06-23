using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using JustSending.Models;
using JustSending.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JustSending.Controllers
{
    public partial class AppController : Controller
    {
        [Route("lite")]
        public IActionResult LiteSessionNew() => RedirectToAction(nameof(LiteSession), new { id1 = Guid.NewGuid().ToString("N"), id2 = Guid.NewGuid().ToString("N") });

        [Route("lite/{id1}/{id2}")]
        public async Task<IActionResult> LiteSession(string id1, string id2)
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

                await _hub.AddSessionNotification(id1, "Session Created", true);
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

        [Route("lite/{id1}/{id2}/delete")]
        public IActionResult LiteSessionDeletePrompt(string id1, string id2)
        {
            if (!IsValidSession(id1, id2))
                return RedirectToAction(nameof(HomeController.Index), "Home");

            var vm = new LiteSessionModel
            {
                SessionId = id1,
                SessionVerification = id2
            };
            return View(vm);
        }

        [HttpPost]
        [Route("lite/post/files")]
        //[RequestSizeLimit(2_147_483_648)]
        public async Task<IActionResult> PostLite(SessionModel model, IFormFile file)
        {
            if (file == null)
            {
                if (string.IsNullOrEmpty(model.ComposerText))
                    return RedirectToLiteSession(model);

                return await Post(model, true);
            }

            var postedFilePath = Path.GetTempFileName();
            using (var fs = new FileStream(postedFilePath, FileMode.OpenOrCreate))
            {
                await file.CopyToAsync(fs);
            }

            try
            {
                if (string.IsNullOrEmpty(model.ComposerText))
                {
                    model.ComposerText = file.FileName;
                }

                var message = SavePostedFile(postedFilePath, model);
                await SaveMessageAndReturnResponse(message, true);
            }
            catch (Microsoft.AspNetCore.Http.BadHttpRequestException)
            {
                return BadRequest();
            }

            return RedirectToLiteSession(model);
        }

        [HttpPost]
        [Route("lite/share-token/cancel")]
        public IActionResult CancelShareToken(SessionModel model)
        {
            if (IsValidRequest(model))
            {
                _hub.CancelShareSessionBySessionId(model.SessionId);
            }

            return RedirectToLiteSession(model);
        }

        [HttpPost]
        [Route("lite/share-token/new")]
        public IActionResult CreateShareToken(SessionModel model)
        {
            if (IsValidRequest(model))
            {
                CreateShareToken(model.SessionId);
            }

            return RedirectToLiteSession(model);
        }

        [HttpPost]
        [Route("lite/erase-session")]
        public async Task<IActionResult> EraseSession(SessionModel model, [FromServices] BackgroundJobScheduler jobScheduler)
        {
            if (IsValidRequest(model))
            {
                var cIds = jobScheduler.EraseSessionReturnConnectionIds(model.SessionId);
                await _hub.NotifySessionDeleted(cIds);
            }
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        [HttpPost]
        [Route("lite/poll")]
        public async Task<LiteSessionStatus> PollSessionStatus(string id, string id2, int from)
        {
            var data = new LiteSessionStatus();

            var token = _db.ShareTokens.FindOne(s => s.SessionId == id);
            var messages = GetMessagesInternal(id, id2, from).ToArray();

            data.HasToken = token != null;
            data.HasSession = IsValidSession(id, id2);
            data.Token = token?.Id.ToString(Constants.TOKEN_FORMAT_STRING);

            ViewData["LiteMode"] = true;

            if (messages.Length > 0)
                data.MessageHtml = await this.RenderViewAsync("GetMessages", messages).ConfigureAwait(false);

            return data;
        }

        private IActionResult RedirectToLiteSession(SessionModel model) => RedirectToAction(nameof(LiteSession), new { id1 = model.SessionId, id2 = model.SessionVerification });

        private void ScheduleOrExtendSessionCleanup(string sessionId, bool isLiteSession)
        {
            var session = _db.Sessions.FindById(sessionId);
            if (session == null) return;

            TimeSpan triggerAfter = TimeSpan.FromHours(isLiteSession ? 6 : 24);

            if (!string.IsNullOrEmpty(session.CleanupJobId))
                BackgroundJob.Delete(session.CleanupJobId);
            var id = BackgroundJob.Schedule<BackgroundJobScheduler>(b => b.Erase(sessionId), triggerAfter);
            session.CleanupJobId = id;
            _db.Sessions.Upsert(session);
        }
    }
}