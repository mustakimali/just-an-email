using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JustSending.Data;
using JustSending.Models;
using JustSending.Services;
using JustSending.Services.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using IOFile = System.IO.File;

namespace JustSending.Controllers
{
    public partial  class AppController : Controller
    {
        private int CreateSession(string id, string id2, bool liteSession = false)
        {
            var sessionId = id;
            var idVerification = id2;

            // Create Session
            var session = new Session
            {
                DateCreated = DateTime.UtcNow,
                Id = sessionId,
                IdVerification = idVerification,
                IsLiteSession = liteSession
            };
            _db.Sessions.Insert(session);

            ScheduleOrExtendSessionCleanup(sessionId, liteSession);

            // New ShareToken
            return CreateShareToken(sessionId);
        }

        private int CreateShareToken(string sessionId)
        {
            var token = _db.CreateNewShareToken(sessionId);
            _db.RecordStats(s => s.Sessions++);
            return token;
        }

        private IEnumerable<Message> GetMessagesInternal(string id, string id2, int @from)
        {
            if(string.IsNullOrEmpty(id)) return Enumerable.Empty<Message>();
            
            var session = _db.Sessions.FindById(id);
            if (session == null || session.IdVerification != id2)
            {
                return Enumerable.Empty<Message>();
            }

            var messages = _db
                .Messages
                .Find(x => x.SessionId == id && x.SessionMessageSequence > @from)
                .OrderByDescending(x => x.DateSent);
            return messages;
        }
    }
    
}