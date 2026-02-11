using System;

namespace JustSending.Data.Models
{
    public class PublicKey
    {
        public string Id { get; set; }
        public string SessionId { get; set; }
        public string Alias { get; set; }
        public string PublicKeyJson { get; set; }
        public DateTime DateCreated { get; set; }
    }
}
