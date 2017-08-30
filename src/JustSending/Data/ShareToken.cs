using LiteDB;

namespace JustSending.Data
{
    public class ShareToken
    {
        [BsonId]
        public int Id { get; set; }
        [BsonIndex]
        public string SessionId { get; set; }
    }
}
