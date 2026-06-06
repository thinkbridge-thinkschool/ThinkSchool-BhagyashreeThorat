namespace Quotes.Repository.Entities;

public class Collection
{
    private const int MinNameLength = 3;
    private const int MaxNameLength = 80;
    private const int MaxItems = 50;

    private readonly List<CollectionItem> _items = [];

    // EF Core needs a parameterless constructor
    private Collection() { }

    public Collection(string name, string ownerId)
    {
        SetName(name);
        OwnerId = ownerId;
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string OwnerId { get; private set; } = string.Empty;

    public IReadOnlyList<CollectionItem> Items => _items.AsReadOnly();

    public void Rename(string newName) => SetName(newName);

    public void AddItem(int quoteId, DateTimeOffset addedAt)
    {
        if (_items.Count >= MaxItems)
            throw new DomainException(
                $"A collection cannot have more than {MaxItems} items.");

        if (_items.Any(i => i.QuoteId == quoteId))
            throw new DomainException(
                $"Quote {quoteId} is already in this collection.");

        _items.Add(new CollectionItem(quoteId, addedAt.UtcDateTime));
    }

    public void RemoveItem(int quoteId)
    {
        var item = _items.FirstOrDefault(i => i.QuoteId == quoteId);

        if (item is null)
            throw new DomainException(
                $"Quote {quoteId} is not in this collection.");

        _items.Remove(item);
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Collection name cannot be empty.");

        if (name.Length < MinNameLength || name.Length > MaxNameLength)
            throw new DomainException(
                $"Collection name must be between {MinNameLength} and {MaxNameLength} characters.");

        Name = name;
    }
}
