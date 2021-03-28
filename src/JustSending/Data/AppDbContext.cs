using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JustSending.Data.Models;
using JustSending.Data.Models.Bson;
using JustSending.Services;
using LiteDB;
using Microsoft.AspNetCore.Hosting;

namespace JustSending.Data
{
    public class AppDbContext
    {
        private readonly IWebHostEnvironment _env;
        private readonly IDataStore _dataStore;
        private LiteDatabase? _db;
        private readonly Random _random;

        public AppDbContext(IWebHostEnvironment env, IDataStore dataStore)
        {
            _env = env;
            _dataStore = dataStore;
            _random = new Random(DateTime.UtcNow.Millisecond);
        }

        private LiteDatabase Database
            => _db ??= new LiteDatabase(Helper.BuildDbConnectionString("AppDb", _env));

        public ILiteCollection<Stats> Statistics => Database.GetCollection<Stats>();
        
        #region Core Methods

        public Task<T?> Get<T>(string id) => _dataStore.Get<T>(id);

        public Task Set<T>(string id, T model) => _dataStore.Set(id, model);

        public Task Remove<T>(string id) => _dataStore.Remove<T>(id);

        private static string GetKey<T>(string id) => $"{typeof(T).Name.ToLower()}-{id}";

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
        
        public async Task SetCount<TModel>(string id, int? count)
        {
            var key = $"{typeof(TModel).Name.ToLower()}-count-{id}";
            if (count == null)
                await Remove<int>(key);
            else
                await Set(key, count.Value);
        }

        public async Task<Session?> GetSession(string id, string id2)
        {
            // ToDo: lock
            var session = await Get<Session>(id);
            return session?.IdVerification == id2
                ? session
                : null;
        }

        private async Task<int> NewConnectionId()
        {
            //ToDo: Lock
            //lock (this)
            {
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
        }

        public async Task MessagesInsert(Message msg)
        {
            var count = await Count<Message>(msg.SessionId);
            msg.SessionMessageSequence = ++count;

            await Set(msg.Id, msg);
            // messageId by sequence number
            await Set($"{msg.SessionId}-{count}", msg.Id);
            await SetCount<Message>(msg.SessionId, msg.SessionMessageSequence);
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

        public void RecordMessageStats(Message msg)
        {
            RecordStats(s =>
            {
                s.Messages++;
                s.MessagesSizeBytes += msg.Text?.Length ?? 0;

                if (msg.HasFile)
                {
                    s.Files++;
                    s.FilesSizeBytes += msg.FileSizeBytes;
                }
            });
        }
        public void RecordStats(Action<Stats> update)
        {
            lock (this)
            {
                var utcNow = DateTime.UtcNow;

                // All time stats
                var allTime = StatsFindByIdOrNew(null);
                update(allTime);

                // This year
                var thisYear = StatsFindByIdOrNew(utcNow.Year);
                update(thisYear);

                // This month
                var thisMonth = StatsFindByIdOrNew(utcNow.Year, utcNow.Month);
                update(thisMonth);

                // Today
                var today = StatsFindByIdOrNew(utcNow.Year, utcNow.Month, utcNow.Day);
                update(today);

                Statistics.Upsert(new[]
                {
                    allTime,
                    thisYear,
                    thisMonth,
                    today
                });
            }
        }

        private Stats StatsFindByIdOrNew(int? year, int? month = null, int? day = null)
        {
            var id = StatsGetIdFor(year, month, day);
            var items = Statistics.FindById(id);
            return items ?? new Stats { Id = id, DateCreatedUtc = DateTime.UtcNow };
        }

        private static int StatsGetIdFor(int? year, int? month = null, int? day = null)
        {
            if (!year.HasValue)
                return -1;

            return Convert.ToInt32(year.Value.ToString().Substring(2) + (month ?? 0).ToString("00") + (day ?? 0).ToString("00"));
        }
    }
}
