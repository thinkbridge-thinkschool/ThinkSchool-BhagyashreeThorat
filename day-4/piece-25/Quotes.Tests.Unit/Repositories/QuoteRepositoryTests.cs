using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using QuotesApi.Data;
using QuotesApi.Entities;
using QuotesApi.Repositories;
using Xunit;

namespace Quotes.Tests.Unit.Repositories;

// ─────────────────────────────────────────────────────────────────────────────
//  QuoteRepository unit tests
//
//  Uses EF Core InMemory provider — same technique as RefreshTokenServiceTests.
//  Each test gets a unique database name so tests are fully isolated.
//
//  What is tested:
//    - GetAllAsync — pagination, respects IsDeleted soft-delete flag
//    - GetByIdAsync — found, not found, soft-deleted quote is invisible
//    - AddAsync — persists quote, returns saved entity
//    - DeleteAsync — soft-deletes, returns true; returns false for unknown id
//    - UpdateAsync — persists Author/Text changes made on the entity
// ─────────────────────────────────────────────────────────────────────────────

public class QuoteRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ILogger<QuoteRepository> _logger = Substitute.For<ILogger<QuoteRepository>>();
    private readonly QuoteRepository _sut;

    public QuoteRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db  = new AppDbContext(options);
        _sut = new QuoteRepository(_db, _logger);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Quote SeedQuote(string author = "Seneca", string text = "Per aspera ad astra.")
    {
        var result = Quote.Create(author, text);
        _db.Quotes.Add(result.Value!);
        _db.SaveChanges();
        return result.Value!;
    }

    // ── GetAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_EmptyDatabase_ReturnsEmptyList()
    {
        // Act
        var result = await _sut.GetAllAsync(page: 1, size: 10, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_WithQuotes_ReturnsPagedResults()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
            SeedQuote(author: $"Author{i}", text: $"Text number {i}.");

        // Act — page 1, size 3
        var page1 = await _sut.GetAllAsync(page: 1, size: 3, CancellationToken.None);

        // Assert
        page1.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetAllAsync_SecondPage_ReturnsRemainingItems()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
            SeedQuote(author: $"Author{i}", text: $"Text number {i}.");

        // Act — page 2, size 3 → should return 2 items
        var page2 = await _sut.GetAllAsync(page: 2, size: 3, CancellationToken.None);

        // Assert
        page2.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllAsync_SoftDeletedQuote_IsNotReturned()
    {
        // Arrange — seed one active, one soft-deleted
        SeedQuote(author: "Active Author", text: "Active quote.");
        var deletedQuote = SeedQuote(author: "Deleted Author", text: "Deleted quote.");
        deletedQuote.Delete();
        _db.SaveChanges();

        // Act
        var results = await _sut.GetAllAsync(page: 1, size: 10, CancellationToken.None);

        // Assert — only the active quote should appear
        results.Should().HaveCount(1);
        results[0].Author.Should().Be("Active Author");
    }

    // ── GetByIdAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsQuote()
    {
        // Arrange
        var quote = SeedQuote("Marcus Aurelius", "The obstacle is the way.");

        // Act
        var result = await _sut.GetByIdAsync(quote.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Author.Should().Be("Marcus Aurelius");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull()
    {
        // Act
        var result = await _sut.GetByIdAsync(99999, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_SoftDeletedQuote_ReturnsNull()
    {
        // Arrange — soft-delete a quote
        var quote = SeedQuote();
        quote.Delete();
        _db.SaveChanges();

        // Act
        var result = await _sut.GetByIdAsync(quote.Id, CancellationToken.None);

        // Assert — soft-deleted quotes must not be visible
        result.Should().BeNull();
    }

    // ── AddAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ValidQuote_PersistsAndReturnsWithId()
    {
        // Arrange
        var quote = Quote.Create("Epictetus", "Freedom is the only worthy goal.").Value!;

        // Act
        var saved = await _sut.AddAsync(quote, CancellationToken.None);

        // Assert
        saved.Id.Should().BePositive("EF assigns a DB-generated Id");
        var fromDb = await _db.Quotes.FindAsync(saved.Id);
        fromDb.Should().NotBeNull();
        fromDb!.Author.Should().Be("Epictetus");
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingQuote_ReturnsTrueAndSoftDeletes()
    {
        // Arrange
        var quote = SeedQuote();

        // Act
        var deleted = await _sut.DeleteAsync(quote.Id, CancellationToken.None);

        // Assert
        deleted.Should().BeTrue();
        var fromDb = await _db.Quotes.FindAsync(quote.Id);
        fromDb!.IsDeleted.Should().BeTrue("soft delete must set IsDeleted = true");
    }

    [Fact]
    public async Task DeleteAsync_NonExistentId_ReturnsFalse()
    {
        // Act
        var deleted = await _sut.DeleteAsync(99999, CancellationToken.None);

        // Assert
        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_AlreadyDeletedQuote_ReturnsFalse()
    {
        // Arrange — soft-delete first, then try again
        var quote = SeedQuote();
        quote.Delete();
        _db.SaveChanges();

        // Act — the repo's FindAsync uses !q.IsDeleted, so it finds nothing
        var deleted = await _sut.DeleteAsync(quote.Id, CancellationToken.None);

        // Assert
        deleted.Should().BeFalse();
    }

    // ── UpdateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_AfterMutation_PersistsChanges()
    {
        // Arrange
        var quote = SeedQuote(author: "Old Author", text: "Old text.");
        quote.Update("New Author", "New text.");

        // Act
        await _sut.UpdateAsync(quote, CancellationToken.None);

        // Assert — reload from DB to confirm persistence
        _db.Entry(quote).Reload();
        quote.Author.Should().Be("New Author");
        quote.Text.Should().Be("New text.");
    }
}
