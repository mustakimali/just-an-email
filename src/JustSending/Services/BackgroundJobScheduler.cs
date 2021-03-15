using System;
using System.IO;
using System.Linq;
using Hangfire;
using JustSending.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace JustSending.Services
{
    public class BackgroundJobScheduler
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<BackgroundJobScheduler> _logger;

        public BackgroundJobScheduler(AppDbContext db, IWebHostEnvironment env, ILogger<BackgroundJobScheduler> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        public void Erase(string sessionId) => EraseSessionReturnConnectionIds(sessionId);

        public string[] EraseSessionReturnConnectionIds(string sessionId)
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
            
            var session = _db.Sessions.FindById(sessionId);
            if (session == null) return Array.Empty<string>();

            _db.Sessions.Delete(sessionId);
            _db.Messages.DeleteMany(x => x.SessionId == sessionId);
            _db.ShareTokens.DeleteMany(x => x.SessionId == sessionId);

            var connectionIds = _db
                .Connections
                .Find(x => x.SessionId == sessionId)
                .Select(x => x.ConnectionId)
                .ToArray();

            _db.Connections.DeleteMany(x => x.SessionId == sessionId);

            if (!string.IsNullOrEmpty(session.CleanupJobId))
                BackgroundJob.Delete(session.CleanupJobId);

            return connectionIds;
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