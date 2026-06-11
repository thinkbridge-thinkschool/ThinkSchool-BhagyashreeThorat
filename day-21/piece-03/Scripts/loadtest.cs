// Day-21 HybridCache lab — minimal concurrent load tester (no external tools).
//
// Runs as a .NET 10 file-based app:
//   dotnet run loadtest.cs -- <url> <connections> <durationSeconds>
//
// Model mirrors bombardier: <connections> worker tasks each fire requests
// back-to-back for <durationSeconds>. We record every request's latency and
// report throughput + the latency distribution (p50/p90/p99) — the numbers the
// exercise asks for. It is deliberately closed-loop (a worker only sends its
// next request once the previous returns), so throughput reflects real
// end-to-end latency under the chosen concurrency.

using System.Diagnostics;

var url = args.Length > 0 ? args[0] : "http://localhost:5005/api/authors/summary";
var connections = args.Length > 1 ? int.Parse(args[1]) : 50;
var durationSeconds = args.Length > 2 ? int.Parse(args[2]) : 15;

// One pooled handler shared by all workers, with a connection cap matching the
// concurrency so we genuinely open N parallel connections to the server.
var handler = new SocketsHttpHandler
{
    MaxConnectionsPerServer = connections,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
};
var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

var deadline = Stopwatch.StartNew();
var stopAt = TimeSpan.FromSeconds(durationSeconds);

var latencies = new List<double>[connections];
var errors = new int[connections];

Console.WriteLine($"→ {connections} connections for {durationSeconds}s against {url}");

var workers = new Task[connections];
for (var w = 0; w < connections; w++)
{
    var id = w;
    latencies[id] = new List<double>(capacity: 4096);
    workers[id] = Task.Run(async () =>
    {
        var local = latencies[id];
        while (deadline.Elapsed < stopAt)
        {
            var sw = Stopwatch.GetTimestamp();
            try
            {
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead);
                // Drain the body so latency includes full response read, like a real client.
                _ = await resp.Content.ReadAsByteArrayAsync();
                if (!resp.IsSuccessStatusCode) errors[id]++;
            }
            catch
            {
                errors[id]++;
            }
            local.Add(Stopwatch.GetElapsedTime(sw).TotalMilliseconds);
        }
    });
}

await Task.WhenAll(workers);
deadline.Stop();

var all = latencies.SelectMany(x => x).OrderBy(x => x).ToArray();
var totalErrors = errors.Sum();
var elapsed = deadline.Elapsed.TotalSeconds;

double Pct(double p)
{
    if (all.Length == 0) return 0;
    var idx = (int)Math.Ceiling(p / 100.0 * all.Length) - 1;
    return all[Math.Clamp(idx, 0, all.Length - 1)];
}

Console.WriteLine();
Console.WriteLine($"requests   : {all.Length}");
Console.WriteLine($"duration   : {elapsed:F2}s");
Console.WriteLine($"throughput : {all.Length / elapsed:F0} req/s");
Console.WriteLine($"errors     : {totalErrors}");
Console.WriteLine($"latency ms : p50={Pct(50):F1}  p90={Pct(90):F1}  p99={Pct(99):F1}  max={Pct(100):F1}");
