namespace Quotes.Repository.Entities;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }
}
