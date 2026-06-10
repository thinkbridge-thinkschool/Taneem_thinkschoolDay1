namespace QuotesApi.Models;

public class OutboxMessage
{
    public int      Id        { get; set; }
    public string   EventType { get; set; } = string.Empty;
    public string   Payload   { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt   { get; set; }
}
