namespace QuotesApi.Infrastructure;

/// <summary>
/// Day-21 HybridCache lab instrumentation.
///
/// A singleton tally shared by every request thread. It answers two questions
/// the load test needs proof for:
///
///   1. How many requests hit the cached endpoint?      (Requests)
///   2. How many of those actually reached the database? (DbQueries)
///
/// DbQueries is incremented ONLY inside the HybridCache factory delegate — the
/// factory runs exactly once per genuine cache miss. So:
///
///   • hit rate            = 1 - DbQueries / Requests
///   • stampede protection = fire N concurrent requests at a cold key and watch
///                           DbQueries rise by 1, not by N.
///
/// Interlocked keeps the counters correct under the concurrent load we throw at
/// them — no lock, no torn reads.
/// </summary>
public sealed class CacheMetrics
{
    private long _requests;
    private long _dbQueries;

    /// <summary>Total calls into the cached endpoint.</summary>
    public long Requests => Interlocked.Read(ref _requests);

    /// <summary>Times the DB factory actually executed (= real cache misses).</summary>
    public long DbQueries => Interlocked.Read(ref _dbQueries);

    public void RecordRequest() => Interlocked.Increment(ref _requests);

    public void RecordDbQuery() => Interlocked.Increment(ref _dbQueries);

    /// <summary>Zero the counters between experiments.</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _requests, 0);
        Interlocked.Exchange(ref _dbQueries, 0);
    }
}
