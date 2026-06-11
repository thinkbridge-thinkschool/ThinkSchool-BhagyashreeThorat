// Day-21 HybridCache lab — stampede burst tester.
//
//   dotnet run burst.cs -- <url> <n>
//
// Unlike loadtest.cs (closed-loop, sustained), this fires <n> requests ALL AT
// ONCE via Task.WhenAll against a COLD cache key. Without stampede protection
// every one of them would miss and hammer the DB with the same query (N DB
// hits). With HybridCache, they coalesce onto a single factory execution — so
// the server-side `dbQueries` counter rises by exactly 1.
//
// This tool just generates the simultaneous burst; the PROOF is read afterwards
// from /api/authors/cache-stats.

using System.Diagnostics;

var url = args.Length > 0 ? args[0] : "http://localhost:5005/api/authors/summary-cached";
var n = args.Length > 1 ? int.Parse(args[1]) : 200;

// Allow all N connections to open at once so the requests genuinely overlap
// inside the factory's execution window (not drip through a small pool).
var handler = new SocketsHttpHandler { MaxConnectionsPerServer = n };
var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

Console.WriteLine($"→ firing {n} simultaneous requests at {url}");

var sw = Stopwatch.StartNew();
var tasks = Enumerable.Range(0, n).Select(async i =>
{
    var t0 = Stopwatch.GetTimestamp();
    using var resp = await http.GetAsync(url);
    _ = await resp.Content.ReadAsByteArrayAsync();
    var code = (int)resp.StatusCode;
    var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    return (code, ms);
}).ToArray();

var results = await Task.WhenAll(tasks);
sw.Stop();

var ok = results.Count(r => r.Item1 == 200);
var lat = results.Select(r => r.Item2).OrderBy(x => x).ToArray();

Console.WriteLine($"completed  : {ok}/{n} OK in {sw.Elapsed.TotalMilliseconds:F0}ms wall-clock");
Console.WriteLine($"latency ms : min={lat.First():F1}  p50={lat[lat.Length / 2]:F1}  max={lat.Last():F1}");
