
namespace Quotes.Repository.Repositories;

public interface ICollectionRepository
{
    Task<List<Collection>> GetAllAsync(CancellationToken cancellationToken);

    Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<Collection> AddAsync(Collection collection, CancellationToken cancellationToken);

    Task UpdateAsync(Collection collection, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}
