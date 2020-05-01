using System;
using System.IO;
using System.Linq;
using Hangfire;
using JustSending.Data;
using Microsoft.AspNetCore.Hosting;

namespace JustSending.Services
{
    public class BackgroundJobScheduler
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public BackgroundJobScheduler(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        public void Erase(string sessionId) => EraseSessionReturnConnectionIds(sessionId);

        public string[] EraseSessionReturnConnectionIds(string sessionId)
        {
            var session = _db.Sessions.FindById(sessionId);
            if (session == null) return new string[0];

            _db.Sessions.Delete(sessionId);
            _db.Messages.DeleteMany(x => x.SessionId == sessionId);
            _db.ShareTokens.DeleteMany(x => x.SessionId == sessionId);

            var connectionIds = _db
                .Connections
                .Find(x => x.SessionId == sessionId)
                .Select(x => x.ConnectionId)
                .ToArray();

            _db.Connections.DeleteMany(x => x.SessionId == sessionId);

            try
            {
                DeleteUploadedFiles(sessionId);
            }
            catch
            {
                BackgroundJob.Schedule(() => DeleteUploadedFiles(sessionId), TimeSpan.FromMinutes(30));
            }

            if (!string.IsNullOrEmpty(session.CleanupJobId))
                BackgroundJob.Delete(session.CleanupJobId);

            return connectionIds;
        }

        public void DeleteUploadedFiles(string sessionId)
        {
            var folder = Helper.GetUploadFolder(sessionId, _env.WebRootPath);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}