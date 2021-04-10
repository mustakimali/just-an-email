using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JustSending.Data;
using JustSending.Data.Models;
using JustSending.Models;
using Microsoft.AspNetCore.Mvc;

namespace JustSending.Controllers
{
    public partial class AppController : Controller
    {
        private async Task<int> CreateSession(string id, string id2, bool liteSession = false)
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
            await _db.Set(sessionId, session);

            await ScheduleOrExtendSessionCleanup(sessionId, liteSession);

            // New ShareToken
            return await CreateShareToken(sessionId);
        }

        private async Task<int> CreateShareToken(string sessionId)
        {
            var token = await _db.CreateNewShareToken(sessionId);
            _statDb.RecordStats(StatsDbContext.RecordType.Session);
            return token;
        }

        private async Task<Message[]> GetMessagesInternal(string id, string id2, int from)
        {
            if (!await IsValidSession(id, id2))
            {
                return Array.Empty<Message>();
            }

            var expiredMessage = new Message {IsNotification = true, Text = "Message Expired"};
            var result = new List<Message>();
            var count = await _db.Count<Message>(id);
            for (int i = from + 1; i <= count; i++)
            {
                var messageId = await _db.Get<string>($"{id}-{i}");
                if (messageId == null)
                {
                    result.Add(expiredMessage);
                    continue;
                }

                var message = await _db.Get<Message>(messageId);
                result.Add(message ?? expiredMessage);
            }

            result.Reverse();
            
            return result.ToArray();
        }

        private Task<bool> IsValidRequest(SessionModel model) => IsValidSession(model.SessionId, model.SessionVerification);

        private async Task<bool> IsValidSession(string id, string id2)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(id2))
                return false;

            var session = await _db.Get<Session>(id);
            if (session == null || session.IdVerification != id2)
            {
                return false;
            }

            return true;
        }
    }

}