namespace QuotesApi.Entities;

/// <summary>
/// Author aggregate. One author has many quotes — this one-to-many relationship
/// is what the deliberately-slow /api/authors/summary endpoint walks N+1 over.
/// </summary>
public class Author
{
    private readonly List<Quote> _quotes = [];

    private Author() { }

    private Author(string name) => Name = name;

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    // Navigation back to the author's quotes. EF populates this via the _quotes
    // backing field. The slow endpoint loads authors WITHOUT this collection and
    // then lazily fires one query per author to fill it — the classic N+1.
    public IReadOnlyList<Quote> Quotes => _quotes.AsReadOnly();

    public static Result<Author> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Result<Author>.Fail("Author name must be between 1 and 200 characters.");

        return Result<Author>.Ok(new Author(name));
    }
}
