using System;

namespace JustSending.Data.Models
{
    public class Message
    {
        public string Id { get; set; }

        public string SessionId { get; set; }

        public string? SessionIdVerification { get; set; }
        public int SessionMessageSequence { get; set; }
        /// <summary>
        /// Unique for each connected device in a particular session
        /// </summary>
        public string? SocketConnectionId { get; set; }

        public string? EncryptionPublicKeyAlias { get; set; }
        public string Text { get; set; }

        public DateTime DateSent { get; set; }
        public bool HasFile { get; set; }
        public long? FileSizeBytes { get; set; }

        /// Indicate if this is system generated Message
        /// Not a user sent message
        public bool IsNotification { get; set; }
        public int DateSentEpoch { get; set; }
    }
}
