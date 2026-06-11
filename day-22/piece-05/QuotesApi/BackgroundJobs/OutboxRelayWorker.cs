using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Quotes.Model.Messaging;
using Quotes.Repository.Context;
using QuotesApi.Infrastructure.Messaging;

namespace QuotesApi.BackgroundJobs;

/// <summary>
/// Transactional-outbox relay. The write path (CreateQuoteHandler) never publishes
/// to Service Bus directly — it stages a row in OutboxMessages inside the same DB
/// transaction as the domain change. This worker polls for unsent rows, publishes
/// them, and marks them sent.
///
/// Crash safety: a row is marked sent ONLY after a successful publish + SaveChanges.
/// If the process dies after publishing but before the row is marked, the row stays
/// unsent and is republished on the next run — at-least-once delivery. The consumer
/// dedupes on the stable Service Bus MessageId (== OutboxMessage.Id), so the duplicate
/// is ignored. Net effect: no message lost, no duplicate processed.
/// </summary>
public sealed class OutboxRelayWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusPublisher _publisher;
    private readonly ILogger<OutboxRelayWorker> _logger;

    public OutboxRelayWorker(
        IServiceScopeFactory scopeFactory,
        ServiceBusPublisher publisher,
        ILogger<OutboxRelayWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox relay started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RelayPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // A failed iteration must never kill the loop. Unsent rows simply
                // stay unsent and are retried next tick — nothing is lost.
                _logger.LogError(ex, "Outbox relay iteration failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task RelayPendingAsync(CancellationToken ct)
    {
        // BackgroundService is a singleton; AppDbContext is scoped — so open a scope
        // per iteration rather than capturing one context for the worker's lifetime.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await db.OutboxMessages
            .Where(x => x.SentAtUtc == null)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var row in pending)
        {
            var evt = JsonSerializer.Deserialize<QuoteCreatedEvent>(row.Payload);
            if (evt is null)
            {
                _logger.LogError(
                    "Outbox {Id} has an unparseable payload; skipping", row.Id);
                continue;
            }

            // Publish BEFORE marking sent. The MessageId carried in the payload equals
            // OutboxMessage.Id, so every redelivery uses the same Service Bus MessageId
            // — the exact key the consumer's ProcessedMessages dedupe is built on.
            await _publisher.PublishQuoteCreatedAsync(evt, ct);

            row.SentAtUtc = DateTime.UtcNow;

            // Save per row, right after each publish, so a crash mid-batch only
            // republishes rows not yet marked — keeping the duplicate window to at
            // most one message instead of the whole batch.
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Outbox {Id} published (MessageId={MessageId})",
                row.Id, evt.MessageId);
        }
    }
}
