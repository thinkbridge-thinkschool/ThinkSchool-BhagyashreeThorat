using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Entities;

namespace QuotesApi.Repositories;

public class CollectionRepository : ICollectionRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<CollectionRepository> _logger;

    public CollectionRepository(AppDbContext context, ILogger<CollectionRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    // FIX: single LEFT JOIN — 1 query instead of N+1, delay removed
    public async Task<List<Collection>> GetAllAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching all collections");

        return await _context.Collections
            .Include(c => c.Items)
            .ToListAsync(cancellationToken);
    }

    public async Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching collection with id {Id}", id);

        // Include owned Items so the aggregate is fully rehydrated
        return await _context.Collections
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Collection> AddAsync(Collection collection, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Adding collection '{Name}'", collection.Name);

        _context.Collections.Add(collection);
        await _context.SaveChangesAsync(cancellationToken);

        return collection;
    }

    public async Task UpdateAsync(Collection collection, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating collection {Id}", collection.Id);

        // Entity is already tracked; just save
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var collection = await _context.Collections
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (collection is null)
            return false;

        _context.Collections.Remove(collection);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}