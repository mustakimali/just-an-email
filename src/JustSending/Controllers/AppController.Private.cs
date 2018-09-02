using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using JustSending.Data;
using JustSending.Models;
using Microsoft.AspNetCore.Mvc;

namespace JustSending.Controllers
{
    public partial class AppController : Controller
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
            if (!IsValidSession(id, id2))
            {
                return Enumerable.Empty<Message>();
            }

            return _db
                .Messages
                .Find(x => x.SessionId == id && x.SessionMessageSequence > @from)
                .OrderByDescending(x => x.DateSent);
        }

        private bool IsValidRequest(SessionModel model) => IsValidSession(model.SessionId, model.SessionVerification);

        private bool IsValidSession(string id, string id2)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(id2))
                return false;

            var session = _db.Sessions.FindById(id);
            if (session == null || session.IdVerification != id2)
            {
                return false;
            }

            return true;
        }

        private void Try(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Trace.TraceError($"[SignalR] {exception.GetBaseException().Message}");
            }
        }
    }

}