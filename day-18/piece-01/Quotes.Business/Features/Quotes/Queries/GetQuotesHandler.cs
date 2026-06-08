using Microsoft.EntityFrameworkCore;

namespace Quotes.Business.Features.Quotes.Queries;

/// <summary>
/// Read path (query handler). It returns a projection-shaped DTO — NEVER an entity.
///
/// Performance characteristics of the read model:
///   • AsNoTracking      — no change-tracker snapshots, no identity-map allocations
///   • Select() projection — SQL selects ONLY Id/Author/Text, skipping IsDeleted,
///     OwnerId, AuthorId and the Author navigation; less data over the wire
///   • the materialized objects are immutable records sized exactly to the response
///
/// This is where Dapper would slot in LATER if this query became hot: the read path
/// is already a flat projection with no domain behaviour, so it could be replaced by
/// a hand-written `SELECT Id, Author, Text FROM Quotes WHERE IsDeleted = 0 ...`
/// executed via `connection.QueryAsync&lt;QuoteListItemDto&gt;(...)` — dropping the
/// EF expression-tree translation cost entirely. We do NOT migrate now; the point is
/// that separating the read path makes such a swap a one-class change, invisible to
/// the write side. (See explanation in the PR description / handoff notes.)
/// </summary>
public sealed class GetQuotesHandler
{
    private readonly AppDbContext _context;
    private readonly ILogger<GetQuotesHandler> _logger;

    public GetQuotesHandler(AppDbContext context, ILogger<GetQuotesHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<QuoteListItemDto>> HandleAsync(GetQuotesQuery query, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Fetching quotes page {Page} size {Size} search {Search}",
            query.Page, query.Size, query.Search);

        // Build the filter as IQueryable so EF translates everything to a single
        // SQL statement — search runs in the database, NOT in memory.
        IQueryable<Quote> quotes = _context.Quotes
            .AsNoTracking()
            .Where(q => !q.IsDeleted);

        var term = query.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            // Text/Author match translates to SQL LIKE '%term%'. Case-sensitivity
            // follows the database collation (case-insensitive by default).
            // If the term is a whole number, ALSO match the quote Id exactly so
            // users can look a quote up by its id.
            if (int.TryParse(term, out var id))
            {
                quotes = quotes.Where(q =>
                    q.Id == id ||
                    q.Author.Contains(term) ||
                    q.Text.Contains(term));
            }
            else
            {
                quotes = quotes.Where(q =>
                    q.Author.Contains(term) ||
                    q.Text.Contains(term));
            }
        }

        return await quotes
            .OrderBy(q => q.Id)
            .Skip((query.Page - 1) * query.Size)
            .Take(query.Size)
            .Select(q => new QuoteListItemDto(q.Id, q.Author, q.Text))
            .ToListAsync(cancellationToken);
    }
}
