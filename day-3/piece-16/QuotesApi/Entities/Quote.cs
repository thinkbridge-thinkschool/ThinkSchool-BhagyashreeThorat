namespace QuotesApi.Entities;

public class Quote
{
    // Private parameterless constructor for EF Core materialization
    private Quote() { }

    private Quote(string author, string text, int? ownerId)
    {
        Author = author;
        Text = text;
        OwnerId = ownerId;
    }

    public int Id { get; private set; }
    public string Author { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }
    /// <summary>Id of the user who created this quote. Null for quotes created before ownership tracking.</summary>
    public int? OwnerId { get; private set; }

    public static Result<Quote> Create(string author, string text, int? ownerId = null)
    {
        if (string.IsNullOrWhiteSpace(author) || author.Length > 200)
            return Result<Quote>.Fail("Author must be between 1 and 200 characters.");

        if (string.IsNullOrWhiteSpace(text) || text.Length > 1000)
            return Result<Quote>.Fail("Text must be between 1 and 1000 characters.");

        return Result<Quote>.Ok(new Quote(author, text, ownerId));
    }

    public void Delete() => IsDeleted = true;
}
