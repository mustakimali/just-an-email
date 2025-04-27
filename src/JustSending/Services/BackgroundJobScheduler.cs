using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using JustSending.Data;
using JustSending.Data.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace JustSending.Services
{
    public class BackgroundJobScheduler(AppDbContext db, IWebHostEnvironment env, ILogger<BackgroundJobScheduler> logger)
    {
        private readonly AppDbContext _db = db;
        private readonly IWebHostEnvironment _env = env;
        private readonly ILogger<BackgroundJobScheduler> _logger = logger;

        public async Task Erase(string sessionId) => await EraseSessionReturnConnectionIds(sessionId);

        public async Task<string[]> EraseSessionReturnConnectionIds(string sessionId)
        {
            try
            {
                DeleteUploadedFiles(sessionId);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error deleting uploaded files {message}", e.GetBaseException().Message);
                BackgroundJob.Schedule(() => DeleteUploadedFiles(sessionId), TimeSpan.FromMinutes(5));
            }

            var session = await _db.GetSessionById(sessionId);
            if (session == null) return [];

            // remove all messages
            var count = await _db.DeleteAllMessagesBySession(sessionId);
            _logger.LogInformation("Deleted {count} messages for session {sessionId}", count, sessionId);

            // remove all connections
            foreach (var connectionId in session.ConnectionIds)
                await _db.KvRemove<SessionMetaByConnectionId>(connectionId);

            // remove share token
            var sessionShareToken = await _db.KvGet<SessionShareToken>(sessionId);
            if (sessionShareToken != null)
            {
                await _db.KvRemove<ShareToken>(sessionShareToken.Token.ToString());
                await _db.KvRemove<SessionShareToken>(sessionId);
            }

            var connectionIds = session.ConnectionIds;

            if (!string.IsNullOrEmpty(session.CleanupJobId))
                BackgroundJob.Delete(session.CleanupJobId);

            await _db.DeleteSession(sessionId);

            return [.. connectionIds];
        }

        public void DeleteUploadedFiles(string sessionId)
        {
            var folder = Helper.GetUploadFolder(sessionId, _env.WebRootPath);
            if (Directory.Exists(folder))
            {
                _logger.LogInformation("Deleting folder {path}", folder);
                Directory.Delete(folder, true);
                _logger.LogInformation("Folder deleted: {path}", folder);
            }
            else
            {
                _logger.LogInformation("Folder does not exist: {path}", folder);
            }
        }
    }
}