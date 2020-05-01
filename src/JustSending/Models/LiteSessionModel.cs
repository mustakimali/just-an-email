using System.Collections.Generic;
using JustSending.Data;

namespace JustSending.Models
{
    public class LiteSessionModel : SessionModel
    {
        public int? Token { get; set; }
        public ICollection<Message> Messages { get; set; }
    }

    public class LiteSessionStatus
    {
        public string MessageHtml { get; set; }
        public bool HasToken { get; set; }
        public string Token { get; set; }
        public bool HasSession { get; set; }
    }

    public static class Constants
    {
        public const string TOKEN_FORMAT_STRING = "### ###";
    }
}
