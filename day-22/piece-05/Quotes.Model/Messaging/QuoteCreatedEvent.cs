namespace Quotes.Model.Messaging;

public sealed class QuoteCreatedEvent
{
    public Guid MessageId { get; set; }

    public int QuoteId { get; set; }

    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
