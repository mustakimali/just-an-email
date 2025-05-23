﻿using System;
using System.IO;
using System.Threading.Tasks;
using Hangfire;
using JustSending.Data;
using JustSending.Data.Models;
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
            var session = await _db.GetSessionById(id1);
            if (session != null && session.IdVerification == id2)
            {
                // Connected
                _statDb.RecordStats(StatsDbContext.RecordType.Device);
                token = (await _db.KvGet<SessionShareToken>(id1))?.Token;
            }
            else
            {
                token = await CreateSession(id1, id2, liteSession: true);

                try
                {
                    await _hub.AddSessionNotification(id1, "Session Created", true);
                }
                catch (System.NullReferenceException)
                {
                    return RedirectToAction("NewSession", "App");
                }
            }

            var vm = new LiteSessionModel
            {
                SessionId = id1,
                SessionVerification = id2,

                Token = token,
                Messages = await GetMessagesInternal(id1, id2, -1)
            };
            ViewData["LiteMode"] = true;
            return View(vm);
        }

        [Route("lite/{id1}/{id2}/delete")]
        public async Task<IActionResult> LiteSessionDeletePrompt(string id1, string id2)
        {
            if (!await IsValidSession(id1, id2))
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
        public async Task<IActionResult> PostLite(SessionModel model, IFormFile? file)
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
        public async Task<IActionResult> CancelShareToken(SessionModel model)
        {
            if (await IsValidRequest(model))
            {
                await _hub.CancelShareSessionBySessionId(model.SessionId);
            }

            return RedirectToLiteSession(model);
        }

        [HttpPost]
        [Route("lite/share-token/new")]
        public async Task<IActionResult> CreateShareToken(SessionModel model)
        {
            if (await IsValidRequest(model))
            {
                await CreateShareToken(model.SessionId);
            }

            return RedirectToLiteSession(model);
        }

        [HttpPost]
        [Route("lite/erase-session")]
        public async Task<IActionResult> EraseSession(SessionModel model, [FromServices] BackgroundJobScheduler jobScheduler)
        {
            if (await IsValidRequest(model))
            {
                var cIds = await jobScheduler.EraseSessionReturnConnectionIds(model.SessionId);
                await _hub.NotifySessionDeleted(cIds);
            }
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        [HttpPost]
        [Route("lite/poll")]
        public async Task<LiteSessionStatus> PollSessionStatus(string id, string id2, int from)
        {
            var data = new LiteSessionStatus();

            var token = (await _db.KvGet<SessionShareToken>(id))?.Token;
            var messages = await GetMessagesInternal(id, id2, from);

            data.HasToken = token != null;
            data.HasSession = await IsValidSession(id, id2);
            data.Token = token?.ToString(Constants.TOKEN_FORMAT_STRING);

            ViewData["LiteMode"] = true;

            if (messages.Length > 0)
                data.MessageHtml = await this.RenderViewAsync("GetMessages", messages).ConfigureAwait(false);

            return data;
        }

        private IActionResult RedirectToLiteSession(SessionModel model) => RedirectToAction(nameof(LiteSession), new { id1 = model.SessionId, id2 = model.SessionVerification });

        [NonAction]
        public async Task ScheduleSessionCleanup(Session session)
        {
            var triggerAfter = TimeSpan.FromHours(24);

            if (!string.IsNullOrEmpty(session.CleanupJobId))
                return;

            var id = BackgroundJob.Schedule<BackgroundJobScheduler>(b => b.Erase(session.Id), triggerAfter);
            session.CleanupJobId = id;
            await _db.AddOrUpdateSession(session);
        }
    }
}