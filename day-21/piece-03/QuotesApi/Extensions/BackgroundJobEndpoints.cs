using QuotesApi.BackgroundJobs;

namespace QuotesApi.Extensions;

// Day-18 background-jobs lab. POST a job; the handler enqueues it and returns
// 202 immediately, off-loading the slow work to the QueuedHostedService drainer.
public static class BackgroundJobEndpoints
{
    public static IEndpointRouteBuilder MapBackgroundJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs");

        // Anonymous + simulated-slow so it's easy to demonstrate that the
        // response returns before the work finishes.
        group.MapPost("/enqueue", async (
            int? seconds,
            IBackgroundTaskQueue queue,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Jobs");
            var duration = TimeSpan.FromSeconds(seconds ?? 3);
            var jobId = Guid.NewGuid();

            await queue.EnqueueAsync(async token =>
            {
                logger.LogInformation("Job {JobId} started — simulating {Seconds}s of work", jobId, duration.TotalSeconds);
                // Task.Delay observes the token so shutdown cancels the wait
                // instead of blocking the host for the full duration.
                await Task.Delay(duration, token);
                logger.LogInformation("Job {JobId} finished", jobId);
            });

            // 202 Accepted: request thread is free; work runs in the background.
            return Results.Accepted($"/api/jobs/{jobId}", new { jobId, status = "queued" });
        });

        return app;
    }
}
