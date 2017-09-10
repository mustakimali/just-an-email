using System.ComponentModel.DataAnnotations;

namespace JustSending.Models
{
    public class SessionModel
    {
        /// <summary>
        /// Primary unique id of the session
        /// </summary>
        public string SessionId { get; set; }
        /// <summary>
        /// Secondery unique id of the session
        /// </summary>
        public string SessionVarification { get; set; }
        /// <summary>
        /// The connection id used to map socket connection with a session
        /// </summary>
        public string SocketConnectionId { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "Nothing to send!")]
        public string ComposerText { get; set; }
    }

    public class ConnectViewModel
    {
        public int Token { get; set; }
    }
}
