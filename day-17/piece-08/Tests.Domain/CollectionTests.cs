using FluentAssertions;
using Quotes.Repository.Entities;
using Quotes.Model.Shared;
using Xunit;

namespace Tests.Domain;

public class CollectionTests
{
    [Fact]
    public void Create_WithEmptyName_ShouldThrow()
    {
        Action act = () => new Collection("", "owner-1");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithNameLongerThan80Chars_ShouldThrow()
    {
        var longName = new string('a', 81);
        Action act = () => new Collection(longName, "owner-1");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddItem_WhenAt51stItem_ShouldThrow()
    {
        var collection = new Collection("My List", "owner-1");
        for (int i = 1; i <= 50; i++)
            collection.AddItem(i, DateTimeOffset.UtcNow);

        Action act = () => collection.AddItem(51, DateTimeOffset.UtcNow);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddItem_WithDuplicateQuoteId_ShouldThrow()
    {
        var collection = new Collection("My List", "owner-1");
        collection.AddItem(42, DateTimeOffset.UtcNow);

        Action act = () => collection.AddItem(42, DateTimeOffset.UtcNow);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void RemoveItem_WhenItemNotFound_ShouldThrow()
    {
        var collection = new Collection("My List", "owner-1");

        Action act = () => collection.RemoveItem(99);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddItem_ThenRemoveItem_LeavesZeroItems()
    {
        var collection = new Collection("My List", "owner-1");
        collection.AddItem(42, DateTimeOffset.UtcNow);

        collection.RemoveItem(42);

        collection.Items.Should().BeEmpty();
    }
}
