using System;
using LiteDB;

namespace JustSending.Data
{
    public class Stats
    {
        [BsonId]
        public int Id { get; set; }
        public int Messages { get; set; }
        public int MessagesSizeBytes { get; set; }
        public int Files { get; set; }
        public long FilesSizeBytes { get; set; }
        
        public int Devices { get; set; }
        public int Sessions { get; set; }

        public DateTime DateCreatedUtc { get; set; }

    }
}
