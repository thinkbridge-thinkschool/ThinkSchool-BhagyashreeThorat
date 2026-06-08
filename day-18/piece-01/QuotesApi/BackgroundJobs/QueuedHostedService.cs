namespace QuotesApi.BackgroundJobs;

// Drains the background queue on a single long-lived loop, off the request
// thread. BackgroundService is the framework base class for IHostedService that
// gives us ExecuteAsync(stoppingToken) and the StartAsync/StopAsync plumbing.
public sealed class QueuedHostedService : BackgroundService
{
    private readonly IBackgroundTaskQueue _queue;
    private readonly ILogger<QueuedHostedService> _logger;

    public QueuedHostedService(IBackgroundTaskQueue queue, ILogger<QueuedHostedService> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    // stoppingToken is signalled by the host when shutdown begins (SIGTERM,
    // Ctrl+C, or IHostApplicationLifetime.StopApplication). Every wait in this
    // method observes it, which is what makes shutdown graceful instead of
    // a hard thread abort.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queue drain service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            Func<CancellationToken, ValueTask> workItem;
            try
            {
                // Parks here with no CPU until an item arrives. When shutdown
                // fires, DequeueAsync throws OperationCanceledException and we
                // break out of the loop cleanly.
                workItem = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                // Hand the same token to the work item so a long-running job
                // can cooperatively bail out during shutdown too.
                await workItem(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown interrupted an in-flight job — expected, not an error.
                break;
            }
            catch (Exception ex)
            {
                // One poisoned work item must not kill the drain loop.
                _logger.LogError(ex, "Background work item failed");
            }
        }

        _logger.LogInformation("Queue drain service stopping — token cancelled");
    }

    // Optional override: log around the base StopAsync, which signals the
    // stoppingToken above and then waits (up to the host's ShutdownTimeout,
    // 30s by default) for ExecuteAsync to finish before the process exits.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Graceful shutdown requested — draining in-flight work");
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("Queue drain service stopped cleanly");
    }
}
