using System.ComponentModel.DataAnnotations;

namespace JustSending.Models
{
    public class SessionModel
    {
        public string SessionId { get; set; }
        public string SessionVarification { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "Nothing to send!")]
        public string ComposerText { get; set; }
    }

    public class ConnectViewModel
    {
        public int Token { get; set; }
    }
}
