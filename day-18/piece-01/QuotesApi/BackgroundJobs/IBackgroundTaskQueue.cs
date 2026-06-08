namespace QuotesApi.BackgroundJobs;

// Producer/consumer seam between request threads (which enqueue) and the
// QueuedHostedService (which dequeues and runs the work). Singleton: one queue
// shared by every request and the single draining BackgroundService.
public interface IBackgroundTaskQueue
{
    // Called on the request thread — returns immediately so the HTTP response
    // isn't blocked by the slow work. The work item receives the host's
    // shutdown token so long-running jobs can cooperate with graceful shutdown.
    ValueTask EnqueueAsync(Func<CancellationToken, ValueTask> workItem);

    // Called by the draining service. Awaits until an item is available or the
    // stopping token fires, so the consumer loop never busy-waits.
    ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(CancellationToken cancellationToken);
}
