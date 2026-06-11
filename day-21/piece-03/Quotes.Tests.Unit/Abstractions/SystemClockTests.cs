using FluentAssertions;
using Quotes.Business.Services;
using Xunit;

namespace Quotes.Tests.Unit.Abstractions;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentUtcTime()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;
        var sut    = new SystemClock();

        // Act
        var now = sut.UtcNow;

        // Assert — wall clock advances monotonically
        var after = DateTimeOffset.UtcNow;
        now.Should().BeOnOrAfter(before);
        now.Should().BeOnOrBefore(after);
        now.Offset.Should().Be(TimeSpan.Zero, "UtcNow must return UTC, not a local offset");
    }
}
