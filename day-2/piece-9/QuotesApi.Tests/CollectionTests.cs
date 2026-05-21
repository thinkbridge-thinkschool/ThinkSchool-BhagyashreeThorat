using QuotesApi.Entities;
using Xunit;

namespace QuotesApi.Tests;

public class CollectionTests
{
    [Fact]
    public void AddItem_StampsAddedAt_FromClock()
    {
        var frozenTime = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(frozenTime);
        var collection = new Collection("Reading List", "user-1");

        collection.AddItem(42, clock.UtcNow);

        var item = collection.Items.Single();
        Assert.Equal(42, item.QuoteId);
        Assert.Equal(frozenTime.UtcDateTime, item.AddedAt);
    }
}
