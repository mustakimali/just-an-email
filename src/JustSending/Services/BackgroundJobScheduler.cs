using System;
using System.IO;
using Hangfire;
using JustSending.Data;
using Microsoft.AspNetCore.Hosting;

namespace JustSending.Services
{
    public class BackgroundJobScheduler
    {
        private readonly AppDbContext _db;
        private readonly IHostingEnvironment _env;

        public BackgroundJobScheduler(AppDbContext db, IHostingEnvironment env)
        {
            _db = db;
            _env = env;
        }

        public void EraseSession(string sessionId)
        {
            var session = _db.Sessions.FindById(sessionId);
            if (session == null) return;

            _db.Sessions.Delete(sessionId);
            _db.Messages.Delete(x => x.SessionId == sessionId);
            _db.ShareTokens.Delete(x => x.SessionId == sessionId);
            
            _db.Connections.Delete(x => x.SessionId == sessionId);

            try
            {
                DeleteUploadedFiles(sessionId);
            }
            catch
            {
                BackgroundJob.Schedule(() => DeleteUploadedFiles(sessionId), TimeSpan.FromMinutes(30));
            }

            BackgroundJob.Delete(session.CleanupJobId);
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