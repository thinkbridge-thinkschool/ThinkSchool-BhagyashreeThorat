namespace QuotesApi.Infrastructure.Messaging;

public sealed class QuoteCreatedEvent
{
    public Guid MessageId { get; set; }

    public Guid QuoteId { get; set; }

    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}