using LiteDB;

namespace JustSending.Data
{
    public class ConnectionSession
    {
        [BsonId]
        public string ConnectionId { get; set; }
        public string SessionId { get; set; }
    }
}
