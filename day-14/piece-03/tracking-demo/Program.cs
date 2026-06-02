using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Entities;

const string ConnectionString =
    "Server=localhost,1433;Database=QuotesDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;";

static AppDbContext NewContext()
{
    var opts = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(ConnectionString)
        .Options;
    return new AppDbContext(opts);
}

// Process-wide allocation counter; async/await can hop threads, so the
// per-thread variant produces noisy (sometimes negative) deltas.
static long Bytes() => GC.GetTotalAllocatedBytes(precise: true);

static void Header(string s)
{
    Console.WriteLine();
    Console.WriteLine(new string('=', 70));
    Console.WriteLine(s);
    Console.WriteLine(new string('=', 70));
}

// --------------------------------------------------------------------
// 1) IDENTITY RESOLUTION
// --------------------------------------------------------------------
Header("1. IDENTITY RESOLUTION");

await using (var ctx = NewContext())
{
    var total = await ctx.Quotes.CountAsync();
    Console.WriteLine($"Total rows in Quotes table: {total}");
}

await using (var ctx = NewContext())
{
    // Tracked: same query returns the SAME instance (identity map).
    var a1 = await ctx.Quotes.FirstAsync(q => q.Id == 1);
    var a2 = await ctx.Quotes.FirstAsync(q => q.Id == 1);
    Console.WriteLine($"[Tracked]      ReferenceEquals(a1, a2) = {ReferenceEquals(a1, a2)}");
    Console.WriteLine($"[Tracked]      ChangeTracker entries   = {ctx.ChangeTracker.Entries<Quote>().Count()}");
}

await using (var ctx = NewContext())
{
    // No-tracking: each query materializes a NEW instance.
    var b1 = await ctx.Quotes.AsNoTracking().FirstAsync(q => q.Id == 1);
    var b2 = await ctx.Quotes.AsNoTracking().FirstAsync(q => q.Id == 1);
    Console.WriteLine($"[NoTracking]   ReferenceEquals(b1, b2) = {ReferenceEquals(b1, b2)}");
    Console.WriteLine($"[NoTracking]   ChangeTracker entries   = {ctx.ChangeTracker.Entries<Quote>().Count()}");
}

// --------------------------------------------------------------------
// 2) TRACKED vs NO-TRACKING — TIMING + ALLOCATIONS
// --------------------------------------------------------------------
Header("2. PERFORMANCE: TRACKED vs AsNoTracking (full table scan)");

// Warm-up: open a connection, JIT, build query cache. Excluded from measurement.
await using (var warm = NewContext())
{
    _ = await warm.Quotes.AsNoTracking().Take(10).ToListAsync();
    _ = await warm.Quotes.Take(10).ToListAsync();
}

async Task<(TimeSpan elapsed, long allocated, int count)> MeasureTrackedAsync()
{
    await using var ctx = NewContext();
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var before = Bytes();
    var sw = Stopwatch.StartNew();
    var list = await ctx.Quotes.ToListAsync();
    sw.Stop();
    var after = Bytes();
    return (sw.Elapsed, after - before, list.Count);
}

async Task<(TimeSpan elapsed, long allocated, int count)> MeasureNoTrackingAsync()
{
    await using var ctx = NewContext();
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var before = Bytes();
    var sw = Stopwatch.StartNew();
    var list = await ctx.Quotes.AsNoTracking().ToListAsync();
    sw.Stop();
    var after = Bytes();
    return (sw.Elapsed, after - before, list.Count);
}

// Run each variant 3 times; report the best (lowest elapsed) to reduce noise.
const int Runs = 3;
var trackedRuns = new (TimeSpan, long, int)[Runs];
var noTrackRuns = new (TimeSpan, long, int)[Runs];

for (int i = 0; i < Runs; i++) trackedRuns[i] = await MeasureTrackedAsync();
for (int i = 0; i < Runs; i++) noTrackRuns[i] = await MeasureNoTrackingAsync();

(TimeSpan elapsed, long allocated, int count) Best((TimeSpan, long, int)[] runs)
{
    var ordered = runs.OrderBy(r => r.Item1).ToArray();
    return ordered[0];
}

var tracked = Best(trackedRuns);
var noTrack = Best(noTrackRuns);

Console.WriteLine($"Variant A (Tracked)      rows={tracked.count}  time={tracked.elapsed.TotalMilliseconds,8:F2} ms  alloc={tracked.allocated,12:N0} bytes");
Console.WriteLine($"Variant B (AsNoTracking) rows={noTrack.count}  time={noTrack.elapsed.TotalMilliseconds,8:F2} ms  alloc={noTrack.allocated,12:N0} bytes");

Console.WriteLine();
Console.WriteLine("All runs (ms / bytes):");
for (int i = 0; i < Runs; i++)
    Console.WriteLine($"  Tracked    #{i + 1}: {trackedRuns[i].Item1.TotalMilliseconds,8:F2} ms  {trackedRuns[i].Item2,12:N0} bytes");
for (int i = 0; i < Runs; i++)
    Console.WriteLine($"  NoTracking #{i + 1}: {noTrackRuns[i].Item1.TotalMilliseconds,8:F2} ms  {noTrackRuns[i].Item2,12:N0} bytes");

// --------------------------------------------------------------------
// 3) COMPARISON / DELTAS
// --------------------------------------------------------------------
Header("3. COMPARISON");

var timeDeltaMs = tracked.elapsed.TotalMilliseconds - noTrack.elapsed.TotalMilliseconds;
var timeDeltaPct = timeDeltaMs / tracked.elapsed.TotalMilliseconds * 100.0;
var allocDelta = tracked.allocated - noTrack.allocated;
var allocDeltaPct = (double)allocDelta / tracked.allocated * 100.0;

Console.WriteLine($"Time   delta (Tracked - NoTracking): {timeDeltaMs,8:F2} ms   ({timeDeltaPct,5:F1}% faster with AsNoTracking)");
Console.WriteLine($"Alloc  delta (Tracked - NoTracking): {allocDelta,12:N0} bytes ({allocDeltaPct,5:F1}% less with AsNoTracking)");

Console.WriteLine();
Console.WriteLine("Avoid AsNoTracking when you intend to modify/delete the entities and SaveChanges -- you need tracking for EF to detect changes.");
