using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using Microsoft.AspNetCore.Hosting;

namespace JustSending.Data
{
    public class AppDbContext
    {
        private readonly IHostingEnvironment _env;
        private LiteDatabase _db;
        private readonly Random _random;

        public AppDbContext(IHostingEnvironment env)
        {
            _env = env;
            _random = new Random();
        }

        public LiteDatabase Database {
            get
            {
                if(_db == null) 
                {
                    var dataDirectory = Path.Combine(_env.ContentRootPath, "App_Data");
                    if(!Directory.Exists(dataDirectory)) Directory.CreateDirectory(dataDirectory);

                    _db = new LiteDatabase(Path.Combine(dataDirectory, "AppDb.ldb"));
                }

                return _db;
            }
        }

        public LiteCollection<Session> Sessions => Database.GetCollection<Session>();
        public LiteCollection<ShareToken> ShareTokens => Database.GetCollection<ShareToken>();
        public LiteCollection<Message> Messages => Database.GetCollection<Message>();
        public LiteCollection<ConnectionSession> Connections => Database.GetCollection<ConnectionSession>();

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

        public void TrackClient(string sessionId, string connectionId)
        {
            var session = Sessions.FindById(sessionId);
            if (session == null) return;

            Connections.Insert(new ConnectionSession
            {
                ConnectionId = connectionId,
                SessionId = sessionId
            });
        }

        public string UntrackClientReturnSessionId(string connectionId)
        {
            var item = Connections.FindById(connectionId);
            if(item == null) return null;

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
    }
}
