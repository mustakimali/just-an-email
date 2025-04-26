using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using JustSending.Data.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
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
        private readonly SqliteConnection _connection;

        public AppDbContext(IDataStore dataStore, ILock @lock, Tracer tracer, IConfiguration config)
        {
            _tracer = tracer;
            _dataStore = dataStore;
            _lock = @lock;
            _random = new Random(DateTime.UtcNow.Millisecond);
            _connection = new SqliteConnection(config.GetConnectionString("StatsCs"));
        }

        #region Core Methods

        public async Task<T?> GetKv<T>(string id)
        {
            using var span = _tracer.StartActiveSpan("get");
            span.SetAttribute("key", id);

            var e = await _connection.QueryFirstOrDefaultAsync<KvEntity>(
                "SELECT * FROM Kv WHERE Id = @Id",
                new { Id = id });
            if (e == null) return default;

            var data = JsonSerializer.Deserialize<T>(e.DataJson);
            if (data == null) return default;

            return data;
        }

        public Task<T?> GetKvInternal<T>(string id) => _dataStore.Get<T>(id);

        public async Task SetKv<T>(string id, T model, TimeSpan? ttl = null)
        {
            using var span = _tracer.StartActiveSpan("set");
            span.SetAttribute("key", id);

            var json = JsonSerializer.Serialize(model);
            var entity = new KvEntity
            {
                Id = id,
                DataJson = json
            };
            var changed = await _connection.ExecuteAsync(
                "INSERT OR REPLACE INTO Kv (Id, DataJson) VALUES (@Id, @DataJson)",
                entity);
        }


        public async Task Remove<T>(string id)
        {
            using var span = _tracer.StartActiveSpan("remove");
            span.SetAttribute("key", id);

            await _connection.ExecuteAsync(
                "DELETE FROM Kv WHERE Id = @Id",
                new { Id = id });
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
            return await GetKv<int>(key);
        }

        public async Task SetCount<TModel>(string id, int? count, TimeSpan? ttl = null)
        {
            var key = $"{typeof(TModel).Name.ToLower()}-count-{id}";
            if (count == null)
                await Remove<int>(key);
            else
                await SetKv(key, count.Value, ttl);
        }

        public async Task<Session?> GetSession(string id, string id2)
        {
            var session = await GetSessionById(id);
            if (session == null) return null;

            return session?.IdVerification == id2
                ? session
                : null;
        }

        public async Task<Session?> GetSessionById(string id)
        {
            var entity = await _connection.QueryFirstOrDefaultAsync<SessionEntity>(
                "SELECT * FROM Sessions WHERE Id = @Id",
                new { Id = id });
            if (entity == null) return null;
            var session = Session.FromEntity(entity);

            return session;
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

            await SetKv(msg.Id, msg, MessageTtl);
            // messageId by sequence number
            await SetKv($"{msg.SessionId}-{count}", msg.Id, MessageTtl);
            await SetCount<Message>(msg.SessionId, msg.SessionMessageSequence, MessageTtl);
        }

        public async Task TrackClient(string sessionId, string connectionId)
        {
            using var span = _tracer.StartActiveSpan("track-client");
            span.SetAttribute("session-id", sessionId);
            span.SetAttribute("connection-id", connectionId);

            // ToDo: lock
            if (!await AddConnectionId(sessionId, connectionId)) return;
            await SetKv(connectionId, new SessionMetaByConnectionId(sessionId));
        }

        public async Task<bool> AddConnectionId(string sessionId, string connectionId)
        {
            using var span = _tracer.StartActiveSpan("add-connection-id");
            span.SetAttribute("session-id", sessionId);
            span.SetAttribute("connection-id", connectionId);

            var session = await GetSessionById(sessionId);
            if (session == null) return false;

            session.ConnectionIds.Add(connectionId);
            return await AddOrUpdateSession(session);
        }

        public async Task<bool> AddOrUpdateSession(Session session)
        {
            using var span = _tracer.StartActiveSpan("add-or-update-session");
            span.SetAttribute("session-id", session.Id);
            span.SetAttribute("id-verification", session.IdVerification);

            var changed = await _connection.ExecuteAsync(
                "INSERT OR REPLACE INTO Sessions (Id, IdVerification, DateCreated, IsLiteSession, CleanupJobId, ConnectionIdsJson) " +
                "VALUES (@Id, @IdVerification, @DateCreated, @IsLiteSession, @CleanupJobId, @ConnectionIdsJson)",
                new
                {
                    session.Id,
                    session.IdVerification,
                    session.DateCreated,
                    session.IsLiteSession,
                    session.CleanupJobId,
                    ConnectionIdsJson = JsonSerializer.Serialize(session.ConnectionIds)
                });
            return changed > 0;
        }


        public async Task<string?> UntrackClientReturnSessionId(string connectionId)
        {
            using var span = _tracer.StartActiveSpan("untrack-client");
            span.SetAttribute("connection-id", connectionId);

            var connectionIdSession = await GetKv<SessionMetaByConnectionId>(connectionId);
            if (connectionIdSession == null) return null;
            var session = await GetSessionById(connectionIdSession.SessionId);
            if (session == null) return null;
            span.SetAttribute("session-id", session.Id);

            session.ConnectionIds.Remove(connectionId);
            await AddOrUpdateSession(session);
            await Remove<SessionMetaByConnectionId>(connectionId);

            return connectionIdSession.SessionId;
        }

        public async Task<IEnumerable<string>> FindClient(string sessionId)
        {
            using var span = _tracer.StartActiveSpan("find-client");
            span.SetAttribute("session-id", sessionId);

            var session = await GetSessionById(sessionId);
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
            await SetKv(shareToken.Id.ToString(), shareToken);
            await SetKv(sessionId, new SessionShareToken(shareToken.Id));
            return shareToken.Id;
        }
    }
}
