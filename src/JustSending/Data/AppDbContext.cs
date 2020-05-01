using System;
using System.Collections.Generic;
using System.Linq;
using JustSending.Services;
using LiteDB;
using Microsoft.AspNetCore.Hosting;

namespace JustSending.Data
{
    public class AppDbContext
    {
        private readonly IWebHostEnvironment _env;
        private LiteDatabase _db;
        private readonly Random _random;

        public AppDbContext(IWebHostEnvironment env)
        {
            _env = env;
            _random = new Random();
        }

        public LiteDatabase Database
        {
            get
            {
                if (_db == null)
                {
                    _db = new LiteDatabase(Helper.BuildDbConnectionString("AppDb", _env));

                    EnsureIndex();
                }

                return _db;
            }
        }

        private void EnsureIndex()
        {
            Connections.EnsureIndex(x => x.SessionId);

            Messages.EnsureIndex(x => x.SessionMessageSequence);
            Messages.EnsureIndex(x => x.SessionId);

            Sessions.EnsureIndex(x => x.IdVerification);

            ShareTokens.EnsureIndex(x => x.SessionId);
        }

        public ILiteCollection<Session> Sessions => Database.GetCollection<Session>();
        public ILiteCollection<ShareToken> ShareTokens => Database.GetCollection<ShareToken>();
        public ILiteCollection<Message> Messages => Database.GetCollection<Message>();
        public ILiteCollection<ConnectionSession> Connections => Database.GetCollection<ConnectionSession>();
        public ILiteCollection<Stats> Statistics => Database.GetCollection<Stats>();
        
        public string NewGuid() => Guid.NewGuid().ToString("N");

        public int NewConnectionId()
        {
            lock (this)
            {
                var min = 100000;
                var max = 1000000;
                var tries = 0;

                var number = _random.Next(min, max - 1);

                while (ShareTokens.Exists(x => x.Id == number))
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

        public void MessagesInsert(Message msg)
        {
            var count = Messages.Count(x => x.SessionId == msg.SessionId);
            msg.SessionMessageSequence = ++count;

            Messages.Insert(msg);
        }

        public void TrackClient(string sessionId, string connectionId)
        {
            var session = Sessions.FindById(sessionId);
            if (session == null) return;

            if(Connections.Exists(x=> x.ConnectionId == connectionId)) 
                return;

            Connections.Insert(new ConnectionSession
            {
                ConnectionId = connectionId,
                SessionId = sessionId
            });
        }

        public string UntrackClientReturnSessionId(string connectionId)
        {
            var item = Connections.FindById(connectionId);
            if (item == null) return null;

            Connections.Delete(connectionId);
            return item.SessionId;
        }

        public IEnumerable<string> FindClient(string sessionId)
        {
            return Connections
                .Find(x => x.SessionId == sessionId)
                .Select(x => x.ConnectionId);
        }

        public int CreateNewShareToken(string sessionId)
        {
            var shareToken = new ShareToken
            {
                Id = NewConnectionId(),
                SessionId = sessionId
            };
            ShareTokens.Insert(shareToken);
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

        private int StatsGetIdFor(int? year, int? month = null, int? day = null)
        {
            if (!year.HasValue)
                return -1;

            return Convert.ToInt32(year.Value.ToString().Substring(2) + (month ?? 0).ToString("00") + (day ?? 0).ToString("00"));
        }
    }
}
