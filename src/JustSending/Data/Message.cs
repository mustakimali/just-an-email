using System;
using LiteDB;

namespace JustSending.Data
{
    public class Message
    {
        [BsonId]
        public string Id { get; set; }
        [BsonIndex]
        public string SessionId { get; set; }

        public string Text { get; set; }
        public string TextMarkdownProcessed { get; set; }

        public DateTimeOffset DateSent { get; set; }
        public bool HasFile { get; internal set; }
        public long FileSizeBytes { get; internal set; }
    }
}
