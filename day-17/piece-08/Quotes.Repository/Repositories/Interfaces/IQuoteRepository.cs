
namespace Quotes.Repository.Repositories;

public interface IQuoteRepository
{
    Task<List<QuoteListItemDto>> GetAllAsync(int page,int size,CancellationToken cancellationToken);

    Task<Quote?> GetByIdAsync(int id,CancellationToken cancellationToken);

    Task<Quote> AddAsync(Quote quote,CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int id,CancellationToken cancellationToken);

    Task UpdateAsync(Quote quote, CancellationToken cancellationToken);
}
