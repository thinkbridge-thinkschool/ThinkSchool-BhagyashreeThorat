using QuotesApi.Abstractions;

namespace Quotes.Tests.Unit.Fakes;

/// <summary>
/// Deterministic clock for tests — set UtcNow to any fixed point in time.
/// Avoids flaky tests caused by real-world clock progression.
/// </summary>
public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

    public void Advance(TimeSpan duration) => UtcNow += duration;
}
