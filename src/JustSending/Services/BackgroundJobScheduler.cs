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
    public class BackgroundJobScheduler
    {
        private readonly AppDbContext _db;
        private readonly StatsDbContext _dbStats;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<BackgroundJobScheduler> _logger;

        public BackgroundJobScheduler(AppDbContext db, StatsDbContext dbStats, IWebHostEnvironment env, ILogger<BackgroundJobScheduler> logger)
        {
            _db = db;
            _dbStats = dbStats;
            _env = env;
            _logger = logger;
        }

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
            if (session == null) return Array.Empty<string>();

            var messageCount = await _db.Count<Message>(sessionId);
            for (var i = 0; i <= messageCount; i++)
            {
                var id = $"{session.Id}-{i}";
                var messageId = await _db.GetKv<string>(id);
                if (messageId != null)
                    await _db.Remove<Message>(messageId);
                await _db.Remove<string>(id);
            }

            await _db.SetCount<Message>(sessionId, null);

            // remove all connections
            foreach (var connectionId in session.ConnectionIds)
                await _db.Remove<SessionMetaByConnectionId>(connectionId);

            // remove share token
            var sessionShareToken = await _db.GetKv<SessionShareToken>(sessionId);
            if (sessionShareToken != null)
            {
                await _db.Remove<ShareToken>(sessionShareToken.Token.ToString());
                await _db.Remove<SessionShareToken>(sessionId);
            }

            var connectionIds = session.ConnectionIds;

            if (!string.IsNullOrEmpty(session.CleanupJobId))
                BackgroundJob.Delete(session.CleanupJobId);

            await _db.Remove<Session>(sessionId);

            return connectionIds.ToArray();
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