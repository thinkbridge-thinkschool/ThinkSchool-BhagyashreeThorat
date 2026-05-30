using QuotesApi.Data;
using QuotesApi.Entities;

namespace QuotesApi.Features.Quotes.Commands;

/// <summary>
/// Write path (command handler). Responsibilities, and ONLY these:
///   1. validation  — delegated to the <see cref="Quote"/> aggregate's invariants
///   2. normalized entity write — Add a tracked entity + SaveChanges
///
/// It returns just the new identifier (<see cref="Result{T}"/> of int), not a view
/// model. The write side has no idea what a screen wants to render — that is the
/// read side's job. Keeping the command focused on "change state" is the whole point.
/// </summary>
public sealed class CreateQuoteHandler
{
    private readonly AppDbContext _context;
    private readonly ILogger<CreateQuoteHandler> _logger;

    public CreateQuoteHandler(AppDbContext context, ILogger<CreateQuoteHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Result<int>> HandleAsync(CreateQuoteCommand command, CancellationToken cancellationToken)
    {
        // Validation stays on the write side. The aggregate owns its invariants
        // (author/text length), so the command handler never duplicates them.
        var result = Quote.Create(command.Author, command.Text,
            command.OwnerId > 0 ? command.OwnerId : null);

        if (!result.IsSuccess)
            return Result<int>.Fail(result.Error!);

        // Normalized write: a tracked entity goes into the same table the read
        // model later projects out of. Same database, same entity — one write path.
        var quote = result.Value!;
        _context.Quotes.Add(quote);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Quote {QuoteId} created by user {OwnerId} with author {Author}",
            quote.Id, command.OwnerId, quote.Author);

        return Result<int>.Ok(quote.Id);
    }
}
