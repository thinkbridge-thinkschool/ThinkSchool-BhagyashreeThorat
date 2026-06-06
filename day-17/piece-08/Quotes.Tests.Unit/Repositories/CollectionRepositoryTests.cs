using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Quotes.Repository.Context;
using Quotes.Repository.Entities;
using Quotes.Model.Shared;
using Quotes.Repository.Repositories;
using Xunit;

namespace Quotes.Tests.Unit.Repositories;

// ─────────────────────────────────────────────────────────────────────────────
//  CollectionRepository unit tests
//
//  Uses EF Core InMemory provider.  Each test gets its own isolated database.
//
//  What is tested:
//    - GetByIdAsync — found with items loaded, not found
//    - AddAsync — persists collection, returns saved entity
//    - UpdateAsync — persists aggregate state changes (e.g. added items)
//    - DeleteAsync — removes collection, returns true; returns false when absent
// ─────────────────────────────────────────────────────────────────────────────

public class CollectionRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ILogger<CollectionRepository> _logger = Substitute.For<ILogger<CollectionRepository>>();
    private readonly CollectionRepository _sut;

    public CollectionRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db  = new AppDbContext(options);
        _sut = new CollectionRepository(_db, _logger);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Collection SeedCollection(string name = "My Collection", string ownerId = "user-1")
    {
        var collection = new Collection(name, ownerId);
        _db.Collections.Add(collection);
        _db.SaveChanges();
        return collection;
    }

    // ── GetByIdAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingCollection_ReturnsCollectionWithItems()
    {
        // Arrange
        var collection = SeedCollection("Stoics");

        // Act
        var result = await _sut.GetByIdAsync(collection.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Stoics");
        result.Items.Should().BeEmpty("no items were added");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull()
    {
        // Act
        var result = await _sut.GetByIdAsync(99999, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ── AddAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ValidCollection_PersistsAndReturnsWithId()
    {
        // Arrange
        var collection = new Collection("Philosophy Quotes", "user-42");

        // Act
        var saved = await _sut.AddAsync(collection, CancellationToken.None);

        // Assert
        saved.Id.Should().BePositive("EF assigns a generated Id");
        var fromDb = await _db.Collections.FindAsync(saved.Id);
        fromDb.Should().NotBeNull();
        fromDb!.Name.Should().Be("Philosophy Quotes");
        fromDb.OwnerId.Should().Be("user-42");
    }

    // ── UpdateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_AfterAddItem_PersistsNewItem()
    {
        // Arrange
        var collection = SeedCollection();
        collection.AddItem(quoteId: 7, DateTimeOffset.UtcNow);

        // Act
        await _sut.UpdateAsync(collection, CancellationToken.None);

        // Assert — reload to confirm persistence
        var fromDb = await _db.Collections
            .Include(c => c.Items)
            .FirstAsync(c => c.Id == collection.Id);
        fromDb.Items.Should().ContainSingle(i => i.QuoteId == 7);
    }

    [Fact]
    public async Task UpdateAsync_AfterRename_PersistsNewName()
    {
        // Arrange
        var collection = SeedCollection("Old Name");
        collection.Rename("New Name");

        // Act
        await _sut.UpdateAsync(collection, CancellationToken.None);

        // Assert
        var fromDb = await _db.Collections.FindAsync(collection.Id);
        fromDb!.Name.Should().Be("New Name");
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingCollection_ReturnsTrueAndRemovesFromDb()
    {
        // Arrange
        var collection = SeedCollection();

        // Act
        var deleted = await _sut.DeleteAsync(collection.Id, CancellationToken.None);

        // Assert
        deleted.Should().BeTrue();
        var fromDb = await _db.Collections.FindAsync(collection.Id);
        fromDb.Should().BeNull("hard delete must remove the record");
    }

    [Fact]
    public async Task DeleteAsync_NonExistentId_ReturnsFalse()
    {
        // Act
        var deleted = await _sut.DeleteAsync(99999, CancellationToken.None);

        // Assert
        deleted.Should().BeFalse();
    }
}
