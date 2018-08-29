using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using JustSending.Models;
using JustSending.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using IOFile = System.IO.File;

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
            catch (BadHttpRequestException)
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
        public IActionResult EraseSession(SessionModel model, [FromServices] BackgroundJobScheduler jobScheduler)
        {
            if (IsValidRequest(model))
            {
                jobScheduler.EraseSession(model.SessionId);
            }
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        private bool IsValidRequest(SessionModel model)
        {
            var session = _db.Sessions.FindById(model.SessionId);
            if (session == null) return false;

            return session.IdVerification == model.SessionVerification;
        }

        private IActionResult RedirectToLiteSession(SessionModel model) => RedirectToAction(nameof(LiteSession), new { id1 = model.SessionId, id2 = model.SessionVerification });

        private void ScheduleOrExtendSessionCleanup(string sessionId, bool isLiteSession)
        {
            var session = _db.Sessions.FindById(sessionId);
            if (session == null) return;

            TimeSpan triggerAfter = TimeSpan.FromHours(isLiteSession ? 6 : 24);

            if (!string.IsNullOrEmpty(session.CleanupJobId))
                BackgroundJob.Delete(session.CleanupJobId);
            var id = BackgroundJob.Schedule<BackgroundJobScheduler>(b => b.EraseSession(sessionId), triggerAfter);
            session.CleanupJobId = id;
            _db.Sessions.Upsert(session);
        }
    }
}