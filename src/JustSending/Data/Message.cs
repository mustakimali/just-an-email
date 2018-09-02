using System;
using LiteDB;

namespace JustSending.Data
{
    public class Message
    {
        [BsonId]
        public string Id { get; set; }
        public int SessionMessageSequence { get; set; }

        public string SessionId { get; set; }
        [BsonIgnore]
        public string SessionIdVerification { get; set; }
        /// <summary>
        /// Unique for each connected device in a particular session
        /// </summary>
        public string SocketConnectionId { get; internal set; }

        public string EncryptionPublicKeyAlias { get; set; }
        public string Text { get; set; }

        public DateTimeOffset DateSent { get; set; }
        public bool HasFile { get; internal set; }
        public long FileSizeBytes { get; internal set; }
        
        /// <summary>
        /// Indicate if this is system generated Message
        /// Not a user sent message
        /// </summary>
        /// <returns></returns>
        public bool IsNotification { get; internal set; }
        public int DateSentEpoch { get; internal set; }
    }
}
