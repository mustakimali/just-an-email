using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;

namespace JustSending.Data.Models
{
    public class Session
    {
        public string Id { get; set; }
        public string IdVerification { get; set; }

        public DateTime DateCreated { get; set; }
        public bool IsLiteSession { get; set; }
        public string? CleanupJobId { get; set; }

        public HashSet<string> ConnectionIds { get; set; } = new();

        public static Session FromEntity(SessionEntity entity)
        {
            return new Session
            {
                Id = entity.Id,
                IdVerification = entity.IdVerification,
                DateCreated = entity.DateCreated,
                IsLiteSession = entity.IsLiteSession,
                CleanupJobId = entity.CleanupJobId,
                ConnectionIds = JsonSerializer.Deserialize<HashSet<string>>(entity.ConnectionIdsJson ?? string.Empty) ?? new HashSet<string>()
            };
        }
    }


    public record SessionMetaByConnectionId(string SessionId);

    public class SessionEntity
    {
        public string Id { get; set; }
        public string IdVerification { get; set; }

        public DateTime DateCreated { get; set; }
        public bool IsLiteSession { get; set; }
        public string? CleanupJobId { get; set; }

        public string? ConnectionIdsJson { get; set; }
    }

    public class KvEntity
    {
        public string Id { get; set; }
        public string DataJson { get; set; }
        public DateTime DateCreated { get; set; }
    }
}