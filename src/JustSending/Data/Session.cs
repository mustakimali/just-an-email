using System;
using LiteDB;

namespace JustSending.Data
{
    public class Session
    {
        [BsonId]
        public string Id { get; set; }
        public string IdVerification { get; set; }

        public DateTime DateCreated { get; set; }
        public bool IsLiteSession { get; set; }
        public string CleanupJobId { get; set; }
    }
}
