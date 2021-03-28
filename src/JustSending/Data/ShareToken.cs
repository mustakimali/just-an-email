namespace JustSending.Data
{
    public class ShareToken
    {
        public int Id { get; set; }
        public string SessionId { get; set; }
    }

    public record SessionShareToken(int Token);
}
