namespace Chat.Api.Entities;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string From { get; set; }
    public required string To { get; set; }
    public required string Body { get; set; }
    public required DateTime SentAt { get; set; }
}
