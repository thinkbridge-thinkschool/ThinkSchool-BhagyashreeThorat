using Microsoft.EntityFrameworkCore;

namespace Quotes.Repository.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<QuoteRepository> _logger;

    public QuoteRepository(AppDbContext context, ILogger<QuoteRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<QuoteListItemDto>> GetAllAsync(int page, int size, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching quotes page {Page} size {Size}", page, size);

        // Projection lets EF select only the columns the list endpoint serializes,
        // skipping IsDeleted / OwnerId and avoiding entity tracking overhead.
        return await _context.Quotes
            .Where(q => !q.IsDeleted)
            .OrderBy(q => q.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(q => new QuoteListItemDto(q.Id, q.Author, q.Text))
            .ToListAsync(cancellationToken);
    }

    public async Task<Quote?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching quote with id {Id}", id);

        return await _context.Quotes
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken);
    }

    public async Task<Quote> AddAsync(Quote quote, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Adding quote by {Author}", quote.Author);

        _context.Quotes.Add(quote);

        await _context.SaveChangesAsync(cancellationToken);

        return quote;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var quote = await _context.Quotes
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken);

        if (quote is null)
            return false;

        quote.Delete();

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task UpdateAsync(Quote quote, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating quote {Id}", quote.Id);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
