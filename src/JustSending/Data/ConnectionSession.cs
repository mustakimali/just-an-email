using LiteDB;

namespace JustSending.Data
{
    public class ConnectionSession
    {
        [BsonId]
        public string ConnectionId { get; set; }
        [BsonIndex]
        public string SessionId { get; set; }
    }
}
