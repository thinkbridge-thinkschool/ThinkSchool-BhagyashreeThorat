using System.Threading.Channels;

namespace QuotesApi.BackgroundJobs;

// Channel<T> is the modern, allocation-light in-memory queue: it gives us an
// async DequeueAsync that parks the consumer with zero CPU until work arrives,
// and back-pressure when the buffer fills.
public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<CancellationToken, ValueTask>> _queue;

    public BackgroundTaskQueue(int capacity = 100)
    {
        // Bounded channel = back-pressure. If producers outrun the single
        // consumer, EnqueueAsync awaits rather than letting the queue grow
        // without bound and exhaust memory.
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,   // exactly one BackgroundService drains it
            SingleWriter = false,  // many request threads enqueue
        };
        _queue = Channel.CreateBounded<Func<CancellationToken, ValueTask>>(options);
    }

    public ValueTask EnqueueAsync(Func<CancellationToken, ValueTask> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return _queue.Writer.WriteAsync(workItem);
    }

    public ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(CancellationToken cancellationToken)
        => _queue.Reader.ReadAsync(cancellationToken);
}
