namespace JustSending.Data.Models
{
    public class ShareToken
    {
        public int Id { get; set; }
        public string SessionId { get; set; }
    }

    public record SessionShareToken(int Token);
}
