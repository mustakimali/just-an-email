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

        #region KeyValue Store

        public async Task<T?> KvGet<T>(string id)
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

        public async Task KvSet<T>(string id, T model, TimeSpan? ttl = null)
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

        public async Task KvRemove<T>(string id)
        {
            using var span = _tracer.StartActiveSpan("remove");
            span.SetAttribute("key", id);

            await _connection.ExecuteAsync(
                "DELETE FROM Kv WHERE Id = @Id",
                new { Id = id });
        }

        public async Task<bool> KvExists<TModel>(string id)
        {
            var count = await _connection.QueryFirstAsync<int>(
                "SELECT COUNT(*) FROM Kv WHERE Id = @Id",
                new { Id = id });
            return count > 0;
        }

        #endregion

        public static string NewGuid() => Guid.NewGuid().ToString("N");

        public async Task<Message[]?> GetMessagesBySession(string sessionId, int fromEpoch = -1)
        {
            var result = await _connection.QueryMultipleAsync(
                "SELECT * FROM Messages WHERE SessionId = @SessionId AND DateSentEpoch > @Seq ORDER BY DateSent DESC",
                new { SessionId = sessionId, Seq = fromEpoch });
            if (result == null) return null;

            var messages = await result.ReadAsync<Message>();
            if (messages == null) return null;

            return [.. messages];
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

            while (await KvExists<ShareToken>(number.ToString()))
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

            var parameters = new
            {
                msg.Id,
                msg.SessionId,
                msg.SessionIdVerification,
                msg.SocketConnectionId,
                msg.EncryptionPublicKeyAlias,
                msg.Text,
                msg.DateSent,
                msg.HasFile,
                msg.FileSizeBytes,
                msg.IsNotification,
                msg.DateSentEpoch
            };

            await _connection.ExecuteAsync(
                @"INSERT INTO Messages
                    (Id, SessionId, SessionIdVerification,
                        SocketConnectionId, EncryptionPublicKeyAlias, Text, DateSent, HasFile, FileSizeBytes, IsNotification, DateSentEpoch)
                VALUES ( @Id, @SessionId, @SessionIdVerification,
                        @SocketConnectionId, @EncryptionPublicKeyAlias, @Text, @DateSent, @HasFile, @FileSizeBytes, @IsNotification, @DateSentEpoch)",
                parameters);
        }

        public async Task TrackClient(string sessionId, string connectionId)
        {
            using var span = _tracer.StartActiveSpan("track-client");
            span.SetAttribute("session-id", sessionId);
            span.SetAttribute("connection-id", connectionId);

            // ToDo: lock
            if (!await AddConnectionId(sessionId, connectionId)) return;
            await KvSet(connectionId, new SessionMetaByConnectionId(sessionId));
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

            var connectionIdSession = await KvGet<SessionMetaByConnectionId>(connectionId);
            if (connectionIdSession == null) return null;
            var session = await GetSessionById(connectionIdSession.SessionId);
            if (session == null) return null;
            span.SetAttribute("session-id", session.Id);

            session.ConnectionIds.Remove(connectionId);
            await AddOrUpdateSession(session);
            await KvRemove<SessionMetaByConnectionId>(connectionId);

            return connectionIdSession.SessionId;
        }

        public async Task<IEnumerable<string>> FindClient(string sessionId)
        {
            using var span = _tracer.StartActiveSpan("find-client");
            span.SetAttribute("session-id", sessionId);

            var session = await GetSessionById(sessionId);
            if (session == null) return [];

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
            await KvSet(shareToken.Id.ToString(), shareToken);
            await KvSet(sessionId, new SessionShareToken(shareToken.Id));
            return shareToken.Id;
        }
    }
}
