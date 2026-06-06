using FluentAssertions;
using Quotes.Repository.Entities;
using Quotes.Model.Shared;
using Xunit;

namespace Quotes.Tests.Unit.Domain;

public class CollectionEntityTests
{
    // ──────────────────────────────────────────────────────────────────
    //  Construction / name validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ValidName_SetsNameAndOwnerId()
    {
        // Arrange & Act
        var collection = new Collection("My Quotes", "owner-1");

        // Assert
        collection.Name.Should().Be("My Quotes");
        collection.OwnerId.Should().Be("owner-1");
        collection.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_NullOrEmptyOrWhitespaceName_ThrowsDomainException(string? name)
    {
        // Arrange & Act
        Action act = () => new Collection(name!, "owner-1");

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_NameTooShort_ThrowsDomainException()
    {
        // Arrange — 2 chars; minimum is 3
        Action act = () => new Collection("ab", "owner-1");

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_NameAtMinimumBoundary_Succeeds()
    {
        // Arrange — exactly 3 chars
        Action act = () => new Collection("abc", "owner-1");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NameAtMaximumBoundary_Succeeds()
    {
        // Arrange — exactly 80 chars
        var name = new string('x', 80);

        Action act = () => new Collection(name, "owner-1");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NameExceedsMaximum_ThrowsDomainException()
    {
        // Arrange — 81 chars
        var tooLong = new string('x', 81);

        Action act = () => new Collection(tooLong, "owner-1");

        // Assert
        act.Should().Throw<DomainException>();
    }

    // ──────────────────────────────────────────────────────────────────
    //  AddItem
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AddItem_FirstItem_AddsToCollection()
    {
        // Arrange
        var collection = new Collection("My Quotes", "owner-1");

        // Act
        collection.AddItem(quoteId: 1, DateTimeOffset.UtcNow);

        // Assert
        collection.Items.Should().HaveCount(1);
        collection.Items[0].QuoteId.Should().Be(1);
    }

    [Fact]
    public void AddItem_DuplicateQuoteId_ThrowsDomainException()
    {
        // Arrange
        var collection = new Collection("My Quotes", "owner-1");
        collection.AddItem(42, DateTimeOffset.UtcNow);

        // Act
        Action act = () => collection.AddItem(42, DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*42*already*");
    }

    [Fact]
    public void AddItem_AtCapacityOf50_ThrowsDomainException()
    {
        // Arrange — fill collection to the 50-item cap
        var collection = new Collection("My Quotes", "owner-1");
        for (int i = 1; i <= 50; i++)
            collection.AddItem(i, DateTimeOffset.UtcNow);

        // Act
        Action act = () => collection.AddItem(51, DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*50*");
    }

    // ──────────────────────────────────────────────────────────────────
    //  RemoveItem
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveItem_ExistingItem_RemovesFromCollection()
    {
        // Arrange
        var collection = new Collection("My Quotes", "owner-1");
        collection.AddItem(10, DateTimeOffset.UtcNow);

        // Act
        collection.RemoveItem(10);

        // Assert
        collection.Items.Should().BeEmpty();
    }

    [Fact]
    public void RemoveItem_NonExistentItem_ThrowsDomainException()
    {
        // Arrange
        var collection = new Collection("My Quotes", "owner-1");

        // Act
        Action act = () => collection.RemoveItem(99);

        // Assert
        act.Should().Throw<DomainException>();
    }

    // ──────────────────────────────────────────────────────────────────
    //  Rename
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Rename_ValidName_UpdatesName()
    {
        // Arrange
        var collection = new Collection("Old Name", "owner-1");

        // Act
        collection.Rename("New Name");

        // Assert
        collection.Name.Should().Be("New Name");
    }

    [Fact]
    public void Rename_TooShortName_ThrowsDomainException()
    {
        // Arrange
        var collection = new Collection("Valid Name", "owner-1");

        // Act
        Action act = () => collection.Rename("ab");

        // Assert
        act.Should().Throw<DomainException>();
    }

    // ──────────────────────────────────────────────────────────────────
    //  CollectionItem value equality
    //
    //  CollectionItem overrides Equals/GetHashCode so it can act as a
    //  value object keyed by QuoteId (needed by List.Remove and hash
    //  collections).  These tests verify the contract.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CollectionItem_SameQuoteId_AreEqual()
    {
        // Arrange
        var collection = new Collection("Test", "owner-1");
        collection.AddItem(42, DateTimeOffset.UtcNow);

        var item = collection.Items[0];

        // Act — identity: the same object must equal itself
        item.Equals(item).Should().BeTrue();
    }

    [Fact]
    public void CollectionItem_GetHashCode_ConsistentWithEquals()
    {
        // Arrange
        var collection = new Collection("Test", "owner-1");
        collection.AddItem(42, DateTimeOffset.UtcNow);
        collection.AddItem(43, DateTimeOffset.UtcNow);

        var item42a = collection.Items[0]; // QuoteId == 42
        var item43  = collection.Items[1]; // QuoteId == 43

        // Act & Assert — hash codes must differ for distinct keys
        item42a.GetHashCode().Should().Be(42.GetHashCode(),
            "CollectionItem hash is based on QuoteId");
        item42a.GetHashCode().Should().NotBe(item43.GetHashCode(),
            "distinct QuoteIds must produce distinct hash codes");
    }

    [Fact]
    public void CollectionItem_DifferentType_NotEqual()
    {
        // Arrange
        var collection = new Collection("Test", "owner-1");
        collection.AddItem(1, DateTimeOffset.UtcNow);
        var item = collection.Items[0];

        // Act & Assert — Equals must return false for an incompatible type
        item.Equals("not-a-collection-item").Should().BeFalse();
    }
}
