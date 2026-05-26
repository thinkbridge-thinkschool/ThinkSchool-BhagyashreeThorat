using QuotesApi.Abstractions;

namespace Tests.Domain.Infrastructure;

/// <summary>
/// Test double for IClock.  Gives integration tests full control over "now"
/// without touching production code or relying on real wall-clock time.
///
/// Usage:
///   factory.Clock.SetTo(DateTimeOffset.UtcNow.AddDays(-8));
///   factory.Clock.Advance(TimeSpan.FromDays(10));
/// </summary>
public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

    public void Advance(TimeSpan duration) => UtcNow += duration;

    public void SetTo(DateTimeOffset time) => UtcNow = time;
}
