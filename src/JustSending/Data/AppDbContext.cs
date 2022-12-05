using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JustSending.Data.Models;
using OpenTelemetry.Trace;

namespace JustSending.Data
{
    public class AppDbContext
    {
        private static readonly TimeSpan MessageTtl = TimeSpan.FromMinutes(15);

        private readonly IDataStore _dataStore;
        private readonly ILock _lock;
        private readonly Random _random;
        private readonly Tracer _tracer;

        public AppDbContext(IDataStore dataStore, ILock @lock, Tracer tracer)
        {
            _tracer = tracer;
            _dataStore = dataStore;
            _lock = @lock;
            _random = new Random(DateTime.UtcNow.Millisecond);
        }

        #region Core Methods

        public async Task<T?> Get<T>(string id)
        {
            using var span = _tracer.StartActiveSpan("get");
            span.SetAttribute("key", id);

            return await _dataStore.Get<T>(id);
        }

        public Task<T?> GetInternal<T>(string id) => _dataStore.Get<T>(id);

        public async Task Set<T>(string id, T model, TimeSpan? ttl = null)
        {
            using var span = _tracer.StartActiveSpan("set");
            span.SetAttribute("key", id);

            await _dataStore.Set(id, model, ttl ?? TimeSpan.FromHours(6));
        }


        public async Task Remove<T>(string id)
        {
            using var span = _tracer.StartActiveSpan("remove");
            span.SetAttribute("key", id);

            await _dataStore.Remove<T>(id);
        }

        #endregion

        public static string NewGuid() => Guid.NewGuid().ToString("N");

        public async Task<bool> Exist<TModel>(string id)
        {
            var data = await _dataStore.Get<TModel>(id);
            return data != null;
        }

        public async Task<int> Count<TModel>(string id)
        {
            var key = $"{typeof(TModel).Name.ToLower()}-count-{id}";
            return await Get<int>(key);
        }

        public async Task SetCount<TModel>(string id, int? count, TimeSpan? ttl = null)
        {
            var key = $"{typeof(TModel).Name.ToLower()}-count-{id}";
            if (count == null)
                await Remove<int>(key);
            else
                await Set(key, count.Value, ttl);
        }

        public async Task<Session?> GetSession(string id, string id2)
        {
            var session = await GetSession(id);
            return session?.IdVerification == id2
                ? session
                : null;
        }

        public Task<Session?> GetSession(string id)
        {
            return Get<Session>(id);
        }

        private async Task<int> NewConnectionId()
        {
            using var span = _tracer.StartActiveSpan("new-connection-id");

            using var _ = await _lock.Acquire("new-connection-id");

            var min = 100000;
            var max = 1000000;
            var tries = 0;

            var number = _random.Next(min, max - 1);

            while (await Exist<ShareToken>(number.ToString()))
            {
                if (tries > (max - min) / 2)
                {
                    tries = 0;

                    min *= 10;
                    max *= 10;
                }

                tries++;
                number = _random.Next(min, max - 1);
            }

            return number;
        }

        public async Task MessagesInsert(Message msg)
        {
            using var span = _tracer.StartActiveSpan("insert-message");
            span.SetAttribute("session-id", msg.SessionId);
            span.SetAttribute("session-id-2", msg.SessionIdVerification);

            using var _ = await _lock.Acquire(msg.SessionId);

            var count = await Count<Message>(msg.SessionId);
            msg.SessionMessageSequence = ++count;

            await Set(msg.Id, msg, MessageTtl);
            // messageId by sequence number
            await Set($"{msg.SessionId}-{count}", msg.Id, MessageTtl);
            await SetCount<Message>(msg.SessionId, msg.SessionMessageSequence, MessageTtl);
        }

        public async Task TrackClient(string sessionId, string connectionId)
        {
            using var span = _tracer.StartActiveSpan("track-client");
            span.SetAttribute("session-id", sessionId);
            span.SetAttribute("connection-id", connectionId);

            // ToDo: lock
            var session = await Get<Session>(sessionId);
            if (session == null) return;

            if (session.ConnectionIds.Contains(connectionId))
                return;

            session.ConnectionIds.Add(connectionId);
            await Set(sessionId, session);
            await Set(connectionId, new SessionMetaByConnectionId(sessionId));
        }

        public async Task<string?> UntrackClientReturnSessionId(string connectionId)
        {
            using var span = _tracer.StartActiveSpan("untrack-client");
            span.SetAttribute("connection-id", connectionId);

            var connectionIdSession = await Get<SessionMetaByConnectionId>(connectionId);
            if (connectionIdSession == null) return null;
            var session = await Get<Session>(connectionIdSession.SessionId);
            if (session == null) return null;
            span.SetAttribute("session-id", session.Id);

            session.ConnectionIds.Remove(connectionId);
            await Set(session.Id, session);
            await Remove<SessionMetaByConnectionId>(connectionId);

            return connectionIdSession.SessionId;
        }

        public async Task<IEnumerable<string>> FindClient(string sessionId)
        {
            using var span = _tracer.StartActiveSpan("find-client");
            span.SetAttribute("session-id", sessionId);

            var session = await Get<Session>(sessionId);
            if (session == null) return Enumerable.Empty<string>();

            return session.ConnectionIds;
        }

        public async Task<int> CreateNewShareToken(string sessionId)
        {
            using var span = _tracer.StartActiveSpan("new-share-token");
            span.SetAttribute("session-id", sessionId);

            var shareToken = new ShareToken
            {
                Id = await NewConnectionId(),
                SessionId = sessionId
            };
            await Set(shareToken.Id.ToString(), shareToken);
            await Set(sessionId, new SessionShareToken(shareToken.Id));
            return shareToken.Id;
        }
    }
}
