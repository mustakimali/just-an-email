using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using JustSending.Data;

namespace JustSending.Models
{
    public class LiteSessionModel : SessionModel
    {
        public int? Token { get; set; }
        public ICollection<Message> Messages { get; set; }
    }
}
