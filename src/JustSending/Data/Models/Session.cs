using System;
using System.Collections.Generic;

namespace JustSending.Data.Models
{
    public class Session
    {
        public string Id { get; set; }
        public string IdVerification { get; set; }

        public DateTime DateCreated { get; set; }
        public bool IsLiteSession { get; set; }
        public string CleanupJobId { get; set; }

        public HashSet<string> ConnectionIds { get; set; } = new();
    }

    public record SessionMetaByConnectionId(string SessionId);
}
