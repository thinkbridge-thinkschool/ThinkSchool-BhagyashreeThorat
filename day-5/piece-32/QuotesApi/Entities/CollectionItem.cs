namespace QuotesApi.Entities;

/// <summary>
/// Immutable value object representing a quote inside a collection.
/// Identity is determined by QuoteId — no surrogate key.
/// </summary>
public sealed class CollectionItem
{
    // EF Core needs a parameterless constructor for owned types
    private CollectionItem() { }

    public CollectionItem(int quoteId, DateTime addedAt)
    {
        QuoteId = quoteId;
        AddedAt = addedAt;
    }

    public int QuoteId { get; private set; }

    public DateTime AddedAt { get; private set; }

    // Value equality: two items with the same QuoteId are the same item
    public override bool Equals(object? obj) =>
        obj is CollectionItem other && QuoteId == other.QuoteId;

    public override int GetHashCode() => QuoteId.GetHashCode();
}