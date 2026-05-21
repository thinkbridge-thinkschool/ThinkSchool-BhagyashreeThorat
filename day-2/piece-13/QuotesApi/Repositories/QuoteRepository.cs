using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Entities;

namespace QuotesApi.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<QuoteRepository> _logger;

    public QuoteRepository(AppDbContext context, ILogger<QuoteRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<Quote>> GetAllAsync(int page, int size, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching quotes page {Page} size {Size}", page, size);

        return await _context.Quotes
            .Where(q => !q.IsDeleted)
            .Skip((page - 1) * size)
            .Take(size)
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
}
