using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JustSending.Data.Models;

namespace JustSending.Data
{
    public class AppDbContext
    {
        private static readonly TimeSpan MessageTtl = TimeSpan.FromMinutes(15);

        private readonly IDataStore _dataStore;
        private readonly ILock _lock;
        private readonly Random _random;

        public AppDbContext(IDataStore dataStore, ILock @lock)
        {
            _dataStore = dataStore;
            _lock = @lock;
            _random = new Random(DateTime.UtcNow.Millisecond);
        }

        #region Core Methods

        public Task<T?> Get<T>(string id) => _dataStore.Get<T>(id);

        public Task Set<T>(string id, T model, TimeSpan? ttl = null) =>
            _dataStore.Set(id, model, ttl ?? TimeSpan.FromHours(6));

        public Task Remove<T>(string id) => _dataStore.Remove<T>(id);

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
            var session = await Get<Session>(id);
            return session?.IdVerification == id2
                ? session
                : null;
        }

        private async Task<int> NewConnectionId()
        {
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
            var connectionIdSession = await Get<SessionMetaByConnectionId>(connectionId);
            if (connectionIdSession == null) return null;
            var session = await Get<Session>(connectionIdSession.SessionId);
            if (session == null) return null;
            
            session.ConnectionIds.Remove(connectionId);
            await Set(session.Id, session);
            await Remove<SessionMetaByConnectionId>(connectionId);

            return connectionIdSession.SessionId;
        }

        public async Task<IEnumerable<string>> FindClient(string sessionId)
        {
            var session = await Get<Session>(sessionId);
            if (session == null) return Enumerable.Empty<string>();

            return session.ConnectionIds;
        }

        public async Task<int> CreateNewShareToken(string sessionId)
        {
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
